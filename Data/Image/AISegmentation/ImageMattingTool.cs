// GeoscientistToolkit/Data/Image/AISegmentation/ImageMattingTool.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Tools.CtImageStack.AISegmentation;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Image.AISegmentation
{
    /// <summary>
    /// SAM-powered image matting tool
    /// Extract foreground or background with transparency
    /// </summary>
    public class ImageMattingTool : IDatasetTools, IDisposable
    {
        private ImageAISegmentationPipeline _pipeline;
        private readonly AISegmentationSettings _settings;

        // UI state
        private ImageAISegmentationPipeline.SegmenterType _segmenterType = ImageAISegmentationPipeline.SegmenterType.SAM2;
        private MattingMode _mattingMode = MattingMode.ExtractForeground;
        private bool _useTextPrompt = false;
        private string _textPrompt = "main object . subject .";

        // Point prompts for manual mode
        private List<(float x, float y)> _positivePoints = new();
        private List<(float x, float y)> _negativePoints = new();
        private bool _isAddingPositivePoints = true;

        // Current mask
        private byte[,] _currentMask;
        private bool _hasMask;
        private bool _isProcessing;

        // Export options
        private string _outputName = "matted_image";
        private bool _invertMask = false;
        private bool _featherEdges = true;
        private int _featherRadius = 2;

        // Error handling
        private string _initializationError;
        private bool _showErrorModal;

        public enum MattingMode
        {
            ExtractForeground,
            ExtractBackground,
            CreateAlphaMask
        }

        public ImageMattingTool()
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

            ImGui.SeparatorText("Image Matting & Extraction");

            if (_pipeline == null)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1),
                    "Pipeline not available. Check model paths in AI Settings.");
                return;
            }

            ImGui.TextWrapped("Extract foreground/background with transparency using SAM segmentation.");
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

            // Mode selection
            ImGui.Text("Mode:");
            if (ImGui.BeginCombo("##mode", _mattingMode.ToString()))
            {
                foreach (MattingMode mode in Enum.GetValues<MattingMode>())
                {
                    bool isSelected = _mattingMode == mode;
                    if (ImGui.Selectable(GetModeDescription(mode), isSelected))
                        _mattingMode = mode;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Spacing();

            // Selection method
            ImGui.Checkbox("Use text prompt (Grounding DINO)", ref _useTextPrompt);

            if (_useTextPrompt)
            {
                ImGui.InputText("Prompt", ref _textPrompt, 256);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Describe the object to extract (e.g., 'person . subject . foreground .')");
            }
            else
            {
                ImGui.TextWrapped("Click on the image to add points:");
                ImGui.Text($"Positive points: {_positivePoints.Count}");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.3f, 1, 0.3f, 1), "(include)");

                ImGui.Text($"Negative points: {_negativePoints.Count}");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), "(exclude)");

                if (ImGui.RadioButton("Add Positive", _isAddingPositivePoints))
                    _isAddingPositivePoints = true;
                ImGui.SameLine();
                if (ImGui.RadioButton("Add Negative", !_isAddingPositivePoints))
                    _isAddingPositivePoints = false;
            }

            ImGui.Separator();

            // Processing options
            ImGui.Checkbox("Invert mask", ref _invertMask);
            ImGui.Checkbox("Feather edges", ref _featherEdges);
            if (_featherEdges)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.SliderInt("##feather", ref _featherRadius, 1, 10);
            }

            ImGui.Spacing();

            // Output name
            ImGui.Text("Output name:");
            ImGui.InputText("##outputname", ref _outputName, 256);

            ImGui.Separator();

            // Action buttons
            if (_isProcessing)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1, 1), "Processing...");
            }
            else
            {
                bool canProcess = _useTextPrompt ? !string.IsNullOrWhiteSpace(_textPrompt)
                                                 : (_positivePoints.Count > 0);

                if (!canProcess)
                {
                    ImGui.TextColored(new Vector4(1, 0.5f, 0, 1),
                        _useTextPrompt ? "Enter a text prompt" : "Add at least one positive point");
                }

                ImGui.BeginDisabled(!canProcess);

                if (ImGui.Button("Generate Mask", new Vector2(-1, 30)))
                {
                    ProcessMatting(image, false);
                }

                if (_hasMask)
                {
                    if (ImGui.Button("Preview Mask", new Vector2(-1, 0)))
                    {
                        ShowMaskPreview(image);
                    }

                    if (ImGui.Button("Extract to New Image", new Vector2(-1, 0)))
                    {
                        ProcessMatting(image, true);
                    }
                }

                ImGui.EndDisabled();

                if (ImGui.Button("Clear Points", new Vector2(-1, 0)))
                {
                    ClearPoints();
                }
            }
        }

        public void AddPoint(ImageDataset dataset, float x, float y, bool isPositive)
        {
            if (isPositive)
                _positivePoints.Add((x, y));
            else
                _negativePoints.Add((x, y));
        }

        public void AddPointCurrent(ImageDataset dataset, float x, float y)
        {
            AddPoint(dataset, x, y, _isAddingPositivePoints);
        }

        private void ProcessMatting(ImageDataset dataset, bool createNewImage)
        {
            _isProcessing = true;

            try
            {
                byte[,] mask;

                if (_useTextPrompt)
                {
                    // Use Grounding DINO + SAM
                    var results = _pipeline.DetectAndSegment(dataset, _textPrompt, PointPlacementStrategy.CenterPoint);

                    if (results.Count == 0)
                    {
                        Console.WriteLine("No objects detected with the given prompt");
                        _isProcessing = false;
                        return;
                    }

                    // Use the first/largest detection
                    mask = results[0].Mask;
                }
                else
                {
                    // Use points
                    var points = new List<(float x, float y)>();
                    var labels = new List<float>();

                    foreach (var pt in _positivePoints)
                    {
                        points.Add(pt);
                        labels.Add(1.0f);
                    }

                    foreach (var pt in _negativePoints)
                    {
                        points.Add(pt);
                        labels.Add(0.0f);
                    }

                    mask = _pipeline.SegmentWithPoints(dataset, points, labels);
                }

                if (_invertMask)
                    mask = InvertMask(mask);

                if (_featherEdges)
                    mask = FeatherMask(mask, _featherRadius);

                _currentMask = mask;
                _hasMask = true;

                if (createNewImage)
                {
                    CreateMattedImage(dataset, mask);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Matting failed: {ex.Message}");
                _currentMask = null;
                _hasMask = false;
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void CreateMattedImage(ImageDataset sourceDataset, byte[,] mask)
        {
            int width = sourceDataset.Width;
            int height = sourceDataset.Height;
            byte[] newImageData = new byte[width * height * 4];

            // Apply mask based on mode
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    int pixelIdx = idx * 4;

                    bool isForeground = mask[y, x] > 0;

                    switch (_mattingMode)
                    {
                        case MattingMode.ExtractForeground:
                            if (isForeground)
                            {
                                // Keep original pixel with alpha
                                newImageData[pixelIdx] = sourceDataset.ImageData[pixelIdx];
                                newImageData[pixelIdx + 1] = sourceDataset.ImageData[pixelIdx + 1];
                                newImageData[pixelIdx + 2] = sourceDataset.ImageData[pixelIdx + 2];
                                newImageData[pixelIdx + 3] = mask[y, x]; // Use mask as alpha
                            }
                            else
                            {
                                // Transparent
                                newImageData[pixelIdx + 3] = 0;
                            }
                            break;

                        case MattingMode.ExtractBackground:
                            if (!isForeground)
                            {
                                // Keep background
                                newImageData[pixelIdx] = sourceDataset.ImageData[pixelIdx];
                                newImageData[pixelIdx + 1] = sourceDataset.ImageData[pixelIdx + 1];
                                newImageData[pixelIdx + 2] = sourceDataset.ImageData[pixelIdx + 2];
                                newImageData[pixelIdx + 3] = (byte)(255 - mask[y, x]);
                            }
                            else
                            {
                                // Transparent
                                newImageData[pixelIdx + 3] = 0;
                            }
                            break;

                        case MattingMode.CreateAlphaMask:
                            // Create grayscale alpha mask
                            byte alpha = mask[y, x];
                            newImageData[pixelIdx] = alpha;
                            newImageData[pixelIdx + 1] = alpha;
                            newImageData[pixelIdx + 2] = alpha;
                            newImageData[pixelIdx + 3] = 255;
                            break;
                    }
                }
            }

            // Create new ImageDataset
            var newDataset = new ImageDataset(_outputName, "");
            newDataset.Width = width;
            newDataset.Height = height;
            newDataset.BitDepth = 32; // RGBA
            newDataset.ImageData = newImageData;
            newDataset.PixelSize = sourceDataset.PixelSize;
            newDataset.Unit = sourceDataset.Unit;
            newDataset.Tags = sourceDataset.Tags;

            // Copy metadata
            foreach (var kvp in sourceDataset.ImageMetadata)
            {
                newDataset.ImageMetadata[kvp.Key] = kvp.Value;
            }

            // Add to project
            ProjectManager.Instance.AddDataset(newDataset);
            ProjectManager.Instance.HasUnsavedChanges = true;

            Console.WriteLine($"Created matted image: {_outputName}");
        }

        private void ShowMaskPreview(ImageDataset dataset)
        {
            if (!_hasMask || _currentMask == null) return;

            // Apply mask to dataset's segmentation for preview
            var segmentation = dataset.GetOrCreateSegmentation();
            segmentation.SaveUndoState();

            // Create or get preview material
            var previewMaterial = segmentation.Materials.FirstOrDefault(m => m.Name == "Matting Preview");
            if (previewMaterial == null)
            {
                previewMaterial = segmentation.AddMaterial("Matting Preview", new Vector4(0, 1, 0, 0.5f));
            }

            // Apply mask
            ImageAISegmentationPipeline.ApplyMaskToLabels(
                _currentMask,
                segmentation.LabelData,
                previewMaterial.ID,
                dataset.Width);

            dataset.ShowSegmentationOverlay = true;
        }

        private byte[,] InvertMask(byte[,] mask)
        {
            int height = mask.GetLength(0);
            int width = mask.GetLength(1);
            var inverted = new byte[height, width];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    inverted[y, x] = (byte)(255 - mask[y, x]);
                }
            }

            return inverted;
        }

        private byte[,] FeatherMask(byte[,] mask, int radius)
        {
            int height = mask.GetLength(0);
            int width = mask.GetLength(1);
            var feathered = new byte[height, width];

            // Simple box blur for feathering
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sum = 0;
                    int count = 0;

                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            int ny = y + dy;
                            int nx = x + dx;

                            if (ny >= 0 && ny < height && nx >= 0 && nx < width)
                            {
                                sum += mask[ny, nx];
                                count++;
                            }
                        }
                    }

                    feathered[y, x] = (byte)(sum / count);
                }
            }

            return feathered;
        }

        private void ClearPoints()
        {
            _positivePoints.Clear();
            _negativePoints.Clear();
            _currentMask = null;
            _hasMask = false;
        }

        private string GetModeDescription(MattingMode mode)
        {
            return mode switch
            {
                MattingMode.ExtractForeground => "Extract Foreground (transparent bg)",
                MattingMode.ExtractBackground => "Extract Background (transparent fg)",
                MattingMode.CreateAlphaMask => "Create Alpha Mask (grayscale)",
                _ => mode.ToString()
            };
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

        public bool IsPointMode => !_useTextPrompt;

        public bool IsAddingPositivePoints => _isAddingPositivePoints;

        public void Dispose()
        {
            _pipeline?.Dispose();
        }
    }
}
