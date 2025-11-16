// GeoscientistToolkit/Data/Media/VideoDatasetTools.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Media;

/// <summary>
/// Tools panel for video datasets with frame extraction and analysis
/// </summary>
public class VideoDatasetTools : IDatasetTools
{
    private double _extractFrameTime = 0.0;
    private string _exportPath = "";
    private bool _isExtracting = false;
    private Task _extractTask;

    public void Draw(Dataset dataset)
    {
        if (dataset is not VideoDataset videoDataset)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Invalid dataset type");
            return;
        }

        if (videoDataset.IsMissing)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Video file not found");
            return;
        }

        ImGui.SeparatorText("Frame Extraction");

        ImGui.Text("Extract Frame at Time:");
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("##FrameTime", ref (float)_extractFrameTime, 0f, (float)videoDataset.DurationSeconds, "%.2fs"))
        {
            _extractFrameTime = Math.Clamp(_extractFrameTime, 0.0, videoDataset.DurationSeconds);
        }

        var frameNumber = (int)(_extractFrameTime * videoDataset.FrameRate);
        ImGui.Text($"Frame: {frameNumber + 1}/{videoDataset.TotalFrames}");

        if (ImGui.Button("Extract Current Frame"))
        {
            ExtractFrame(videoDataset, _extractFrameTime);
        }

        ImGui.SameLine();
        if (ImGui.Button("Extract All Frames"))
        {
            ImGui.OpenPopup("extract_all_confirm");
        }

        // Confirmation popup for extract all
        if (ImGui.BeginPopupModal("extract_all_confirm", ref var isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Extract all {videoDataset.TotalFrames} frames?");
            ImGui.Text("This may take a while and use significant disk space.");
            ImGui.Separator();

            if (ImGui.Button("Yes, Extract All"))
            {
                ExtractAllFrames(videoDataset);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        if (_isExtracting)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Extracting frames...");
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Video Information");

        ImGui.Text($"Codec: {videoDataset.Codec}");
        ImGui.Text($"Format: {videoDataset.Format}");
        ImGui.Text($"Bitrate: {videoDataset.BitRate / 1000} kbps");

        if (videoDataset.HasAudioTrack)
        {
            ImGui.Spacing();
            ImGui.SeparatorText("Audio Track");
            ImGui.Text($"Channels: {videoDataset.AudioChannels}");
            ImGui.Text($"Sample Rate: {videoDataset.AudioSampleRate} Hz");
            ImGui.Text($"Codec: {videoDataset.AudioCodec}");
        }
    }

    private async void ExtractFrame(VideoDataset dataset, double time)
    {
        if (dataset == null || _isExtracting) return;

        try
        {
            _isExtracting = true;

            var frameData = await dataset.ExtractFrameAsync(time);
            if (frameData != null)
            {
                // Save frame to file
                var timestamp = time.ToString("F2").Replace(".", "_");
                var outputPath = Path.Combine(
                    Path.GetDirectoryName(dataset.FilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"{Path.GetFileNameWithoutExtension(dataset.FilePath)}_frame_{timestamp}.png"
                );

                ImageExporter.SaveImage(frameData, dataset.Width, dataset.Height, outputPath);
                Logger.Log($"[VideoTools] Extracted frame to: {outputPath}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[VideoTools] Failed to extract frame: {ex.Message}");
        }
        finally
        {
            _isExtracting = false;
        }
    }

    private async void ExtractAllFrames(VideoDataset dataset)
    {
        if (dataset == null || _isExtracting) return;

        try
        {
            _isExtracting = true;

            var outputDir = Path.Combine(
                Path.GetDirectoryName(dataset.FilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"{Path.GetFileNameWithoutExtension(dataset.FilePath)}_frames"
            );

            Directory.CreateDirectory(outputDir);

            // Extract frames at intervals (e.g., every 1 second or every 30th frame)
            var frameInterval = Math.Max(1, (int)dataset.FrameRate); // 1 frame per second
            var framesToExtract = dataset.TotalFrames / frameInterval;

            await Task.Run(async () =>
            {
                for (int i = 0; i < framesToExtract && _isExtracting; i++)
                {
                    var frameTime = i * (1.0 / frameInterval) * dataset.DurationSeconds / framesToExtract;
                    var frameData = await dataset.ExtractFrameAsync(frameTime);

                    if (frameData != null)
                    {
                        var outputPath = Path.Combine(outputDir, $"frame_{i:D5}.png");
                        ImageExporter.SaveImage(frameData, dataset.Width, dataset.Height, outputPath);
                    }
                }
            });

            Logger.Log($"[VideoTools] Extracted {framesToExtract} frames to: {outputDir}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[VideoTools] Failed to extract frames: {ex.Message}");
        }
        finally
        {
            _isExtracting = false;
        }
    }
}
