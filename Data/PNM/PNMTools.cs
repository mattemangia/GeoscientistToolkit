// GeoscientistToolkit/UI/PNMTools.cs - FIXED VERSION
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
using System.IO;
using System.Text;

namespace GeoscientistToolkit.UI
{
    public class PNMTools : IDatasetTools
    {
        private readonly ImGuiExportFileDialog _exportDialog;
        private readonly ImGuiExportFileDialog _exportResultsDialog;

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
        private bool _calcLatticeBoltzmann = false; // Default off (slowest)
        
        // Pressure parameters
        private float _inletPressure = 2.0f; // Pa (default 2 Pa)
        private float _outletPressure = 0.0f; // Pa (default 0 Pa)
        private int _pressureUnitIndex = 0; // 0=Pa, 1=kPa, 2=bar, 3=psi

        private readonly string[] _pressureUnits = { "Pa", "kPa", "bar", "psi" };
        private readonly float[] _pressureConversions = { 1.0f, 1000.0f, 100000.0f, 6894.76f };

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
            
            _exportResultsDialog = new ImGuiExportFileDialog("ExportResultsDialog", "Export Results");
            _exportResultsDialog.SetExtensions(
                (".csv", "CSV (Comma-separated values)"),
                (".txt", "Text Report")
            );
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
                DrawExportSection(pnm);
            }

