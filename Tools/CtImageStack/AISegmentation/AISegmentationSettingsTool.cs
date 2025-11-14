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
        private ImGuiExportFileDialog _fileDialog;
        private string _currentSettingKey;

        public AISegmentationSettingsTool()
        {
            _settings = AISegmentationSettings.Instance;
            _fileDialog = new ImGuiExportFileDialog();
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
                DrawModelPathSetting("SAM2 Encoder", _settings.Sam2EncoderPath, "sam2_encoder");
                DrawModelPathSetting("SAM2 Decoder", _settings.Sam2DecoderPath, "sam2_decoder");

                ImGui.Spacing();
                var status = _settings.ValidateSam2Models();
                DrawStatusIndicator("SAM2 Models", status);
            }

            ImGui.Separator();

            // MicroSAM Section
            if (ImGui.CollapsingHeader("MicroSAM (Microscopy SAM)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawModelPathSetting("MicroSAM Encoder", _settings.MicroSamEncoderPath, "microsam_encoder");
                DrawModelPathSetting("MicroSAM Decoder", _settings.MicroSamDecoderPath, "microsam_decoder");

                ImGui.Spacing();
                var status = _settings.ValidateMicroSamModels();
                DrawStatusIndicator("MicroSAM Models", status);
            }

            ImGui.Separator();

            // Grounding DINO Section
            if (ImGui.CollapsingHeader("Grounding DINO (Text-Prompted Detection)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawModelPathSetting("Grounding DINO Model", _settings.GroundingDinoModelPath, "gdino_model");
                DrawModelPathSetting("Vocabulary File", _settings.GroundingDinoVocabPath, "gdino_vocab");

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

            // Handle file dialog
            _fileDialog.Draw();

            if (_fileDialog.IsFileSelected())
            {
                var selectedPath = _fileDialog.SelectedPath;
                UpdateSettingPath(_currentSettingKey, selectedPath);
                _fileDialog.Reset();
            }
        }

        private void DrawModelPathSetting(string label, string currentPath, string settingKey)
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
            if (ImGui.SmallButton($"Browse##{settingKey}"))
            {
                _currentSettingKey = settingKey;
                _fileDialog.Open("Select ONNX Model", Path.GetDirectoryName(currentPath) ?? "ONNX",
                    settingKey.Contains("vocab") ? ".txt" : ".onnx");
            }

            if (!exists && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("File not found!");
            }
        }

        private void UpdateSettingPath(string settingKey, string newPath)
        {
            switch (settingKey)
            {
                case "sam2_encoder":
                    _settings.Sam2EncoderPath = newPath;
                    break;
                case "sam2_decoder":
                    _settings.Sam2DecoderPath = newPath;
                    break;
                case "microsam_encoder":
                    _settings.MicroSamEncoderPath = newPath;
                    break;
                case "microsam_decoder":
                    _settings.MicroSamDecoderPath = newPath;
                    break;
                case "gdino_model":
                    _settings.GroundingDinoModelPath = newPath;
                    break;
                case "gdino_vocab":
                    _settings.GroundingDinoVocabPath = newPath;
                    break;
            }
            _settings.Save();
        }

        private void DrawStatusIndicator(string label, bool isValid)
        {
            ImGui.Text($"{label} Status:");
            ImGui.SameLine();

            if (isValid)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.2f, 1.0f, 0.2f, 1.0f));
                ImGui.Text("✓ Ready");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));
                ImGui.Text("✗ Models not found");
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
