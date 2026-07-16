// GAIA/UI/Borehole/BoreholeCorrelation3DViewer.cs

using System.Numerics;
using System.Runtime.InteropServices;
using GAIA.Data;
using GAIA.Data.Borehole;
using GAIA.Data.GIS;
using GAIA.Data.Mesh3D;
using GAIA.Business;
using GAIA.Util;
using ImGuiNET;
using OpenTK.Graphics.OpenGL;

namespace GAIA.UI.Borehole;

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
        public int VertexArray;
        public int VertexBuffer;
        public int IndexBuffer;
        public uint IndexCount;
        public bool IsLineList;

        public void Dispose()
        {
            if (VertexBuffer != 0) GL.DeleteBuffer(VertexBuffer);
            if (IndexBuffer != 0) GL.DeleteBuffer(IndexBuffer);
            if (VertexArray != 0) GL.DeleteVertexArray(VertexArray);
        }
    }

    #endregion

    // Data
    private readonly BoreholeLogCorrelationDataset _correlationData;
    private readonly List<BoreholeDataset> _boreholes = new();
    private readonly Dictionary<string, BoreholeDataset> _boreholeMap = new();

    // GPU Resources
    private int _framebuffer;
    private int _renderTarget;
    private int _depthTexture;
    private uint _renderWidth = 800;
    private uint _renderHeight = 600;

    private int _shaderProgram;

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
        BoreholeLogCorrelationDataset correlationData,
        List<BoreholeDataset> boreholes)
    {
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
    }

    private void CreateRenderTarget(uint width, uint height)
    {
        if (_renderTarget != 0) GL.DeleteTexture(_renderTarget);
        if (_depthTexture != 0) GL.DeleteRenderbuffer(_depthTexture);
        if (_framebuffer != 0) GL.DeleteFramebuffer(_framebuffer);

        _renderWidth = width;
        _renderHeight = height;

        _framebuffer = GL.GenFramebuffer();
        _renderTarget = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _renderTarget);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, (int)width, (int)height,
            0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _depthTexture = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthTexture);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent24,
            (int)width, (int)height);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _renderTarget, 0);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _depthTexture);
        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException("Borehole OpenGL framebuffer is incomplete.");
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void CreatePipelines()
    {
        if (_shaderProgram != 0) return;
        _shaderProgram = CreateProgram(OpenGlVertexShader, OpenGlFragmentShader);
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

        var vao = GL.GenVertexArray();
        var vb = GL.GenBuffer();
        var ib = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vb);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * Marshal.SizeOf<VertexData>(),
            vertices.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ib);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint),
            indices.ToArray(), BufferUsageHint.StaticDraw);
        var stride = Marshal.SizeOf<VertexData>();
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 12);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 28);
        GL.BindVertexArray(0);

        return new GpuMesh
        {
            VertexArray = vao,
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
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        GL.Viewport(0, 0, (int)_renderWidth, (int)_renderHeight);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.ClearColor(0.1f, 0.1f, 0.12f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        GL.UseProgram(_shaderProgram);
        SetMatrix(_shaderProgram, "uMvp", _viewMatrix * _projectionMatrix);
        GL.Uniform3(GL.GetUniformLocation(_shaderProgram, "uLight"), 0f, 0f, 1000f);

        if (_showTopography) DrawMesh(_topographyMesh);
        if (_showBoreholes) foreach (var mesh in _boreholeMeshes) DrawMesh(mesh);
        if (_showLithology) foreach (var mesh in _lithologyMeshes) DrawMesh(mesh);
        if (_showHorizonSurfaces) DrawMesh(_horizonSurfaceMesh);
        if (_showCorrelationLines) DrawMesh(_correlationLinesMesh);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
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
            UpdateCamera();
        }

        // Handle input
        HandleInput();

        // Render
        Render();

        // Display render target
        ImGui.Image((IntPtr)_renderTarget, viewportSize, new Vector2(0, 1), new Vector2(1, 0));
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

    private void DrawMesh(GpuMesh mesh)
    {
        if (mesh.IndexCount == 0 || mesh.VertexArray == 0) return;
        GL.BindVertexArray(mesh.VertexArray);
        if (mesh.IsLineList) GL.LineWidth(Math.Clamp(_correlationLineWidth, 1f, 10f));
        GL.DrawElements(mesh.IsLineList ? PrimitiveType.Lines : PrimitiveType.Triangles,
            checked((int)mesh.IndexCount), DrawElementsType.UnsignedInt, 0);
    }

    private static void SetMatrix(int program, string name, Matrix4x4 matrix)
    {
        var values = new[] { matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22,
            matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41,
            matrix.M42, matrix.M43, matrix.M44 };
        // System.Numerics is row-vector (v*M); GLSL is column-vector (M*v). Passing the row-major
        // array with transpose=false makes GL read it column-major, which is the transpose GLSL needs.
        GL.UniformMatrix4(GL.GetUniformLocation(program, name), 1, false, values);
    }

    private static int CreateProgram(string vertexSource, string fragmentSource)
    {
        static int Compile(ShaderType type, string source)
        {
            var shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out var success);
            if (success == 0) throw new InvalidOperationException(GL.GetShaderInfoLog(shader));
            return shader;
        }
        var vertex = Compile(ShaderType.VertexShader, vertexSource);
        var fragment = Compile(ShaderType.FragmentShader, fragmentSource);
        var program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var linked);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        if (linked == 0) throw new InvalidOperationException(GL.GetProgramInfoLog(program));
        return program;
    }

    private const string OpenGlVertexShader = @"#version 330 core
layout(location=0) in vec3 Position;
layout(location=1) in vec4 Color;
layout(location=2) in vec3 Normal;
uniform mat4 uMvp;
out vec4 fragColor; out vec3 fragNormal; out vec3 fragWorldPos;
void main(){ gl_Position=uMvp*vec4(Position,1); fragColor=Color; fragNormal=Normal; fragWorldPos=Position; }";

    private const string OpenGlFragmentShader = @"#version 330 core
in vec4 fragColor; in vec3 fragNormal; in vec3 fragWorldPos;
uniform vec3 uLight; out vec4 outColor;
void main(){ vec3 n=normalize(fragNormal); float d=.3+.7*max(dot(n,normalize(uLight-fragWorldPos)),0); outColor=vec4(fragColor.rgb*d,fragColor.a); }";

    #endregion

    public void Dispose()
    {
        _topographyMesh.Dispose();
        foreach (var mesh in _boreholeMeshes) mesh.Dispose();
        foreach (var mesh in _lithologyMeshes) mesh.Dispose();
        _correlationLinesMesh.Dispose();
        _horizonSurfaceMesh.Dispose();

        if (_shaderProgram != 0) GL.DeleteProgram(_shaderProgram);
        if (_renderTarget != 0) GL.DeleteTexture(_renderTarget);
        if (_depthTexture != 0) GL.DeleteRenderbuffer(_depthTexture);
        if (_framebuffer != 0) GL.DeleteFramebuffer(_framebuffer);

        Logger.Log("[BoreholeCorrelation3DViewer] Disposed");
    }
}
