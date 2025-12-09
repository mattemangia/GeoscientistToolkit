// GeoscientistToolkit/Data/Nerf/NerfViewer.cs

using System.Numerics;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// Viewer for NeRF datasets with interactive novel view rendering.
/// </summary>
public class NerfViewer : IDatasetViewer
{
    private readonly NerfDataset _dataset;
    private NerfTrainer _trainer;

    // View state
    private float _cameraDistance = 3.0f;
    private float _cameraYaw = 0f;
    private float _cameraPitch = 0.3f;
    private Vector3 _cameraTarget = Vector3.Zero;
    private bool _autoRotate = false;
    private float _autoRotateSpeed = 0.5f;

    // Render state
    private byte[] _renderedImage;
    private IntPtr _renderedTexture = IntPtr.Zero;
    private int _renderWidth = 512;
    private int _renderHeight = 512;
    private bool _needsRender = true;
    private DateTime _lastRenderTime = DateTime.MinValue;
    private float _renderQuality = 1.0f;

    // Preview during training
    private byte[] _previewImage;
    private IntPtr _previewTexture = IntPtr.Zero;
    private int _previewWidth = 256;
    private int _previewHeight = 256;

    // View modes
    private ViewMode _currentViewMode = ViewMode.NovelView;
    private int _selectedFrameIndex = 0;

    // Camera orbit interaction
    private bool _isDragging = false;
    private Vector2 _lastMousePos;

