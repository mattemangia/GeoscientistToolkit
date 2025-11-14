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
                syntheticTrack.Values.Add(new ParameterValue
                {
                    Depth = i * depthInterval,
                    Value = synthetic[i]
                });
            }

            borehole.ParameterTracks["Synthetic Seismic"] = syntheticTrack;
            ProjectManager.Instance.MarkDatasetChanged(borehole);

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
            var bestTrace = BoreholeSeismicIntegration.TieWellToSeismic(borehole, seismic, _dominantFrequency);

            if (bestTrace < 0)
            {
                Logger.LogError("[BoreholeSeismicTools] Failed to tie well to seismic");
                return;
            }

            Logger.Log($"[BoreholeSeismicTools] Best correlation found at trace {bestTrace}");
            _selectedTraceIndex = bestTrace;

            // TODO: Could add visualization of the correlation results
        }
        catch (Exception ex)
        {
            Logger.LogError($"[BoreholeSeismicTools] Error in well tie: {ex.Message}");
        }
    }
}
