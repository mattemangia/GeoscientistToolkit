using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace GeoscientistToolkit.Tools.CtImageStack.AISegmentation
{
    /// <summary>
    /// MicroSAM segmentation implementation optimized for microscopy images
    /// Supports both point-prompted and zero-shot segmentation modes
    /// </summary>
    public class MicroSamSegmenter : IDisposable
    {
        private InferenceSession _encoderSession;
        private InferenceSession _decoderSession;
        private readonly AISegmentationSettings _settings;
        private bool _disposed;

        // Cached embeddings
        private DenseTensor<float> _cachedImageEmbeddings;
        private SKBitmap _cachedSourceImage;

        public MicroSamSegmenter()
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
                    // Try DirectML first (Windows), then CUDA
                    try
                    {
                        options.AppendExecutionProvider_DML();
                    }
                    catch
                    {
                        options.AppendExecutionProvider_CUDA();
                    }
                }
                catch
                {
                    Console.WriteLine("GPU not available, falling back to CPU for MicroSAM");
                }
            }

            if (!_settings.ValidateMicroSamModels())
            {
                throw new FileNotFoundException("MicroSAM model files not found. Please configure model paths in settings.");
            }

            _encoderSession = new InferenceSession(_settings.MicroSamEncoderPath, options);
            _decoderSession = new InferenceSession(_settings.MicroSamDecoderPath, options);
        }

        /// <summary>
        /// Segment with point prompts
        /// </summary>
        public byte[,] Segment(SKBitmap image, List<(float x, float y)> points, List<float> labels)
        {
            if (points == null || points.Count == 0)
                throw new ArgumentException("At least one point is required for segmentation");

            if (points.Count != labels.Count)
                throw new ArgumentException("Number of points must match number of labels");

            int originalWidth = image.Width;
            int originalHeight = image.Height;

            // Encode image if not cached
            if (_cachedImageEmbeddings == null || !ImageEquals(image, _cachedSourceImage))
            {
                EncodeImage(image);
            }

            return RunDecoder(points, labels, originalWidth, originalHeight);
        }

        /// <summary>
        /// Zero-shot segmentation mode - automatically generates candidate masks
        /// Returns up to 5 high-quality masks filtered by IoU threshold
        /// </summary>
        public List<byte[,]> SegmentZeroShot(SKBitmap image)
        {
            int originalWidth = image.Width;
            int originalHeight = image.Height;

            // Encode image if not cached
            if (_cachedImageEmbeddings == null || !ImageEquals(image, _cachedSourceImage))
            {
                EncodeImage(image);
            }

            // Use placeholder point at center with label -1 for zero-shot mode
            var points = new List<(float x, float y)> { (512f, 512f) };
            var labels = new List<float> { -1.0f }; // -1 indicates zero-shot mode

            var masks = RunDecoderMultipleMasks(points, labels, originalWidth, originalHeight);

            // Filter masks by IoU threshold
            return masks.Where(m => m.iou >= _settings.MicroSamZeroShotIouThreshold)
                       .OrderByDescending(m => m.iou)
                       .Take(5)
                       .Select(m => m.mask)
                       .ToList();
        }

        private void EncodeImage(SKBitmap sourceImage)
        {
            // Resize to 1024x1024
            using var resized = sourceImage.Resize(new SKSizeI(1024, 1024), SKFilterQuality.High);

            // Create normalized tensor [1, 3, 1024, 1024]
            var imageTensor = new DenseTensor<float>(new[] { 1, 3, 1024, 1024 });

            for (int y = 0; y < 1024; y++)
            {
                for (int x = 0; x < 1024; x++)
                {
                    var pixel = resized.GetPixel(x, y);
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
                _cachedImageEmbeddings = results.First(r => r.Name == "image_embeddings")
                                               .AsTensor<float>().Clone() as DenseTensor<float>;

                _cachedSourceImage?.Dispose();
                _cachedSourceImage = sourceImage.Copy();
            }
        }

        private byte[,] RunDecoder(List<(float x, float y)> points, List<float> labels,
                                   int originalWidth, int originalHeight)
        {
            var result = RunDecoderMultipleMasks(points, labels, originalWidth, originalHeight);

            // Return best mask
            return result.OrderByDescending(m => m.iou).First().mask;
        }

        private List<(byte[,] mask, float iou)> RunDecoderMultipleMasks(
            List<(float x, float y)> points, List<float> labels,
            int originalWidth, int originalHeight)
        {
            var pointCoords = CreatePointCoordsTensor(points, originalWidth, originalHeight);
            var pointLabels = CreatePointLabelsTensor(labels);
            var maskInput = new DenseTensor<float>(new[] { 1, 1, 256, 256 });
            var hasMaskInput = new DenseTensor<float>(new[] { 1 });
            hasMaskInput[0] = 0.0f;
            var origImSize = new DenseTensor<float>(new[] { 2 });
            origImSize[0] = originalHeight;
            origImSize[1] = originalWidth;

            var decoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image_embeddings", _cachedImageEmbeddings),
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

                var maskList = new List<(byte[,] mask, float iou)>();

                // Extract all masks with their IoU scores
                int numMasks = (int)masks.Dimensions[1];
                for (int i = 0; i < numMasks; i++)
                {
                    float iou = iouPredictions[0, i];
                    var mask = ExtractMask(masks, i, originalWidth, originalHeight);
                    maskList.Add((mask, iou));
                }

                return maskList;
            }
        }

        private DenseTensor<float> CreatePointCoordsTensor(List<(float x, float y)> points,
                                                           int width, int height)
        {
            var tensor = new DenseTensor<float>(new[] { 1, points.Count, 2 });

            for (int i = 0; i < points.Count; i++)
            {
                // Scale to 1024x1024 space
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

        private byte[,] ExtractMask(Tensor<float> masks, int maskIndex, int targetWidth, int targetHeight)
        {
            int maskHeight = (int)masks.Dimensions[2];
            int maskWidth = (int)masks.Dimensions[3];

            var tempMask = new byte[maskHeight, maskWidth];

            for (int y = 0; y < maskHeight; y++)
            {
                for (int x = 0; x < maskWidth; x++)
                {
                    tempMask[y, x] = masks[0, maskIndex, y, x] > 0.0f ? (byte)255 : (byte)0;
                }
            }

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
            _cachedImageEmbeddings = null;
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
