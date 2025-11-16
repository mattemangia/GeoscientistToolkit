// GeoscientistToolkit/Data/Image/AISegmentation/ImageAISegmentationPipeline.cs

using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using GeoscientistToolkit.Tools.CtImageStack.AISegmentation;

namespace GeoscientistToolkit.Data.Image.AISegmentation
{
    /// <summary>
    /// AI segmentation pipeline for ImageDataset
    /// Integrates SAM2, MicroSAM, and GroundingDino for 2D images
    /// </summary>
    public class ImageAISegmentationPipeline : IDisposable
    {
        private GroundingDinoDetector _detector;
        private Sam2Segmenter _sam2;
        private MicroSamSegmenter _microSam;
        private bool _disposed;

        public enum SegmenterType
        {
            SAM2,
            MicroSAM
        }

        public ImageAISegmentationPipeline(SegmenterType segmenterType)
        {
            _detector = new GroundingDinoDetector();

            if (segmenterType == SegmenterType.SAM2)
                _sam2 = new Sam2Segmenter();
            else
                _microSam = new MicroSamSegmenter();
        }

        /// <summary>
        /// Detect and segment objects in an ImageDataset
        /// </summary>
        /// <param name="dataset">Source image dataset</param>
        /// <param name="textPrompt">Detection prompt (e.g., "mineral . crystal .")</param>
        /// <param name="strategy">Point placement strategy</param>
        /// <returns>List of segmentation results</returns>
        public List<ImageSegmentationResult> DetectAndSegment(
            ImageDataset dataset,
            string textPrompt,
            PointPlacementStrategy strategy = PointPlacementStrategy.CenterPoint)
        {
            if (dataset.ImageData == null)
                throw new InvalidOperationException("Image data not loaded");

            using var bitmap = ConvertToBitmap(dataset);
            return DetectAndSegment(bitmap, textPrompt, strategy);
        }

        /// <summary>
        /// Detect and segment objects in a bitmap
        /// </summary>
        public List<ImageSegmentationResult> DetectAndSegment(
            SKBitmap image,
            string textPrompt,
            PointPlacementStrategy strategy = PointPlacementStrategy.CenterPoint)
        {
            // Step 1: Detect objects with Grounding DINO
            var detections = _detector.Detect(image, textPrompt);

            if (detections.Count == 0)
                return new List<ImageSegmentationResult>();

            // Step 2: Convert each detection to SAM prompts and segment
            var results = new List<ImageSegmentationResult>();

            foreach (var detection in detections)
            {
                var (points, labels) = GeneratePrompts(detection.Box, strategy);

                byte[,] mask;
                if (_sam2 != null)
                    mask = _sam2.Segment(image, points, labels);
                else
                    mask = _microSam.Segment(image, points, labels);

                results.Add(new ImageSegmentationResult
                {
                    BoundingBox = detection.Box,
                    Mask = mask,
                    Score = detection.Score,
                    ClassId = detection.ClassId
                });
            }

            return results;
        }

        /// <summary>
        /// Interactive segmentation with point prompts (no detection)
        /// </summary>
        /// <param name="dataset">Source image dataset</param>
        /// <param name="points">Prompt points (x, y coordinates)</param>
        /// <param name="labels">Point labels (1.0 = positive, 0.0 = negative)</param>
        /// <returns>Segmentation mask as 2D array</returns>
        public byte[,] SegmentWithPoints(
            ImageDataset dataset,
            List<(float x, float y)> points,
            List<float> labels)
        {
            if (dataset.ImageData == null)
                throw new InvalidOperationException("Image data not loaded");

            using var bitmap = ConvertToBitmap(dataset);
            return SegmentWithPoints(bitmap, points, labels);
        }

        /// <summary>
        /// Interactive segmentation with point prompts (no detection)
        /// </summary>
        public byte[,] SegmentWithPoints(
            SKBitmap image,
            List<(float x, float y)> points,
            List<float> labels)
        {
            if (_sam2 != null)
                return _sam2.Segment(image, points, labels);
            else
                return _microSam.Segment(image, points, labels);
        }

        /// <summary>
        /// Apply segmentation result to ImageDataset
        /// </summary>
        /// <param name="dataset">Target dataset</param>
        /// <param name="result">Segmentation result to apply</param>
        /// <param name="materialId">Material ID to assign (0 = create new)</param>
        public void ApplyToDataset(
            ImageDataset dataset,
            ImageSegmentationResult result,
            byte materialId = 0)
        {
            var segmentation = dataset.GetOrCreateSegmentation();
            segmentation.SaveUndoState();

            // Create new material if needed
            if (materialId == 0)
            {
                var material = segmentation.AddMaterial(
                    $"AI Segment {segmentation.Materials.Count}",
                    new System.Numerics.Vector4(
                        (float)Random.Shared.NextDouble(),
                        (float)Random.Shared.NextDouble(),
                        (float)Random.Shared.NextDouble(),
                        0.6f));
                materialId = material.ID;
            }

            // Apply mask to label data
            ApplyMaskToLabels(result.Mask, segmentation.LabelData, materialId, dataset.Width);
        }

