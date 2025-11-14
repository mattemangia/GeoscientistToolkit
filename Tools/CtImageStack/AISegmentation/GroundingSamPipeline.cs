using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;

namespace GeoscientistToolkit.Tools.CtImageStack.AISegmentation
{
    /// <summary>
    /// Combined pipeline: Grounding DINO detection + SAM/MicroSAM segmentation
    /// Detects objects with text prompts, then generates precise masks
    /// </summary>
    public class GroundingSamPipeline : IDisposable
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

        public GroundingSamPipeline(SegmenterType segmenterType)
        {
            _detector = new GroundingDinoDetector();

            if (segmenterType == SegmenterType.SAM2)
                _sam2 = new Sam2Segmenter();
            else
                _microSam = new MicroSamSegmenter();
        }

        /// <summary>
        /// Detect and segment objects in one pipeline
        /// </summary>
        /// <param name="image">Source image</param>
        /// <param name="textPrompt">Detection prompt (e.g., "rock . mineral .")</param>
        /// <param name="strategy">How to convert bounding boxes to point prompts</param>
        /// <returns>List of segmentation results with bounding boxes and masks</returns>
        public List<SegmentationResult> DetectAndSegment(
            SKBitmap image,
            string textPrompt,
            PointPlacementStrategy strategy = PointPlacementStrategy.CenterPoint)
        {
            // Step 1: Detect objects with Grounding DINO
            var detections = _detector.Detect(image, textPrompt);

            if (detections.Count == 0)
                return new List<SegmentationResult>();

            // Step 2: Convert each detection to SAM prompts and segment
            var results = new List<SegmentationResult>();

            foreach (var detection in detections)
            {
                var (points, labels) = GeneratePrompts(detection.Box, strategy);

                byte[,] mask;
                if (_sam2 != null)
                    mask = _sam2.Segment(image, points, labels);
                else
                    mask = _microSam.Segment(image, points, labels);

                results.Add(new SegmentationResult
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
                    // Single positive point at center
                    points.Add((box.CenterX, box.CenterY));
                    labels.Add(1.0f);
                    break;

                case PointPlacementStrategy.CornerPoints:
                    // Four positive points at corners
                    points.Add((box.X1, box.Y1));
                    points.Add((box.X2, box.Y1));
                    points.Add((box.X1, box.Y2));
                    points.Add((box.X2, box.Y2));
                    labels.AddRange(new[] { 1.0f, 1.0f, 1.0f, 1.0f });
                    break;

                case PointPlacementStrategy.BoxOutline:
                    // Points along box perimeter
                    AddOutlinePoints(box, points, labels);
                    break;

                case PointPlacementStrategy.WeightedGrid:
                    // 3x3 grid with center weighted
                    AddWeightedGridPoints(box, points, labels);
                    break;

                case PointPlacementStrategy.BoxFill:
                    // Dense 5x5 grid
                    AddDenseGridPoints(box, points, labels);
                    break;
            }

            return (points, labels);
        }

        private void AddOutlinePoints(BoundingBox box, List<(float x, float y)> points, List<float> labels)
        {
            int numPointsPerSide = 3;

            // Top edge
            for (int i = 0; i < numPointsPerSide; i++)
            {
                float x = box.X1 + (box.Width * i / (numPointsPerSide - 1));
                points.Add((x, box.Y1));
                labels.Add(1.0f);
            }

            // Right edge (skip corners)
            for (int i = 1; i < numPointsPerSide - 1; i++)
            {
                float y = box.Y1 + (box.Height * i / (numPointsPerSide - 1));
                points.Add((box.X2, y));
                labels.Add(1.0f);
            }

            // Bottom edge
            for (int i = numPointsPerSide - 1; i >= 0; i--)
            {
                float x = box.X1 + (box.Width * i / (numPointsPerSide - 1));
                points.Add((x, box.Y2));
                labels.Add(1.0f);
            }

            // Left edge (skip corners)
            for (int i = numPointsPerSide - 2; i > 0; i--)
            {
                float y = box.Y1 + (box.Height * i / (numPointsPerSide - 1));
                points.Add((box.X1, y));
                labels.Add(1.0f);
            }
        }

        private void AddWeightedGridPoints(BoundingBox box, List<(float x, float y)> points, List<float> labels)
        {
            // 3x3 grid with center point emphasized
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
            // 5x5 dense grid
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

    public class SegmentationResult
    {
        public BoundingBox BoundingBox { get; set; }
        public byte[,] Mask { get; set; }
        public float Score { get; set; }
        public int ClassId { get; set; }
    }
}
