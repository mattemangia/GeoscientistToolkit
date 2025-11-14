using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using SkiaSharp;

namespace GeoscientistToolkit.Tools.CtImageStack.AISegmentation
{
    /// <summary>
    /// Interactive SAM2 segmentation tool for CT image stacks
    /// Allows users to click points on the viewer to generate segmentation masks
    /// </summary>
    public class Sam2InteractiveTool : IDatasetTools, IDisposable
    {
        private Sam2Segmenter _segmenter;
        private readonly AISegmentationSettings _settings;

        // Interaction state
        private List<SegmentationPoint> _points = new();
        private byte[,] _currentMask;
        private bool _isActive;
        private PointMode _pointMode = PointMode.Positive;
        private int _currentSliceIndex = -1;

        // Material assignment
        private byte _targetMaterialId = 1;
        private bool _autoApply = false;

        // Performance tracking
        private float _lastInferenceTime;

        // Error handling
        private string _initializationError;
        private bool _showErrorModal;

        private enum PointMode
        {
            Positive,
            Negative
        }

        public Sam2InteractiveTool()
        {
            _settings = AISegmentationSettings.Instance;

            try
            {
                if (_settings.ValidateSam2Models())
                {
                    _segmenter = new Sam2Segmenter();
                }
                else
                {
                    _initializationError = "SAM2 model files not found.\n\n" +
                        "Please configure model paths in AI Settings:\n" +
                        $"- Encoder: {_settings.Sam2EncoderPath}\n" +
                        $"- Decoder: {_settings.Sam2DecoderPath}";
                    _showErrorModal = true;
                }
            }
            catch (Exception ex)
            {
                _initializationError = $"Failed to initialize SAM2:\n\n{ex.Message}\n\n" +
                    "Please check:\n" +
                    "- Model files are valid ONNX format\n" +
                    "- ONNX Runtime is properly installed\n" +
                    "- GPU drivers (if using GPU acceleration)";
                _showErrorModal = true;
            }
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ct) return;

            // Draw error modal if needed
            DrawErrorModal();

            ImGui.SeparatorText("SAM2 Interactive Segmentation");

