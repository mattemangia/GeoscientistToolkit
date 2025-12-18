// GeoscientistToolkit/UI/Borehole/BoreholeCorrelation3DViewer.cs

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using Veldrid.SPIRV;

namespace GeoscientistToolkit.UI.Borehole;

/// <summary>
/// 3D viewer for visualizing borehole log correlations with topography (DEM) support.
/// Integrates with SubsurfaceGIS for full 3D subsurface mapping.
/// </summary>
public class BoreholeCorrelation3DViewer : IDisposable
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
    private readonly BoreholeLogCorrelationDataset _correlationData;
    private readonly List<BoreholeDataset> _boreholes = new();
    private readonly Dictionary<string, BoreholeDataset> _boreholeMap = new();

    // GPU Resources
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ResourceFactory _factory;
    private Framebuffer _framebuffer;
    private Texture _renderTarget;
    private TextureView _renderTargetView;
    private Texture _depthTexture;
    private uint _renderWidth = 800;
    private uint _renderHeight = 600;

    private Pipeline _solidPipeline;
    private Pipeline _wireframePipeline;
    private ResourceLayout _resourceLayout;
    private ResourceSet _resourceSet;
    private DeviceBuffer _uniformBuffer;

    // Meshes
    private GpuMesh _topographyMesh;
    private List<GpuMesh> _boreholeMeshes = new();
    private List<GpuMesh> _lithologyMeshes = new();
    private GpuMesh _correlationLinesMesh;
    private GpuMesh _horizonSurfaceMesh;

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
    private bool _showCorrelationLines = true;
    private bool _showHorizonSurfaces = true;
    private bool _showWireframe;
    private float _verticalExaggeration = 1.0f;
    private float _boreholeRadius = 2f;
    private float _correlationLineWidth = 3f;

    // DEM/Topography
    private GISRasterLayer _demLayer;
    private string _demDatasetName;
    private float _demElevationOffset;

    // Subsurface GIS integration
    private SubsurfaceGISDataset _subsurfaceGIS;

    // UI State
    private bool _isOpen = true;
    private string _statusMessage = "";

    // Colors
    private readonly Vector4 _topographyColor = new(0.4f, 0.6f, 0.3f, 0.8f);
    private readonly Vector4 _boreholeColor = new(0.3f, 0.3f, 0.3f, 1f);
    private readonly Vector4 _defaultCorrelationColor = new(0.3f, 0.6f, 0.9f, 0.8f);

    public bool IsOpen => _isOpen;

    public event Action OnClose;

    public BoreholeCorrelation3DViewer(
        GraphicsDevice graphicsDevice,
        BoreholeLogCorrelationDataset correlationData,
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
        BuildMeshes();
        UpdateCamera();

        Logger.Log($"[BoreholeCorrelation3DViewer] Initialized with {_boreholes.Count} boreholes");
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
        // Create shaders
        var vertexCode = GetVertexShaderCode();
        var fragmentCode = GetFragmentShaderCode();

        var vertexShaderDesc = new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes(vertexCode), "main");
        var fragmentShaderDesc = new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes(fragmentCode), "main");
        var shaders = _factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

        // Vertex layout
        var vertexLayout = new VertexLayoutDescription(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("Normal", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
            new VertexElementDescription("TexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Value", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float1),
            new VertexElementDescription("UVW", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3));

        // Resource layout
        _resourceLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("UniformData", ResourceKind.UniformBuffer,
                ShaderStages.Vertex | ShaderStages.Fragment)));

        // Solid pipeline
        _solidPipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.DepthOnlyLessEqual,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.Back, PolygonFillMode.Solid, FrontFace.CounterClockwise, true, false),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = new[] { _resourceLayout },
            ShaderSet = new ShaderSetDescription(new[] { vertexLayout }, new[] { shaders[0], shaders[1] }),
            Outputs = _framebuffer.OutputDescription
        });

        // Wireframe pipeline (for lines)
        _wireframePipeline = _factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
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

    private void BuildMeshes()
    {
        BuildBoreholeMeshes();
        BuildCorrelationLinesMesh();
        BuildHorizonSurfaceMesh();

        if (_demLayer != null)
            BuildTopographyMesh();

        // Calculate camera target from borehole positions
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
            // Build borehole casing mesh
            var casingMesh = BuildBoreholeCasingMesh(borehole);
            if (casingMesh.IndexCount > 0)
                _boreholeMeshes.Add(casingMesh);

            // Build lithology column mesh
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

        // Create cylinder
        int segments = 16;
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

            // Two triangles per segment
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
        var radius = _boreholeRadius * 0.8f; // Slightly smaller than casing

        foreach (var unit in borehole.LithologyUnits)
        {
            var topZ = (surfaceElev - unit.DepthFrom) * _verticalExaggeration;
            var bottomZ = (surfaceElev - unit.DepthTo) * _verticalExaggeration;
            var color = unit.Color;

            // Create cylinder for this unit
            int segments = 16;
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

    private void BuildCorrelationLinesMesh()
    {
        _correlationLinesMesh.Dispose();

        var vertices = new List<VertexData>();
        var indices = new List<uint>();

        foreach (var correlation in _correlationData.Correlations)
        {
            var sourceBorehole = _boreholeMap.GetValueOrDefault(correlation.SourceBoreholeID);
            var targetBorehole = _boreholeMap.GetValueOrDefault(correlation.TargetBoreholeID);

            if (sourceBorehole == null || targetBorehole == null) continue;

            var sourceUnit = sourceBorehole.LithologyUnits.FirstOrDefault(u => u.ID == correlation.SourceLithologyID);
            var targetUnit = targetBorehole.LithologyUnits.FirstOrDefault(u => u.ID == correlation.TargetLithologyID);

            if (sourceUnit == null || targetUnit == null) continue;

            // Calculate 3D positions taking elevation into account
            var sourceX = sourceBorehole.SurfaceCoordinates.X;
            var sourceY = sourceBorehole.SurfaceCoordinates.Y;
            var sourceDepthMid = (sourceUnit.DepthFrom + sourceUnit.DepthTo) / 2;
            var sourceZ = (sourceBorehole.Elevation - sourceDepthMid) * _verticalExaggeration;

            var targetX = targetBorehole.SurfaceCoordinates.X;
            var targetY = targetBorehole.SurfaceCoordinates.Y;
            var targetDepthMid = (targetUnit.DepthFrom + targetUnit.DepthTo) / 2;
            var targetZ = (targetBorehole.Elevation - targetDepthMid) * _verticalExaggeration;

            var color = correlation.Color;
            var baseIdx = (uint)vertices.Count;

            // Create thick line using two triangles
            var dir = Vector3.Normalize(new Vector3(targetX - sourceX, targetY - sourceY, targetZ - sourceZ));
            var perpHoriz = Vector3.Cross(dir, Vector3.UnitZ);
            if (perpHoriz.Length() < 0.001f)
                perpHoriz = Vector3.UnitX;
            perpHoriz = Vector3.Normalize(perpHoriz) * _correlationLineWidth;

            var p1 = new Vector3(sourceX, sourceY, sourceZ) - perpHoriz;
            var p2 = new Vector3(sourceX, sourceY, sourceZ) + perpHoriz;
            var p3 = new Vector3(targetX, targetY, targetZ) - perpHoriz;
            var p4 = new Vector3(targetX, targetY, targetZ) + perpHoriz;

            vertices.Add(new VertexData(p1, color));
            vertices.Add(new VertexData(p2, color));
            vertices.Add(new VertexData(p3, color));
            vertices.Add(new VertexData(p4, color));

            indices.AddRange(new uint[] { baseIdx, baseIdx + 1, baseIdx + 2 });
            indices.AddRange(new uint[] { baseIdx + 1, baseIdx + 3, baseIdx + 2 });
        }

        _correlationLinesMesh = CreateGpuMesh(vertices, indices, false);
    }

    private void BuildHorizonSurfaceMesh()
    {
        _horizonSurfaceMesh.Dispose();

        if (_correlationData.Horizons.Count == 0) return;

        var vertices = new List<VertexData>();
        var indices = new List<uint>();

        foreach (var horizon in _correlationData.Horizons)
        {
            if (horizon.LithologyUnits.Count < 3) continue;

            // Gather 3D points for this horizon
            var points3D = new List<Vector3>();
            foreach (var (boreholeID, lithologyID) in horizon.LithologyUnits)
            {
                if (!_boreholeMap.TryGetValue(boreholeID, out var borehole)) continue;
                var unit = borehole.LithologyUnits.FirstOrDefault(u => u.ID == lithologyID);
                if (unit == null) continue;

                var x = borehole.SurfaceCoordinates.X;
                var y = borehole.SurfaceCoordinates.Y;
                var z = (borehole.Elevation - unit.DepthFrom) * _verticalExaggeration;
                points3D.Add(new Vector3(x, y, z));
            }

            if (points3D.Count < 3) continue;

            // Simple triangulation using delaunay-like approach (fan triangulation from centroid)
            var centroid = points3D.Aggregate(Vector3.Zero, (acc, p) => acc + p) / points3D.Count;

            // Sort points by angle around centroid
            var sortedPoints = points3D
                .Select(p => new { Point = p, Angle = MathF.Atan2(p.Y - centroid.Y, p.X - centroid.X) })
                .OrderBy(x => x.Angle)
                .Select(x => x.Point)
                .ToList();

            var color = new Vector4(horizon.Color.X, horizon.Color.Y, horizon.Color.Z, 0.5f);
            var baseIdx = (uint)vertices.Count;

            // Add centroid
            vertices.Add(new VertexData(centroid, color, Vector3.UnitZ));

            // Add surrounding points
            foreach (var p in sortedPoints)
                vertices.Add(new VertexData(p, color, Vector3.UnitZ));

            // Create fan triangles
            for (int i = 0; i < sortedPoints.Count; i++)
            {
                var next = (i + 1) % sortedPoints.Count;
                indices.Add(baseIdx); // centroid
                indices.Add(baseIdx + 1 + (uint)i);
                indices.Add(baseIdx + 1 + (uint)next);
            }
        }

        _horizonSurfaceMesh = CreateGpuMesh(vertices, indices, false);
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

                // Sample elevation from DEM
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

        // Create triangles
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
        cl.ClearColorTarget(0, new RgbaFloat(0.1f, 0.1f, 0.12f, 1f));
        cl.ClearDepthStencil(1f);

        // Update uniform buffer
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
            cl.SetPipeline(_solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.SetVertexBuffer(0, _topographyMesh.VertexBuffer);
            cl.SetIndexBuffer(_topographyMesh.IndexBuffer, IndexFormat.UInt32);
            cl.DrawIndexed(_topographyMesh.IndexCount);
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

        // Render horizon surfaces
        if (_showHorizonSurfaces && _horizonSurfaceMesh.IndexCount > 0)
        {
            cl.SetPipeline(_solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.SetVertexBuffer(0, _horizonSurfaceMesh.VertexBuffer);
            cl.SetIndexBuffer(_horizonSurfaceMesh.IndexBuffer, IndexFormat.UInt32);
            cl.DrawIndexed(_horizonSurfaceMesh.IndexCount);
        }

        // Render correlation lines
        if (_showCorrelationLines && _correlationLinesMesh.IndexCount > 0)
        {
            cl.SetPipeline(_solidPipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.SetVertexBuffer(0, _correlationLinesMesh.VertexBuffer);
            cl.SetIndexBuffer(_correlationLinesMesh.IndexBuffer, IndexFormat.UInt32);
            cl.DrawIndexed(_correlationLinesMesh.IndexCount);
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

        ImGui.SetNextWindowSize(new Vector2(1000, 700), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("3D Borehole Correlation View", ref _isOpen, ImGuiWindowFlags.MenuBar))
        {
            DrawMenuBar();
            DrawToolbar();
            DrawViewport();
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
                if (ImGui.MenuItem("Close"))
                    _isOpen = false;
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                ImGui.Checkbox("Show Topography", ref _showTopography);
                ImGui.Checkbox("Show Boreholes", ref _showBoreholes);
                ImGui.Checkbox("Show Lithology", ref _showLithology);
                ImGui.Checkbox("Show Correlation Lines", ref _showCorrelationLines);
                ImGui.Checkbox("Show Horizon Surfaces", ref _showHorizonSurfaces);
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

            if (ImGui.BeginMenu("Data"))
            {
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

    private void DrawDEMMenu()
    {
        // List available DEM/raster datasets
        var rasterDatasets = ProjectManager.Instance.LoadedDatasets
            .OfType<GISDataset>()
            .SelectMany(g => g.Layers)
            .OfType<GISRasterLayer>()
            .ToList();

        if (rasterDatasets.Count == 0)
        {
            ImGui.TextDisabled("No raster datasets available");
            ImGui.TextDisabled("Load a DEM/heightmap first");
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
            _demDatasetName = null;
            _topographyMesh.Dispose();
            _topographyMesh = default;
            _statusMessage = "DEM cleared";
        }
    }

    private void DrawToolbar()
    {
        ImGui.BeginChild("Toolbar3D", new Vector2(0, 35), ImGuiChildFlags.None);

        ImGui.Text("Vertical Exaggeration:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.SliderFloat("##VExag", ref _verticalExaggeration, 1f, 10f, "%.1fx"))
        {
            BuildMeshes();
        }

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        ImGui.Text("Borehole Radius:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.SliderFloat("##BRadius", ref _boreholeRadius, 1f, 10f, "%.1f"))
        {
            BuildBoreholeMeshes();
        }

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        ImGui.Text("DEM Offset:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.SliderFloat("##DEMOffset", ref _demElevationOffset, -100f, 100f, "%.0fm"))
        {
            if (_demLayer != null)
                BuildTopographyMesh();
        }

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        if (!string.IsNullOrEmpty(_statusMessage))
            ImGui.TextDisabled(_statusMessage);

        ImGui.EndChild();
        ImGui.Separator();
    }

    private void DrawViewport()
    {
        var viewportSize = ImGui.GetContentRegionAvail();
        if (viewportSize.X < 10 || viewportSize.Y < 10) return;

        // Resize render target if needed
        if ((uint)viewportSize.X != _renderWidth || (uint)viewportSize.Y != _renderHeight)
        {
            _renderWidth = (uint)viewportSize.X;
            _renderHeight = (uint)viewportSize.Y;
            CreateRenderTarget(_renderWidth, _renderHeight);
            CreateResourceSet();
            UpdateCamera();
        }

        // Handle input
        HandleInput();

        // Render
        Render();

        // Display render target
        ImGui.Image((IntPtr)_renderTargetView.GetHashCode(), viewportSize);
    }

    private void HandleInput()
    {
        if (!ImGui.IsWindowHovered()) return;

        var io = ImGui.GetIO();
        var mouse = io.MousePos;

        // Rotate with left mouse
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

        // Pan with middle mouse
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

        // Zoom with scroll
        if (io.MouseWheel != 0)
        {
            _cameraDistance *= io.MouseWheel > 0 ? 0.9f : 1.1f;
            _cameraDistance = Math.Clamp(_cameraDistance, 10f, 10000f);
            UpdateCamera();
        }
    }

    #endregion

    #region Export

    private void ExportToSubsurfaceGIS()
    {
        try
        {
            var subsurfaceGIS = new SubsurfaceGISDataset(
                $"Correlation_{_correlationData.Name}",
                "");

            // Build from boreholes
            subsurfaceGIS.BuildFromBoreholes(_boreholes, _demLayer);

            // Add to project
            ProjectManager.Instance.AddDataset(subsurfaceGIS);

            _statusMessage = "Exported to SubsurfaceGIS";
            Logger.Log("[BoreholeCorrelation3DViewer] Exported to SubsurfaceGIS");
        }
        catch (Exception ex)
        {
            _statusMessage = $"Export failed: {ex.Message}";
            Logger.LogError($"Export to SubsurfaceGIS failed: {ex.Message}");
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
    float ambient = 0.3;
    float diffuse = max(dot(normalize(fragNormal), lightDir), 0.0) * 0.7;
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
        _correlationLinesMesh.Dispose();
        _horizonSurfaceMesh.Dispose();

        _solidPipeline?.Dispose();
        _wireframePipeline?.Dispose();
        _resourceLayout?.Dispose();
        _resourceSet?.Dispose();
        _uniformBuffer?.Dispose();
        _renderTarget?.Dispose();
        _renderTargetView?.Dispose();
        _depthTexture?.Dispose();
        _framebuffer?.Dispose();

        Logger.Log("[BoreholeCorrelation3DViewer] Disposed");
    }
}
