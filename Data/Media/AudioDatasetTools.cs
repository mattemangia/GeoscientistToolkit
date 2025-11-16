// GeoscientistToolkit/Data/Media/AudioDatasetTools.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Media;

/// <summary>
/// Tools panel for audio datasets with analysis and export options
/// </summary>
public class AudioDatasetTools : IDatasetTools
{
    private double _trimStart = 0.0;
    private double _trimEnd = 0.0;
    private bool _normalizeTo0dB = false;

    public void Draw(Dataset dataset)
    {
        if (dataset is not AudioDataset audioDataset)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Invalid dataset type");
            return;
        }

        if (audioDataset.IsMissing)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Audio file not found");
            return;
        }

        ImGui.SeparatorText("Audio Analysis");

        // Basic audio statistics
        if (audioDataset.WaveformData != null && audioDataset.WaveformData.Length > 0)
        {
            var maxAmplitude = audioDataset.WaveformData.Max();
            var avgAmplitude = audioDataset.WaveformData.Average();
            var rms = Math.Sqrt(audioDataset.WaveformData.Select(x => x * x).Average());

            ImGui.Text($"Peak Amplitude: {maxAmplitude:F4}");
            ImGui.Text($"Average Amplitude: {avgAmplitude:F4}");
            ImGui.Text($"RMS Level: {rms:F4}");

            var peakDb = 20 * Math.Log10(maxAmplitude);
            var rmsDb = 20 * Math.Log10(rms);
            ImGui.Text($"Peak dB: {peakDb:F2} dB");
            ImGui.Text($"RMS dB: {rmsDb:F2} dB");
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Export Options");

        ImGui.Text("Trim Audio:");
        ImGui.SetNextItemWidth(200);
        float tempStart = (float)_trimStart;
        if (ImGui.SliderFloat("Start##TrimStart", ref tempStart, 0f, (float)audioDataset.DurationSeconds, "%.2fs"))
        {
            _trimStart = Math.Clamp(tempStart, 0.0, audioDataset.DurationSeconds);
            _trimEnd = Math.Max(_trimEnd, _trimStart);
        }

        ImGui.SetNextItemWidth(200);
        float tempEnd = (float)_trimEnd;
        if (ImGui.SliderFloat("End##TrimEnd", ref tempEnd, 0f, (float)audioDataset.DurationSeconds, "%.2fs"))
        {
            _trimEnd = Math.Clamp(tempEnd, _trimStart, audioDataset.DurationSeconds);
        }

        ImGui.Checkbox("Normalize to 0 dB", ref _normalizeTo0dB);

        if (ImGui.Button("Export Segment"))
        {
            ExportAudioSegment(audioDataset);
        }

        ImGui.SameLine();
        if (ImGui.Button("Export Waveform Image"))
        {
            ExportWaveformImage(audioDataset);
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Audio Information");

        ImGui.Text($"Encoding: {audioDataset.Encoding}");
        ImGui.Text($"Format: {audioDataset.Format}");
        ImGui.Text($"Bitrate: {audioDataset.BitRate / 1000} kbps");
        ImGui.Text($"Total Samples: {audioDataset.TotalSamples:N0}");

        var nyquistFreq = audioDataset.SampleRate / 2.0;
        ImGui.Text($"Nyquist Frequency: {nyquistFreq / 1000.0:F1} kHz");
    }

    private void ExportAudioSegment(AudioDataset dataset)
    {
        if (dataset == null) return;

        try
        {
            var outputPath = Path.Combine(
                Path.GetDirectoryName(dataset.FilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"{Path.GetFileNameWithoutExtension(dataset.FilePath)}_segment_{_trimStart:F2}_{_trimEnd:F2}.wav"
            );

            // Read samples for the segment
            var startSample = (int)(_trimStart * dataset.SampleRate * dataset.Channels);
            var endSample = (int)(_trimEnd * dataset.SampleRate * dataset.Channels);
            var sampleCount = endSample - startSample;

            if (sampleCount > 0)
            {
                var samples = dataset.ReadSamples(startSample, sampleCount);

                if (samples != null)
                {
                    // Apply normalization if requested
                    if (_normalizeTo0dB && samples.Length > 0)
                    {
                        var maxVal = samples.Max(Math.Abs);
                        if (maxVal > 0)
                        {
                            var scale = 1.0f / maxVal;
                            for (int i = 0; i < samples.Length; i++)
                            {
                                samples[i] *= scale;
                            }
                        }
                    }

                    // Save as WAV file
                    using var writer = new NAudio.Wave.WaveFileWriter(
                        outputPath,
                        new NAudio.Wave.WaveFormat(dataset.SampleRate, dataset.BitsPerSample, dataset.Channels)
                    );

                    writer.WriteSamples(samples, 0, samples.Length);

                    Logger.Log($"[AudioTools] Exported audio segment to: {outputPath}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AudioTools] Failed to export audio segment: {ex.Message}");
        }
    }

    private void ExportWaveformImage(AudioDataset dataset)
    {
        if (dataset == null || dataset.WaveformData == null) return;

        try
        {
            var outputPath = Path.Combine(
                Path.GetDirectoryName(dataset.FilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"{Path.GetFileNameWithoutExtension(dataset.FilePath)}_waveform.png"
            );

            // Create waveform image (simplified version)
            var width = Math.Min(4096, dataset.WaveformData.Length);
            var height = 512;
            var imageData = new byte[width * height * 4]; // RGBA

            // Fill background
            for (int i = 0; i < imageData.Length; i += 4)
            {
                imageData[i] = 26;     // R
                imageData[i + 1] = 26; // G
                imageData[i + 2] = 26; // B
                imageData[i + 3] = 255; // A
            }

            // Draw waveform
            var centerY = height / 2;
            var maxAmplitude = height * 0.45f;

            for (int x = 0; x < width; x++)
            {
                var dataIndex = (int)((float)x / width * dataset.WaveformData.Length);
                if (dataIndex >= dataset.WaveformData.Length) break;

                var amplitude = dataset.WaveformData[dataIndex] * maxAmplitude;
                var y1 = (int)(centerY - amplitude);
                var y2 = (int)(centerY + amplitude);

                y1 = Math.Clamp(y1, 0, height - 1);
                y2 = Math.Clamp(y2, 0, height - 1);

                for (int y = y1; y <= y2; y++)
                {
                    var pixelIndex = (y * width + x) * 4;
                    imageData[pixelIndex] = 51;       // R
                    imageData[pixelIndex + 1] = 204; // G
                    imageData[pixelIndex + 2] = 255; // B
                    imageData[pixelIndex + 3] = 255; // A
                }
            }

            ImageExporter.SaveImage(imageData, width, height, outputPath);
            Logger.Log($"[AudioTools] Exported waveform image to: {outputPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AudioTools] Failed to export waveform image: {ex.Message}");
        }
    }
}
