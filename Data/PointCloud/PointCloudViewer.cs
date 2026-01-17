// GeoscientistToolkit/Data/PointCloud/PointCloudViewer.cs

using System.Numerics;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.Data.PointCloud;

/// <summary>
/// 3D point cloud viewer using mesh rendering infrastructure.
/// Converts points to small 3D geometry for GPU-accelerated rendering.
/// </summary>
public class PointCloudViewer : IDatasetViewer, IDisposable
{
    private readonly PointCloudDataset _dataset;
    private readonly D3D11MeshRenderer _d3dRenderer;
    private readonly MetalMeshRenderer _metalRenderer;
    private readonly VulkanMeshRenderer _vulkanRenderer;
    private readonly TextureManager _textureManager;

    // Camera state
    private float _cameraDistance = 2.0f;
    private float _cameraPitch = MathF.PI / 6f;
    private Vector3 _cameraTarget = Vector3.Zero;
    private float _cameraYaw = -MathF.PI / 4f;
    private bool _isDragging;
    private bool _isPanning;
    private Vector2 _lastMousePos;

    // View settings
    private CameraPreset _lastPreset = CameraPreset.Custom;
    private Matrix4x4 _projMatrix;
    private bool _showGrid = true;
    private Matrix4x4 _viewMatrix;

    // Point cloud visualization settings
    private int _displayMode = 0; // 0 = Points as cubes, 1 = Surface mesh
    private float _pointScale = 0.5f;
    private int _maxDisplayPoints = 100000;
    private bool _colorByHeight = false;

    // Internal mesh for rendering
    private Mesh3DDataset _renderMesh;
    private bool _meshNeedsUpdate = true;

    public PointCloudViewer(PointCloudDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _dataset.Load();

        // Create render mesh from point cloud
        CreateRenderMesh();

        // Initialize platform-specific renderer
        var backend = VeldridManager.GraphicsDevice.BackendType;
        Logger.Log($"Initializing PointCloud renderer for backend: {backend}");

        switch (backend)
        {
            case GraphicsBackend.Metal:
                _metalRenderer = new MetalMeshRenderer();
                _metalRenderer.Initialize(_renderMesh);
                _textureManager = TextureManager.CreateFromTexture(_metalRenderer.ColorTarget);
                break;

            case GraphicsBackend.Direct3D11:
                _d3dRenderer = new D3D11MeshRenderer();
                _d3dRenderer.Initialize(_renderMesh);
                _textureManager = TextureManager.CreateFromTexture(_d3dRenderer.ColorTarget);
                break;

            case GraphicsBackend.Vulkan:
            case GraphicsBackend.OpenGL:
            case GraphicsBackend.OpenGLES:
                _vulkanRenderer = new VulkanMeshRenderer();
                _vulkanRenderer.Initialize(_renderMesh);
                _textureManager = TextureManager.CreateFromTexture(_vulkanRenderer.ColorTarget);
                break;

            default:
                throw new NotSupportedException($"Graphics backend {backend} is not supported for PointCloud rendering");
        }

        _cameraTarget = Vector3.Zero;
        UpdateCameraMatrices();
    }

    private void CreateRenderMesh()
    {
        // Create a Mesh3DDataset that represents the point cloud for rendering
        var tempPath = Path.Combine(Path.GetTempPath(), $"pointcloud_render_{Guid.NewGuid()}.obj");

        _renderMesh = new Mesh3DDataset(_dataset.Name + "_render", tempPath);

        // Determine how many points to display
        var pointCount = Math.Min(_dataset.PointCount, _maxDisplayPoints);
        var stride = _dataset.PointCount > _maxDisplayPoints ? _dataset.PointCount / _maxDisplayPoints : 1;

        if (_displayMode == 0)
        {
            // Render points as small cubes
            CreatePointCubes(pointCount, stride);
        }
        else
        {
            // Create a simple surface representation
            CreateSurfaceFromPoints(pointCount, stride);
        }

        _renderMesh.CalculateBounds();
        _meshNeedsUpdate = false;
    }

