// GeoscientistToolkit/UI/Visualization/GeothermalVisualization3D.cs

using System.IO;
using System.Linq;
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
    private float _cameraDistance = 300f;
    private float _cameraElevation = 45f;
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
    private float _previewBoreholeDepth;

    // Preview mode state
    private GeothermalSimulationOptions _previewOptions;
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
    private bool _showHeatExchanger = true;  // Show heat exchanger by default
    private bool _showVelocityVectors;
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
    
    // Clipping planes for volume slicing
    private bool _enableClipping = false;
    private int _clipAxis = 0; // 0=X, 1=Y, 2=Z
    private float _clipPosition = 0.5f; // 0-1 normalized
    private bool _clipNegativeSide = true; // Which side to clip
    
    private Pipeline _velocityPipeline;
    private Texture _velocityTexture3D;
    private TextureView _velocityView;
    private Matrix4x4 _viewMatrix;
    private Pipeline _wireframePipeline; // Add wireframe pipeline for lines

    public GeothermalVisualization3D(GraphicsDevice graphicsDevice)
    {
        Logger.Log("[GeothermalVisualization3D] Constructor starting...");
        _graphicsDevice = graphicsDevice;
        _factory = graphicsDevice.ResourceFactory;

        Logger.Log("[GeothermalVisualization3D] Calling InitializeColorMaps...");
        InitializeColorMaps(); // Must be called BEFORE InitializeResources

        Logger.Log("[GeothermalVisualization3D] Calling InitializeResources...");
        InitializeResources();

        Logger.Log("[GeothermalVisualization3D] Calling UpdateCamera...");
        UpdateCamera();

        Logger.Log("[GeothermalVisualization3D] Constructor complete!");
    }

    public void Dispose()
    {
        _temperaturePipeline?.Dispose();
        _velocityPipeline?.Dispose();
        _streamlinePipeline?.Dispose();
        _isosurfacePipeline?.Dispose();
        _slicePipeline?.Dispose();
        _wireframePipeline?.Dispose();

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
        Logger.Log("[InitializeResources] Starting...");

        Logger.Log("[InitializeResources] Creating render target...");
        CreateRenderTarget(_renderWidth, _renderHeight);

        Logger.Log("[InitializeResources] Creating uniform buffer...");
        _uniformBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<UniformData>(),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));

        Logger.Log("[InitializeResources] Creating samplers...");
        _linearSampler = _factory.CreateSampler(SamplerDescription.Linear);
        _pointSampler = _factory.CreateSampler(SamplerDescription.Point);

        Logger.Log("[InitializeResources] Creating resource layout...");
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

        Logger.Log("[InitializeResources] Creating pipelines...");
        CreatePipelines();

        Logger.Log("[InitializeResources] Creating dummy textures...");
        // Create dummy textures for preview mode
        CreateDummyTextures();

        Logger.Log("[InitializeResources] Initializing resource set...");
        // Initialize resource set
        InitializeResourceSet();

        Logger.Log("[InitializeResources] Complete!");
    }

    private void CreateDummyTextures()
    {
        Logger.Log("[CreateDummyTextures] Starting...");

        // Create dummy temperature texture if not exists
        if (_temperatureTexture3D == null)
        {
            Logger.Log("[CreateDummyTextures] Creating temperature texture...");

            // Use 2x2x2 texture to avoid potential D3D11 edge cases with 1x1x1 3D textures
            uint texSize = 2;
            _temperatureTexture3D = _factory.CreateTexture(TextureDescription.Texture3D(
                texSize, texSize, texSize, 1,
                PixelFormat.R32_Float, TextureUsage.Sampled));

            // Initialize with dummy data BEFORE creating view (required by D3D11 for some configurations)
            var dataSize = (int)(texSize * texSize * texSize);
            var dummyData = new float[dataSize];
            for (var i = 0; i < dataSize; i++) dummyData[i] = 293.15f; // 20°C in Kelvin
            _graphicsDevice.UpdateTexture(_temperatureTexture3D, dummyData, 0, 0, 0, texSize, texSize, texSize, 0, 0);

            // Now create the view after data initialization
            _temperatureView?.Dispose();
            _temperatureView = _factory.CreateTextureView(_temperatureTexture3D);

            if (_temperatureView == null)
                throw new InvalidOperationException("Failed to create temperature texture view");

            Logger.Log("[CreateDummyTextures] Temperature texture created successfully");
        }

        // Create dummy velocity texture if not exists
        if (_velocityTexture3D == null)
        {
            Logger.Log("[CreateDummyTextures] Creating velocity texture...");

            // Use 2x2x2 texture to avoid potential D3D11 edge cases with 1x1x1 3D textures
            uint texSize = 2;
            _velocityTexture3D = _factory.CreateTexture(TextureDescription.Texture3D(
                texSize, texSize, texSize, 1,
                PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled));

            // Initialize with dummy data BEFORE creating view (required by D3D11 for some configurations)
            var dataSize = (int)(texSize * texSize * texSize);
            var dummyData = new Vector4[dataSize];
            for (var i = 0; i < dataSize; i++) dummyData[i] = Vector4.Zero;
            _graphicsDevice.UpdateTexture(_velocityTexture3D, dummyData, 0, 0, 0, texSize, texSize, texSize, 0, 0);

            // Now create the view after data initialization
            _velocityView?.Dispose();
            _velocityView = _factory.CreateTextureView(_velocityTexture3D);

            if (_velocityView == null) throw new InvalidOperationException("Failed to create velocity texture view");

            Logger.Log("[CreateDummyTextures] Velocity texture created successfully");
        }

        Logger.Log(
            $"[CreateDummyTextures] Complete. TempView={_temperatureView != null}, VelView={_velocityView != null}");
    }

    private void InitializeResourceSet()
    {
        try
        {
            Logger.Log("[InitializeResourceSet] Starting...");
            _resourceSet?.Dispose();

            Logger.Log("[InitializeResourceSet] Checking resources:");
            Logger.Log($"  _uniformBuffer: {(_uniformBuffer != null ? "OK" : "NULL")}");
            Logger.Log($"  _colorMapView: {(_colorMapView != null ? "OK" : "NULL")}");
            Logger.Log($"  _linearSampler: {(_linearSampler != null ? "OK" : "NULL")}");
            Logger.Log($"  _temperatureView: {(_temperatureView != null ? "OK" : "NULL")}");
            Logger.Log($"  _velocityView: {(_velocityView != null ? "OK" : "NULL")}");
            Logger.Log($"  _pointSampler: {(_pointSampler != null ? "OK" : "NULL")}");

            if (_uniformBuffer == null) throw new InvalidOperationException("_uniformBuffer is null");
            if (_colorMapView == null) throw new InvalidOperationException("_colorMapView is null");
            if (_linearSampler == null) throw new InvalidOperationException("_linearSampler is null");
            if (_temperatureView == null) throw new InvalidOperationException("_temperatureView is null");
            if (_velocityView == null) throw new InvalidOperationException("_velocityView is null");
            if (_pointSampler == null) throw new InvalidOperationException("_pointSampler is null");

            Logger.Log("[InitializeResourceSet] Creating resource set...");
            _resourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
                _resourceLayout,
                _uniformBuffer, _colorMapView, _linearSampler,
                _temperatureView, _velocityView, _pointSampler
            ));

            Logger.Log($"[InitializeResourceSet] Success! ResourceSet created: {_resourceSet != null}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[InitializeResourceSet] FAILED: {ex.Message}");
            Logger.LogError($"[InitializeResourceSet] Stack: {ex.StackTrace}");
            throw;
        }
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
            PrimitiveTopology.LineList, thickLines: true);

        var isosurfaceShaders = CreateShaders("Isosurface");
        _isosurfacePipeline = CreatePipeline(isosurfaceShaders, BlendStateDescription.SingleAlphaBlend);

        var sliceShaders = CreateShaders("Slice");
        _slicePipeline = CreatePipeline(sliceShaders, BlendStateDescription.SingleAlphaBlend);

        // Create wireframe pipeline for line rendering with thick, visible lines
        var wireframeShaders = CreateShaders("Isosurface"); // Use basic shader
        _wireframePipeline = CreatePipeline(wireframeShaders, BlendStateDescription.SingleAlphaBlend,
            PrimitiveTopology.LineList, thickLines: true);
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
        PrimitiveTopology topology = PrimitiveTopology.TriangleList, bool thickLines = false)
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

        // For lines, disable face culling to make them more visible
        var rasterizerState = thickLines
            ? new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid,
                FrontFace.CounterClockwise, true, false)
            : new RasterizerStateDescription(FaceCullMode.Back, PolygonFillMode.Solid,
                FrontFace.CounterClockwise, true, false);

        var pipelineDescription = new GraphicsPipelineDescription
        {
            BlendState = blendState,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = rasterizerState,
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

        Logger.Log("[LoadResults] Loading visualization data...");
        Logger.Log($"  Temperature field: {(results?.FinalTemperatureField != null ? "OK" : "NULL")}");
        Logger.Log($"  Mesh: {(mesh != null ? $"{mesh.RadialPoints}x{mesh.AngularPoints}x{mesh.VerticalPoints}" : "NULL")}");
        
        if (results?.FinalTemperatureField == null || mesh == null)
        {
            Logger.LogWarning("[LoadResults] Missing required data - skipping visualization setup");
            return;
        }

        GenerateDomainAndBoreholeGpuMeshes();
        Logger.Log($"  Domain mesh: {_domainGpuMesh.IndexCount} indices");
        
        CreateDataTextures();
        CreateSliceQuad();
        UpdateResourceSet();
        
        Logger.Log("[LoadResults] Visualization setup complete");
    }

    private GpuMesh CreateGpuMesh(Mesh3DDataset mesh, bool isStreamline = false)
    {
        if (mesh.Vertices.Count == 0) return default;

        var vertices = new List<VertexData>();
        for (var i = 0; i < mesh.Vertices.Count; i++)
        {
            // Use mesh color if available, otherwise default gray or bright cyan for wireframes
            var color = i < mesh.Colors.Count
                ? mesh.Colors[i]
                :
                mesh.Faces.Any() && mesh.Faces.All(f => f.Length == 2)
                    ?
                    new Vector4(0.0f, 1.0f, 1.0f, 1.0f)
                    : // Cyan for wireframes
                    new Vector4(0.8f, 0.8f, 0.8f, 1.0f); // Gray for solid

            vertices.Add(new VertexData(
                mesh.Vertices[i],
                color,
                i < mesh.Normals.Count ? mesh.Normals[i] : Vector3.UnitZ,
                Vector2.Zero,
                0f,
                Vector3.Zero
            ));
        }

        var indices = new List<uint>();

        // Check if this is a wireframe mesh (has 2-vertex faces)
        var isWireframe = mesh.Faces.Any() && mesh.Faces.All(f => f.Length == 2);

        if (isStreamline || isWireframe) // For streamlines and wireframes, faces are pairs of indices for lines
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

        return new GpuMesh
        {
            VertexBuffer = vb,
            IndexBuffer = ib,
            IndexCount = (uint)indices.Count,
            Source = mesh,
            IsWireframe = isWireframe || isStreamline // Set flag for wireframe rendering
        };
    }

    private void GenerateDomainAndBoreholeGpuMeshes()
    {
        if (_mesh == null || _mesh.RadialPoints == 0) return;

        var vertices = new List<VertexData>();
        var indices = new List<uint>();
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;

        // PRODUCTION FIX: COMSOL-quality volume rendering
        // Create all vertices with proper UVW coordinates for 3D texture sampling
        for (var k = 0; k < nz; k++)
        for (var j = 0; j < nth; j++)
        for (var i = 0; i < nr; i++)
        {
            var r = _mesh.R[i];
            var theta = _mesh.Theta[j];
            var z = _mesh.Z[k];
            var position = new Vector3(r * MathF.Cos(theta), r * MathF.Sin(theta), z);
            var normal = new Vector3(MathF.Cos(theta), MathF.Sin(theta), 0);
            
            // UVW matches 3D texture layout [r, theta, z]
            var uvw = new Vector3(
                (float)i / Math.Max(1, nr - 1),    // U: radial [0,1]
                (float)j / Math.Max(1, nth - 1),   // V: angular [0,1]
                (float)k / Math.Max(1, nz - 1)     // W: vertical [0,1]
            );

            vertices.Add(new VertexData(position, Vector4.One, normal, Vector2.Zero, 0, uvw));
        }

        // ===== PRODUCTION VOLUME RENDERING STRATEGY =====
        // Generate DENSE mesh that fills the entire volume for proper visualization
        
        // 1. OUTER CYLINDRICAL SURFACE - Always visible boundary
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
        
        // 2. DENSE RADIAL SLICES - Every single angular position for complete volume fill
        // This creates a SOLID volume, not hollow slices
        for (var j = 0; j < nth; j++)  // ALL angular positions
        {
            for (var k = 0; k < nz - 1; k++)
            for (var i = 0; i < nr - 1; i++)
            {
                var v0 = (uint)(k * nth * nr + j * nr + i);
                var v1 = (uint)(k * nth * nr + j * nr + i + 1);
                var v2 = (uint)((k + 1) * nth * nr + j * nr + i);
                var v3 = (uint)((k + 1) * nth * nr + j * nr + i + 1);
                indices.AddRange(new[] { v0, v2, v1, v1, v2, v3 });
            }
        }

        // 3. DENSE HORIZONTAL SLICES - Many layers for full volume visualization
        var sliceStep = Math.Max(1, nz / 20);  // At least 20 horizontal slices
        for (var k = 0; k < nz; k += sliceStep)
        {
            for (var j = 0; j < nth; j++)
            for (var i = 0; i < nr - 1; i++)
            {
                var j_next = (j + 1) % nth;
                var v0 = (uint)(k * nth * nr + j * nr + i);
                var v1 = (uint)(k * nth * nr + j * nr + i + 1);
                var v2 = (uint)(k * nth * nr + j_next * nr + i);
                var v3 = (uint)(k * nth * nr + j_next * nr + i + 1);
                indices.AddRange(new[] { v0, v2, v1, v1, v2, v3 });
            }
        }
        
        // 4. TOP AND BOTTOM CAPS - Seal the volume
        // Top cap (k=0)
        for (var j = 0; j < nth; j++)
        for (var i = 0; i < nr - 1; i++)
        {
            var j_next = (j + 1) % nth;
            var v0 = (uint)(j * nr + i);
            var v1 = (uint)(j * nr + i + 1);
            var v2 = (uint)(j_next * nr + i);
            var v3 = (uint)(j_next * nr + i + 1);
            indices.AddRange(new[] { v0, v2, v1, v1, v2, v3 });
        }
        
        // Bottom cap (k=nz-1)
        var k_bottom = nz - 1;
        for (var j = 0; j < nth; j++)
        for (var i = 0; i < nr - 1; i++)
        {
            var j_next = (j + 1) % nth;
            var v0 = (uint)(k_bottom * nth * nr + j * nr + i);
            var v1 = (uint)(k_bottom * nth * nr + j * nr + i + 1);
            var v2 = (uint)(k_bottom * nth * nr + j_next * nr + i);
            var v3 = (uint)(k_bottom * nth * nr + j_next * nr + i + 1);
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

        // Generate heat exchanger geometry (U-tube or coaxial)
        GenerateHeatExchangerGeometry();
        
        if (_results.BoreholeMesh != null)
        {
            _boreholeGpuMesh.Dispose();
            _boreholeGpuMesh = CreateGpuMesh(_results.BoreholeMesh);
        }
    }
    
    /// <summary>
    /// Generate 3D geometry for heat exchanger inside borehole (U-tube or coaxial pipes)
    /// </summary>
    private void GenerateHeatExchangerGeometry()
    {
        if (_options == null || _mesh == null) return;
        
        var heatExchangerMesh = new Mesh3DDataset("HeatExchanger", Path.Combine(Path.GetTempPath(), "heatexchanger_temp.obj"));
        
        // Get borehole parameters
        var boreholeRadius = (float)(_options.BoreholeDataset?.WellDiameter / 2000.0 ?? 0.1); // mm to m
        var depth = Math.Abs(_mesh.Z[_mesh.VerticalPoints - 1] - _mesh.Z[0]);
        
        // Heat exchanger type from options
        var hxType = _options.HeatExchangerType;
        
        if (hxType == HeatExchangerType.UTube)
        {
            GenerateUTubeGeometry(heatExchangerMesh, boreholeRadius, depth, false);
        }
        else if (hxType == HeatExchangerType.Coaxial)
        {
            GenerateCoaxialGeometry(heatExchangerMesh, boreholeRadius, depth);
        }
        
        // Add flow arrows along pipes
        GenerateFlowArrows(heatExchangerMesh, boreholeRadius, depth, hxType.ToString());
        
        if (heatExchangerMesh.Vertices.Count > 0)
        {
            // Create GPU mesh for heat exchanger
            var hxGpuMesh = CreateGpuMesh(heatExchangerMesh);
            if (hxGpuMesh.VertexBuffer != null)
            {
                _dynamicGpuMeshes.Add(hxGpuMesh);
                Logger.Log($"Heat exchanger geometry generated: {heatExchangerMesh.Vertices.Count} vertices");
            }
        }
    }
    
    private void GenerateUTubeGeometry(Mesh3DDataset mesh, float boreholeRadius, float depth, bool isDouble)
    {
        // U-tube configuration: pipes go down and come back up
        var pipeRadius = boreholeRadius * 0.15f; // Pipe is ~15% of borehole radius
        var pipeSpacing = boreholeRadius * 0.5f; // Distance between pipes
        
        var numLoops = isDouble ? 2 : 1;
        var angleStep = 360f / numLoops;
        
        for (int loop = 0; loop < numLoops; loop++)
        {
            var angle = loop * angleStep * MathF.PI / 180f;
            var offsetX = pipeSpacing * MathF.Cos(angle);
            var offsetY = pipeSpacing * MathF.Sin(angle);
            
            // Down pipe
            GeneratePipeSegment(mesh, 
                new Vector3(offsetX, offsetY, _mesh.Z[0]), 
                new Vector3(offsetX, offsetY, _mesh.Z[_mesh.VerticalPoints - 1]),
                pipeRadius, new Vector4(0.2f, 0.4f, 1.0f, 1.0f)); // Blue for cold down
            
            // Up pipe (parallel to down pipe)
            var upOffsetX = -offsetX * 0.5f;
            var upOffsetY = -offsetY * 0.5f;
            GeneratePipeSegment(mesh,
                new Vector3(upOffsetX, upOffsetY, _mesh.Z[_mesh.VerticalPoints - 1]),
                new Vector3(upOffsetX, upOffsetY, _mesh.Z[0]),
                pipeRadius, new Vector4(1.0f, 0.3f, 0.2f, 1.0f)); // Red for warm up
            
            // U-bend at bottom
            GenerateUBend(mesh,
                new Vector3(offsetX, offsetY, _mesh.Z[_mesh.VerticalPoints - 1]),
                new Vector3(upOffsetX, upOffsetY, _mesh.Z[_mesh.VerticalPoints - 1]),
                pipeRadius, new Vector4(0.7f, 0.4f, 0.7f, 1.0f)); // Purple transition
        }
    }
    
    private void GenerateCoaxialGeometry(Mesh3DDataset mesh, float boreholeRadius, float depth)
    {
        // Coaxial: inner pipe (down) and annular space (up)
        var innerRadius = boreholeRadius * 0.25f;
        var outerRadius = boreholeRadius * 0.45f;
        
        // Inner pipe (downflow - cold)
        GeneratePipeSegment(mesh,
            new Vector3(0, 0, _mesh.Z[0]),
            new Vector3(0, 0, _mesh.Z[_mesh.VerticalPoints - 1]),
            innerRadius, new Vector4(0.2f, 0.4f, 1.0f, 1.0f));
        
        // Outer pipe (upflow - warm) - show as translucent shell
        GeneratePipeSegment(mesh,
            new Vector3(0, 0, _mesh.Z[_mesh.VerticalPoints - 1]),
            new Vector3(0, 0, _mesh.Z[0]),
            outerRadius, new Vector4(1.0f, 0.4f, 0.2f, 0.5f));
    }
    
    private void GeneratePipeSegment(Mesh3DDataset mesh, Vector3 start, Vector3 end, 
        float radius, Vector4 color)
    {
        var segments = 16; // Angular segments for pipe
        var lengthSegments = 32; // Vertical segments
        var startIdx = mesh.Vertices.Count;
        
        for (int i = 0; i <= lengthSegments; i++)
        {
            var t = (float)i / lengthSegments;
            var center = Vector3.Lerp(start, end, t);
            var direction = Vector3.Normalize(end - start);
            
            // Create perpendicular vectors for pipe cross-section
            var tangent = Math.Abs(direction.Z) < 0.99f ? Vector3.UnitZ : Vector3.UnitX;
            var binormal = Vector3.Normalize(Vector3.Cross(direction, tangent));
            tangent = Vector3.Cross(binormal, direction);
            
            for (int j = 0; j < segments; j++)
            {
                var angle = (float)j / segments * MathF.PI * 2f;
                var offset = tangent * MathF.Cos(angle) + binormal * MathF.Sin(angle);
                var pos = center + offset * radius;
                var normal = Vector3.Normalize(offset);
                
                mesh.Vertices.Add(pos);
                mesh.Normals.Add(normal);
                mesh.Colors.Add(color);
            }
        }
        
        // Generate faces
        for (int i = 0; i < lengthSegments; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                var v0 = startIdx + i * segments + j;
                var v1 = startIdx + i * segments + (j + 1) % segments;
                var v2 = startIdx + (i + 1) * segments + j;
                var v3 = startIdx + (i + 1) * segments + (j + 1) % segments;
                
                mesh.Faces.Add(new[] { v0, v2, v1 });
                mesh.Faces.Add(new[] { v1, v2, v3 });
            }
        }
    }
    
    private void GenerateUBend(Mesh3DDataset mesh, Vector3 start, Vector3 end, 
        float radius, Vector4 color)
    {
        // Simple curved connection at bottom of U-tube
        var segments = 16;
        var center = (start + end) * 0.5f;
        var bendRadius = Vector3.Distance(start, end) * 0.5f;
        
        var startIdx = mesh.Vertices.Count;
        for (int i = 0; i <= segments; i++)
        {
            var t = (float)i / segments;
            var angle = t * MathF.PI; // 180 degree bend
            var pos = center + new Vector3(
                (start.X - center.X) * MathF.Cos(angle) + (start.Y - center.Y) * MathF.Sin(angle),
                (start.Y - center.Y) * MathF.Cos(angle) - (start.X - center.X) * MathF.Sin(angle),
                start.Z
            );
            
            mesh.Vertices.Add(pos);
            mesh.Normals.Add(Vector3.UnitZ);
            mesh.Colors.Add(color);
        }
    }
    
    private void GenerateFlowArrows(Mesh3DDataset mesh, float boreholeRadius, 
        float depth, string hxType)
    {
        // Add velocity arrows along flow paths
        var arrowSize = boreholeRadius * 0.3f;
        var numArrows = 10;
        var pipeSpacing = boreholeRadius * 0.5f;
        
        for (int i = 0; i < numArrows; i++)
        {
            var t = (float)i / (numArrows - 1);
            var z = _mesh.Z[0] + t * (_mesh.Z[_mesh.VerticalPoints - 1] - _mesh.Z[0]);
            
            // Downflow arrow
            var downPos = new Vector3(pipeSpacing, 0, z);
            GenerateArrow(mesh, downPos, Vector3.UnitZ * -1, arrowSize, 
                new Vector4(0.3f, 0.5f, 1.0f, 1.0f));
            
            // Upflow arrow
            var upPos = new Vector3(-pipeSpacing * 0.5f, 0, z);
            GenerateArrow(mesh, upPos, Vector3.UnitZ, arrowSize,
                new Vector4(1.0f, 0.4f, 0.3f, 1.0f));
        }
    }
    
    private void GenerateArrow(Mesh3DDataset mesh, Vector3 position, Vector3 direction,
        float size, Vector4 color)
    {
        var startIdx = mesh.Vertices.Count;
        direction = Vector3.Normalize(direction);
        
        // Arrow shaft
        var shaftStart = position - direction * size * 0.5f;
        var shaftEnd = position + direction * size * 0.3f;
        
        // Arrow head (cone)
        var headBase = shaftEnd;
        var headTip = position + direction * size * 0.5f;
        var headRadius = size * 0.2f;
        
        // Simple arrow as line segments for now
        mesh.Vertices.Add(shaftStart);
        mesh.Colors.Add(color);
        mesh.Normals.Add(direction);
        
        mesh.Vertices.Add(headTip);
        mesh.Colors.Add(color);
        mesh.Normals.Add(direction);
        
        // Line face
        mesh.Faces.Add(new[] { startIdx, startIdx + 1 });
    }
    
    /// <summary>
    /// Generate velocity vector field glyphs throughout the domain
    /// </summary>
    public void GenerateVelocityVectorField(int gridDensity = 5)
    {
        if (_results?.DarcyVelocityField == null || _mesh == null)
        {
            Logger.LogWarning("No velocity field data available for vector visualization");
            return;
        }
        
        var vectorMesh = new Mesh3DDataset("VelocityVectors", Path.Combine(Path.GetTempPath(), "velocityvectors_temp.obj"));
        
        var nr = _mesh.RadialPoints;
        var nth = _mesh.AngularPoints;
        var nz = _mesh.VerticalPoints;
        
        // Sample velocity field at regular grid points
        var sampleR = Math.Max(1, nr / gridDensity);
        var sampleTheta = Math.Max(1, nth / gridDensity);
        var sampleZ = Math.Max(1, nz / gridDensity);
        
        // Find maximum velocity for scaling
        var maxVelocity = 0f;
        for (int i = 0; i < nr; i += sampleR)
        for (int j = 0; j < nth; j += sampleTheta)
        for (int k = 0; k < nz; k += sampleZ)
        {
            if (i >= nr || j >= nth || k >= nz) continue;
            var vx = _results.DarcyVelocityField[i, j, k, 0];
            var vy = _results.DarcyVelocityField[i, j, k, 1];
            var vz = _results.DarcyVelocityField[i, j, k, 2];
            var mag = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
            if (mag > maxVelocity) maxVelocity = mag;
        }
        
        if (maxVelocity < 1e-10f)
        {
            Logger.LogWarning("Velocity field is essentially zero");
            return;
        }
        
        var domainSize = (float)_options.DomainRadius;
        var arrowScale = domainSize * 0.05f / maxVelocity;
        
        Logger.Log($"Generating velocity vectors: max = {maxVelocity:E2} m/s");
        
        for (int i = 0; i < nr; i += sampleR)
        for (int j = 0; j < nth; j += sampleTheta)
        for (int k = 0; k < nz; k += sampleZ)
        {
            if (i >= nr || j >= nth || k >= nz) continue;
            
            var r = _mesh.R[i];
            var theta = _mesh.Theta[j];
            var z = _mesh.Z[k];
            
            var position = new Vector3(r * MathF.Cos(theta), r * MathF.Sin(theta), z);
            
            var vx = _results.DarcyVelocityField[i, j, k, 0];
            var vy = _results.DarcyVelocityField[i, j, k, 1];
            var vz = _results.DarcyVelocityField[i, j, k, 2];
            var velocity = new Vector3(vx, vy, vz);
            var magnitude = velocity.Length();
            
            if (magnitude < maxVelocity * 0.01f) continue;
            
            var colorT = magnitude / maxVelocity;
            var color = new Vector4(colorT, 0.3f, 1.0f - colorT, 0.8f);
            
            var arrowLength = magnitude * arrowScale;
            var direction = Vector3.Normalize(velocity);
            GenerateVelocityArrow(vectorMesh, position, direction, arrowLength, color);
        }
        
        Logger.Log($"Generated {vectorMesh.Vertices.Count} vertices for velocity field");
        
        if (vectorMesh.Vertices.Count > 0)
        {
            var gpuMesh = CreateGpuMesh(vectorMesh);
            if (gpuMesh.VertexBuffer != null)
            {
                _dynamicGpuMeshes.Add(gpuMesh);
            }
        }
    }
    
    private void GenerateVelocityArrow(Mesh3DDataset mesh, Vector3 position, 
        Vector3 direction, float length, Vector4 color)
    {
        var startIdx = mesh.Vertices.Count;
        
        var shaftLength = length * 0.7f;
        var shaftRadius = length * 0.02f;
        var segments = 6;
        
        var start = position;
        var shaftEnd = position + direction * shaftLength;
        var tip = position + direction * length;
        
        var tangent = Math.Abs(direction.Z) < 0.99f ? Vector3.UnitZ : Vector3.UnitX;
        var binormal = Vector3.Normalize(Vector3.Cross(direction, tangent));
        tangent = Vector3.Cross(binormal, direction);
        
        for (int i = 0; i < 2; i++)
        {
            var pos = i == 0 ? start : shaftEnd;
            for (int j = 0; j < segments; j++)
            {
                var angle = (float)j / segments * MathF.PI * 2f;
                var offset = tangent * MathF.Cos(angle) + binormal * MathF.Sin(angle);
                var vertex = pos + offset * shaftRadius;
                
                mesh.Vertices.Add(vertex);
                mesh.Normals.Add(Vector3.Normalize(offset));
                mesh.Colors.Add(color);
            }
        }
        
        for (int j = 0; j < segments; j++)
        {
            var v0 = startIdx + j;
            var v1 = startIdx + (j + 1) % segments;
            var v2 = startIdx + segments + j;
            var v3 = startIdx + segments + (j + 1) % segments;
            
            mesh.Faces.Add(new[] { v0, v2, v1 });
            mesh.Faces.Add(new[] { v1, v2, v3 });
        }
        
        var headRadius = length * 0.08f;
        var coneBaseIdx = mesh.Vertices.Count;
        
        for (int j = 0; j < segments; j++)
        {
            var angle = (float)j / segments * MathF.PI * 2f;
            var offset = tangent * MathF.Cos(angle) + binormal * MathF.Sin(angle);
            var vertex = shaftEnd + offset * headRadius;
            
            mesh.Vertices.Add(vertex);
            mesh.Normals.Add(direction);
            mesh.Colors.Add(color);
        }
        
        var tipIdx = mesh.Vertices.Count;
        mesh.Vertices.Add(tip);
        mesh.Normals.Add(direction);
        mesh.Colors.Add(color);
        
        for (int j = 0; j < segments; j++)
        {
            var v0 = coneBaseIdx + j;
            var v1 = coneBaseIdx + (j + 1) % segments;
            mesh.Faces.Add(new[] { v0, v1, tipIdx });
        }
    }

    private void CreateDataTextures()
    {
        if (_results?.FinalTemperatureField == null || _mesh == null) return;

        var nr = (uint)_mesh.RadialPoints;
        var nth = (uint)_mesh.AngularPoints;
        var nz = (uint)_mesh.VerticalPoints;

        Logger.Log($"[CreateDataTextures] Creating 3D textures: {nr}x{nth}x{nz} = {nr * nth * nz} voxels");

        // Safety check for texture size (limit to 512^3 = ~134M voxels)
        var totalVoxels = nr * nth * nz;
        if (totalVoxels > 134217728)
        {
            Logger.LogError(
                $"[CreateDataTextures] ERROR: Texture too large ({totalVoxels} voxels). Maximum is 134M voxels.");
            throw new InvalidOperationException($"3D texture size ({nr}x{nth}x{nz}) exceeds GPU limits");
        }

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
        Logger.Log("[CreateDataTextures] Temperature texture created and uploaded successfully");


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
            Logger.Log("[CreateDataTextures] Velocity texture created and uploaded successfully");
        }

        Logger.Log("[CreateDataTextures] Complete");
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
        // Ensure dummy textures exist
        CreateDummyTextures();

        // Recreate resource set with current textures
        InitializeResourceSet();
    }

    public void Render()
    {
        // Allow rendering in preview mode (without simulation results)
        var isPreviewMode = _results == null;

        var commandList = _graphicsDevice.ResourceFactory.CreateCommandList();
        commandList.Begin();
        commandList.SetFramebuffer(_framebuffer);
        commandList.ClearColorTarget(0, RgbaFloat.Black);
        commandList.ClearDepthStencil(1.0f);

        if (!isPreviewMode)
            UpdateUniforms();
        else
            // In preview mode, update uniforms with basic camera/lighting only
            UpdatePreviewUniforms();

        // CRITICAL FIX: Must set pipeline BEFORE setting resource sets
        // We'll set the resource set after each SetPipeline call

        if (!isPreviewMode)
        {
            // Full rendering mode with simulation results
            // Render domain mesh
            // SURGICAL FIX 11: Add diagnostics for domain mesh rendering
            if (_showDomainMesh && _domainGpuMesh.VertexBuffer != null && _domainGpuMesh.IndexCount > 0)
            {
                // CORRECTED: Use a switch statement to select the pipeline
                Pipeline pipeline;
                switch (_renderMode)
                {
                    case RenderMode.Velocity:
                        pipeline = _velocityPipeline;
                        break;
                    case RenderMode.Pressure:
                        // SURGICAL FIX 9: Use temperature pipeline for pressure (reuses temperature shader)
                        pipeline = _temperaturePipeline;
                        break;
                    case RenderMode.Temperature:
                    default:
                        pipeline = _temperaturePipeline;
                        break;
                }

                // The domain mesh should ONLY be skipped for these specific modes
                if (_renderMode == RenderMode.Slices || _renderMode == RenderMode.Isosurface ||
                    _renderMode == RenderMode.Streamlines)
                {
                    // Skip domain mesh rendering - these modes use their own geometry
                }
                else
                {
                    // SURGICAL FIX 9b: ALWAYS render domain for Temperature/Velocity/Pressure modes
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
        }
        else
        {
            // Preview mode: render only dynamic meshes (preview meshes)
            if (_dynamicGpuMeshes.Any())
            {
                // Group meshes by rendering type
                var wireframeMeshes = _dynamicGpuMeshes.Where(m => m.IsWireframe).ToList();
                var solidMeshes = _dynamicGpuMeshes.Where(m => !m.IsWireframe).ToList();

                // Render wireframe meshes with line topology
                if (wireframeMeshes.Any())
                {
                    commandList.SetPipeline(_wireframePipeline);
                    commandList.SetGraphicsResourceSet(0, _resourceSet);

                    foreach (var gpuMesh in wireframeMeshes)
                    {
                        if (gpuMesh.VertexBuffer == null) continue;
                        commandList.SetVertexBuffer(0, gpuMesh.VertexBuffer);
                        commandList.SetIndexBuffer(gpuMesh.IndexBuffer, IndexFormat.UInt32);
                        commandList.DrawIndexed(gpuMesh.IndexCount);
                    }
                }

                // Render solid meshes with triangle topology
                if (solidMeshes.Any())
                {
                    commandList.SetPipeline(_isosurfacePipeline);
                    commandList.SetGraphicsResourceSet(0, _resourceSet);

                    foreach (var gpuMesh in solidMeshes)
                    {
                        if (gpuMesh.VertexBuffer == null) continue;
                        commandList.SetVertexBuffer(0, gpuMesh.VertexBuffer);
                        commandList.SetIndexBuffer(gpuMesh.IndexBuffer, IndexFormat.UInt32);
                        commandList.DrawIndexed(gpuMesh.IndexCount);
                    }
                }
            }
        }

        commandList.End();
        _graphicsDevice.SubmitCommands(commandList);
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
            DomainInfo = new Vector4((float)_options.DomainRadius, _mesh.Z[0], _mesh.Z.Last() - _mesh.Z[0], 0),
            ClipPlane = new Vector4(
                _clipAxis,
                _clipPosition,
                _clipNegativeSide ? 1f : 0f,
                _enableClipping ? 1f : 0f
            )
        };

        _graphicsDevice.UpdateBuffer(_uniformBuffer, 0, ref uniforms);
    }

    public void SetCameraDistance(float d)
    {
        _cameraDistance = Math.Clamp(d, 5f, 20000f);
        UpdateCamera();
    }

    private void UpdatePreviewUniforms()
    {
        // Simplified uniforms for preview mode (without simulation results)
        // Use stored preview options if available for proper domain info
        var domainRadius = (float)(_previewOptions?.DomainRadius ?? 100f);
        var boreholeDepth = _previewBoreholeDepth > 0 ? _previewBoreholeDepth : 100f;
        var domainExtension = (float)(_previewOptions?.DomainExtension ?? 10f);

        var uniforms = new UniformData
        {
            ViewMatrix = _viewMatrix,
            ProjectionMatrix = _projectionMatrix,
            ModelMatrix = Matrix4x4.Identity,
            LightDirection = Vector4.Normalize(new Vector4(-1, -1, -2, 0)),
            ViewPosition = new Vector4(_cameraPosition, 1),
            ColorMapRange = new Vector4(0, 100, 0.001f, 0),
            SliceInfo = new Vector4(0, 0, 0, 0),
            RenderSettings = new Vector4(0, 1.0f, 0, 293.15f), // opacity = 1.0
            DomainInfo = new Vector4(domainRadius, -(boreholeDepth + domainExtension),
                boreholeDepth + domainExtension * 2, 0),
            ClipPlane = Vector4.Zero // No clipping in preview mode
        };

        _graphicsDevice.UpdateBuffer(_uniformBuffer, 0, ref uniforms);
    }

    public void FrameDetailView(float domainRadius, float totalDepth)
    {
        // Put the camera close enough to see small features but far enough to keep the boundary in frame.
        // Use ~20–30% of the domain radius as viewing distance, clamped to a sane range.
        var target = Math.Clamp(0.25f * domainRadius, 8f, 2000f);
        _cameraDistance = target;

        // Look at the borehole center (assumed at origin) around mid-depth.
        _cameraTarget = new Vector3(0, 0, -0.5f * totalDepth);

        UpdateCamera();
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

        // --- dynamic near/far based on preview or results ---
        var domainRadius =
            (float)(_options?.DomainRadius
                    ?? _previewOptions?.DomainRadius
                    ?? 50f);

        // Use simulation mesh extent if present, otherwise preview depths
        float zMin, zMax;
        if (_mesh?.Z != null && _mesh.Z.Length > 0)
        {
            zMin = _mesh.Z[0];
            zMax = _mesh.Z[^1];
        }
        else
        {
            var ext = (float)(_previewOptions?.DomainExtension ?? 0f);
            zMin = -(_previewBoreholeDepth + ext);
            zMax = +ext;
        }

        var depthExtent = MathF.Abs(zMax - zMin);
        var sceneExtent = MathF.Max(2f * domainRadius, depthExtent);

        var aspect = (float)_renderWidth / Math.Max(1, _renderHeight);

        // Near as a tiny fraction of camera distance (avoid z-fighting when zoomed in)
        var near = MathF.Max(0.01f, _cameraDistance * 0.001f);
        // Far far enough to cover radius + depth comfortably
        var far = MathF.Max(2000f, _cameraDistance + 3.0f * sceneExtent);

        _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            45f * MathF.PI / 180f, aspect, near, far);
    }


    /// <summary>
    /// Reset camera to show the full simulation domain
    /// </summary>
    public void ResetCameraToFullView()
    {
        // Use simulation results if available, otherwise preview settings
        var domainRadius = (float)(_options?.DomainRadius ?? _previewOptions?.DomainRadius ?? 50f);
        var totalDepth = _mesh?.Z != null && _mesh.Z.Length > 0 
            ? Math.Abs(_mesh.Z[^1] - _mesh.Z[0])
            : (_previewBoreholeDepth + (float)(_previewOptions?.DomainExtension ?? 10f) * 2);
        
        // Set camera distance to see full domain
        _cameraDistance = Math.Max(domainRadius * 1.5f, totalDepth * 0.75f);
        _cameraDistance = Math.Clamp(_cameraDistance, 10f, 2000f);
        
        // Look at center of domain
        _cameraTarget = new Vector3(0, 0, -totalDepth * 0.5f);
        
        // Reset angles to default
        _cameraAzimuth = 45f;
        _cameraElevation = 30f;
        
        UpdateCamera();
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
                    UpdateResourceSet(); // FIX: Re-create the resource set with the new colormap texture
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
        
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "Advanced Visualization:");
        
        if (ImGui.Button("Show Heat Exchanger", new Vector2(-1, 0)))
        {
            ClearDynamicMeshes();
            GenerateHeatExchangerGeometry();
            Logger.Log("Heat exchanger visualization added");
        }
        ImGui.SetItemTooltip("Display U-tube or coaxial heat exchanger inside borehole");
        
        if (_options?.SimulateGroundwaterFlow == true && _results?.DarcyVelocityField != null)
        {
            ImGui.Spacing();
            if (ImGui.Button("Show Velocity Vectors", new Vector2(-1, 0)))
            {
                ClearDynamicMeshes();
                GenerateVelocityVectorField(5);
                Logger.Log("Velocity vector field generated");
            }
            ImGui.SetItemTooltip("Display 3D velocity field as arrows (shows groundwater flow)");
            
            if (ImGui.Button("Show Combined View", new Vector2(-1, 0)))
            {
                ClearDynamicMeshes();
                GenerateHeatExchangerGeometry();
                GenerateVelocityVectorField(6);
                _showDomainMesh = true;
                Logger.Log("Combined visualization: domain + heat exchanger + velocity");
            }
            ImGui.SetItemTooltip("Show everything: temperature field + heat exchanger + flow vectors");
        }

        if (_renderMode == RenderMode.Isosurface)
            ImGui.SliderFloat("Iso Value (°C)", ref _isoValue, _temperatureMin, _temperatureMax);

        if (_renderMode == RenderMode.Slices)
            ImGui.SliderFloat("Slice Depth (0=top, 1=bottom)", ref _sliceDepth, 0f, 1f);

        // SURGICAL FIX 6: Add camera controls
        ImGui.Separator();
        ImGui.Text("Camera Controls");
        
        var camDist = _cameraDistance;
        if (ImGui.SliderFloat("Zoom", ref camDist, 5f, 2000f, "%.0f", ImGuiSliderFlags.Logarithmic))
        {
            SetCameraDistance(camDist);
        }
        ImGui.SetItemTooltip("Mouse wheel also controls zoom");
        
        var camAz = _cameraAzimuth;
        var camEl = _cameraElevation;
        if (ImGui.SliderFloat("Azimuth", ref camAz, 0f, 360f))
        {
            _cameraAzimuth = camAz;
            UpdateCamera();
        }
        if (ImGui.SliderFloat("Elevation", ref camEl, -89f, 89f))
        {
            _cameraElevation = camEl;
            UpdateCamera();
        }
        
        ImGui.Spacing();
        if (ImGui.Button("Focus on Borehole", new Vector2(-1, 0)))
        {
            // Zoom to borehole region
            var boreholeRadius = _options?.BoreholeDataset?.WellDiameter / 2000.0 ?? 0.1; // Convert mm to m
            _cameraDistance = Math.Max(1f, (float)boreholeRadius * 5f); // 5x borehole diameter
            _cameraTarget = Vector3.Zero; // Center on borehole
            UpdateCamera();
            Logger.Log($"Focused on borehole (radius: {boreholeRadius:F3} m, distance: {_cameraDistance:F1} m)");
        }
        ImGui.SetItemTooltip("Zoom close to the borehole to see local temperature changes");
        
        if (ImGui.Button("View Full Domain", new Vector2(-1, 0)))
        {
            // Reset to full view
            ResetCameraToFullView();
            Logger.Log("Reset camera to full domain view");
        }
        ImGui.SetItemTooltip("Reset camera to show entire simulation domain");
        
        ImGui.Separator();
        ImGui.Text("Volume Clipping");
        ImGui.Checkbox("Enable Clipping Plane", ref _enableClipping);
        if (_enableClipping)
        {
            var axisNames = new[] { "X (Radial)", "Y (Radial)", "Z (Depth)" };
            if (ImGui.Combo("Clip Axis", ref _clipAxis, axisNames, axisNames.Length))
            {
                // Axis changed
            }
            ImGui.SliderFloat("Clip Position", ref _clipPosition, 0f, 1f);
            ImGui.Checkbox("Clip Negative Side", ref _clipNegativeSide);
            ImGui.SetItemTooltip(_clipNegativeSide ? "Showing positive side" : "Showing negative side");
            
            // Preset buttons
            ImGui.Spacing();
            if (ImGui.Button("Clip to Borehole"))
            {
                _clipAxis = 0; // X axis
                _clipPosition = 0.15f; // Show inner 15% radially
                _clipNegativeSide = false; // Show negative side (inner region)
                _enableClipping = true;
            }
            ImGui.SetItemTooltip("Show only the region near the borehole");
            
            ImGui.SameLine();
            if (ImGui.Button("Half Domain"))
            {
                _clipAxis = 1; // Y axis
                _clipPosition = 0.5f;
                _clipNegativeSide = true;
            }
            ImGui.SetItemTooltip("Cut the domain in half to see interior");
        }

        ImGui.Separator();
        ImGui.Text("Camera");
        if (ImGui.SliderFloat("Distance", ref _cameraDistance, 10f, 500f)) UpdateCamera();
        if (ImGui.SliderFloat("Azimuth", ref _cameraAzimuth, -180f, 180f)) UpdateCamera();
        if (ImGui.SliderFloat("Elevation", ref _cameraElevation, -89f, 89f)) UpdateCamera();

        if (ImGui.Button("Reset Camera"))
        {
            _cameraDistance = 300f;
            _cameraAzimuth = 45f;
            _cameraElevation = 45f;
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

    public void StartRotation(Vector2 mousePos)
    {
        _isRotating = true;
        _lastMousePos = mousePos;
    }

    public void StopRotation()
    {
        _isRotating = false;
    }

    public void UpdateRotation(Vector2 mousePos)
    {
        if (!_isRotating) return;

        var delta = mousePos - _lastMousePos;
        _cameraAzimuth += delta.X * 0.5f;
        _cameraElevation = Math.Clamp(_cameraElevation - delta.Y * 0.5f, -89f, 89f);
        _lastMousePos = mousePos;
        UpdateCamera();
    }

    public void StartPanning(Vector2 mousePos)
    {
        _isPanning = true;
        _lastMousePos = mousePos;
    }

    public void StopPanning()
    {
        _isPanning = false;
    }

    public void UpdatePanning(Vector2 mousePos)
    {
        if (!_isPanning) return;

        var delta = mousePos - _lastMousePos;
        var right = Vector3.Normalize(Vector3.Cross(_cameraPosition - _cameraTarget, _cameraUp));
        var up = Vector3.Cross(right, Vector3.Normalize(_cameraPosition - _cameraTarget));

        var panSpeed = _cameraDistance * 0.001f;
        _cameraTarget += right * (-delta.X * panSpeed) + up * (delta.Y * panSpeed);
        _lastMousePos = mousePos;
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
        
        // SURGICAL FIX 7+9c: Ensure domain mesh visibility based on render mode
        // Temperature, Velocity, and Pressure modes REQUIRE the domain mesh
        if (mode == RenderMode.Temperature || mode == RenderMode.Velocity || mode == RenderMode.Pressure)
        {
            _showDomainMesh = true;
            Logger.Log($"Switched to {mode} mode - domain mesh enabled");
        }
        // Isosurface and Streamline modes typically hide the domain
        else if (mode == RenderMode.Isosurface || mode == RenderMode.Streamlines)
        {
            // Keep user's preference, don't force change
            Logger.Log($"Switched to {mode} mode");
        }
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

    /// <summary>
    ///     Sets preview options for proper domain information rendering in preview mode.
    /// </summary>
    public void SetPreviewOptions(GeothermalSimulationOptions options, float boreholeDepth)
    {
        _previewOptions = options;
        _previewBoreholeDepth = boreholeDepth;

        // Center camera target on the borehole depth
        _cameraTarget = new Vector3(0, 0, -boreholeDepth / 2f);
        UpdateCamera();
    }

    private void InitializeColorMaps()
    {
        try
        {
            Logger.Log("[InitializeColorMaps] Starting...");
            _colorMapTexture?.Dispose();
            _colorMapView?.Dispose();

            Logger.Log("[InitializeColorMaps] Generating color map data...");
            var colorMapData = GenerateColorMapData(_currentColorMap);

            Logger.Log($"[InitializeColorMaps] Creating Texture1D with {colorMapData.Length} bytes...");
            _colorMapTexture = _factory.CreateTexture(TextureDescription.Texture1D(
                256, 1, 1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled));

            if (_colorMapTexture == null) throw new InvalidOperationException("Failed to create color map texture");

            Logger.Log("[InitializeColorMaps] Updating texture data...");
            _graphicsDevice.UpdateTexture(_colorMapTexture, colorMapData, 0, 0, 0, 256, 1, 1, 0, 0);

            Logger.Log("[InitializeColorMaps] Creating texture view...");
            _colorMapView = _factory.CreateTextureView(_colorMapTexture);

            if (_colorMapView == null) throw new InvalidOperationException("Failed to create color map texture view");

            Logger.Log(
                $"[InitializeColorMaps] Success! Texture={_colorMapTexture != null}, View={_colorMapView != null}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[InitializeColorMaps] FAILED: {ex.Message}");
            throw;
        }
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
    vec4 ColorMapRange; vec4 SliceInfo; vec4 RenderSettings; vec4 DomainInfo; vec4 ClipPlane;
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
    mat4 v; mat4 p; mat4 m; vec4 LightDirection; vec4 ViewPosition; vec4 ColorMapRange; vec4 si; vec4 RenderSettings; vec4 di; vec4 ClipPlane;
} ubo;
layout(set = 0, binding = 1) uniform texture1D ColorMap; layout(set = 0, binding = 2) uniform sampler ColorMapSampler;
layout(set = 0, binding = 3) uniform texture3D TemperatureData; layout(set = 0, binding = 5) uniform sampler DataSampler;
void main() {
    // SURGICAL FIX 5: Improved clipping plane with proper coordinate handling
    if (ubo.ClipPlane.w > 0.5) {
        int axis = int(ubo.ClipPlane.x);
        float position = ubo.ClipPlane.y;
        float side = ubo.ClipPlane.z;
        float coord = (axis == 0) ? frag_UVW.x : ((axis == 1) ? frag_UVW.y : frag_UVW.z);
        if (side > 0.5) { if (coord < position) discard; }
        else { if (coord > position) discard; }
    }
    
    // Sample temperature from 3D texture
    float temp_K = texture(sampler3D(TemperatureData, DataSampler), frag_UVW).r;
    float temp_C = temp_K - 273.15;
    
    // SURGICAL FIX 5b: ColorMapRange.x is min, ColorMapRange.y is RANGE (max-min)
    float tempMin = ubo.ColorMapRange.x;
    float tempRange = max(ubo.ColorMapRange.y, 0.001);  // Avoid division by zero
    float t = clamp((temp_C - tempMin) / tempRange, 0.0, 1.0);
    
    vec4 color = texture(sampler1D(ColorMap, ColorMapSampler), t);
    vec3 normal = normalize(frag_Normal);
    float diffuse = max(dot(normal, normalize(ubo.LightDirection.xyz)), 0.4);
    FragColor = vec4(color.rgb * (diffuse + 0.3), ubo.RenderSettings.y);
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
    mat4 v; mat4 p; mat4 m; vec4 LightDirection; vec4 ViewPosition; vec4 ColorMapRange; vec4 si; vec4 RenderSettings; vec4 di; vec4 ClipPlane;
} ubo;
void main() {
    vec3 normal = normalize(frag_Normal);
    float diffuse = max(dot(normal, normalize(ubo.LightDirection.xyz)), 0.3);
    vec3 viewDir = normalize(ubo.ViewPosition.xyz - frag_WorldPos);
    vec3 reflectDir = reflect(-normalize(ubo.LightDirection.xyz), normal);
    float specular = pow(max(dot(viewDir, reflectDir), 0.0), 64) * 0.1; // Reduced from 0.5 to 0.1, sharper highlight
    vec3 finalColor = frag_Color.rgb * (diffuse + 0.25) + vec3(1.0) * specular;
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
    mat4 v; mat4 p; mat4 m; vec4 LightDirection; vec4 ViewPosition; vec4 ColorMapRange; vec4 si; vec4 RenderSettings; vec4 di; vec4 ClipPlane;
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
    mat4 v; mat4 p; mat4 m; vec4 LightDirection; vec4 ViewPosition; vec4 ColorMapRange; vec4 SliceInfo; vec4 RenderSettings; vec4 DomainInfo; vec4 ClipPlane;
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
    
    // SURGICAL FIX 8: Proper normalization using range (same as temperature shader)
    float tempMin = ubo.ColorMapRange.x;
    float tempRange = max(ubo.ColorMapRange.y, 0.001);
    float t = clamp((temp_C - tempMin) / tempRange, 0.0, 1.0);
    
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
    mat4 v; mat4 p; mat4 m; vec4 LightDirection; vec4 ViewPosition; vec4 ColorMapRange; vec4 si; vec4 RenderSettings; vec4 di; vec4 ClipPlane;
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
        public bool IsWireframe; // Flag to indicate if this should be rendered as lines

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
        public Vector4 ClipPlane; // x=axis(0-2), y=position(0-1), z=side(0/1), w=enabled(0/1)
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