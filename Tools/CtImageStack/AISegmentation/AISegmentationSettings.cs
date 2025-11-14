using System;
using System.IO;
using Newtonsoft.Json;

namespace GeoscientistToolkit.Tools.CtImageStack.AISegmentation
{
    /// <summary>
    /// Manages settings for AI segmentation models (SAM2, MicroSAM, Grounding DINO)
    /// </summary>
    public class AISegmentationSettings
    {
        private static AISegmentationSettings _instance;
        private static readonly object _lock = new object();

        public static AISegmentationSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = Load() ?? new AISegmentationSettings();
                        }
                    }
                }
                return _instance;
            }
        }

        // SAM2 Model Paths
        public string Sam2EncoderPath { get; set; } = "ONNX/sam2.1_large.encoder.onnx";
        public string Sam2DecoderPath { get; set; } = "ONNX/sam2.1_large.decoder.onnx";

        // MicroSAM Model Paths
        public string MicroSamEncoderPath { get; set; } = "ONNX/micro-sam-encoder.onnx";
        public string MicroSamDecoderPath { get; set; } = "ONNX/micro-sam-decoder.onnx";

        // Grounding DINO Model Paths
        public string GroundingDinoModelPath { get; set; } = "ONNX/g_dino.onnx";
        public string GroundingDinoVocabPath { get; set; } = "ONNX/vocab.txt";

        // Performance Settings
        public bool UseGpu { get; set; } = true;
        public int CpuThreads { get; set; } = Environment.ProcessorCount;

        // Segmentation Parameters
        public float ConfidenceThreshold { get; set; } = 0.3f;
        public float IouThreshold { get; set; } = 0.7f;
        public float MicroSamZeroShotIouThreshold { get; set; } = 0.4f;

        // Pipeline Settings
        public PointPlacementStrategy DefaultPointStrategy { get; set; } = PointPlacementStrategy.CenterPoint;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GeoscientistToolkit",
            "ai_segmentation_settings.json"
        );

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save AI segmentation settings: {ex.Message}");
            }
        }

        private static AISegmentationSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonConvert.DeserializeObject<AISegmentationSettings>(json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load AI segmentation settings: {ex.Message}");
            }
            return null;
        }

        public bool ValidateSam2Models()
        {
            return File.Exists(Sam2EncoderPath) && File.Exists(Sam2DecoderPath);
        }

        public bool ValidateMicroSamModels()
        {
            return File.Exists(MicroSamEncoderPath) && File.Exists(MicroSamDecoderPath);
        }

        public bool ValidateGroundingDinoModel()
        {
            return File.Exists(GroundingDinoModelPath) && File.Exists(GroundingDinoVocabPath);
        }
    }

    public enum PointPlacementStrategy
    {
        CenterPoint,        // Single point at bbox center
        CornerPoints,       // 4 points at corners
        BoxOutline,         // Points along box perimeter
        WeightedGrid,       // Grid of points weighted by position
        BoxFill            // Dense grid filling the box
    }
}
