// GeoscientistToolkit/Data/CtImageStack/CtImageStackTools.cs
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;
using System;
using System.Linq;
using System.Collections.Generic;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackTools : IDatasetTools
    {
        private CtImageStackDataset _currentDataset;
        
        // Thresholding state
        private byte _minThreshold = 30;
        private byte _maxThreshold = 200;
        
        // Material selection state
        private Material _selectedMaterialForEditing;
        private Material _selectedMaterialForThresholding;

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ctDataset) return;
            _currentDataset = ctDataset;

            // Initialize selection if null
            if (_selectedMaterialForEditing == null && _currentDataset.Materials.Count > 1)
                _selectedMaterialForEditing = _currentDataset.Materials[1];
            if (_selectedMaterialForThresholding == null && _currentDataset.Materials.Count > 1)
                _selectedMaterialForThresholding = _currentDataset.Materials[1];

            DrawVolumeInfo();
            ImGui.Separator();
            DrawMaterialEditor();
            ImGui.Separator();
            DrawSegmentationTools();
        }

        private void DrawVolumeInfo()
        {
            if (ImGui.CollapsingHeader("Volume Information", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                ImGui.Text($"Dimensions: {_currentDataset.Width} × {_currentDataset.Height} × {_currentDataset.Depth}");
                ImGui.Text($"Voxel Size: {_currentDataset.PixelSize:F2} × {_currentDataset.PixelSize:F2} × {_currentDataset.SliceThickness:F2} {_currentDataset.Unit}");
                
                if (_currentDataset.BinningSize > 1)
                {
                    ImGui.Text($"Binning: {_currentDataset.BinningSize}×{_currentDataset.BinningSize}");
                }
                
                float volumeMm3 = (_currentDataset.Width * _currentDataset.PixelSize / 1000f) * 
                                 (_currentDataset.Height * _currentDataset.PixelSize / 1000f) * 
                                 (_currentDataset.Depth * _currentDataset.SliceThickness / 1000f);
                ImGui.Text($"Physical Volume: {volumeMm3:F2} mm³");
                
                ImGui.Unindent();
            }
        }

        private void DrawMaterialEditor()
        {
            if (ImGui.CollapsingHeader("Material Editor", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                // --- Material List ---
                if (ImGui.BeginChild("MaterialList", new Vector2(0, 150), ImGuiChildFlags.Border))
                {
                    foreach (var material in _currentDataset.Materials.ToList())
                    {
                        if (material.ID == 0) continue; // Skip exterior

                        bool isSelected = _selectedMaterialForEditing == material;
                        if (ImGui.Selectable($"{material.Name}##{material.ID}", isSelected))
                        {
                            _selectedMaterialForEditing = material;
                        }

                        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 80);

                        // --- CORRECTED Color Edit ---
                        Vector4 color = material.Color;
                        if (ImGui.ColorEdit4($"##color{material.ID}", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                        {
                            material.Color = color;
                        }

                        ImGui.SameLine();
                        bool isVisible = material.IsVisible;
                        if (ImGui.Checkbox($"##visibility{material.ID}", ref isVisible))
                        {
                            material.IsVisible = isVisible;
                        }
                    }
                }
                ImGui.EndChild();

                // --- Material Buttons ---
                if (ImGui.Button("Add Material"))
                {
                    byte newId = MaterialOperations.GetNextMaterialID(_currentDataset.Materials);
                    // Generate a random color for the new material
                    var random = new Random();
                    var color = new Vector4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 1.0f);
                    var newMaterial = new Material(newId, $"Material {newId}", color);
                    _currentDataset.Materials.Add(newMaterial);
                    _selectedMaterialForEditing = newMaterial;
                }
                ImGui.SameLine();

                if (_selectedMaterialForEditing == null || _selectedMaterialForEditing.ID == 0) ImGui.BeginDisabled();
                if (ImGui.Button("Remove Selected"))
                {
                    _currentDataset.Materials.Remove(_selectedMaterialForEditing);
                    _selectedMaterialForEditing = _currentDataset.Materials.FirstOrDefault(m => m.ID != 0);
                }
                if (_selectedMaterialForEditing == null || _selectedMaterialForEditing.ID == 0) ImGui.EndDisabled();
                
                ImGui.SameLine();
                if (_selectedMaterialForEditing == null || _selectedMaterialForEditing.ID == 0) ImGui.BeginDisabled();
                if (ImGui.Button("Rename"))
                {
                    // Simple rename popup logic
                    ImGui.OpenPopup("Rename Material");
                }
                if (_selectedMaterialForEditing == null || _selectedMaterialForEditing.ID == 0) ImGui.EndDisabled();
                
                // Handle the rename popup
                if (ImGui.BeginPopup("Rename Material"))
                {
                    string name = _selectedMaterialForEditing.Name;
                    if (ImGui.InputText("New Name", ref name, 100, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        _selectedMaterialForEditing.Name = name;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }

                ImGui.Unindent();
            }
        }
        
        private void DrawSegmentationTools()
        {
            if (ImGui.CollapsingHeader("Segmentation Tools", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                ImGui.Text("Thresholding:");

                int min = _minThreshold;
                int max = _maxThreshold;

                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.DragIntRange2("##threshold", ref min, ref max, 1.0f, 0, 255, "Min: %d", "Max: %d"))
                {
                    _minThreshold = (byte)min;
                    _maxThreshold = (byte)max;
                }
                
                string preview = _selectedMaterialForThresholding != null ? _selectedMaterialForThresholding.Name : "Select a material...";
                if (ImGui.BeginCombo("Target Material", preview))
                {
                    foreach(var mat in _currentDataset.Materials.Where(m => m.ID != 0))
                    {
                        if (ImGui.Selectable(mat.Name, mat == _selectedMaterialForThresholding))
                        {
                            _selectedMaterialForThresholding = mat;
                        }
                    }
                    ImGui.EndCombo();
                }

                if (_selectedMaterialForThresholding == null)
                {
                    ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "Create a material to enable thresholding.");
                    ImGui.BeginDisabled();
                }

                if (ImGui.Button("Add by Threshold [+]"))
                {
                    _selectedMaterialForThresholding.MinValue = _minThreshold;
                    _selectedMaterialForThresholding.MaxValue = _maxThreshold;
                    MaterialOperations.AddVoxelsByThresholdAsync(_currentDataset.VolumeData, _currentDataset.LabelData, _selectedMaterialForThresholding.ID, _minThreshold, _maxThreshold);
                }
                ImGui.SameLine();
                if (ImGui.Button("Remove by Threshold [-]"))
                {
                    MaterialOperations.RemoveVoxelsByThresholdAsync(_currentDataset.VolumeData, _currentDataset.LabelData, _selectedMaterialForThresholding.ID, _minThreshold, _maxThreshold);
                }
                
                if (_selectedMaterialForThresholding == null)
                {
                    ImGui.EndDisabled();
                }

                ImGui.Unindent();
            }
        }
    }
}