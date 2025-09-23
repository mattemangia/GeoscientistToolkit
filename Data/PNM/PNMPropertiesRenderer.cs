// GeoscientistToolkit/Data/PNM/PNMPropertiesRenderer.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class PNMPropertiesRenderer : IDatasetPropertiesRenderer
    {
        public void Draw(Dataset dataset)
        {
            if (dataset is not PNMDataset pnm) return;

            if (ImGui.CollapsingHeader("Pore Network Properties", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                PropertiesPanel.DrawProperty("Pore Count", $"{pnm.Pores.Count:N0}");
                PropertiesPanel.DrawProperty("Throat Count", $"{pnm.Throats.Count:N0}");
                PropertiesPanel.DrawProperty("Voxel Size", $"{pnm.VoxelSize:F2} µm");
                ImGui.Unindent();
            }

            if (ImGui.CollapsingHeader("Calculated Flow Properties", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                PropertiesPanel.DrawProperty("Tortuosity", $"{pnm.Tortuosity:F4}");
                PropertiesPanel.DrawProperty("Darcy Permeability", $"{pnm.DarcyPermeability:F3} mD");
                PropertiesPanel.DrawProperty("Navier-Stokes Perm.", $"{pnm.NavierStokesPermeability:F3} mD");
                PropertiesPanel.DrawProperty("Lattice-Boltzmann Perm.", $"{pnm.LatticeBoltzmannPermeability:F3} mD");
                ImGui.Unindent();
            }

            if (ImGui.CollapsingHeader("Pore Size Distribution", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                PropertiesPanel.DrawProperty("Min Pore Radius", $"{pnm.MinPoreRadius * pnm.VoxelSize:F2} µm");
                PropertiesPanel.DrawProperty("Max Pore Radius", $"{pnm.MaxPoreRadius * pnm.VoxelSize:F2} µm");
                ImGui.Unindent();
            }

            if (ImGui.CollapsingHeader("Throat Size Distribution", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                PropertiesPanel.DrawProperty("Min Throat Radius", $"{pnm.MinThroatRadius * pnm.VoxelSize:F2} µm");
                PropertiesPanel.DrawProperty("Max Throat Radius", $"{pnm.MaxThroatRadius * pnm.VoxelSize:F2} µm");
                ImGui.Unindent();
            }
        }
    }
}