        /// <summary>
        /// Apply 2D mask to flat label array
        /// </summary>
        public static void ApplyMaskToLabels(byte[,] mask, byte[] labelData, byte materialId, int width)
        {
            int height = mask.GetLength(0);
            int maskWidth = mask.GetLength(1);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < maskWidth; x++)
                {
                    if (mask[y, x] > 0)
                    {
                        int index = y * width + x;
                        if (index < labelData.Length)
                            labelData[index] = materialId;
                    }
                }
            }
        }

        /// <summary>
        /// Convert ImageDataset to SKBitmap
        /// </summary>
        private SKBitmap ConvertToBitmap(ImageDataset dataset)
        {
            var bitmap = new SKBitmap(dataset.Width, dataset.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

            unsafe
            {
                var pixels = (byte*)bitmap.GetPixels().ToPointer();
                for (int i = 0; i < dataset.ImageData.Length; i++)
                {
                    pixels[i] = dataset.ImageData[i];
                }
            }

            return bitmap;
        }

        /// <summary>
        /// Generate point prompts from bounding box based on strategy
        /// </summary>
        private (List<(float x, float y)> points, List<float> labels) GeneratePrompts(
            BoundingBox box,
            PointPlacementStrategy strategy)
        {
            var points = new List<(float x, float y)>();
            var labels = new List<float>();

            switch (strategy)
            {
                case PointPlacementStrategy.CenterPoint:
                    points.Add((box.CenterX, box.CenterY));
                    labels.Add(1.0f);
                    break;

                case PointPlacementStrategy.CornerPoints:
                    points.Add((box.X1, box.Y1));
                    points.Add((box.X2, box.Y1));
                    points.Add((box.X1, box.Y2));
                    points.Add((box.X2, box.Y2));
                    labels.AddRange(new[] { 1.0f, 1.0f, 1.0f, 1.0f });
                    break;

                case PointPlacementStrategy.BoxOutline:
                    AddOutlinePoints(box, points, labels);
                    break;

                case PointPlacementStrategy.WeightedGrid:
                    AddWeightedGridPoints(box, points, labels);
                    break;

                case PointPlacementStrategy.BoxFill:
                    AddDenseGridPoints(box, points, labels);
                    break;
            }

            return (points, labels);
        }

        private void AddOutlinePoints(BoundingBox box, List<(float x, float y)> points, List<float> labels)
        {
            int numPointsPerSide = 3;

            for (int i = 0; i < numPointsPerSide; i++)
            {
                float x = box.X1 + (box.Width * i / (numPointsPerSide - 1));
                points.Add((x, box.Y1));
                labels.Add(1.0f);
            }

            for (int i = 1; i < numPointsPerSide - 1; i++)
            {
                float y = box.Y1 + (box.Height * i / (numPointsPerSide - 1));
                points.Add((box.X2, y));
                labels.Add(1.0f);
            }

            for (int i = numPointsPerSide - 1; i >= 0; i--)
            {
                float x = box.X1 + (box.Width * i / (numPointsPerSide - 1));
                points.Add((x, box.Y2));
                labels.Add(1.0f);
            }

            for (int i = numPointsPerSide - 2; i > 0; i--)
            {
                float y = box.Y1 + (box.Height * i / (numPointsPerSide - 1));
                points.Add((box.X1, y));
                labels.Add(1.0f);
            }
        }

        private void AddWeightedGridPoints(BoundingBox box, List<(float x, float y)> points, List<float> labels)
        {
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    float x = box.X1 + (box.Width * col / 2);
                    float y = box.Y1 + (box.Height * row / 2);
                    points.Add((x, y));
                    labels.Add(1.0f);
                }
            }
        }

        private void AddDenseGridPoints(BoundingBox box, List<(float x, float y)> points, List<float> labels)
        {
            for (int row = 0; row < 5; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    float x = box.X1 + (box.Width * col / 4);
                    float y = box.Y1 + (box.Height * row / 4);
                    points.Add((x, y));
                    labels.Add(1.0f);
                }
            }
        }

        public void ClearCache()
        {
            _sam2?.ClearCache();
            _microSam?.ClearCache();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _detector?.Dispose();
                _sam2?.Dispose();
                _microSam?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Segmentation result for ImageDataset
    /// </summary>
    public class ImageSegmentationResult
    {
        public BoundingBox BoundingBox { get; set; }
        public byte[,] Mask { get; set; }
        public float Score { get; set; }
        public int ClassId { get; set; }
    }
}
