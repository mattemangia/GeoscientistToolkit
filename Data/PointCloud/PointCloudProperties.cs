// GeoscientistToolkit/Data/PointCloud/PointCloudProperties.cs

using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.PointCloud;

/// <summary>
/// Displays properties of a PointCloudDataset in the Properties panel.
/// </summary>
public class PointCloudProperties : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not PointCloudDataset pc) return;

        if (ImGui.CollapsingHeader("Point Cloud Summary", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            ImGui.Text($"Format: {pc.FileFormat.ToUpper()}");
            ImGui.Text($"Points: {pc.PointCount:N0}");
            ImGui.Text($"Has Colors: {(pc.HasColors ? "Yes" : "No")}");
            ImGui.Text($"Has Intensity: {(pc.HasIntensities ? "Yes" : "No")}");
            ImGui.Text($"Point Size: {pc.PointSize:F2}");
            ImGui.Text($"Scale: {pc.Scale:F2}x");
            ImGui.Unindent();
        }

        if (pc.IsLoaded && ImGui.CollapsingHeader("Bounding Box", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            ImGui.Text($"Min: ({pc.BoundingBoxMin.X:F2}, {pc.BoundingBoxMin.Y:F2}, {pc.BoundingBoxMin.Z:F2})");
            ImGui.Text($"Max: ({pc.BoundingBoxMax.X:F2}, {pc.BoundingBoxMax.Y:F2}, {pc.BoundingBoxMax.Z:F2})");
            ImGui.Text($"Center: ({pc.Center.X:F2}, {pc.Center.Y:F2}, {pc.Center.Z:F2})");
            ImGui.Text($"Size: {pc.Size.X:F2} x {pc.Size.Y:F2} x {pc.Size.Z:F2}");
            ImGui.Unindent();
        }

        if (ImGui.CollapsingHeader("Statistics"))
        {
            ImGui.Indent();

            if (pc.IsLoaded && pc.PointCount > 0)
            {
                // Calculate some basic statistics
                var avgZ = pc.Points.Average(p => p.Z);
                var minZ = pc.BoundingBoxMin.Z;
                var maxZ = pc.BoundingBoxMax.Z;

                ImGui.Text("Elevation Statistics:");
                ImGui.Text($"  Min Z: {minZ:F2}");
                ImGui.Text($"  Max Z: {maxZ:F2}");
                ImGui.Text($"  Avg Z: {avgZ:F2}");
                ImGui.Text($"  Range: {maxZ - minZ:F2}");

                // Point density
                var area = pc.Size.X * pc.Size.Y;
                if (area > 0)
                {
                    var density = pc.PointCount / area;
                    ImGui.Separator();
                    ImGui.Text($"Point Density: {density:F2} pts/unit^2");
                }
            }
            else
            {
                ImGui.TextDisabled("Load the dataset to view statistics");
            }

            ImGui.Unindent();
        }

        if (ImGui.CollapsingHeader("File Information"))
        {
            ImGui.Indent();
            ImGui.Text($"File: {Path.GetFileName(pc.FilePath)}");
            ImGui.Text($"Size: {FormatBytes(pc.GetSizeInBytes())}");
            ImGui.Text($"Loaded: {(pc.IsLoaded ? "Yes" : "No")}");
            ImGui.Unindent();
        }
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
