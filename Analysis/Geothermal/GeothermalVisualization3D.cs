// GeoscientistToolkit/UI/Visualization/GeothermalVisualization3D.cs

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Data.Mesh3D;
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
        Slices
    }

    private readonly Vector3 _cameraUp = Vector3.UnitZ;
    private readonly List<GpuMesh> _dynamicGpuMeshes = new();

    private readonly ResourceFactory _factory;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly float _velocityMax = 0.001f;

    // GPU resources for meshes
    private GpuMesh _boreholeGpuMesh;
    private float _cameraAzimuth = 45f;
    private float _cameraDistance = 200f;
    private float _cameraElevation = 30f;
    private Vector3 _cameraPosition = new(100, 100, 100);
    private Vector3 _cameraTarget = Vector3.Zero;

    private Texture _colorMapTexture;
    private TextureView _colorMapView;
    private ColorMap _currentColorMap = ColorMap.Turbo;
    private GpuMesh _domainGpuMesh;
    private Framebuffer _framebuffer;
    private Pipeline _isosurfacePipeline;
    private float _isoValue = 20f; // Celsius

    private bool _isPanning;
    private bool _isRotating;
    private Vector2 _lastMousePos;

    private Sampler _linearSampler;
    private GeothermalMesh _mesh;
    private float _opacity = 1.0f;
    private GeothermalSimulationOptions _options; // CORRECTED: Added field to store options
    private Sampler _pointSampler;
    private Matrix4x4 _projectionMatrix;
    private uint _renderHeight = 600;
    private RenderMode _renderMode = RenderMode.Temperature;
    private Texture _renderTarget;
    private TextureView _renderTargetView;
    private uint _renderWidth = 800;
    private ResourceLayout _resourceLayout;
    private ResourceSet _resourceSet;
    private GeothermalSimulationResults _results;
    private bool _showBorehole = true;
    private bool _showDomainMesh = true;
    private bool _showVectors;
    private float _sliceDepth = 0.5f; // Normalized depth
    private Pipeline _slicePipeline;
    private GpuMesh _sliceQuad;
    private Pipeline _streamlinePipeline;
    private float _temperatureMax = 100f; // Celsius
    private float _temperatureMin; // Celsius

    // CORRECTED: Added all pipeline fields
    private Pipeline _temperaturePipeline;
    private Texture _temperatureTexture3D;
    private TextureView _temperatureView;
    private DeviceBuffer _uniformBuffer;
    private float _vectorScale = 10f;
    private Pipeline _velocityPipeline;
    private Texture _velocityTexture3D;
    private TextureView _velocityView;
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

        _uniformBuffer?.Dispose();

        _domainGpuMesh.Dispose();
        _boreholeGpuMesh.Dispose();
        foreach (var mesh in _dynamicGpuMeshes) mesh.Dispose();
        _dynamicGpuMeshes.Clear();
        _sliceQuad.Dispose();

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
        CreateRenderTarget(_renderWidth, _renderHeight);

        _uniformBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<UniformData>(),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        _linearSampler = _factory.CreateSampler(SamplerDescription.Linear);
        _pointSampler = _factory.CreateSampler(SamplerDescription.Point);

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

        _renderTarget = _factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));

        _renderTargetView = _factory.CreateTextureView(_renderTarget);

        // Use backend-compatible depth format
        // Metal and Vulkan require D32_Float_S8_UInt, D3D11 can use D24_UNorm_S8_UInt
        var depthFormat = _graphicsDevice.BackendType == GraphicsBackend.Direct3D11
            ? PixelFormat.D24_UNorm_S8_UInt
            : PixelFormat.D32_Float_S8_UInt;

        var depthTexture = _factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, depthFormat, TextureUsage.DepthStencil));

        _framebuffer = _factory.CreateFramebuffer(new FramebufferDescription(depthTexture, _renderTarget));
    }

    private void CreatePipelines()
    {
        var temperatureShaders = CreateShaders("Temperature");
        _temperaturePipeline = CreatePipeline(temperatureShaders, BlendStateDescription.SingleAlphaBlend);

        var velocityShaders = CreateShaders("Velocity");
        _velocityPipeline = CreatePipeline(velocityShaders, BlendStateDescription.SingleAlphaBlend);

        var streamlineShaders = CreateShaders("Streamline");
        _streamlinePipeline = CreatePipeline(streamlineShaders, BlendStateDescription.SingleAlphaBlend,
            PrimitiveTopology.LineList);

        var isosurfaceShaders = CreateShaders("Isosurface");
        _isosurfacePipeline = CreatePipeline(isosurfaceShaders, BlendStateDescription.SingleAlphaBlend);

        var sliceShaders = CreateShaders("Slice");
        _slicePipeline = CreatePipeline(sliceShaders, BlendStateDescription.SingleAlphaBlend);
    }

    private (Shader vertex, Shader fragment) CreateShaders(string name)
    {
        var vertexCode = GetVertexShaderCode(name);
        var fragmentCode = GetFragmentShaderCode(name);

        var vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexCode), "main");
        var fragmentShaderDesc =
            new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentCode), "main");

        var shaders = _factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        return (shaders[0], shaders[1]);
    }

    private Pipeline CreatePipeline((Shader vertex, Shader fragment) shaders, BlendStateDescription blendState,
        PrimitiveTopology topology = PrimitiveTopology.TriangleList)
    {
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate,
                VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate,
                VertexElementFormat.Float2),
            new VertexElementDescription("Value", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1),
            new VertexElementDescription("UVW", VertexElementSemantic.TextureCoordinate,
                VertexElementFormat.Float3) // For 3D texture sampling
        );

        var pipelineDescription = new GraphicsPipelineDescription
        {
            BlendState = blendState,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid,
                FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = topology,
            ResourceLayouts = new[] { _resourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { shaders.vertex, shaders.fragment }),
            Outputs = _framebuffer.OutputDescription
        };

        return _factory.CreateGraphicsPipeline(pipelineDescription);
    }

    public void LoadResults(GeothermalSimulationResults results, GeothermalMesh mesh,
        GeothermalSimulationOptions options)
    {
        _results = results;
        _mesh = mesh;
        _options = options; // CORRECTED: Store options

        if (results?.FinalTemperatureField == null || mesh == null) return;

        GenerateDomainAndBoreholeGpuMeshes();
        CreateDataTextures();
        CreateSliceQuad();
        UpdateResourceSet();
    }

    private GpuMesh CreateGpuMesh(Mesh3DDataset mesh, bool isStreamline = false)
    {
        if (mesh.Vertices.Count == 0) return default;

        var vertices = new List<VertexData>();
        for (var i = 0; i < mesh.Vertices.Count; i++)
            vertices.Add(new VertexData(
                mesh.Vertices[i],
                new Vector4(0.8f, 0.8f, 0.8f, 1.0f), // Default color
                i < mesh.Normals.Count ? mesh.Normals[i] : Vector3.UnitZ,
                Vector2.Zero,
                0f,
                Vector3.Zero
            ));

        var indices = new List<uint>();
        if (isStreamline) // For streamlines, faces are pairs of indices for lines
            foreach (var face in mesh.Faces)
            {
                if (face.Length < 2) continue;
                indices.Add((uint)face[0]);
                indices.Add((uint)face[1]);
            }
        else // For regular meshes, triangulate
            foreach (var face in mesh.Faces)
            {
                if (face.Length < 3) continue;
                indices.Add((uint)face[0]);
                indices.Add((uint)face[1]);
                indices.Add((uint)face[2]);
                if (face.Length == 4)
                {
                    indices.Add((uint)face[0]);
                    indices.Add((uint)face[2]);
                    indices.Add((uint)face[3]);
                }
            }

        if (vertices.Count == 0 || indices.Count == 0) return default;

        var vb = _factory.CreateBuffer(new BufferDescription((uint)(vertices.Count * Marshal.SizeOf<VertexData>()),
            BufferUsage.VertexBuffer));
        _graphicsDevice.UpdateBuffer(vb, 0, vertices.ToArray());

        var ib = _factory.CreateBuffer(new BufferDescription((uint)(indices.Count * sizeof(uint)),
            BufferUsage.IndexBuffer));
        _graphicsDevice.UpdateBuffer(ib, 0, indices.ToArray());

        return new GpuMesh { VertexBuffer = vb, IndexBuffer = ib, IndexCount = (uint)indices.Count, Source = mesh };
    }

    private void GenerateDomainAndBoreholeGpuMeshes()
    {
        if (_mesh == null || _mesh.RadialPoints == 0) return;

        var vertices = new List<VertexData>();
        var indices = new List<uint>();
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        for (var k = 0; k < nz; k++)
        for (var j = 0; j < nth; j++)
        for (var i = 0; i < nr; i++)
        {
            var r = _mesh.R[i];
            var theta = _mesh.Theta[j];
            var z = _mesh.Z[k];
            var position = new Vector3(r * MathF.Cos(theta), r * MathF.Sin(theta), z);
            var normal = new Vector3(MathF.Cos(theta), MathF.Sin(theta), 0);
            var uvw = new Vector3((float)i / (nr - 1), (float)j / (nth - 1), (float)k / (nz - 1));

            vertices.Add(new VertexData(position, Vector4.One, normal, Vector2.Zero, 0, uvw));
        }

        // Generate indices for the outer shell of the cylinder
        var i_outer = nr - 1;
        for (var k = 0; k < nz - 1; k++)
        for (var j = 0; j < nth; j++)
        {
            var j_next = (j + 1) % nth;
            var v0 = (uint)(k * nth * nr + j * nr + i_outer);
            var v1 = (uint)(k * nth * nr + j_next * nr + i_outer);
            var v2 = (uint)((k + 1) * nth * nr + j * nr + i_outer);
            var v3 = (uint)((k + 1) * nth * nr + j_next * nr + i_outer);
            indices.AddRange(new[] { v0, v1, v2, v1, v3, v2 });
        }

        _domainGpuMesh.Dispose();
        var vb = _factory.CreateBuffer(new BufferDescription((uint)(vertices.Count * Marshal.SizeOf<VertexData>()),
            BufferUsage.VertexBuffer));
        _graphicsDevice.UpdateBuffer(vb, 0, vertices.ToArray());

        var ib = _factory.CreateBuffer(new BufferDescription((uint)(indices.Count * sizeof(uint)),
            BufferUsage.IndexBuffer));
        _graphicsDevice.UpdateBuffer(ib, 0, indices.ToArray());
        _domainGpuMesh = new GpuMesh { VertexBuffer = vb, IndexBuffer = ib, IndexCount = (uint)indices.Count };

        if (_results.BoreholeMesh != null)
        {
            _boreholeGpuMesh.Dispose();
            _boreholeGpuMesh = CreateGpuMesh(_results.BoreholeMesh);
        }
    }

    private void CreateDataTextures()
    {
        if (_results?.FinalTemperatureField == null || _mesh == null) return;

        var nr = (uint)_mesh.RadialPoints;
        var nth = (uint)_mesh.AngularPoints;
        var nz = (uint)_mesh.VerticalPoints;

        _temperatureTexture3D?.Dispose();
        _temperatureTexture3D =
            _factory.CreateTexture(TextureDescription.Texture3D(nr, nth, nz, 1, PixelFormat.R32_Float,
                TextureUsage.Sampled));
        _temperatureView?.Dispose();
        _temperatureView = _factory.CreateTextureView(_temperatureTexture3D);

        // CORRECTED: Flatten 3D array to 1D array for UpdateTexture
        var tempData = new float[nr * nth * nz];
        for (uint k = 0; k < nz; k++)
        for (uint j = 0; j < nth; j++)
        for (uint i = 0; i < nr; i++)
            // Veldrid texture data is ordered by Z, then Y, then X (or in our case, Z, Theta, R)
            tempData[k * nr * nth + j * nr + i] = _results.FinalTemperatureField[i, j, k];
        _graphicsDevice.UpdateTexture(_temperatureTexture3D, tempData, 0, 0, 0, nr, nth, nz, 0, 0);


        if (_results.DarcyVelocityField != null)
        {
            _velocityTexture3D?.Dispose();
            _velocityTexture3D = _factory.CreateTexture(TextureDescription.Texture3D(nr, nth, nz, 1,
                PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            _velocityView?.Dispose();
            _velocityView = _factory.CreateTextureView(_velocityTexture3D);

            // CORRECTED: Flatten 4D array to 1D Vector4 array
            var velData = new Vector4[nr * nth * nz];
            for (uint k = 0; k < nz; k++)
            for (uint j = 0; j < nth; j++)
            for (uint i = 0; i < nr; i++)
            {
                var idx = k * nr * nth + j * nr + i;
                velData[idx] = new Vector4(
                    _results.DarcyVelocityField[i, j, k, 0],
                    _results.DarcyVelocityField[i, j, k, 1],
                    _results.DarcyVelocityField[i, j, k, 2],
                    0);
            }

            // Explicitly specify the generic type argument
            _graphicsDevice.UpdateTexture(_velocityTexture3D, velData, 0, 0, 0, nr, nth, nz, 0, 0);
        }
    }

    private void CreateSliceQuad()
    {
        var r = _mesh != null ? (float)_options.DomainRadius : 50f;

        var vertices = new VertexData[]
        {
            new(new Vector3(-r, -r, 0), Vector4.One, Vector3.UnitZ, new Vector2(0, 0), 0, Vector3.Zero),
            new(new Vector3(r, -r, 0), Vector4.One, Vector3.UnitZ, new Vector2(1, 0), 0, Vector3.Zero),
            new(new Vector3(-r, r, 0), Vector4.One, Vector3.UnitZ, new Vector2(0, 1), 0, Vector3.Zero),
            new(new Vector3(r, r, 0), Vector4.One, Vector3.UnitZ, new Vector2(1, 1), 0, Vector3.Zero)
        };
        var indices = new uint[] { 0, 1, 2, 1, 3, 2 };

        _sliceQuad.Dispose();
        var vb = _factory.CreateBuffer(new BufferDescription((uint)(vertices.Length * Marshal.SizeOf<VertexData>()),
            BufferUsage.VertexBuffer));
        _graphicsDevice.UpdateBuffer(vb, 0, vertices);
        var ib = _factory.CreateBuffer(new BufferDescription((uint)(indices.Length * sizeof(uint)),
            BufferUsage.IndexBuffer));
        _graphicsDevice.UpdateBuffer(ib, 0, indices);
        _sliceQuad = new GpuMesh { VertexBuffer = vb, IndexBuffer = ib, IndexCount = (uint)indices.Length };
    }

    private void UpdateResourceSet()
    {
        _resourceSet?.Dispose();

        if (_velocityView == null)
        {
            var dummyTex = _factory.CreateTexture(TextureDescription.Texture3D(1, 1, 1, 1,
                PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));
            _velocityView = _factory.CreateTextureView(dummyTex);
            // Dispose the dummy texture right away if it's not needed elsewhere
            dummyTex.Dispose();
        }

        _resourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _resourceLayout,
            _uniformBuffer, _colorMapView, _linearSampler,
            _temperatureView, _velocityView, _pointSampler
        ));
    }

    public void Render()
    {
        if (_results == null) return;

        var commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
        commandList.Begin();
        commandList.SetFramebuffer(_framebuffer);
        commandList.ClearColorTarget(0, RgbaFloat.Black);
        commandList.ClearDepthStencil(1.0f);

        UpdateUniforms();

        // CRITICAL FIX: Must set pipeline BEFORE setting resource sets
        // We'll set the resource set after each SetPipeline call

        // Render domain mesh
        if (_showDomainMesh && _domainGpuMesh.VertexBuffer != null)
        {
            // CORRECTED: Use a switch statement to select the pipeline
            Pipeline pipeline;
            switch (_renderMode)
            {
                case RenderMode.Velocity:
                    pipeline = _velocityPipeline;
                    break;
                case RenderMode.Temperature:
                default:
                    pipeline = _temperaturePipeline;
                    break;
            }

            // The domain mesh should not be rendered in these modes
            if (_renderMode == RenderMode.Slices || _renderMode == RenderMode.Isosurface ||
                _renderMode == RenderMode.Streamlines)
            {
                // Do nothing, or render a wireframe
            }
            else
            {
                commandList.SetPipeline(pipeline);
                commandList.SetGraphicsResourceSet(0, _resourceSet); // After pipeline
                commandList.SetVertexBuffer(0, _domainGpuMesh.VertexBuffer);
                commandList.SetIndexBuffer(_domainGpuMesh.IndexBuffer, IndexFormat.UInt32);
                commandList.DrawIndexed(_domainGpuMesh.IndexCount);
            }
        }

        // Render dynamic meshes (isosurfaces, streamlines)
        if (_dynamicGpuMeshes.Any())
        {
            var pipeline = _renderMode == RenderMode.Streamlines ? _streamlinePipeline : _isosurfacePipeline;
            commandList.SetPipeline(pipeline);
            commandList.SetGraphicsResourceSet(0, _resourceSet); // After pipeline
            foreach (var gpuMesh in _dynamicGpuMeshes)
            {
                if (gpuMesh.VertexBuffer == null) continue;
                commandList.SetVertexBuffer(0, gpuMesh.VertexBuffer);
                commandList.SetIndexBuffer(gpuMesh.IndexBuffer, IndexFormat.UInt32);
                commandList.DrawIndexed(gpuMesh.IndexCount);
            }
        }

        // Render slices
        if (_renderMode == RenderMode.Slices && _sliceQuad.VertexBuffer != null)
        {
            commandList.SetPipeline(_slicePipeline);
            commandList.SetGraphicsResourceSet(0, _resourceSet); // After pipeline
            commandList.SetVertexBuffer(0, _sliceQuad.VertexBuffer);
            commandList.SetIndexBuffer(_sliceQuad.IndexBuffer, IndexFormat.UInt32);
            commandList.DrawIndexed(_sliceQuad.IndexCount);
        }

        // Render borehole
        if (_showBorehole && _boreholeGpuMesh.VertexBuffer != null)
        {
            commandList.SetPipeline(_isosurfacePipeline); // Use a simple solid shader
            commandList.SetGraphicsResourceSet(0, _resourceSet); // After pipeline
            commandList.SetVertexBuffer(0, _boreholeGpuMesh.VertexBuffer);
            commandList.SetIndexBuffer(_boreholeGpuMesh.IndexBuffer, IndexFormat.UInt32);
            commandList.DrawIndexed(_boreholeGpuMesh.IndexCount);
        }

        commandList.End();
        _graphicsDevice.SubmitCommands(commandList);
        _graphicsDevice.WaitForIdle();
        commandList.Dispose();
    }

    private void UpdateUniforms()
    {
        if (_options == null || _mesh == null) return; // Guard against null refs

        var uniforms = new UniformData
        {
            ViewMatrix = _viewMatrix,
            ProjectionMatrix = _projectionMatrix,
            ModelMatrix = Matrix4x4.Identity,
            LightDirection = Vector4.Normalize(new Vector4(-1, -1, -2, 0)),
            ViewPosition = new Vector4(_cameraPosition, 1),
            ColorMapRange = new Vector4(_temperatureMin, _temperatureMax - _temperatureMin, _velocityMax, 0),
            SliceInfo = new Vector4(_sliceDepth, 0, 0, 0),
            RenderSettings =
                new Vector4((float)_renderMode, _opacity, 0, _isoValue + 273.15f), // Pass Iso value in Kelvin
            DomainInfo = new Vector4((float)_options.DomainRadius, _mesh.Z[0], _mesh.Z.Last() - _mesh.Z[0], 0)
        };

        _graphicsDevice.UpdateBuffer(_uniformBuffer, 0, ref uniforms);
    }

    private void UpdateCamera()
    {
        var azimRad = _cameraAzimuth * MathF.PI / 180f;
        var elevRad = _cameraElevation * MathF.PI / 180f;

        _cameraPosition = new Vector3(
            _cameraDistance * MathF.Cos(elevRad) * MathF.Cos(azimRad),
            _cameraDistance * MathF.Cos(elevRad) * MathF.Sin(azimRad),
            _cameraDistance * MathF.Sin(elevRad)
        ) + _cameraTarget;

        _viewMatrix = Matrix4x4.CreateLookAt(_cameraPosition, _cameraTarget, _cameraUp);
        _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            45f * MathF.PI / 180f,
            (float)_renderWidth / Math.Max(1, _renderHeight), // prevent divide by zero
            0.1f,
            2000f
        );
    }

    public void RenderControls()
    {
        ImGui.Text("3D Visualization Controls");
        ImGui.Separator();

        if (ImGui.BeginCombo("Render Mode", _renderMode.ToString()))
        {
            foreach (var mode in Enum.GetValues<RenderMode>())
                if (ImGui.Selectable(mode.ToString(), _renderMode == mode))
                    _renderMode = mode;
            ImGui.EndCombo();
        }

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

        var tempMin = _temperatureMin;
        var tempMax = _temperatureMax;
        if (ImGui.DragFloatRange2("Temp Range (°C)", ref tempMin, ref tempMax, 0.1f, -50f, 200f))
        {
            _temperatureMin = tempMin;
            _temperatureMax = tempMax;
        }

        ImGui.Checkbox("Show Domain Outline", ref _showDomainMesh);
        ImGui.SameLine();
        ImGui.Checkbox("Show Borehole", ref _showBorehole);

        ImGui.SliderFloat("Opacity", ref _opacity, 0f, 1f);

        if (_renderMode == RenderMode.Isosurface)
            ImGui.SliderFloat("Iso Value (°C)", ref _isoValue, _temperatureMin, _temperatureMax);

        if (_renderMode == RenderMode.Slices)
            ImGui.SliderFloat("Slice Depth (0=top, 1=bottom)", ref _sliceDepth, 0f, 1f);

        ImGui.Separator();
        ImGui.Text("Camera");
        if (ImGui.SliderFloat("Distance", ref _cameraDistance, 10f, 500f)) UpdateCamera();
        if (ImGui.SliderFloat("Azimuth", ref _cameraAzimuth, -180f, 180f)) UpdateCamera();
        if (ImGui.SliderFloat("Elevation", ref _cameraElevation, -89f, 89f)) UpdateCamera();

        if (ImGui.Button("Reset Camera"))
        {
            _cameraDistance = 200f;
            _cameraAzimuth = 45f;
            _cameraElevation = 30f;
            _cameraTarget = Vector3.Zero;
            UpdateCamera();
        }
    }

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
            var forward = Vector3.Normalize(_cameraTarget - _cameraPosition);
            var right = Vector3.Normalize(Vector3.Cross(forward, _cameraUp));
            var up = Vector3.Normalize(Vector3.Cross(right, forward));
            var panSpeed = _cameraDistance / 500f;
            _cameraTarget += right * -delta.X * panSpeed;
            _cameraTarget += up * delta.Y * panSpeed;
            _lastMousePos = mousePos;
            UpdateCamera();
        }
    }

    public void HandleMouseWheel(float delta)
    {
        _cameraDistance = Math.Clamp(_cameraDistance - delta * 10f, 10f, 1000f);
        UpdateCamera();
    }

    public IntPtr GetRenderTargetImGuiBinding()
    {
        return VeldridManager.ImGuiController.GetOrCreateImGuiBinding(_factory, _renderTargetView);
    }

    public void Resize(uint width, uint height)
    {
        if (width > 0 && height > 0 && (width != _renderWidth || height != _renderHeight))
        {
            CreateRenderTarget(width, height);
            UpdateCamera();
        }
    }

    public void SetRenderMode(RenderMode mode)
    {
        _renderMode = mode;
    }

    public void SetSliceDepth(float depth)
    {
        _sliceDepth = Math.Clamp(depth, 0f, 1f);
    }

    public void AddMesh(Mesh3DDataset mesh)
    {
        var isStreamline = mesh.Name == "Streamlines";
        var gpuMesh = CreateGpuMesh(mesh, isStreamline);
        if (gpuMesh.VertexBuffer != null) _dynamicGpuMeshes.Add(gpuMesh);
    }

    public void AddMeshes(IEnumerable<Mesh3DDataset> meshes)
    {
        foreach (var mesh in meshes) AddMesh(mesh);
    }

    public void ClearDynamicMeshes()
    {
        foreach (var mesh in _dynamicGpuMeshes) mesh.Dispose();
        _dynamicGpuMeshes.Clear();
    }

    private void InitializeColorMaps()
    {
        _colorMapTexture?.Dispose();
        _colorMapView?.Dispose();
        var colorMapData = GenerateColorMapData(_currentColorMap);
        _colorMapTexture =
            _factory.CreateTexture(TextureDescription.Texture1D(256, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled));
        _graphicsDevice.UpdateTexture(_colorMapTexture, colorMapData, 0, 0, 0, 256, 1, 1, 0, 0);
        _colorMapView = _factory.CreateTextureView(_colorMapTexture);
    }

    private byte[] GenerateColorMapData(ColorMap map)
    {
        var data = new byte[256 * 4];
        for (var i = 0; i < 256; i++)
        {
            var t = i / 255f;
            var color = GetColorMapColor(map, t);
            data[i * 4 + 0] = (byte)(color.X * 255);
            data[i * 4 + 1] = (byte)(color.Y * 255);
            data[i * 4 + 2] = (byte)(color.Z * 255);
            data[i * 4 + 3] = 255;
        }

        return data;
    }

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
            _ => new Vector3(t, t, t)
        };
    }

    private Vector3 TurboColorMap(float t)
    {
        if (t < 0.25f) return Vector3.Lerp(new Vector3(0.2f, 0.1f, 0.5f), new Vector3(0.1f, 0.5f, 0.8f), t * 4);
        if (t < 0.5f)
            return Vector3.Lerp(new Vector3(0.1f, 0.5f, 0.8f), new Vector3(0.2f, 0.8f, 0.2f), (t - 0.25f) * 4);
        if (t < 0.75f)
            return Vector3.Lerp(new Vector3(0.2f, 0.8f, 0.2f), new Vector3(0.9f, 0.7f, 0.1f), (t - 0.5f) * 4);
        return Vector3.Lerp(new Vector3(0.9f, 0.7f, 0.1f), new Vector3(0.9f, 0.1f, 0.1f), (t - 0.75f) * 4);
    }

    private Vector3 ViridisColorMap(float t)
    {
        var r = 0.267f + 0.004780f * t + 0.329f * t * t - 0.698f * t * t * t;
        var g = 0.004f + 1.384f * t - 0.394f * t * t;
        var b = 0.329f + 1.267f * t - 2.572f * t * t + 1.977f * t * t * t;
        return new Vector3(r, g, b);
    }

    private Vector3 PlasmaColorMap(float t)
    {
        if (t < 0.5f) return Vector3.Lerp(new Vector3(0.1f, 0.0f, 0.5f), new Vector3(0.9f, 0.1f, 0.5f), t * 2);
        return Vector3.Lerp(new Vector3(0.9f, 0.1f, 0.5f), new Vector3(0.9f, 0.9f, 0.1f), (t - 0.5f) * 2);
    }

    private Vector3 JetColorMap(float t)
    {
        if (t < 0.125f) return new Vector3(0, 0, 0.5f + t * 4);
        if (t < 0.375f) return new Vector3(0, (t - 0.125f) * 4, 1);
        if (t < 0.625f) return new Vector3((t - 0.375f) * 4, 1, 1 - (t - 0.375f) * 4);
        if (t < 0.875f) return new Vector3(1, 1 - (t - 0.625f) * 4, 0);
        return new Vector3(1 - (t - 0.875f) * 4, 0, 0);
    }

    // Shader code getters
    private string GetVertexShaderCode(string shaderName)
    {
        return GetBasicVertexShader();
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
layout(location = 0) in vec3 Position; layout(location = 1) in vec4 Color; layout(location = 2) in vec3 Normal;
layout(location = 3) in vec2 TexCoord; layout(location = 4) in float Value; layout(location = 5) in vec3 UVW;

layout(location = 0) out vec4 frag_Color; layout(location = 1) out vec3 frag_Normal; layout(location = 2) out vec3 frag_UVW;
layout(location = 3) out vec3 frag_WorldPos;

layout(set = 0, binding = 0) uniform UniformData {
    mat4 ViewMatrix; mat4 ProjectionMatrix; mat4 ModelMatrix; vec4 LightDirection; vec4 ViewPosition;
    vec4 ColorMapRange; vec4 SliceInfo; vec4 RenderSettings; vec4 DomainInfo;
} ubo;

void main() {
    vec4 worldPos = ubo.ModelMatrix * vec4(Position, 1.0);

    // For slice, we need to adjust the Z position
    if (int(ubo.RenderSettings.x) == 5) { // 5 is RenderMode.Slices
        float z_min = ubo.DomainInfo.y;
        float z_range = ubo.DomainInfo.z;
        worldPos.z = z_min + ubo.SliceInfo.x * z_range;
    }

    gl_Position = ubo.ProjectionMatrix * ubo.ViewMatrix * worldPos;
    frag_Color = Color; frag_Normal = mat3(ubo.ModelMatrix) * Normal; frag_UVW = UVW;
    frag_WorldPos = worldPos.xyz;
}";
    }

    private string GetBasicFragmentShader()
    {
        return @"
#version 450
layout(location = 0) in vec4 frag_Color;
layout(location = 0) out vec4 FragColor;
void main() { FragColor = frag_Color; }";
    }

    private string GetTemperatureFragmentShader()
    {
        return @"
#version 450
layout(location = 0) in vec4 frag_Color; layout(location = 1) in vec3 frag_Normal; layout(location = 2) in vec3 frag_UVW;
layout(location = 3) in vec3 frag_WorldPos;
layout(location = 0) out vec4 FragColor;
layout(set = 0, binding = 0) uniform UniformData {
    mat4 v; mat4 p; mat4 m; vec4 LightDirection; vec4 ViewPosition; vec4 ColorMapRange; vec4 si; vec4 RenderSettings; vec4 di;
} ubo;
layout(set = 0, binding = 1) uniform texture1D ColorMap; layout(set = 0, binding = 2) uniform sampler ColorMapSampler;
layout(set = 0, binding = 3) uniform texture3D TemperatureData; layout(set = 0, binding = 5) uniform sampler DataSampler;
void main() {
    float temp_K = texture(sampler3D(TemperatureData, DataSampler), frag_UVW).r;
    float temp_C = temp_K - 273.15;
    float t = clamp((temp_C - ubo.ColorMapRange.x) / ubo.ColorMapRange.y, 0.0, 1.0);
    vec4 color = texture(sampler1D(ColorMap, ColorMapSampler), t);
    vec3 normal = normalize(frag_Normal);
    float diffuse = max(dot(normal, normalize(ubo.LightDirection.xyz)), 0.3);
    FragColor = vec4(color.rgb * diffuse, ubo.RenderSettings.y);
}";
    }

    private string GetIsosurfaceFragmentShader()
    {
        return @"
#version 450
layout(location = 0) in vec4 frag_Color; layout(location = 1) in vec3 frag_Normal; layout(location = 2) in vec3 frag_UVW;
layout(location = 3) in vec3 frag_WorldPos;
layout(location = 0) out vec4 FragColor;
layout(set = 0, binding = 0) uniform UniformData {
    mat4 v; mat4 p; mat4 m; vec4 LightDirection; vec4 ViewPosition; vec4 ColorMapRange; vec4 si; vec4 RenderSettings; vec4 di;
} ubo;
void main() {
    vec3 normal = normalize(frag_Normal);
    float diffuse = max(dot(normal, normalize(ubo.LightDirection.xyz)), 0.2);
    vec3 viewDir = normalize(ubo.ViewPosition.xyz - frag_WorldPos);
    vec3 reflectDir = reflect(-normalize(ubo.LightDirection.xyz), normal);
    float specular = pow(max(dot(viewDir, reflectDir), 0.0), 32);
    vec3 finalColor = frag_Color.rgb * diffuse + vec3(0.5) * specular;
    FragColor = vec4(finalColor, frag_Color.a * ubo.RenderSettings.y);
}";
    }

    private string GetStreamlineFragmentShader()
    {
        return @"
#version 450
layout(location = 0) in vec4 frag_Color; layout(location = 1) in vec3 frag_Normal; layout(location = 2) in vec3 frag_UVW;
layout(location = 3) in vec3 frag_WorldPos;
layout(location = 0) out vec4 FragColor;
layout(set = 0, binding = 0) uniform UniformData {
    mat4 v; mat4 p; mat4 m; vec4 LightDirection; vec4 ViewPosition; vec4 ColorMapRange; vec4 si; vec4 RenderSettings; vec4 di;
} ubo;
layout(set = 0, binding = 1) uniform texture1D ColorMap; layout(set = 0, binding = 2) uniform sampler ColorMapSampler;
layout(set = 0, binding = 4) uniform texture3D VelocityData; layout(set = 0, binding = 5) uniform sampler DataSampler;
void main() { 
    // Sample velocity at this location to color streamline by flow speed
    vec3 velocity = texture(sampler3D(VelocityData, DataSampler), frag_UVW).xyz;
    float magnitude = length(velocity);
    float t = clamp(magnitude / ubo.ColorMapRange.z, 0.0, 1.0);
    
    // Use color map for velocity-based coloring
    vec4 color = texture(sampler1D(ColorMap, ColorMapSampler), t);
    
    // Make streamlines bright and easy to see
    vec3 finalColor = mix(vec3(0.9, 0.1, 0.9), color.rgb, 0.7);
    FragColor = vec4(finalColor, 1.0); 
}";
    }

    private string GetSliceFragmentShader()
    {
        return @"
#version 450
layout(location = 0) in vec4 frag_Color; layout(location = 1) in vec3 frag_Normal; layout(location = 2) in vec3 frag_UVW;
layout(location = 3) in vec3 frag_WorldPos;
layout(location = 0) out vec4 FragColor;
layout(set = 0, binding = 0) uniform UniformData {
    mat4 v; mat4 p; mat4 m; vec4 LightDirection; vec4 ViewPosition; vec4 ColorMapRange; vec4 SliceInfo; vec4 RenderSettings; vec4 DomainInfo;
} ubo;
layout(set = 0, binding = 1) uniform texture1D ColorMap; layout(set = 0, binding = 2) uniform sampler ColorMapSampler;
layout(set = 0, binding = 3) uniform texture3D TemperatureData; layout(set = 0, binding = 5) uniform sampler DataSampler;
void main() {
    float domain_radius = ubo.DomainInfo.x;
    
    // Discard fragments outside the cylindrical domain
    if (length(frag_WorldPos.xy) > domain_radius) discard;

    // Transform world position to cylindrical to get texture coords
    float r = length(frag_WorldPos.xy);
    float theta = atan(frag_WorldPos.y, frag_WorldPos.x);
    if (theta < 0.0) theta += 2.0 * 3.14159;
    
    vec3 tex_coord_3d = vec3(r / domain_radius, theta / (2.0 * 3.14159), ubo.SliceInfo.x);
    
    float temp_K = texture(sampler3D(TemperatureData, DataSampler), tex_coord_3d).r;
    float temp_C = temp_K - 273.15;
    float t = clamp((temp_C - ubo.ColorMapRange.x) / ubo.ColorMapRange.y, 0.0, 1.0);
    vec4 color = texture(sampler1D(ColorMap, ColorMapSampler), t);
    
    FragColor = vec4(color.rgb, ubo.RenderSettings.y);
}";
    }

    private string GetVelocityFragmentShader()
    {
        return @"
#version 450
layout(location = 0) in vec4 frag_Color; layout(location = 1) in vec3 frag_Normal; layout(location = 2) in vec3 frag_UVW;
layout(location = 3) in vec3 frag_WorldPos;
layout(location = 0) out vec4 FragColor;
layout(set = 0, binding = 0) uniform UniformData {
    mat4 v; mat4 p; mat4 m; vec4 LightDirection; vec4 ViewPosition; vec4 ColorMapRange; vec4 si; vec4 RenderSettings; vec4 di;
} ubo;
layout(set = 0, binding = 1) uniform texture1D ColorMap; layout(set = 0, binding = 2) uniform sampler ColorMapSampler;
layout(set = 0, binding = 4) uniform texture3D VelocityData; layout(set = 0, binding = 5) uniform sampler DataSampler;
void main() {
    vec3 velocity = texture(sampler3D(VelocityData, DataSampler), frag_UVW).xyz;
    float magnitude = length(velocity);
    
    // Color based on velocity magnitude
    float t = clamp(magnitude / ubo.ColorMapRange.z, 0.0, 1.0);
    vec4 color = texture(sampler1D(ColorMap, ColorMapSampler), t);
    
    // Add directional highlighting: create stripes based on velocity direction
    vec3 vel_normalized = magnitude > 1e-6 ? normalize(velocity) : vec3(0, 0, 1);
    float stripePattern = sin(dot(frag_WorldPos, vel_normalized) * 3.14159 * 2.0) * 0.5 + 0.5;
    float directionStrength = 0.3 * stripePattern * t; // Only visible where velocity is significant
    
    // Lighting
    vec3 normal = normalize(frag_Normal);
    float diffuse = max(dot(normal, normalize(ubo.LightDirection.xyz)), 0.3);
    
    // Final color with directional pattern
    vec3 finalColor = color.rgb * diffuse + vec3(directionStrength);
    FragColor = vec4(finalColor, ubo.RenderSettings.y);
}";
    }

    // Helper struct to hold Veldrid resources for a renderable mesh
    private struct GpuMesh : IDisposable
    {
        public DeviceBuffer VertexBuffer;
        public DeviceBuffer IndexBuffer;
        public uint IndexCount;
        public Mesh3DDataset Source;

        public void Dispose()
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniformData
    {
        public Matrix4x4 ViewMatrix;
        public Matrix4x4 ProjectionMatrix;
        public Matrix4x4 ModelMatrix;
        public Vector4 LightDirection;
        public Vector4 ViewPosition;
        public Vector4 ColorMapRange; // x=min_temp_C, y=range_temp_C, z=vel_max
        public Vector4 SliceInfo; // x=depth(0-1)
        public Vector4 RenderSettings; // x=mode, y=opacity, z=time, w=isoValue_K
        public Vector4 DomainInfo; // x=radius, y=z_min, z=z_range
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VertexData
    {
        public Vector3 Position;
        public Vector4 Color;
        public Vector3 Normal;
        public Vector2 TexCoord;
        public float Value;
        public Vector3 UVW;

        public VertexData(Vector3 pos, Vector4 col, Vector3 norm, Vector2 uv, float val, Vector3 uvw)
        {
            Position = pos;
            Color = col;
            Normal = norm;
            TexCoord = uv;
            Value = val;
            UVW = uvw;
        }
    }
}