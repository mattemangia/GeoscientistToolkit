// GeoscientistToolkit/UI/Visualization/GeothermalVisualization3D.cs

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using Veldrid.SPIRV;

namespace GeoscientistToolkit.UI.Visualization;

/// <summary>
///     3D visualization system for geothermal simulation results with advanced rendering capabilities.
/// </summary>
public class GeothermalVisualization3D : IDisposable
{
    public enum ColorMap
    {
        Turbo,
        Viridis,
        Plasma,
        Inferno,
        Magma,
        Jet,
        Rainbow,
        Thermal,
        BlueRed
    }

    public enum RenderMode
    {
        Temperature,
        Velocity,
        Pressure,
        Streamlines,
        Isosurface,
        Slices,
        Combined
    }

    private readonly ResourceFactory _factory;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly List<uint> _indices = new();
    private readonly List<VertexPositionColorTexture> _vertices = new();
    private float _cameraAzimuth = 45f;
    private float _cameraDistance = 200f;
    private float _cameraElevation = 30f;
    private Vector3 _cameraPosition = new(100, 100, 100);
    private readonly Vector3 _cameraTarget = Vector3.Zero;
    private readonly Vector3 _cameraUp = Vector3.UnitZ;

    private Texture _colorMapTexture;
    private TextureView _colorMapView;
    private ColorMap _currentColorMap = ColorMap.Turbo;
    private Framebuffer _framebuffer;
    private DeviceBuffer _indexBuffer;
    private Pipeline _isosurfacePipeline;
    private float _isoValue = 290f; // Kelvin
    private bool _isPanning;

    private bool _isRotating;
    private Vector2 _lastMousePos;

    private Sampler _linearSampler;
    private GeothermalMesh _mesh;
    private float _opacity = 1.0f;
    private Sampler _pointSampler;
    private Matrix4x4 _projectionMatrix;
    private uint _renderHeight = 600;

    // Visualization settings
    private RenderMode _renderMode = RenderMode.Temperature;

    // Render target for offscreen rendering
    private Texture _renderTarget;
    private TextureView _renderTargetView;

    // Render dimensions
    private uint _renderWidth = 800;

    private ResourceLayout _resourceLayout;
    private ResourceSet _resourceSet;

    // Visualization data
    private GeothermalSimulationResults _results;
    private bool _showBorehole = true;
    private bool _showMesh;
    private bool _showVectors;
    private float _sliceDepth = 0.5f; // Normalized depth
    private Pipeline _slicePipeline;
    private float _streamlineLength = 100f;
    private Pipeline _streamlinePipeline;
    private float _streamlineThickness = 1f;
    private DeviceBuffer _temperatureDataBuffer;
    private float _temperatureMax = 373.15f;
    private float _temperatureMin = 273.15f;

    // Render resources
    private Pipeline _temperaturePipeline;
    private Texture _temperatureTexture3D;
    private TextureView _temperatureView;
    private DeviceBuffer _uniformBuffer;
    private float _vectorScale = 10f;
    private DeviceBuffer _velocityDataBuffer;
    private readonly float _velocityMax = 0.001f;
    private float _velocityMin = 0f;
    private Pipeline _velocityPipeline;
    private Texture _velocityTexture3D;
    private TextureView _velocityView;

    private DeviceBuffer _vertexBuffer;

    // Camera and interaction
    private Matrix4x4 _viewMatrix;

    public GeothermalVisualization3D(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        _factory = graphicsDevice.ResourceFactory;

        InitializeResources();
        InitializeColorMaps();
        UpdateCamera();
    }

    public void Dispose()
    {
        _temperaturePipeline?.Dispose();
        _velocityPipeline?.Dispose();
        _streamlinePipeline?.Dispose();
        _isosurfacePipeline?.Dispose();
        _slicePipeline?.Dispose();

        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _uniformBuffer?.Dispose();
        _temperatureDataBuffer?.Dispose();
        _velocityDataBuffer?.Dispose();

        _resourceLayout?.Dispose();
        _resourceSet?.Dispose();

        _colorMapTexture?.Dispose();
        _temperatureTexture3D?.Dispose();
        _velocityTexture3D?.Dispose();
        _colorMapView?.Dispose();
        _temperatureView?.Dispose();
        _velocityView?.Dispose();

        _linearSampler?.Dispose();
        _pointSampler?.Dispose();

        _renderTarget?.Dispose();
        _renderTargetView?.Dispose();
        _framebuffer?.Dispose();
    }

