// GeoscientistToolkit/Data/Media/VideoDatasetViewer.cs

using System.Numerics;
using GeoscientistToolkit.Data.Media.AISegmentation;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Media;

/// <summary>
/// Viewer for video datasets with playback controls and frame display
/// Thread-safe implementation with null checking
/// </summary>
public class VideoDatasetViewer : IDatasetViewer
{
    private readonly VideoDataset _dataset;
    private readonly object _frameLock = new object();

    // Playback state
    private bool _isPlaying = false;
    private double _currentTime = 0.0;
    private int _currentFrame = 0;
    private DateTime _lastFrameTime = DateTime.Now;

    // Frame data
    private byte[] _currentFrameData;
    private TextureManager _frameTexture;
    private Task<byte[]> _frameLoadTask;
    private bool _frameNeedsUpdate = true;

    // UI state
    private float _playbackSpeed = 1.0f;
    private bool _loop = false;
    private bool _showTimeline = true;
    private bool _isLoadingFrame = false;

    // Click handler for AI tools (e.g., SAM2 point selection)
    public Action<float, float> OnFrameClick;

    public VideoDatasetViewer(VideoDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
    }

    public void DrawToolbarControls()
    {
        if (_dataset == null) return;

        // Playback controls
        var playIcon = _isPlaying ? "⏸" : "▶";
        if (ImGui.Button($"{playIcon} {(_isPlaying ? "Pause" : "Play")}"))
        {
            TogglePlayback();
        }

        ImGui.SameLine();
        if (ImGui.Button("⏹ Stop"))
        {
            StopPlayback();
        }

        ImGui.SameLine();
        if (ImGui.Button("⏮ Start"))
        {
            SeekToStart();
        }

        ImGui.SameLine();
        if (ImGui.Button("⏭ End"))
        {
            SeekToEnd();
        }

        ImGui.SameLine();
        ImGui.Checkbox("Loop", ref _loop);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.SliderFloat("Speed", ref _playbackSpeed, 0.25f, 2.0f, "%.2fx"))
        {
            _playbackSpeed = Math.Clamp(_playbackSpeed, 0.25f, 4.0f);
        }

        ImGui.SameLine();
        ImGui.Checkbox("Timeline", ref _showTimeline);

        ImGui.SameLine();
        ImGui.TextDisabled($"Frame: {_currentFrame + 1}/{_dataset.TotalFrames} | {FormatTime(_currentTime)}/{FormatTime(_dataset.DurationSeconds)}");

