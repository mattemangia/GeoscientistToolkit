// GeoscientistToolkit/Data/Mesh3D/Mesh3DProperties.cs

using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.Data.Mesh3D;

/// <summary>
///     Displays properties of a Mesh3DDataset in the Properties panel.
/// </summary>
public class Mesh3DProperties : IDatasetPropertiesRenderer
{
    public void Draw(Dataset dataset)
    {
        if (dataset is not Mesh3DDataset mesh) return;

        if (ImGui.CollapsingHeader("Mesh Summary", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            ImGui.Text($"Format: {mesh.FileFormat}");
            ImGui.Text($"Vertices: {mesh.VertexCount:N0}");
            ImGui.Text($"Faces: {mesh.FaceCount:N0}");
            ImGui.Text($"Scale: {mesh.Scale:F2}×");

            if (mesh.IsLoaded)
            {
                var size = mesh.BoundingBoxMax - mesh.BoundingBoxMin;
                ImGui.Separator();
                ImGui.Text("Bounding Box:");
                ImGui.Text(
                    $"  Min: ({mesh.BoundingBoxMin.X:F2}, {mesh.BoundingBoxMin.Y:F2}, {mesh.BoundingBoxMin.Z:F2})");
                ImGui.Text(
                    $"  Max: ({mesh.BoundingBoxMax.X:F2}, {mesh.BoundingBoxMax.Y:F2}, {mesh.BoundingBoxMax.Z:F2})");
                ImGui.Text($"  Center: ({mesh.Center.X:F2}, {mesh.Center.Y:F2}, {mesh.Center.Z:F2})");
                ImGui.Text($"  Size: {size.X:F2} × {size.Y:F2} × {size.Z:F2}");
            }

            ImGui.Unindent();
        }
    }
}