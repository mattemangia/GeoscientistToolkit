// GeoscientistToolkit/Data/Image/AISegmentation/ImageObjectExtractorTool.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Tools.CtImageStack.AISegmentation;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Image.AISegmentation
{
    /// <summary>
    /// Extract multiple objects as separate ImageDatasets
    /// Uses SAM + GroundingDino for automatic multi-object extraction
    /// </summary>
    public class ImageObjectExtractorTool : IDatasetTools, IDisposable
    {
        private ImageAISegmentationPipeline _pipeline;
        private readonly AISegmentationSettings _settings;

        // UI state
        private ImageAISegmentationPipeline.SegmenterType _segmenterType = ImageAISegmentationPipeline.SegmenterType.SAM2;
        private string _textPrompt = "object . item . particle .";
        private PointPlacementStrategy _strategy = PointPlacementStrategy.CenterPoint;

        // Results
        private List<ImageSegmentationResult> _detectedObjects;
        private bool _isProcessing;
        private float _lastProcessingTime;

        // Extraction options
        private bool[] _selectedForExtraction;
        private string _namingPattern = "object_{0}";
        private ExtractionMode _extractionMode = ExtractionMode.TransparentBackground;
        private bool _cropToBounds = true;
        private int _paddingPixels = 10;
        private bool _preserveCalibration = true;

        // Error handling
        private string _initializationError;
        private bool _showErrorModal;

        public enum ExtractionMode
        {
            TransparentBackground,
            WhiteBackground,
            BlackBackground,
            CheckerboardBackground,
            MaskOnly
        }

        public ImageObjectExtractorTool()
        {
            _settings = AISegmentationSettings.Instance;

            try
            {
                bool hasDino = _settings.ValidateGroundingDinoModel();
                bool hasSam2 = _settings.ValidateSam2Models();
                bool hasMicroSam = _settings.ValidateMicroSamModels();

                if (!hasDino)
                {
                    _initializationError = "Grounding DINO model not found. This tool requires object detection.";
                    _showErrorModal = true;
                }
                else if (!hasSam2 && !hasMicroSam)
                {
                    _initializationError = "No segmentation model found.";
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

            ImGui.SeparatorText("Multi-Object Extractor");

            if (_pipeline == null)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1),
                    "Pipeline not available. Check model paths in AI Settings.");
                return;
            }

            ImGui.TextWrapped("Detect and extract multiple objects as separate images with transparency.");
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

            // Detection settings
            ImGui.Text("Detection Prompt:");
            ImGui.InputText("##prompt", ref _textPrompt, 256);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Describe objects to detect (e.g., 'particle . grain . crystal .')");

            ImGui.Spacing();

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

            ImGui.Separator();

            // Extraction options
            ImGui.Text("Background:");
            if (ImGui.BeginCombo("##bgmode", GetExtractionModeDescription(_extractionMode)))
            {
                foreach (ExtractionMode mode in Enum.GetValues<ExtractionMode>())
                {
                    bool isSelected = _extractionMode == mode;
                    if (ImGui.Selectable(GetExtractionModeDescription(mode), isSelected))
                        _extractionMode = mode;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Checkbox("Crop to bounds", ref _cropToBounds);
            if (_cropToBounds)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.SliderInt("Padding", ref _paddingPixels, 0, 50);
            }

            ImGui.Checkbox("Preserve calibration", ref _preserveCalibration);

            ImGui.Spacing();

            ImGui.Text("Naming pattern:");
            ImGui.InputText("##naming", ref _namingPattern, 256);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Use {0} for index, {1} for class ID, {2} for score");

            ImGui.Separator();

            // Detect button
            if (_isProcessing)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1, 1), "Processing...");
            }
            else
            {
                if (ImGui.Button("Detect Objects", new Vector2(-1, 30)))
                {
                    DetectObjects(image);
                }
            }

            // Show results
            if (_detectedObjects != null && _detectedObjects.Count > 0)
            {
                ImGui.Spacing();
                ImGui.SeparatorText($"Detected Objects ({_detectedObjects.Count})");
                ImGui.Text($"Processing time: {_lastProcessingTime:F2}s");

                DrawObjectList();

                ImGui.Spacing();
                if (ImGui.Button("Extract Selected", new Vector2(-1, 0)))
                {
                    ExtractSelectedObjects(image);
                }

                ImGui.SameLine();
                if (ImGui.Button("Extract All", new Vector2(-1, 0)))
                {
                    ExtractAllObjects(image);
                }
            }
        }

        private void DetectObjects(ImageDataset dataset)
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
                _detectedObjects = _pipeline.DetectAndSegment(dataset, _textPrompt, _strategy);
                _lastProcessingTime = (float)(DateTime.Now - startTime).TotalSeconds;

                if (_detectedObjects.Count > 0)
                {
                    _selectedForExtraction = new bool[_detectedObjects.Count];
                    for (int i = 0; i < _selectedForExtraction.Length; i++)
                        _selectedForExtraction[i] = true; // Select all by default
                }

                Console.WriteLine($"Detected {_detectedObjects.Count} objects in {_lastProcessingTime:F2}s");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Detection failed: {ex.Message}");
                _detectedObjects = null;
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void DrawObjectList()
        {
            for (int i = 0; i < _detectedObjects.Count; i++)
            {
                var obj = _detectedObjects[i];
                ImGui.PushID(i);

                ImGui.Checkbox($"##select{i}", ref _selectedForExtraction[i]);
                ImGui.SameLine();

                ImGui.Text($"Object {i + 1}");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1),
                    $"Score: {obj.Score:F3} | Box: {obj.BoundingBox.Width:F0}x{obj.BoundingBox.Height:F0}");

                ImGui.PopID();
            }
        }

        private void ExtractSelectedObjects(ImageDataset sourceDataset)
        {
            int extracted = 0;

            for (int i = 0; i < _detectedObjects.Count; i++)
            {
                if (_selectedForExtraction[i])
                {
                    ExtractObject(sourceDataset, _detectedObjects[i], i);
                    extracted++;
                }
            }

            Console.WriteLine($"Extracted {extracted} objects to new images");
        }

        private void ExtractAllObjects(ImageDataset sourceDataset)
        {
            for (int i = 0; i < _detectedObjects.Count; i++)
            {
                ExtractObject(sourceDataset, _detectedObjects[i], i);
            }

            Console.WriteLine($"Extracted {_detectedObjects.Count} objects to new images");
        }

        private void ExtractObject(ImageDataset sourceDataset, ImageSegmentationResult result, int index)
        {
            var bbox = result.BoundingBox;
            var mask = result.Mask;

            // Calculate bounds
            int x1 = (int)Math.Max(0, bbox.X1 - _paddingPixels);
            int y1 = (int)Math.Max(0, bbox.Y1 - _paddingPixels);
            int x2 = (int)Math.Min(sourceDataset.Width - 1, bbox.X2 + _paddingPixels);
            int y2 = (int)Math.Min(sourceDataset.Height - 1, bbox.Y2 + _paddingPixels);

            int width = _cropToBounds ? (x2 - x1 + 1) : sourceDataset.Width;
            int height = _cropToBounds ? (y2 - y1 + 1) : sourceDataset.Height;

            byte[] newImageData = new byte[width * height * 4];

            if (_extractionMode == ExtractionMode.MaskOnly)
            {
                // Extract mask only as grayscale image
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcX = _cropToBounds ? x + x1 : x;
                        int srcY = _cropToBounds ? y + y1 : y;

                        int dstIdx = y * width + x;
                        int dstPixelIdx = dstIdx * 4;

                        byte maskValue = 0;
                        if (srcX >= 0 && srcX < sourceDataset.Width && srcY >= 0 && srcY < sourceDataset.Height)
                        {
                            maskValue = mask[srcY, srcX];
                        }

                        // Grayscale mask
                        newImageData[dstPixelIdx] = maskValue;
                        newImageData[dstPixelIdx + 1] = maskValue;
                        newImageData[dstPixelIdx + 2] = maskValue;
                        newImageData[dstPixelIdx + 3] = 255;
                    }
                }
            }
            else
            {
                // Fill background
                FillBackground(newImageData, width, height);

                // Copy pixels with mask
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int srcX = _cropToBounds ? x + x1 : x;
                        int srcY = _cropToBounds ? y + y1 : y;

                        if (srcX >= 0 && srcX < sourceDataset.Width && srcY >= 0 && srcY < sourceDataset.Height)
                        {
                            int srcIdx = srcY * sourceDataset.Width + srcX;
                            int dstIdx = y * width + x;

                            int srcPixelIdx = srcIdx * 4;
                            int dstPixelIdx = dstIdx * 4;

                            if (mask[srcY, srcX] > 0)
                            {
                                // Copy pixel
                                newImageData[dstPixelIdx] = sourceDataset.ImageData[srcPixelIdx];
                                newImageData[dstPixelIdx + 1] = sourceDataset.ImageData[srcPixelIdx + 1];
                                newImageData[dstPixelIdx + 2] = sourceDataset.ImageData[srcPixelIdx + 2];

                                if (_extractionMode == ExtractionMode.TransparentBackground)
                                    newImageData[dstPixelIdx + 3] = mask[srcY, srcX];
                                else
                                    newImageData[dstPixelIdx + 3] = 255;
                            }
                        }
                    }
                }
            }

            // Create new dataset
            string name = string.Format(_namingPattern, index, result.ClassId, (int)(result.Score * 100));
            var newDataset = new ImageDataset(name, "");
            newDataset.Width = width;
            newDataset.Height = height;
            newDataset.BitDepth = 32;
            newDataset.ImageData = newImageData;

            if (_preserveCalibration)
            {
                newDataset.PixelSize = sourceDataset.PixelSize;
                newDataset.Unit = sourceDataset.Unit;
                newDataset.Tags = sourceDataset.Tags;
            }

            // Add metadata
            newDataset.ImageMetadata["ExtractedFrom"] = sourceDataset.Name;
            newDataset.ImageMetadata["ExtractionIndex"] = index;
            newDataset.ImageMetadata["DetectionScore"] = result.Score;
            newDataset.ImageMetadata["BoundingBox"] = $"{bbox.X1},{bbox.Y1},{bbox.X2},{bbox.Y2}";

            ProjectManager.Instance.AddDataset(newDataset);
            ProjectManager.Instance.HasUnsavedChanges = true;
        }

        private void FillBackground(byte[] imageData, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 4;

                    switch (_extractionMode)
                    {
                        case ExtractionMode.TransparentBackground:
                            imageData[idx + 3] = 0;
                            break;

                        case ExtractionMode.WhiteBackground:
                            imageData[idx] = 255;
                            imageData[idx + 1] = 255;
                            imageData[idx + 2] = 255;
                            imageData[idx + 3] = 255;
                            break;

                        case ExtractionMode.BlackBackground:
                            imageData[idx + 3] = 255;
                            break;

                        case ExtractionMode.CheckerboardBackground:
                            int checkerSize = 8;
                            bool isLight = ((x / checkerSize) + (y / checkerSize)) % 2 == 0;
                            byte val = isLight ? (byte)200 : (byte)150;
                            imageData[idx] = val;
                            imageData[idx + 1] = val;
                            imageData[idx + 2] = val;
                            imageData[idx + 3] = 255;
                            break;
                    }
                }
            }
        }

        private string GetExtractionModeDescription(ExtractionMode mode)
        {
            return mode switch
            {
                ExtractionMode.TransparentBackground => "Transparent (PNG)",
                ExtractionMode.WhiteBackground => "White",
                ExtractionMode.BlackBackground => "Black",
                ExtractionMode.CheckerboardBackground => "Checkerboard",
                ExtractionMode.MaskOnly => "Mask Only (Grayscale)",
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

        public void Dispose()
        {
            _pipeline?.Dispose();
        }
    }
}
