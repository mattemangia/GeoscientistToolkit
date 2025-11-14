using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace GeoscientistToolkit.Tools.CtImageStack.AISegmentation
{
    /// <summary>
    /// Grounding DINO object detector with text prompts
    /// Detects objects based on natural language descriptions
    /// </summary>
    public class GroundingDinoDetector : IDisposable
    {
        private InferenceSession _session;
        private readonly AISegmentationSettings _settings;
        private Dictionary<string, int> _vocabulary;
        private bool _disposed;

        // Special tokens
        private const string CLS_TOKEN = "[CLS]";
        private const string SEP_TOKEN = "[SEP]";
        private const string PAD_TOKEN = "[PAD]";
        private const string UNK_TOKEN = "[UNK]";
        private const int MAX_SEQ_LENGTH = 256;

        public GroundingDinoDetector()
        {
            _settings = AISegmentationSettings.Instance;
            LoadVocabulary();
            InitializeSession();
        }

        private void LoadVocabulary()
        {
            if (!File.Exists(_settings.GroundingDinoVocabPath))
            {
                throw new FileNotFoundException($"Vocabulary file not found: {_settings.GroundingDinoVocabPath}");
            }

            _vocabulary = new Dictionary<string, int>();
            var lines = File.ReadAllLines(_settings.GroundingDinoVocabPath);

            for (int i = 0; i < lines.Length; i++)
            {
                _vocabulary[lines[i].Trim()] = i;
            }
        }

        private void InitializeSession()
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                EnableMemPattern = true,
                EnableCpuMemArena = true,
                IntraOpNumThreads = Math.Max(1, _settings.CpuThreads / 2)
            };

            if (_settings.UseGpu)
            {
                try
                {
                    options.AppendExecutionProvider_CUDA();
                }
                catch
                {
                    Console.WriteLine("CUDA not available, falling back to CPU for Grounding DINO");
                }
            }

            if (!File.Exists(_settings.GroundingDinoModelPath))
            {
                throw new FileNotFoundException("Grounding DINO model not found. Please configure model path in settings.");
            }

            _session = new InferenceSession(_settings.GroundingDinoModelPath, options);
        }

        /// <summary>
        /// Detect objects in image based on text prompt
        /// </summary>
        /// <param name="image">Source image</param>
        /// <param name="textPrompt">Text description (e.g., "rock . mineral . crystal .")</param>
        /// <returns>List of detected bounding boxes with scores and labels</returns>
        public List<Detection> Detect(SKBitmap image, string textPrompt)
        {
            if (string.IsNullOrWhiteSpace(textPrompt))
                throw new ArgumentException("Text prompt cannot be empty");

            // Preprocess image
            var pixelValues = PreprocessImage(image);

            // Tokenize text
            var (inputIds, tokenTypeIds, attentionMask) = TokenizeText(textPrompt);

            // Create pixel mask (all ones for valid pixels)
            var pixelMask = new DenseTensor<long>(new[] { 1, 800, 800 });
            for (int i = 0; i < 800; i++)
                for (int j = 0; j < 800; j++)
                    pixelMask[0, i, j] = 1;

            // Run inference
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("pixel_values", pixelValues),
                NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
                NamedOnnxValue.CreateFromTensor("pixel_mask", pixelMask)
            };

            using (var results = _session.Run(inputs))
            {
                var logits = results.First(r => r.Name == "logits").AsTensor<float>();
                var predBoxes = results.First(r => r.Name == "pred_boxes").AsTensor<float>();

                return ProcessDetections(logits, predBoxes, image.Width, image.Height);
            }
        }

        private DenseTensor<float> PreprocessImage(SKBitmap sourceImage)
        {
            // Resize to 800x800
            using var resized = sourceImage.Resize(new SKSizeI(800, 800), SKFilterQuality.High);

            var tensor = new DenseTensor<float>(new[] { 1, 3, 800, 800 });

            // ImageNet normalization parameters
            float[] mean = { 0.485f, 0.456f, 0.406f };
            float[] std = { 0.229f, 0.224f, 0.225f };

            for (int y = 0; y < 800; y++)
            {
                for (int x = 0; x < 800; x++)
                {
                    var pixel = resized.GetPixel(x, y);

                    // Convert to RGB [0-1] and normalize
                    float r = pixel.Red / 255.0f;
                    float g = pixel.Green / 255.0f;
                    float b = pixel.Blue / 255.0f;

                    tensor[0, 0, y, x] = (r - mean[0]) / std[0];
                    tensor[0, 1, y, x] = (g - mean[1]) / std[1];
                    tensor[0, 2, y, x] = (b - mean[2]) / std[2];
                }
            }

            return tensor;
        }

        private (DenseTensor<long> inputIds, DenseTensor<long> tokenTypeIds, DenseTensor<long> attentionMask)
            TokenizeText(string text)
        {
            var inputIds = new DenseTensor<long>(new[] { 1, MAX_SEQ_LENGTH });
            var tokenTypeIds = new DenseTensor<long>(new[] { 1, MAX_SEQ_LENGTH });
            var attentionMask = new DenseTensor<long>(new[] { 1, MAX_SEQ_LENGTH });

            // Normalize text
            text = text.ToLowerInvariant();

            var tokens = new List<string> { CLS_TOKEN };

            // Simple whitespace tokenization with character fallback
            var words = text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                if (_vocabulary.ContainsKey(word))
                {
                    tokens.Add(word);
                }
                else
                {
                    // Character-level fallback for unknown words
                    foreach (char c in word)
                    {
                        string charStr = c.ToString();
                        tokens.Add(_vocabulary.ContainsKey(charStr) ? charStr : UNK_TOKEN);
                    }
                }
            }

            tokens.Add(SEP_TOKEN);

            // Convert tokens to IDs and pad
            for (int i = 0; i < MAX_SEQ_LENGTH; i++)
            {
                if (i < tokens.Count)
                {
                    string token = tokens[i];
                    inputIds[0, i] = _vocabulary.ContainsKey(token) ? _vocabulary[token] : _vocabulary[UNK_TOKEN];
                    tokenTypeIds[0, i] = 0;
                    attentionMask[0, i] = 1;
                }
                else
                {
                    inputIds[0, i] = _vocabulary[PAD_TOKEN];
                    tokenTypeIds[0, i] = 0;
                    attentionMask[0, i] = 0;
                }
            }

            return (inputIds, tokenTypeIds, attentionMask);
        }

        private List<Detection> ProcessDetections(Tensor<float> logits, Tensor<float> predBoxes,
                                                  int imageWidth, int imageHeight)
        {
            int numQueries = (int)logits.Dimensions[1]; // 900 queries
            int numClasses = (int)logits.Dimensions[2]; // 256 classes

            var detections = new List<Detection>();

            // Process each query
            for (int i = 0; i < numQueries; i++)
            {
                // Find max probability across classes (apply sigmoid first)
                float maxProb = float.MinValue;
                int maxClass = 0;

                for (int c = 0; c < numClasses; c++)
                {
                    float prob = Sigmoid(logits[0, i, c]);
                    if (prob > maxProb)
                    {
                        maxProb = prob;
                        maxClass = c;
                    }
                }

                // Filter by confidence threshold
                if (maxProb < _settings.ConfidenceThreshold)
                    continue;

                // Extract box coordinates [cx, cy, w, h] normalized to [0, 1]
                float cx = predBoxes[0, i, 0];
                float cy = predBoxes[0, i, 1];
                float w = predBoxes[0, i, 2];
                float h = predBoxes[0, i, 3];

                // Convert to pixel coordinates [x1, y1, x2, y2]
                float x1 = (cx - w / 2) * imageWidth;
                float y1 = (cy - h / 2) * imageHeight;
                float x2 = (cx + w / 2) * imageWidth;
                float y2 = (cy + h / 2) * imageHeight;

                detections.Add(new Detection
                {
                    Box = new BoundingBox
                    {
                        X1 = Math.Max(0, x1),
                        Y1 = Math.Max(0, y1),
                        X2 = Math.Min(imageWidth, x2),
                        Y2 = Math.Min(imageHeight, y2)
                    },
                    Score = maxProb,
                    ClassId = maxClass
                });
            }

            // Apply Non-Maximum Suppression
            return ApplyNMS(detections, _settings.IouThreshold);
        }

        private List<Detection> ApplyNMS(List<Detection> detections, float iouThreshold)
        {
            if (detections.Count == 0)
                return detections;

            // Sort by score descending
            var sorted = detections.OrderByDescending(d => d.Score).ToList();
            var keep = new List<Detection>();

            while (sorted.Count > 0)
            {
                var best = sorted[0];
                keep.Add(best);
                sorted.RemoveAt(0);

                // Remove overlapping boxes
                sorted = sorted.Where(d => CalculateIoU(best.Box, d.Box) < iouThreshold).ToList();
            }

            return keep.OrderByDescending(d => d.Score).ToList();
        }

        private float CalculateIoU(BoundingBox box1, BoundingBox box2)
        {
            float x1 = Math.Max(box1.X1, box2.X1);
            float y1 = Math.Max(box1.Y1, box2.Y1);
            float x2 = Math.Min(box1.X2, box2.X2);
            float y2 = Math.Min(box1.Y2, box2.Y2);

            if (x2 < x1 || y2 < y1)
                return 0.0f;

            float intersection = (x2 - x1) * (y2 - y1);
            float area1 = (box1.X2 - box1.X1) * (box1.Y2 - box1.Y1);
            float area2 = (box2.X2 - box2.X1) * (box2.Y2 - box2.Y1);
            float union = area1 + area2 - intersection;

            return intersection / union;
        }

        private float Sigmoid(float x)
        {
            return 1.0f / (1.0f + MathF.Exp(-x));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _session?.Dispose();
                _disposed = true;
            }
        }
    }

    public class Detection
    {
        public BoundingBox Box { get; set; }
        public float Score { get; set; }
        public int ClassId { get; set; }
    }

    public class BoundingBox
    {
        public float X1 { get; set; }
        public float Y1 { get; set; }
        public float X2 { get; set; }
        public float Y2 { get; set; }

        public float Width => X2 - X1;
        public float Height => Y2 - Y1;
        public float CenterX => (X1 + X2) / 2;
        public float CenterY => (Y1 + Y2) / 2;
    }
}
