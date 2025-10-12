using System.Numerics;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.Data.Mesh3D;

/// <summary>
///     3D model viewer for Mesh3DDataset, with editor and view presets support.
/// </summary>
public class Mesh3DViewer : IDatasetViewer, IDisposable
{
    private readonly D3D11MeshRenderer _d3dRenderer;
    private readonly Mesh3DDataset _dataset;
    private readonly Mesh3DEditor _editor;
    private readonly MetalMeshRenderer _metalRenderer;
    private readonly TextureManager _textureManager;
    private readonly VulkanMeshRenderer _vulkanRenderer;

    private float _cameraDistance = 2.0f;
    private float _cameraPitch = MathF.PI / 6f;
    private Vector3 _cameraTarget = Vector3.Zero;
    private float _cameraYaw = -MathF.PI / 4f;
    private bool _isDragging;
    private bool _isPanning;
    private Vector2 _lastMousePos;

    // View presets
    private CameraPreset _lastPreset = CameraPreset.Custom;
    private Matrix4x4 _projMatrix;
    private bool _showGrid = true;
    private Matrix4x4 _viewMatrix;

    public Mesh3DViewer(Mesh3DDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _dataset.Load();

        // Initialize editor
        _editor = new Mesh3DEditor(_dataset);

        // Initialize platform-specific renderer
        var backend = VeldridManager.GraphicsDevice.BackendType;
        Logger.Log($"Initializing Mesh3D renderer for backend: {backend}");

        switch (backend)
        {
            case GraphicsBackend.Metal:
                _metalRenderer = new MetalMeshRenderer();
                _metalRenderer.Initialize(_dataset);
                _textureManager = TextureManager.CreateFromTexture(_metalRenderer.ColorTarget);
                break;

            case GraphicsBackend.Direct3D11:
                _d3dRenderer = new D3D11MeshRenderer();
                _d3dRenderer.Initialize(_dataset);
                _textureManager = TextureManager.CreateFromTexture(_d3dRenderer.ColorTarget);
                break;

            case GraphicsBackend.Vulkan:
                _vulkanRenderer = new VulkanMeshRenderer();
                _vulkanRenderer.Initialize(_dataset);
                _textureManager = TextureManager.CreateFromTexture(_vulkanRenderer.ColorTarget);
                break;

            case GraphicsBackend.OpenGL:
            case GraphicsBackend.OpenGLES:
                _vulkanRenderer = new VulkanMeshRenderer();
                _vulkanRenderer.Initialize(_dataset);
                _textureManager = TextureManager.CreateFromTexture(_vulkanRenderer.ColorTarget);
                Logger.Log("Using Vulkan renderer for OpenGL backend");
                break;

            default:
                throw new NotSupportedException($"Graphics backend {backend} is not supported for Mesh3D rendering");
        }

        _cameraTarget = Vector3.Zero;
        UpdateCameraMatrices();
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

        // View Presets
        ImGui.Text("View:");
        ImGui.SameLine();

        if (ImGui.Button("Top"))
            ApplyViewPreset(CameraPreset.Top);
        ImGui.SameLine();

        if (ImGui.Button("Bottom"))
            ApplyViewPreset(CameraPreset.Bottom);
        ImGui.SameLine();

        if (ImGui.Button("Front"))
            ApplyViewPreset(CameraPreset.Front);
        ImGui.SameLine();

        if (ImGui.Button("Back"))
            ApplyViewPreset(CameraPreset.Back);
        ImGui.SameLine();

        if (ImGui.Button("Left"))
            ApplyViewPreset(CameraPreset.Left);
        ImGui.SameLine();

        if (ImGui.Button("Right"))
            ApplyViewPreset(CameraPreset.Right);

        // Editor controls
        _editor.DrawToolbarControls();

        // Backend info
        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();
        ImGui.TextDisabled($"[{VeldridManager.GraphicsDevice.BackendType}]");
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        // Render the scene
        RenderScene();

        // Display rendered image
        var textureId = _textureManager.GetImGuiTextureId();
        if (textureId != IntPtr.Zero)
        {
            var availableSize = ImGui.GetContentRegionAvail();
            var imagePos = ImGui.GetCursorScreenPos();

            // Invisible button for mouse input
            ImGui.InvisibleButton("Mesh3DViewArea", availableSize);
            var isHovered = ImGui.IsItemHovered();
            var isActive = ImGui.IsItemActive();

            // Draw image
            ImGui.SetCursorScreenPos(imagePos);
            ImGui.Image(textureId, availableSize, new Vector2(0, 1), new Vector2(1, 0));

            // Handle mouse interaction
            if (isHovered || isActive || _isDragging || _isPanning)
            {
                HandleMouseInput();

                // Mark as custom view when user interacts
                if ((_isDragging || _isPanning) && _lastPreset != CameraPreset.Custom)
                    _lastPreset = CameraPreset.Custom;
            }

            // Context menu
            if (ImGui.BeginPopupContextItem("Mesh3DViewerContextMenu"))
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
                    if (ImGui.MenuItem("Bottom View")) ApplyViewPreset(CameraPreset.Bottom);
                    if (ImGui.MenuItem("Front View")) ApplyViewPreset(CameraPreset.Front);
                    if (ImGui.MenuItem("Back View")) ApplyViewPreset(CameraPreset.Back);
                    if (ImGui.MenuItem("Left View")) ApplyViewPreset(CameraPreset.Left);
                    if (ImGui.MenuItem("Right View")) ApplyViewPreset(CameraPreset.Right);
                    ImGui.EndMenu();
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Toggle Grid", null, _showGrid))
                    _showGrid = !_showGrid;

                ImGui.EndPopup();
            }
        }

