// GeoscientistToolkit/UI/Visualization/GeothermalVisualization3D.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Mesh3D;
using ImGuiNET;
using Veldrid;
using Veldrid.SPIRV;

namespace GeoscientistToolkit.UI.Visualization;

/// <summary>
/// 3D visualization system for geothermal simulation results with advanced rendering capabilities.
/// </summary>
public class GeothermalVisualization3D : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ResourceFactory _factory;
    
    // Render resources
    private Pipeline _temperaturePipeline;
    private Pipeline _velocityPipeline;
    private Pipeline _streamlinePipeline;
    private Pipeline _isosurfacePipeline;
    private Pipeline _slicePipeline;
    
    private DeviceBuffer _vertexBuffer;
    private DeviceBuffer _indexBuffer;
    private DeviceBuffer _uniformBuffer;
    private DeviceBuffer _temperatureDataBuffer;
    private DeviceBuffer _velocityDataBuffer;
    
    private ResourceLayout _resourceLayout;
    private ResourceSet _resourceSet;
    
    private Texture _colorMapTexture;
    private Texture _temperatureTexture3D;
    private Texture _velocityTexture3D;
    private TextureView _colorMapView;
    private TextureView _temperatureView;
    private TextureView _velocityView;
    
    private Sampler _linearSampler;
    private Sampler _pointSampler;
    
    // Visualization data
    private GeothermalSimulationResults _results;
    private GeothermalMesh _mesh;
    private readonly List<VertexPositionColorTexture> _vertices = new();
    private readonly List<uint> _indices = new();
    
    // Visualization settings
    private RenderMode _renderMode = RenderMode.Temperature;
    private ColorMap _currentColorMap = ColorMap.Turbo;
    private float _isoValue = 290f; // Kelvin
    private float _sliceDepth = 0.5f; // Normalized depth
    private float _temperatureMin = 273.15f;
    private float _temperatureMax = 373.15f;
    private float _velocityMin = 0f;
    private float _velocityMax = 0.001f;
    private float _streamlineLength = 100f;
    private float _streamlineThickness = 1f;
    private bool _showMesh = false;
    private bool _showBorehole = true;
    private bool _showVectors = false;
    private float _vectorScale = 10f;
    private float _opacity = 1.0f;
    
    // Camera and interaction
    private Matrix4x4 _viewMatrix;
    private Matrix4x4 _projectionMatrix;
    private Vector3 _cameraPosition = new(100, 100, 100);
    private Vector3 _cameraTarget = Vector3.Zero;
    private Vector3 _cameraUp = Vector3.UnitZ;
    private float _cameraDistance = 200f;
    private float _cameraAzimuth = 45f;
    private float _cameraElevation = 30f;
    
    private bool _isRotating = false;
    private bool _isPanning = false;
    private Vector2 _lastMousePos;
    
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
        
        public VertexPositionColorTexture(Vector3 position, Vector4 color, Vector3 normal, Vector2 texCoord, float value)
        {
            Position = position;
            Color = color;
            Normal = normal;
            TexCoord = texCoord;
            Value = value;
        }
        
        public static uint SizeInBytes => (uint)Marshal.SizeOf<VertexPositionColorTexture>();
    }
    
    public GeothermalVisualization3D(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        _factory = graphicsDevice.ResourceFactory;
        
        InitializeResources();
        InitializeColorMaps();
        UpdateCamera();
    }
    
    private void InitializeResources()
    {
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
            new ResourceLayoutElementDescription("UniformData", ResourceKind.UniformBuffer, ShaderStages.Vertex | ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ColorMap", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ColorMapSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("TemperatureData", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("VelocityData", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("DataSampler", ResourceKind.Sampler, ShaderStages.Fragment)
        ));
        
        CreatePipelines();
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
            System.Text.Encoding.UTF8.GetBytes(vertexCode),
            "main");
        
        var fragmentShaderDesc = new ShaderDescription(
            ShaderStages.Fragment,
            System.Text.Encoding.UTF8.GetBytes(fragmentCode),
            "main");
        
        var shaders = _factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        return (shaders[0], shaders[1]);
    }
    
    private Pipeline CreatePipeline((Shader vertex, Shader fragment) shaders, 
        BlendStateDescription blendState, 
        PrimitiveTopology topology = PrimitiveTopology.TriangleList)
    {
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Value", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1)
        );
        
        var pipelineDescription = new GraphicsPipelineDescription
        {
            BlendState = blendState,
            DepthStencilState = new DepthStencilStateDescription(
                depthTestEnabled: true,
                depthWriteEnabled: true,
                comparisonKind: ComparisonKind.Less),
            RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.CounterClockwise,
                depthClipEnabled: true,
                scissorTestEnabled: false),
            PrimitiveTopology = topology,
            ResourceLayouts = new[] { _resourceLayout },
            ShaderSet = new ShaderSetDescription(
                vertexLayouts: new[] { vertexLayout },
                shaders: new[] { shaders.vertex, shaders.fragment }),
            Outputs = _graphicsDevice.SwapchainFramebuffer.OutputDescription
        };
        
        return _factory.CreateGraphicsPipeline(pipelineDescription);
    }
    
    /// <summary>
    /// Loads simulation results and prepares visualization data.
    /// </summary>
    public void LoadResults(GeothermalSimulationResults results, GeothermalMesh mesh)
    {
        _results = results;
        _mesh = mesh;
        
        GenerateMeshGeometry();
        CreateDataTextures();
        UpdateBuffers();
    }
    
    /// <summary>
    /// Generates 3D mesh geometry from simulation mesh.
    /// </summary>
    private void GenerateMeshGeometry()
    {
        _vertices.Clear();
        _indices.Clear();
        
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        
        // Generate vertices
        for (int i = 0; i < nr; i++)
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    var r = _mesh.R[i];
                    var theta = _mesh.Theta[j];
                    var z = _mesh.Z[k];
                    
                    // Convert cylindrical to Cartesian
                    var x = r * MathF.Cos(theta);
                    var y = r * MathF.Sin(theta);
                    var position = new Vector3(x, y, z);
                    
                    // Get data values
                    var temperature = _results.FinalTemperatureField?[i, j, k] ?? 293.15f;
                    var velocity = 0f;
                    if (_results.DarcyVelocityField != null)
                    {
                        var vr = _results.DarcyVelocityField[i, j, k, 0];
                        var vth = _results.DarcyVelocityField[i, j, k, 1];
                        var vz = _results.DarcyVelocityField[i, j, k, 2];
                        velocity = MathF.Sqrt(vr * vr + vth * vth + vz * vz);
                    }
                    
                    // Calculate normal (approximate)
                    var normal = Vector3.Normalize(new Vector3(x, y, 0));
                    
                    // Map to texture coordinates
                    var texCoord = new Vector2((float)j / nth, (float)k / nz);
                    
                    // Create vertex
                    var color = TemperatureToColor(temperature);
                    _vertices.Add(new VertexPositionColorTexture(
                        position, color, normal, texCoord, temperature));
                }
            }
        }
        
        // Generate indices for triangles
        for (int i = 0; i < nr - 1; i++)
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 0; k < nz - 1; k++)
                {
                    var j1 = (j + 1) % nth;
                    
                    // Current layer indices
                    uint idx00 = (uint)(i * nth * nz + j * nz + k);
                    uint idx01 = (uint)(i * nth * nz + j * nz + k + 1);
                    uint idx10 = (uint)(i * nth * nz + j1 * nz + k);
                    uint idx11 = (uint)(i * nth * nz + j1 * nz + k + 1);
                    
                    // Next radial layer indices
                    uint idx20 = (uint)((i + 1) * nth * nz + j * nz + k);
                    uint idx21 = (uint)((i + 1) * nth * nz + j * nz + k + 1);
                    uint idx30 = (uint)((i + 1) * nth * nz + j1 * nz + k);
                    uint idx31 = (uint)((i + 1) * nth * nz + j1 * nz + k + 1);
                    
                    // Create quads (as two triangles each)
                    // Radial faces
                    _indices.Add(idx00); _indices.Add(idx01); _indices.Add(idx21);
                    _indices.Add(idx00); _indices.Add(idx21); _indices.Add(idx20);
                    
                    // Angular faces
                    _indices.Add(idx00); _indices.Add(idx10); _indices.Add(idx11);
                    _indices.Add(idx00); _indices.Add(idx11); _indices.Add(idx01);
                    
                    // Vertical faces
                    _indices.Add(idx00); _indices.Add(idx20); _indices.Add(idx30);
                    _indices.Add(idx00); _indices.Add(idx30); _indices.Add(idx10);
                }
            }
        }
    }
    
    /// <summary>
    /// Creates 3D textures from simulation data.
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
        for (int i = 0; i < nr; i++)
        {
            for (int j = 0; j < nth; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    tempData[i * nth * nz + j * nz + k] = _results.FinalTemperatureField[i, j, k];
                }
            }
        }
        
        _graphicsDevice.UpdateTexture(_temperatureTexture3D, tempData, 0, 0, 0, (uint)nr, (uint)nth, (uint)nz, 0, 0);
        
        // Create velocity 3D texture if available
        if (_results.DarcyVelocityField != null)
        {
            _velocityTexture3D?.Dispose();
            _velocityTexture3D = _factory.CreateTexture(new TextureDescription(
                (uint)nr, (uint)nth, (uint)nz,
                1, 1,
                PixelFormat.R32G32B32A32_Float,
                TextureUsage.Sampled,
                TextureType.Texture3D));
            
            // Upload velocity data
            var velData = new Vector4[nr * nth * nz];
            for (int i = 0; i < nr; i++)
            {
                for (int j = 0; j < nth; j++)
                {
                    for (int k = 0; k < nz; k++)
                    {
                        var idx = i * nth * nz + j * nz + k;
                        velData[idx] = new Vector4(
                            _results.DarcyVelocityField[i, j, k, 0],
                            _results.DarcyVelocityField[i, j, k, 1],
                            _results.DarcyVelocityField[i, j, k, 2],
                            0);
                    }
                }
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
    /// Updates GPU buffers with mesh data.
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
        
        // Update resource set
        _resourceSet?.Dispose();
        _resourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _resourceLayout,
            _uniformBuffer,
            _colorMapView ?? _temperatureView,
            _linearSampler,
            _temperatureView,
            _velocityView ?? _temperatureView,
            _pointSampler
        ));
    }
    
    /// <summary>
    /// Renders the visualization.
    /// </summary>
    public void Render(CommandList commandList)
    {
        if (_vertexBuffer == null || _indexBuffer == null || _resourceSet == null)
            return;
        
        // Update uniform buffer
        var uniformData = new UniformData
        {
            ViewMatrix = _viewMatrix,
            ProjectionMatrix = _projectionMatrix,
            ModelMatrix = Matrix4x4.Identity,
            LightDirection = Vector4.Normalize(new Vector4(1, 1, 2, 0)),
            ViewPosition = new Vector4(_cameraPosition, 1),
            ColorMapRange = new Vector4(_temperatureMin, _temperatureMax, 0, 0),
            SliceInfo = new Vector4(_sliceDepth, 0, 0, 1),
            RenderSettings = new Vector4((float)_renderMode, _opacity, 0, _isoValue)
        };
        
        commandList.UpdateBuffer(_uniformBuffer, 0, uniformData);
        
        // Select pipeline based on render mode
        Pipeline pipeline = _renderMode switch
        {
            RenderMode.Temperature => _temperaturePipeline,
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
        {
            RenderStreamlines(commandList);
        }
        else
        {
            commandList.DrawIndexed((uint)_indices.Count);
        }
        
        // Render additional elements
        if (_showBorehole)
        {
            RenderBorehole(commandList);
        }
        
        if (_showVectors)
        {
            RenderVelocityVectors(commandList);
        }
    }
    
    /// <summary>
    /// Renders velocity streamlines.
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
            
            for (int i = 0; i < streamline.Count; i++)
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
    /// Renders the borehole geometry.
    /// </summary>
    private void RenderBorehole(CommandList commandList)
    {
        if (_results?.BoreholeMesh == null)
            return;
        
        // Use the borehole mesh from results
        // This would render the actual borehole geometry
        // Implementation depends on the Mesh3DDataset structure
    }
    
    /// <summary>
    /// Renders velocity vectors.
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
        
        // Sample vectors at regular intervals
        var skip = 5;
        uint vertexIdx = 0;
        
        for (int i = 0; i < nr; i += skip)
        {
            for (int j = 0; j < nth; j += skip)
            {
                for (int k = 0; k < nz; k += skip)
                {
                    var r = _mesh.R[i];
                    var theta = _mesh.Theta[j];
                    var z = _mesh.Z[k];
                    
                    var x = r * MathF.Cos(theta);
                    var y = r * MathF.Sin(theta);
                    var start = new Vector3(x, y, z);
                    
                    var vr = _results.DarcyVelocityField[i, j, k, 0];
                    var vth = _results.DarcyVelocityField[i, j, k, 1];
                    var vz = _results.DarcyVelocityField[i, j, k, 2];
                    
                    // Convert to Cartesian
                    var vx = vr * MathF.Cos(theta) - vth * MathF.Sin(theta);
                    var vy = vr * MathF.Sin(theta) + vth * MathF.Cos(theta);
                    var velocity = new Vector3(vx, vy, vz);
                    
                    var magnitude = velocity.Length();
                    if (magnitude < 1e-10f)
                        continue;
                    
                    var end = start + velocity * _vectorScale;
                    var color = ColorMapValue(magnitude / _velocityMax);
                    
                    vectorVertices.Add(new VertexPositionColorTexture(start, color, Vector3.Zero, Vector2.Zero, magnitude));
                    vectorVertices.Add(new VertexPositionColorTexture(end, color, Vector3.Zero, Vector2.One, magnitude));
                    
                    vectorIndices.Add(vertexIdx);
                    vectorIndices.Add(vertexIdx + 1);
                    vertexIdx += 2;
                }
            }
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
            
            commandList.SetPipeline(_streamlinePipeline); // Use line pipeline
            commandList.SetVertexBuffer(0, vectorVB);
            commandList.SetIndexBuffer(vectorIB, IndexFormat.UInt32);
            commandList.DrawIndexed((uint)vectorIndices.Count);
            
            vectorVB.Dispose();
            vectorIB.Dispose();
        }
    }
    
    /// <summary>
    /// Draws the ImGui controls for the visualization.
    /// </summary>
    public void DrawImGuiControls()
    {
        if (ImGui.Begin("Geothermal Visualization"))
        {
            ImGui.Text("Render Mode");
            if (ImGui.RadioButton("Temperature", _renderMode == RenderMode.Temperature))
                _renderMode = RenderMode.Temperature;
            if (ImGui.RadioButton("Velocity", _renderMode == RenderMode.Velocity))
                _renderMode = RenderMode.Velocity;
            if (ImGui.RadioButton("Pressure", _renderMode == RenderMode.Pressure))
                _renderMode = RenderMode.Pressure;
            if (ImGui.RadioButton("Streamlines", _renderMode == RenderMode.Streamlines))
                _renderMode = RenderMode.Streamlines;
            if (ImGui.RadioButton("Isosurface", _renderMode == RenderMode.Isosurface))
                _renderMode = RenderMode.Isosurface;
            if (ImGui.RadioButton("Slices", _renderMode == RenderMode.Slices))
                _renderMode = RenderMode.Slices;
            
            ImGui.Separator();
            
            ImGui.Text("Color Map");
            if (ImGui.Combo("##ColorMap", ref Unsafe.As<ColorMap, int>(ref _currentColorMap), 
                new[] { "Turbo", "Viridis", "Plasma", "Inferno", "Magma", "Jet", "Rainbow", "Thermal", "Blue-Red" }, 9))
            {
                UpdateColorMap();
            }
            
            ImGui.Separator();
            
            ImGui.Text("Range Settings");
            ImGui.DragFloat("Temp Min (°C)", ref _temperatureMin, 1f, 0f, 200f);
            _temperatureMin = Math.Max(0, _temperatureMin) + 273.15f;
            ImGui.DragFloat("Temp Max (°C)", ref _temperatureMax, 1f, 0f, 200f);
            _temperatureMax = Math.Max(_temperatureMin, _temperatureMax) + 273.15f;
            
            ImGui.DragFloat("Velocity Min", ref _velocityMin, 0.0001f, 0f, 0.1f, "%.6f");
            ImGui.DragFloat("Velocity Max", ref _velocityMax, 0.0001f, 0f, 0.1f, "%.6f");
            
            ImGui.Separator();
            
            ImGui.Text("Display Options");
            ImGui.Checkbox("Show Mesh", ref _showMesh);
            ImGui.Checkbox("Show Borehole", ref _showBorehole);
            ImGui.Checkbox("Show Velocity Vectors", ref _showVectors);
            
            if (_showVectors)
            {
                ImGui.DragFloat("Vector Scale", ref _vectorScale, 0.1f, 0.1f, 100f);
            }
            
            ImGui.DragFloat("Opacity", ref _opacity, 0.01f, 0f, 1f);
            
            if (_renderMode == RenderMode.Isosurface)
            {
                ImGui.DragFloat("Iso Value (°C)", ref _isoValue, 1f, 0f, 100f);
                _isoValue += 273.15f;
            }
            
            if (_renderMode == RenderMode.Slices)
            {
                ImGui.SliderFloat("Slice Depth", ref _sliceDepth, 0f, 1f);
            }
            
            ImGui.Separator();
            
            ImGui.Text("Camera");
            ImGui.DragFloat("Distance", ref _cameraDistance, 1f, 10f, 1000f);
            ImGui.DragFloat("Azimuth", ref _cameraAzimuth, 1f, -180f, 180f);
            ImGui.DragFloat("Elevation", ref _cameraElevation, 1f, -90f, 90f);
            
            if (ImGui.Button("Reset Camera"))
            {
                ResetCamera();
            }
            
            ImGui.Separator();
            
            if (_results != null)
            {
                ImGui.Text("Results Info");
                ImGui.Text($"Avg Heat Extraction: {_results.AverageHeatExtractionRate:F0} W");
                ImGui.Text($"Total Energy: {_results.TotalExtractedEnergy / 1e9:F2} GJ");
                ImGui.Text($"Thermal Radius: {_results.ThermalInfluenceRadius:F1} m");
                if (_results.OutletTemperature?.Any() == true)
                {
                    var lastTemp = _results.OutletTemperature.Last().temperature - 273.15;
                    ImGui.Text($"Outlet Temp: {lastTemp:F1} °C");
                }
            }
        }
        ImGui.End();
    }
    
    /// <summary>
    /// Updates camera matrices.
    /// </summary>
    private void UpdateCamera()
    {
        // Calculate camera position from spherical coordinates
        var azimuthRad = _cameraAzimuth * MathF.PI / 180f;
        var elevationRad = _cameraElevation * MathF.PI / 180f;
        
        _cameraPosition = new Vector3(
            _cameraDistance * MathF.Cos(elevationRad) * MathF.Cos(azimuthRad),
            _cameraDistance * MathF.Cos(elevationRad) * MathF.Sin(azimuthRad),
            _cameraDistance * MathF.Sin(elevationRad)
        );
        
        _viewMatrix = Matrix4x4.CreateLookAt(_cameraPosition, _cameraTarget, _cameraUp);
        _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4f, // 45 degree FOV
            (float)_graphicsDevice.SwapchainFramebuffer.Width / _graphicsDevice.SwapchainFramebuffer.Height,
            0.1f,
            10000f
        );
    }
    
    /// <summary>
    /// Resets camera to default position.
    /// </summary>
    private void ResetCamera()
    {
        _cameraDistance = 200f;
        _cameraAzimuth = 45f;
        _cameraElevation = 30f;
        UpdateCamera();
    }
    
    /// <summary>
    /// Handles mouse input for camera control.
    /// </summary>
    public void HandleMouseInput(Vector2 mousePos, bool leftButton, bool rightButton, bool middleButton, float wheelDelta)
    {
        var mouseDelta = mousePos - _lastMousePos;
        
        if (leftButton && !_isRotating)
        {
            _isRotating = true;
        }
        else if (!leftButton && _isRotating)
        {
            _isRotating = false;
        }
        
        if (rightButton && !_isPanning)
        {
            _isPanning = true;
        }
        else if (!rightButton && _isPanning)
        {
            _isPanning = false;
        }
        
        if (_isRotating)
        {
            _cameraAzimuth += mouseDelta.X * 0.5f;
            _cameraElevation = Math.Clamp(_cameraElevation - mouseDelta.Y * 0.5f, -89f, 89f);
            UpdateCamera();
        }
        
        if (_isPanning)
        {
            var right = Vector3.Cross(_cameraUp, Vector3.Normalize(_cameraPosition - _cameraTarget));
            var up = Vector3.Cross(Vector3.Normalize(_cameraPosition - _cameraTarget), right);
            
            _cameraTarget += right * mouseDelta.X * 0.1f + up * mouseDelta.Y * 0.1f;
            UpdateCamera();
        }
        
        if (Math.Abs(wheelDelta) > 0.001f)
        {
            _cameraDistance = Math.Clamp(_cameraDistance - wheelDelta * 10f, 10f, 1000f);
            UpdateCamera();
        }
        
        _lastMousePos = mousePos;
    }
    
    /// <summary>
    /// Initializes color map textures.
    /// </summary>
    private void InitializeColorMaps()
    {
        UpdateColorMap();
    }
    
    /// <summary>
    /// Updates the active color map texture.
    /// </summary>
    private void UpdateColorMap()
    {
        _colorMapTexture?.Dispose();
        _colorMapView?.Dispose();
        
        var colorMapData = GenerateColorMapData(_currentColorMap);
        
        _colorMapTexture = _factory.CreateTexture(new TextureDescription(
            256, 1, 1, 1, 1,
            PixelFormat.R8G8B8A8_UNorm,
            TextureUsage.Sampled,
            TextureType.Texture1D
        ));
        
        _graphicsDevice.UpdateTexture(_colorMapTexture, colorMapData, 0, 0, 0, 256, 1, 1, 0, 0);
        _colorMapView = _factory.CreateTextureView(_colorMapTexture);
    }
    
    /// <summary>
    /// Generates color map data for the specified color map.
    /// </summary>
    private byte[] GenerateColorMapData(ColorMap colorMap)
    {
        var data = new byte[256 * 4];
        
        for (int i = 0; i < 256; i++)
        {
            var t = i / 255f;
            var color = GetColorMapColor(colorMap, t);
            
            data[i * 4 + 0] = (byte)(color.X * 255);
            data[i * 4 + 1] = (byte)(color.Y * 255);
            data[i * 4 + 2] = (byte)(color.Z * 255);
            data[i * 4 + 3] = 255;
        }
        
        return data;
    }
    
    /// <summary>
    /// Gets color from color map at normalized position.
    /// </summary>
    private Vector3 GetColorMapColor(ColorMap colorMap, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        
        return colorMap switch
        {
            ColorMap.Turbo => GetTurboColor(t),
            ColorMap.Viridis => GetViridisColor(t),
            ColorMap.Plasma => GetPlasmaColor(t),
            ColorMap.Inferno => GetInfernoColor(t),
            ColorMap.Magma => GetMagmaColor(t),
            ColorMap.Jet => GetJetColor(t),
            ColorMap.Rainbow => GetRainbowColor(t),
            ColorMap.Thermal => GetThermalColor(t),
            ColorMap.BlueRed => GetBlueRedColor(t),
            _ => new Vector3(t, t, t)
        };
    }
    
    // Individual color map implementations
    private Vector3 GetTurboColor(float t)
    {
        // Turbo colormap approximation
        float r = Math.Max(0, Math.Min(1, 2.0f * t - 0.5f));
        float g = Math.Max(0, Math.Min(1, 4.0f * t * (1 - t)));
        float b = Math.Max(0, Math.Min(1, 1.5f - Math.Abs(2.0f * t - 1.0f)));
        return new Vector3(r, g, b);
    }
    
    private Vector3 GetViridisColor(float t)
    {
        // Viridis colormap approximation
        float r = 0.267f + t * (0.004f + t * (0.329f + t * 2.755f));
        float g = 0.004f + t * (0.108f + t * (1.524f - t * 0.720f));
        float b = 0.329f + t * (1.558f - t * (1.448f - t * 0.290f));
        return new Vector3(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
    }
    
    private Vector3 GetPlasmaColor(float t)
    {
        // Plasma colormap approximation
        float r = 0.050f + t * (2.788f - t * (3.222f - t * 1.882f));
        float g = 0.029f + t * (0.024f + t * (0.878f + t * 0.291f));
        float b = 0.527f + t * (1.351f - t * (2.315f - t * 1.509f));
        return new Vector3(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
    }
    
    private Vector3 GetInfernoColor(float t)
    {
        // Inferno colormap approximation
        float r = 0.001f + t * (1.975f - t * (1.076f - t * 0.156f));
        float g = t * (0.012f + t * (0.663f + t * 0.559f));
        float b = 0.014f + t * (1.409f - t * (3.892f - t * 3.524f));
        return new Vector3(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
    }
    
    private Vector3 GetMagmaColor(float t)
    {
        // Magma colormap approximation
        float r = 0.001f + t * (1.463f + t * (0.421f - t * 0.025f));
        float g = t * t * (0.662f + t * 0.547f);
        float b = 0.014f + t * (1.787f - t * (4.299f - t * 3.700f));
        return new Vector3(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
    }
    
    private Vector3 GetJetColor(float t)
    {
        // Jet colormap
        float r = Math.Clamp(1.5f - Math.Abs(4.0f * t - 3.0f), 0, 1);
        float g = Math.Clamp(1.5f - Math.Abs(4.0f * t - 2.0f), 0, 1);
        float b = Math.Clamp(1.5f - Math.Abs(4.0f * t - 1.0f), 0, 1);
        return new Vector3(r, g, b);
    }
    
    private Vector3 GetRainbowColor(float t)
    {
        // HSV to RGB with full saturation and value
        float h = t * 360f;
        float c = 1.0f;
        float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
        float m = 0f;
        
        Vector3 rgb;
        if (h < 60) rgb = new Vector3(c, x, 0);
        else if (h < 120) rgb = new Vector3(x, c, 0);
        else if (h < 180) rgb = new Vector3(0, c, x);
        else if (h < 240) rgb = new Vector3(0, x, c);
        else if (h < 300) rgb = new Vector3(x, 0, c);
        else rgb = new Vector3(c, 0, x);
        
        return rgb + new Vector3(m, m, m);
    }
    
    private Vector3 GetThermalColor(float t)
    {
        // Thermal camera style (black -> red -> yellow -> white)
        if (t < 0.33f)
        {
            float s = t * 3;
            return new Vector3(s, 0, 0);
        }
        else if (t < 0.67f)
        {
            float s = (t - 0.33f) * 3;
            return new Vector3(1, s, 0);
        }
        else
        {
            float s = (t - 0.67f) * 3;
            return new Vector3(1, 1, s);
        }
    }
    
    private Vector3 GetBlueRedColor(float t)
    {
        // Blue to Red diverging
        if (t < 0.5f)
        {
            float s = t * 2;
            return new Vector3(s, s, 1);
        }
        else
        {
            float s = (t - 0.5f) * 2;
            return new Vector3(1, 1 - s, 1 - s);
        }
    }
    
    /// <summary>
    /// Maps temperature to color.
    /// </summary>
    private Vector4 TemperatureToColor(float temperature)
    {
        var t = (temperature - _temperatureMin) / (_temperatureMax - _temperatureMin);
        return ColorMapValue(t);
    }
    
    /// <summary>
    /// Gets color from current color map.
    /// </summary>
    private Vector4 ColorMapValue(float t)
    {
        var color = GetColorMapColor(_currentColorMap, t);
        return new Vector4(color, _opacity);
    }
    
    // Shader code generation methods
    private string GetVertexShaderCode(string name)
    {
        return name switch
        {
            "Temperature" => GetTemperatureVertexShader(),
            "Velocity" => GetVelocityVertexShader(),
            "Streamline" => GetStreamlineVertexShader(),
            "Isosurface" => GetIsosurfaceVertexShader(),
            "Slice" => GetSliceVertexShader(),
            _ => GetBasicVertexShader()
        };
    }
    
    private string GetFragmentShaderCode(string name)
    {
        return name switch
        {
            "Temperature" => GetTemperatureFragmentShader(),
            "Velocity" => GetVelocityFragmentShader(),
            "Streamline" => GetStreamlineFragmentShader(),
            "Isosurface" => GetIsosurfaceFragmentShader(),
            "Slice" => GetSliceFragmentShader(),
            _ => GetBasicFragmentShader()
        };
    }
    
    private string GetBasicVertexShader() => @"
#version 450

layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;
layout(location = 2) in vec3 Normal;
layout(location = 3) in vec2 TexCoord;
layout(location = 4) in float Value;

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

layout(location = 0) out vec4 frag_Color;
layout(location = 1) out vec3 frag_Normal;
layout(location = 2) out vec2 frag_TexCoord;
layout(location = 3) out float frag_Value;
layout(location = 4) out vec3 frag_WorldPos;

void main() {
    vec4 worldPos = ubo.ModelMatrix * vec4(Position, 1.0);
    frag_WorldPos = worldPos.xyz;
    gl_Position = ubo.ProjectionMatrix * ubo.ViewMatrix * worldPos;
    frag_Color = Color;
    frag_Normal = mat3(ubo.ModelMatrix) * Normal;
    frag_TexCoord = TexCoord;
    frag_Value = Value;
}";
    
    private string GetBasicFragmentShader() => @"
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
    
    private string GetTemperatureVertexShader() => GetBasicVertexShader();
    
    private string GetTemperatureFragmentShader() => @"
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
    
    private string GetVelocityVertexShader() => GetBasicVertexShader();
    
    private string GetVelocityFragmentShader() => @"
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
    
    private string GetStreamlineVertexShader() => GetBasicVertexShader();
    private string GetStreamlineFragmentShader() => GetBasicFragmentShader();
    
    private string GetIsosurfaceVertexShader() => GetBasicVertexShader();
    
    private string GetIsosurfaceFragmentShader() => @"
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
    
    private string GetSliceVertexShader() => GetBasicVertexShader();
    
    private string GetSliceFragmentShader() => @"
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
    }
}