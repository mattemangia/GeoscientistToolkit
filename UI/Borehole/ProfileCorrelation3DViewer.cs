// GeoscientistToolkit/UI/Borehole/ProfileCorrelation3DViewer.cs

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using Veldrid.SPIRV;

namespace GeoscientistToolkit.UI.Borehole;

/// <summary>
/// 3D viewer for multi-profile borehole correlation with interpolated surfaces.
/// Displays correlation profiles, cross-profile correlations, and interpolated horizons.
/// </summary>
public class ProfileCorrelation3DViewer : IDisposable
{
    #region GPU Structures

    [StructLayout(LayoutKind.Sequential)]
    private struct VertexData
    {
        public Vector3 Position;
        public Vector4 Color;
        public Vector3 Normal;
        public Vector2 TexCoord;
        public float Value;
        public Vector3 UVW;

        public VertexData(Vector3 position, Vector4 color, Vector3 normal = default,
            Vector2 texCoord = default, float value = 0, Vector3 uvw = default)
        {
            Position = position;
            Color = color;
            Normal = normal == default ? Vector3.UnitZ : normal;
            TexCoord = texCoord;
            Value = value;
            UVW = uvw;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UniformData
    {
        public Matrix4x4 ModelViewProjection;
        public Matrix4x4 Model;
        public Matrix4x4 View;
        public Matrix4x4 Projection;
        public Vector4 LightPosition;
        public Vector4 CameraPosition;
        public float Opacity;
        public float Time;
        public int RenderMode;
        public int Padding;
    }

    private struct GpuMesh : IDisposable
    {
        public DeviceBuffer VertexBuffer;
        public DeviceBuffer IndexBuffer;
        public uint IndexCount;
        public bool IsLineList;

        public void Dispose()
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
        }
    }

    #endregion

    // Data
    private readonly MultiProfileCorrelationDataset _correlationData;
    private readonly List<BoreholeDataset> _boreholes = new();
    private readonly Dictionary<string, BoreholeDataset> _boreholeMap = new();

    // GPU Resources
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ResourceFactory _factory;
    private Framebuffer _framebuffer;
    private Texture _renderTarget;
    private TextureView _renderTargetView;
    private Texture _depthTexture;
    private uint _renderWidth = 900;
    private uint _renderHeight = 650;

    private Pipeline _solidPipeline;
    private Pipeline _wireframePipeline;
    private Pipeline _linePipeline;
    private ResourceLayout _resourceLayout;
    private ResourceSet _resourceSet;
    private DeviceBuffer _uniformBuffer;

    // Meshes
    private GpuMesh _topographyMesh;
    private List<GpuMesh> _boreholeMeshes = new();
    private List<GpuMesh> _lithologyMeshes = new();
    private List<GpuMesh> _profileLineMeshes = new();
    private GpuMesh _intraCorrelationLinesMesh;
    private GpuMesh _crossCorrelationLinesMesh;
    private GpuMesh _intersectionMarkersMesh;
    private List<GpuMesh> _horizonSurfaceMeshes = new();

    // Camera
    private Vector3 _cameraTarget = Vector3.Zero;
    private float _cameraAzimuth = 45f;
    private float _cameraElevation = 35f;
    private float _cameraDistance = 500f;
    private Matrix4x4 _viewMatrix;
    private Matrix4x4 _projectionMatrix;

    // Input
    private bool _isRotating;
    private bool _isPanning;
    private Vector2 _lastMousePos;

    // Display options
    private bool _showTopography = true;
    private bool _showBoreholes = true;
    private bool _showLithology = true;
    private bool _showProfileLines = true;
    private bool _showIntraCorrelations = true;
    private bool _showCrossCorrelations = true;
    private bool _showIntersections = true;
    private bool _showHorizonSurfaces = true;
    private bool _showWireframe;
    private float _verticalExaggeration = 1.0f;
    private float _boreholeRadius = 2f;
    private float _profileLineWidth = 4f;
    private float _correlationLineWidth = 2f;
    private float _horizonOpacity = 0.6f;

    // DEM/Topography
    private GISRasterLayer _demLayer;
    private string _demDatasetName;
    private float _demElevationOffset;

    // Selection
    private string _selectedProfileID;
    private string _selectedHorizonID;
    private int _hoveredBoreholeIndex = -1;

    // UI State
    private bool _isOpen = true;
    private string _statusMessage = "";
    private bool _showProfilePanel = true;
    private bool _showHorizonPanel = true;

    // Colors
    private readonly Vector4 _topographyColor = new(0.4f, 0.6f, 0.3f, 0.7f);
    private readonly Vector4 _boreholeColor = new(0.3f, 0.3f, 0.3f, 1f);
    private readonly Vector4 _intersectionMarkerColor = new(1f, 0.8f, 0.2f, 1f);

    public bool IsOpen => _isOpen;

    public event Action OnClose;
    public event Action<string> OnProfileSelected;
    public event Action<string> OnHorizonSelected;

    public ProfileCorrelation3DViewer(
        GraphicsDevice graphicsDevice,
        MultiProfileCorrelationDataset correlationData,
        List<BoreholeDataset> boreholes)
    {
        _graphicsDevice = graphicsDevice;
        _factory = graphicsDevice.ResourceFactory;
        _correlationData = correlationData;

        foreach (var borehole in boreholes)
        {
            var id = borehole.FilePath ?? borehole.WellName ?? borehole.GetHashCode().ToString();
            _boreholes.Add(borehole);
            _boreholeMap[id] = borehole;
        }

        InitializeResources();
        BuildAllMeshes();
        UpdateCamera();

        Logger.Log($"[ProfileCorrelation3DViewer] Initialized with {_boreholes.Count} boreholes, " +
                   $"{_correlationData.Profiles.Count} profiles");
    }

    private void InitializeResources()
    {
        CreateRenderTarget(_renderWidth, _renderHeight);
        CreatePipelines();
        CreateUniformBuffer();
        CreateResourceSet();
    }

    private void CreateRenderTarget(uint width, uint height)
    {
        _renderTarget?.Dispose();
        _renderTargetView?.Dispose();
        _depthTexture?.Dispose();
        _framebuffer?.Dispose();

        _renderWidth = width;
        _renderHeight = height;

        _renderTarget = _factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm,
            TextureUsage.RenderTarget | TextureUsage.Sampled));
        _renderTargetView = _factory.CreateTextureView(_renderTarget);

        var depthFormat = _graphicsDevice.BackendType == GraphicsBackend.Direct3D11
            ? PixelFormat.D24_UNorm_S8_UInt
            : PixelFormat.D32_Float_S8_UInt;

