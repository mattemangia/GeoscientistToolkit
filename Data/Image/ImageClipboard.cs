// GeoscientistToolkit/Data/Image/ImageClipboard.cs

using System;
using GeoscientistToolkit.Data.Image.Selection;

namespace GeoscientistToolkit.Data.Image
{
    /// <summary>
    /// Global clipboard for image data
    /// Supports copying, cutting, and pasting image regions between datasets
    /// </summary>
    public static class ImageClipboard
    {
        private static byte[] _clipboardData;
        private static int _clipboardWidth;
        private static int _clipboardHeight;
        private static bool _hasData;

        public static bool HasData => _hasData;
        public static int Width => _clipboardWidth;
        public static int Height => _clipboardHeight;

        /// <summary>
        /// Copy entire image to clipboard
        /// </summary>
        public static void Copy(ImageDataset dataset)
        {
            if (dataset?.ImageData == null) return;

            _clipboardWidth = dataset.Width;
            _clipboardHeight = dataset.Height;
            _clipboardData = new byte[dataset.ImageData.Length];
            Array.Copy(dataset.ImageData, _clipboardData, dataset.ImageData.Length);
            _hasData = true;
        }

        /// <summary>
        /// Copy selected region to clipboard
        /// </summary>
        public static void Copy(ImageDataset dataset, ImageSelection selection)
        {
            if (dataset?.ImageData == null || selection == null || !selection.HasSelection)
            {
                Copy(dataset);
                return;
            }

            int width = selection.MaxX - selection.MinX + 1;
            int height = selection.MaxY - selection.MinY + 1;

            _clipboardWidth = width;
            _clipboardHeight = height;
            _clipboardData = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int srcX = selection.MinX + x;
                    int srcY = selection.MinY + y;
                    int srcIdx = (srcY * dataset.Width + srcX) * 4;
                    int dstIdx = (y * width + x) * 4;

                    byte maskValue = selection.Mask[srcY * dataset.Width + srcX];

                    if (maskValue > 0)
                    {
                        _clipboardData[dstIdx] = dataset.ImageData[srcIdx];
                        _clipboardData[dstIdx + 1] = dataset.ImageData[srcIdx + 1];
                        _clipboardData[dstIdx + 2] = dataset.ImageData[srcIdx + 2];
                        _clipboardData[dstIdx + 3] = (byte)((dataset.ImageData[srcIdx + 3] * maskValue) / 255);
                    }
                }
            }

