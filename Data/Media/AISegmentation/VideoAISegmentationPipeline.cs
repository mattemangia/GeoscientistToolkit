// GeoscientistToolkit/Data/Media/AISegmentation/VideoAISegmentationPipeline.cs

using System;
using System.Collections.Generic;
using System.Numerics;
using SkiaSharp;
using GeoscientistToolkit.Tools.CtImageStack.AISegmentation;

namespace GeoscientistToolkit.Data.Media.AISegmentation
{
    /// <summary>
    /// AI segmentation pipeline for VideoDataset
    /// Integrates SAM2 for interactive video segmentation with frame-by-frame tracking
    /// </summary>
    public class VideoAISegmentationPipeline : IDisposable
    {
        private Sam2Segmenter _sam2;
        private bool _disposed;

        // Frame cache for the current segmentation session
        private Dictionary<int, byte[,]> _frameMasks;
        private int _lastSegmentedFrame = -1;

        public VideoAISegmentationPipeline()
        {
            _sam2 = new Sam2Segmenter();
            _frameMasks = new Dictionary<int, byte[,]>();
        }

        /// <summary>
        /// Segment a specific frame with point prompts
        /// </summary>
        /// <param name="dataset">Source video dataset</param>
        /// <param name="frameNumber">Frame to segment (0-based)</param>
        /// <param name="points">Prompt points (x, y coordinates)</param>
        /// <param name="labels">Point labels (1.0 = positive, 0.0 = negative)</param>
        /// <returns>Segmentation mask as 2D array</returns>
        public async Task<byte[,]> SegmentFrameAsync(
            VideoDataset dataset,
            int frameNumber,
            List<(float x, float y)> points,
            List<float> labels)
        {
            if (dataset == null)
                throw new ArgumentNullException(nameof(dataset));

            if (frameNumber < 0 || frameNumber >= dataset.TotalFrames)
                throw new ArgumentOutOfRangeException(nameof(frameNumber));

            // Extract frame from video
            var timeSeconds = frameNumber / dataset.FrameRate;
            var frameData = await dataset.ExtractFrameAsync(timeSeconds, dataset.Width, dataset.Height);

            if (frameData == null || frameData.Length == 0)
                throw new InvalidOperationException($"Failed to extract frame {frameNumber}");

            // Convert frame to SKBitmap
            using var bitmap = ConvertFrameToBitmap(frameData, dataset.Width, dataset.Height);

            // Perform segmentation
            var mask = _sam2.Segment(bitmap, points, labels);

            // Cache the mask for this frame
            _frameMasks[frameNumber] = mask;
            _lastSegmentedFrame = frameNumber;

            return mask;
        }

        /// <summary>
        /// Get segmentation mask for a previously segmented frame
        /// </summary>
        /// <param name="frameNumber">Frame number</param>
        /// <returns>Cached mask or null if not segmented</returns>
        public byte[,] GetFrameMask(int frameNumber)
        {
            return _frameMasks.TryGetValue(frameNumber, out var mask) ? mask : null;
        }

        /// <summary>
        /// Check if a frame has been segmented
        /// </summary>
        public bool HasFrameMask(int frameNumber)
        {
            return _frameMasks.ContainsKey(frameNumber);
        }

        /// <summary>
        /// Get all segmented frame numbers
        /// </summary>
        public IEnumerable<int> GetSegmentedFrames()
        {
            return _frameMasks.Keys;
        }

        /// <summary>
        /// Clear all cached masks
        /// </summary>
        public void ClearMasks()
        {
            _frameMasks.Clear();
            _lastSegmentedFrame = -1;
            _sam2?.ClearCache();
        }

        /// <summary>
        /// Export segmented mask for a specific frame
        /// </summary>
        /// <param name="frameNumber">Frame to export</param>
        /// <param name="width">Video width</param>
        /// <param name="height">Video height</param>
        /// <returns>Mask as grayscale byte array (0=background, 255=foreground)</returns>
        public byte[] ExportFrameMask(int frameNumber, int width, int height)
        {
            if (!_frameMasks.TryGetValue(frameNumber, out var mask))
                return null;

            var exportData = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    exportData[y * width + x] = mask[y, x];
                }
            }

            return exportData;
        }

        /// <summary>
        /// Export masked video frame (object extracted with transparent background)
        /// </summary>
        /// <param name="frameData">Original frame RGBA data</param>
        /// <param name="frameNumber">Frame number</param>
        /// <param name="width">Video width</param>
        /// <param name="height">Video height</param>
        /// <returns>RGBA byte array with alpha channel from mask</returns>
        public byte[] ExportMaskedFrame(byte[] frameData, int frameNumber, int width, int height)
        {
            if (!_frameMasks.TryGetValue(frameNumber, out var mask))
                return null;

            if (frameData == null || frameData.Length != width * height * 4)
                return null;

            var maskedData = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixelIndex = (y * width + x) * 4;
                    bool isInMask = mask[y, x] > 0;

                    if (isInMask)
                    {
                        // Copy RGB and set alpha to opaque
                        maskedData[pixelIndex] = frameData[pixelIndex];         // R
                        maskedData[pixelIndex + 1] = frameData[pixelIndex + 1]; // G
                        maskedData[pixelIndex + 2] = frameData[pixelIndex + 2]; // B
                        maskedData[pixelIndex + 3] = 255;                        // A
                    }
                    else
                    {
                        // Transparent background
                        maskedData[pixelIndex] = 0;
                        maskedData[pixelIndex + 1] = 0;
                        maskedData[pixelIndex + 2] = 0;
                        maskedData[pixelIndex + 3] = 0;
                    }
                }
            }

            return maskedData;
        }

        /// <summary>
        /// Convert frame RGBA data to SKBitmap
        /// </summary>
        private SKBitmap ConvertFrameToBitmap(byte[] frameData, int width, int height)
        {
            var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

            unsafe
            {
                var pixels = (byte*)bitmap.GetPixels().ToPointer();
                for (int i = 0; i < frameData.Length; i++)
                {
                    pixels[i] = frameData[i];
                }
            }

            return bitmap;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _sam2?.Dispose();
                _frameMasks?.Clear();
                _disposed = true;
            }
        }
    }
}
