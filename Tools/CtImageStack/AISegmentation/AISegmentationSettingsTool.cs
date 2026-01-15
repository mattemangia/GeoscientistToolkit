using System;
using System.IO;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using ImGuiNET;

namespace GeoscientistToolkit.Tools.CtImageStack.AISegmentation
{
    /// <summary>
    /// Settings tool for AI segmentation models
    /// Allows users to configure ONNX model paths and parameters
    /// </summary>
    public class AISegmentationSettingsTool : IDatasetTools
    {
        private readonly AISegmentationSettings _settings;
        private ImGuiExportFileDialog _sam2EncoderDialog;
        private ImGuiExportFileDialog _sam2DecoderDialog;
        private ImGuiExportFileDialog _microSamEncoderDialog;
        private ImGuiExportFileDialog _microSamDecoderDialog;
        private ImGuiExportFileDialog _gdinoModelDialog;
        private ImGuiExportFileDialog _gdinoVocabDialog;

        public AISegmentationSettingsTool()
        {
            _settings = AISegmentationSettings.Instance;

            // Initialize file dialogs
            _sam2EncoderDialog = new ImGuiExportFileDialog("sam2_encoder", "Select SAM2 Encoder Model");
            _sam2EncoderDialog.SetExtensions((".onnx", "ONNX Model"));

            _sam2DecoderDialog = new ImGuiExportFileDialog("sam2_decoder", "Select SAM2 Decoder Model");
            _sam2DecoderDialog.SetExtensions((".onnx", "ONNX Model"));

            _microSamEncoderDialog = new ImGuiExportFileDialog("microsam_encoder", "Select MicroSAM Encoder Model");
            _microSamEncoderDialog.SetExtensions((".onnx", "ONNX Model"));

            _microSamDecoderDialog = new ImGuiExportFileDialog("microsam_decoder", "Select MicroSAM Decoder Model");
            _microSamDecoderDialog.SetExtensions((".onnx", "ONNX Model"));

            _gdinoModelDialog = new ImGuiExportFileDialog("gdino_model", "Select Grounding DINO Model");
            _gdinoModelDialog.SetExtensions((".onnx", "ONNX Model"));

            _gdinoVocabDialog = new ImGuiExportFileDialog("gdino_vocab", "Select Vocabulary File");
            _gdinoVocabDialog.SetExtensions((".txt", "Text File"));
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset) return;

            ImGui.SeparatorText("AI Segmentation Settings");

            ImGui.TextWrapped("Configure paths to ONNX models for AI-powered segmentation. " +
                            "Download models from the official repositories and place them in your ONNX folder.");

            ImGui.Separator();

