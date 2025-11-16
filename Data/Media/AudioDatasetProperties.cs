// GeoscientistToolkit/Data/Media/AudioDatasetProperties.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Media;

/// <summary>
/// Properties renderer for audio datasets
/// </summary>
public class AudioDatasetProperties : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not AudioDataset audioDataset)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Invalid dataset type");
            return;
        }

        ImGui.SeparatorText("Audio Properties");

        // Basic file information
        ImGui.Text("File Information:");
        ImGui.Indent();

        var fileName = Path.GetFileName(audioDataset.FilePath);
        ImGui.Text($"Name: {fileName}");

        if (!audioDataset.IsMissing)
        {
            var fileInfo = new FileInfo(audioDataset.FilePath);
            ImGui.Text($"Size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
            ImGui.Text($"Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "File not found");
        }

        ImGui.Text($"Path: {audioDataset.FilePath}");

        ImGui.Unindent();
        ImGui.Spacing();

        // Audio stream properties
        ImGui.SeparatorText("Audio Stream");
        ImGui.Indent();

        ImGui.Text($"Sample Rate: {audioDataset.SampleRate:N0} Hz ({audioDataset.SampleRate / 1000.0:F1} kHz)");
        ImGui.Text($"Channels: {audioDataset.Channels} ({GetChannelLayout(audioDataset.Channels)})");
        ImGui.Text($"Bit Depth: {audioDataset.BitsPerSample} bits");
        ImGui.Text($"Duration: {FormatDuration(audioDataset.DurationSeconds)}");
        ImGui.Text($"Total Samples: {audioDataset.TotalSamples:N0}");
        ImGui.Text($"Encoding: {audioDataset.Encoding}");
        ImGui.Text($"Format: {audioDataset.Format}");
        ImGui.Text($"Bitrate: {audioDataset.BitRate / 1000.0:F0} kbps");

        ImGui.Unindent();
        ImGui.Spacing();

        // Technical specifications
        ImGui.SeparatorText("Technical Specifications");
        ImGui.Indent();

        var nyquistFreq = audioDataset.SampleRate / 2.0;
        ImGui.Text($"Nyquist Frequency: {nyquistFreq / 1000.0:F1} kHz");

        var dynamicRange = audioDataset.BitsPerSample * 6.02; // Theoretical dynamic range in dB
        ImGui.Text($"Dynamic Range: ~{dynamicRange:F1} dB");

        var bytesPerSample = audioDataset.BitsPerSample / 8;
        var bytesPerSecond = audioDataset.SampleRate * audioDataset.Channels * bytesPerSample;
        ImGui.Text($"Data Rate: {bytesPerSecond / 1024.0:F1} KB/s");

        ImGui.Unindent();
        ImGui.Spacing();

        // Waveform information
        if (audioDataset.WaveformData != null)
        {
            ImGui.SeparatorText("Waveform Data");
            ImGui.Indent();

            ImGui.Text($"Waveform Samples: {audioDataset.WaveformData.Length:N0}");
            ImGui.Text($"Sample Rate: {audioDataset.WaveformSampleRate} samples/sec");

            var compressionRatio = (double)audioDataset.TotalSamples / audioDataset.WaveformData.Length;
            ImGui.Text($"Compression Ratio: 1:{compressionRatio:F0}");

            ImGui.Unindent();
            ImGui.Spacing();
        }

        // Metadata
        if (audioDataset.AudioMetadata != null && audioDataset.AudioMetadata.Count > 0)
        {
            ImGui.SeparatorText("Metadata");
            ImGui.Indent();

            foreach (var kvp in audioDataset.AudioMetadata)
            {
                ImGui.Text($"{kvp.Key}: {kvp.Value}");
            }

            ImGui.Unindent();
            ImGui.Spacing();
        }

        // Dataset metadata
        DrawDatasetMetadata(audioDataset);
    }

    private void DrawDatasetMetadata(AudioDataset dataset)
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

    private string GetChannelLayout(int channels)
    {
        return channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            3 => "2.1",
            4 => "Quad",
            5 => "4.1",
            6 => "5.1 Surround",
            7 => "6.1 Surround",
            8 => "7.1 Surround",
            _ => $"{channels} channels"
        };
    }

    private string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
