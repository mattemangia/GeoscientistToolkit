using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using SkiaSharp;

namespace GeoscientistToolkit.Tools.CtImageStack.AISegmentation
{
    /// <summary>
    /// Combined Grounding DINO + SAM pipeline tool
    /// Detects objects with text prompts, then segments them automatically
    /// </summary>
    public class GroundingSamTool : IDatasetTools, IDisposable
    {
        private GroundingSamPipeline _pipeline;
        private readonly AISegmentationSettings _settings;

        // UI state
        private string _textPrompt = "rock . mineral . crystal .";
        private GroundingSamPipeline.SegmenterType _segmenterType = GroundingSamPipeline.SegmenterType.SAM2;
        private PointPlacementStrategy _strategy;
        private bool _isProcessing;

        // Results
        private List<SegmentationResult> _results;
        private int _currentSliceIndex = -1;
        private float _lastProcessingTime;

        // Material assignment
        private byte _targetMaterialId = 1;
        private bool _showResults;

        // Error handling
        private string _initializationError;
        private bool _showErrorModal;

        public GroundingSamTool()
        {
            _settings = AISegmentationSettings.Instance;
            _strategy = _settings.DefaultPointStrategy;

            try
            {
                bool hasDino = _settings.ValidateGroundingDinoModel();
                bool hasSam2 = _settings.ValidateSam2Models();
                bool hasMicroSam = _settings.ValidateMicroSamModels();

                if (!hasDino)
                {
                    _initializationError = "Grounding DINO model files not found.\n\n" +
                        "Please configure model paths in AI Settings:\n" +
                        $"- Model: {_settings.GroundingDinoModelPath}\n" +
                        $"- Vocabulary: {_settings.GroundingDinoVocabPath}";
                    _showErrorModal = true;
                }
                else if (!hasSam2 && !hasMicroSam)
                {
                    _initializationError = "No segmentation model found.\n\n" +
                        "Please configure either SAM2 or MicroSAM models in AI Settings.";
                    _showErrorModal = true;
                }
                else
                {
                    // Initialize with SAM2 if available, else MicroSAM
                    var segmenterType = hasSam2
                        ? GroundingSamPipeline.SegmenterType.SAM2
                        : GroundingSamPipeline.SegmenterType.MicroSAM;

                    _pipeline = new GroundingSamPipeline(segmenterType);
                    _segmenterType = segmenterType;
                }
            }
            catch (Exception ex)
            {
                _initializationError = $"Failed to initialize pipeline:\n\n{ex.Message}\n\n" +
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

            ImGui.SeparatorText("Grounding DINO + SAM Pipeline");

            if (_pipeline == null)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1),
                    "Pipeline not available. Check model paths in AI Settings.");

                if (ImGui.Button("Open AI Settings"))
                {
                    ImGui.SetTooltip("Switch to 'AI Settings' tool to configure model paths");
                }

                ImGui.Spacing();
                DrawRequirementsInfo();
                return;
            }

            ImGui.TextWrapped("Automatic object detection and segmentation using natural language prompts. " +
                            "Grounding DINO detects objects, then SAM generates precise masks.");

            ImGui.Separator();

            // Segmenter selection
            ImGui.Text("Segmentation Model:");
            ImGui.SameLine();

            var prevType = _segmenterType;
            if (ImGui.RadioButton("SAM2", _segmenterType == GroundingSamPipeline.SegmenterType.SAM2))
            {
                _segmenterType = GroundingSamPipeline.SegmenterType.SAM2;
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("MicroSAM", _segmenterType == GroundingSamPipeline.SegmenterType.MicroSAM))
            {
                _segmenterType = GroundingSamPipeline.SegmenterType.MicroSAM;
            }

            // Recreate pipeline if type changed
            if (prevType != _segmenterType)
            {
                try
                {
                    _pipeline?.Dispose();
                    _pipeline = new GroundingSamPipeline(_segmenterType);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to switch segmenter: {ex.Message}");
                    _pipeline = null;
                    return;
                }
            }

            ImGui.Separator();

            // Text prompt
            ImGui.Text("Detection Prompt:");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextMultiline("##TextPrompt", ref _textPrompt, 500, new Vector2(0, 60));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Enter object descriptions separated by dots (e.g., 'rock . mineral . crystal .')");

            ImGui.Separator();

