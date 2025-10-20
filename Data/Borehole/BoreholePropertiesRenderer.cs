// GeoscientistToolkit/UI/Borehole/BoreholePropertiesRenderer.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Borehole;

/// <summary>
///     Properties renderer for borehole datasets
/// </summary>
public class BoreholePropertiesRenderer : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not BoreholeDataset borehole)
            return;

        ImGui.Text("Borehole/Well Log");
        ImGui.Separator();

        // Well information
        if (ImGui.CollapsingHeader("Well Information", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Well Name: {borehole.WellName}");
            ImGui.Text($"Field: {borehole.Field}");
            ImGui.Text($"Total Depth: {borehole.TotalDepth:F2} m");
            ImGui.Text($"Well Diameter: {borehole.WellDiameter * 1000:F1} mm");
            ImGui.Text($"Elevation: {borehole.Elevation:F2} m ASL");

            if (borehole.SurfaceCoordinates != Vector2.Zero)
            {
                ImGui.Text("Surface Coordinates:");
                ImGui.Indent();
                ImGui.Text($"X: {borehole.SurfaceCoordinates.X:F2}");
                ImGui.Text($"Y: {borehole.SurfaceCoordinates.Y:F2}");
                ImGui.Unindent();
            }
        }

        // Statistics
        if (ImGui.CollapsingHeader("Statistics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Lithology Units: {borehole.LithologyUnits.Count}");

            if (borehole.LithologyUnits.Any())
            {
                var coveredDepth = borehole.LithologyUnits.Sum(u => u.DepthTo - u.DepthFrom);
                var coverage = coveredDepth / borehole.TotalDepth * 100;

                ImGui.Text($"Depth Coverage: {coverage:F1}%");
                ImGui.Text($"Covered Depth: {coveredDepth:F2} m");

                // Count parameters
                var totalParams = borehole.LithologyUnits.Sum(u => u.Parameters.Count);
                ImGui.Text($"Total Parameters: {totalParams}");
            }

            ImGui.Text($"Parameter Tracks: {borehole.ParameterTracks.Count}");
            var visibleTracks = borehole.ParameterTracks.Values.Count(t => t.IsVisible);
            ImGui.Text($"Visible Tracks: {visibleTracks}");
        }

        // Lithology summary
        if (ImGui.CollapsingHeader("Lithology Summary"))
        {
            if (borehole.LithologyUnits.Any())
            {
                var lithologyGroups = borehole.LithologyUnits
                    .GroupBy(u => u.LithologyType)
                    .OrderByDescending(g => g.Sum(u => u.DepthTo - u.DepthFrom));

                foreach (var group in lithologyGroups)
                {
                    var totalThickness = group.Sum(u => u.DepthTo - u.DepthFrom);
                    var percentage = totalThickness / borehole.TotalDepth * 100;

                    ImGui.Text($"{group.Key}:");
                    ImGui.Indent();
                    ImGui.Text($"  Thickness: {totalThickness:F2} m ({percentage:F1}%)");
                    ImGui.Text($"  Units: {group.Count()}");
                    ImGui.Unindent();
                }
            }
            else
            {
                ImGui.TextDisabled("No lithology units defined");
            }
        }

        // Parameter summary
        if (ImGui.CollapsingHeader("Parameter Summary"))
            foreach (var track in borehole.ParameterTracks.Values)
            {
                ImGui.Text($"{track.Name} ({track.Unit}):");
                ImGui.Indent();
                ImGui.Text($"  Range: {track.MinValue:F2} - {track.MaxValue:F2}");
                ImGui.Text($"  Data Points: {track.Points.Count}");
                ImGui.Text($"  Logarithmic: {(track.IsLogarithmic ? "Yes" : "No")}");
                ImGui.Text($"  Visible: {(track.IsVisible ? "Yes" : "No")}");
                ImGui.Unindent();
                ImGui.Spacing();
            }

        // Display settings
        if (ImGui.CollapsingHeader("Display Settings"))
        {
            ImGui.Text($"Track Width: {borehole.TrackWidth:F0} px");
            ImGui.Text($"Depth Scale Factor: {borehole.DepthScaleFactor:F2}");
            ImGui.Text($"Show Grid: {(borehole.ShowGrid ? "Yes" : "No")}");
            ImGui.Text($"Show Legend: {(borehole.ShowLegend ? "Yes" : "No")}");
        }

        // File information
        ImGui.Separator();
        ImGui.Text($"File: {dataset.FilePath}");
        ImGui.Text($"Size: {FormatBytes(dataset.GetSizeInBytes())}");
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