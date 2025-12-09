// GeoscientistToolkit/Data/Nerf/NerfPropertiesRenderer.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Nerf;

/// <summary>
/// Properties panel renderer for NeRF datasets.
/// </summary>
public class NerfPropertiesRenderer : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not NerfDataset nerfDataset)
        {
            ImGui.TextDisabled("Invalid dataset type.");
            return;
        }

        ImGui.Text("NeRF Dataset Properties");
        ImGui.Separator();

        // Basic info
        ImGui.Text($"Name: {nerfDataset.Name}");
        ImGui.Text($"Size: {FormatBytes(nerfDataset.GetSizeInBytes())}");
        ImGui.Separator();

        // Source information
        if (ImGui.CollapsingHeader("Source", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Type: {nerfDataset.SourceType}");

            if (!string.IsNullOrEmpty(nerfDataset.SourcePath))
            {
                ImGui.TextWrapped($"Path: {nerfDataset.SourcePath}");
            }

            if (!string.IsNullOrEmpty(nerfDataset.VideoPath))
            {
                ImGui.TextWrapped($"Video: {nerfDataset.VideoPath}");
            }
        }

        // Image collection info
        if (ImGui.CollapsingHeader("Image Collection", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var collection = nerfDataset.ImageCollection;
            if (collection != null)
            {
                ImGui.Text($"Total frames: {collection.FrameCount}");
                ImGui.Text($"Frames with poses: {collection.GetFramesWithPoses().Count()}");
                ImGui.Text($"Enabled frames: {collection.GetEnabledFrames().Count()}");

                if (collection.FrameCount > 0)
                {
                    var firstFrame = collection.Frames[0];
                    ImGui.Text($"Resolution: {firstFrame.Width} x {firstFrame.Height}");
                }

                ImGui.Spacing();
                ImGui.Text("Scene Bounds:");
                ImGui.Text($"  Center: ({collection.SceneCenter.X:F2}, {collection.SceneCenter.Y:F2}, {collection.SceneCenter.Z:F2})");
                ImGui.Text($"  Radius: {collection.SceneRadius:F2}");
            }
            else
            {
                ImGui.TextDisabled("No images loaded");
            }
        }

        // Training info
        if (ImGui.CollapsingHeader("Training", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var stateColor = nerfDataset.TrainingState switch
            {
                NerfTrainingState.Training => new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
                NerfTrainingState.Completed => new Vector4(0.2f, 0.6f, 1.0f, 1.0f),
                NerfTrainingState.Paused => new Vector4(1.0f, 0.8f, 0.2f, 1.0f),
                NerfTrainingState.Failed => new Vector4(1.0f, 0.2f, 0.2f, 1.0f),
                _ => new Vector4(0.5f, 0.5f, 0.5f, 1.0f)
            };

            ImGui.TextColored(stateColor, $"State: {nerfDataset.TrainingState}");
            ImGui.Text($"Iterations: {nerfDataset.CurrentIteration}/{nerfDataset.TotalIterations}");

            if (nerfDataset.CurrentIteration > 0)
            {
                ImGui.Text($"Loss: {nerfDataset.CurrentLoss:F6}");
                ImGui.Text($"PSNR: {nerfDataset.CurrentPSNR:F2} dB");
            }

            if (nerfDataset.TrainingStartTime.HasValue)
            {
                ImGui.Spacing();
                ImGui.Text($"Started: {nerfDataset.TrainingStartTime:yyyy-MM-dd HH:mm}");

                if (nerfDataset.TrainingEndTime.HasValue)
                {
                    ImGui.Text($"Ended: {nerfDataset.TrainingEndTime:yyyy-MM-dd HH:mm}");
                    ImGui.Text($"Duration: {nerfDataset.TrainingDurationSeconds:F1} seconds");
                }
            }
        }

        // Model info
        if (ImGui.CollapsingHeader("Model"))
        {
            var model = nerfDataset.ModelData;
            if (model != null)
            {
                ImGui.Text($"Model size: {FormatBytes(model.GetSizeBytes())}");
                ImGui.Spacing();

                ImGui.Text("Hash Grid Encoding:");
                ImGui.Text($"  Levels: {model.NumLevels}");
                ImGui.Text($"  Features per level: {model.FeaturesPerLevel}");
                ImGui.Text($"  Table size: 2^{model.Log2HashTableSize}");
                ImGui.Text($"  Resolution: {model.BaseResolution:F0} - {model.MaxResolution:F0}");

                ImGui.Spacing();
                ImGui.Text("MLP Architecture:");
                ImGui.Text($"  Density MLP: {model.DensityMLP.Count} layers");
                ImGui.Text($"  Color MLP: {model.ColorMLP.Count} layers");

                ImGui.Spacing();
                ImGui.Text("Scene Parameters:");
                ImGui.Text($"  Center: ({model.SceneCenter.X:F2}, {model.SceneCenter.Y:F2}, {model.SceneCenter.Z:F2})");
                ImGui.Text($"  Scale: {model.SceneScale:F4}");
            }
            else
            {
                ImGui.TextDisabled("No model trained yet");
            }
        }

        // Training configuration
        if (ImGui.CollapsingHeader("Configuration"))
        {
            var config = nerfDataset.TrainingConfig;
            if (config != null)
            {
                ImGui.Text("Hash Grid:");
                ImGui.Text($"  Levels: {config.HashGridLevels}");
                ImGui.Text($"  Features: {config.HashGridFeatures}");

                ImGui.Spacing();
                ImGui.Text("MLP:");
                ImGui.Text($"  Hidden layers: {config.MlpHiddenLayers}");
                ImGui.Text($"  Hidden width: {config.MlpHiddenWidth}");

                ImGui.Spacing();
                ImGui.Text("Training:");
                ImGui.Text($"  Learning rate: {config.LearningRate}");
                ImGui.Text($"  Batch size: {config.BatchSize}");
                ImGui.Text($"  Samples per ray: {config.SamplesPerRay}");

                ImGui.Spacing();
                ImGui.Text("Scene:");
                ImGui.Text($"  Near plane: {config.NearPlane}");
                ImGui.Text($"  Far plane: {config.FarPlane}");

                ImGui.Spacing();
                ImGui.Text($"GPU: {(config.UseGPU ? "Enabled" : "Disabled")}");
                ImGui.Text($"Mixed precision: {(config.UseMixedPrecision ? "Enabled" : "Disabled")}");
            }
        }

        // Training history summary
        if (nerfDataset.TrainingHistory.Count > 0 && ImGui.CollapsingHeader("Training History"))
        {
            var history = nerfDataset.TrainingHistory;

            ImGui.Text($"Log entries: {history.Count}");

            if (history.Count > 0)
            {
                var lastEntry = history[^1];
                var firstEntry = history[0];

                ImGui.Spacing();
                ImGui.Text("First entry:");
                ImGui.Text($"  Iteration: {firstEntry.Iteration}");
                ImGui.Text($"  Loss: {firstEntry.Loss:F6}");
                ImGui.Text($"  PSNR: {firstEntry.PSNR:F2} dB");

                ImGui.Spacing();
                ImGui.Text("Last entry:");
                ImGui.Text($"  Iteration: {lastEntry.Iteration}");
                ImGui.Text($"  Loss: {lastEntry.Loss:F6}");
                ImGui.Text($"  PSNR: {lastEntry.PSNR:F2} dB");

                if (history.Count > 1)
                {
                    float lossImprovement = (firstEntry.Loss - lastEntry.Loss) / firstEntry.Loss * 100;
                    float psnrImprovement = lastEntry.PSNR - firstEntry.PSNR;

                    ImGui.Spacing();
                    ImGui.Text("Improvement:");
                    ImGui.Text($"  Loss: {lossImprovement:F1}% reduction");
                    ImGui.Text($"  PSNR: +{psnrImprovement:F2} dB");
                }
            }
        }

        // Metadata
        DrawMetadataSection(nerfDataset);
    }

    private void DrawMetadataSection(NerfDataset dataset)
    {
        if (ImGui.CollapsingHeader("Metadata"))
        {
            var meta = dataset.DatasetMetadata;

            if (!string.IsNullOrEmpty(meta.SampleName))
                ImGui.Text($"Sample: {meta.SampleName}");

            if (!string.IsNullOrEmpty(meta.LocationName))
                ImGui.Text($"Location: {meta.LocationName}");

            if (meta.Latitude.HasValue && meta.Longitude.HasValue)
                ImGui.Text($"Coordinates: {meta.Latitude:F6}, {meta.Longitude:F6}");

            if (meta.CollectionDate.HasValue)
                ImGui.Text($"Date: {meta.CollectionDate:yyyy-MM-dd}");

            if (!string.IsNullOrEmpty(meta.Collector))
                ImGui.Text($"Collector: {meta.Collector}");

            if (!string.IsNullOrEmpty(meta.Notes))
            {
                ImGui.Spacing();
                ImGui.TextWrapped($"Notes: {meta.Notes}");
            }

            if (meta.CustomFields?.Count > 0)
            {
                ImGui.Spacing();
                ImGui.Text("Custom Fields:");
                foreach (var field in meta.CustomFields)
                {
                    ImGui.Text($"  {field.Key}: {field.Value}");
                }
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