    private void InitializeResources()
    {
        // Create render target for offscreen rendering
        CreateRenderTarget(_renderWidth, _renderHeight);

        // Create uniform buffer
        _uniformBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<UniformData>(),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        // Create samplers
        _linearSampler = _factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear,
            ComparisonKind.Never,
            0,
            0,
            0,
            0,
            SamplerBorderColor.TransparentBlack));

        _pointSampler = _factory.CreateSampler(new SamplerDescription(
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerFilter.MinPoint_MagPoint_MipPoint,
            ComparisonKind.Never,
            0,
            0,
            0,
            0,
            SamplerBorderColor.TransparentBlack));

        // Create resource layout
        _resourceLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("UniformData", ResourceKind.UniformBuffer,
                ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ColorMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ColorMapSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("TemperatureData", ResourceKind.TextureReadOnly,
                ShaderStages.Fragment),
            new ResourceLayoutElementDescription("VelocityData", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DataSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        ));

        CreatePipelines();
    }

    private void CreateRenderTarget(uint width, uint height)
    {
        _renderTarget?.Dispose();
        _renderTargetView?.Dispose();
        _framebuffer?.Dispose();

        _renderWidth = width;
        _renderHeight = height;

        // Create render target texture
        _renderTarget = _factory.CreateTexture(new TextureDescription(
            width, height, 1, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.RenderTarget | TextureUsage.Sampled,
            TextureType.Texture2D));

        _renderTargetView = _factory.CreateTextureView(_renderTarget);

        // Create depth buffer
        var depthTexture = _factory.CreateTexture(new TextureDescription(
            width, height, 1, 1, 1,
            PixelFormat.D24_UNorm_S8_UInt,
            TextureUsage.DepthStencil,
            TextureType.Texture2D));

        _framebuffer = _factory.CreateFramebuffer(new FramebufferDescription(
            depthTexture,
            _renderTarget));
    }

    private void CreatePipelines()
    {
        // Temperature visualization pipeline
        var temperatureShaders = CreateShaders("Temperature");
        _temperaturePipeline = CreatePipeline(temperatureShaders, BlendStateDescription.SingleAlphaBlend);

        // Velocity visualization pipeline
        var velocityShaders = CreateShaders("Velocity");
        _velocityPipeline = CreatePipeline(velocityShaders, BlendStateDescription.SingleAlphaBlend);

        // Streamline pipeline
        var streamlineShaders = CreateShaders("Streamline");
        _streamlinePipeline = CreatePipeline(streamlineShaders, BlendStateDescription.SingleOverrideBlend,
            PrimitiveTopology.LineList);

        // Isosurface pipeline
        var isosurfaceShaders = CreateShaders("Isosurface");
        _isosurfacePipeline = CreatePipeline(isosurfaceShaders, BlendStateDescription.SingleAlphaBlend);

        // Slice pipeline
        var sliceShaders = CreateShaders("Slice");
        _slicePipeline = CreatePipeline(sliceShaders, BlendStateDescription.SingleAlphaBlend);
    }

    private (Shader vertex, Shader fragment) CreateShaders(string name)
    {
        var vertexCode = GetVertexShaderCode(name);
        var fragmentCode = GetFragmentShaderCode(name);

        var vertexShaderDesc = new ShaderDescription(
            ShaderStages.Vertex,
            Encoding.UTF8.GetBytes(vertexCode),
            "main");

        var fragmentShaderDesc = new ShaderDescription(
            ShaderStages.Fragment,
            Encoding.UTF8.GetBytes(fragmentCode),
            "main");

        var shaders = _factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        return (shaders[0], shaders[1]);
    }

    private Pipeline CreatePipeline((Shader vertex, Shader fragment) shaders,
        BlendStateDescription blendState,
        PrimitiveTopology topology = PrimitiveTopology.TriangleList)
    {
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate,
                VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate,
                VertexElementFormat.Float2),
            new VertexElementDescription("Value", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1)
        );

        var pipelineDescription = new GraphicsPipelineDescription
        {
            BlendState = blendState,
            DepthStencilState = new DepthStencilStateDescription(
                true,
                true,
                ComparisonKind.Less),
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.Back,
                PolygonFillMode.Solid,
                FrontFace.CounterClockwise,
                true,
                false),
            PrimitiveTopology = topology,
            ResourceLayouts = new[] { _resourceLayout },
            ShaderSet = new ShaderSetDescription(
                new[] { vertexLayout },
                new[] { shaders.vertex, shaders.fragment }),
            Outputs = _framebuffer.OutputDescription
        };

        return _factory.CreateGraphicsPipeline(pipelineDescription);
    }

    /// <summary>
    ///     Loads simulation results and prepares visualization data.
    /// </summary>
    public void LoadResults(GeothermalSimulationResults results, GeothermalMesh mesh)
    {
        _results = results;
        _mesh = mesh;

        // Validate data before processing
        if (results?.FinalTemperatureField == null || mesh == null) return;

        GenerateMeshGeometry();
        CreateDataTextures();
        UpdateBuffers();
        UpdateResourceSet();
    }

    /// <summary>
    ///     Generates 3D mesh geometry from simulation mesh.
    /// </summary>
    private void GenerateMeshGeometry()
    {
        _vertices.Clear();
        _indices.Clear();

        if (_mesh == null || _mesh.RadialPoints == 0 || _mesh.AngularPoints == 0 || _mesh.VerticalPoints == 0)
            return;

        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        // Generate vertices
        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
        {
            var r = _mesh.R[i];
            var theta = _mesh.Theta[j];
            var z = _mesh.Z[k];

            // Convert cylindrical to Cartesian
            var x = r * MathF.Cos(theta);
            var y = r * MathF.Sin(theta);
            var position = new Vector3(x, y, z);

            // Get temperature value
            var temp = _results.FinalTemperatureField[i, j, k];
            var normalizedTemp = (temp - _temperatureMin) / (_temperatureMax - _temperatureMin);

            // Calculate normal (pointing outward in cylindrical coords)
            var normal = new Vector3(MathF.Cos(theta), MathF.Sin(theta), 0);
            normal = Vector3.Normalize(normal);

            // Texture coordinates
            var texCoord = new Vector2((float)i / (nr - 1), (float)k / (nz - 1));

            // Color based on temperature
            var color = ColorMapValue(normalizedTemp);

            _vertices.Add(new VertexPositionColorTexture(position, color, normal, texCoord, temp));
        }

        // Generate indices for cylindrical mesh
        for (var i = 0; i < nr - 1; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz - 1; k++)
        {
            var j1 = (j + 1) % nth;

            // Current layer vertices
            var v00 = (uint)(i * nth * nz + j * nz + k);
            var v01 = (uint)(i * nth * nz + j * nz + k + 1);
            var v10 = (uint)(i * nth * nz + j1 * nz + k);
            var v11 = (uint)(i * nth * nz + j1 * nz + k + 1);

            // Next radial layer vertices
            var v20 = (uint)((i + 1) * nth * nz + j * nz + k);
            var v21 = (uint)((i + 1) * nth * nz + j * nz + k + 1);
            var v30 = (uint)((i + 1) * nth * nz + j1 * nz + k);
            var v31 = (uint)((i + 1) * nth * nz + j1 * nz + k + 1);

            // Create quads (as two triangles each)
            // Radial faces
            _indices.AddRange(new[] { v00, v20, v21, v00, v21, v01 });
            _indices.AddRange(new[] { v10, v11, v31, v10, v31, v30 });

            // Angular faces
            _indices.AddRange(new[] { v00, v01, v11, v00, v11, v10 });
            _indices.AddRange(new[] { v20, v30, v31, v20, v31, v21 });

            // Vertical faces
            _indices.AddRange(new[] { v00, v10, v30, v00, v30, v20 });
            _indices.AddRange(new[] { v01, v21, v31, v01, v31, v11 });
        }
    }

    /// <summary>
    ///     Creates 3D textures for temperature and velocity data.
    /// </summary>
    private void CreateDataTextures()
    {
        if (_results?.FinalTemperatureField == null || _mesh == null)
            return;

        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        // Create temperature 3D texture
        _temperatureTexture3D?.Dispose();
        _temperatureTexture3D = _factory.CreateTexture(new TextureDescription(
            (uint)nr, (uint)nth, (uint)nz,
            1, 1,
            PixelFormat.R32_Float,
            TextureUsage.Sampled,
            TextureType.Texture3D));

        // Upload temperature data
        var tempData = new float[nr * nth * nz];
        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
            tempData[i * nth * nz + j * nz + k] = _results.FinalTemperatureField[i, j, k];

        _graphicsDevice.UpdateTexture(_temperatureTexture3D, tempData, 0, 0, 0, (uint)nr, (uint)nth, (uint)nz, 0, 0);

        // Create velocity 3D texture if available
        if (_results.DarcyVelocityField != null)
        {
            _velocityTexture3D?.Dispose();
            _velocityTexture3D = _factory.CreateTexture(new TextureDescription(
                (uint)nr, (uint)nth, (uint)nz,
                1, 1,
                PixelFormat.R32_G32_B32_A32_Float,
                TextureUsage.Sampled,
                TextureType.Texture3D));

            // Upload velocity data
            var velData = new Vector4[nr * nth * nz];
            for (var i = 0; i < nr; i++)
            for (var j = 0; j < nth; j++)
            for (var k = 0; k < nz; k++)
            {
                var idx = i * nth * nz + j * nz + k;
                velData[idx] = new Vector4(
                    _results.DarcyVelocityField[i, j, k, 0],
                    _results.DarcyVelocityField[i, j, k, 1],
                    _results.DarcyVelocityField[i, j, k, 2],
                    0);
            }

            _graphicsDevice.UpdateTexture(_velocityTexture3D, velData, 0, 0, 0, (uint)nr, (uint)nth, (uint)nz, 0, 0);
        }

        // Create texture views
        _temperatureView?.Dispose();
        _temperatureView = _factory.CreateTextureView(_temperatureTexture3D);

        if (_velocityTexture3D != null)
        {
            _velocityView?.Dispose();
            _velocityView = _factory.CreateTextureView(_velocityTexture3D);
        }
    }

    /// <summary>
    ///     Updates GPU buffers with mesh data.
    /// </summary>
    private void UpdateBuffers()
    {
        if (_vertices.Count == 0 || _indices.Count == 0)
            return;

        // Update vertex buffer
        _vertexBuffer?.Dispose();
        _vertexBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)(_vertices.Count * Marshal.SizeOf<VertexPositionColorTexture>()),
            BufferUsage.VertexBuffer));
        _graphicsDevice.UpdateBuffer(_vertexBuffer, 0, _vertices.ToArray());

        // Update index buffer
        _indexBuffer?.Dispose();
        _indexBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)(_indices.Count * sizeof(uint)),
            BufferUsage.IndexBuffer));
        _graphicsDevice.UpdateBuffer(_indexBuffer, 0, _indices.ToArray());
    }

    /// <summary>
    ///     Updates the resource set with current textures and buffers.
    /// </summary>
    private void UpdateResourceSet()
    {
        _resourceSet?.Dispose();

        // Create placeholder textures if needed
        if (_velocityView == null)
        {
            var dummyVelocity = _factory.CreateTexture(new TextureDescription(
                1, 1, 1, 1, 1,
                PixelFormat.R32_G32_B32_A32_Float,
                TextureUsage.Sampled,
                TextureType.Texture3D));
            _velocityView = _factory.CreateTextureView(dummyVelocity);
        }

        _resourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _resourceLayout,
            _uniformBuffer,
            _colorMapView,
            _linearSampler,
            _temperatureView,
            _velocityView,
            _pointSampler
        ));
    }

    /// <summary>
    ///     Renders the visualization to the offscreen framebuffer.
    /// </summary>
    public void Render()
    {
        if (_results == null || _vertexBuffer == null || _indexBuffer == null)
            return;

        var commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
        commandList.Begin();

        // Set render target
        commandList.SetFramebuffer(_framebuffer);
        commandList.ClearColorTarget(0, RgbaFloat.Black);
        commandList.ClearDepthStencil(1.0f);

        // Update uniforms
        UpdateUniforms();

        // Select pipeline based on render mode
        var pipeline = _renderMode switch
        {
            RenderMode.Velocity => _velocityPipeline,
            RenderMode.Streamlines => _streamlinePipeline,
            RenderMode.Isosurface => _isosurfacePipeline,
            RenderMode.Slices => _slicePipeline,
            _ => _temperaturePipeline
        };

        // Render
        commandList.SetPipeline(pipeline);
        commandList.SetVertexBuffer(0, _vertexBuffer);
        commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt32);
        commandList.SetGraphicsResourceSet(0, _resourceSet);

        if (_renderMode == RenderMode.Streamlines)
            RenderStreamlines(commandList);
        else
            commandList.DrawIndexed((uint)_indices.Count);

        // Render additional elements
        if (_showBorehole) RenderBorehole(commandList);

        if (_showVectors) RenderVelocityVectors(commandList);

        commandList.End();
        _graphicsDevice.SubmitCommands(commandList);
        _graphicsDevice.WaitForIdle();

        commandList.Dispose();
    }

    /// <summary>
    ///     Renders velocity streamlines.
    /// </summary>
    private void RenderStreamlines(CommandList commandList)
    {
        if (_results?.Streamlines == null || !_results.Streamlines.Any())
            return;

        var streamlineVertices = new List<VertexPositionColorTexture>();
        var streamlineIndices = new List<uint>();
        uint vertexOffset = 0;

        foreach (var streamline in _results.Streamlines)
        {
            if (streamline.Count < 2)
                continue;

            for (var i = 0; i < streamline.Count; i++)
            {
                var point = streamline[i];
                var t = (float)i / (streamline.Count - 1);
                var color = ColorMapValue(t);

                streamlineVertices.Add(new VertexPositionColorTexture(
                    point,
                    color,
                    Vector3.Zero,
                    new Vector2(t, 0),
                    t
                ));

                if (i > 0)
                {
                    streamlineIndices.Add(vertexOffset + (uint)(i - 1));
                    streamlineIndices.Add(vertexOffset + (uint)i);
                }
            }

            vertexOffset += (uint)streamline.Count;
        }

        if (streamlineVertices.Count > 0)
        {
            var streamlineVB = _factory.CreateBuffer(new BufferDescription(
                (uint)(streamlineVertices.Count * Marshal.SizeOf<VertexPositionColorTexture>()),
                BufferUsage.VertexBuffer));
            _graphicsDevice.UpdateBuffer(streamlineVB, 0, streamlineVertices.ToArray());

            var streamlineIB = _factory.CreateBuffer(new BufferDescription(
                (uint)(streamlineIndices.Count * sizeof(uint)),
                BufferUsage.IndexBuffer));
            _graphicsDevice.UpdateBuffer(streamlineIB, 0, streamlineIndices.ToArray());

            commandList.SetVertexBuffer(0, streamlineVB);
            commandList.SetIndexBuffer(streamlineIB, IndexFormat.UInt32);
            commandList.DrawIndexed((uint)streamlineIndices.Count);

            streamlineVB.Dispose();
            streamlineIB.Dispose();
        }
    }

    /// <summary>
    ///     Renders the borehole geometry.
    /// </summary>
    private void RenderBorehole(CommandList commandList)
    {
        if (_results?.BoreholeMesh == null)
            return;

        // Use the borehole mesh from results
        var boreholeVertices = new List<VertexPositionColorTexture>();
        var boreholeIndices = new List<uint>();

        var vertices = _results.BoreholeMesh.Vertices;
        var normals = _results.BoreholeMesh.Normals;
        var faces = _results.BoreholeMesh.Faces;

        for (var i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            var n = i < normals.Count ? normals[i] : Vector3.UnitZ;
            var color = new Vector4(0.8f, 0.2f, 0.2f, 1.0f); // Red for borehole
            boreholeVertices.Add(new VertexPositionColorTexture(
                v,
                color,
                n,
                Vector2.Zero,
                0
            ));
        }

        // Flatten the faces list into a triangle list
        foreach (var face in faces)
        {
            if (face.Length >= 3)
            {
                boreholeIndices.Add((uint)face[0]);
                boreholeIndices.Add((uint)face[1]);
                boreholeIndices.Add((uint)face[2]);
            }

            // Triangulate if it's a quad
            if (face.Length == 4)
            {
                boreholeIndices.Add((uint)face[0]);
                boreholeIndices.Add((uint)face[2]);
                boreholeIndices.Add((uint)face[3]);
            }
        }

        if (boreholeVertices.Count > 0)
        {
            var boreholeVB = _factory.CreateBuffer(new BufferDescription(
                (uint)(boreholeVertices.Count * Marshal.SizeOf<VertexPositionColorTexture>()),
                BufferUsage.VertexBuffer));
            _graphicsDevice.UpdateBuffer(boreholeVB, 0, boreholeVertices.ToArray());

            var boreholeIB = _factory.CreateBuffer(new BufferDescription(
                (uint)(boreholeIndices.Count * sizeof(uint)),
                BufferUsage.IndexBuffer));
            _graphicsDevice.UpdateBuffer(boreholeIB, 0, boreholeIndices.ToArray());

            commandList.SetVertexBuffer(0, boreholeVB);
            commandList.SetIndexBuffer(boreholeIB, IndexFormat.UInt32);
            commandList.DrawIndexed((uint)boreholeIndices.Count);

            boreholeVB.Dispose();
            boreholeIB.Dispose();
        }
    }

    /// <summary>
    ///     Renders velocity vectors as arrows.
    /// </summary>
    private void RenderVelocityVectors(CommandList commandList)
    {
        if (_results?.DarcyVelocityField == null)
            return;

        var vectorVertices = new List<VertexPositionColorTexture>();
        var vectorIndices = new List<uint>();

        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        uint vertexOffset = 0;
        var skipR = Math.Max(1, nr / 10);
        var skipTheta = Math.Max(1, nth / 20);
        var skipZ = Math.Max(1, nz / 10);

        for (var i = 0; i < nr; i += skipR)
        for (var j = 0; j < nth; j += skipTheta)
        for (var k = 0; k < nz; k += skipZ)
        {
            var r = _mesh.R[i];
            var theta = _mesh.Theta[j];
            var z = _mesh.Z[k];

            var x = r * MathF.Cos(theta);
            var y = r * MathF.Sin(theta);
            var start = new Vector3(x, y, z);

            var vel = new Vector3(
                _results.DarcyVelocityField[i, j, k, 0],
                _results.DarcyVelocityField[i, j, k, 1],
                _results.DarcyVelocityField[i, j, k, 2]
            );

            var magnitude = vel.Length();
            if (magnitude < 1e-10f)
                continue;

            var end = start + vel * _vectorScale;
            var t = magnitude / _velocityMax;
            var color = ColorMapValue(t);

            vectorVertices.Add(new VertexPositionColorTexture(start, color, Vector3.Zero, Vector2.Zero, magnitude));
            vectorVertices.Add(new VertexPositionColorTexture(end, color, Vector3.Zero, Vector2.One, magnitude));

            vectorIndices.Add(vertexOffset);
            vectorIndices.Add(vertexOffset + 1);

            vertexOffset += 2;
        }

        if (vectorVertices.Count > 0)
        {
            var vectorVB = _factory.CreateBuffer(new BufferDescription(
                (uint)(vectorVertices.Count * Marshal.SizeOf<VertexPositionColorTexture>()),
                BufferUsage.VertexBuffer));
            _graphicsDevice.UpdateBuffer(vectorVB, 0, vectorVertices.ToArray());

            var vectorIB = _factory.CreateBuffer(new BufferDescription(
                (uint)(vectorIndices.Count * sizeof(uint)),
                BufferUsage.IndexBuffer));
            _graphicsDevice.UpdateBuffer(vectorIB, 0, vectorIndices.ToArray());

            commandList.SetPipeline(_streamlinePipeline);
            commandList.SetVertexBuffer(0, vectorVB);
            commandList.SetIndexBuffer(vectorIB, IndexFormat.UInt32);
            commandList.DrawIndexed((uint)vectorIndices.Count);

            vectorVB.Dispose();
            vectorIB.Dispose();
        }
    }

    /// <summary>
    ///     Updates uniform buffer with current transformation matrices and settings.
    /// </summary>
    private void UpdateUniforms()
    {
        var uniforms = new UniformData
        {
            ViewMatrix = _viewMatrix,
            ProjectionMatrix = _projectionMatrix,
            ModelMatrix = Matrix4x4.Identity,
            LightDirection = Vector4.Normalize(new Vector4(-1, -1, -2, 0)),
            ViewPosition = new Vector4(_cameraPosition, 1),
            ColorMapRange = new Vector4(_temperatureMin, _temperatureMax, _velocityMax, 0),
            SliceInfo = new Vector4(_sliceDepth, 0, 0, 1), // Z-slice by default
            RenderSettings = new Vector4((float)_renderMode, _opacity, 0, _isoValue)
        };

        _graphicsDevice.UpdateBuffer(_uniformBuffer, 0, ref uniforms);
    }

    /// <summary>
    ///     Updates camera matrices based on current position and orientation.
    /// </summary>
    private void UpdateCamera()
    {
        // Convert spherical to Cartesian for camera position
        var azimRad = _cameraAzimuth * MathF.PI / 180f;
        var elevRad = _cameraElevation * MathF.PI / 180f;

        _cameraPosition = new Vector3(
            _cameraDistance * MathF.Cos(elevRad) * MathF.Cos(azimRad),
            _cameraDistance * MathF.Cos(elevRad) * MathF.Sin(azimRad),
            _cameraDistance * MathF.Sin(elevRad)
        );

        _viewMatrix = Matrix4x4.CreateLookAt(_cameraPosition, _cameraTarget, _cameraUp);
        _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            45f * MathF.PI / 180f,
            (float)_renderWidth / _renderHeight,
            0.1f,
            1000f
        );
    }

    /// <summary>
    ///     Renders the visualization controls using ImGui.
    /// </summary>
    public void RenderControls()
    {
        ImGui.Text("3D Visualization Controls");
        ImGui.Separator();

        // Render mode selection
        if (ImGui.BeginCombo("Render Mode", _renderMode.ToString()))
        {
            foreach (var mode in Enum.GetValues<RenderMode>())
                if (ImGui.Selectable(mode.ToString(), _renderMode == mode))
                    _renderMode = mode;

            ImGui.EndCombo();
        }

        // Color map selection
        if (ImGui.BeginCombo("Color Map", _currentColorMap.ToString()))
        {
            foreach (var map in Enum.GetValues<ColorMap>())
                if (ImGui.Selectable(map.ToString(), _currentColorMap == map))
                {
                    _currentColorMap = map;
                    InitializeColorMaps();
                }

            ImGui.EndCombo();
        }

        // Temperature range
        ImGui.DragFloatRange2("Temperature Range (°C)",
            ref _temperatureMin, ref _temperatureMax,
            0.1f, 0f, 200f);
        _temperatureMin += 273.15f; // Convert to Kelvin
        _temperatureMax += 273.15f;

        // Visualization options
        ImGui.Checkbox("Show Borehole", ref _showBorehole);
        ImGui.SameLine();
        ImGui.Checkbox("Show Mesh", ref _showMesh);
        ImGui.SameLine();
        ImGui.Checkbox("Show Vectors", ref _showVectors);

        ImGui.SliderFloat("Opacity", ref _opacity, 0f, 1f);

        if (_renderMode == RenderMode.Isosurface)
        {
            ImGui.SliderFloat("Iso Value (°C)", ref _isoValue, 0f, 100f);
            _isoValue += 273.15f; // Convert to Kelvin
        }

        if (_renderMode == RenderMode.Slices) ImGui.SliderFloat("Slice Depth", ref _sliceDepth, 0f, 1f);

        if (_showVectors) ImGui.SliderFloat("Vector Scale", ref _vectorScale, 1f, 100f);

        // Camera controls
        ImGui.Separator();
        ImGui.Text("Camera");
        ImGui.SliderFloat("Distance", ref _cameraDistance, 10f, 500f);
        ImGui.SliderFloat("Azimuth", ref _cameraAzimuth, 0f, 360f);
        ImGui.SliderFloat("Elevation", ref _cameraElevation, -90f, 90f);

        if (ImGui.Button("Reset Camera"))
        {
            _cameraDistance = 200f;
            _cameraAzimuth = 45f;
            _cameraElevation = 30f;
        }

        UpdateCamera();
    }

    /// <summary>
    ///     Handles mouse input for camera control.
    /// </summary>
    public void HandleMouseInput(Vector2 mousePos, bool leftButton, bool rightButton)
    {
        if (leftButton && !_isRotating)
        {
            _isRotating = true;
            _lastMousePos = mousePos;
        }
        else if (!leftButton)
        {
            _isRotating = false;
        }

        if (rightButton && !_isPanning)
        {
            _isPanning = true;
            _lastMousePos = mousePos;
        }
        else if (!rightButton)
        {
            _isPanning = false;
        }

        if (_isRotating)
        {
            var delta = mousePos - _lastMousePos;
            _cameraAzimuth += delta.X * 0.5f;
            _cameraElevation = Math.Clamp(_cameraElevation - delta.Y * 0.5f, -89f, 89f);
            _lastMousePos = mousePos;
            UpdateCamera();
        }

        if (_isPanning)
        {
            var delta = mousePos - _lastMousePos;
            // Implement panning logic here if needed
            _lastMousePos = mousePos;
        }
    }

    /// <summary>
    ///     Handles mouse wheel for zooming.
    /// </summary>
    public void HandleMouseWheel(float delta)
    {
        _cameraDistance = Math.Clamp(_cameraDistance - delta * 10f, 10f, 500f);
        UpdateCamera();
    }

    /// <summary>
    ///     Gets the current render target as an ImGui texture ID.
    /// </summary>
    public IntPtr GetRenderTargetImGuiBinding()
    {
        return VeldridManager.ImGuiController.GetOrCreateImGuiBinding(_factory, _renderTargetView);
    }

    /// <summary>
    ///     Resizes the render target if needed.
    /// </summary>
    public void Resize(uint width, uint height)
    {
        if (width > 0 && height > 0 && (width != _renderWidth || height != _renderHeight))
        {
            CreateRenderTarget(width, height);
            // Recreate pipelines with new framebuffer
            CreatePipelines();
            UpdateCamera();
        }
    }

    /// <summary>
    ///     Initializes color map textures.
    /// </summary>
    private void InitializeColorMaps()
    {
        _colorMapTexture?.Dispose();
        _colorMapView?.Dispose();

        var colorMapData = GenerateColorMapData(_currentColorMap);

        _colorMapTexture = _factory.CreateTexture(new TextureDescription(
            256, 1, 1, 1, 1,
            PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.Sampled,
            TextureType.Texture1D
        ));

        _graphicsDevice.UpdateTexture(_colorMapTexture, colorMapData, 0, 0, 0, 256, 1, 1, 0, 0);
        _colorMapView = _factory.CreateTextureView(_colorMapTexture);
    }

    /// <summary>
    ///     Generates color map data for the specified map type.
    /// </summary>
    private byte[] GenerateColorMapData(ColorMap map)
    {
        var data = new byte[256 * 4]; // RGBA

        for (var i = 0; i < 256; i++)
        {
            var t = i / 255f;
            var color = GetColorMapColor(map, t);

            data[i * 4 + 0] = (byte)(color.X * 255);
            data[i * 4 + 1] = (byte)(color.Y * 255);
            data[i * 4 + 2] = (byte)(color.Z * 255);
            data[i * 4 + 3] = 255; // Alpha
        }

        return data;
    }

    /// <summary>
    ///     Gets color from a specific color map at parameter t.
    /// </summary>
    private Vector3 GetColorMapColor(ColorMap map, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        return map switch
        {
            ColorMap.Turbo => TurboColorMap(t),
            ColorMap.Viridis => ViridisColorMap(t),
            ColorMap.Plasma => PlasmaColorMap(t),
            ColorMap.Jet => JetColorMap(t),
            ColorMap.Thermal => new Vector3(t, t * (1 - t) * 4, 1 - t),
            ColorMap.BlueRed => new Vector3(t, 0, 1 - t),
            _ => new Vector3(t, t, t) // Grayscale fallback
        };
    }

    private Vector3 TurboColorMap(float t)
    {
        // Simplified Turbo colormap
        if (t < 0.25f)
            return Vector3.Lerp(new Vector3(0.2f, 0.1f, 0.5f), new Vector3(0.1f, 0.5f, 0.8f), t * 4);
        if (t < 0.5f)
            return Vector3.Lerp(new Vector3(0.1f, 0.5f, 0.8f), new Vector3(0.2f, 0.8f, 0.2f), (t - 0.25f) * 4);
        if (t < 0.75f)
            return Vector3.Lerp(new Vector3(0.2f, 0.8f, 0.2f), new Vector3(0.9f, 0.7f, 0.1f), (t - 0.5f) * 4);
        return Vector3.Lerp(new Vector3(0.9f, 0.7f, 0.1f), new Vector3(0.9f, 0.1f, 0.1f), (t - 0.75f) * 4);
    }

    private Vector3 ViridisColorMap(float t)
    {
        // Simplified Viridis colormap
        var r = 0.267f + 0.004780f * t + 0.329f * t * t - 0.698f * t * t * t;
        var g = 0.004f + 1.384f * t - 0.394f * t * t;
        var b = 0.329f + 1.267f * t - 2.572f * t * t + 1.977f * t * t * t;
        return new Vector3(r, g, b);
    }

    private Vector3 PlasmaColorMap(float t)
    {
        // Simplified Plasma colormap
        if (t < 0.5f)
            return Vector3.Lerp(new Vector3(0.1f, 0.0f, 0.5f), new Vector3(0.9f, 0.1f, 0.5f), t * 2);
        return Vector3.Lerp(new Vector3(0.9f, 0.1f, 0.5f), new Vector3(0.9f, 0.9f, 0.1f), (t - 0.5f) * 2);
    }

    private Vector3 JetColorMap(float t)
    {
        // Classic Jet colormap
        if (t < 0.125f)
            return new Vector3(0, 0, 0.5f + t * 4);
        if (t < 0.375f)
            return new Vector3(0, (t - 0.125f) * 4, 1);
        if (t < 0.625f)
            return new Vector3((t - 0.375f) * 4, 1, 1 - (t - 0.375f) * 4);
        if (t < 0.875f)
            return new Vector3(1, 1 - (t - 0.625f) * 4, 0);
        return new Vector3(1 - (t - 0.875f) * 4, 0, 0);
    }

    /// <summary>
    ///     Gets color value from current color map.
    /// </summary>
    private Vector4 ColorMapValue(float t)
    {
        var rgb = GetColorMapColor(_currentColorMap, t);
        return new Vector4(rgb, 1f);
    }

    // Shader code getters
    private string GetVertexShaderCode(string shaderName)
    {
        return shaderName switch
        {
            "Temperature" => GetTemperatureVertexShader(),
            "Velocity" => GetVelocityVertexShader(),
            "Streamline" => GetStreamlineVertexShader(),
            "Isosurface" => GetIsosurfaceVertexShader(),
            "Slice" => GetSliceVertexShader(),
            _ => GetBasicVertexShader()
        };
    }

    private string GetFragmentShaderCode(string shaderName)
    {
        return shaderName switch
        {
            "Temperature" => GetTemperatureFragmentShader(),
            "Velocity" => GetVelocityFragmentShader(),
            "Streamline" => GetStreamlineFragmentShader(),
            "Isosurface" => GetIsosurfaceFragmentShader(),
            "Slice" => GetSliceFragmentShader(),
            _ => GetBasicFragmentShader()
        };
    }

    private string GetBasicVertexShader()
    {
        return @"
#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;
layout(location = 2) in vec3 Normal;
layout(location = 3) in vec2 TexCoord;
layout(location = 4) in float Value;

layout(location = 0) out vec4 frag_Color;
layout(location = 1) out vec3 frag_Normal;
layout(location = 2) out vec2 frag_TexCoord;
layout(location = 3) out float frag_Value;
layout(location = 4) out vec3 frag_WorldPos;

layout(set = 0, binding = 0) uniform UniformData {
    mat4 ViewMatrix;
    mat4 ProjectionMatrix;
    mat4 ModelMatrix;
    vec4 LightDirection;
    vec4 ViewPosition;
    vec4 ColorMapRange;
    vec4 SliceInfo;
    vec4 RenderSettings;
} ubo;

void main() {
    vec4 worldPos = ubo.ModelMatrix * vec4(Position, 1.0);
    gl_Position = ubo.ProjectionMatrix * ubo.ViewMatrix * worldPos;
    
    frag_Color = Color;
    frag_Normal = mat3(ubo.ModelMatrix) * Normal;
    frag_TexCoord = TexCoord;
    frag_Value = Value;
    frag_WorldPos = worldPos.xyz;
}";
    }

    private string GetBasicFragmentShader()
    {
        return @"
#version 450

layout(location = 0) in vec4 frag_Color;
layout(location = 1) in vec3 frag_Normal;
layout(location = 2) in vec2 frag_TexCoord;
layout(location = 3) in float frag_Value;
layout(location = 4) in vec3 frag_WorldPos;

layout(location = 0) out vec4 FragColor;

layout(set = 0, binding = 0) uniform UniformData {
    mat4 ViewMatrix;
    mat4 ProjectionMatrix;
    mat4 ModelMatrix;
    vec4 LightDirection;
    vec4 ViewPosition;
    vec4 ColorMapRange;
    vec4 SliceInfo;
    vec4 RenderSettings;
} ubo;

layout(set = 0, binding = 1) uniform texture1D ColorMap;
layout(set = 0, binding = 2) uniform sampler ColorMapSampler;

void main() {
    float t = (frag_Value - ubo.ColorMapRange.x) / (ubo.ColorMapRange.y - ubo.ColorMapRange.x);
    vec4 color = texture(sampler1D(ColorMap, ColorMapSampler), t);
    
    vec3 normal = normalize(frag_Normal);
    float diffuse = max(dot(normal, ubo.LightDirection.xyz), 0.3);
    
    FragColor = vec4(color.rgb * diffuse, color.a * ubo.RenderSettings.y);
}";
    }

    private string GetTemperatureVertexShader()
    {
        return GetBasicVertexShader();
    }

    private string GetTemperatureFragmentShader()
    {
        return @"
#version 450

layout(location = 0) in vec4 frag_Color;
layout(location = 1) in vec3 frag_Normal;
layout(location = 2) in vec2 frag_TexCoord;
layout(location = 3) in float frag_Value;
layout(location = 4) in vec3 frag_WorldPos;

layout(location = 0) out vec4 FragColor;

layout(set = 0, binding = 0) uniform UniformData {
    mat4 ViewMatrix;
    mat4 ProjectionMatrix;
    mat4 ModelMatrix;
    vec4 LightDirection;
    vec4 ViewPosition;
    vec4 ColorMapRange;
    vec4 SliceInfo;
    vec4 RenderSettings;
} ubo;

layout(set = 0, binding = 1) uniform texture1D ColorMap;
layout(set = 0, binding = 2) uniform sampler ColorMapSampler;
layout(set = 0, binding = 3) uniform texture3D TemperatureData;
layout(set = 0, binding = 5) uniform sampler DataSampler;

void main() {
    // Sample temperature from 3D texture
    vec3 texCoord3D = vec3(frag_TexCoord, frag_WorldPos.z);
    float temperature = texture(sampler3D(TemperatureData, DataSampler), texCoord3D).r;
    
    float t = clamp((temperature - ubo.ColorMapRange.x) / (ubo.ColorMapRange.y - ubo.ColorMapRange.x), 0.0, 1.0);
    vec4 color = texture(sampler1D(ColorMap, ColorMapSampler), t);
    
    vec3 normal = normalize(frag_Normal);
    float diffuse = max(dot(normal, ubo.LightDirection.xyz), 0.3);
    
    // Add specular highlight
    vec3 viewDir = normalize(ubo.ViewPosition.xyz - frag_WorldPos);
    vec3 reflectDir = reflect(-ubo.LightDirection.xyz, normal);
    float specular = pow(max(dot(viewDir, reflectDir), 0.0), 32);
    
    FragColor = vec4(color.rgb * diffuse + vec3(0.2) * specular, color.a * ubo.RenderSettings.y);
}";
    }

    private string GetVelocityVertexShader()
    {
        return GetBasicVertexShader();
    }

    private string GetVelocityFragmentShader()
    {
        return @"
#version 450

layout(location = 0) in vec4 frag_Color;
layout(location = 1) in vec3 frag_Normal;
layout(location = 2) in vec2 frag_TexCoord;
layout(location = 3) in float frag_Value;
layout(location = 4) in vec3 frag_WorldPos;

layout(location = 0) out vec4 FragColor;

layout(set = 0, binding = 0) uniform UniformData {
    mat4 ViewMatrix;
    mat4 ProjectionMatrix;
    mat4 ModelMatrix;
    vec4 LightDirection;
    vec4 ViewPosition;
    vec4 ColorMapRange;
    vec4 SliceInfo;
    vec4 RenderSettings;
} ubo;

layout(set = 0, binding = 1) uniform texture1D ColorMap;
layout(set = 0, binding = 2) uniform sampler ColorMapSampler;
layout(set = 0, binding = 4) uniform texture3D VelocityData;
layout(set = 0, binding = 5) uniform sampler DataSampler;

void main() {
    vec3 texCoord3D = vec3(frag_TexCoord, frag_WorldPos.z);
    vec3 velocity = texture(sampler3D(VelocityData, DataSampler), texCoord3D).xyz;
    float magnitude = length(velocity);
    
    float t = clamp(magnitude / ubo.ColorMapRange.z, 0.0, 1.0);
    vec4 color = texture(sampler1D(ColorMap, ColorMapSampler), t);
    
    vec3 normal = normalize(frag_Normal);
    float diffuse = max(dot(normal, ubo.LightDirection.xyz), 0.3);
    
    FragColor = vec4(color.rgb * diffuse, color.a * ubo.RenderSettings.y);
}";
    }

    private string GetStreamlineVertexShader()
    {
        return GetBasicVertexShader();
    }

    private string GetStreamlineFragmentShader()
    {
        return GetBasicFragmentShader();
    }

    private string GetIsosurfaceVertexShader()
    {
        return GetBasicVertexShader();
    }

    private string GetIsosurfaceFragmentShader()
    {
        return @"
#version 450

layout(location = 0) in vec4 frag_Color;
layout(location = 1) in vec3 frag_Normal;
layout(location = 2) in vec2 frag_TexCoord;
layout(location = 3) in float frag_Value;
layout(location = 4) in vec3 frag_WorldPos;

layout(location = 0) out vec4 FragColor;

layout(set = 0, binding = 0) uniform UniformData {
    mat4 ViewMatrix;
    mat4 ProjectionMatrix;
    mat4 ModelMatrix;
    vec4 LightDirection;
    vec4 ViewPosition;
    vec4 ColorMapRange;
    vec4 SliceInfo;
    vec4 RenderSettings;
} ubo;

void main() {
    // Only render if near iso value
    if (abs(frag_Value - ubo.RenderSettings.w) > 1.0) {
        discard;
    }
    
    vec3 normal = normalize(frag_Normal);
    float diffuse = max(dot(normal, ubo.LightDirection.xyz), 0.3);
    
    vec3 viewDir = normalize(ubo.ViewPosition.xyz - frag_WorldPos);
    vec3 reflectDir = reflect(-ubo.LightDirection.xyz, normal);
    float specular = pow(max(dot(viewDir, reflectDir), 0.0), 64);
    
    vec3 color = frag_Color.rgb * diffuse + vec3(0.3) * specular;
    FragColor = vec4(color, ubo.RenderSettings.y);
}";
    }

    private string GetSliceVertexShader()
    {
        return GetBasicVertexShader();
    }

    private string GetSliceFragmentShader()
    {
        return @"
#version 450

layout(location = 0) in vec4 frag_Color;
layout(location = 1) in vec3 frag_Normal;
layout(location = 2) in vec2 frag_TexCoord;
layout(location = 3) in float frag_Value;
layout(location = 4) in vec3 frag_WorldPos;

layout(location = 0) out vec4 FragColor;

layout(set = 0, binding = 0) uniform UniformData {
    mat4 ViewMatrix;
    mat4 ProjectionMatrix;
    mat4 ModelMatrix;
    vec4 LightDirection;
    vec4 ViewPosition;
    vec4 ColorMapRange;
    vec4 SliceInfo;
    vec4 RenderSettings;
} ubo;

layout(set = 0, binding = 1) uniform texture1D ColorMap;
layout(set = 0, binding = 2) uniform sampler ColorMapSampler;

void main() {
    // Calculate distance from slice plane
    vec3 sliceNormal = ubo.SliceInfo.yzw;
    float sliceDepth = ubo.SliceInfo.x;
    float dist = dot(frag_WorldPos - sliceNormal * sliceDepth, sliceNormal);
    
    if (abs(dist) > 0.5) {
        discard;
    }
    
    float t = (frag_Value - ubo.ColorMapRange.x) / (ubo.ColorMapRange.y - ubo.ColorMapRange.x);
    vec4 color = texture(sampler1D(ColorMap, ColorMapSampler), t);
    
    FragColor = vec4(color.rgb, ubo.RenderSettings.y);
}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniformData
    {
        public Matrix4x4 ViewMatrix;
        public Matrix4x4 ProjectionMatrix;
        public Matrix4x4 ModelMatrix;
        public Vector4 LightDirection;
        public Vector4 ViewPosition;
        public Vector4 ColorMapRange; // min, max, 0, 0
        public Vector4 SliceInfo; // depth, normal.x, normal.y, normal.z
        public Vector4 RenderSettings; // mode, opacity, time, isoValue
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VertexPositionColorTexture
    {
        public Vector3 Position;
        public Vector4 Color;
        public Vector3 Normal;
        public Vector2 TexCoord;
        public float Value;

        public VertexPositionColorTexture(Vector3 position, Vector4 color, Vector3 normal, Vector2 texCoord,
            float value)
        {
            Position = position;
            Color = color;
            Normal = normal;
            TexCoord = texCoord;
            Value = value;
        }

        public static uint SizeInBytes => (uint)Marshal.SizeOf<VertexPositionColorTexture>();
    }
}