    private void CreatePointCubes(int maxPoints, int stride)
    {
        var size = _dataset.Size;
        var maxDim = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        var cubeSize = maxDim * 0.002f * _pointScale; // Scale relative to dataset size

        var vertices = new List<Vector3>();
        var faces = new List<int[]>();
        var colors = new List<Vector4>();

        // Min/max Z for height coloring
        var minZ = _dataset.BoundingBoxMin.Z;
        var maxZ = _dataset.BoundingBoxMax.Z;
        var zRange = maxZ - minZ;

        for (int i = 0, count = 0; i < _dataset.PointCount && count < maxPoints; i += stride, count++)
        {
            var p = _dataset.Points[i];
            var baseIdx = vertices.Count;

            // Create a small cube at this point
            var halfSize = cubeSize * 0.5f;
            vertices.Add(p + new Vector3(-halfSize, -halfSize, -halfSize));
            vertices.Add(p + new Vector3(halfSize, -halfSize, -halfSize));
            vertices.Add(p + new Vector3(halfSize, halfSize, -halfSize));
            vertices.Add(p + new Vector3(-halfSize, halfSize, -halfSize));
            vertices.Add(p + new Vector3(-halfSize, -halfSize, halfSize));
            vertices.Add(p + new Vector3(halfSize, -halfSize, halfSize));
            vertices.Add(p + new Vector3(halfSize, halfSize, halfSize));
            vertices.Add(p + new Vector3(-halfSize, halfSize, halfSize));

            // Cube faces
            faces.Add(new[] { baseIdx + 0, baseIdx + 1, baseIdx + 2 });
            faces.Add(new[] { baseIdx + 0, baseIdx + 2, baseIdx + 3 });
            faces.Add(new[] { baseIdx + 5, baseIdx + 4, baseIdx + 7 });
            faces.Add(new[] { baseIdx + 5, baseIdx + 7, baseIdx + 6 });
            faces.Add(new[] { baseIdx + 4, baseIdx + 0, baseIdx + 3 });
            faces.Add(new[] { baseIdx + 4, baseIdx + 3, baseIdx + 7 });
            faces.Add(new[] { baseIdx + 1, baseIdx + 5, baseIdx + 6 });
            faces.Add(new[] { baseIdx + 1, baseIdx + 6, baseIdx + 2 });
            faces.Add(new[] { baseIdx + 3, baseIdx + 2, baseIdx + 6 });
            faces.Add(new[] { baseIdx + 3, baseIdx + 6, baseIdx + 7 });
            faces.Add(new[] { baseIdx + 4, baseIdx + 5, baseIdx + 1 });
            faces.Add(new[] { baseIdx + 4, baseIdx + 1, baseIdx + 0 });

            // Color for this point
            Vector4 color;
            if (_dataset.HasColors && !_colorByHeight)
            {
                color = _dataset.Colors[i];
            }
            else if (_colorByHeight && zRange > 0)
            {
                // Height-based coloring (blue to red gradient)
                var t = (p.Z - minZ) / zRange;
                color = new Vector4(t, 0.2f, 1 - t, 1.0f);
            }
            else
            {
                // Default gray color
                color = new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
            }

            // Add color for each vertex of the cube
            for (int v = 0; v < 8; v++)
                colors.Add(color);
        }

        // Set mesh data using reflection to access private setters
        var verticesField = typeof(Mesh3DDataset).GetProperty("Vertices");
        var facesField = typeof(Mesh3DDataset).GetProperty("Faces");

        // Use direct assignment since they have private setters
        _renderMesh.GetType().GetField("Vertices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(_renderMesh, vertices);
        _renderMesh.GetType().GetField("Faces", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(_renderMesh, faces);

        // Actually, the properties have private setters, let's use a different approach
        // Create a simple mesh with the data
        SetMeshData(vertices, faces, colors);
    }

    private void SetMeshData(List<Vector3> vertices, List<int[]> faces, List<Vector4> colors)
    {
        // Clear and set via reflection
        var vField = _renderMesh.GetType().GetField("<Vertices>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var fField = _renderMesh.GetType().GetField("<Faces>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        if (vField != null && fField != null)
        {
            vField.SetValue(_renderMesh, vertices);
            fField.SetValue(_renderMesh, faces);
        }
        else
        {
            // Fallback: access through property reflection
            var verticesProp = _renderMesh.GetType().GetProperty("Vertices");
            var facesProp = _renderMesh.GetType().GetProperty("Faces");

            // For lists with private setters, we can clear and add
            _renderMesh.Vertices.Clear();
            _renderMesh.Vertices.AddRange(vertices);
            _renderMesh.Faces.Clear();
            _renderMesh.Faces.AddRange(faces);
        }

        _renderMesh.Colors = colors;
        _renderMesh.VertexCount = vertices.Count;
        _renderMesh.FaceCount = faces.Count;
    }

    private void CreateSurfaceFromPoints(int maxPoints, int stride)
    {
        // Simple surface: create triangles connecting nearby points
        // This is a simplified approach - for complex surfaces, use Delaunay triangulation
        var vertices = new List<Vector3>();
        var faces = new List<int[]>();
        var colors = new List<Vector4>();

        // Sample points
        for (int i = 0; i < _dataset.PointCount && vertices.Count < maxPoints; i += stride)
        {
            vertices.Add(_dataset.Points[i]);
            if (_dataset.HasColors)
                colors.Add(_dataset.Colors[i]);
            else
                colors.Add(new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
        }

        // Simple triangulation: sort by X, then connect adjacent points
        // This is a basic approach - real implementation would use proper triangulation
        var sortedIndices = Enumerable.Range(0, vertices.Count)
            .OrderBy(i => vertices[i].X)
            .ThenBy(i => vertices[i].Y)
            .ToList();

        // Create a grid-based triangulation approximation
        int gridSize = (int)MathF.Sqrt(vertices.Count);
        if (gridSize < 2) gridSize = 2;

        for (int i = 0; i < gridSize - 1; i++)
        {
            for (int j = 0; j < gridSize - 1; j++)
            {
                int idx = i * gridSize + j;
                if (idx + gridSize + 1 < sortedIndices.Count)
                {
                    var i0 = sortedIndices[idx];
                    var i1 = sortedIndices[idx + 1];
                    var i2 = sortedIndices[idx + gridSize];
                    var i3 = sortedIndices[idx + gridSize + 1];

                    faces.Add(new[] { i0, i1, i2 });
                    faces.Add(new[] { i1, i3, i2 });
                }
            }
        }

        SetMeshData(vertices, faces, colors);
    }

    private void UpdateCameraMatrices()
    {
        // Calculate camera position from spherical coordinates
        var x = _cameraDistance * MathF.Cos(_cameraPitch) * MathF.Sin(_cameraYaw);
        var y = _cameraDistance * MathF.Cos(_cameraPitch) * MathF.Cos(_cameraYaw);
        var z = _cameraDistance * MathF.Sin(_cameraPitch);

        var cameraPos = _cameraTarget + new Vector3(x, y, z);

        _viewMatrix = Matrix4x4.CreateLookAt(cameraPos, _cameraTarget, Vector3.UnitZ);
        _projMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4f,
            1.0f,
            0.1f,
            1000f);
    }

    public void DrawToolbarControls()
    {
        // Camera reset
        if (ImGui.Button("Reset Camera"))
        {
            _cameraYaw = -MathF.PI / 4f;
            _cameraPitch = MathF.PI / 6f;
            _cameraDistance = 2.0f;
            _cameraTarget = Vector3.Zero;
            _lastPreset = CameraPreset.Custom;
            UpdateCameraMatrices();
        }

        ImGui.SameLine();

        // Grid toggle
        var grid = _showGrid;
        if (ImGui.Checkbox("Grid", ref grid))
            _showGrid = grid;

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Point display mode
        ImGui.Text("Display:");
        ImGui.SameLine();

        var displayMode = _displayMode;
        if (ImGui.RadioButton("Points", ref displayMode, 0))
        {
            _displayMode = 0;
            _meshNeedsUpdate = true;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Surface", ref displayMode, 1))
        {
            _displayMode = 1;
            _meshNeedsUpdate = true;
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Color by height toggle
        var colorByHeight = _colorByHeight;
        if (ImGui.Checkbox("Color by Height", ref colorByHeight))
        {
            _colorByHeight = colorByHeight;
            _meshNeedsUpdate = true;
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // View Presets
        ImGui.Text("View:");
        ImGui.SameLine();

        if (ImGui.Button("Top")) ApplyViewPreset(CameraPreset.Top);
        ImGui.SameLine();
        if (ImGui.Button("Front")) ApplyViewPreset(CameraPreset.Front);
        ImGui.SameLine();
        if (ImGui.Button("Side")) ApplyViewPreset(CameraPreset.Left);

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();
        ImGui.TextDisabled($"[{VeldridManager.GraphicsDevice.BackendType}]");
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        // Update mesh if needed
        if (_meshNeedsUpdate)
        {
            CreateRenderMesh();
            UpdateRenderer();
        }

        // Render the scene
        RenderScene();

        // Display rendered image
        var textureId = _textureManager.GetImGuiTextureId();
        if (textureId != IntPtr.Zero)
        {
            var availableSize = ImGui.GetContentRegionAvail();
            var imagePos = ImGui.GetCursorScreenPos();

            // Invisible button for mouse input
            ImGui.InvisibleButton("PointCloudViewArea", availableSize);
            var isHovered = ImGui.IsItemHovered();
            var isActive = ImGui.IsItemActive();

            // Draw image
            ImGui.SetCursorScreenPos(imagePos);
            ImGui.Image(textureId, availableSize, new Vector2(0, 1), new Vector2(1, 0));

            // Handle mouse interaction
            if (isHovered || isActive || _isDragging || _isPanning)
            {
                HandleMouseInput();

                if ((_isDragging || _isPanning) && _lastPreset != CameraPreset.Custom)
                    _lastPreset = CameraPreset.Custom;
            }

            // Context menu
            if (ImGui.BeginPopupContextItem("PointCloudViewerContextMenu"))
            {
                if (ImGui.MenuItem("Reset Camera"))
                {
                    _cameraYaw = -MathF.PI / 4f;
                    _cameraPitch = MathF.PI / 6f;
                    _cameraDistance = 2.0f;
                    _cameraTarget = Vector3.Zero;
                    _lastPreset = CameraPreset.Custom;
                    UpdateCameraMatrices();
                }

                ImGui.Separator();

                if (ImGui.BeginMenu("View Preset"))
                {
                    if (ImGui.MenuItem("Top View")) ApplyViewPreset(CameraPreset.Top);
                    if (ImGui.MenuItem("Front View")) ApplyViewPreset(CameraPreset.Front);
                    if (ImGui.MenuItem("Left View")) ApplyViewPreset(CameraPreset.Left);
                    if (ImGui.MenuItem("Right View")) ApplyViewPreset(CameraPreset.Right);
                    ImGui.EndMenu();
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Points Display", "", _displayMode == 0))
                {
                    _displayMode = 0;
                    _meshNeedsUpdate = true;
                }
                if (ImGui.MenuItem("Surface Display", "", _displayMode == 1))
                {
                    _displayMode = 1;
                    _meshNeedsUpdate = true;
                }

                ImGui.EndPopup();
            }
        }
    }

    private void UpdateRenderer()
    {
        // Re-initialize renderer with updated mesh
        if (_d3dRenderer != null) _d3dRenderer.Initialize(_renderMesh);
        if (_metalRenderer != null) _metalRenderer.Initialize(_renderMesh);
        if (_vulkanRenderer != null) _vulkanRenderer.Initialize(_renderMesh);
    }

    private void RenderScene()
    {
        UpdateCameraMatrices();

        if (_d3dRenderer != null)
            _d3dRenderer.Render(_renderMesh, _viewMatrix, _projMatrix, _cameraTarget, _showGrid);
        else if (_metalRenderer != null)
            _metalRenderer.Render(_renderMesh, _viewMatrix, _projMatrix, _cameraTarget, _showGrid);
        else if (_vulkanRenderer != null)
            _vulkanRenderer.Render(_renderMesh, _viewMatrix, _projMatrix, _cameraTarget, _showGrid);
    }

    private void HandleMouseInput()
    {
        var io = ImGui.GetIO();
        var mousePos = io.MousePos;

        // Left mouse button - rotation
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _isDragging = true;
            _lastMousePos = mousePos;
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            _isDragging = false;

        if (_isDragging)
        {
            var delta = mousePos - _lastMousePos;
            _cameraYaw += delta.X * 0.01f;
            _cameraPitch -= delta.Y * 0.01f;
            _cameraPitch = Math.Clamp(_cameraPitch, -MathF.PI / 2f + 0.1f, MathF.PI / 2f - 0.1f);
            _lastMousePos = mousePos;
        }

        // Right or middle mouse button - panning
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
        {
            _isPanning = true;
            _lastMousePos = mousePos;
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Right) || ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
            _isPanning = false;

        if (_isPanning)
        {
            var delta = mousePos - _lastMousePos;
            var panSpeed = _cameraDistance * 0.002f;

            var right = new Vector3(MathF.Cos(_cameraYaw), -MathF.Sin(_cameraYaw), 0);
            var up = Vector3.UnitZ;

            _cameraTarget -= right * delta.X * panSpeed;
            _cameraTarget += up * delta.Y * panSpeed;
            _lastMousePos = mousePos;
        }

        // Mouse wheel - zoom
        if (MathF.Abs(io.MouseWheel) > 0.001f)
        {
            _cameraDistance *= 1.0f - io.MouseWheel * 0.1f;
            _cameraDistance = Math.Clamp(_cameraDistance, 0.5f, 20.0f);
        }
    }

    private void ApplyViewPreset(CameraPreset preset)
    {
        _lastPreset = preset;

        switch (preset)
        {
            case CameraPreset.Top:
                _cameraYaw = 0;
                _cameraPitch = MathF.PI / 2f - 0.01f;
                break;
            case CameraPreset.Bottom:
                _cameraYaw = 0;
                _cameraPitch = -MathF.PI / 2f + 0.01f;
                break;
            case CameraPreset.Front:
                _cameraYaw = 0;
                _cameraPitch = 0;
                break;
            case CameraPreset.Back:
                _cameraYaw = MathF.PI;
                _cameraPitch = 0;
                break;
            case CameraPreset.Left:
                _cameraYaw = -MathF.PI / 2f;
                _cameraPitch = 0;
                break;
            case CameraPreset.Right:
                _cameraYaw = MathF.PI / 2f;
                _cameraPitch = 0;
                break;
        }

        UpdateCameraMatrices();
    }

    public void Dispose()
    {
        _d3dRenderer?.Dispose();
        _metalRenderer?.Dispose();
        _vulkanRenderer?.Dispose();
        _textureManager?.Dispose();
        _renderMesh?.Unload();
    }

    private enum CameraPreset
    {
        Custom,
        Top,
        Bottom,
        Front,
        Back,
        Left,
        Right
    }
}