        if (_isLoadingFrame)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Loading...");
        }
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        if (_dataset == null) return;

        try
        {
            _dataset.Load();

            if (_dataset.IsMissing)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Video file not found: {_dataset.FilePath}");
                return;
            }

            // Update playback
            if (_isPlaying)
            {
                UpdatePlayback();
            }

            // Load current frame if needed
            UpdateCurrentFrame();

            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();

            // Reserve space for timeline if shown
            if (_showTimeline)
            {
                canvasSize.Y -= 40;
            }

            var dl = ImGui.GetWindowDrawList();
            var io = ImGui.GetIO();

            // Draw background
            dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF101010);
            dl.PushClipRect(canvasPos, canvasPos + canvasSize, true);

            // Calculate display size maintaining aspect ratio
            var aspectRatio = _dataset.Width > 0 && _dataset.Height > 0 ? (float)_dataset.Width / _dataset.Height : 16.0f / 9.0f;
            var displaySize = new Vector2(canvasSize.X, canvasSize.X / aspectRatio);
            if (displaySize.Y > canvasSize.Y)
            {
                displaySize = new Vector2(canvasSize.Y * aspectRatio, canvasSize.Y);
            }
            displaySize *= zoom;

            var imagePos = canvasPos + (canvasSize - displaySize) * 0.5f + pan;
            var isMouseOverImage = io.MousePos.X >= imagePos.X && io.MousePos.X < imagePos.X + displaySize.X &&
                                   io.MousePos.Y >= imagePos.Y && io.MousePos.Y < imagePos.Y + displaySize.Y;

            // Handle zoom with mouse wheel
            if (isMouseOverImage && Math.Abs(io.MouseWheel) > 0.0f)
            {
                var zoomDelta = io.MouseWheel * 0.1f;
                var newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
                if (Math.Abs(newZoom - zoom) > 0.001f)
                {
                    var mousePos = io.MousePos - imagePos;
                    pan -= mousePos * (newZoom / zoom - 1.0f);
                    zoom = newZoom;
                }
            }

            // Handle pan with middle mouse button
            if (isMouseOverImage && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            {
                pan += io.MouseDelta;
            }

            // Handle left click for AI tools (e.g., SAM2 point selection)
            if (isMouseOverImage && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                // Convert screen coordinates to image coordinates
                var relativePos = io.MousePos - imagePos;
                var imageX = (relativePos.X / displaySize.X) * _dataset.Width;
                var imageY = (relativePos.Y / displaySize.Y) * _dataset.Height;

                // Clamp to image bounds
                imageX = Math.Clamp(imageX, 0, _dataset.Width - 1);
                imageY = Math.Clamp(imageY, 0, _dataset.Height - 1);

                // Call active SAM2 tool if available
                if (VideoSam2InteractiveTool.ActiveTool != null)
                {
                    VideoSam2InteractiveTool.ActiveTool.AddPoint(_dataset, imageX, imageY);
                }

                // Also invoke callback if registered (for other tools)
                OnFrameClick?.Invoke(imageX, imageY);
            }

            // Draw frame
            lock (_frameLock)
            {
                if (_currentFrameData != null && _frameTexture != null)
                {
                    dl.AddImage(
                        _frameTexture.ImGuiHandle,
                        imagePos,
                        imagePos + displaySize
                    );
                }
                else if (_dataset.ThumbnailData != null)
                {
                    // Show thumbnail while loading
                    var thumbTexture = GetThumbnailTexture();
                    if (thumbTexture != null)
                    {
                        dl.AddImage(thumbTexture.ImGuiHandle, imagePos, imagePos + displaySize);
                        dl.AddText(imagePos + new Vector2(10, 10), 0xFFFFFFFF, "Loading frame...");
                    }
                }
                else
                {
                    // No frame data available
                    var center = imagePos + displaySize * 0.5f;
                    dl.AddText(center - new Vector2(50, 10), 0xFFFFFFFF, "No frame data");
                }
            }

            dl.PopClipRect();

            // Draw timeline
            if (_showTimeline)
            {
                ImGui.SetCursorScreenPos(canvasPos + new Vector2(0, canvasSize.Y + 5));
                DrawTimeline(canvasSize.X);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[VideoViewer] Error in DrawContent: {ex.Message}");
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error: {ex.Message}");
        }
    }

    private void DrawTimeline(float width)
    {
        if (_dataset == null || _dataset.DurationSeconds <= 0) return;

        var cursorPos = ImGui.GetCursorScreenPos();
        var timelineHeight = 30f;
        var dl = ImGui.GetWindowDrawList();

        // Timeline background
        dl.AddRectFilled(cursorPos, cursorPos + new Vector2(width, timelineHeight), 0xFF303030);

        // Timeline progress
        var progress = _dataset.DurationSeconds > 0 ? (float)(_currentTime / _dataset.DurationSeconds) : 0f;
        progress = Math.Clamp(progress, 0f, 1f);

        dl.AddRectFilled(cursorPos, cursorPos + new Vector2(width * progress, timelineHeight), 0xFF4070C0);

        // Playhead
        var playheadX = cursorPos.X + width * progress;
        dl.AddLine(new Vector2(playheadX, cursorPos.Y), new Vector2(playheadX, cursorPos.Y + timelineHeight), 0xFFFFFFFF, 2);

        // Time markers
        var markerCount = 10;
        for (int i = 0; i <= markerCount; i++)
        {
            var markerX = cursorPos.X + (width / markerCount) * i;
            var markerTime = (_dataset.DurationSeconds / markerCount) * i;
            dl.AddLine(new Vector2(markerX, cursorPos.Y + timelineHeight - 5), new Vector2(markerX, cursorPos.Y + timelineHeight), 0xFF808080);

            if (i % 2 == 0) // Show time label for every other marker
            {
                var timeStr = FormatTime(markerTime);
                dl.AddText(new Vector2(markerX - 20, cursorPos.Y + 2), 0xFFCCCCCC, timeStr);
            }
        }

        // Handle timeline scrubbing
        ImGui.SetCursorScreenPos(cursorPos);
        ImGui.InvisibleButton("##timeline", new Vector2(width, timelineHeight));

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var mouseX = ImGui.GetIO().MousePos.X;
            var relativeX = (mouseX - cursorPos.X) / width;
            SeekTo(relativeX * _dataset.DurationSeconds);
        }

        ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, timelineHeight + 5));
    }

    private void UpdatePlayback()
    {
        if (_dataset == null || _dataset.DurationSeconds <= 0) return;

        var now = DateTime.Now;
        var deltaTime = (now - _lastFrameTime).TotalSeconds * _playbackSpeed;
        _lastFrameTime = now;

        _currentTime += deltaTime;

        if (_currentTime >= _dataset.DurationSeconds)
        {
            if (_loop)
            {
                _currentTime = 0.0;
            }
            else
            {
                _currentTime = _dataset.DurationSeconds;
                _isPlaying = false;
            }
        }

        var newFrame = (int)(_currentTime * _dataset.FrameRate);
        newFrame = Math.Clamp(newFrame, 0, _dataset.TotalFrames - 1);

        if (newFrame != _currentFrame)
        {
            _currentFrame = newFrame;
            _frameNeedsUpdate = true;
        }
    }

    private void UpdateCurrentFrame()
    {
        if (!_frameNeedsUpdate || _isLoadingFrame) return;
        if (_dataset == null || _dataset.IsMissing) return;

        // Check if previous load task completed
        if (_frameLoadTask != null && _frameLoadTask.IsCompleted && !_frameLoadTask.IsFaulted)
        {
            lock (_frameLock)
            {
                _currentFrameData = _frameLoadTask.Result;
                UpdateFrameTexture();
                _isLoadingFrame = false;
                _frameLoadTask = null;
            }
            _frameNeedsUpdate = false;
        }
        else if (_frameLoadTask == null)
        {
            // Start new frame load
            _isLoadingFrame = true;
            _frameLoadTask = Task.Run(async () =>
            {
                try
                {
                    return await _dataset.ExtractFrameAsync(_currentTime);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[VideoViewer] Failed to load frame: {ex.Message}");
                    return null;
                }
            });
        }
    }

    private void UpdateFrameTexture()
    {
        if (_currentFrameData == null) return;

        try
        {
            _frameTexture?.Dispose();
            _frameTexture = new TextureManager(_currentFrameData, _dataset.Width, _dataset.Height);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[VideoViewer] Failed to create frame texture: {ex.Message}");
        }
    }

    private TextureManager GetThumbnailTexture()
    {
        if (_dataset?.ThumbnailData == null) return null;

        try
        {
            return new TextureManager(_dataset.ThumbnailData, _dataset.ThumbnailWidth, _dataset.ThumbnailHeight);
        }
        catch
        {
            return null;
        }
    }

    private void TogglePlayback()
    {
        _isPlaying = !_isPlaying;
        if (_isPlaying)
        {
            _lastFrameTime = DateTime.Now;

            // If at end, restart
            if (_currentTime >= _dataset.DurationSeconds)
            {
                _currentTime = 0.0;
                _currentFrame = 0;
                _frameNeedsUpdate = true;
            }
        }
    }

    private void StopPlayback()
    {
        _isPlaying = false;
        SeekToStart();
    }

    private void SeekToStart()
    {
        SeekTo(0.0);
    }

    private void SeekToEnd()
    {
        if (_dataset != null)
        {
            SeekTo(_dataset.DurationSeconds);
        }
    }

    private void SeekTo(double timeSeconds)
    {
        if (_dataset == null) return;

        _currentTime = Math.Clamp(timeSeconds, 0.0, _dataset.DurationSeconds);
        _currentFrame = (int)(_currentTime * _dataset.FrameRate);
        _currentFrame = Math.Clamp(_currentFrame, 0, _dataset.TotalFrames - 1);
        _frameNeedsUpdate = true;
        _lastFrameTime = DateTime.Now;
    }

    private string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}";
    }

    public void Dispose()
    {
        _isPlaying = false;

        lock (_frameLock)
        {
            _frameTexture?.Dispose();
            _frameTexture = null;
            _currentFrameData = null;
        }

        _frameLoadTask = null;
    }
}
