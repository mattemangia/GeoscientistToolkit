// GeoscientistToolkit/Data/Media/VideoDatasetProperties.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Media;

/// <summary>
/// Properties renderer for video datasets
/// </summary>
public class VideoDatasetProperties : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not VideoDataset videoDataset)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Invalid dataset type");
            return;
        }

        ImGui.SeparatorText("Video Properties");

        // Basic file information
        ImGui.Text("File Information:");
        ImGui.Indent();

        var fileName = Path.GetFileName(videoDataset.FilePath);
        ImGui.Text($"Name: {fileName}");

        if (!videoDataset.IsMissing)
        {
            var fileInfo = new FileInfo(videoDataset.FilePath);
            ImGui.Text($"Size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
            ImGui.Text($"Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "File not found");
        }

        ImGui.Text($"Path: {videoDataset.FilePath}");

        ImGui.Unindent();
        ImGui.Spacing();

        // Video properties
        ImGui.SeparatorText("Video Stream");
        ImGui.Indent();

        ImGui.Text($"Resolution: {videoDataset.Width} x {videoDataset.Height}");
        ImGui.Text($"Aspect Ratio: {GetAspectRatio(videoDataset.Width, videoDataset.Height)}");
        ImGui.Text($"Duration: {FormatDuration(videoDataset.DurationSeconds)}");
        ImGui.Text($"Frame Rate: {videoDataset.FrameRate:F2} fps");
        ImGui.Text($"Total Frames: {videoDataset.TotalFrames:N0}");
        ImGui.Text($"Codec: {videoDataset.Codec}");
        ImGui.Text($"Format: {videoDataset.Format}");
        ImGui.Text($"Bitrate: {videoDataset.BitRate / 1000.0:F0} kbps");

        ImGui.Unindent();
        ImGui.Spacing();

        // Audio track information
        if (videoDataset.HasAudioTrack)
        {
            ImGui.SeparatorText("Audio Stream");
            ImGui.Indent();

            ImGui.Text($"Codec: {videoDataset.AudioCodec}");
            ImGui.Text($"Channels: {videoDataset.AudioChannels} ({GetChannelLayout(videoDataset.AudioChannels)})");
            ImGui.Text($"Sample Rate: {videoDataset.AudioSampleRate / 1000.0:F1} kHz");

            ImGui.Unindent();
            ImGui.Spacing();
        }

        // Metadata
        if (videoDataset.VideoMetadata != null && videoDataset.VideoMetadata.Count > 0)
        {
            ImGui.SeparatorText("Metadata");
            ImGui.Indent();

            foreach (var kvp in videoDataset.VideoMetadata)
            {
                ImGui.Text($"{kvp.Key}: {kvp.Value}");
            }

            ImGui.Unindent();
            ImGui.Spacing();
        }

        // Dataset metadata
        DrawDatasetMetadata(videoDataset);
    }

    private void DrawDatasetMetadata(VideoDataset dataset)
    {
        if (dataset.DatasetMetadata == null) return;

        ImGui.SeparatorText("Dataset Metadata");
        ImGui.Indent();

        var meta = dataset.DatasetMetadata;

        // Sample name
        var sampleName = meta.SampleName ?? "";
        ImGui.Text($"Sample Name: {sampleName}");

        // Location
        if (!string.IsNullOrEmpty(meta.LocationName))
        {
            ImGui.Text($"Location: {meta.LocationName}");
        }

        // Coordinates
        if (meta.Latitude.HasValue && meta.Longitude.HasValue)
        {
            ImGui.Text($"Coordinates: {meta.Latitude:F6}, {meta.Longitude:F6}");
        }

        // Collection date
        if (meta.CollectionDate.HasValue)
        {
            ImGui.Text($"Collection Date: {meta.CollectionDate.Value:yyyy-MM-dd}");
        }

        // Collector
        if (!string.IsNullOrEmpty(meta.Collector))
        {
            ImGui.Text($"Collector: {meta.Collector}");
        }

        // Notes
        if (!string.IsNullOrEmpty(meta.Notes))
        {
            ImGui.Text("Notes:");
            ImGui.TextWrapped(meta.Notes);
        }

        ImGui.Unindent();
    }

    private string GetAspectRatio(int width, int height)
    {
        if (width == 0 || height == 0) return "Unknown";

        var gcd = GCD(width, height);
        var ratioW = width / gcd;
        var ratioH = height / gcd;

        // Common aspect ratios
        if (ratioW == 16 && ratioH == 9) return "16:9 (Widescreen)";
        if (ratioW == 4 && ratioH == 3) return "4:3 (Standard)";
        if (ratioW == 21 && ratioH == 9) return "21:9 (Ultrawide)";
        if (ratioW == 1 && ratioH == 1) return "1:1 (Square)";

        return $"{ratioW}:{ratioH}";
    }

    private string GetChannelLayout(int channels)
    {
        return channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            3 => "2.1",
            4 => "Quad",
            5 => "4.1",
            6 => "5.1",
            7 => "6.1",
            8 => "7.1",
            _ => $"{channels} channels"
        };
    }

    private string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private int GCD(int a, int b)
    {
        while (b != 0)
        {
            var temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }
}
