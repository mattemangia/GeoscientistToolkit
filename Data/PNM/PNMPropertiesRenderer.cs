// GeoscientistToolkit/Data/PNM/PNMPropertiesRenderer.cs

using System.Numerics;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

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
            PropertiesPanel.DrawProperty("Voxel Size", $"{pnm.VoxelSize:F3} μm");

            // Calculate average connectivity
            if (pnm.Pores.Count > 0)
            {
                var avgConnectivity = pnm.Throats.Count * 2.0f / pnm.Pores.Count;
                PropertiesPanel.DrawProperty("Avg. Connectivity", $"{avgConnectivity:F2}");
            }

            ImGui.Unindent();
        }

        if (ImGui.CollapsingHeader("Flow Properties", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // Tortuosity
            PropertiesPanel.DrawProperty("Tortuosity (τ)", $"{pnm.Tortuosity:F4}");
            if (pnm.Tortuosity > 0)
            {
                var tau2 = pnm.Tortuosity * pnm.Tortuosity;
                PropertiesPanel.DrawProperty("τ²", $"{tau2:F4}");
                PropertiesPanel.DrawProperty("Correction (1/τ²)", $"{1.0f / tau2:F4}");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Permeability Results
            ImGui.Text("Permeability (mD):");
            ImGui.Indent();

            // Get last calculation results if available
            var results = AbsolutePermeability.GetLastResults();

            // Darcy Permeability
            if (pnm.DarcyPermeability > 0)
            {
                ImGui.Text("Darcy Method:");
                ImGui.Indent();

                var uncorrected = results?.DarcyUncorrected ?? pnm.DarcyPermeability;
                PropertiesPanel.DrawProperty("  Uncorrected", $"{uncorrected:F3} mD");

                if (pnm.Tortuosity > 0)
                {
                    var corrected = results?.DarcyCorrected ??
                                    uncorrected / (pnm.Tortuosity * pnm.Tortuosity);
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1),
                        $"  τ²-Corrected: {corrected:F3} mD");
                }

                ImGui.Unindent();
            }

            // Navier-Stokes Permeability
            if (pnm.NavierStokesPermeability > 0)
            {
                ImGui.Text("Navier-Stokes:");
                ImGui.Indent();

                var uncorrected = results?.NavierStokesUncorrected ?? pnm.NavierStokesPermeability;
                PropertiesPanel.DrawProperty("  Uncorrected", $"{uncorrected:F3} mD");

                if (pnm.Tortuosity > 0)
                {
                    var corrected = results?.NavierStokesCorrected ??
                                    uncorrected / (pnm.Tortuosity * pnm.Tortuosity);
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1),
                        $"  τ²-Corrected: {corrected:F3} mD");
                }

                ImGui.Unindent();
            }

            // Lattice-Boltzmann Permeability
            if (pnm.LatticeBoltzmannPermeability > 0)
            {
                ImGui.Text("Lattice-Boltzmann:");
                ImGui.Indent();

                var uncorrected = results?.LatticeBoltzmannUncorrected ?? pnm.LatticeBoltzmannPermeability;
                PropertiesPanel.DrawProperty("  Uncorrected", $"{uncorrected:F3} mD");

                if (pnm.Tortuosity > 0)
                {
                    var corrected = results?.LatticeBoltzmannCorrected ??
                                    uncorrected / (pnm.Tortuosity * pnm.Tortuosity);
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1),
                        $"  τ²-Corrected: {corrected:F3} mD");
                }

                ImGui.Unindent();
            }

            if (pnm.DarcyPermeability == 0 &&
                pnm.NavierStokesPermeability == 0 &&
                pnm.LatticeBoltzmannPermeability == 0)
            {
                ImGui.TextDisabled("Not calculated");
                ImGui.TextDisabled("Use PNM Tools to compute");
            }

            ImGui.Unindent();
            ImGui.Unindent();
        }

        if (ImGui.CollapsingHeader("Pore Size Distribution"))
        {
            ImGui.Indent();

            if (pnm.Pores.Count > 0)
            {
                PropertiesPanel.DrawProperty("Min Pore Radius",
                    $"{pnm.MinPoreRadius:F3} vox ({pnm.MinPoreRadius * pnm.VoxelSize:F2} μm)");
                PropertiesPanel.DrawProperty("Max Pore Radius",
                    $"{pnm.MaxPoreRadius:F3} vox ({pnm.MaxPoreRadius * pnm.VoxelSize:F2} μm)");

                // Calculate mean and std dev
                var meanRadius = pnm.Pores.Sum(p => p.Radius) / pnm.Pores.Count;
                var variance = pnm.Pores.Sum(p => (p.Radius - meanRadius) * (p.Radius - meanRadius)) / pnm.Pores.Count;
                var stdDev = MathF.Sqrt(variance);

                PropertiesPanel.DrawProperty("Mean Pore Radius",
                    $"{meanRadius:F3} vox ({meanRadius * pnm.VoxelSize:F2} μm)");
                PropertiesPanel.DrawProperty("Std Dev",
                    $"{stdDev:F3} vox ({stdDev * pnm.VoxelSize:F2} μm)");
            }
            else
            {
                ImGui.TextDisabled("No pore data");
            }

            ImGui.Unindent();
        }

        if (ImGui.CollapsingHeader("Throat Size Distribution"))
        {
            ImGui.Indent();

            if (pnm.Throats.Count > 0)
            {
                PropertiesPanel.DrawProperty("Min Throat Radius",
                    $"{pnm.MinThroatRadius:F3} vox ({pnm.MinThroatRadius * pnm.VoxelSize:F2} μm)");
                PropertiesPanel.DrawProperty("Max Throat Radius",
                    $"{pnm.MaxThroatRadius:F3} vox ({pnm.MaxThroatRadius * pnm.VoxelSize:F2} μm)");

                // Calculate mean and std dev
                var meanRadius = pnm.Throats.Sum(t => t.Radius) / pnm.Throats.Count;
                var variance = pnm.Throats.Sum(t => (t.Radius - meanRadius) * (t.Radius - meanRadius)) /
                               pnm.Throats.Count;
                var stdDev = MathF.Sqrt(variance);

                PropertiesPanel.DrawProperty("Mean Throat Radius",
                    $"{meanRadius:F3} vox ({meanRadius * pnm.VoxelSize:F2} μm)");
                PropertiesPanel.DrawProperty("Std Dev",
                    $"{stdDev:F3} vox ({stdDev * pnm.VoxelSize:F2} μm)");
            }
            else
            {
                ImGui.TextDisabled("No throat data");
            }

            ImGui.Unindent();
        }

        // Add information tooltip
        if (ImGui.CollapsingHeader("Information"))
        {
            ImGui.Indent();
            ImGui.TextWrapped("Permeability values are shown as:");
            ImGui.BulletText("Uncorrected: Raw calculated value");
            ImGui.BulletText("τ²-Corrected: Divided by tortuosity squared");
            ImGui.Spacing();
            ImGui.TextWrapped("The corrected value accounts for the tortuous flow path through the pore network.");
            ImGui.Unindent();
        }
    }
}