        // Draw editor panel if in edit mode
        if (_editor.IsEditMode) _editor.DrawEditorPanel();
    }

    public void Dispose()
    {
        _textureManager?.Dispose();
        _d3dRenderer?.Dispose();
        _metalRenderer?.Dispose();
        _vulkanRenderer?.Dispose();
    }

    private void RenderScene()
    {
        if (_d3dRenderer != null)
            _d3dRenderer.Render(_dataset, _viewMatrix, _projMatrix, _cameraTarget, _showGrid);
        else if (_metalRenderer != null)
            _metalRenderer.Render(_dataset, _viewMatrix, _projMatrix, _cameraTarget, _showGrid);
        else if (_vulkanRenderer != null)
            _vulkanRenderer.Render(_dataset, _viewMatrix, _projMatrix, _cameraTarget, _showGrid);
    }

    private void UpdateCameraMatrices()
    {
        // Calculate camera position from spherical coordinates
        Vector3 offset;
        offset.X = MathF.Cos(_cameraYaw) * MathF.Cos(_cameraPitch);
        offset.Y = MathF.Sin(_cameraPitch);
        offset.Z = MathF.Sin(_cameraYaw) * MathF.Cos(_cameraPitch);
        offset *= _cameraDistance;

        var cameraPosition = _cameraTarget + offset;
        _viewMatrix = Matrix4x4.CreateLookAt(cameraPosition, _cameraTarget, Vector3.UnitY);

        // Projection matrix
        var aspect = 1.0f;
        if (_d3dRenderer != null)
            aspect = _d3dRenderer.Width / (float)_d3dRenderer.Height;
        else if (_metalRenderer != null)
            aspect = _metalRenderer.Width / (float)_metalRenderer.Height;
        else if (_vulkanRenderer != null)
            aspect = _vulkanRenderer.Width / (float)_vulkanRenderer.Height;

        _projMatrix = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, 0.1f, 1000f);
    }

    private void HandleMouseInput()
    {
        var io = ImGui.GetIO();

        // Mouse wheel zoom
        if (io.MouseWheel != 0)
        {
            _cameraDistance = Math.Clamp(_cameraDistance * (1.0f - io.MouseWheel * 0.1f), 0.5f, 20.0f);
            UpdateCameraMatrices();
        }

        // Start dragging
        if (!_isDragging && !_isPanning)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _isDragging = true;
                _lastMousePos = io.MousePos;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _isPanning = true;
                _lastMousePos = io.MousePos;
            }
        }

        // Orbit rotation (left mouse)
        if (_isDragging)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var delta = io.MousePos - _lastMousePos;
                _cameraYaw -= delta.X * 0.01f;
                _cameraPitch = Math.Clamp(_cameraPitch - delta.Y * 0.01f, -MathF.PI / 2.01f, MathF.PI / 2.01f);
                _lastMousePos = io.MousePos;
                UpdateCameraMatrices();
            }
            else
            {
                _isDragging = false;
            }
        }

        // Pan (middle/right mouse)
        if (_isPanning)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Middle) || ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                var delta = io.MousePos - _lastMousePos;
                Matrix4x4.Invert(_viewMatrix, out var invView);
                var right = Vector3.Normalize(new Vector3(invView.M11, invView.M12, invView.M13));
                var up = Vector3.Normalize(new Vector3(invView.M21, invView.M22, invView.M23));
                var panSpeed = _cameraDistance * 0.001f;
                _cameraTarget -= right * delta.X * panSpeed;
                _cameraTarget += up * delta.Y * panSpeed;
                _lastMousePos = io.MousePos;
                UpdateCameraMatrices();
            }
            else
            {
                _isPanning = false;
            }
        }
    }

    private void ApplyViewPreset(CameraPreset preset)
    {
        _lastPreset = preset;
        _cameraTarget = Vector3.Zero;

        // Get model bounds for appropriate distance
        var size = _dataset.BoundingBoxMax - _dataset.BoundingBoxMin;
        var maxDim = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        _cameraDistance = maxDim * 2.0f;
        if (_cameraDistance < 2.0f) _cameraDistance = 2.0f;

        switch (preset)
        {
            case CameraPreset.Top:
                _cameraYaw = 0;
                _cameraPitch = MathF.PI / 2f - 0.01f; // Slightly off vertical to avoid gimbal lock
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
        Logger.Log($"Applied {preset} view preset");
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