            // SAM2 Section
            if (ImGui.CollapsingHeader("SAM2 (Segment Anything Model 2)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawModelPathSetting("SAM2 Encoder", _settings.Sam2EncoderPath, _sam2EncoderDialog);
                DrawModelPathSetting("SAM2 Decoder", _settings.Sam2DecoderPath, _sam2DecoderDialog);

                ImGui.Spacing();
                var status = _settings.ValidateSam2Models();
                DrawStatusIndicator("SAM2 Models", status);
            }

            ImGui.Separator();

            // MicroSAM Section
            if (ImGui.CollapsingHeader("MicroSAM (Microscopy SAM)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawModelPathSetting("MicroSAM Encoder", _settings.MicroSamEncoderPath, _microSamEncoderDialog);
                DrawModelPathSetting("MicroSAM Decoder", _settings.MicroSamDecoderPath, _microSamDecoderDialog);

                ImGui.Spacing();
                var status = _settings.ValidateMicroSamModels();
                DrawStatusIndicator("MicroSAM Models", status);
            }

            ImGui.Separator();

            // Grounding DINO Section
            if (ImGui.CollapsingHeader("Grounding DINO (Text-Prompted Detection)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawModelPathSetting("Grounding DINO Model", _settings.GroundingDinoModelPath, _gdinoModelDialog);
                DrawModelPathSetting("Vocabulary File", _settings.GroundingDinoVocabPath, _gdinoVocabDialog);

                ImGui.Spacing();
                var status = _settings.ValidateGroundingDinoModel();
                DrawStatusIndicator("Grounding DINO", status);
            }

            ImGui.Separator();

            // Performance Settings
            if (ImGui.CollapsingHeader("Performance Settings", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var useGpu = _settings.UseGpu;
                if (ImGui.Checkbox("Use GPU Acceleration", ref useGpu))
                {
                    _settings.UseGpu = useGpu;
                    _settings.Save();
                }
                ImGui.SameLine();
                HelpMarker("Enable CUDA/DirectML GPU acceleration for faster inference");

                var cpuThreads = _settings.CpuThreads;
                if (ImGui.SliderInt("CPU Threads", ref cpuThreads, 1, Environment.ProcessorCount))
                {
                    _settings.CpuThreads = cpuThreads;
                    _settings.Save();
                }
            }

            ImGui.Separator();

            // Advanced Parameters
            if (ImGui.CollapsingHeader("Advanced Parameters"))
            {
                var confThreshold = _settings.ConfidenceThreshold;
                if (ImGui.SliderFloat("Confidence Threshold", ref confThreshold, 0.1f, 0.9f, "%.2f"))
                {
                    _settings.ConfidenceThreshold = confThreshold;
                    _settings.Save();
                }
                HelpMarker("Minimum confidence for Grounding DINO detections");

                var iouThreshold = _settings.IouThreshold;
                if (ImGui.SliderFloat("IoU Threshold (NMS)", ref iouThreshold, 0.3f, 0.9f, "%.2f"))
                {
                    _settings.IouThreshold = iouThreshold;
                    _settings.Save();
                }
                HelpMarker("Non-Maximum Suppression threshold for overlapping boxes");

                var zeroShotIou = _settings.MicroSamZeroShotIouThreshold;
                if (ImGui.SliderFloat("Zero-Shot IoU Threshold", ref zeroShotIou, 0.2f, 0.8f, "%.2f"))
                {
                    _settings.MicroSamZeroShotIouThreshold = zeroShotIou;
                    _settings.Save();
                }
                HelpMarker("Minimum IoU for MicroSAM zero-shot mask filtering");

                var strategyNames = Enum.GetNames(typeof(PointPlacementStrategy));
                var currentStrategy = (int)_settings.DefaultPointStrategy;
                if (ImGui.Combo("Default Point Strategy", ref currentStrategy, strategyNames, strategyNames.Length))
                {
                    _settings.DefaultPointStrategy = (PointPlacementStrategy)currentStrategy;
                    _settings.Save();
                }
                HelpMarker("How to convert bounding boxes to SAM point prompts in pipelines");
            }

            ImGui.Separator();

            // Save button
            if (ImGui.Button("Save Settings", new Vector2(150, 30)))
            {
                _settings.Save();
                ImGui.OpenPopup("SettingsSaved");
            }

            if (ImGui.BeginPopup("SettingsSaved"))
            {
                ImGui.Text("Settings saved successfully!");
                ImGui.EndPopup();
            }

            // Handle file dialog submissions
            if (_sam2EncoderDialog.Submit())
            {
                _settings.Sam2EncoderPath = _sam2EncoderDialog.SelectedPath;
                _settings.Save();
            }

            if (_sam2DecoderDialog.Submit())
            {
                _settings.Sam2DecoderPath = _sam2DecoderDialog.SelectedPath;
                _settings.Save();
            }

            if (_microSamEncoderDialog.Submit())
            {
                _settings.MicroSamEncoderPath = _microSamEncoderDialog.SelectedPath;
                _settings.Save();
            }

            if (_microSamDecoderDialog.Submit())
            {
                _settings.MicroSamDecoderPath = _microSamDecoderDialog.SelectedPath;
                _settings.Save();
            }

            if (_gdinoModelDialog.Submit())
            {
                _settings.GroundingDinoModelPath = _gdinoModelDialog.SelectedPath;
                _settings.Save();
            }

            if (_gdinoVocabDialog.Submit())
            {
                _settings.GroundingDinoVocabPath = _gdinoVocabDialog.SelectedPath;
                _settings.Save();
            }
        }

        private void DrawModelPathSetting(string label, string currentPath, ImGuiExportFileDialog dialog)
        {
            ImGui.Text(label + ":");
            ImGui.SameLine();

            // Show current path with validation
            var exists = File.Exists(currentPath);
            if (!exists) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.3f, 0.3f, 1));

            var displayPath = currentPath;
            if (displayPath.Length > 50)
                displayPath = "..." + displayPath.Substring(displayPath.Length - 47);

            ImGui.Text(displayPath);
            if (!exists) ImGui.PopStyleColor();

            ImGui.SameLine();
            if (ImGui.SmallButton($"Browse##{dialog}"))
            {
                var startDir = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir))
                    startDir = "ONNX";
                dialog.Open(Path.GetFileName(currentPath), startDir);
            }

            if (!exists && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("File not found!");
            }
        }

        private void DrawStatusIndicator(string label, bool isValid)
        {
            ImGui.Text($"{label} Status:");
            ImGui.SameLine();

            if (isValid)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 1.0f, 0.2f, 1.0f));
                ImGui.Text("Ready");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));
                ImGui.Text("Models not found");
                ImGui.PopStyleColor();
            }
        }

        private void HelpMarker(string description)
        {
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
                ImGui.TextUnformatted(description);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }
    }
}