            _hasData = true;
        }

        /// <summary>
        /// Copy from layer (string-based overload)
        /// </summary>
        public static void CopyLayer(string name, byte[] data, int width, int height)
        {
            if (data == null) return;

            _clipboardWidth = width;
            _clipboardHeight = height;
            _clipboardData = new byte[data.Length];
            Array.Copy(data, _clipboardData, data.Length);
            _hasData = true;
        }

        /// <summary>
        /// Copy from layer (string-based overload with selection)
        /// </summary>
        public static void CopyLayer(string name, byte[] data, int width, int height, ImageSelection selection)
        {
            if (data == null || selection == null || !selection.HasSelection)
            {
                CopyLayer(name, data, width, height);
                return;
            }

            int selWidth = selection.MaxX - selection.MinX + 1;
            int selHeight = selection.MaxY - selection.MinY + 1;

            _clipboardWidth = selWidth;
            _clipboardHeight = selHeight;
            _clipboardData = new byte[selWidth * selHeight * 4];

            for (int y = 0; y < selHeight; y++)
            {
                for (int x = 0; x < selWidth; x++)
                {
                    int srcX = selection.MinX + x;
                    int srcY = selection.MinY + y;
                    int srcIdx = (srcY * width + srcX) * 4;
                    int dstIdx = (y * selWidth + x) * 4;

                    byte maskValue = selection.Mask[srcY * width + srcX];

                    if (maskValue > 0)
                    {
                        _clipboardData[dstIdx] = data[srcIdx];
                        _clipboardData[dstIdx + 1] = data[srcIdx + 1];
                        _clipboardData[dstIdx + 2] = data[srcIdx + 2];
                        _clipboardData[dstIdx + 3] = (byte)((data[srcIdx + 3] * maskValue) / 255);
                    }
                }
            }

            _hasData = true;
        }

        /// <summary>
        /// Copy from layer
        /// </summary>
        public static void CopyLayer(ImageLayer layer, int width, int height)
        {
            if (layer?.Data == null) return;
            CopyLayer(layer.Name, layer.Data, width, height);
        }

        /// <summary>
        /// Copy from layer with selection
        /// </summary>
        public static void CopyLayer(ImageLayer layer, int width, int height, ImageSelection selection)
        {
            if (layer?.Data == null) return;
            CopyLayer(layer.Name, layer.Data, width, height, selection);
        }

        /// <summary>
        /// Cut entire image to clipboard (clears source)
        /// </summary>
        public static void Cut(ImageDataset dataset)
        {
            Copy(dataset);
            Array.Clear(dataset.ImageData, 0, dataset.ImageData.Length);
        }

        /// <summary>
        /// Cut selected region to clipboard (clears source)
        /// </summary>
        public static void Cut(ImageDataset dataset, ImageSelection selection)
        {
            if (dataset?.ImageData == null) return;

            Copy(dataset, selection);

            if (selection != null && selection.HasSelection)
            {
                // Clear selected pixels
                for (int y = selection.MinY; y <= selection.MaxY; y++)
                {
                    for (int x = selection.MinX; x <= selection.MaxX; x++)
                    {
                        if (x >= 0 && x < dataset.Width && y >= 0 && y < dataset.Height)
                        {
                            byte maskValue = selection.Mask[y * dataset.Width + x];
                            if (maskValue > 0)
                            {
                                int idx = (y * dataset.Width + x) * 4;
                                dataset.ImageData[idx] = 0;
                                dataset.ImageData[idx + 1] = 0;
                                dataset.ImageData[idx + 2] = 0;
                                dataset.ImageData[idx + 3] = 0;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Cut from layer (string-based overload)
        /// </summary>
        public static void CutLayer(string name, byte[] data, int width, int height)
        {
            CopyLayer(name, data, width, height);
            Array.Clear(data, 0, data.Length);
        }

        /// <summary>
        /// Cut from layer with selection (string-based overload)
        /// </summary>
        public static void CutLayer(string name, byte[] data, int width, int height, ImageSelection selection)
        {
            if (data == null) return;

            CopyLayer(name, data, width, height, selection);

            if (selection != null && selection.HasSelection)
            {
                // Clear selected pixels
                for (int y = selection.MinY; y <= selection.MaxY; y++)
                {
                    for (int x = selection.MinX; x <= selection.MaxX; x++)
                    {
                        if (x >= 0 && x < width && y >= 0 && y < height)
                        {
                            byte maskValue = selection.Mask[y * width + x];
                            if (maskValue > 0)
                            {
                                int idx = (y * width + x) * 4;
                                data[idx] = 0;
                                data[idx + 1] = 0;
                                data[idx + 2] = 0;
                                data[idx + 3] = 0;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Cut from layer
        /// </summary>
        public static void CutLayer(ImageLayer layer, int width, int height)
        {
            if (layer?.Data == null) return;
            CutLayer(layer.Name, layer.Data, width, height);
        }

        /// <summary>
        /// Cut from layer with selection
        /// </summary>
        public static void CutLayer(ImageLayer layer, int width, int height, ImageSelection selection)
        {
            if (layer?.Data == null) return;
            CutLayer(layer.Name, layer.Data, width, height, selection);
        }

        /// <summary>
        /// Paste clipboard data to image at specified position
        /// </summary>
        public static void Paste(ImageDataset dataset, int offsetX = 0, int offsetY = 0, float opacity = 1.0f)
        {
            if (!_hasData || dataset?.ImageData == null) return;

            for (int y = 0; y < _clipboardHeight; y++)
            {
                for (int x = 0; x < _clipboardWidth; x++)
                {
                    int dstX = x + offsetX;
                    int dstY = y + offsetY;

                    if (dstX >= 0 && dstX < dataset.Width && dstY >= 0 && dstY < dataset.Height)
                    {
                        int srcIdx = (y * _clipboardWidth + x) * 4;
                        int dstIdx = (dstY * dataset.Width + dstX) * 4;

                        byte srcAlpha = (byte)((_clipboardData[srcIdx + 3] * opacity));
                        if (srcAlpha == 0) continue;

                        byte dstAlpha = dataset.ImageData[dstIdx + 3];

                        if (srcAlpha == 255)
                        {
                            // Opaque paste - replace
                            dataset.ImageData[dstIdx] = _clipboardData[srcIdx];
                            dataset.ImageData[dstIdx + 1] = _clipboardData[srcIdx + 1];
                            dataset.ImageData[dstIdx + 2] = _clipboardData[srcIdx + 2];
                            dataset.ImageData[dstIdx + 3] = srcAlpha;
                        }
                        else
                        {
                            // Alpha blending
                            float srcA = srcAlpha / 255.0f;
                            float dstA = dstAlpha / 255.0f;
                            float outA = srcA + dstA * (1 - srcA);

                            if (outA > 0)
                            {
                                for (int c = 0; c < 3; c++)
                                {
                                    float srcC = _clipboardData[srcIdx + c] / 255.0f;
                                    float dstC = dataset.ImageData[dstIdx + c] / 255.0f;
                                    float outC = (srcC * srcA + dstC * dstA * (1 - srcA)) / outA;
                                    dataset.ImageData[dstIdx + c] = (byte)(outC * 255);
                                }

                                dataset.ImageData[dstIdx + 3] = (byte)(outA * 255);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Paste clipboard data to layer at specified position (string-based overload)
        /// </summary>
        public static void PasteToLayer(string name, byte[] data, int width, int height,
            int offsetX = 0, int offsetY = 0, float opacity = 1.0f)
        {
            if (!_hasData || data == null) return;

            for (int y = 0; y < _clipboardHeight; y++)
            {
                for (int x = 0; x < _clipboardWidth; x++)
                {
                    int dstX = x + offsetX;
                    int dstY = y + offsetY;

                    if (dstX >= 0 && dstX < width && dstY >= 0 && dstY < height)
                    {
                        int srcIdx = (y * _clipboardWidth + x) * 4;
                        int dstIdx = (dstY * width + dstX) * 4;

                        byte srcAlpha = (byte)((_clipboardData[srcIdx + 3] * opacity));
                        if (srcAlpha == 0) continue;

                        byte dstAlpha = data[dstIdx + 3];

                        if (srcAlpha == 255)
                        {
                            // Opaque paste - replace
                            data[dstIdx] = _clipboardData[srcIdx];
                            data[dstIdx + 1] = _clipboardData[srcIdx + 1];
                            data[dstIdx + 2] = _clipboardData[srcIdx + 2];
                            data[dstIdx + 3] = srcAlpha;
                        }
                        else
                        {
                            // Alpha blending
                            float srcA = srcAlpha / 255.0f;
                            float dstA = dstAlpha / 255.0f;
                            float outA = srcA + dstA * (1 - srcA);

                            if (outA > 0)
                            {
                                for (int c = 0; c < 3; c++)
                                {
                                    float srcC = _clipboardData[srcIdx + c] / 255.0f;
                                    float dstC = data[dstIdx + c] / 255.0f;
                                    float outC = (srcC * srcA + dstC * dstA * (1 - srcA)) / outA;
                                    data[dstIdx + c] = (byte)(outC * 255);
                                }

                                data[dstIdx + 3] = (byte)(outA * 255);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Paste clipboard data to layer at specified position
        /// </summary>
        public static void PasteToLayer(ImageLayer layer, int width, int height,
            int offsetX = 0, int offsetY = 0, float opacity = 1.0f)
        {
            if (!_hasData || layer?.Data == null) return;
            PasteToLayer(layer.Name, layer.Data, width, height, offsetX, offsetY, opacity);
        }

        /// <summary>
        /// Create new layer from clipboard data
        /// </summary>
        public static ImageLayer PasteAsNewLayer(string name = "Pasted Layer")
        {
            if (!_hasData) return null;

            var layer = new ImageLayer(name, _clipboardWidth, _clipboardHeight);
            Array.Copy(_clipboardData, layer.Data, _clipboardData.Length);
            return layer;
        }

        /// <summary>
        /// Create new ImageDataset from clipboard data
        /// </summary>
        public static ImageDataset PasteAsNewImage(string name = "Pasted Image")
        {
            if (!_hasData) return null;

            var dataset = new ImageDataset(name, "");
            dataset.Width = _clipboardWidth;
            dataset.Height = _clipboardHeight;
            dataset.BitDepth = 32;
            dataset.ImageData = new byte[_clipboardData.Length];
            Array.Copy(_clipboardData, dataset.ImageData, _clipboardData.Length);

            return dataset;
        }

        /// <summary>
        /// Clear clipboard
        /// </summary>
        public static void Clear()
        {
            _clipboardData = null;
            _clipboardWidth = 0;
            _clipboardHeight = 0;
            _hasData = false;
        }
    }
}
