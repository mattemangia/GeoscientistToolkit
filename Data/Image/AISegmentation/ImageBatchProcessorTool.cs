// GeoscientistToolkit/Data/Image/AISegmentation/ImageBatchProcessorTool.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Tools.CtImageStack.AISegmentation;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Image.AISegmentation
{
    /// <summary>
    /// Batch process multiple images with SAM
    /// Apply background removal, object extraction, or segmentation to image collections
    /// </summary>
    public class ImageBatchProcessorTool : IDatasetTools, IDisposable
    {
        private readonly AISegmentationSettings _settings;

        // UI state
        private ImageAISegmentationPipeline.SegmenterType _segmenterType = ImageAISegmentationPipeline.SegmenterType.SAM2;
        private BatchOperation _operation = BatchOperation.RemoveBackground;
        private string _textPrompt = "main object . foreground .";
        private PointPlacementStrategy _strategy = PointPlacementStrategy.CenterPoint;

        // Image selection
        private List<ImageDataset> _availableImages = new();
        private bool[] _selectedImages;
        private bool _selectAll = true;

        // Processing state
        private bool _isProcessing;
        private int _processedCount;
        private int _totalCount;
        private string _currentProcessing = "";
        private List<string> _processingLog = new();

        // Options
        private string _outputSuffix = "_processed";
        private bool _createNewDatasets = true;
        private bool _preserveOriginals = true;

        // Error handling
        private string _initializationError;
        private bool _showErrorModal;

        public enum BatchOperation
        {
            RemoveBackground,
            ExtractObjects,
            ApplySegmentation,
            CreateAlphaMasks
        }

        public ImageBatchProcessorTool()
        {
            _settings = AISegmentationSettings.Instance;

            try
            {
                bool hasSam2 = _settings.ValidateSam2Models();
                bool hasMicroSam = _settings.ValidateMicroSamModels();
                bool hasDino = _settings.ValidateGroundingDinoModel();

                if (!hasSam2 && !hasMicroSam)
                {
                    _initializationError = "No segmentation model found.";
                    _showErrorModal = true;
                }
            }
            catch (Exception ex)
            {
                _initializationError = $"Failed to initialize: {ex.Message}";
                _showErrorModal = true;
            }
        }

        public void Draw(Dataset dataset)
        {
            DrawErrorModal();

            ImGui.SeparatorText("Batch Image Processor");

            ImGui.TextWrapped("Process multiple images with SAM-powered operations.");
            ImGui.Separator();

            // Refresh image list
            RefreshImageList();

            // Segmenter selection
            ImGui.Text("Model:");
            ImGui.SameLine();

            if (ImGui.RadioButton("SAM2", _segmenterType == ImageAISegmentationPipeline.SegmenterType.SAM2))
                _segmenterType = ImageAISegmentationPipeline.SegmenterType.SAM2;
            ImGui.SameLine();
            if (ImGui.RadioButton("MicroSAM", _segmenterType == ImageAISegmentationPipeline.SegmenterType.MicroSAM))
                _segmenterType = ImageAISegmentationPipeline.SegmenterType.MicroSAM;

            ImGui.Separator();

            // Operation selection
            ImGui.Text("Operation:");
            if (ImGui.BeginCombo("##operation", GetOperationDescription(_operation)))
            {
                foreach (BatchOperation op in Enum.GetValues<BatchOperation>())
                {
                    bool isSelected = _operation == op;
                    if (ImGui.Selectable(GetOperationDescription(op), isSelected))
                        _operation = op;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Spacing();

            // Operation-specific settings
            if (_operation == BatchOperation.ExtractObjects || _operation == BatchOperation.ApplySegmentation)
            {
                ImGui.Text("Text Prompt:");
                ImGui.InputText("##prompt", ref _textPrompt, 256);

                ImGui.Text("Strategy:");
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
            }

            ImGui.Separator();

            // Output options
            ImGui.Checkbox("Create new datasets", ref _createNewDatasets);
            if (_createNewDatasets)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("Suffix", ref _outputSuffix, 64);
            }

            ImGui.Checkbox("Preserve originals", ref _preserveOriginals);

            ImGui.Separator();

            // Image selection
            ImGui.Text($"Select Images ({_availableImages.Count} available):");

            if (ImGui.Checkbox("Select All", ref _selectAll))
            {
                for (int i = 0; i < _selectedImages.Length; i++)
                    _selectedImages[i] = _selectAll;
            }

            ImGui.BeginChild("ImageList", new Vector2(0, 200), ImGuiChildFlags.Border);

            for (int i = 0; i < _availableImages.Count; i++)
            {
                ImGui.Checkbox(_availableImages[i].Name, ref _selectedImages[i]);
            }

            ImGui.EndChild();

            int selectedCount = _selectedImages.Count(s => s);
            ImGui.Text($"Selected: {selectedCount} images");

            ImGui.Separator();

            // Processing controls
            if (_isProcessing)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1, 1), "Processing...");
                ImGui.Text($"Progress: {_processedCount}/{_totalCount}");
                ImGui.Text($"Current: {_currentProcessing}");

                ImGui.Spacing();
                ImGui.BeginChild("ProcessingLog", new Vector2(0, 100), ImGuiChildFlags.Border);
                foreach (var log in _processingLog.TakeLast(10))
                {
                    ImGui.TextWrapped(log);
                }
                ImGui.EndChild();
            }
            else
            {
                ImGui.BeginDisabled(selectedCount == 0);

                if (ImGui.Button("Process Selected Images", new Vector2(-1, 40)))
                {
                    ProcessSelectedImages();
                }

                ImGui.EndDisabled();
            }
        }

        private void RefreshImageList()
        {
            var allImages = ProjectManager.Instance.LoadedDatasets
                .OfType<ImageDataset>()
                .Where(img => img.ImageData != null || System.IO.File.Exists(img.FilePath))
                .ToList();

            if (_availableImages.Count != allImages.Count)
            {
                _availableImages = allImages;
                _selectedImages = new bool[allImages.Count];
                for (int i = 0; i < _selectedImages.Length; i++)
                    _selectedImages[i] = _selectAll;
            }
        }

        private async void ProcessSelectedImages()
        {
            var imagesToProcess = new List<ImageDataset>();
            for (int i = 0; i < _availableImages.Count; i++)
            {
                if (_selectedImages[i])
                    imagesToProcess.Add(_availableImages[i]);
            }

            if (imagesToProcess.Count == 0) return;

            _isProcessing = true;
            _processedCount = 0;
            _totalCount = imagesToProcess.Count;
            _processingLog.Clear();

            await Task.Run(() =>
            {
                foreach (var image in imagesToProcess)
                {
                    try
                    {
                        _currentProcessing = image.Name;
                        ProcessSingleImage(image);
                        _processedCount++;
                        _processingLog.Add($"✓ {image.Name} - Success");
                    }
                    catch (Exception ex)
                    {
                        _processingLog.Add($"✗ {image.Name} - Error: {ex.Message}");
                    }
                }
            });

            _isProcessing = false;
            _currentProcessing = "";

            Console.WriteLine($"Batch processing complete: {_processedCount}/{_totalCount} images processed");
        }

        private void ProcessSingleImage(ImageDataset image)
        {
            image.Load();

            using var pipeline = new ImageAISegmentationPipeline(_segmenterType);

            switch (_operation)
            {
                case BatchOperation.RemoveBackground:
                    RemoveBackground(image, pipeline);
                    break;

                case BatchOperation.ExtractObjects:
                    ExtractObjects(image, pipeline);
                    break;

                case BatchOperation.ApplySegmentation:
                    ApplySegmentation(image, pipeline);
                    break;

                case BatchOperation.CreateAlphaMasks:
                    CreateAlphaMasks(image, pipeline);
                    break;
            }
        }

        private void RemoveBackground(ImageDataset image, ImageAISegmentationPipeline pipeline)
        {
            // Detect main object
            var results = pipeline.DetectAndSegment(image, "main object . subject . foreground .", PointPlacementStrategy.CenterPoint);

            if (results.Count == 0) return;

            var mask = results[0].Mask;

            // Create transparent background image
            byte[] newImageData = new byte[image.Width * image.Height * 4];

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    int idx = y * image.Width + x;
                    int pixelIdx = idx * 4;

                    if (mask[y, x] > 0)
                    {
                        newImageData[pixelIdx] = image.ImageData[pixelIdx];
                        newImageData[pixelIdx + 1] = image.ImageData[pixelIdx + 1];
                        newImageData[pixelIdx + 2] = image.ImageData[pixelIdx + 2];
                        newImageData[pixelIdx + 3] = mask[y, x];
                    }
                }
            }

            if (_createNewDatasets)
            {
                CreateNewImageDataset(image, newImageData, _outputSuffix);
            }
            else
            {
                image.ImageData = newImageData;
            }
        }

        private void ExtractObjects(ImageDataset image, ImageAISegmentationPipeline pipeline)
        {
            var results = pipeline.DetectAndSegment(image, _textPrompt, _strategy);

            foreach (var result in results)
            {
                // Create separate image for each object
                byte[] objectData = new byte[image.Width * image.Height * 4];

                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        int idx = y * image.Width + x;
                        int pixelIdx = idx * 4;

                        if (result.Mask[y, x] > 0)
                        {
                            objectData[pixelIdx] = image.ImageData[pixelIdx];
                            objectData[pixelIdx + 1] = image.ImageData[pixelIdx + 1];
                            objectData[pixelIdx + 2] = image.ImageData[pixelIdx + 2];
                            objectData[pixelIdx + 3] = result.Mask[y, x];
                        }
                    }
                }

                CreateNewImageDataset(image, objectData, $"_obj{result.ClassId}");
            }
        }

        private void ApplySegmentation(ImageDataset image, ImageAISegmentationPipeline pipeline)
        {
            var results = pipeline.DetectAndSegment(image, _textPrompt, _strategy);

            foreach (var result in results)
            {
                pipeline.ApplyToDataset(image, result, 0);
            }
        }

        private void CreateAlphaMasks(ImageDataset image, ImageAISegmentationPipeline pipeline)
        {
            var results = pipeline.DetectAndSegment(image, _textPrompt, _strategy);

            if (results.Count == 0) return;

            var mask = results[0].Mask;
            byte[] maskData = new byte[image.Width * image.Height * 4];

            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    int idx = y * image.Width + x;
                    int pixelIdx = idx * 4;

                    byte alpha = mask[y, x];
                    maskData[pixelIdx] = alpha;
                    maskData[pixelIdx + 1] = alpha;
                    maskData[pixelIdx + 2] = alpha;
                    maskData[pixelIdx + 3] = 255;
                }
            }

            CreateNewImageDataset(image, maskData, "_mask");
        }

        private void CreateNewImageDataset(ImageDataset source, byte[] imageData, string suffix)
        {
            var newDataset = new ImageDataset(source.Name + suffix, "");
            newDataset.Width = source.Width;
            newDataset.Height = source.Height;
            newDataset.BitDepth = 32;
            newDataset.ImageData = imageData;
            newDataset.PixelSize = source.PixelSize;
            newDataset.Unit = source.Unit;
            newDataset.Tags = source.Tags;

            foreach (var kvp in source.ImageMetadata)
            {
                newDataset.ImageMetadata[kvp.Key] = kvp.Value;
            }

            ProjectManager.Instance.AddDataset(newDataset);
        }

        private string GetOperationDescription(BatchOperation op)
        {
            return op switch
            {
                BatchOperation.RemoveBackground => "Remove Background",
                BatchOperation.ExtractObjects => "Extract Objects (multi)",
                BatchOperation.ApplySegmentation => "Apply Segmentation Labels",
                BatchOperation.CreateAlphaMasks => "Create Alpha Masks",
                _ => op.ToString()
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
            // No resources to dispose
        }
    }
}
