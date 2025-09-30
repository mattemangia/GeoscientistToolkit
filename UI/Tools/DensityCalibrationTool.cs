// GeoscientistToolkit/UI/Tools/DensityCalibrationTool.cs
using System;
using System.Collections.Generic;
using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Tools
{
    /// <summary>
    /// Tool for calibrating material densities using physical measurements
    /// </summary>
    public class DensityCalibrationTool : IDatasetTools
    {
        private class CalibrationRecord
        {
            public string MaterialName { get; set; }
            public DateTime Timestamp { get; set; }
            public float MeasuredMass { get; set; } // grams
            public float MeasuredVolume { get; set; } // cm³
            public float CalculatedDensity { get; set; } // g/cm³
            public string Notes { get; set; }
        }

        private CtImageStackDataset _currentDataset;
        private int _selectedMaterialIndex = 0;
        private float _measuredMass = 100.0f;
        private float _measuredVolume = 50.0f;
        private float _calculatedDensity = 2.0f;
        private bool _autoApplyToMaterial = true;
        private string _calibrationNotes = "";
        private List<CalibrationRecord> _calibrationHistory = new List<CalibrationRecord>();

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ctDataset)
            {
                ImGui.TextDisabled("Density calibration requires a CT Image Stack dataset.");
                return;
            }

            _currentDataset = ctDataset;

            ImGui.SeparatorText("Density Calibration");

            // Material selection
            var nonExteriorMaterials = _currentDataset.Materials.Where(m => m.ID != 0).ToList();
            if (nonExteriorMaterials.Count == 0)
            {
                ImGui.TextDisabled("No materials available for calibration. Create materials first.");
                return;
            }

            string[] materialNames = nonExteriorMaterials.Select(m => m.Name).ToArray();
            if (_selectedMaterialIndex >= materialNames.Length)
                _selectedMaterialIndex = 0;

            ImGui.Text("Target Material:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##CalibrationMaterial", ref _selectedMaterialIndex, materialNames, materialNames.Length))
            {
                UpdateDensityDisplay(nonExteriorMaterials);
            }

            var selectedMaterial = nonExteriorMaterials[_selectedMaterialIndex];

            // Current material info
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1), $"Current Density: {selectedMaterial.Density:F3} g/cm³");
            
            if (!string.IsNullOrEmpty(selectedMaterial.PhysicalMaterialName))
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1), $"(from {selectedMaterial.PhysicalMaterialName})");
            }

            ImGui.Separator();
            ImGui.Spacing();

            // Measurement input
            ImGui.Text("Physical Measurements:");
            
            bool measurementChanged = false;
            
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputFloat("Mass (g)##MassInput", ref _measuredMass, 0.1f, 1.0f, "%.1f"))
            {
                measurementChanged = true;
            }
            
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputFloat("Volume (cm³)##VolumeInput", ref _measuredVolume, 0.1f, 1.0f, "%.1f"))
            {
                measurementChanged = true;
            }

            if (measurementChanged)
            {
                RecalculateDensity();
                if (_autoApplyToMaterial)
                {
                    ApplyDensityToMaterial();
                }
            }

            // Density result
            ImGui.Spacing();
            ImGui.Text("Calculated Density:");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"{_calculatedDensity:F3} g/cm³");
            
            // Convert to kg/m³ for reference
            float densityKgM3 = _calculatedDensity * 1000f;
            ImGui.SameLine();
            ImGui.TextDisabled($"(≈{densityKgM3:F0} kg/m³)");

            // Notes
            ImGui.Spacing();
            ImGui.Text("Calibration Notes:");
            ImGui.InputTextMultiline("##CalibrationNotes", ref _calibrationNotes, 256, new Vector2(-1, 60));

            // Auto-apply option
            ImGui.Spacing();
            ImGui.Checkbox("Auto-apply to material", ref _autoApplyToMaterial);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Automatically update material density when measurements change");
            }

            // Action buttons
            ImGui.Spacing();
            if (ImGui.Button("Apply to Material", new Vector2(120, 0)))
            {
                ApplyDensityToMaterial();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Save Calibration Record", new Vector2(150, 0)))
            {
                SaveCalibrationRecord();
            }

            ImGui.SameLine();
            if (ImGui.Button("Reset", new Vector2(80, 0)))
            {
                ResetMeasurements();
            }

            // Material Library Integration
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.Text("Material Library Integration:");
            
            var physicalMaterials = MaterialLibrary.Instance.Materials;
            var libraryNames = new List<string> { "None (Custom Properties)" };
            libraryNames.AddRange(physicalMaterials.Select(m => m.Name));
            
            int currentSelection = 0;
            if (!string.IsNullOrEmpty(selectedMaterial.PhysicalMaterialName))
            {
                currentSelection = libraryNames.FindIndex(n => n == selectedMaterial.PhysicalMaterialName);
                if (currentSelection < 0) currentSelection = 0;
            }
            
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##LibraryMaterial", ref currentSelection, libraryNames.ToArray(), libraryNames.Count))
            {
                if (currentSelection == 0)
                {
                    selectedMaterial.PhysicalMaterialName = null;
                }
                else
                {
                    var physMat = physicalMaterials[currentSelection - 1];
                    selectedMaterial.PhysicalMaterialName = physMat.Name;
                    if (physMat.Density_kg_m3.HasValue)
                    {
                        selectedMaterial.Density = physMat.Density_kg_m3.Value / 1000.0;
                        UpdateDensityDisplay(nonExteriorMaterials);
                    }
                    _currentDataset.SaveMaterials();
                }
            }

            // Calibration history
            if (_calibrationHistory.Count > 0)
            {
                ImGui.Separator();
                ImGui.Spacing();
                ImGui.Text("Calibration History:");
                
                if (ImGui.BeginChild("CalibrationHistory", new Vector2(0, 150), ImGuiChildFlags.AlwaysAutoResize))
                {
                    foreach (var record in _calibrationHistory)
                    {
                        ImGui.Text($"{record.Timestamp:MM/dd HH:mm} - {record.MaterialName}:");
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0, 1, 0, 1), $"{record.CalculatedDensity:F3} g/cm³");
                        
                        if (!string.IsNullOrEmpty(record.Notes))
                        {
                            ImGui.SameLine();
                            ImGui.TextDisabled($"({record.Notes})");
                        }
                    }
                }
                ImGui.EndChild();
                
                if (ImGui.Button("Clear History", new Vector2(120, 0)))
                {
                    _calibrationHistory.Clear();
                }
            }
        }

        private void UpdateDensityDisplay(List<Material> materials)
        {
            if (_selectedMaterialIndex < materials.Count)
            {
                var material = materials[_selectedMaterialIndex];
                _calculatedDensity = (float)material.Density;
            }
        }

        private void RecalculateDensity()
        {
            if (_measuredVolume > 0)
            {
                _calculatedDensity = _measuredMass / _measuredVolume;
            }
        }

        private void ApplyDensityToMaterial()
        {
            if (_currentDataset == null) return;

            var nonExteriorMaterials = _currentDataset.Materials.Where(m => m.ID != 0).ToList();
            if (_selectedMaterialIndex < nonExteriorMaterials.Count)
            {
                var material = nonExteriorMaterials[_selectedMaterialIndex];
                material.Density = _calculatedDensity;
                
                // Clear physical material assignment when manually setting density
                if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                {
                    material.PhysicalMaterialName = null;
                }
                
                _currentDataset.SaveMaterials();
                
                GeoscientistToolkit.Util.Logger.Log($"[DensityCalibration] Set density for {material.Name}: {material.Density:F3} g/cm³");
            }
        }

        private void SaveCalibrationRecord()
        {
            if (_currentDataset == null) return;

            var nonExteriorMaterials = _currentDataset.Materials.Where(m => m.ID != 0).ToList();
            if (_selectedMaterialIndex < nonExteriorMaterials.Count)
            {
                var material = nonExteriorMaterials[_selectedMaterialIndex];
                
                var record = new CalibrationRecord
                {
                    MaterialName = material.Name,
                    Timestamp = DateTime.Now,
                    MeasuredMass = _measuredMass,
                    MeasuredVolume = _measuredVolume,
                    CalculatedDensity = _calculatedDensity,
                    Notes = _calibrationNotes
                };
                
                _calibrationHistory.Insert(0, record);
                _calibrationHistory = _calibrationHistory.Take(50).ToList(); // Keep only recent 50
                
                GeoscientistToolkit.Util.Logger.Log($"[DensityCalibration] Saved calibration record for {material.Name}");
            }
        }

        private void ResetMeasurements()
        {
            _measuredMass = 100.0f;
            _measuredVolume = 50.0f;
            _calibrationNotes = "";
            RecalculateDensity();
        }
    }
}