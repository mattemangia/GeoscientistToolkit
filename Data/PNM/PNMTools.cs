// GeoscientistToolkit/UI/PNMTools.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Analysis.Pnm; // Import the new analysis tools
using System.Threading.Tasks;
using System;

namespace GeoscientistToolkit.UI
{
    public class PNMTools : IDatasetTools
    {
        private readonly ImGuiExportFileDialog _exportDialog;

        // --- NEW: State for Permeability Calculator ---
        private bool _isCalculating = false;
        private string _calculationStatus = "";
        private int _fluidTypeIndex = 0;
        private float _customViscosity = 1.0f; // cP
        private bool _correctForTortuosity = true;
        private int _flowAxisIndex = 2; // Z-axis
        private bool _useGpu = false;
        private bool _calcDarcy = true;
        private bool _calcNavierStokes = true;
        private bool _calcLatticeBoltzmann = true;

        private readonly string[] _fluidTypes = { "Water (1 cP)", "Nitrogen (0.02 cP)", "Custom" };
        private readonly float[] _fluidViscosities = { 1.0f, 0.02f, 1.0f };


        public PNMTools()
        {
            _exportDialog = new ImGuiExportFileDialog("ExportPNMDialog", "Export PNM");
            _exportDialog.SetExtensions((".pnm.json", "PNM JSON File"));
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not PNMDataset pnm) return;

            ImGui.Text("PNM Tools");
            ImGui.Separator();

            // --- REPLACED: Permeability section is now fully implemented ---
            if (ImGui.CollapsingHeader("Absolute Permeability", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawPermeabilityCalculator(pnm);
            }

            ImGui.Spacing();

            if (ImGui.CollapsingHeader("Export", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text("Export the current pore network model to a file.");
                if (ImGui.Button("Export as JSON...", new Vector2(-1, 0)))
                {
                    _exportDialog.Open(pnm.Name);
                }
            }
            ImGui.Separator();
            if (ImGui.Button("Create Pores Table Dataset", new Vector2(-1, 0)))
            {
                var poresTbl = pnm.BuildPoresTableDataset($"{pnm.Name}_Pores");
                ProjectManager.Instance.AddDataset(poresTbl);
                Logger.Log($"[PNMTools] Created table dataset '{poresTbl.Name}' for pores.");
            }

            if (ImGui.Button("Create Throats Table Dataset", new Vector2(-1, 0)))
            {
                var throatsTbl = pnm.BuildThroatsTableDataset($"{pnm.Name}_Throats");
                ProjectManager.Instance.AddDataset(throatsTbl);
                Logger.Log($"[PNMTools] Created table dataset '{throatsTbl.Name}' for throats.");
            }
            // Handle the export dialog
            if (_exportDialog.Submit())
            {
                try
                {
                    pnm.ExportToJson(_exportDialog.SelectedPath);
                    Logger.Log($"[PNMTools] Successfully exported PNM dataset to '{_exportDialog.SelectedPath}'");
                }
                catch (System.Exception ex)
                {
                    Logger.LogError($"[PNMTools] Failed to export PNM dataset: {ex.Message}");
                }
            }
        }

        private void DrawPermeabilityCalculator(PNMDataset pnm)
        {
            ImGui.Indent();

            // Fluid properties
            ImGui.SetNextItemWidth(200);
            if (ImGui.Combo("Fluid Type", ref _fluidTypeIndex, _fluidTypes, _fluidTypes.Length))
            {
                _customViscosity = _fluidViscosities[_fluidTypeIndex];
            }

            if (_fluidTypeIndex == 2) // Custom
            {
                ImGui.SetNextItemWidth(100);
                ImGui.InputFloat("Viscosity (cP)", ref _customViscosity, 0.01f, 0.1f, "%.2f");
            }

            // Flow and model options
            string[] axes = { "X", "Y", "Z" };
            ImGui.SetNextItemWidth(100);
            ImGui.Combo("Flow Axis", ref _flowAxisIndex, axes, axes.Length);

            ImGui.Checkbox("Correct for Tortuosity", ref _correctForTortuosity);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Applies a 1/τ² correction factor to the final permeability.");
            }
            
            ImGui.Separator();
            ImGui.Text("Calculation Engines:");
            ImGui.Checkbox("Darcy (Simplified)", ref _calcDarcy);
            ImGui.Checkbox("Navier-Stokes (Network)", ref _calcNavierStokes);
            ImGui.Checkbox("Lattice-Boltzmann (Network)", ref _calcLatticeBoltzmann);

            ImGui.Separator();
            ImGui.Text("Solver Backend:");
            ImGui.Checkbox("Use GPU (OpenCL)", ref _useGpu);
            if (!Analysis.Pnm.OpenCLContext.IsAvailable)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(Not Available)");
            }


            ImGui.Spacing();

            // Action button and status
            if (_isCalculating)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Calculating...", new Vector2(-1, 0));
                ImGui.EndDisabled();
                ImGui.Text(_calculationStatus);
            }
            else
            {
                if (ImGui.Button("Calculate Permeability", new Vector2(-1, 0)))
                {
                    var options = new PermeabilityOptions
                    {
                        Dataset = pnm,
                        Axis = (FlowAxis)_flowAxisIndex,
                        FluidViscosity = _fluidTypeIndex == 2 ? _customViscosity : _fluidViscosities[_fluidTypeIndex],
                        CorrectForTortuosity = _correctForTortuosity,
                        UseGpu = _useGpu && Analysis.Pnm.OpenCLContext.IsAvailable,
                        CalculateDarcy = _calcDarcy,
                        CalculateNavierStokes = _calcNavierStokes,
                        CalculateLatticeBoltzmann = _calcLatticeBoltzmann,
                    };
                    StartCalculation(options);
                }
            }

            ImGui.Unindent();
        }

        private void StartCalculation(PermeabilityOptions options)
        {
            _isCalculating = true;
            _calculationStatus = "Starting permeability calculation...";

            Task.Run(() =>
            {
                try
                {
                    AbsolutePermeability.Calculate(options);
                    _calculationStatus = "Calculation finished successfully.";

                    // Notify UI to update properties panels
                    ProjectManager.Instance.NotifyDatasetDataChanged(options.Dataset);
                }
                catch (Exception ex)
                {
                    _calculationStatus = $"Error: {ex.Message}";
                    Logger.LogError($"[Permeability] Calculation failed: {ex.Message}");
                }
                finally
                {
                    _isCalculating = false;
                }
            });
        }
    }
}