    public NerfViewer(NerfDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));

        // Initialize trainer with event handlers
        _trainer = new NerfTrainer(_dataset);
        _trainer.OnPreviewReady += OnPreviewReceived;
        _trainer.OnIterationComplete += OnIterationComplete;
        _trainer.OnStateChanged += OnTrainingStateChanged;

        // Set camera target to scene center if available
        if (_dataset.ImageCollection != null)
        {
            _cameraTarget = _dataset.ImageCollection.SceneCenter;
        }
    }

    public void Dispose()
    {
        _trainer?.Dispose();
        _renderedImage = null;
        _previewImage = null;
        GC.SuppressFinalize(this);
    }

    public void DrawToolbarControls()
    {
        // View mode selector
        ImGui.Text("View:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Novel View", _currentViewMode == ViewMode.NovelView))
        {
            _currentViewMode = ViewMode.NovelView;
            _needsRender = true;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Training Frame", _currentViewMode == ViewMode.TrainingFrame))
        {
            _currentViewMode = ViewMode.TrainingFrame;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Camera Poses", _currentViewMode == ViewMode.CameraPoses))
        {
            _currentViewMode = ViewMode.CameraPoses;
        }

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        // Auto-rotate toggle
        if (ImGui.Checkbox("Auto-rotate", ref _autoRotate))
        {
            if (_autoRotate) _needsRender = true;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.SliderFloat("##RotateSpeed", ref _autoRotateSpeed, 0.1f, 2.0f, "%.1f"))
        {
        }

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        // Render quality
        ImGui.Text("Quality:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.SliderFloat("##Quality", ref _renderQuality, 0.25f, 2.0f, "%.2fx"))
        {
            _needsRender = true;
        }

        // Training state indicator
        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        var stateColor = _dataset.TrainingState switch
        {
            NerfTrainingState.Training => new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
            NerfTrainingState.Completed => new Vector4(0.2f, 0.6f, 1.0f, 1.0f),
            NerfTrainingState.Paused => new Vector4(1.0f, 0.8f, 0.2f, 1.0f),
            NerfTrainingState.Failed => new Vector4(1.0f, 0.2f, 0.2f, 1.0f),
            _ => new Vector4(0.5f, 0.5f, 0.5f, 1.0f)
        };

        ImGui.TextColored(stateColor, $"[{_dataset.TrainingState}]");

        if (_dataset.TrainingState == NerfTrainingState.Training)
        {
            ImGui.SameLine();
            ImGui.Text($"Iter: {_dataset.CurrentIteration}/{_dataset.TotalIterations}");
            ImGui.SameLine();
            ImGui.Text($"PSNR: {_dataset.CurrentPSNR:F2}dB");
        }
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        var contentRegion = ImGui.GetContentRegionAvail();

        // Handle auto-rotation
        if (_autoRotate && _dataset.TrainingState != NerfTrainingState.Training)
        {
            _cameraYaw += _autoRotateSpeed * 0.01f;
            _needsRender = true;
        }

        switch (_currentViewMode)
        {
            case ViewMode.NovelView:
                DrawNovelView(contentRegion);
                break;
            case ViewMode.TrainingFrame:
                DrawTrainingFrameView(contentRegion);
                break;
            case ViewMode.CameraPoses:
                DrawCameraPosesView(contentRegion);
                break;
        }

        // Handle mouse interaction for camera orbit
        HandleMouseInput(contentRegion);
    }

    private void DrawNovelView(Vector2 contentRegion)
    {
        // Calculate render dimensions based on quality
        int targetWidth = (int)(contentRegion.X * _renderQuality);
        int targetHeight = (int)(contentRegion.Y * _renderQuality);
        targetWidth = Math.Max(64, Math.Min(targetWidth, 1024));
        targetHeight = Math.Max(64, Math.Min(targetHeight, 1024));

        // Check if we need to re-render
        bool needsNewRender = _needsRender ||
                             targetWidth != _renderWidth ||
                             targetHeight != _renderHeight ||
                             (DateTime.Now - _lastRenderTime).TotalMilliseconds > 100;

        if (_dataset.ModelData == null)
        {
            // No trained model - show message or preview
            ImGui.SetCursorPos((contentRegion - new Vector2(300, 100)) * 0.5f);
            ImGui.BeginChild("NoModel", new Vector2(300, 100), ImGuiChildFlags.Borders);
            ImGui.Text("No trained model available.");
            ImGui.Spacing();

            if (_dataset.TrainingState == NerfTrainingState.Training)
            {
                ImGui.Text("Training in progress...");
                float progress = (float)_dataset.CurrentIteration / _dataset.TotalIterations;
                ImGui.ProgressBar(progress, new Vector2(-1, 20));

                // Show training preview if available
                if (_previewImage != null && _previewTexture != IntPtr.Zero)
                {
                    ImGui.Separator();
                    ImGui.Text("Preview:");
                    ImGui.Image(_previewTexture, new Vector2(_previewWidth, _previewHeight));
                }
            }
            else if (_dataset.ImageCollection?.FrameCount > 0)
            {
                ImGui.Text("Use the Tools panel to start training.");
            }
            else
            {
                ImGui.Text("Import images first to train a NeRF.");
            }

            ImGui.EndChild();
            return;
        }

        // Render novel view
        if (needsNewRender && _dataset.TrainingState != NerfTrainingState.Training)
        {
            _renderWidth = targetWidth;
            _renderHeight = targetHeight;
            RenderNovelView();
            _lastRenderTime = DateTime.Now;
            _needsRender = false;
        }

        // Display rendered image
        if (_renderedImage != null)
        {
            // Update or create texture
            if (_renderedTexture == IntPtr.Zero)
            {
                _renderedTexture = CreateTextureFromData(_renderedImage, _renderWidth, _renderHeight, 3);
            }

            if (_renderedTexture != IntPtr.Zero)
            {
                // Center the image
                var imageSize = new Vector2(_renderWidth, _renderHeight) / _renderQuality;
                var padding = (contentRegion - imageSize) * 0.5f;
                ImGui.SetCursorPos(padding);
                ImGui.Image(_renderedTexture, imageSize);
            }
        }

        // Show camera info overlay
        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(cursorPos + new Vector2(10, -contentRegion.Y + 10));
        ImGui.BeginChild("CameraInfo", new Vector2(200, 80), ImGuiChildFlags.None);
        ImGui.TextColored(new Vector4(1, 1, 1, 0.7f), $"Distance: {_cameraDistance:F2}");
        ImGui.TextColored(new Vector4(1, 1, 1, 0.7f), $"Yaw: {MathF.Round(_cameraYaw * 180 / MathF.PI)}°");
        ImGui.TextColored(new Vector4(1, 1, 1, 0.7f), $"Pitch: {MathF.Round(_cameraPitch * 180 / MathF.PI)}°");
        ImGui.EndChild();
    }

    private void RenderNovelView()
    {
        try
        {
            var cameraMatrix = GetCurrentCameraMatrix();
            float focalLength = _renderWidth / (2f * MathF.Tan(MathF.PI / 6f)); // 60 degree FOV

            _renderedImage = _trainer.RenderView(cameraMatrix, focalLength, _renderWidth, _renderHeight);

            // Update texture
            if (_renderedImage != null && _renderedTexture != IntPtr.Zero)
            {
                UpdateTextureData(_renderedTexture, _renderedImage, _renderWidth, _renderHeight, 3);
            }
            else if (_renderedImage != null)
            {
                _renderedTexture = CreateTextureFromData(_renderedImage, _renderWidth, _renderHeight, 3);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to render novel view: {ex.Message}");
        }
    }

    private void DrawTrainingFrameView(Vector2 contentRegion)
    {
        var frames = _dataset.ImageCollection?.Frames;
        if (frames == null || frames.Count == 0)
        {
            ImGui.Text("No training frames available.");
            return;
        }

        // Frame selector
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Frame", ref _selectedFrameIndex, 0, frames.Count - 1))
        {
        }

        ImGui.SameLine();
        if (ImGui.Button("<"))
        {
            _selectedFrameIndex = Math.Max(0, _selectedFrameIndex - 1);
        }
        ImGui.SameLine();
        if (ImGui.Button(">"))
        {
            _selectedFrameIndex = Math.Min(frames.Count - 1, _selectedFrameIndex + 1);
        }

        var frame = frames[_selectedFrameIndex];

        // Show frame info
        ImGui.Text($"Size: {frame.Width}x{frame.Height}");
        ImGui.SameLine();
        ImGui.Text($"Pose: {frame.PoseStatus}");
        ImGui.SameLine();

        var poseStatusColor = frame.PoseStatus switch
        {
            PoseEstimationStatus.Estimated or PoseEstimationStatus.ManuallySet
                => new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
            PoseEstimationStatus.NotEstimated => new Vector4(1.0f, 0.5f, 0.2f, 1.0f),
            _ => new Vector4(0.5f, 0.5f, 0.5f, 1.0f)
        };
        ImGui.TextColored(poseStatusColor, $"[{frame.PoseStatus}]");

        ImGui.Separator();

        // Display thumbnail or full image
        if (frame.ThumbnailData != null)
        {
            // Create texture from thumbnail
            var texId = CreateTextureFromData(frame.ThumbnailData, frame.ThumbnailWidth, frame.ThumbnailHeight, frame.Channels);
            if (texId != IntPtr.Zero)
            {
                // Scale to fit content area while maintaining aspect ratio
                float aspect = (float)frame.ThumbnailWidth / frame.ThumbnailHeight;
                var maxSize = contentRegion - new Vector2(0, 50);
                var displaySize = new Vector2(maxSize.Y * aspect, maxSize.Y);
                if (displaySize.X > maxSize.X)
                {
                    displaySize = new Vector2(maxSize.X, maxSize.X / aspect);
                }

                var padding = (contentRegion - displaySize - new Vector2(0, 50)) * 0.5f;
                ImGui.SetCursorPosX(padding.X);
                ImGui.Image(texId, displaySize);
            }
        }
    }

    private void DrawCameraPosesView(Vector2 contentRegion)
    {
        var dl = ImGui.GetWindowDrawList();
        var windowPos = ImGui.GetCursorScreenPos();
        var center = windowPos + contentRegion * 0.5f;
        float scale = Math.Min(contentRegion.X, contentRegion.Y) * 0.4f;

        // Draw scene bounds
        dl.AddCircle(center, scale, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1.0f)), 64);

        // Draw camera positions
        var frames = _dataset.ImageCollection?.GetFramesWithPoses().ToList();
        if (frames != null && frames.Count > 0)
        {
            var bounds = _dataset.ImageCollection;
            float maxDist = bounds.SceneRadius;

            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var pos3D = frame.CameraPosition - bounds.SceneCenter;

                // Project to 2D (top-down view by default)
                var pos2D = center + new Vector2(
                    pos3D.X / maxDist * scale,
                    -pos3D.Z / maxDist * scale
                );

                // Draw camera as small triangle pointing in look direction
                var lookDir = frame.LookDirection;
                var look2D = new Vector2(lookDir.X, -lookDir.Z);
                look2D = look2D.Length() > 0 ? Vector2.Normalize(look2D) : Vector2.UnitY;

                var color = i == _selectedFrameIndex
                    ? ImGui.GetColorU32(new Vector4(1.0f, 0.8f, 0.2f, 1.0f))
                    : ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 0.4f, 1.0f));

                // Draw camera frustum indicator
                float camSize = 8;
                var tip = pos2D + look2D * camSize * 2;
                var left = pos2D + new Vector2(-look2D.Y, look2D.X) * camSize;
                var right = pos2D + new Vector2(look2D.Y, -look2D.X) * camSize;

                dl.AddTriangleFilled(tip, left, right, color);
                dl.AddCircleFilled(pos2D, 4, color);
            }

            // Draw current camera position (novel view)
            if (_currentViewMode == ViewMode.CameraPoses)
            {
                var camMatrix = GetCurrentCameraMatrix();
                var camPos3D = new Vector3(camMatrix.M41, camMatrix.M42, camMatrix.M43) - bounds.SceneCenter;
                var camPos2D = center + new Vector2(
                    camPos3D.X / maxDist * scale,
                    -camPos3D.Z / maxDist * scale
                );

                dl.AddCircleFilled(camPos2D, 8, ImGui.GetColorU32(new Vector4(1.0f, 0.3f, 0.3f, 1.0f)));
                dl.AddCircle(camPos2D, 12, ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 1.0f, 0.8f)), 16, 2);
            }
        }

        // Draw sparse points if available
        var sparsePoints = _dataset.ImageCollection?.SparsePointCloud;
        if (sparsePoints != null && sparsePoints.Count > 0)
        {
            var bounds = _dataset.ImageCollection;
            float maxDist = bounds.SceneRadius;

            foreach (var point in sparsePoints.Take(500)) // Limit to avoid slowdown
            {
                var pos3D = point.Position - bounds.SceneCenter;
                var pos2D = center + new Vector2(
                    pos3D.X / maxDist * scale,
                    -pos3D.Z / maxDist * scale
                );

                var color = ImGui.GetColorU32(new Vector4(point.Color.X, point.Color.Y, point.Color.Z, 0.5f));
                dl.AddCircleFilled(pos2D, 2, color);
            }
        }

        // Legend
        ImGui.SetCursorScreenPos(windowPos + new Vector2(10, 10));
        ImGui.BeginChild("Legend", new Vector2(150, 100), ImGuiChildFlags.None);
        ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.4f, 1.0f), "Camera positions");
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), "Selected frame");
        ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), "Current view");
        if (sparsePoints?.Count > 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), $"Points: {sparsePoints.Count}");
        }
        ImGui.EndChild();
    }

    private void HandleMouseInput(Vector2 contentRegion)
    {
        var io = ImGui.GetIO();

        if (ImGui.IsWindowHovered())
        {
            // Zoom with scroll wheel
            if (io.MouseWheel != 0)
            {
                _cameraDistance *= 1.0f - io.MouseWheel * 0.1f;
                _cameraDistance = Math.Clamp(_cameraDistance, 0.1f, 100f);
                _needsRender = true;
            }

            // Orbit with left mouse button
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var delta = io.MouseDelta;
                _cameraYaw += delta.X * 0.01f;
                _cameraPitch -= delta.Y * 0.01f;
                _cameraPitch = Math.Clamp(_cameraPitch, -MathF.PI * 0.45f, MathF.PI * 0.45f);
                _needsRender = true;
            }

            // Pan with right mouse button
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Right))
            {
                var delta = io.MouseDelta;
                var right = new Vector3(MathF.Cos(_cameraYaw), 0, MathF.Sin(_cameraYaw));
                var up = Vector3.UnitY;

                _cameraTarget -= right * delta.X * 0.01f * _cameraDistance;
                _cameraTarget += up * delta.Y * 0.01f * _cameraDistance;
                _needsRender = true;
            }
        }
    }

    private Matrix4x4 GetCurrentCameraMatrix()
    {
        // Calculate camera position on sphere around target
        var offset = new Vector3(
            MathF.Cos(_cameraPitch) * MathF.Sin(_cameraYaw),
            MathF.Sin(_cameraPitch),
            MathF.Cos(_cameraPitch) * MathF.Cos(_cameraYaw)
        ) * _cameraDistance;

        var position = _cameraTarget + offset;
        var forward = Vector3.Normalize(_cameraTarget - position);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var up = Vector3.Cross(forward, right);

        return new Matrix4x4(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            -forward.X, -forward.Y, -forward.Z, 0,
            position.X, position.Y, position.Z, 1
        );
    }

    private void OnPreviewReceived(byte[] data, int width, int height)
    {
        _previewImage = data;
        _previewWidth = width;
        _previewHeight = height;

        if (_previewTexture == IntPtr.Zero)
        {
            _previewTexture = CreateTextureFromData(data, width, height, 3);
        }
        else
        {
            UpdateTextureData(_previewTexture, data, width, height, 3);
        }
    }

    private void OnIterationComplete(int iteration, float loss, float psnr)
    {
        // Could update UI elements here if needed
    }

    private void OnTrainingStateChanged(NerfTrainingState state)
    {
        if (state == NerfTrainingState.Completed)
        {
            _needsRender = true;
        }
    }

    // Texture management helpers (simplified - actual implementation depends on graphics backend)
    private static IntPtr CreateTextureFromData(byte[] data, int width, int height, int channels)
    {
        try
        {
            // Register with TextureManager
            var textureName = $"nerf_view_{Guid.NewGuid()}";
            var texture = TextureManager.Instance.CreateOrUpdateTexture(textureName, data, width, height, channels == 4);
            return texture != IntPtr.Zero ? texture : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static void UpdateTextureData(IntPtr textureId, byte[] data, int width, int height, int channels)
    {
        // Texture update logic - depends on backend
        // For now, we'll recreate the texture each time
    }

    // Access to trainer for external control
    public NerfTrainer Trainer => _trainer;
}

/// <summary>
/// View modes for the NeRF viewer
/// </summary>
public enum ViewMode
{
    NovelView,
    TrainingFrame,
    CameraPoses
}