            // Point placement strategy
            var strategyNames = Enum.GetNames(typeof(PointPlacementStrategy));
            var currentStrategy = (int)_strategy;
            ImGui.Text("Point Strategy:");
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("##Strategy", ref currentStrategy, strategyNames, strategyNames.Length))
            {
                _strategy = (PointPlacementStrategy)currentStrategy;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("How to convert detected boxes to point prompts for SAM");

            ImGui.Separator();

            // Material selection
            ImGui.Text("Target Material:");
            DrawMaterialSelector(ct);

            ImGui.Separator();

            // Current slice selection
            ImGui.Text("Process Slice:");
            ImGui.SameLine();

            int sliceIdx = _currentSliceIndex >= 0 ? _currentSliceIndex : 0;
            if (ImGui.SliderInt("##SliceIdx", ref sliceIdx, 0, ct.Depth - 1))
            {
                _currentSliceIndex = sliceIdx;
            }

            ImGui.Separator();

            // Process button
            var buttonSize = new Vector2(150, 30);
            if (_isProcessing)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Processing...", buttonSize);
                ImGui.EndDisabled();
            }
            else
            {
                if (ImGui.Button("Detect & Segment", buttonSize))
                {
                    ProcessSlice(ct, _currentSliceIndex);
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Apply All to Material", buttonSize) && _results != null && _results.Count > 0)
            {
                ApplyAllResultsToMaterial(ct);
            }

            // Performance info
            if (_lastProcessingTime > 0)
            {
                ImGui.Separator();
                ImGui.Text($"Processing time: {_lastProcessingTime:F2}ms");
            }

            // Results
            if (_results != null && _results.Count > 0)
            {
                ImGui.Separator();
                ImGui.Checkbox("Show Results", ref _showResults);

                if (_showResults)
                {
                    ImGui.Text($"Detected {_results.Count} object(s):");

                    ImGui.BeginChild("ResultsList", new Vector2(0, 200), ImGuiChildFlags.Border);
                    for (int i = 0; i < _results.Count; i++)
                    {
                        var result = _results[i];
                        ImGui.PushID(i);

                        ImGui.Text($"Object {i + 1}:");
                        ImGui.SameLine();
                        ImGui.Text($"Score: {result.Score:F3}");

                        ImGui.Text($"  Box: ({result.BoundingBox.X1:F0}, {result.BoundingBox.Y1:F0}) - " +
                                  $"({result.BoundingBox.X2:F0}, {result.BoundingBox.Y2:F0})");

                        int maskPixels = 0;
                        if (result.Mask != null)
                        {
                            for (int y = 0; y < result.Mask.GetLength(0); y++)
                                for (int x = 0; x < result.Mask.GetLength(1); x++)
                                    if (result.Mask[y, x] > 0) maskPixels++;
                        }
                        ImGui.Text($"  Mask pixels: {maskPixels:N0}");

                        if (ImGui.SmallButton($"Apply to Material##{i}"))
                        {
                            ApplyResultToMaterial(ct, result);
                        }

                        ImGui.Separator();
                        ImGui.PopID();
                    }
                    ImGui.EndChild();
                }
            }
        }

        private void ProcessSlice(CtImageStackDataset dataset, int sliceIndex)
        {
            if (string.IsNullOrWhiteSpace(_textPrompt))
            {
                Console.WriteLine("Text prompt cannot be empty");
                return;
            }

            _isProcessing = true;

            try
            {
                var startTime = DateTime.Now;

                // Get slice as bitmap
                var slice = new byte[dataset.Width * dataset.Height];
                dataset.VolumeData.ReadSliceZ(sliceIndex, slice);
                using var bitmap = ConvertSliceToBitmap(slice, dataset.Width, dataset.Height);

                // Run pipeline
                _results = _pipeline.DetectAndSegment(bitmap, _textPrompt, _strategy);

                _lastProcessingTime = (float)(DateTime.Now - startTime).TotalMilliseconds;
                _showResults = true;

                Console.WriteLine($"Grounding-SAM pipeline completed: {_results.Count} objects in {_lastProcessingTime:F2}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pipeline failed: {ex.Message}");
                _results = null;
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void ApplyResultToMaterial(CtImageStackDataset dataset, SegmentationResult result)
        {
            if (result.Mask == null || _currentSliceIndex < 0) return;

            int width = dataset.Width;
            int height = dataset.Height;

            for (int y = 0; y < height && y < result.Mask.GetLength(0); y++)
            {
                for (int x = 0; x < width && x < result.Mask.GetLength(1); x++)
                {
                    if (result.Mask[y, x] > 0)
                    {
                        dataset.LabelData[x, y, _currentSliceIndex] = _targetMaterialId;
                    }
                }
            }

            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
        }

        private void ApplyAllResultsToMaterial(CtImageStackDataset dataset)
        {
            if (_results == null) return;

            foreach (var result in _results)
            {
                ApplyResultToMaterial(dataset, result);
            }

            Console.WriteLine($"Applied {_results.Count} segmentation results to material {_targetMaterialId}");
        }

        private void DrawMaterialSelector(CtImageStackDataset dataset)
        {
            var materials = dataset.Materials.Where(m => m.ID != 0).ToList();

            if (materials.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No materials defined");
                return;
            }

            ImGui.SetNextItemWidth(200);
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

        private void DrawRequirementsInfo()
        {
            ImGui.Text("Requirements:");

            var gdinoValid = _settings.ValidateGroundingDinoModel();
            var sam2Valid = _settings.ValidateSam2Models();
            var microsamValid = _settings.ValidateMicroSamModels();

            DrawRequirement("Grounding DINO models", gdinoValid);
            DrawRequirement("SAM2 models OR MicroSAM models", sam2Valid || microsamValid);
        }

        private void DrawRequirement(string name, bool met)
        {
            ImGui.Text("  ");
            ImGui.SameLine();
            if (met)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 1, 0.2f, 1));
                ImGui.Text("OK");
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.3f, 0.3f, 1));
                ImGui.Text("Missing");
            }
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.Text(name);
        }

        /// <summary>
        /// Get current results for rendering as overlay
        /// </summary>
        public List<SegmentationResult> GetCurrentResults() => _results;

        private void DrawErrorModal()
        {
            if (_showErrorModal)
            {
                ImGui.OpenPopup("Pipeline Initialization Error");
                _showErrorModal = false; // Open once
            }

            // Set red title bar color
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.7f, 0.1f, 0.1f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.9f, 0.1f, 0.1f, 1.0f));

            if (ImGui.BeginPopupModal("Pipeline Initialization Error", ref _showErrorModal,
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
            _pipeline?.Dispose();
        }
    }
}
