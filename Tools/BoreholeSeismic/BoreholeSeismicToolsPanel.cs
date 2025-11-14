// GeoscientistToolkit/Tools/BoreholeSeismic/BoreholeSeismicToolsPanel.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Tools.BoreholeSeismic;

/// <summary>
/// UI panel for borehole-seismic integration tools
/// </summary>
public class BoreholeSeismicToolsPanel
{
    private float _dominantFrequency = 30.0f;
    private float _sampleRateMs = 2.0f;
    private int _selectedBoreholeIndex = -1;
    private int _selectedSeismicIndex = -1;
    private int _selectedTraceIndex = 0;
    private string _newBoreholeName = "Seismic_Well";
    private float _newBoreholeX = 0;
    private float _newBoreholeY = 0;
    private float _newBoreholeElevation = 0;

    // Well tie results
    private float[] _correlationResults;
    private int _bestTraceIndex = -1;
    private float _bestCorrelation = 0;

    public void DrawBoreholeToSeismic()
    {
        ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "Create Synthetic Seismic from Borehole");
        ImGui.Separator();
        ImGui.Spacing();

        // Select borehole
        var boreholes = ProjectManager.Instance.LoadedDatasets
            .OfType<BoreholeDataset>()
            .ToList();

        if (boreholes.Count == 0)
        {
            ImGui.TextDisabled("No boreholes available. Load or create a borehole first.");
            return;
        }

        ImGui.Text("Select Borehole:");
        var boreholeNames = boreholes.Select(b => b.Name).ToArray();
        if (_selectedBoreholeIndex < 0 || _selectedBoreholeIndex >= boreholes.Count)
            _selectedBoreholeIndex = 0;

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##Borehole", ref _selectedBoreholeIndex, boreholeNames, boreholeNames.Length);

        ImGui.Spacing();
        ImGui.Text("Wavelet Parameters:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderFloat("Dominant Frequency (Hz)", ref _dominantFrequency, 10f, 80f, "%.1f Hz");

        ImGui.SetNextItemWidth(-1);
        ImGui.SliderFloat("Sample Rate (ms)", ref _sampleRateMs, 0.5f, 4.0f, "%.1f ms");

        ImGui.Spacing();
        if (ImGui.Button("Generate Synthetic Seismic", new Vector2(-1, 0)))
        {
            GenerateSyntheticSeismic(boreholes[_selectedBoreholeIndex]);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Creates a synthetic seismic trace from borehole acoustic impedance data");
        }
    }

