// GeoscientistToolkit/Data/Image/AISegmentation/ImageSam2InteractiveTool.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Tools.CtImageStack.AISegmentation;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Image.AISegmentation
{
    /// <summary>
    /// Interactive SAM2/MicroSAM tool for ImageDataset
    /// Point-based segmentation with positive/negative prompts
    /// </summary>
    public class ImageSam2InteractiveTool : IDatasetTools, IDisposable
    {
        private ImageAISegmentationPipeline _pipeline;
        private readonly AISegmentationSettings _settings;

        // UI state
        private ImageAISegmentationPipeline.SegmenterType _segmenterType = ImageAISegmentationPipeline.SegmenterType.SAM2;
        private bool _isPositivePoint = true;
        private bool _autoApply = false;

        // Point prompts
        private List<(float x, float y)> _points = new();
        private List<float> _labels = new();

        // Current mask
        private byte[,] _currentMask;
        private bool _hasMask;

        // Material assignment
        private byte _targetMaterialId = 0;

        // Error handling
        private string _initializationError;
        private bool _showErrorModal;
        private bool _isProcessing;

        // Click handler callback
        public Action<float, float> OnImageClick;

        public ImageSam2InteractiveTool()
        {
            _settings = AISegmentationSettings.Instance;

            try
            {
                bool hasSam2 = _settings.ValidateSam2Models();
                bool hasMicroSam = _settings.ValidateMicroSamModels();

                if (!hasSam2 && !hasMicroSam)
                {
                    _initializationError = "No segmentation model found.\n\n" +
                        "Please configure either SAM2 or MicroSAM models in AI Settings.";
                    _showErrorModal = true;
                }
                else
                {
                    var segmenterType = hasSam2
                        ? ImageAISegmentationPipeline.SegmenterType.SAM2
                        : ImageAISegmentationPipeline.SegmenterType.MicroSAM;

                    _pipeline = new ImageAISegmentationPipeline(segmenterType);
                    _segmenterType = segmenterType;
                }
            }
            catch (Exception ex)
            {
                _initializationError = $"Failed to initialize pipeline:\n\n{ex.Message}";
                _showErrorModal = true;
            }
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not ImageDataset image) return;

            DrawErrorModal();

            ImGui.SeparatorText("AI Segmentation: Interactive SAM2");

            if (_pipeline == null)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1),
                    "Pipeline not available. Check model paths in AI Settings.");
                DrawRequirementsInfo();
                return;
            }

            ImGui.TextWrapped("Click on the image to add points. SAM2 will segment the region.");
            ImGui.Separator();

            // Segmenter selection
            ImGui.Text("Model:");
            ImGui.SameLine();

            var prevType = _segmenterType;
            if (ImGui.RadioButton("SAM2", _segmenterType == ImageAISegmentationPipeline.SegmenterType.SAM2))
                _segmenterType = ImageAISegmentationPipeline.SegmenterType.SAM2;
            ImGui.SameLine();
            if (ImGui.RadioButton("MicroSAM", _segmenterType == ImageAISegmentationPipeline.SegmenterType.MicroSAM))
                _segmenterType = ImageAISegmentationPipeline.SegmenterType.MicroSAM;

            if (prevType != _segmenterType)
            {
                try
                {
                    _pipeline?.Dispose();
                    _pipeline = new ImageAISegmentationPipeline(_segmenterType);
                    ClearPoints();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to switch segmenter: {ex.Message}");
                    _pipeline = null;
                    return;
                }
            }

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

            // Auto-apply option
            ImGui.Checkbox("Auto-apply after segment", ref _autoApply);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Automatically apply segmentation to dataset after each update");

            ImGui.Spacing();

            // Material assignment
            ImGui.Text("Material:");
            var segmentation = image.GetOrCreateSegmentation();
            if (ImGui.BeginCombo("##material", GetMaterialName(segmentation, _targetMaterialId)))
            {
                if (ImGui.Selectable("Auto-create new", _targetMaterialId == 0))
                    _targetMaterialId = 0;

                foreach (var mat in segmentation.Materials)
                {
                    if (mat.IsExterior) continue;
                    bool isSelected = _targetMaterialId == mat.ID;
                    if (ImGui.Selectable($"{mat.Name} (ID: {mat.ID})", isSelected))
                        _targetMaterialId = mat.ID;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Separator();

            // Points info
            ImGui.Text($"Points: {_points.Count}");
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

            // Action buttons
            if (_isProcessing)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1, 1), "Processing...");
            }
            else
            {
                if (_points.Count > 0)
                {
                    if (ImGui.Button("Segment", new Vector2(-1, 30)))
                    {
                        ProcessSegmentation(image);
                    }

                    if (_hasMask && !_autoApply)
                    {
                        if (ImGui.Button("Apply to Dataset", new Vector2(-1, 0)))
                        {
                            ApplyMaskToDataset(image);
                        }
                    }

                    if (ImGui.Button("Clear Points", new Vector2(-1, 0)))
                    {
                        ClearPoints();
                    }
                }
                else
                {
                    ImGui.TextWrapped("Click on the image to add points");
                }
            }
        }

        public void AddPoint(ImageDataset dataset, float x, float y)
        {
            _points.Add((x, y));
            _labels.Add(_isPositivePoint ? 1.0f : 0.0f);

            // Auto-segment if enabled
            if (_autoApply && _points.Count > 0)
            {
                ProcessSegmentation(dataset);
            }
        }

        private void ProcessSegmentation(ImageDataset dataset)
        {
            if (_points.Count == 0) return;

            _isProcessing = true;

            try
            {
                _currentMask = _pipeline.SegmentWithPoints(dataset, _points, _labels);
                _hasMask = true;

                if (_autoApply)
                {
                    ApplyMaskToDataset(dataset);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Segmentation failed: {ex.Message}");
                _currentMask = null;
                _hasMask = false;
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void ApplyMaskToDataset(ImageDataset dataset)
        {
            if (!_hasMask || _currentMask == null) return;

            var segmentation = dataset.GetOrCreateSegmentation();
            segmentation.SaveUndoState();

            // Create new material if needed
            byte materialId = _targetMaterialId;
            if (materialId == 0)
            {
                var material = segmentation.AddMaterial(
                    $"SAM Segment {segmentation.Materials.Count}",
                    new Vector4(
                        (float)Random.Shared.NextDouble(),
                        (float)Random.Shared.NextDouble(),
                        (float)Random.Shared.NextDouble(),
                        0.6f));
                materialId = material.ID;
            }

            // Apply mask
            ImageAISegmentationPipeline.ApplyMaskToLabels(
                _currentMask,
                segmentation.LabelData,
                materialId,
                dataset.Width);

            Console.WriteLine($"Applied mask to dataset with material ID {materialId}");
        }

        private void ClearPoints()
        {
            _points.Clear();
            _labels.Clear();
            _currentMask = null;
            _hasMask = false;
        }

        private string GetMaterialName(ImageSegmentationData segmentation, byte id)
        {
            if (id == 0) return "Auto-create new";
            var mat = segmentation.GetMaterial(id);
            return mat != null ? mat.Name : "Unknown";
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
            ImGui.BulletText("OR MicroSAM: micro-sam-encoder.onnx + micro-sam-decoder.onnx");
        }

        public bool IsPointMode => true;

        public byte[,] GetCurrentMask() => _currentMask;

        public bool HasMask => _hasMask;

        public void Dispose()
        {
            _pipeline?.Dispose();
        }
    }
}