            if (_segmenter == null)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1),
                    "SAM2 not available. Check model paths in AI Settings.");

                if (ImGui.Button("Open AI Settings"))
                {
                    // User can switch to settings tool manually
                    ImGui.SetTooltip("Switch to 'AI Settings' tool to configure model paths");
                }

                return;
            }

            // Activation toggle
            var wasActive = _isActive;
            if (ImGui.Checkbox("Activate Interactive Mode", ref _isActive))
            {
                if (_isActive && !wasActive)
                {
                    // Just activated
                    _points.Clear();
                    _currentMask = null;
                    _segmenter.ClearCache();
                }
                else if (!_isActive && wasActive)
                {
                    // Deactivated
                    _points.Clear();
                    _currentMask = null;
                }
            }

            if (!_isActive) return;

            ImGui.Separator();

            // Point mode selection
            ImGui.Text("Point Mode:");
            ImGui.SameLine();
            if (ImGui.RadioButton("Positive (Include)", _pointMode == PointMode.Positive))
                _pointMode = PointMode.Positive;
            ImGui.SameLine();
            if (ImGui.RadioButton("Negative (Exclude)", _pointMode == PointMode.Negative))
                _pointMode = PointMode.Negative;

            ImGui.TextWrapped("Click on the viewer to add points. Positive points mark areas to include, " +
                            "negative points mark areas to exclude from the segmentation.");

            ImGui.Separator();

            // Material selection
            ImGui.Text("Target Material:");
            DrawMaterialSelector(ct);

            ImGui.Checkbox("Auto-apply mask to material", ref _autoApply);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Automatically apply mask to selected material after each segmentation");

            ImGui.Separator();

            // Points list
            ImGui.Text($"Points: {_points.Count}");
            if (_points.Count > 0)
            {
                ImGui.BeginChild("PointsList", new Vector2(0, 100), ImGuiChildFlags.Border);
                for (int i = 0; i < _points.Count; i++)
                {
                    var point = _points[i];
                    var color = point.IsPositive ? new Vector4(0.2f, 1.0f, 0.2f, 1) : new Vector4(1.0f, 0.3f, 0.3f, 1);
                    ImGui.PushStyleColor(ImGuiCol.Text, color);

                    ImGui.Text($"{(point.IsPositive ? "+" : "-")} ({point.X:F0}, {point.Y:F0})");

                    ImGui.PopStyleColor();

                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Remove##{i}"))
                    {
                        _points.RemoveAt(i);
                        if (_points.Count > 0)
                            RunSegmentation(ct);
                        else
                            _currentMask = null;
                        break;
                    }
                }
                ImGui.EndChild();
            }

            // Actions
            ImGui.Separator();

            if (ImGui.Button("Clear Points", new Vector2(120, 0)))
            {
                _points.Clear();
                _currentMask = null;
            }

            ImGui.SameLine();

            if (ImGui.Button("Run Segmentation", new Vector2(150, 0)) && _points.Count > 0)
            {
                RunSegmentation(ct);
            }

            ImGui.SameLine();

            if (ImGui.Button("Apply to Material", new Vector2(150, 0)) && _currentMask != null)
            {
                ApplyMaskToMaterial(ct);
            }

            // Performance info
            if (_lastInferenceTime > 0)
            {
                ImGui.Separator();
                ImGui.Text($"Last inference: {_lastInferenceTime:F2}ms");
            }

            // Mask preview info
            if (_currentMask != null)
            {
                int maskPixels = 0;
                for (int y = 0; y < _currentMask.GetLength(0); y++)
                    for (int x = 0; x < _currentMask.GetLength(1); x++)
                        if (_currentMask[y, x] > 0) maskPixels++;

                ImGui.Text($"Mask pixels: {maskPixels:N0}");
            }
        }

        /// <summary>
        /// Called by the viewer when user clicks on the image
        /// </summary>
        public void OnViewerClick(CtImageStackDataset dataset, float imageX, float imageY, int sliceIndex)
        {
            if (!_isActive) return;

            _currentSliceIndex = sliceIndex;

            // Add point
            _points.Add(new SegmentationPoint
            {
                X = imageX,
                Y = imageY,
                IsPositive = _pointMode == PointMode.Positive
            });

            // Run segmentation
            RunSegmentation(dataset);

            // Auto-apply if enabled
            if (_autoApply && _currentMask != null)
            {
                ApplyMaskToMaterial(dataset);
            }
        }

        private void RunSegmentation(CtImageStackDataset dataset)
        {
            if (_points.Count == 0 || _currentSliceIndex < 0 || _currentSliceIndex >= dataset.Depth)
                return;

            try
            {
                var startTime = DateTime.Now;

                // Get current slice as bitmap
                var slice = new byte[dataset.Width * dataset.Height];
                dataset.VolumeData.ReadSliceZ(_currentSliceIndex, slice);
                using var bitmap = ConvertSliceToBitmap(slice, dataset.Width, dataset.Height);

                // Prepare points and labels
                var points = _points.Select(p => (p.X, p.Y)).ToList();
                var labels = _points.Select(p => p.IsPositive ? 1.0f : 0.0f).ToList();

                // Run SAM2
                _currentMask = _segmenter.Segment(bitmap, points, labels);

                _lastInferenceTime = (float)(DateTime.Now - startTime).TotalMilliseconds;

                Console.WriteLine($"SAM2 segmentation completed in {_lastInferenceTime:F2}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SAM2 segmentation failed: {ex.Message}");
                _currentMask = null;
            }
        }

        private void ApplyMaskToMaterial(CtImageStackDataset dataset)
        {
            if (_currentMask == null || _currentSliceIndex < 0) return;

            // Apply mask to dataset labels
            int width = dataset.Width;
            int height = dataset.Height;

            for (int y = 0; y < height && y < _currentMask.GetLength(0); y++)
            {
                for (int x = 0; x < width && x < _currentMask.GetLength(1); x++)
                {
                    if (_currentMask[y, x] > 0)
                    {
                        dataset.LabelData[x, y, _currentSliceIndex] = _targetMaterialId;
                    }
                }
            }

            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
            Console.WriteLine($"Applied SAM2 mask to material {_targetMaterialId}");
        }

        private void DrawMaterialSelector(CtImageStackDataset dataset)
        {
            var materials = dataset.Materials.Where(m => m.ID != 0).ToList();

            if (materials.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No materials defined. Create materials first.");
                return;
            }

            ImGui.PushItemWidth(200);
            if (ImGui.BeginCombo("##MaterialSelect", GetMaterialName(dataset, _targetMaterialId)))
            {
                foreach (var material in materials)
                {
                    bool isSelected = _targetMaterialId == material.ID;
                    var colorU32 = ImGui.ColorConvertFloat4ToU32(material.Color);

                    ImGui.PushStyleColor(ImGuiCol.Text, colorU32);
                    if (ImGui.Selectable($"{material.Name} (ID: {material.ID})", isSelected))
                    {
                        _targetMaterialId = material.ID;
                    }
                    ImGui.PopStyleColor();

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
        }

        private string GetMaterialName(CtImageStackDataset dataset, byte materialId)
        {
            var material = dataset.Materials.FirstOrDefault(m => m.ID == materialId);
            return material != null ? $"{material.Name} (ID: {materialId})" : "Select Material";
        }

        private SKBitmap ConvertSliceToBitmap(byte[] slice, int width, int height)
        {
            var bitmap = new SKBitmap(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    byte gray = slice[idx];
                    bitmap.SetPixel(x, y, new SKColor(gray, gray, gray));
                }
            }

            return bitmap;
        }

        /// <summary>
        /// Get current mask for rendering as overlay in viewer
        /// </summary>
        public byte[,] GetCurrentMask() => _currentMask;

        /// <summary>
        /// Get current points for rendering as overlay in viewer
        /// </summary>
        public List<SegmentationPoint> GetCurrentPoints() => _points;

        public bool IsActive => _isActive;

        private void DrawErrorModal()
        {
            if (_showErrorModal)
            {
                ImGui.OpenPopup("SAM2 Initialization Error");
                _showErrorModal = false; // Open once
            }

            // Set red title bar color
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.7f, 0.1f, 0.1f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.9f, 0.1f, 0.1f, 1.0f));

            if (ImGui.BeginPopupModal("SAM2 Initialization Error", ref _showErrorModal,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            {
                ImGui.PopStyleColor(2);

                ImGui.TextWrapped(_initializationError);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("OK", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
            else
            {
                ImGui.PopStyleColor(2);
            }
        }

        public void Dispose()
        {
            _segmenter?.Dispose();
        }

        public class SegmentationPoint
        {
            public float X { get; set; }
            public float Y { get; set; }
            public bool IsPositive { get; set; }
        }
    }
}
