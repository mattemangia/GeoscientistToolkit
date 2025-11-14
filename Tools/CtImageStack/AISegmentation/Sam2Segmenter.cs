using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace GeoscientistToolkit.Tools.CtImageStack.AISegmentation
{
    /// <summary>
    /// SAM2 (Segment Anything Model 2) segmentation implementation using ONNX Runtime
    /// Based on SAM 2.1 large architecture with encoder-decoder pipeline
    /// </summary>
    public class Sam2Segmenter : IDisposable
    {
        private InferenceSession _encoderSession;
        private InferenceSession _decoderSession;
        private readonly AISegmentationSettings _settings;
        private bool _disposed;

        // Cached embeddings to avoid re-encoding same image
        private DenseTensor<float> _cachedImageEmbed;
        private DenseTensor<float> _cachedHighResFeats0;
        private DenseTensor<float> _cachedHighResFeats1;
        private SKBitmap _cachedSourceImage;

        public Sam2Segmenter()
        {
            _settings = AISegmentationSettings.Instance;
            InitializeSessions();
        }

        private void InitializeSessions()
        {
            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.EnableCpuMemArena = true;
            options.IntraOpNumThreads = Math.Max(1, _settings.CpuThreads / 2);

            // Try GPU first if enabled
            if (_settings.UseGpu)
            {
                try
                {
                    options.AppendExecutionProvider_CUDA();
                }
                catch
                {
                    Console.WriteLine("CUDA not available, falling back to CPU for SAM2");
                }
            }

            if (!_settings.ValidateSam2Models())
            {
                throw new FileNotFoundException("SAM2 model files not found. Please configure model paths in settings.");
            }

            _encoderSession = new InferenceSession(_settings.Sam2EncoderPath, options);
            _decoderSession = new InferenceSession(_settings.Sam2DecoderPath, options);
        }

        /// <summary>
        /// Segment an image using point prompts
        /// </summary>
        /// <param name="image">Source image (will be resized to 1024x1024)</param>
        /// <param name="points">Prompt points (x, y coordinates)</param>
        /// <param name="labels">Point labels (1.0 = positive, 0.0 = negative)</param>
        /// <returns>Segmentation mask as byte array (0 or 255)</returns>
        public byte[,] Segment(SKBitmap image, List<(float x, float y)> points, List<float> labels)
        {
            if (points == null || points.Count == 0)
                throw new ArgumentException("At least one point is required for segmentation");

            if (points.Count != labels.Count)
                throw new ArgumentException("Number of points must match number of labels");

            int originalWidth = image.Width;
            int originalHeight = image.Height;

            // Encode image if not cached or if image changed
            if (_cachedImageEmbed == null || !ImageEquals(image, _cachedSourceImage))
            {
                EncodeImage(image);
            }

            // Prepare decoder inputs
            var pointCoords = CreatePointCoordsTensor(points, originalWidth, originalHeight);
            var pointLabels = CreatePointLabelsTensor(labels);
            var maskInput = new DenseTensor<float>(new[] { 1, 1, 256, 256 });
            var hasMaskInput = new DenseTensor<float>(new[] { 1 });
            hasMaskInput[0] = 0.0f;
            var origImSize = new DenseTensor<float>(new[] { 2 });
            origImSize[0] = originalHeight;
            origImSize[1] = originalWidth;

            // Run decoder
            var decoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image_embed", _cachedImageEmbed),
                NamedOnnxValue.CreateFromTensor("high_res_feats_0", _cachedHighResFeats0),
                NamedOnnxValue.CreateFromTensor("high_res_feats_1", _cachedHighResFeats1),
                NamedOnnxValue.CreateFromTensor("point_coords", pointCoords),
                NamedOnnxValue.CreateFromTensor("point_labels", pointLabels),
                NamedOnnxValue.CreateFromTensor("mask_input", maskInput),
                NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskInput),
                NamedOnnxValue.CreateFromTensor("orig_im_size", origImSize)
            };

            using (var results = _decoderSession.Run(decoderInputs))
            {
                var masks = results.First(r => r.Name == "masks").AsTensor<float>();
                var iouPredictions = results.First(r => r.Name == "iou_predictions").AsTensor<float>();

                // Select best mask based on IoU prediction
                int bestMaskIdx = GetBestMaskIndex(iouPredictions);
                return ExtractMask(masks, bestMaskIdx, originalWidth, originalHeight);
            }
        }

        /// <summary>
        /// Encode image and cache embeddings for faster repeated segmentations
        /// </summary>
        private void EncodeImage(SKBitmap sourceImage)
        {
            // Resize to 1024x1024 with high quality
            using var resized = sourceImage.Resize(new SKSizeI(1024, 1024), SKFilterQuality.High);

            // Create normalized tensor [1, 3, 1024, 1024]
            var imageTensor = new DenseTensor<float>(new[] { 1, 3, 1024, 1024 });

            for (int y = 0; y < 1024; y++)
            {
                for (int x = 0; x < 1024; x++)
                {
                    var pixel = resized.GetPixel(x, y);
                    // Normalize to [0, 1] range
                    imageTensor[0, 0, y, x] = pixel.Red / 255.0f;
                    imageTensor[0, 1, y, x] = pixel.Green / 255.0f;
                    imageTensor[0, 2, y, x] = pixel.Blue / 255.0f;
                }
            }

            // Run encoder
            var encoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image", imageTensor)
            };

            using (var results = _encoderSession.Run(encoderInputs))
            {
                // Cache embeddings
                _cachedImageEmbed = results.First(r => r.Name == "image_embed").AsTensor<float>().Clone() as DenseTensor<float>;
                _cachedHighResFeats0 = results.First(r => r.Name == "high_res_feats_0").AsTensor<float>().Clone() as DenseTensor<float>;
                _cachedHighResFeats1 = results.First(r => r.Name == "high_res_feats_1").AsTensor<float>().Clone() as DenseTensor<float>;

                // Cache source image for comparison
                _cachedSourceImage?.Dispose();
                _cachedSourceImage = sourceImage.Copy();
            }
        }

        private DenseTensor<float> CreatePointCoordsTensor(List<(float x, float y)> points, int width, int height)
        {
            var tensor = new DenseTensor<float>(new[] { 1, points.Count, 2 });

            for (int i = 0; i < points.Count; i++)
            {
                // Scale points to 1024x1024 space
                tensor[0, i, 0] = points[i].x * 1024.0f / width;
                tensor[0, i, 1] = points[i].y * 1024.0f / height;
            }

            return tensor;
        }

        private DenseTensor<float> CreatePointLabelsTensor(List<float> labels)
        {
            var tensor = new DenseTensor<float>(new[] { 1, labels.Count });

            for (int i = 0; i < labels.Count; i++)
            {
                tensor[0, i] = labels[i];
            }

            return tensor;
        }

        private int GetBestMaskIndex(Tensor<float> iouPredictions)
        {
            int bestIdx = 0;
            float bestIou = float.MinValue;

            for (int i = 0; i < iouPredictions.Dimensions[1]; i++)
            {
                float iou = iouPredictions[0, i];
                if (iou > bestIou)
                {
                    bestIou = iou;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        private byte[,] ExtractMask(Tensor<float> masks, int maskIndex, int targetWidth, int targetHeight)
        {
            int maskHeight = (int)masks.Dimensions[2];
            int maskWidth = (int)masks.Dimensions[3];

            // Create temporary mask at original resolution
            var tempMask = new byte[maskHeight, maskWidth];

            for (int y = 0; y < maskHeight; y++)
            {
                for (int x = 0; x < maskWidth; x++)
                {
                    // Threshold at 0.0
                    tempMask[y, x] = masks[0, maskIndex, y, x] > 0.0f ? (byte)255 : (byte)0;
                }
            }

            // Resize to target dimensions if needed
            if (maskHeight != targetHeight || maskWidth != targetWidth)
            {
                return ResizeMask(tempMask, targetWidth, targetHeight);
            }

            return tempMask;
        }

        private byte[,] ResizeMask(byte[,] mask, int newWidth, int newHeight)
        {
            int oldHeight = mask.GetLength(0);
            int oldWidth = mask.GetLength(1);
            var resized = new byte[newHeight, newWidth];

            float xRatio = (float)oldWidth / newWidth;
            float yRatio = (float)oldHeight / newHeight;

            for (int y = 0; y < newHeight; y++)
            {
                for (int x = 0; x < newWidth; x++)
                {
                    int srcX = (int)(x * xRatio);
                    int srcY = (int)(y * yRatio);
                    resized[y, x] = mask[srcY, srcX];
                }
            }

            return resized;
        }

        private bool ImageEquals(SKBitmap img1, SKBitmap img2)
        {
            if (img1 == null || img2 == null)
                return false;

            if (img1.Width != img2.Width || img1.Height != img2.Height)
                return false;

            // Quick sample-based comparison (checking corners and center)
            var checkPoints = new[] { (0, 0), (img1.Width - 1, 0), (0, img1.Height - 1),
                                     (img1.Width - 1, img1.Height - 1), (img1.Width / 2, img1.Height / 2) };

            foreach (var (x, y) in checkPoints)
            {
                if (img1.GetPixel(x, y) != img2.GetPixel(x, y))
                    return false;
            }

            return true;
        }

        public void ClearCache()
        {
            _cachedImageEmbed = null;
            _cachedHighResFeats0 = null;
            _cachedHighResFeats1 = null;
            _cachedSourceImage?.Dispose();
            _cachedSourceImage = null;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _encoderSession?.Dispose();
                _decoderSession?.Dispose();
                _cachedSourceImage?.Dispose();
                _disposed = true;
            }
        }
    }
}
