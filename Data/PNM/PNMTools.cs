// GeoscientistToolkit/UI/PNMTools.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Analysis.Pnm;
using System.Threading.Tasks;
using System;

namespace GeoscientistToolkit.UI
{
    public class PNMTools : IDatasetTools
    {
        private readonly ImGuiExportFileDialog _exportDialog;

        // --- Permeability Calculator State ---
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

        private readonly string[] _fluidTypes = { 
            "Water (20°C, 1.0 cP)", 
            "Air (20°C, 0.018 cP)", 
            "Nitrogen (20°C, 0.018 cP)",
            "CO₂ (20°C, 0.015 cP)",
            "Oil (Light, 5.0 cP)",
            "Oil (Heavy, 100.0 cP)",
            "Custom"
        };
        
        private readonly float[] _fluidViscosities = { 
            1.0f,    // Water
            0.018f,  // Air
            0.018f,  // Nitrogen
            0.015f,  // CO2
            5.0f,    // Light oil
            100.0f,  // Heavy oil
            1.0f     // Custom default
        };

        // Store last results for display
        private PermeabilityResults _lastResults = null;

        public PNMTools()
        {
            _exportDialog = new ImGuiExportFileDialog("ExportPNMDialog", "Export PNM");
            _exportDialog.SetExtensions((".pnm.json", "PNM JSON File"));
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not PNMDataset pnm) return;

            ImGui.Text("PNM Analysis Tools");
            ImGui.Separator();

            // Network Statistics
            if (ImGui.CollapsingHeader("Network Statistics", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawNetworkStatistics(pnm);
            }

            ImGui.Spacing();

            // Absolute Permeability Calculator
            if (ImGui.CollapsingHeader("Absolute Permeability Calculator", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawPermeabilityCalculator(pnm);
            }

            ImGui.Spacing();

            // Export Options
            if (ImGui.CollapsingHeader("Export"))
            {
                ImGui.Text("Export the current pore network model to a file.");
                if (ImGui.Button("Export as JSON...", new Vector2(-1, 0)))
                {
                    _exportDialog.Open(pnm.Name);
                }
                
                ImGui.Spacing();
                
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

        private void DrawNetworkStatistics(PNMDataset pnm)
        {
            ImGui.Indent();
            
            ImGui.Text($"Network Dimensions:");
            ImGui.BulletText($"Pores: {pnm.Pores.Count:N0}");
            ImGui.BulletText($"Throats: {pnm.Throats.Count:N0}");
            ImGui.BulletText($"Average Connectivity: {(pnm.Pores.Count > 0 ? pnm.Throats.Count * 2.0f / pnm.Pores.Count : 0):F2}");
            
            ImGui.Spacing();
            ImGui.Text($"Physical Properties:");
            ImGui.BulletText($"Voxel Size: {pnm.VoxelSize:F3} μm");
            ImGui.BulletText($"Tortuosity: {pnm.Tortuosity:F4}");
            
            // Calculate and display porosity if possible
            if (pnm.Pores.Count > 0)
            {
                var minBounds = new Vector3(
                    pnm.Pores.Min(p => p.Position.X),
                    pnm.Pores.Min(p => p.Position.Y),
                    pnm.Pores.Min(p => p.Position.Z));
                var maxBounds = new Vector3(
                    pnm.Pores.Max(p => p.Position.X),
                    pnm.Pores.Max(p => p.Position.Y),
                    pnm.Pores.Max(p => p.Position.Z));
                
                float totalVolume = (maxBounds.X - minBounds.X) * 
                                   (maxBounds.Y - minBounds.Y) * 
                                   (maxBounds.Z - minBounds.Z);
                float poreVolume = pnm.Pores.Sum(p => p.VolumeVoxels);
                float porosity = totalVolume > 0 ? poreVolume / totalVolume : 0;
                
                ImGui.BulletText($"Estimated Porosity: {porosity:P2}");
            }
            
            ImGui.Unindent();
        }

        private void DrawPermeabilityCalculator(PNMDataset pnm)
        {
            ImGui.Indent();

            // Display last results if available
            if (_lastResults != null || pnm.DarcyPermeability > 0 || pnm.NavierStokesPermeability > 0 || pnm.LatticeBoltzmannPermeability > 0)
            {
                DrawPermeabilityResults(pnm);
                ImGui.Separator();
            }

            // Fluid properties section
            ImGui.Text("Fluid Properties:");
            ImGui.SetNextItemWidth(250);
            if (ImGui.Combo("Fluid Type", ref _fluidTypeIndex, _fluidTypes, _fluidTypes.Length))
            {
                if (_fluidTypeIndex < _fluidViscosities.Length - 1) // Not custom
                {
                    _customViscosity = _fluidViscosities[_fluidTypeIndex];
                }
            }

            if (_fluidTypeIndex == _fluidTypes.Length - 1) // Custom
            {
                ImGui.SetNextItemWidth(150);
                ImGui.InputFloat("Viscosity (cP)", ref _customViscosity, 0.001f, 0.1f, "%.3f");
                if (_customViscosity < 0.001f) _customViscosity = 0.001f;
                if (_customViscosity > 10000f) _customViscosity = 10000f;
                
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Viscosity in centipoise (cP)\n" +
                                    "Water @ 20°C: 1.0 cP\n" +
                                    "Air @ 20°C: 0.018 cP\n" +
                                    "Honey: ~2000-10000 cP");
                }
            }
            else
            {
                ImGui.Text($"Viscosity: {_fluidViscosities[_fluidTypeIndex]:F3} cP");
            }

            ImGui.Spacing();

            // Flow direction
            ImGui.Text("Flow Configuration:");
            string[] axes = { "X-axis", "Y-axis", "Z-axis" };
            ImGui.SetNextItemWidth(150);
            ImGui.Combo("Flow Direction", ref _flowAxisIndex, axes, axes.Length);
            
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Direction of pressure gradient.\n" +
                                "Typically Z-axis for vertical cores.");
            }

