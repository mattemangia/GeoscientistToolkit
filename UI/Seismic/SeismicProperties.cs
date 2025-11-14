// GeoscientistToolkit/UI/Seismic/SeismicProperties.cs

using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Seismic;

/// <summary>
/// Properties panel for seismic datasets
/// </summary>
public class SeismicProperties : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not SeismicDataset seismicDataset)
        {
            ImGui.TextDisabled("Invalid dataset type");
            return;
        }

        // Basic dataset info
        ImGui.SeparatorText("Dataset Information");
        ImGui.Text($"Name: {seismicDataset.Name}");
        ImGui.Text($"Type: Seismic (SEG-Y)");
        ImGui.Text($"File: {Path.GetFileName(seismicDataset.FilePath)}");
        ImGui.Text($"Size: {FormatBytes(seismicDataset.GetSizeInBytes())}");
        ImGui.Spacing();

        // SEG-Y specific information
        if (seismicDataset.SegyData?.Header != null)
        {
            var header = seismicDataset.SegyData.Header;

            ImGui.SeparatorText("SEG-Y Format Information");
            ImGui.Text($"SEG-Y Revision: {header.SegyRevision}");
            ImGui.Text($"Sample Format: {GetSampleFormatName(header.SampleFormat)}");
            ImGui.Text($"Job ID: {header.JobId}");
            ImGui.Text($"Line Number: {header.LineNumber}");
            ImGui.Text($"Reel Number: {header.ReelNumber}");
            ImGui.Spacing();

            // Data dimensions
            ImGui.SeparatorText("Data Dimensions");
            ImGui.Text($"Number of Traces: {seismicDataset.GetTraceCount():N0}");
            ImGui.Text($"Samples per Trace: {seismicDataset.GetSampleCount():N0}");
            ImGui.Text($"Sample Interval: {seismicDataset.GetSampleIntervalMs():F3} ms");
            ImGui.Text($"Total Duration: {seismicDataset.GetDurationSeconds():F2} seconds");

            var measurementSystem = header.MeasurementSystem == 1 ? "Meters" :
                                  header.MeasurementSystem == 2 ? "Feet" : "Unknown";
            ImGui.Text($"Measurement System: {measurementSystem}");
            ImGui.Spacing();

            // Amplitude statistics
            ImGui.SeparatorText("Amplitude Statistics");
            var (min, max, rms) = seismicDataset.GetAmplitudeStatistics();
            ImGui.Text($"Minimum: {min:E3}");
            ImGui.Text($"Maximum: {max:E3}");
            ImGui.Text($"RMS: {rms:E3}");
            ImGui.Text($"Range: {(max - min):E3}");
            ImGui.Spacing();

            // Survey metadata
            ImGui.SeparatorText("Survey Information");
            ImGui.Text($"Survey Name: {(string.IsNullOrEmpty(seismicDataset.SurveyName) ? "N/A" : seismicDataset.SurveyName)}");
            ImGui.Text($"Line Number: {(string.IsNullOrEmpty(seismicDataset.LineNumber) ? "N/A" : seismicDataset.LineNumber)}");
            ImGui.Text($"Data Type: {seismicDataset.DataType}");
            ImGui.Text($"Is Stack: {(seismicDataset.IsStack ? "Yes" : "No")}");
            ImGui.Text($"Is Migrated: {(seismicDataset.IsMigrated ? "Yes" : "No")}");
            ImGui.Spacing();

            // Processing history
            if (!string.IsNullOrEmpty(seismicDataset.ProcessingHistory))
            {
                ImGui.SeparatorText("Processing History");
                ImGui.TextWrapped(seismicDataset.ProcessingHistory);
                ImGui.Spacing();
            }

            // Textual header
            if (!string.IsNullOrEmpty(header.TextualHeader))
            {
                ImGui.SeparatorText("SEG-Y Textual Header");

                if (ImGui.BeginChild("TextualHeader", new System.Numerics.Vector2(0, 200), ImGuiChildFlags.Border))
                {
                    ImGui.TextWrapped(header.TextualHeader);
                }
                ImGui.EndChild();
                ImGui.Spacing();
            }

            // Line packages
            ImGui.SeparatorText("Line Packages");
            ImGui.Text($"Total Packages: {seismicDataset.LinePackages.Count}");

            if (seismicDataset.LinePackages.Count > 0)
            {
                if (ImGui.BeginChild("PackagesList", new System.Numerics.Vector2(0, 150), ImGuiChildFlags.Border))
                {
                    foreach (var package in seismicDataset.LinePackages)
                    {
                        ImGui.BulletText($"{package.Name}: Traces {package.StartTrace}-{package.EndTrace} ({package.TraceCount} traces)");
                    }
                }
                ImGui.EndChild();
            }
            ImGui.Spacing();

            // Display settings
            ImGui.SeparatorText("Display Settings");
            ImGui.Text($"Gain: {seismicDataset.GainValue:F2}");
            ImGui.Text($"Show Wiggle Trace: {(seismicDataset.ShowWiggleTrace ? "Yes" : "No")}");
            ImGui.Text($"Show Variable Area: {(seismicDataset.ShowVariableArea ? "Yes" : "No")}");
            ImGui.Text($"Show Color Map: {(seismicDataset.ShowColorMap ? "Yes" : "No")}");
        }
        else
        {
            ImGui.TextDisabled("No SEG-Y data loaded");
        }
    }

    private string GetSampleFormatName(int format)
    {
        return format switch
        {
            1 => "4-byte IBM floating point",
            2 => "4-byte two's complement integer",
            3 => "2-byte two's complement integer",
            5 => "4-byte IEEE floating point",
            6 => "8-byte IEEE floating point",
            8 => "1-byte two's complement integer",
            _ => $"Unknown ({format})"
        };
    }

    private string FormatBytes(long bytes)
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
