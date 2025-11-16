// GeoscientistToolkit/Data/Media/AISegmentation/VideoSam2InteractiveTool.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Tools.CtImageStack.AISegmentation;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Media.AISegmentation
{
    /// <summary>
    /// Interactive SAM2 tool for VideoDataset
    /// Point-based video segmentation with frame navigation and export
    /// </summary>
    public class VideoSam2InteractiveTool : IDatasetTools, IDisposable
    {
        // Static reference to currently active tool (for viewer click handling)
        public static VideoSam2InteractiveTool ActiveTool { get; set; }

        private VideoAISegmentationPipeline _pipeline;
        private readonly AISegmentationSettings _settings;

        // UI state
        private bool _isPositivePoint = true;
        private bool _isProcessing;

        // Point prompts for current frame
        private List<(float x, float y)> _points = new();
        private List<float> _labels = new();

        // Current frame and segmentation
        private int _currentFrame = 0;
        private byte[,] _currentMask;
        private bool _hasMask;

        // Frame navigation
        private byte[] _currentFrameData;
        private Task<byte[]> _frameLoadTask;

        // Export dialogs
        private ImGuiExportFileDialog _exportMaskDialog;
        private ImGuiExportFileDialog _exportObjectDialog;
        private bool _showingMaskExport;
        private bool _showingObjectExport;

        // Export options
        private enum ExportMode
        {
            CurrentFrame,
            AllSegmented
        }
        private ExportMode _exportMode = ExportMode.CurrentFrame;

        // Error handling
        private string _initializationError;
        private bool _showErrorModal;
        private string _lastError;

        public VideoSam2InteractiveTool()
        {
            _settings = AISegmentationSettings.Instance;

            try
            {
                bool hasSam2 = _settings.ValidateSam2Models();

                if (!hasSam2)
                {
                    _initializationError = "SAM2 model not found.\n\n" +
                        "Please configure SAM2 models in AI Settings.";
                    _showErrorModal = true;
                }
                else
                {
                    _pipeline = new VideoAISegmentationPipeline();
                }
            }
            catch (Exception ex)
            {
                _initializationError = $"Failed to initialize pipeline:\n\n{ex.Message}";
                _showErrorModal = true;
            }

            // Initialize export dialogs
            _exportMaskDialog = new ImGuiExportFileDialog("exportVideoMask", "Export Segmentation Mask");
            _exportMaskDialog.SetExtensions(
                (".png", "PNG Image"),
                (".jpg", "JPEG Image"),
                (".bmp", "BMP Image")
            );

            _exportObjectDialog = new ImGuiExportFileDialog("exportVideoObject", "Export Segmented Object");
            _exportObjectDialog.SetExtensions(
                (".png", "PNG Image (with transparency)"),
                (".jpg", "JPEG Image"),
                (".bmp", "BMP Image")
            );
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not VideoDataset video) return;

            // Set this as the active tool for viewer click handling
            ActiveTool = this;

            DrawErrorModal();

            ImGui.SeparatorText("AI Segmentation: SAM2 for Video");

            if (_pipeline == null)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1),
                    "Pipeline not available. Check model paths in AI Settings.");
                DrawRequirementsInfo();
                return;
            }

            if (video.IsMissing)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Video file not found");
                return;
            }

            ImGui.TextWrapped("Click on the video frame to add segmentation points.");
            ImGui.Separator();

            // Frame navigation
            DrawFrameNavigation(video);

            ImGui.Separator();

            // Point mode selection
            ImGui.Text("Point Mode:");
            ImGui.SameLine();

            if (ImGui.RadioButton("Positive", _isPositivePoint))
                _isPositivePoint = true;
            ImGui.SameLine();
            if (ImGui.RadioButton("Negative", !_isPositivePoint))
                _isPositivePoint = false;

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Positive: include region, Negative: exclude region");

            ImGui.Spacing();

            // Points info
            ImGui.Text($"Points on current frame: {_points.Count}");
            if (_points.Count > 0)
            {
                int positiveCount = 0;
                int negativeCount = 0;
                for (int i = 0; i < _labels.Count; i++)
                {
                    if (_labels[i] > 0)
                        positiveCount++;
                    else
                        negativeCount++;
                }
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.3f, 1, 0.3f, 1), $"(+{positiveCount}");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"-{negativeCount})");
            }

            ImGui.Spacing();

            // Segmentation status
            var segmentedFrames = _pipeline?.GetSegmentedFrames();
            int segmentedCount = segmentedFrames != null ? System.Linq.Enumerable.Count(segmentedFrames) : 0;
            ImGui.Text($"Segmented frames: {segmentedCount} / {video.TotalFrames}");

            ImGui.Separator();

            // Action buttons
            if (_isProcessing)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1, 1), "Processing...");
            }
            else
            {
                if (_points.Count > 0)
                {
                    if (ImGui.Button("Segment Current Frame", new Vector2(-1, 30)))
                    {
                        ProcessSegmentation(video);
                    }

                    if (ImGui.Button("Clear Points", new Vector2(-1, 0)))
                    {
                        ClearPoints();
                    }
                }
                else
                {
                    ImGui.TextWrapped("Click on the video frame to add points for segmentation");
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.SeparatorText("Export");

                // Export mode selection
                ImGui.Text("Export:");
                ImGui.SameLine();
                if (ImGui.RadioButton("Current Frame Only", _exportMode == ExportMode.CurrentFrame))
                    _exportMode = ExportMode.CurrentFrame;
                ImGui.SameLine();
                if (ImGui.RadioButton("All Segmented Frames", _exportMode == ExportMode.AllSegmented))
                    _exportMode = ExportMode.AllSegmented;

                ImGui.Spacing();

                // Export buttons
                bool canExportCurrent = _hasMask;
                bool canExportAll = segmentedCount > 0;

                if (!canExportCurrent && _exportMode == ExportMode.CurrentFrame)
                {
                    ImGui.BeginDisabled();
                }

                if (ImGui.Button("Export as Mask", new Vector2(-1, 0)))
                {
                    _showingMaskExport = true;
                    _exportMaskDialog.Open($"{System.IO.Path.GetFileNameWithoutExtension(video.FilePath)}_mask",
                        System.IO.Path.GetDirectoryName(video.FilePath));
                }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Export segmentation mask (white=object, black=background)");

                if (!canExportCurrent && _exportMode == ExportMode.CurrentFrame)
                {
                    ImGui.EndDisabled();
                }

                if (!canExportCurrent && _exportMode == ExportMode.CurrentFrame)
                {
                    ImGui.BeginDisabled();
                }

                if (ImGui.Button("Export Extracted Object", new Vector2(-1, 0)))
                {
                    _showingObjectExport = true;
                    _exportObjectDialog.Open($"{System.IO.Path.GetFileNameWithoutExtension(video.FilePath)}_object",
                        System.IO.Path.GetDirectoryName(video.FilePath));
                }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip("Export object with transparent background");

                if (!canExportCurrent && _exportMode == ExportMode.CurrentFrame)
                {
                    ImGui.EndDisabled();
                }

                if (!string.IsNullOrEmpty(_lastError))
                {
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), _lastError);
                }
            }

            // Handle export dialogs
            HandleExportDialogs(video);
        }

        private void DrawFrameNavigation(VideoDataset video)
        {
            ImGui.Text("Frame:");
            ImGui.SameLine();

            int frameNum = _currentFrame;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputInt("##FrameNum", ref frameNum))
            {
                SeekToFrame(video, Math.Clamp(frameNum, 0, video.TotalFrames - 1));
            }

            ImGui.SameLine();
            ImGui.Text($"/ {video.TotalFrames - 1}");

            ImGui.SameLine();
            if (ImGui.Button("◀◀ -10"))
                SeekToFrame(video, Math.Max(0, _currentFrame - 10));

            ImGui.SameLine();
            if (ImGui.Button("◀ -1"))
                SeekToFrame(video, Math.Max(0, _currentFrame - 1));

            ImGui.SameLine();
            if (ImGui.Button("▶ +1"))
                SeekToFrame(video, Math.Min(video.TotalFrames - 1, _currentFrame + 1));

            ImGui.SameLine();
            if (ImGui.Button("▶▶ +10"))
                SeekToFrame(video, Math.Min(video.TotalFrames - 1, _currentFrame + 10));

            // Show if current frame has segmentation
            if (_pipeline?.HasFrameMask(_currentFrame) == true)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.3f, 1, 0.3f, 1), "● Segmented");
            }
        }

        private void SeekToFrame(VideoDataset video, int frameNumber)
        {
            if (frameNumber == _currentFrame) return;

            _currentFrame = frameNumber;

            // Load mask for this frame if available
            if (_pipeline?.HasFrameMask(_currentFrame) == true)
            {
                _currentMask = _pipeline.GetFrameMask(_currentFrame);
                _hasMask = true;
            }
            else
            {
                _currentMask = null;
                _hasMask = false;
            }

            // Clear points when changing frames
            _points.Clear();
            _labels.Clear();

            // Trigger frame load for display
            LoadCurrentFrame(video);
        }

        private void LoadCurrentFrame(VideoDataset video)
        {
            if (_frameLoadTask == null || _frameLoadTask.IsCompleted)
            {
                var timeSeconds = _currentFrame / video.FrameRate;
                _frameLoadTask = video.ExtractFrameAsync(timeSeconds, video.Width, video.Height);
            }
        }

        public void AddPoint(VideoDataset dataset, float x, float y)
        {
            _points.Add((x, y));
            _labels.Add(_isPositivePoint ? 1.0f : 0.0f);
        }

        private async void ProcessSegmentation(VideoDataset dataset)
        {
            if (_points.Count == 0) return;

            _isProcessing = true;
            _lastError = "";

            try
            {
                _currentMask = await _pipeline.SegmentFrameAsync(dataset, _currentFrame, _points, _labels);
                _hasMask = true;

                Logger.Log($"[VideoSAM2] Segmented frame {_currentFrame}");
            }
            catch (Exception ex)
            {
                _lastError = $"Segmentation failed: {ex.Message}";
                Logger.LogError($"[VideoSAM2] {_lastError}");
                _currentMask = null;
                _hasMask = false;
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void ClearPoints()
        {
            _points.Clear();
            _labels.Clear();
        }

        private void HandleExportDialogs(VideoDataset video)
        {
            // Mask export dialog
            if (_showingMaskExport && _exportMaskDialog.Submit())
            {
                _showingMaskExport = false;
                ExportMask(video, _exportMaskDialog.SelectedPath, _exportMaskDialog.SelectedExtension);
            }

            // Object export dialog
            if (_showingObjectExport && _exportObjectDialog.Submit())
            {
                _showingObjectExport = false;
                ExportObject(video, _exportObjectDialog.SelectedPath, _exportObjectDialog.SelectedExtension);
            }
        }

        private async void ExportMask(VideoDataset video, string basePath, string extension)
        {
            _lastError = "";

            try
            {
                if (_exportMode == ExportMode.CurrentFrame)
                {
                    // Export single frame mask
                    var maskData = _pipeline.ExportFrameMask(_currentFrame, video.Width, video.Height);
                    if (maskData == null)
                    {
                        _lastError = "No mask available for current frame";
                        return;
                    }

                    ImageExporter.ExportGrayscaleSlice(maskData, video.Width, video.Height, basePath);
                    Logger.Log($"[VideoSAM2] Exported mask for frame {_currentFrame} to {basePath}");
                }
                else
                {
                    // Export all segmented frames
                    var segmentedFrames = _pipeline.GetSegmentedFrames();
                    int count = 0;

                    foreach (var frameNum in segmentedFrames)
                    {
                        var maskData = _pipeline.ExportFrameMask(frameNum, video.Width, video.Height);
                        if (maskData != null)
                        {
                            var frameFileName = basePath.Replace(extension, $"_frame{frameNum:D5}{extension}");
                            ImageExporter.ExportGrayscaleSlice(maskData, video.Width, video.Height, frameFileName);
                            count++;
                        }
                    }

                    Logger.Log($"[VideoSAM2] Exported {count} mask frames to {System.IO.Path.GetDirectoryName(basePath)}");
                }
            }
            catch (Exception ex)
            {
                _lastError = $"Export failed: {ex.Message}";
                Logger.LogError($"[VideoSAM2] {_lastError}");
            }
        }

        private async void ExportObject(VideoDataset video, string basePath, string extension)
        {
            _lastError = "";

            try
            {
                if (_exportMode == ExportMode.CurrentFrame)
                {
                    // Export single frame with extracted object
                    var timeSeconds = _currentFrame / video.FrameRate;
                    var frameData = await video.ExtractFrameAsync(timeSeconds, video.Width, video.Height);

                    if (frameData == null)
                    {
                        _lastError = "Failed to load frame";
                        return;
                    }

                    var maskedData = _pipeline.ExportMaskedFrame(frameData, _currentFrame, video.Width, video.Height);
                    if (maskedData == null)
                    {
                        _lastError = "No mask available for current frame";
                        return;
                    }

                    ImageExporter.ExportColorSlice(maskedData, video.Width, video.Height, basePath);
                    Logger.Log($"[VideoSAM2] Exported extracted object for frame {_currentFrame} to {basePath}");
                }
                else
                {
                    // Export all segmented frames with extracted objects
                    var segmentedFrames = _pipeline.GetSegmentedFrames();
                    int count = 0;

                    foreach (var frameNum in segmentedFrames)
                    {
                        var timeSeconds = frameNum / video.FrameRate;
                        var frameData = await video.ExtractFrameAsync(timeSeconds, video.Width, video.Height);

                        if (frameData != null)
                        {
                            var maskedData = _pipeline.ExportMaskedFrame(frameData, frameNum, video.Width, video.Height);
                            if (maskedData != null)
                            {
                                var frameFileName = basePath.Replace(extension, $"_frame{frameNum:D5}{extension}");
                                ImageExporter.ExportColorSlice(maskedData, video.Width, video.Height, frameFileName);
                                count++;
                            }
                        }
                    }

                    Logger.Log($"[VideoSAM2] Exported {count} object frames to {System.IO.Path.GetDirectoryName(basePath)}");
                }
            }
            catch (Exception ex)
            {
                _lastError = $"Export failed: {ex.Message}";
                Logger.LogError($"[VideoSAM2] {_lastError}");
            }
        }

        private void DrawErrorModal()
        {
            if (_showErrorModal)
            {
                ImGui.OpenPopup("Initialization Error");
                _showErrorModal = false;
            }

            if (ImGui.BeginPopupModal("Initialization Error", ref _showErrorModal, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextWrapped(_initializationError);
                ImGui.Spacing();
                if (ImGui.Button("OK", new Vector2(120, 0)))
                    ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
        }

        private void DrawRequirementsInfo()
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.3f, 1), "Required ONNX Models:");
            ImGui.BulletText("SAM2: sam2.1_large.encoder.onnx + sam2.1_large.decoder.onnx");
        }

        public byte[,] GetCurrentMask() => _currentMask;

        public bool HasMask => _hasMask;

        public int CurrentFrame => _currentFrame;

        public void Dispose()
        {
            if (ActiveTool == this)
                ActiveTool = null;

            _pipeline?.Dispose();
        }
    }
}
