// GeoscientistToolkit/Data/Image/AISegmentation/ImageSmartCutoutTool.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Tools.CtImageStack.AISegmentation;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Image.AISegmentation
{
    /// <summary>
    /// Quick SAM-powered cutout tool
    /// One-click object isolation with instant preview
    /// </summary>
    public class ImageSmartCutoutTool : IDatasetTools, IDisposable
    {
        private ImageAISegmentationPipeline _pipeline;
        private readonly AISegmentationSettings _settings;

        // UI state
        private ImageAISegmentationPipeline.SegmenterType _segmenterType = ImageAISegmentationPipeline.SegmenterType.SAM2;
        private CutoutMode _cutoutMode = CutoutMode.InstantCutout;

        // Click points
        private List<(float x, float y)> _clickPoints = new();
        private List<float> _clickLabels = new();
        private bool _isRefining = false;

        // Current result
        private byte[,] _currentMask;
        private bool _hasMask;
        private bool _isProcessing;

        // Options
        private string _outputName = "cutout";
        private bool _autoIncrement = true;
        private int _cutoutCounter = 1;
        private bool _addWhiteBorder = false;
        private int _borderWidth = 2;
        private bool _shadowEffect = false;
        private bool _saveMaskToo = false;

        // Error handling
        private string _initializationError;
        private bool _showErrorModal;

        public enum CutoutMode
        {
            InstantCutout,      // Single click creates cutout
            RefinableCutout     // Click to refine before extracting
        }

        public ImageSmartCutoutTool()
        {
            _settings = AISegmentationSettings.Instance;

            try
            {
                bool hasSam2 = _settings.ValidateSam2Models();
                bool hasMicroSam = _settings.ValidateMicroSamModels();

                if (!hasSam2 && !hasMicroSam)
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

            ImGui.SeparatorText("Smart Cutout Tool");

            if (_pipeline == null)
            {
                ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1),
                    "Pipeline not available. Check model paths in AI Settings.");
                return;
            }

            ImGui.TextWrapped("One-click object cutout with SAM. Click on any object to instantly extract it.");
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
                    Reset();
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
            if (ImGui.BeginCombo("##mode", GetModeDescription(_cutoutMode)))
            {
                foreach (CutoutMode mode in Enum.GetValues<CutoutMode>())
                {
                    bool isSelected = _cutoutMode == mode;
                    if (ImGui.Selectable(GetModeDescription(mode), isSelected))
                        _cutoutMode = mode;

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Spacing();

            // Status
            if (_isRefining && _clickPoints.Count > 0)
            {
                ImGui.TextColored(new Vector4(0.3f, 1, 0.3f, 1), $"Points: {_clickPoints.Count}");
                ImGui.TextWrapped("Left-click: add positive point, Right-click: add negative point");
            }
            else
            {
                ImGui.TextWrapped("Click on the object you want to cut out.");
            }

            ImGui.Separator();

            // Output options
            ImGui.Text("Output name:");
            ImGui.InputText("##name", ref _outputName, 256);
            ImGui.SameLine();
            ImGui.Checkbox("Auto++", ref _autoIncrement);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Automatically increment name for multiple cutouts");

            ImGui.Spacing();

            // Visual options
            ImGui.Checkbox("Add white border", ref _addWhiteBorder);
            if (_addWhiteBorder)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.SliderInt("##border", ref _borderWidth, 1, 10);
            }

            ImGui.Checkbox("Drop shadow effect", ref _shadowEffect);
            ImGui.Checkbox("Save mask as separate image", ref _saveMaskToo);

            ImGui.Separator();

            // Action buttons
            if (_isProcessing)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1, 1), "Processing...");
            }
            else
            {
                if (_hasMask && _isRefining)
                {
                    if (ImGui.Button("Accept Cutout", new Vector2(-1, 30)))
                    {
                        CreateCutout(image);
                    }

                    if (ImGui.Button("Cancel", new Vector2(-1, 0)))
                    {
                        Reset();
                    }
                }
                else if (_hasMask)
                {
                    if (ImGui.Button("Create Cutout", new Vector2(-1, 30)))
                    {
                        CreateCutout(image);
                    }

                    if (ImGui.Button("Refine Selection", new Vector2(-1, 0)))
                    {
                        _isRefining = true;
                    }

                    if (ImGui.Button("Reset", new Vector2(-1, 0)))
                    {
                        Reset();
                    }
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 0.8f, 0, 1),
                        "Click on the image to select an object");
                }
            }
        }

        public void HandleClick(ImageDataset dataset, float x, float y, bool isLeftClick)
        {
            if (_isRefining)
            {
                // Add refinement point
                _clickPoints.Add((x, y));
                _clickLabels.Add(isLeftClick ? 1.0f : 0.0f);
                ProcessCutout(dataset, false);
            }
            else
            {
                // Initial click
                Reset();
                _clickPoints.Add((x, y));
                _clickLabels.Add(1.0f);

                if (_cutoutMode == CutoutMode.InstantCutout)
                {
                    ProcessCutout(dataset, true);
                }
                else if (_cutoutMode == CutoutMode.RefinableCutout)
                {
                    _isRefining = true;
                    ProcessCutout(dataset, false);
                }
            }
        }

        private void ProcessCutout(ImageDataset dataset, bool autoCreate)
        {
            if (_clickPoints.Count == 0) return;

            _isProcessing = true;

            try
            {
                _currentMask = _pipeline.SegmentWithPoints(dataset, _clickPoints, _clickLabels);
                _hasMask = true;

                if (autoCreate && _cutoutMode == CutoutMode.InstantCutout)
                {
                    CreateCutout(dataset);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cutout failed: {ex.Message}");
                _currentMask = null;
                _hasMask = false;
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private void CreateCutout(ImageDataset sourceDataset)
        {
            if (!_hasMask || _currentMask == null) return;

            int width = sourceDataset.Width;
            int height = sourceDataset.Height;

            // Find bounding box of mask
            int minX = width, minY = height, maxX = 0, maxY = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (_currentMask[y, x] > 0)
                    {
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            // Add padding for effects
            int padding = _addWhiteBorder ? _borderWidth + 2 : 2;
            if (_shadowEffect) padding += 8;

            minX = Math.Max(0, minX - padding);
            minY = Math.Max(0, minY - padding);
            maxX = Math.Min(width - 1, maxX + padding);
            maxY = Math.Min(height - 1, maxY + padding);

            int cropWidth = maxX - minX + 1;
            int cropHeight = maxY - minY + 1;

            byte[] newImageData = new byte[cropWidth * cropHeight * 4];

            // Create cutout
            for (int y = 0; y < cropHeight; y++)
            {
                for (int x = 0; x < cropWidth; x++)
                {
                    int srcX = minX + x;
                    int srcY = minY + y;
                    int dstIdx = (y * cropWidth + x) * 4;

                    if (srcX >= 0 && srcX < width && srcY >= 0 && srcY < height)
                    {
                        int srcIdx = (srcY * width + srcX) * 4;
                        byte maskValue = _currentMask[srcY, srcX];

                        if (maskValue > 0)
                        {
                            // Copy pixel
                            newImageData[dstIdx] = sourceDataset.ImageData[srcIdx];
                            newImageData[dstIdx + 1] = sourceDataset.ImageData[srcIdx + 1];
                            newImageData[dstIdx + 2] = sourceDataset.ImageData[srcIdx + 2];
                            newImageData[dstIdx + 3] = maskValue;
                        }
                    }
                }
            }

            // Apply effects
            if (_addWhiteBorder)
                newImageData = AddWhiteBorder(newImageData, cropWidth, cropHeight);

            if (_shadowEffect)
                newImageData = AddDropShadow(newImageData, cropWidth, cropHeight);

            // Determine output name
            string outputName = _outputName;
            if (_autoIncrement)
            {
                outputName = $"{_outputName}_{_cutoutCounter:D3}";
                _cutoutCounter++;
            }

            // Create new dataset
            var newDataset = new ImageDataset(outputName, "");
            newDataset.Width = cropWidth;
            newDataset.Height = cropHeight;
            newDataset.BitDepth = 32;
            newDataset.ImageData = newImageData;
            newDataset.PixelSize = sourceDataset.PixelSize;
            newDataset.Unit = sourceDataset.Unit;
            newDataset.Tags = sourceDataset.Tags;

            newDataset.ImageMetadata["CutoutFrom"] = sourceDataset.Name;
            newDataset.ImageMetadata["CutoutBounds"] = $"{minX},{minY},{maxX},{maxY}";

            ProjectManager.Instance.AddDataset(newDataset);
            ProjectManager.Instance.HasUnsavedChanges = true;

            Console.WriteLine($"Created cutout: {outputName}");

            // Save mask if requested
            if (_saveMaskToo)
            {
                CreateMaskDataset(sourceDataset, outputName, minX, minY, cropWidth, cropHeight);
            }

            // Reset for next cutout
            if (_cutoutMode == CutoutMode.InstantCutout)
                Reset();
        }

        private void CreateMaskDataset(ImageDataset sourceDataset, string baseName, int minX, int minY, int width, int height)
        {
            byte[] maskImageData = new byte[width * height * 4];

            // Convert mask to grayscale image
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcX = minX + x;
                    int srcY = minY + y;
                    int dstIdx = (y * width + x) * 4;

                    byte maskValue = 0;
                    if (srcX >= 0 && srcX < sourceDataset.Width && srcY >= 0 && srcY < sourceDataset.Height)
                    {
                        maskValue = _currentMask[srcY, srcX];
                    }

                    // Grayscale mask
                    maskImageData[dstIdx] = maskValue;
                    maskImageData[dstIdx + 1] = maskValue;
                    maskImageData[dstIdx + 2] = maskValue;
                    maskImageData[dstIdx + 3] = 255;
                }
            }

            // Create mask dataset
            string maskName = $"{baseName}_mask";
            var maskDataset = new ImageDataset(maskName, "");
            maskDataset.Width = width;
            maskDataset.Height = height;
            maskDataset.BitDepth = 32;
            maskDataset.ImageData = maskImageData;
            maskDataset.PixelSize = sourceDataset.PixelSize;
            maskDataset.Unit = sourceDataset.Unit;
            maskDataset.Tags = sourceDataset.Tags;

            maskDataset.ImageMetadata["MaskFrom"] = sourceDataset.Name;
            maskDataset.ImageMetadata["AssociatedCutout"] = baseName;

            ProjectManager.Instance.AddDataset(maskDataset);
            Console.WriteLine($"Created mask: {maskName}");
        }

        private byte[] AddWhiteBorder(byte[] imageData, int width, int height)
        {
            // Simple white border by dilating alpha channel
            var withBorder = (byte[])imageData.Clone();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 4;

                    if (withBorder[idx + 3] > 0)
                    {
                        // Add white border around opaque pixels
                        for (int dy = -_borderWidth; dy <= _borderWidth; dy++)
                        {
                            for (int dx = -_borderWidth; dx <= _borderWidth; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;

                                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                {
                                    int nIdx = (ny * width + nx) * 4;

                                    if (withBorder[nIdx + 3] == 0)
                                    {
                                        withBorder[nIdx] = 255;
                                        withBorder[nIdx + 1] = 255;
                                        withBorder[nIdx + 2] = 255;
                                        withBorder[nIdx + 3] = 200;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return withBorder;
        }

        private byte[] AddDropShadow(byte[] imageData, int width, int height)
        {
            // Create shadow layer
            int shadowOffset = 4;
            int shadowBlur = 6;
            byte shadowAlpha = 128;

            // Extract alpha channel to create shadow mask
            byte[] shadowMask = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 4;
                    shadowMask[y * width + x] = imageData[idx + 3];
                }
            }

            // Blur the shadow mask using box blur (approximation of gaussian)
            shadowMask = BoxBlur(shadowMask, width, height, shadowBlur);

            // Create new image with shadow
            byte[] withShadow = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = (y * width + x) * 4;

                    // Check if there's a shadow at this offset position
                    int shadowX = x - shadowOffset;
                    int shadowY = y - shadowOffset;

                    if (shadowX >= 0 && shadowX < width && shadowY >= 0 && shadowY < height)
                    {
                        int shadowIdx = shadowY * width + shadowX;
                        byte shadowValue = shadowMask[shadowIdx];

                        if (shadowValue > 0 && imageData[idx + 3] == 0)
                        {
                            // Draw shadow (dark gray with reduced alpha)
                            withShadow[idx] = 0;
                            withShadow[idx + 1] = 0;
                            withShadow[idx + 2] = 0;
                            withShadow[idx + 3] = (byte)((shadowValue * shadowAlpha) / 255);
                        }
                        else
                        {
                            // Copy original pixel
                            withShadow[idx] = imageData[idx];
                            withShadow[idx + 1] = imageData[idx + 1];
                            withShadow[idx + 2] = imageData[idx + 2];
                            withShadow[idx + 3] = imageData[idx + 3];
                        }
                    }
                    else
                    {
                        // Copy original pixel
                        withShadow[idx] = imageData[idx];
                        withShadow[idx + 1] = imageData[idx + 1];
                        withShadow[idx + 2] = imageData[idx + 2];
                        withShadow[idx + 3] = imageData[idx + 3];
                    }
                }
            }

            return withShadow;
        }

        private byte[] BoxBlur(byte[] data, int width, int height, int radius)
        {
            if (radius <= 0) return data;

            byte[] blurred = new byte[width * height];

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
                            int nx = x + dx;
                            int ny = y + dy;

                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                sum += data[ny * width + nx];
                                count++;
                            }
                        }
                    }

                    blurred[y * width + x] = (byte)(sum / count);
                }
            }

            return blurred;
        }

        private void Reset()
        {
            _clickPoints.Clear();
            _clickLabels.Clear();
            _currentMask = null;
            _hasMask = false;
            _isRefining = false;
        }

        private string GetModeDescription(CutoutMode mode)
        {
            return mode switch
            {
                CutoutMode.InstantCutout => "Instant (one-click cutout)",
                CutoutMode.RefinableCutout => "Refinable (multi-point)",
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

        public bool IsInteractive => true;

        public void Dispose()
        {
            _pipeline?.Dispose();
        }
    }
}
