// GeoscientistToolkit/Data/Image/AISegmentation/ImageGroundingSamTool.cs

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
    /// Grounding DINO + SAM pipeline tool for ImageDataset
    /// Automated object detection and segmentation for 2D images
    /// </summary>
    public class ImageGroundingSamTool : IDatasetTools, IDisposable
    {
        private ImageAISegmentationPipeline _pipeline;
        private readonly AISegmentationSettings _settings;

        // UI state
        private string _textPrompt = "mineral . crystal . particle .";
        private ImageAISegmentationPipeline.SegmenterType _segmenterType = ImageAISegmentationPipeline.SegmenterType.SAM2;
        private PointPlacementStrategy _strategy;
        private bool _isProcessing;

        // Results
        private List<ImageSegmentationResult> _results;
        private float _lastProcessingTime;

        // Material assignment
        private byte _targetMaterialId = 0; // 0 = auto-create new material
        private bool _showResults;
        private int _selectedResultIndex = -1;

        // Error handling
        private string _initializationError;
        private bool _showErrorModal;

        public ImageGroundingSamTool()
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

            ImGui.SeparatorText("AI Segmentation: Grounding DINO + SAM");

            if (_pipeline == null)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1),
                    "Pipeline not available. Check model paths in AI Settings.");
                DrawRequirementsInfo();
                return;
            }

            ImGui.TextWrapped("Automatic object detection and segmentation using natural language prompts.");
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
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to switch segmenter: {ex.Message}");
                    _pipeline = null;
                    return;
                }
            }

            ImGui.Separator();

            // Text prompt input
            ImGui.Text("Text Prompt:");
            ImGui.InputText("##prompt", ref _textPrompt, 256);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Natural language description. Separate multiple objects with ' . ' (e.g., 'mineral . crystal . particle .')");

            ImGui.Spacing();

            // Point placement strategy
            ImGui.Text("Point Strategy:");
            if (ImGui.BeginCombo("##strategy", _strategy.ToString()))
            {
                foreach (PointPlacementStrategy strat in Enum.GetValues<PointPlacementStrategy>())
                {
                    bool isSelected = _strategy == strat;
                    if (ImGui.Selectable(strat.ToString(), isSelected))
                        _strategy = strat;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Spacing();

            // Material assignment
            ImGui.Text("Material:");
            var segmentation = image.GetOrCreateSegmentation();
            if (ImGui.BeginCombo("##material", GetMaterialName(segmentation, _targetMaterialId)))
            {
                // Auto-create option
                if (ImGui.Selectable("Auto-create new", _targetMaterialId == 0))
                    _targetMaterialId = 0;

                // Existing materials
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

            // Process button
            if (_isProcessing)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1, 1), "Processing...");
            }
            else
            {
                if (ImGui.Button("Detect and Segment", new Vector2(-1, 30)))
                {
                    ProcessImage(image);
                }
            }

            // Show results
            if (_results != null && _results.Count > 0)
            {
                ImGui.Spacing();
                ImGui.SeparatorText($"Results ({_results.Count} objects found)");
                ImGui.Text($"Processing time: {_lastProcessingTime:F2}s");

                DrawResults(image);
            }
        }

        private void ProcessImage(ImageDataset dataset)
        {
            if (string.IsNullOrWhiteSpace(_textPrompt))
            {
                Console.WriteLine("Text prompt is empty");
                return;
            }

            _isProcessing = true;

            try
            {
                var startTime = DateTime.Now;
                _results = _pipeline.DetectAndSegment(dataset, _textPrompt, _strategy);
                _lastProcessingTime = (float)(DateTime.Now - startTime).TotalSeconds;

                Console.WriteLine($"Found {_results.Count} objects in {_lastProcessingTime:F2}s");
                _showResults = true;
                _selectedResultIndex = -1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Segmentation failed: {ex.Message}");
                _results = null;
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void DrawResults(ImageDataset dataset)
        {
            for (int i = 0; i < _results.Count; i++)
            {
                var result = _results[i];
                ImGui.PushID(i);

                bool isSelected = _selectedResultIndex == i;
                if (ImGui.Selectable($"Object {i + 1}", isSelected))
                    _selectedResultIndex = i;

                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1),
                    $"Score: {result.Score:F3} | Box: ({result.BoundingBox.X1:F0}, {result.BoundingBox.Y1:F0}) - ({result.BoundingBox.X2:F0}, {result.BoundingBox.Y2:F0})");

                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 80);
                if (ImGui.Button("Apply", new Vector2(80, 0)))
                {
                    _pipeline.ApplyToDataset(dataset, result, _targetMaterialId);
                    Console.WriteLine($"Applied object {i + 1} to dataset");
                }

                ImGui.PopID();
            }

            ImGui.Spacing();
            if (ImGui.Button("Apply All", new Vector2(-1, 0)))
            {
                foreach (var result in _results)
                {
                    _pipeline.ApplyToDataset(dataset, result, _targetMaterialId);
                }
                Console.WriteLine($"Applied all {_results.Count} objects to dataset");
            }
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
            ImGui.BulletText("Grounding DINO: g_dino.onnx + vocab.txt");
            ImGui.BulletText("SAM2: sam2.1_large.encoder.onnx + sam2.1_large.decoder.onnx");
            ImGui.BulletText("OR MicroSAM: micro-sam-encoder.onnx + micro-sam-decoder.onnx");
        }

        public void Dispose()
        {
            _pipeline?.Dispose();
        }
    }
}