            // Handle dialogs
            HandleDialogs(pnm);
        }

        private void DrawNetworkStatistics(PNMDataset pnm)
        {
            ImGui.Indent();
            
            // Create a table for better layout
            if (ImGui.BeginTable("NetStatsTable", 2, ImGuiTableFlags.BordersInner))
            {
                ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Pores:");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{pnm.Pores.Count:N0}");
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Throats:");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{pnm.Throats.Count:N0}");
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Avg. Connectivity:");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{(pnm.Pores.Count > 0 ? pnm.Throats.Count * 2.0f / pnm.Pores.Count : 0):F2}");
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Voxel Size:");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{pnm.VoxelSize:F3} μm");
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Tortuosity:");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{pnm.Tortuosity:F4}");
                
                // Porosity estimate
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
                    
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(0);
                    ImGui.Text("Est. Porosity:");
                    ImGui.TableSetColumnIndex(1);
                    ImGui.Text($"{porosity:P2}");
                }
                
                ImGui.EndTable();
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
                if (_fluidTypeIndex < _fluidViscosities.Length - 1)
                {
                    _customViscosity = _fluidViscosities[_fluidTypeIndex];
                }
            }

            if (_fluidTypeIndex == _fluidTypes.Length - 1) // Custom
            {
                ImGui.SetNextItemWidth(150);
                ImGui.InputFloat("Viscosity (cP)", ref _customViscosity, 0.001f, 0.1f, "%.3f");
                _customViscosity = Math.Clamp(_customViscosity, 0.001f, 10000f);
                
                ImGui.SameLine();
                ImGui.TextDisabled("(?)");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Dynamic viscosity in centipoise\n" +
                                    "Water @ 20°C: 1.0 cP\n" +
                                    "Air @ 20°C: 0.018 cP\n" +
                                    "Motor oil: 100-1000 cP");
                }
            }
            else
            {
                ImGui.Text($"Viscosity: {_fluidViscosities[_fluidTypeIndex]:F3} cP");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Pressure configuration section
            ImGui.Text("Pressure Configuration:");
            
            // Pressure unit selector
            ImGui.SetNextItemWidth(100);
            ImGui.Combo("Pressure Unit", ref _pressureUnitIndex, _pressureUnits, _pressureUnits.Length);
            
            // Inlet pressure
            ImGui.SetNextItemWidth(150);
            float inletDisplay = _inletPressure / _pressureConversions[_pressureUnitIndex];
            if (ImGui.InputFloat($"Inlet Pressure ({_pressureUnits[_pressureUnitIndex]})", ref inletDisplay, 0.1f, 1.0f, "%.3f"))
            {
                _inletPressure = inletDisplay * _pressureConversions[_pressureUnitIndex];
            }
            
            // Outlet pressure
            ImGui.SetNextItemWidth(150);
            float outletDisplay = _outletPressure / _pressureConversions[_pressureUnitIndex];
            if (ImGui.InputFloat($"Outlet Pressure ({_pressureUnits[_pressureUnitIndex]})", ref outletDisplay, 0.1f, 1.0f, "%.3f"))
            {
                _outletPressure = outletDisplay * _pressureConversions[_pressureUnitIndex];
            }
            
            // Show pressure drop
            float pressureDrop = Math.Abs(_inletPressure - _outletPressure);
            ImGui.Text($"Pressure Drop: {pressureDrop:F3} Pa ({pressureDrop/_pressureConversions[_pressureUnitIndex]:F3} {_pressureUnits[_pressureUnitIndex]})");
            
            if (pressureDrop < 0.001f)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Warning: Pressure drop is too small!");
            }
            
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Typical pressure drops:\n" +
                                "Laboratory: 0.1-10 kPa\n" +
                                "Field conditions: 10-1000 kPa\n" +
                                "High pressure: >1000 kPa");
            }

            ImGui.Spacing();
            ImGui.Separator();
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
                ImGui.SetTooltip($"Divides permeability by τ²\n" +
                                 $"Current tortuosity: {pnm.Tortuosity:F3}\n" +
                                 $"Correction factor: {1.0f / (pnm.Tortuosity * pnm.Tortuosity):F3}");
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Calculation Methods:");
            
            ImGui.Checkbox("Darcy (Simple Hagen-Poiseuille)", ref _calcDarcy);
            ImGui.Checkbox("Navier-Stokes (Entrance effects)", ref _calcNavierStokes);
            ImGui.Checkbox("Lattice-Boltzmann (Full resistance)", ref _calcLatticeBoltzmann);

            if (!_calcDarcy && !_calcNavierStokes && !_calcLatticeBoltzmann)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Select at least one method");
            }

            ImGui.Spacing();
            ImGui.Separator();
            
            // Solver options
            ImGui.Checkbox("Use GPU Acceleration", ref _useGpu);
            if (!OpenCLContext.IsAvailable)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(Not Available)");
                _useGpu = false;
            }

            ImGui.Spacing();

            // Calculate button
            if (_isCalculating)
            {
                ImGui.BeginDisabled();
                ImGui.Button("Calculating...", new Vector2(-1, 30));
                ImGui.EndDisabled();
                
                ImGui.TextColored(new Vector4(1, 1, 0, 1), _calculationStatus);
                
                float progress = _calculationStatus.Contains("Darcy") ? 0.33f :
                               _calculationStatus.Contains("Navier") ? 0.66f :
                               _calculationStatus.Contains("Lattice") ? 0.90f : 0.1f;
                ImGui.ProgressBar(progress, new Vector2(-1, 0));
            }
            else
            {
                bool canCalculate = (_calcDarcy || _calcNavierStokes || _calcLatticeBoltzmann) &&
                                   pnm.Pores.Count > 0 && pnm.Throats.Count > 0 &&
                                   Math.Abs(_inletPressure - _outletPressure) > 0.001f;
                
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
                        InletPressure = _inletPressure,
                        OutletPressure = _outletPressure
                    };
                    StartCalculation(options);
                }
                
                if (!canCalculate) ImGui.EndDisabled();
            }

            ImGui.Unindent();
        }

        private void DrawPermeabilityResults(PNMDataset pnm)
        {
            var results = _lastResults ?? AbsolutePermeability.GetLastResults();
            
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.15f, 0.5f));
            
            // FIXED: Increased height from 250 to 400 pixels for better visibility
            ImGui.BeginChild("PermeabilityResults", new Vector2(-1, 400), ImGuiChildFlags.Border, 
                            ImGuiWindowFlags.HorizontalScrollbar);
            
            ImGui.Text("Permeability Results");
            ImGui.Separator();
            
            // Parameters table - adjusted row height with spacing
            if (ImGui.BeginTable("ParamsTable", 2, ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg | ImGuiTableFlags.PadOuterX))
            {
                ImGui.TableSetupColumn("Parameter", ImGuiTableColumnFlags.WidthFixed, 180);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 200);
                
                ImGui.TableHeadersRow();
                
                // Flow parameters with better spacing
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); 
                ImGui.Text("Flow Axis:");
                ImGui.TableSetColumnIndex(1); 
                ImGui.Text($"{results?.FlowAxis ?? "Z"}");
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); 
                ImGui.Text("Model Length:");
                ImGui.TableSetColumnIndex(1); 
                ImGui.Text($"{(results?.ModelLength ?? 0) * 1e6:F1} μm");
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); 
                ImGui.Text("Cross-sectional Area:");
                ImGui.TableSetColumnIndex(1); 
                ImGui.Text($"{(results?.CrossSectionalArea ?? 0) * 1e12:F3} μm²");
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); 
                ImGui.Text("Pressure Drop:");
                ImGui.TableSetColumnIndex(1); 
                ImGui.Text($"{results?.UsedPressureDrop ?? 1.0f:F3} Pa");
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); 
                ImGui.Text("Viscosity:");
                ImGui.TableSetColumnIndex(1); 
                ImGui.Text($"{results?.UsedViscosity ?? 1.0f:F3} cP");
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); 
                ImGui.Text("Total Flow Rate:");
                ImGui.TableSetColumnIndex(1); 
                ImGui.Text($"{(results?.TotalFlowRate ?? 0):E3} m³/s");
                
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); 
                ImGui.Text("Tortuosity (τ):");
                ImGui.TableSetColumnIndex(1); 
                ImGui.Text($"{results?.Tortuosity ?? pnm.Tortuosity:F4}");
                
                ImGui.EndTable();
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("Permeability Values:");
            ImGui.Spacing();
            
            // Permeability results table with better visibility
            if (ImGui.BeginTable("PermTable", 4, ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg | 
                                ImGuiTableFlags.ScrollX | ImGuiTableFlags.PadOuterX))
            {
                ImGui.TableSetupColumn("Method", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("Uncorrected (mD)", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("τ²-Corrected (mD)", ImGuiTableColumnFlags.WidthFixed, 150);
                ImGui.TableSetupColumn("Corrected (Darcy)", ImGuiTableColumnFlags.WidthFixed, 150);
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
                    ImGui.TableSetColumnIndex(3);
                    ImGui.Text($"{corrected/1000:F6}");
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
                    ImGui.TableSetColumnIndex(3);
                    ImGui.Text($"{corrected/1000:F6}");
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
                    ImGui.TableSetColumnIndex(3);
                    ImGui.Text($"{corrected/1000:F6}");
                }
                
                ImGui.EndTable();
            }
            
            ImGui.Spacing();
            ImGui.Spacing();
            
            // Export button
            if (ImGui.Button("Export Results...", new Vector2(-1, 0)))
            {
                _exportResultsDialog.Open($"{pnm.Name}_results");
            }
            
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        private void DrawExportSection(PNMDataset pnm)
        {
            ImGui.Indent();
            
            ImGui.Text("Export Options:");
            ImGui.Separator();
            
            // PNM export
            if (ImGui.Button("Export PNM as JSON...", new Vector2(-1, 0)))
            {
                _exportDialog.Open(pnm.Name);
            }
            
            ImGui.Spacing();
            
            // Table datasets
            if (ImGui.Button("Create Pores Table", new Vector2(-1, 0)))
            {
                var poresTbl = pnm.BuildPoresTableDataset($"{pnm.Name}_Pores");
                ProjectManager.Instance.AddDataset(poresTbl);
                Logger.Log($"[PNMTools] Created table dataset for pores");
            }

            if (ImGui.Button("Create Throats Table", new Vector2(-1, 0)))
            {
                var throatsTbl = pnm.BuildThroatsTableDataset($"{pnm.Name}_Throats");
                ProjectManager.Instance.AddDataset(throatsTbl);
                Logger.Log($"[PNMTools] Created table dataset for throats");
            }
            
            ImGui.Spacing();
            
            // CSV export
            if (ImGui.Button("Export Pores CSV...", new Vector2(-1, 0)))
            {
                var dialog = new ImGuiExportFileDialog("ExportPoresCSV", "Export Pores");
                dialog.SetExtensions((".csv", "CSV File"));
                dialog.Open($"{pnm.Name}_pores");
                // Handle in next frame...
            }
            
            if (ImGui.Button("Export Throats CSV...", new Vector2(-1, 0)))
            {
                var dialog = new ImGuiExportFileDialog("ExportThroatsCSV", "Export Throats");
                dialog.SetExtensions((".csv", "CSV File"));
                dialog.Open($"{pnm.Name}_throats");
                // Handle in next frame...
            }
            
            ImGui.Unindent();
        }

        private void HandleDialogs(PNMDataset pnm)
        {
            if (_exportDialog.Submit())
            {
                try
                {
                    pnm.ExportToJson(_exportDialog.SelectedPath);
                    Logger.Log($"[PNMTools] Exported PNM to '{_exportDialog.SelectedPath}'");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[PNMTools] Export failed: {ex.Message}");
                }
            }

            if (_exportResultsDialog.Submit())
            {
                try
                {
                    ExportResults(_exportResultsDialog.SelectedPath, pnm);
                    Logger.Log($"[PNMTools] Exported results to '{_exportResultsDialog.SelectedPath}'");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[PNMTools] Results export failed: {ex.Message}");
                }
            }
        }

        private void ExportResults(string path, PNMDataset pnm)
        {
            var results = _lastResults ?? AbsolutePermeability.GetLastResults();
            if (results == null)
            {
                Logger.LogWarning("[PNMTools] No results to export");
                return;
            }

            var ext = Path.GetExtension(path).ToLower();
            
            if (ext == ".csv")
            {
                // Export as CSV
                using (var writer = new StreamWriter(path, false, Encoding.UTF8))
                {
                    writer.WriteLine("Permeability Analysis Results");
                    writer.WriteLine($"Dataset,{pnm.Name}");
                    writer.WriteLine($"Date,{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine();
                    
                    writer.WriteLine("Flow Parameters");
                    writer.WriteLine("Parameter,Value,Unit");
                    writer.WriteLine($"Flow Axis,{results.FlowAxis},");
                    writer.WriteLine($"Model Length,{results.ModelLength * 1e6:F3},μm");
                    writer.WriteLine($"Cross-sectional Area,{results.CrossSectionalArea * 1e12:F3},μm²");
                    writer.WriteLine($"Pressure Drop,{results.UsedPressureDrop:F3},Pa");
                    writer.WriteLine($"Fluid Viscosity,{results.UsedViscosity:F3},cP");
                    writer.WriteLine($"Total Flow Rate,{results.TotalFlowRate:E3},m³/s");
                    writer.WriteLine($"Voxel Size,{results.VoxelSize:F3},μm");
                    writer.WriteLine($"Pore Count,{results.PoreCount},");
                    writer.WriteLine($"Throat Count,{results.ThroatCount},");
                    writer.WriteLine($"Tortuosity,{results.Tortuosity:F4},");
                    writer.WriteLine();
                    
                    writer.WriteLine("Permeability Results");
                    writer.WriteLine("Method,Uncorrected (mD),τ²-Corrected (mD),Uncorrected (D),τ²-Corrected (D)");
                    
                    if (results.DarcyUncorrected > 0)
                        writer.WriteLine($"Darcy,{results.DarcyUncorrected:F6},{results.DarcyCorrected:F6}," +
                                       $"{results.DarcyUncorrected/1000:F9},{results.DarcyCorrected/1000:F9}");
                    
                    if (results.NavierStokesUncorrected > 0)
                        writer.WriteLine($"Navier-Stokes,{results.NavierStokesUncorrected:F6},{results.NavierStokesCorrected:F6}," +
                                       $"{results.NavierStokesUncorrected/1000:F9},{results.NavierStokesCorrected/1000:F9}");
                    
                    if (results.LatticeBoltzmannUncorrected > 0)
                        writer.WriteLine($"Lattice-Boltzmann,{results.LatticeBoltzmannUncorrected:F6},{results.LatticeBoltzmannCorrected:F6}," +
                                       $"{results.LatticeBoltzmannUncorrected/1000:F9},{results.LatticeBoltzmannCorrected/1000:F9}");
                }
            }
            else
            {
                // Export as text report
                using (var writer = new StreamWriter(path, false, Encoding.UTF8))
                {
                    writer.WriteLine("================================================================================");
                    writer.WriteLine("                        PERMEABILITY ANALYSIS REPORT");
                    writer.WriteLine("================================================================================");
                    writer.WriteLine();
                    writer.WriteLine($"Dataset: {pnm.Name}");
                    writer.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine();
                    writer.WriteLine("NETWORK PROPERTIES");
                    writer.WriteLine("------------------");
                    writer.WriteLine($"  Pores:                {results.PoreCount:N0}");
                    writer.WriteLine($"  Throats:              {results.ThroatCount:N0}");
                    writer.WriteLine($"  Voxel Size:           {results.VoxelSize:F3} μm");
                    writer.WriteLine($"  Tortuosity (τ):       {results.Tortuosity:F4}");
                    writer.WriteLine($"  τ² Correction:        {1.0f/(results.Tortuosity*results.Tortuosity):F4}");
                    writer.WriteLine();
                    writer.WriteLine("FLOW CONFIGURATION");
                    writer.WriteLine("------------------");
                    writer.WriteLine($"  Flow Axis:            {results.FlowAxis}");
                    writer.WriteLine($"  Model Length:         {results.ModelLength * 1e6:F3} μm");
                    writer.WriteLine($"  Cross-sectional Area: {results.CrossSectionalArea * 1e12:F3} μm²");
                    writer.WriteLine($"  Inlet Pressure:       {_inletPressure:F3} Pa");
                    writer.WriteLine($"  Outlet Pressure:      {_outletPressure:F3} Pa");
                    writer.WriteLine($"  Pressure Drop:        {results.UsedPressureDrop:F3} Pa");
                    writer.WriteLine($"  Fluid Viscosity:      {results.UsedViscosity:F3} cP");
                    writer.WriteLine($"  Total Flow Rate:      {results.TotalFlowRate:E3} m³/s");
                    writer.WriteLine();
                    writer.WriteLine("PERMEABILITY RESULTS");
                    writer.WriteLine("--------------------");
                    
                    if (results.DarcyUncorrected > 0)
                    {
                        writer.WriteLine("  Darcy Method:");
                        writer.WriteLine($"    Uncorrected:        {results.DarcyUncorrected:F6} mD ({results.DarcyUncorrected/1000:F9} D)");
                        writer.WriteLine($"    τ²-Corrected:       {results.DarcyCorrected:F6} mD ({results.DarcyCorrected/1000:F9} D)");
                    }
                    
                    if (results.NavierStokesUncorrected > 0)
                    {
                        writer.WriteLine("  Navier-Stokes Method:");
                        writer.WriteLine($"    Uncorrected:        {results.NavierStokesUncorrected:F6} mD ({results.NavierStokesUncorrected/1000:F9} D)");
                        writer.WriteLine($"    τ²-Corrected:       {results.NavierStokesCorrected:F6} mD ({results.NavierStokesCorrected/1000:F9} D)");
                    }
                    
                    if (results.LatticeBoltzmannUncorrected > 0)
                    {
                        writer.WriteLine("  Lattice-Boltzmann Method:");
                        writer.WriteLine($"    Uncorrected:        {results.LatticeBoltzmannUncorrected:F6} mD ({results.LatticeBoltzmannUncorrected/1000:F9} D)");
                        writer.WriteLine($"    τ²-Corrected:       {results.LatticeBoltzmannCorrected:F6} mD ({results.LatticeBoltzmannCorrected/1000:F9} D)");
                    }
                    
                    writer.WriteLine();
                    writer.WriteLine("================================================================================");
                }
            }
        }

        private void StartCalculation(PermeabilityOptions options)
        {
            _isCalculating = true;
            _calculationStatus = "Initializing...";

            Task.Run(() =>
            {
                try
                {
                    if (options.CalculateDarcy)
                        _calculationStatus = "Calculating Darcy permeability...";
                    
                    if (options.CalculateNavierStokes)
                        _calculationStatus = "Calculating Navier-Stokes permeability...";
                    
                    if (options.CalculateLatticeBoltzmann)
                        _calculationStatus = "Calculating Lattice-Boltzmann permeability...";
                    
                    AbsolutePermeability.Calculate(options);
                    _lastResults = AbsolutePermeability.GetLastResults();
                    _calculationStatus = "Calculation completed!";

                    ProjectManager.Instance.NotifyDatasetDataChanged(options.Dataset);
                }
                catch (Exception ex)
                {
                    _calculationStatus = $"Error: {ex.Message}";
                    Logger.LogError($"[Permeability] Calculation failed: {ex}");
                }
                finally
                {
                    System.Threading.Thread.Sleep(1000);
                    _isCalculating = false;
                }
            });
        }
    }
}