            ImGui.Spacing();

            // Tortuosity correction
            ImGui.Checkbox("Apply Tortuosity Correction", ref _correctForTortuosity);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Corrects permeability by dividing by τ²\n" +
                                 $"Current tortuosity: {pnm.Tortuosity:F3}\n" +
                                 $"Correction factor: 1/{pnm.Tortuosity * pnm.Tortuosity:F3} = {1.0f / (pnm.Tortuosity * pnm.Tortuosity):F3}");
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Calculation Methods:");
            
            ImGui.Checkbox("Darcy (Simplified Hagen-Poiseuille)", ref _calcDarcy);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Uses simple Hagen-Poiseuille law for throat conductance.\n" +
                                "Fast but less accurate for complex geometries.");
            }
            
            ImGui.Checkbox("Navier-Stokes (with entrance effects)", ref _calcNavierStokes);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Includes entrance/exit effects in throat conductance.\n" +
                                "More accurate for short throats.");
            }
            
            ImGui.Checkbox("Lattice-Boltzmann (pore body resistance)", ref _calcLatticeBoltzmann);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Models full pore-throat-pore resistance.\n" +
                                "Most accurate but computationally intensive.");
            }

            if (!_calcDarcy && !_calcNavierStokes && !_calcLatticeBoltzmann)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Please select at least one method");
            }

            ImGui.Spacing();
            ImGui.Separator();
            
            // Solver options
            ImGui.Text("Solver Options:");
            ImGui.Checkbox("Use GPU Acceleration (OpenCL)", ref _useGpu);
            if (!OpenCLContext.IsAvailable)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(Not Available)");
                _useGpu = false;
            }
            else if (_useGpu)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "(Available)");
            }

            ImGui.Spacing();

            // Calculate button
            if (_isCalculating)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Calculating...", new Vector2(-1, 30));
                ImGui.EndDisabled();
                
                ImGui.TextColored(new Vector4(1, 1, 0, 1), _calculationStatus);
                
                // Add a progress bar
                float progress = _calculationStatus.Contains("Darcy") ? 0.33f :
                               _calculationStatus.Contains("Navier") ? 0.66f :
                               _calculationStatus.Contains("Lattice") ? 0.90f : 0.1f;
                ImGui.ProgressBar(progress, new Vector2(-1, 0));
            }
            else
            {
                bool canCalculate = (_calcDarcy || _calcNavierStokes || _calcLatticeBoltzmann) &&
                                   pnm.Pores.Count > 0 && pnm.Throats.Count > 0;
                
                if (!canCalculate) ImGui.BeginDisabled();
                
                if (ImGui.Button("Calculate Permeability", new Vector2(-1, 30)))
                {
                    var options = new PermeabilityOptions
                    {
                        Dataset = pnm,
                        Axis = (FlowAxis)_flowAxisIndex,
                        FluidViscosity = _fluidTypeIndex == _fluidTypes.Length - 1 ? 
                                        _customViscosity : _fluidViscosities[_fluidTypeIndex],
                        CorrectForTortuosity = _correctForTortuosity,
                        UseGpu = _useGpu && OpenCLContext.IsAvailable,
                        CalculateDarcy = _calcDarcy,
                        CalculateNavierStokes = _calcNavierStokes,
                        CalculateLatticeBoltzmann = _calcLatticeBoltzmann,
                    };
                    StartCalculation(options);
                }
                
                if (!canCalculate) 
                {
                    ImGui.EndDisabled();
                    if (pnm.Pores.Count == 0 || pnm.Throats.Count == 0)
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), "No pore network data available");
                    }
                }
            }

            ImGui.Unindent();
        }

        private void DrawPermeabilityResults(PNMDataset pnm)
        {
            var results = _lastResults ?? AbsolutePermeability.GetLastResults();
            
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.15f, 0.5f));
            ImGui.BeginChild("PermeabilityResults", new Vector2(-1, 180), ImGuiChildFlags.Border);
            
            ImGui.Text("Permeability Results");
            ImGui.Separator();
            
            // Create a table for results
            if (ImGui.BeginTable("PermTable", 3, ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Method", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("Uncorrected (mD)", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("τ²-Corrected (mD)", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableHeadersRow();
                
                // Darcy
                if (results?.DarcyUncorrected > 0 || pnm.DarcyPermeability > 0)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Darcy");
                    ImGui.TableSetColumnIndex(1);
                    float uncorrected = results?.DarcyUncorrected ?? pnm.DarcyPermeability;
                    ImGui.Text($"{uncorrected:F3}");
                    ImGui.TableSetColumnIndex(2);
                    float corrected = results?.DarcyCorrected ?? 
                                     (pnm.Tortuosity > 0 ? uncorrected / (pnm.Tortuosity * pnm.Tortuosity) : uncorrected);
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"{corrected:F3}");
                }
                
                // Navier-Stokes
                if (results?.NavierStokesUncorrected > 0 || pnm.NavierStokesPermeability > 0)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Navier-Stokes");
                    ImGui.TableSetColumnIndex(1);
                    float uncorrected = results?.NavierStokesUncorrected ?? pnm.NavierStokesPermeability;
                    ImGui.Text($"{uncorrected:F3}");
                    ImGui.TableSetColumnIndex(2);
                    float corrected = results?.NavierStokesCorrected ?? 
                                     (pnm.Tortuosity > 0 ? uncorrected / (pnm.Tortuosity * pnm.Tortuosity) : uncorrected);
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"{corrected:F3}");
                }
                
                // Lattice-Boltzmann
                if (results?.LatticeBoltzmannUncorrected > 0 || pnm.LatticeBoltzmannPermeability > 0)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Lattice-Boltzmann");
                    ImGui.TableSetColumnIndex(1);
                    float uncorrected = results?.LatticeBoltzmannUncorrected ?? pnm.LatticeBoltzmannPermeability;
                    ImGui.Text($"{uncorrected:F3}");
                    ImGui.TableSetColumnIndex(2);
                    float corrected = results?.LatticeBoltzmannCorrected ?? 
                                     (pnm.Tortuosity > 0 ? uncorrected / (pnm.Tortuosity * pnm.Tortuosity) : uncorrected);
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"{corrected:F3}");
                }
                
                ImGui.EndTable();
            }
            
            ImGui.Spacing();
            ImGui.Text($"Tortuosity: {(results?.Tortuosity ?? pnm.Tortuosity):F4}");
            ImGui.Text($"Correction Factor (1/τ²): {1.0f / ((results?.Tortuosity ?? pnm.Tortuosity) * (results?.Tortuosity ?? pnm.Tortuosity)):F4}");
            
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        private void StartCalculation(PermeabilityOptions options)
        {
            _isCalculating = true;
            _calculationStatus = "Initializing calculation...";

            Task.Run(() =>
            {
                try
                {
                    // Update status for each method
                    if (options.CalculateDarcy)
                    {
                        _calculationStatus = "Calculating Darcy permeability...";
                        System.Threading.Thread.Sleep(100); // Allow UI to update
                    }
                    
                    if (options.CalculateNavierStokes)
                    {
                        _calculationStatus = "Calculating Navier-Stokes permeability...";
                        System.Threading.Thread.Sleep(100);
                    }
                    
                    if (options.CalculateLatticeBoltzmann)
                    {
                        _calculationStatus = "Calculating Lattice-Boltzmann permeability...";
                        System.Threading.Thread.Sleep(100);
                    }
                    
                    AbsolutePermeability.Calculate(options);
                    _lastResults = AbsolutePermeability.GetLastResults();
                    _calculationStatus = "Calculation completed successfully!";

                    // Notify UI to update properties panels
                    ProjectManager.Instance.NotifyDatasetDataChanged(options.Dataset);
                }
                catch (Exception ex)
                {
                    _calculationStatus = $"Error: {ex.Message}";
                    Logger.LogError($"[Permeability] Calculation failed: {ex}");
                }
                finally
                {
                    System.Threading.Thread.Sleep(1000); // Show final status briefly
                    _isCalculating = false;
                }
            });
        }
    }
}