        _depthTexture = _factory.CreateTexture(TextureDescription.Texture2D(
            width, height, 1, 1, depthFormat, TextureUsage.DepthStencil));

        _framebuffer = _factory.CreateFramebuffer(new FramebufferDescription(_depthTexture, _renderTarget));
    }

    private void CreatePipelines()
    {
        var vertexCode = GetVertexShaderCode();
        var fragmentCode = GetFragmentShaderCode();

        var vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexCode), "main");
        var fragmentShaderDesc = new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentCode), "main");
        var shaders = _factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Value", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1),
            new VertexElementDescription("UVW", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));

        _resourceLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("UniformData", ResourceKind.UniformBuffer,
                ShaderStages.Vertex | ShaderStages.Fragment)));

        // Solid pipeline for filled geometry
        _solidPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _resourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { shaders[0], shaders[1] }),
            Outputs = _framebuffer.OutputDescription
        });

        // Line pipeline
        _linePipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.LineList,
            ResourceLayouts = new[] { _resourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { shaders[0], shaders[1] }),
            Outputs = _framebuffer.OutputDescription
        });

        // Wireframe pipeline
        _wireframePipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None, PolygonFillMode.Wireframe, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _resourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { shaders[0], shaders[1] }),
            Outputs = _framebuffer.OutputDescription
        });
    }

    private void CreateUniformBuffer()
    {
        _uniformBuffer = _factory.CreateBuffer(new BufferDescription(
            (uint)Marshal.SizeOf<UniformData>(),
            BufferUsage.UniformBuffer | BufferUsage.Dynamic));
    }

    private void CreateResourceSet()
    {
        _resourceSet?.Dispose();
        _resourceSet = _factory.CreateResourceSet(new ResourceSetDescription(
            _resourceLayout, _uniformBuffer));
    }

    #region Mesh Building

    public void BuildAllMeshes()
    {
        BuildBoreholeMeshes();
        BuildProfileLineMeshes();
        BuildCorrelationLinesMeshes();
        BuildIntersectionMarkersMesh();
        BuildHorizonSurfaceMeshes();

        if (_demLayer != null)
            BuildTopographyMesh();

        CalculateCameraTargetFromBoreholes();
    }

    private void CalculateCameraTargetFromBoreholes()
    {
        if (_boreholes.Count == 0) return;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var borehole in _boreholes)
        {
            var x = borehole.SurfaceCoordinates.X;
            var y = borehole.SurfaceCoordinates.Y;
            var zTop = borehole.Elevation;
            var zBottom = borehole.Elevation - borehole.TotalDepth;

            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
            maxZ = Math.Max(maxZ, zTop);
            minZ = Math.Min(minZ, zBottom);
        }

        _cameraTarget = new Vector3(
            (minX + maxX) / 2,
            (minY + maxY) / 2,
            (minZ + maxZ) / 2);

        _cameraDistance = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)) * 1.5f;
        _cameraDistance = Math.Max(_cameraDistance, 100f);
    }

    private void BuildBoreholeMeshes()
    {
        foreach (var mesh in _boreholeMeshes)
            mesh.Dispose();
        _boreholeMeshes.Clear();

        foreach (var mesh in _lithologyMeshes)
            mesh.Dispose();
        _lithologyMeshes.Clear();

        foreach (var borehole in _boreholes)
        {
            var casingMesh = BuildBoreholeCasingMesh(borehole);
            if (casingMesh.IndexCount > 0)
                _boreholeMeshes.Add(casingMesh);

            var lithMesh = BuildBoreholeLithologyMesh(borehole);
            if (lithMesh.IndexCount > 0)
                _lithologyMeshes.Add(lithMesh);
        }
    }

    private GpuMesh BuildBoreholeCasingMesh(BoreholeDataset borehole)
    {
        var vertices = new List<VertexData>();
        var indices = new List<uint>();

        var x = borehole.SurfaceCoordinates.X;
        var y = borehole.SurfaceCoordinates.Y;
        var topZ = borehole.Elevation * _verticalExaggeration;
        var bottomZ = (borehole.Elevation - borehole.TotalDepth) * _verticalExaggeration;
        var radius = _boreholeRadius;

        int segments = 12;
        for (int i = 0; i < segments; i++)
        {
            float angle1 = i * MathF.PI * 2 / segments;
            float angle2 = (i + 1) * MathF.PI * 2 / segments;

            var p1Top = new Vector3(x + MathF.Cos(angle1) * radius, y + MathF.Sin(angle1) * radius, topZ);
            var p2Top = new Vector3(x + MathF.Cos(angle2) * radius, y + MathF.Sin(angle2) * radius, topZ);
            var p1Bottom = new Vector3(x + MathF.Cos(angle1) * radius, y + MathF.Sin(angle1) * radius, bottomZ);
            var p2Bottom = new Vector3(x + MathF.Cos(angle2) * radius, y + MathF.Sin(angle2) * radius, bottomZ);

            var normal = Vector3.Normalize(new Vector3(MathF.Cos(angle1), MathF.Sin(angle1), 0));

            var baseIdx = (uint)vertices.Count;
            vertices.Add(new VertexData(p1Top, _boreholeColor, normal));
            vertices.Add(new VertexData(p2Top, _boreholeColor, normal));
            vertices.Add(new VertexData(p1Bottom, _boreholeColor, normal));
            vertices.Add(new VertexData(p2Bottom, _boreholeColor, normal));

            indices.AddRange(new uint[] { baseIdx, baseIdx + 1, baseIdx + 2 });
            indices.AddRange(new uint[] { baseIdx + 1, baseIdx + 3, baseIdx + 2 });
        }

        return CreateGpuMesh(vertices, indices, false);
    }

    private GpuMesh BuildBoreholeLithologyMesh(BoreholeDataset borehole)
    {
        var vertices = new List<VertexData>();
        var indices = new List<uint>();

        var x = borehole.SurfaceCoordinates.X;
        var y = borehole.SurfaceCoordinates.Y;
        var surfaceElev = borehole.Elevation;
        var radius = _boreholeRadius * 0.8f;

        foreach (var unit in borehole.LithologyUnits)
        {
            var topZ = (surfaceElev - unit.DepthFrom) * _verticalExaggeration;
            var bottomZ = (surfaceElev - unit.DepthTo) * _verticalExaggeration;
            var color = unit.Color;

            int segments = 12;
            for (int i = 0; i < segments; i++)
            {
                float angle1 = i * MathF.PI * 2 / segments;
                float angle2 = (i + 1) * MathF.PI * 2 / segments;

                var p1Top = new Vector3(x + MathF.Cos(angle1) * radius, y + MathF.Sin(angle1) * radius, topZ);
                var p2Top = new Vector3(x + MathF.Cos(angle2) * radius, y + MathF.Sin(angle2) * radius, topZ);
                var p1Bottom = new Vector3(x + MathF.Cos(angle1) * radius, y + MathF.Sin(angle1) * radius, bottomZ);
                var p2Bottom = new Vector3(x + MathF.Cos(angle2) * radius, y + MathF.Sin(angle2) * radius, bottomZ);

                var normal = Vector3.Normalize(new Vector3(MathF.Cos(angle1), MathF.Sin(angle1), 0));

                var baseIdx = (uint)vertices.Count;
                vertices.Add(new VertexData(p1Top, color, normal));
                vertices.Add(new VertexData(p2Top, color, normal));
                vertices.Add(new VertexData(p1Bottom, color, normal));
                vertices.Add(new VertexData(p2Bottom, color, normal));

                indices.AddRange(new uint[] { baseIdx, baseIdx + 1, baseIdx + 2 });
                indices.AddRange(new uint[] { baseIdx + 1, baseIdx + 3, baseIdx + 2 });
            }
        }

        return CreateGpuMesh(vertices, indices, false);
    }

    private void BuildProfileLineMeshes()
    {
        foreach (var mesh in _profileLineMeshes)
            mesh.Dispose();
        _profileLineMeshes.Clear();

        foreach (var profile in _correlationData.Profiles)
        {
            if (!profile.IsVisible) continue;

            var vertices = new List<VertexData>();
            var indices = new List<uint>();

            // Draw profile line on surface
            var color = profile.Color;
            var avgElevation = GetAverageElevation(profile) * _verticalExaggeration;

            // Create a ribbon for the profile line
            var dir = Vector2.Normalize(profile.EndPoint - profile.StartPoint);
            var perpDir = new Vector2(-dir.Y, dir.X) * _profileLineWidth;

            var p1 = new Vector3(profile.StartPoint.X - perpDir.X, profile.StartPoint.Y - perpDir.Y, avgElevation + 5);
            var p2 = new Vector3(profile.StartPoint.X + perpDir.X, profile.StartPoint.Y + perpDir.Y, avgElevation + 5);
            var p3 = new Vector3(profile.EndPoint.X - perpDir.X, profile.EndPoint.Y - perpDir.Y, avgElevation + 5);
            var p4 = new Vector3(profile.EndPoint.X + perpDir.X, profile.EndPoint.Y + perpDir.Y, avgElevation + 5);

            vertices.Add(new VertexData(p1, color, Vector3.UnitZ));
            vertices.Add(new VertexData(p2, color, Vector3.UnitZ));
            vertices.Add(new VertexData(p3, color, Vector3.UnitZ));
            vertices.Add(new VertexData(p4, color, Vector3.UnitZ));

            indices.AddRange(new uint[] { 0, 1, 2, 1, 3, 2 });

            // Add vertical fence connecting boreholes
            for (int i = 0; i < profile.BoreholeOrder.Count - 1; i++)
            {
                var bhID1 = profile.BoreholeOrder[i];
                var bhID2 = profile.BoreholeOrder[i + 1];

                if (!_boreholeMap.TryGetValue(bhID1, out var bh1) ||
                    !_boreholeMap.TryGetValue(bhID2, out var bh2))
                    continue;

                var fenceColor = new Vector4(color.X, color.Y, color.Z, 0.3f);
                var baseIdx = (uint)vertices.Count;

                var topZ1 = bh1.Elevation * _verticalExaggeration;
                var bottomZ1 = (bh1.Elevation - bh1.TotalDepth) * _verticalExaggeration;
                var topZ2 = bh2.Elevation * _verticalExaggeration;
                var bottomZ2 = (bh2.Elevation - bh2.TotalDepth) * _verticalExaggeration;

                vertices.Add(new VertexData(new Vector3(bh1.SurfaceCoordinates.X, bh1.SurfaceCoordinates.Y, topZ1), fenceColor));
                vertices.Add(new VertexData(new Vector3(bh2.SurfaceCoordinates.X, bh2.SurfaceCoordinates.Y, topZ2), fenceColor));
                vertices.Add(new VertexData(new Vector3(bh1.SurfaceCoordinates.X, bh1.SurfaceCoordinates.Y, bottomZ1), fenceColor));
                vertices.Add(new VertexData(new Vector3(bh2.SurfaceCoordinates.X, bh2.SurfaceCoordinates.Y, bottomZ2), fenceColor));

                indices.AddRange(new uint[] { baseIdx, baseIdx + 1, baseIdx + 2 });
                indices.AddRange(new uint[] { baseIdx + 1, baseIdx + 3, baseIdx + 2 });
            }

            _profileLineMeshes.Add(CreateGpuMesh(vertices, indices, false));
        }
    }

    private float GetAverageElevation(CorrelationProfile profile)
    {
        float sum = 0;
        int count = 0;
        foreach (var bhID in profile.BoreholeOrder)
        {
            if (_boreholeMap.TryGetValue(bhID, out var bh))
            {
                sum += bh.Elevation;
                count++;
            }
        }
        return count > 0 ? sum / count : 0;
    }

    private void BuildCorrelationLinesMeshes()
    {
        _intraCorrelationLinesMesh.Dispose();
        _crossCorrelationLinesMesh.Dispose();

        // Intra-profile correlations
        var intraVertices = new List<VertexData>();
        var intraIndices = new List<uint>();

        foreach (var correlation in _correlationData.IntraProfileCorrelations)
        {
            if (!_boreholeMap.TryGetValue(correlation.SourceBoreholeID, out var sourceBh) ||
                !_boreholeMap.TryGetValue(correlation.TargetBoreholeID, out var targetBh))
                continue;

            var sourceUnit = sourceBh.LithologyUnits.FirstOrDefault(u => u.ID == correlation.SourceLithologyID);
            var targetUnit = targetBh.LithologyUnits.FirstOrDefault(u => u.ID == correlation.TargetLithologyID);

            if (sourceUnit == null || targetUnit == null) continue;

            var sourcePos = new Vector3(
                sourceBh.SurfaceCoordinates.X,
                sourceBh.SurfaceCoordinates.Y,
                (sourceBh.Elevation - (sourceUnit.DepthFrom + sourceUnit.DepthTo) / 2) * _verticalExaggeration);

            var targetPos = new Vector3(
                targetBh.SurfaceCoordinates.X,
                targetBh.SurfaceCoordinates.Y,
                (targetBh.Elevation - (targetUnit.DepthFrom + targetUnit.DepthTo) / 2) * _verticalExaggeration);

            AddCorrelationRibbon(intraVertices, intraIndices, sourcePos, targetPos, correlation.Color, _correlationLineWidth);
        }

        _intraCorrelationLinesMesh = CreateGpuMesh(intraVertices, intraIndices, false);

        // Cross-profile correlations (different color/style)
        var crossVertices = new List<VertexData>();
        var crossIndices = new List<uint>();

        foreach (var correlation in _correlationData.CrossProfileCorrelations)
        {
            if (!_boreholeMap.TryGetValue(correlation.SourceBoreholeID, out var sourceBh) ||
                !_boreholeMap.TryGetValue(correlation.TargetBoreholeID, out var targetBh))
                continue;

            var sourceUnit = sourceBh.LithologyUnits.FirstOrDefault(u => u.ID == correlation.SourceLithologyID);
            var targetUnit = targetBh.LithologyUnits.FirstOrDefault(u => u.ID == correlation.TargetLithologyID);

            if (sourceUnit == null || targetUnit == null) continue;

            var sourcePos = new Vector3(
                sourceBh.SurfaceCoordinates.X,
                sourceBh.SurfaceCoordinates.Y,
                (sourceBh.Elevation - (sourceUnit.DepthFrom + sourceUnit.DepthTo) / 2) * _verticalExaggeration);

            var targetPos = new Vector3(
                targetBh.SurfaceCoordinates.X,
                targetBh.SurfaceCoordinates.Y,
                (targetBh.Elevation - (targetUnit.DepthFrom + targetUnit.DepthTo) / 2) * _verticalExaggeration);

            // Use dashed style by creating multiple short segments
            AddDashedCorrelationLine(crossVertices, crossIndices, sourcePos, targetPos,
                correlation.Color, _correlationLineWidth * 1.5f);
        }

        _crossCorrelationLinesMesh = CreateGpuMesh(crossVertices, crossIndices, false);
    }

    private void AddCorrelationRibbon(List<VertexData> vertices, List<uint> indices,
        Vector3 start, Vector3 end, Vector4 color, float width)
    {
        var dir = Vector3.Normalize(end - start);
        var perpHoriz = Vector3.Cross(dir, Vector3.UnitZ);
        if (perpHoriz.Length() < 0.001f)
            perpHoriz = Vector3.UnitX;
        perpHoriz = Vector3.Normalize(perpHoriz) * width;

        var baseIdx = (uint)vertices.Count;

        vertices.Add(new VertexData(start - perpHoriz, color));
        vertices.Add(new VertexData(start + perpHoriz, color));
        vertices.Add(new VertexData(end - perpHoriz, color));
        vertices.Add(new VertexData(end + perpHoriz, color));

        indices.AddRange(new uint[] { baseIdx, baseIdx + 1, baseIdx + 2 });
        indices.AddRange(new uint[] { baseIdx + 1, baseIdx + 3, baseIdx + 2 });
    }

    private void AddDashedCorrelationLine(List<VertexData> vertices, List<uint> indices,
        Vector3 start, Vector3 end, Vector4 color, float width)
    {
        var total = end - start;
        float length = total.Length();
        var dir = total / length;
        int dashCount = Math.Max(3, (int)(length / 20f));
        float dashLength = length / (dashCount * 2);

        for (int i = 0; i < dashCount; i++)
        {
            var dashStart = start + dir * (i * 2 * dashLength);
            var dashEnd = start + dir * ((i * 2 + 1) * dashLength);
            AddCorrelationRibbon(vertices, indices, dashStart, dashEnd, color, width);
        }
    }

    private void BuildIntersectionMarkersMesh()
    {
        _intersectionMarkersMesh.Dispose();

        var vertices = new List<VertexData>();
        var indices = new List<uint>();

        foreach (var intersection in _correlationData.Intersections)
        {
            // Find average elevation at intersection
            float avgElev = 0;
            int count = 0;
            foreach (var profile in _correlationData.Profiles)
            {
                if (profile.ID == intersection.Profile1ID || profile.ID == intersection.Profile2ID)
                {
                    avgElev += GetAverageElevation(profile);
                    count++;
                }
            }
            if (count > 0) avgElev /= count;
            avgElev *= _verticalExaggeration;

            // Create a diamond marker
            var center = new Vector3(intersection.IntersectionPoint.X, intersection.IntersectionPoint.Y, avgElev + 10);
            float size = 5f;

            var baseIdx = (uint)vertices.Count;

            // Diamond vertices
            vertices.Add(new VertexData(center + new Vector3(0, 0, size), _intersectionMarkerColor, Vector3.UnitZ)); // top
            vertices.Add(new VertexData(center + new Vector3(size, 0, 0), _intersectionMarkerColor, Vector3.UnitX)); // right
            vertices.Add(new VertexData(center + new Vector3(0, size, 0), _intersectionMarkerColor, Vector3.UnitY)); // front
            vertices.Add(new VertexData(center + new Vector3(-size, 0, 0), _intersectionMarkerColor, -Vector3.UnitX)); // left
            vertices.Add(new VertexData(center + new Vector3(0, -size, 0), _intersectionMarkerColor, -Vector3.UnitY)); // back
            vertices.Add(new VertexData(center + new Vector3(0, 0, -size), _intersectionMarkerColor, -Vector3.UnitZ)); // bottom

            // Triangles for octahedron
            indices.AddRange(new uint[] { baseIdx, baseIdx + 1, baseIdx + 2 });
            indices.AddRange(new uint[] { baseIdx, baseIdx + 2, baseIdx + 3 });
            indices.AddRange(new uint[] { baseIdx, baseIdx + 3, baseIdx + 4 });
            indices.AddRange(new uint[] { baseIdx, baseIdx + 4, baseIdx + 1 });
            indices.AddRange(new uint[] { baseIdx + 5, baseIdx + 2, baseIdx + 1 });
            indices.AddRange(new uint[] { baseIdx + 5, baseIdx + 3, baseIdx + 2 });
            indices.AddRange(new uint[] { baseIdx + 5, baseIdx + 4, baseIdx + 3 });
            indices.AddRange(new uint[] { baseIdx + 5, baseIdx + 1, baseIdx + 4 });
        }

        _intersectionMarkersMesh = CreateGpuMesh(vertices, indices, false);
    }

    private void BuildHorizonSurfaceMeshes()
    {
        foreach (var mesh in _horizonSurfaceMeshes)
            mesh.Dispose();
        _horizonSurfaceMeshes.Clear();

        foreach (var horizon in _correlationData.Horizons)
        {
            if (horizon.Triangles.Count == 0 && horizon.ControlPoints.Count < 3) continue;

            var vertices = new List<VertexData>();
            var indices = new List<uint>();

            var color = new Vector4(horizon.Color.X, horizon.Color.Y, horizon.Color.Z, _horizonOpacity);

            // Build mesh from triangles
            if (horizon.Triangles.Count > 0)
            {
                foreach (var cp in horizon.ControlPoints)
                {
                    var pos = new Vector3(cp.Position.X, cp.Position.Y, cp.Position.Z * _verticalExaggeration);
                    vertices.Add(new VertexData(pos, color, Vector3.UnitZ));
                }

                foreach (var tri in horizon.Triangles)
                {
                    if (tri.A >= 0 && tri.A < vertices.Count &&
                        tri.B >= 0 && tri.B < vertices.Count &&
                        tri.C >= 0 && tri.C < vertices.Count)
                    {
                        // Calculate normal
                        var p0 = vertices[tri.A].Position;
                        var p1 = vertices[tri.B].Position;
                        var p2 = vertices[tri.C].Position;
                        var normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));

                        // Update normals
                        var v0 = vertices[tri.A];
                        v0.Normal = normal;
                        vertices[tri.A] = v0;

                        var v1 = vertices[tri.B];
                        v1.Normal = normal;
                        vertices[tri.B] = v1;

                        var v2 = vertices[tri.C];
                        v2.Normal = normal;
                        vertices[tri.C] = v2;

                        indices.Add((uint)tri.A);
                        indices.Add((uint)tri.B);
                        indices.Add((uint)tri.C);
                    }
                }
            }
            else if (horizon.ElevationGrid != null && horizon.GridBounds != null)
            {
                // Build from grid
                int resX = horizon.GridResolutionX;
                int resY = horizon.GridResolutionY;
                var bounds = horizon.GridBounds;

                float stepX = bounds.Width / (resX - 1);
                float stepY = bounds.Height / (resY - 1);

                for (int j = 0; j < resY; j++)
                {
                    for (int i = 0; i < resX; i++)
                    {
                        float x = bounds.Min.X + i * stepX;
                        float y = bounds.Min.Y + j * stepY;
                        float z = horizon.ElevationGrid[i, j] * _verticalExaggeration;

                        vertices.Add(new VertexData(new Vector3(x, y, z), color, Vector3.UnitZ));
                    }
                }

                for (int j = 0; j < resY - 1; j++)
                {
                    for (int i = 0; i < resX - 1; i++)
                    {
                        uint idx = (uint)(j * resX + i);
                        indices.AddRange(new uint[] { idx, idx + 1, idx + (uint)resX });
                        indices.AddRange(new uint[] { idx + 1, idx + (uint)resX + 1, idx + (uint)resX });
                    }
                }
            }

            if (vertices.Count > 0 && indices.Count > 0)
            {
                _horizonSurfaceMeshes.Add(CreateGpuMesh(vertices, indices, false));
            }
        }
    }

    private void BuildTopographyMesh()
    {
        _topographyMesh.Dispose();

        if (_demLayer == null) return;

        var vertices = new List<VertexData>();
        var indices = new List<uint>();

        var bounds = _demLayer.Bounds;
        var pixelData = _demLayer.GetPixelData();
        int gridX = Math.Min(100, _demLayer.Width);
        int gridY = Math.Min(100, _demLayer.Height);

        float stepX = bounds.Width / gridX;
        float stepY = bounds.Height / gridY;

        for (int j = 0; j <= gridY; j++)
        {
            for (int i = 0; i <= gridX; i++)
            {
                float x = bounds.Min.X + i * stepX;
                float y = bounds.Min.Y + j * stepY;

                int pixelX = Math.Clamp((int)((float)i / gridX * (_demLayer.Width - 1)), 0, _demLayer.Width - 1);
                int pixelY = Math.Clamp((int)((float)j / gridY * (_demLayer.Height - 1)), 0, _demLayer.Height - 1);
                float z = (pixelData[pixelX, pixelY] + _demElevationOffset) * _verticalExaggeration;

                vertices.Add(new VertexData(
                    new Vector3(x, y, z),
                    _topographyColor,
                    Vector3.UnitZ,
                    new Vector2((float)i / gridX, (float)j / gridY)));
            }
        }

        for (int j = 0; j < gridY; j++)
        {
            for (int i = 0; i < gridX; i++)
            {
                uint topLeft = (uint)(j * (gridX + 1) + i);
                uint topRight = topLeft + 1;
                uint bottomLeft = topLeft + (uint)(gridX + 1);
                uint bottomRight = bottomLeft + 1;

                indices.AddRange(new uint[] { topLeft, bottomLeft, topRight });
                indices.AddRange(new uint[] { topRight, bottomLeft, bottomRight });
            }
        }

        _topographyMesh = CreateGpuMesh(vertices, indices, false);
    }

    private GpuMesh CreateGpuMesh(List<VertexData> vertices, List<uint> indices, bool isLineList)
    {
        if (vertices.Count == 0 || indices.Count == 0)
            return default;

        var vb = _factory.CreateBuffer(new BufferDescription(
            (uint)(vertices.Count * Marshal.SizeOf<VertexData>()), BufferUsage.VertexBuffer));
        _graphicsDevice.UpdateBuffer(vb, 0, vertices.ToArray());

        var ib = _factory.CreateBuffer(new BufferDescription(
            (uint)(indices.Count * sizeof(uint)), BufferUsage.IndexBuffer));
        _graphicsDevice.UpdateBuffer(ib, 0, indices.ToArray());

        return new GpuMesh
        {
            VertexBuffer = vb,
            IndexBuffer = ib,
            IndexCount = (uint)indices.Count,
            IsLineList = isLineList
        };
    }

    #endregion

    #region Camera & Rendering

    private void UpdateCamera()
    {
        float azimuthRad = _cameraAzimuth * MathF.PI / 180f;
        float elevationRad = _cameraElevation * MathF.PI / 180f;

        var cameraOffset = new Vector3(
            MathF.Cos(elevationRad) * MathF.Cos(azimuthRad),
            MathF.Cos(elevationRad) * MathF.Sin(azimuthRad),
            MathF.Sin(elevationRad)) * _cameraDistance;

        var cameraPosition = _cameraTarget + cameraOffset;

        _viewMatrix = Matrix4x4.CreateLookAt(cameraPosition, _cameraTarget, Vector3.UnitZ);
        _projectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            45f * MathF.PI / 180f,
            (float)_renderWidth / _renderHeight,
            1f, _cameraDistance * 10f);
    }

    public void Render()
    {
        var cl = _graphicsDevice.ResourceFactory.CreateCommandList();
        cl.Begin();
        cl.SetFramebuffer(_framebuffer);
        cl.ClearColorTarget(0, new RgbaFloat(0.12f, 0.12f, 0.15f, 1f));
        cl.ClearDepthStencil(1f);

        var mvp = _viewMatrix * _projectionMatrix;
        var uniforms = new UniformData
        {
            ModelViewProjection = mvp,
            Model = Matrix4x4.Identity,
            View = _viewMatrix,
            Projection = _projectionMatrix,
            LightPosition = new Vector4(0, 0, 1000, 1),
            CameraPosition = new Vector4(_cameraTarget, 1),
            Opacity = 1f,
            Time = 0,
            RenderMode = 0,
            Padding = 0
        };
        _graphicsDevice.UpdateBuffer(_uniformBuffer, 0, ref uniforms);

        // Render topography
        if (_showTopography && _topographyMesh.IndexCount > 0)
        {
            cl.SetPipeline(_showWireframe ? _wireframePipeline : _solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.SetVertexBuffer(0, _topographyMesh.VertexBuffer);
            cl.SetIndexBuffer(_topographyMesh.IndexBuffer, IndexFormat.UInt32);
            cl.DrawIndexed(_topographyMesh.IndexCount);
        }

        // Render horizon surfaces (behind everything else)
        if (_showHorizonSurfaces)
        {
            cl.SetPipeline(_showWireframe ? _wireframePipeline : _solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            foreach (var mesh in _horizonSurfaceMeshes)
            {
                if (mesh.IndexCount == 0) continue;
                cl.SetVertexBuffer(0, mesh.VertexBuffer);
                cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
                cl.DrawIndexed(mesh.IndexCount);
            }
        }

        // Render profile lines
        if (_showProfileLines)
        {
            cl.SetPipeline(_solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            foreach (var mesh in _profileLineMeshes)
            {
                if (mesh.IndexCount == 0) continue;
                cl.SetVertexBuffer(0, mesh.VertexBuffer);
                cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
                cl.DrawIndexed(mesh.IndexCount);
            }
        }

        // Render borehole casings
        if (_showBoreholes)
        {
            cl.SetPipeline(_solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            foreach (var mesh in _boreholeMeshes)
            {
                if (mesh.IndexCount == 0) continue;
                cl.SetVertexBuffer(0, mesh.VertexBuffer);
                cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
                cl.DrawIndexed(mesh.IndexCount);
            }
        }

        // Render lithology
        if (_showLithology)
        {
            cl.SetPipeline(_solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            foreach (var mesh in _lithologyMeshes)
            {
                if (mesh.IndexCount == 0) continue;
                cl.SetVertexBuffer(0, mesh.VertexBuffer);
                cl.SetIndexBuffer(mesh.IndexBuffer, IndexFormat.UInt32);
                cl.DrawIndexed(mesh.IndexCount);
            }
        }

        // Render intra-profile correlations
        if (_showIntraCorrelations && _intraCorrelationLinesMesh.IndexCount > 0)
        {
            cl.SetPipeline(_solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.SetVertexBuffer(0, _intraCorrelationLinesMesh.VertexBuffer);
            cl.SetIndexBuffer(_intraCorrelationLinesMesh.IndexBuffer, IndexFormat.UInt32);
            cl.DrawIndexed(_intraCorrelationLinesMesh.IndexCount);
        }

        // Render cross-profile correlations
        if (_showCrossCorrelations && _crossCorrelationLinesMesh.IndexCount > 0)
        {
            cl.SetPipeline(_solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.SetVertexBuffer(0, _crossCorrelationLinesMesh.VertexBuffer);
            cl.SetIndexBuffer(_crossCorrelationLinesMesh.IndexBuffer, IndexFormat.UInt32);
            cl.DrawIndexed(_crossCorrelationLinesMesh.IndexCount);
        }

        // Render intersection markers
        if (_showIntersections && _intersectionMarkersMesh.IndexCount > 0)
        {
            cl.SetPipeline(_solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.SetVertexBuffer(0, _intersectionMarkersMesh.VertexBuffer);
            cl.SetIndexBuffer(_intersectionMarkersMesh.IndexBuffer, IndexFormat.UInt32);
            cl.DrawIndexed(_intersectionMarkersMesh.IndexCount);
        }

        cl.End();
        _graphicsDevice.SubmitCommands(cl);
        cl.Dispose();
    }

    #endregion

    #region UI

    public void Draw()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(1200, 800), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Multi-Profile Correlation 3D View", ref _isOpen, ImGuiWindowFlags.MenuBar))
        {
            DrawMenuBar();

            // Main layout: side panel + viewport
            var contentRegion = ImGui.GetContentRegionAvail();

            // Side panel
            ImGui.BeginChild("SidePanel", new Vector2(250, contentRegion.Y), ImGuiChildFlags.Border);
            DrawSidePanel();
            ImGui.EndChild();

            ImGui.SameLine();

            // Viewport area
            ImGui.BeginChild("ViewportArea", new Vector2(contentRegion.X - 260, contentRegion.Y), ImGuiChildFlags.None);
            DrawToolbar();
            DrawViewport();
            ImGui.EndChild();
        }
        ImGui.End();

        if (!_isOpen)
            OnClose?.Invoke();
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Export to SubsurfaceGIS..."))
                    ExportToSubsurfaceGIS();
                if (ImGui.MenuItem("Export Horizons to GIS..."))
                    ExportHorizonsToGIS();
                ImGui.Separator();
                if (ImGui.MenuItem("Close"))
                    _isOpen = false;
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                ImGui.Checkbox("Show Topography", ref _showTopography);
                ImGui.Checkbox("Show Boreholes", ref _showBoreholes);
                ImGui.Checkbox("Show Lithology", ref _showLithology);
                ImGui.Checkbox("Show Profile Lines", ref _showProfileLines);
                ImGui.Checkbox("Show Intra-Profile Correlations", ref _showIntraCorrelations);
                ImGui.Checkbox("Show Cross-Profile Correlations", ref _showCrossCorrelations);
                ImGui.Checkbox("Show Intersections", ref _showIntersections);
                ImGui.Checkbox("Show Horizon Surfaces", ref _showHorizonSurfaces);
                ImGui.Checkbox("Wireframe Mode", ref _showWireframe);
                ImGui.Separator();
                if (ImGui.MenuItem("Reset Camera"))
                {
                    _cameraAzimuth = 45f;
                    _cameraElevation = 35f;
                    CalculateCameraTargetFromBoreholes();
                    UpdateCamera();
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Tools"))
            {
                if (ImGui.MenuItem("Auto-Correlate Lithologies"))
                    AutoCorrelate();
                if (ImGui.MenuItem("Build Horizons from Correlations"))
                    BuildHorizons();
                ImGui.Separator();
                if (ImGui.BeginMenu("Load DEM..."))
                {
                    DrawDEMMenu();
                    ImGui.EndMenu();
                }
                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
    }

    private void DrawSidePanel()
    {
        // Profiles section
        if (ImGui.CollapsingHeader("Profiles", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var profile in _correlationData.Profiles)
            {
                var isSelected = _selectedProfileID == profile.ID;
                var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
                if (isSelected) flags |= ImGuiTreeNodeFlags.Selected;

                var nodeColor = new Vector4(profile.Color.X, profile.Color.Y, profile.Color.Z, 1);
                ImGui.PushStyleColor(ImGuiCol.Text, nodeColor);

                bool expanded = ImGui.TreeNodeEx(profile.ID, flags, profile.Name);
                ImGui.PopStyleColor();

                if (ImGui.IsItemClicked())
                {
                    _selectedProfileID = profile.ID;
                    OnProfileSelected?.Invoke(profile.ID);
                }

                if (expanded)
                {
                    ImGui.Indent();
                    var isVisible = profile.IsVisible;
                    ImGui.Checkbox("Visible##" + profile.ID, ref isVisible);
                    profile.IsVisible = isVisible;
                    ImGui.Text($"Boreholes: {profile.BoreholeOrder.Count}");
                    ImGui.Text($"Azimuth: {profile.Azimuth:F1}");

                    // List boreholes
                    foreach (var bhID in profile.BoreholeOrder)
                    {
                        if (_correlationData.Headers.TryGetValue(bhID, out var header))
                        {
                            ImGui.BulletText(header.DisplayName);
                        }
                    }
                    ImGui.Unindent();
                    ImGui.TreePop();
                }
            }

            ImGui.Spacing();
            ImGui.Text($"Intersections: {_correlationData.Intersections.Count}");
        }

        ImGui.Separator();

        // Horizons section
        if (ImGui.CollapsingHeader("Horizons", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var horizon in _correlationData.Horizons)
            {
                var isSelected = _selectedHorizonID == horizon.ID;
                var flags = ImGuiSelectableFlags.None;

                var horizonColor = new Vector4(horizon.Color.X, horizon.Color.Y, horizon.Color.Z, 1);
                ImGui.PushStyleColor(ImGuiCol.Text, horizonColor);

                if (ImGui.Selectable($"  {horizon.Name}", isSelected, flags))
                {
                    _selectedHorizonID = horizon.ID;
                    OnHorizonSelected?.Invoke(horizon.ID);
                }

                ImGui.PopStyleColor();

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Type: {horizon.LithologyType}");
                    ImGui.Text($"Control Points: {horizon.ControlPoints.Count}");
                    ImGui.Text($"Triangles: {horizon.Triangles.Count}");
                    ImGui.EndTooltip();
                }
            }

            if (_correlationData.Horizons.Count == 0)
            {
                ImGui.TextDisabled("No horizons built yet");
                ImGui.TextDisabled("Use Tools > Build Horizons");
            }
        }

        ImGui.Separator();

        // Correlations summary
        if (ImGui.CollapsingHeader("Correlations"))
        {
            ImGui.Text($"Intra-profile: {_correlationData.IntraProfileCorrelations.Count}");
            ImGui.Text($"Cross-profile: {_correlationData.CrossProfileCorrelations.Count}");
        }

        ImGui.Separator();

        // Status
        if (!string.IsNullOrEmpty(_statusMessage))
        {
            ImGui.TextWrapped(_statusMessage);
        }
    }

    private void DrawToolbar()
    {
        ImGui.BeginChild("Toolbar3D", new Vector2(0, 35), ImGuiChildFlags.None);

        ImGui.Text("V.Exag:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.SliderFloat("##VExag", ref _verticalExaggeration, 1f, 10f, "%.1fx"))
        {
            BuildAllMeshes();
        }

        ImGui.SameLine();
        ImGui.Text("Radius:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(60);
        if (ImGui.SliderFloat("##BRadius", ref _boreholeRadius, 1f, 10f, "%.1f"))
        {
            BuildBoreholeMeshes();
        }

        ImGui.SameLine();
        ImGui.Text("Horizon:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.SliderFloat("##HOpacity", ref _horizonOpacity, 0.1f, 1f, "%.1f"))
        {
            BuildHorizonSurfaceMeshes();
        }

        ImGui.SameLine();
        if (ImGui.Button("Rebuild"))
        {
            _correlationData.BuildHorizons(_boreholeMap);
            BuildAllMeshes();
            _statusMessage = "Rebuilt all meshes";
        }

        ImGui.EndChild();
        ImGui.Separator();
    }

    private void DrawViewport()
    {
        var viewportSize = ImGui.GetContentRegionAvail();
        if (viewportSize.X < 10 || viewportSize.Y < 10) return;

        if ((uint)viewportSize.X != _renderWidth || (uint)viewportSize.Y != _renderHeight)
        {
            _renderWidth = (uint)viewportSize.X;
            _renderHeight = (uint)viewportSize.Y;
            CreateRenderTarget(_renderWidth, _renderHeight);
            CreateResourceSet();
            UpdateCamera();
        }

        HandleInput();
        Render();
        ImGui.Image((IntPtr)_renderTargetView.GetHashCode(), viewportSize);
    }

    private void HandleInput()
    {
        if (!ImGui.IsWindowHovered()) return;

        var io = ImGui.GetIO();
        var mouse = io.MousePos;

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (!_isRotating)
            {
                _isRotating = true;
                _lastMousePos = mouse;
            }

            var delta = mouse - _lastMousePos;
            _cameraAzimuth -= delta.X * 0.5f;
            _cameraElevation += delta.Y * 0.5f;
            _cameraElevation = Math.Clamp(_cameraElevation, -89f, 89f);
            _lastMousePos = mouse;
            UpdateCamera();
        }
        else
        {
            _isRotating = false;
        }

        if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            if (!_isPanning)
            {
                _isPanning = true;
                _lastMousePos = mouse;
            }

            var delta = mouse - _lastMousePos;
            float panSpeed = _cameraDistance * 0.001f;
            float azimuthRad = _cameraAzimuth * MathF.PI / 180f;

            _cameraTarget.X -= (MathF.Cos(azimuthRad) * delta.X + MathF.Sin(azimuthRad) * delta.Y) * panSpeed;
            _cameraTarget.Y -= (MathF.Sin(azimuthRad) * delta.X - MathF.Cos(azimuthRad) * delta.Y) * panSpeed;
            _lastMousePos = mouse;
            UpdateCamera();
        }
        else
        {
            _isPanning = false;
        }

        if (io.MouseWheel != 0)
        {
            _cameraDistance *= io.MouseWheel > 0 ? 0.9f : 1.1f;
            _cameraDistance = Math.Clamp(_cameraDistance, 10f, 10000f);
            UpdateCamera();
        }
    }

    private void DrawDEMMenu()
    {
        var rasterDatasets = ProjectManager.Instance.LoadedDatasets
            .OfType<GISDataset>()
            .SelectMany(g => g.Layers)
            .OfType<GISRasterLayer>()
            .ToList();

        if (rasterDatasets.Count == 0)
        {
            ImGui.TextDisabled("No raster datasets available");
        }
        else
        {
            foreach (var raster in rasterDatasets)
            {
                if (ImGui.MenuItem(raster.Name, null, _demLayer == raster))
                {
                    _demLayer = raster;
                    _demDatasetName = raster.Name;
                    BuildTopographyMesh();
                    _statusMessage = $"Loaded DEM: {raster.Name}";
                }
            }
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Clear DEM", null, false, _demLayer != null))
        {
            _demLayer = null;
            _topographyMesh.Dispose();
            _topographyMesh = default;
            _statusMessage = "DEM cleared";
        }
    }

    #endregion

    #region Actions

    private void AutoCorrelate()
    {
        int count = _correlationData.AutoCorrelate(_boreholeMap);
        BuildCorrelationLinesMeshes();
        _statusMessage = $"Auto-correlated: {count} new correlations";
    }

    private void BuildHorizons()
    {
        _correlationData.BuildHorizons(_boreholeMap);
        BuildHorizonSurfaceMeshes();
        _statusMessage = $"Built {_correlationData.Horizons.Count} horizons";
    }

    private void ExportToSubsurfaceGIS()
    {
        try
        {
            var subsurfaceGIS = new SubsurfaceGISDataset(
                $"ProfileCorrelation_{_correlationData.Name}",
                "");

            subsurfaceGIS.BuildFromBoreholes(_boreholes.ToList(), _demLayer);
            ProjectManager.Instance.AddDataset(subsurfaceGIS);

            _statusMessage = "Exported to SubsurfaceGIS";
            Logger.Log("[ProfileCorrelation3DViewer] Exported to SubsurfaceGIS");
        }
        catch (Exception ex)
        {
            _statusMessage = $"Export failed: {ex.Message}";
            Logger.LogError($"Export failed: {ex.Message}");
        }
    }

    private void ExportHorizonsToGIS()
    {
        try
        {
            if (_correlationData.Horizons.Count == 0)
            {
                _statusMessage = "No horizons to export. Build horizons first.";
                return;
            }

            // Create GIS dataset with horizon contours
            var gisDataset = new GISDataset($"Horizons_{_correlationData.Name}", "");

            foreach (var horizon in _correlationData.Horizons)
            {
                var layer = new GISLayer
                {
                    Name = horizon.Name,
                    Type = LayerType.Vector
                };

                // Add control points as point features
                foreach (var cp in horizon.ControlPoints)
                {
                    var feature = new GISFeature
                    {
                        Type = FeatureType.Point
                    };
                    feature.Coordinates.Add(new Vector2(cp.Position.X, cp.Position.Y));
                    feature.Properties["elevation"] = cp.Position.Z.ToString("F2");
                    feature.Properties["lithology_id"] = cp.LithologyID;
                    feature.Properties["borehole_id"] = cp.BoreholeID;
                    layer.Features.Add(feature);
                }

                gisDataset.Layers.Add(layer);
            }

            ProjectManager.Instance.AddDataset(gisDataset);
            _statusMessage = $"Exported {_correlationData.Horizons.Count} horizons to GIS";
        }
        catch (Exception ex)
        {
            _statusMessage = $"Export failed: {ex.Message}";
        }
    }

    #endregion

    #region Shaders

    private string GetVertexShaderCode()
    {
        return @"
#version 450

layout(set = 0, binding = 0) uniform UniformData {
    mat4 ModelViewProjection;
    mat4 Model;
    mat4 View;
    mat4 Projection;
    vec4 LightPosition;
    vec4 CameraPosition;
    float Opacity;
    float Time;
    int RenderMode;
    int Padding;
};

layout(location = 0) in vec3 Position;
layout(location = 1) in vec4 Color;
layout(location = 2) in vec3 Normal;
layout(location = 3) in vec2 TexCoord;
layout(location = 4) in float Value;
layout(location = 5) in vec3 UVW;

layout(location = 0) out vec4 fragColor;
layout(location = 1) out vec3 fragNormal;
layout(location = 2) out vec3 fragWorldPos;

void main() {
    gl_Position = ModelViewProjection * vec4(Position, 1.0);
    fragColor = Color;
    fragNormal = Normal;
    fragWorldPos = Position;
}
";
    }

    private string GetFragmentShaderCode()
    {
        return @"
#version 450

layout(set = 0, binding = 0) uniform UniformData {
    mat4 ModelViewProjection;
    mat4 Model;
    mat4 View;
    mat4 Projection;
    vec4 LightPosition;
    vec4 CameraPosition;
    float Opacity;
    float Time;
    int RenderMode;
    int Padding;
};

layout(location = 0) in vec4 fragColor;
layout(location = 1) in vec3 fragNormal;
layout(location = 2) in vec3 fragWorldPos;

layout(location = 0) out vec4 outColor;

void main() {
    vec3 lightDir = normalize(LightPosition.xyz - fragWorldPos);
    float ambient = 0.35;
    float diffuse = max(dot(normalize(fragNormal), lightDir), 0.0) * 0.65;
    float lighting = ambient + diffuse;

    outColor = vec4(fragColor.rgb * lighting, fragColor.a * Opacity);
}
";
    }

    #endregion

    public void Dispose()
    {
        _topographyMesh.Dispose();
        foreach (var mesh in _boreholeMeshes) mesh.Dispose();
        foreach (var mesh in _lithologyMeshes) mesh.Dispose();
        foreach (var mesh in _profileLineMeshes) mesh.Dispose();
        _intraCorrelationLinesMesh.Dispose();
        _crossCorrelationLinesMesh.Dispose();
        _intersectionMarkersMesh.Dispose();
        foreach (var mesh in _horizonSurfaceMeshes) mesh.Dispose();

        _solidPipeline?.Dispose();
        _wireframePipeline?.Dispose();
        _linePipeline?.Dispose();
        _resourceLayout?.Dispose();
        _resourceSet?.Dispose();
        _uniformBuffer?.Dispose();
        _renderTarget?.Dispose();
        _renderTargetView?.Dispose();
        _depthTexture?.Dispose();
        _framebuffer?.Dispose();

        Logger.Log("[ProfileCorrelation3DViewer] Disposed");
    }
}