    public void DrawSeismicToBorehole()
    {
        ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.5f, 1.0f), "Create Pseudo-Borehole from Seismic");
        ImGui.Separator();
        ImGui.Spacing();

        // Select seismic dataset
        var seismicDatasets = ProjectManager.Instance.LoadedDatasets
            .OfType<SeismicDataset>()
            .ToList();

        if (seismicDatasets.Count == 0)
        {
            ImGui.TextDisabled("No seismic datasets available. Load a SEG-Y file first.");
            return;
        }

        ImGui.Text("Select Seismic Dataset:");
        var seismicNames = seismicDatasets.Select(s => s.Name).ToArray();
        if (_selectedSeismicIndex < 0 || _selectedSeismicIndex >= seismicDatasets.Count)
            _selectedSeismicIndex = 0;

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##Seismic", ref _selectedSeismicIndex, seismicNames, seismicNames.Length);

        var selectedSeismic = seismicDatasets[_selectedSeismicIndex];
        var maxTrace = selectedSeismic.GetTraceCount() - 1;

        ImGui.Spacing();
        ImGui.Text("Trace Selection:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderInt("Trace Index", ref _selectedTraceIndex, 0, maxTrace);

        ImGui.Spacing();
        ImGui.Text("Borehole Properties:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("Name", ref _newBoreholeName, 100);

        ImGui.SetNextItemWidth(-1);
        ImGui.InputFloat("X Coordinate", ref _newBoreholeX, 1f, 10f, "%.2f");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputFloat("Y Coordinate", ref _newBoreholeY, 1f, 10f, "%.2f");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputFloat("Elevation (m)", ref _newBoreholeElevation, 1f, 10f, "%.2f");

        ImGui.Spacing();
        if (ImGui.Button("Create Borehole from Seismic Trace", new Vector2(-1, 0)))
        {
            CreateBoreholeFromSeismic(selectedSeismic);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Extracts a pseudo-borehole from the selected seismic trace\nIncludes amplitude and envelope curves");
        }
    }

    public void DrawWellTie()
    {
        ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f), "Well-to-Seismic Tie");
        ImGui.Separator();
        ImGui.Spacing();

        var boreholes = ProjectManager.Instance.LoadedDatasets
            .OfType<BoreholeDataset>()
            .ToList();

        var seismicDatasets = ProjectManager.Instance.LoadedDatasets
            .OfType<SeismicDataset>()
            .ToList();

        if (boreholes.Count == 0 || seismicDatasets.Count == 0)
        {
            ImGui.TextDisabled("Need both borehole and seismic data loaded.");
            return;
        }

        ImGui.Text("Select Borehole:");
        var boreholeNames = boreholes.Select(b => b.Name).ToArray();
        if (_selectedBoreholeIndex < 0 || _selectedBoreholeIndex >= boreholes.Count)
            _selectedBoreholeIndex = 0;

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##WellTieBorehole", ref _selectedBoreholeIndex, boreholeNames, boreholeNames.Length);

        ImGui.Spacing();
        ImGui.Text("Select Seismic:");
        var seismicNames = seismicDatasets.Select(s => s.Name).ToArray();
        if (_selectedSeismicIndex < 0 || _selectedSeismicIndex >= seismicDatasets.Count)
            _selectedSeismicIndex = 0;

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##WellTieSeismic", ref _selectedSeismicIndex, seismicNames, seismicNames.Length);

        ImGui.Spacing();
        ImGui.Text("Correlation Parameters:");
        ImGui.SetNextItemWidth(-1);
        ImGui.SliderFloat("Dominant Frequency", ref _dominantFrequency, 10f, 80f, "%.1f Hz");

        ImGui.Spacing();
        if (ImGui.Button("Find Best Trace Match", new Vector2(-1, 0)))
        {
            TieWellToSeismic(boreholes[_selectedBoreholeIndex], seismicDatasets[_selectedSeismicIndex]);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Correlates synthetic seismic from borehole with actual seismic traces\nFinds the trace with best correlation");
        }

        // Show correlation results if available
        if (_correlationResults != null && _correlationResults.Length > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f), "Correlation Results");

            ImGui.Text($"Best match: Trace {_bestTraceIndex}");
            ImGui.Text($"Correlation: {_bestCorrelation:F3}");

            ImGui.Spacing();
            if (ImGui.BeginChild("CorrelationPlot", new Vector2(-1, 200), ImGuiChildFlags.Border))
            {
                DrawCorrelationPlot(_correlationResults);
            }
            ImGui.EndChild();

            if (ImGui.Button("Clear Results", new Vector2(-1, 0)))
            {
                _correlationResults = null;
                _bestTraceIndex = -1;
                _bestCorrelation = 0;
            }
        }
    }

    private void GenerateSyntheticSeismic(BoreholeDataset borehole)
    {
        try
        {
            var synthetic = BoreholeSeismicIntegration.GenerateSyntheticSeismic(
                borehole, _dominantFrequency, _sampleRateMs);

            if (synthetic.Length == 0)
            {
                Logger.LogError("[BoreholeSeismicTools] Failed to generate synthetic seismic - check borehole has AI/Vp/Density data");
                return;
            }

            // Create a new parameter track in the borehole for the synthetic
            var syntheticTrack = new ParameterTrack
            {
                Name = "Synthetic Seismic",
                Unit = "amplitude",
                Color = new Vector4(1, 0, 1, 1),
                IsVisible = true
            };

            // Map synthetic samples to depth
            var depthInterval = borehole.TotalDepth / synthetic.Length;
            for (int i = 0; i < synthetic.Length; i++)
            {
                syntheticTrack.Points.Add(new ParameterPoint
                {
                    Depth = i * depthInterval,
                    Value = synthetic[i]
                });
            }

            borehole.ParameterTracks["Synthetic Seismic"] = syntheticTrack;
            ProjectManager.Instance.NotifyDatasetDataChanged(borehole);

            Logger.Log($"[BoreholeSeismicTools] Generated {synthetic.Length} synthetic seismic samples");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[BoreholeSeismicTools] Error generating synthetic: {ex.Message}");
        }
    }

    private void CreateBoreholeFromSeismic(SeismicDataset seismic)
    {
        try
        {
            var borehole = BoreholeSeismicIntegration.CreateBoreholeFromSeismic(
                seismic, _selectedTraceIndex, _newBoreholeName,
                _newBoreholeX, _newBoreholeY, _newBoreholeElevation);

            if (borehole == null)
            {
                Logger.LogError("[BoreholeSeismicTools] Failed to create borehole from seismic");
                return;
            }

            ProjectManager.Instance.AddDataset(borehole);
            Logger.Log($"[BoreholeSeismicTools] Created pseudo-borehole '{_newBoreholeName}' from trace {_selectedTraceIndex}");

            // Reset for next use
            _newBoreholeName = $"Seismic_Well_{DateTime.Now:HHmmss}";
        }
        catch (Exception ex)
        {
            Logger.LogError($"[BoreholeSeismicTools] Error creating borehole: {ex.Message}");
        }
    }

    private void TieWellToSeismic(BoreholeDataset borehole, SeismicDataset seismic)
    {
        try
        {
            var synthetic = BoreholeSeismicIntegration.GenerateSyntheticSeismic(borehole, _dominantFrequency);
            if (synthetic.Length == 0)
            {
                Logger.LogError("[BoreholeSeismicTools] Failed to generate synthetic seismic");
                return;
            }

            // Calculate correlation for all traces
            _correlationResults = new float[seismic.GetTraceCount()];
            _bestCorrelation = float.MinValue;
            _bestTraceIndex = -1;

            for (int i = 0; i < seismic.GetTraceCount(); i++)
            {
                var trace = seismic.SegyData.Traces[i];
                _correlationResults[i] = CalculateCorrelation(synthetic, trace.Samples);

                if (_correlationResults[i] > _bestCorrelation)
                {
                    _bestCorrelation = _correlationResults[i];
                    _bestTraceIndex = i;
                }
            }

            Logger.Log($"[BoreholeSeismicTools] Best correlation: {_bestCorrelation:F3} at trace {_bestTraceIndex}");
            _selectedTraceIndex = _bestTraceIndex;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[BoreholeSeismicTools] Error in well tie: {ex.Message}");
        }
    }

    private float CalculateCorrelation(float[] signal1, float[] signal2)
    {
        var minLength = Math.Min(signal1.Length, signal2.Length);
        float sum1 = 0, sum2 = 0, sum12 = 0, sum1Sq = 0, sum2Sq = 0;

        for (int i = 0; i < minLength; i++)
        {
            sum1 += signal1[i];
            sum2 += signal2[i];
            sum12 += signal1[i] * signal2[i];
            sum1Sq += signal1[i] * signal1[i];
            sum2Sq += signal2[i] * signal2[i];
        }

        var n = minLength;
        var numerator = n * sum12 - sum1 * sum2;
        var denominator = Math.Sqrt((n * sum1Sq - sum1 * sum1) * (n * sum2Sq - sum2 * sum2));

        if (Math.Abs(denominator) < 1e-10)
            return 0;

        return (float)(numerator / denominator);
    }

    private void DrawCorrelationPlot(float[] correlations)
    {
        var dl = ImGui.GetWindowDrawList();
        var plotPos = ImGui.GetCursorScreenPos();
        var plotSize = ImGui.GetContentRegionAvail();

        if (plotSize.X < 10 || plotSize.Y < 10) return;

        // Background
        dl.AddRectFilled(plotPos, plotPos + plotSize, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1.0f)));

        // Find min/max for scaling
        var minCorr = correlations.Min();
        var maxCorr = correlations.Max();
        var range = maxCorr - minCorr;
        if (range < 0.001f) range = 1.0f;

        // Draw correlation line
        for (int i = 0; i < correlations.Length - 1; i++)
        {
            var x1 = plotPos.X + (i / (float)(correlations.Length - 1)) * plotSize.X;
            var y1 = plotPos.Y + plotSize.Y - ((correlations[i] - minCorr) / range) * plotSize.Y;
            var x2 = plotPos.X + ((i + 1) / (float)(correlations.Length - 1)) * plotSize.X;
            var y2 = plotPos.Y + plotSize.Y - ((correlations[i + 1] - minCorr) / range) * plotSize.Y;

            dl.AddLine(new Vector2(x1, y1), new Vector2(x2, y2),
                ImGui.GetColorU32(new Vector4(0.3f, 0.8f, 1.0f, 1.0f)), 2.0f);
        }

        // Mark best correlation
        if (_bestTraceIndex >= 0 && _bestTraceIndex < correlations.Length)
        {
            var xBest = plotPos.X + (_bestTraceIndex / (float)(correlations.Length - 1)) * plotSize.X;
            var yBest = plotPos.Y + plotSize.Y - ((correlations[_bestTraceIndex] - minCorr) / range) * plotSize.Y;

            dl.AddCircleFilled(new Vector2(xBest, yBest), 5.0f,
                ImGui.GetColorU32(new Vector4(1.0f, 0.0f, 0.0f, 1.0f)));
        }

        // Border
        dl.AddRect(plotPos, plotPos + plotSize, ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f)));

        // Labels
        dl.AddText(new Vector2(plotPos.X + 5, plotPos.Y + 5),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), $"Max: {maxCorr:F3}");
        dl.AddText(new Vector2(plotPos.X + 5, plotPos.Y + plotSize.Y - 20),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), $"Min: {minCorr:F3}");

        ImGui.Dummy(plotSize);
    }
}
