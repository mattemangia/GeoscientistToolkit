// GeoscientistToolkit/UI/Tools/CtImageStackTools.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit
{
    public class CtImageStackTools : IDatasetTools
    {
        // --- Static events for external preview control ---
        public static event Action<CtImageStackDataset, byte[], Vector4> Preview3DChanged;
        public static event Action<CtImageStackDataset> PreviewChanged;

        // --- Static members for external preview control ---
        private static readonly object _previewLock = new object();
        private static WeakReference<Dataset> _previewDatasetRef;
        private static byte[] _external3DPreviewMask;
        private static Vector4 _externalPreviewColor;
        private static bool _isExternalPreviewActive = false;

        // --- Instance members for segmentation UI ---
        private byte _minThreshold = 0;
        private byte _maxThreshold = 255;
        private int _selectedMaterialIndex = 0;
        private Material _selectedMaterialForThresholding;
        private CtSegmentationIntegration _interactiveSegmentation;
        private CtImageStackDataset _currentDataset;
        
        // --- NEW: Physical material assignment state ---
        private Dictionary<byte, int> _physicalMaterialSelectionIndex = new Dictionary<byte, int>();
        
        // --- Threshold preview state ---
        private bool _showThresholdPreview = false;
        private byte[] _thresholdPreviewMask;
        private bool _isGeneratingPreview = false;
        private DateTime _lastPreviewUpdate = DateTime.MinValue;
        private const int PREVIEW_UPDATE_DELAY_MS = 250;

        public static void Update3DPreviewFromExternal(CtImageStackDataset dataset, byte[] mask, Vector4 color)
        {
            lock (_previewLock)
            {
                if (mask == null)
                {
                    _isExternalPreviewActive = false;
                    _external3DPreviewMask = null;
                    _previewDatasetRef = null;
                }
                else
                {
                    _previewDatasetRef = new WeakReference<Dataset>(dataset);
                    _external3DPreviewMask = mask;
                    _externalPreviewColor = color;
                    _isExternalPreviewActive = true;
                }
                Business.ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                Preview3DChanged?.Invoke(dataset, mask, color);
                PreviewChanged?.Invoke(dataset);
            }
        }

        public static (bool isActive, byte[] mask, Vector4 color) GetPreviewData(Dataset dataset)
        {
            lock (_previewLock)
            {
                if (_isExternalPreviewActive && _previewDatasetRef != null && _previewDatasetRef.TryGetTarget(out var target) && target == dataset)
                {
                    return (true, _external3DPreviewMask, _externalPreviewColor);
                }
                return (false, null, Vector4.Zero);
            }
        }
        
        public Material GetSelectedMaterialForThresholding()
        {
            return _selectedMaterialForThresholding;
        }

        private async Task UpdateThresholdPreviewAsync(CtImageStackDataset dataset)
        {
            if (_isGeneratingPreview) return;
            
            _isGeneratingPreview = true;
            
            try
            {
                await Task.Run(() =>
                {
                    var volume = dataset.VolumeData;
                    int width = dataset.Width;
                    int height = dataset.Height;
                    int depth = dataset.Depth;
                    
                    _thresholdPreviewMask = new byte[width * height * depth];
                    
                    Parallel.For(0, depth, z =>
                    {
                        var slice = new byte[width * height];
                        volume.ReadSliceZ(z, slice);
                        
                        for (int i = 0; i < slice.Length; i++)
                        {
                            if (slice[i] >= _minThreshold && slice[i] <= _maxThreshold)
                            {
                                _thresholdPreviewMask[z * width * height + i] = 255;
                            }
                        }
                    });
                });
                
                var previewColor = _selectedMaterialForThresholding?.Color ?? new Vector4(1, 0, 0, 0.5f);
                previewColor.W = 0.5f;
                Update3DPreviewFromExternal(dataset, _thresholdPreviewMask, previewColor);
                
                _lastPreviewUpdate = DateTime.Now;
            }
            finally
            {
                _isGeneratingPreview = false;
            }
        }

        private void ClearThresholdPreview()
        {
            if (_currentDataset != null && _showThresholdPreview)
            {
                Update3DPreviewFromExternal(_currentDataset, null, Vector4.Zero);
                _thresholdPreviewMask = null;
                _showThresholdPreview = false;
            }
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ctDataset) return;
            
            // Ensure MaterialLibrary is loaded
            if (MaterialLibrary.Instance.Materials.Count == 0)
            {
                MaterialLibrary.Instance.Load();
            }
            
            if (_currentDataset != ctDataset)
            {
                ClearThresholdPreview();
                _currentDataset = ctDataset;
                _interactiveSegmentation = CtSegmentationIntegration.GetInstance(ctDataset);
            }

            var materials = ctDataset.Materials.Where(m => m.ID != 0).ToList();
            if (materials.Count == 0)
            {
                ImGui.TextDisabled("No materials defined. Add/edit materials in the Properties panel.");
                return;
            }

            // --- THRESHOLD-BASED SEGMENTATION ---
            ImGui.SeparatorText("Threshold-based Segmentation");
            
            // Material selection combo
            string[] materialNames = materials.Select(m => m.Name).ToArray();
            ImGui.Text("Target Material:");
            ImGui.SetNextItemWidth(-1);

            if (_selectedMaterialIndex >= materialNames.Length)
            {
                _selectedMaterialIndex = 0;
            }

            if (ImGui.Combo("##TargetMaterial", ref _selectedMaterialIndex, materialNames, materialNames.Length))
            {
                _selectedMaterialForThresholding = materials[_selectedMaterialIndex];
            }
            _selectedMaterialForThresholding ??= materials.FirstOrDefault();

            // --- NEW: Physical Material Assignment Section ---
            if (_selectedMaterialForThresholding != null)
            {
                ImGui.Spacing();
                ImGui.Text("Physical Properties:");
                
                // Build dropdown options
                var physicalMaterials = MaterialLibrary.Instance.Materials.ToList();
                var libraryNames = new List<string> { "None (Custom Properties)" };
                libraryNames.AddRange(physicalMaterials.Select(m => m.Name));
                
                // Get current selection index for this material
                if (!_physicalMaterialSelectionIndex.ContainsKey(_selectedMaterialForThresholding.ID))
                {
                    // Initialize based on existing assignment
                    int index = 0;
                    if (!string.IsNullOrEmpty(_selectedMaterialForThresholding.PhysicalMaterialName))
                    {
                        index = libraryNames.FindIndex(n => n == _selectedMaterialForThresholding.PhysicalMaterialName);
                        if (index < 0) index = 0;
                    }
                    _physicalMaterialSelectionIndex[_selectedMaterialForThresholding.ID] = index;
                }
                
                int currentPhysIndex = _physicalMaterialSelectionIndex[_selectedMaterialForThresholding.ID];
                
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##PhysicalMaterial", ref currentPhysIndex, libraryNames.ToArray(), libraryNames.Count))
                {
                    _physicalMaterialSelectionIndex[_selectedMaterialForThresholding.ID] = currentPhysIndex;
                    
                    if (currentPhysIndex == 0)
                    {
                        // "None" selected
                        _selectedMaterialForThresholding.PhysicalMaterialName = null;
                        Logger.Log($"[Segmentation] Removed physical material assignment from {_selectedMaterialForThresholding.Name}");
                    }
                    else
                    {
                        // Physical material selected
                        var physMat = physicalMaterials[currentPhysIndex - 1];
                        _selectedMaterialForThresholding.PhysicalMaterialName = physMat.Name;
                        
                        // Auto-apply density if available
                        if (physMat.Density_kg_m3.HasValue)
                        {
                            _selectedMaterialForThresholding.Density = physMat.Density_kg_m3.Value / 1000.0; // Convert to g/cm³
                            Logger.Log($"[Segmentation] Assigned {physMat.Name} to {_selectedMaterialForThresholding.Name}, " +
                                     $"density: {_selectedMaterialForThresholding.Density:F3} g/cm³");
                        }
                    }
                    
                    // Save the changes immediately
                    ctDataset.SaveMaterials();
                }
                
                // Show current properties if a physical material is assigned
                if (!string.IsNullOrEmpty(_selectedMaterialForThresholding.PhysicalMaterialName))
                {
                    var physMat = MaterialLibrary.Instance.Find(_selectedMaterialForThresholding.PhysicalMaterialName);
                    if (physMat != null)
                    {
                        ImGui.Indent();
                        ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1), $"Using: {physMat.Name}");
                        
                        // Show key properties in a compact grid
                        ImGui.Columns(2, "PhysProps", false);
                        ImGui.SetColumnWidth(0, 120);
                        
                        if (physMat.Density_kg_m3.HasValue)
                        {
                            ImGui.Text("Density:");
                            ImGui.NextColumn();
                            ImGui.Text($"{physMat.Density_kg_m3.Value:F0} kg/m³");
                            ImGui.NextColumn();
                        }
                        
                        if (physMat.YoungModulus_GPa.HasValue)
                        {
                            ImGui.Text("Young's Mod:");
                            ImGui.NextColumn();
                            ImGui.Text($"{physMat.YoungModulus_GPa.Value:F1} GPa");
                            ImGui.NextColumn();
                        }
                        
                        if (physMat.PoissonRatio.HasValue)
                        {
                            ImGui.Text("Poisson's:");
                            ImGui.NextColumn();
                            ImGui.Text($"{physMat.PoissonRatio.Value:F3}");
                            ImGui.NextColumn();
                        }
                        
                        if (physMat.Vp_m_s.HasValue)
                        {
                            ImGui.Text("P-wave:");
                            ImGui.NextColumn();
                            ImGui.Text($"{physMat.Vp_m_s.Value:F0} m/s");
                            ImGui.NextColumn();
                        }
                        
                        ImGui.Columns(1);
                        
                        // Hover for full details
                        if (ImGui.IsItemHovered() && ImGui.BeginTooltip())
                        {
                            ImGui.Text($"Complete properties of {physMat.Name}:");
                            ImGui.Separator();
    
                            // Phase is not nullable, so no need to check HasValue
                            ImGui.Text($"Phase: {physMat.Phase}");
    
                            if (physMat.MohsHardness.HasValue)
                                ImGui.Text($"Mohs Hardness: {physMat.MohsHardness.Value:F1}");
                            if (physMat.ThermalConductivity_W_mK.HasValue)
                                ImGui.Text($"Thermal Conductivity: {physMat.ThermalConductivity_W_mK.Value:F2} W/mK");
                            if (physMat.Vs_m_s.HasValue)
                                ImGui.Text($"S-wave velocity: {physMat.Vs_m_s.Value:F0} m/s");
                            if (physMat.FrictionAngle_deg.HasValue)
                                ImGui.Text($"Friction Angle: {physMat.FrictionAngle_deg.Value:F1}°");
                            if (physMat.BreakingPressure_MPa.HasValue)
                                ImGui.Text($"Breaking Pressure: {physMat.BreakingPressure_MPa.Value:F0} MPa");
                            if (physMat.TypicalPorosity_fraction.HasValue)
                                ImGui.Text($"Typical Porosity: {physMat.TypicalPorosity_fraction.Value * 100:F1}%");
    
                            if (!string.IsNullOrEmpty(physMat.Notes))
                            {
                                ImGui.Separator();
                                ImGui.TextWrapped(physMat.Notes);
                            }
    
                            ImGui.EndTooltip();
                        }
                        
                        ImGui.Unindent();
                    }
                }
                
                // Manual density override
                float density = (float)_selectedMaterialForThresholding.Density;
                ImGui.Text("Density Override:");
                ImGui.SetNextItemWidth(150);
                if (ImGui.DragFloat("##Density", ref density, 0.01f, 0.0f, 10.0f, "%.3f g/cm³"))
                {
                    _selectedMaterialForThresholding.Density = density;
                    ctDataset.SaveMaterials();
                }
                
                if (!string.IsNullOrEmpty(_selectedMaterialForThresholding.PhysicalMaterialName))
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Reset"))
                    {
                        var physMat = MaterialLibrary.Instance.Find(_selectedMaterialForThresholding.PhysicalMaterialName);
                        if (physMat?.Density_kg_m3.HasValue == true)
                        {
                            _selectedMaterialForThresholding.Density = physMat.Density_kg_m3.Value / 1000.0;
                            ctDataset.SaveMaterials();
                        }
                    }
                }
                
                ImGui.Separator();
            }

            // --- THRESHOLD CONTROLS ---
            ImGui.Spacing();
            ImGui.Text("Grayscale Range:");
            
            bool thresholdChanged = false;
            int min = _minThreshold, max = _maxThreshold;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f - 5);
            if (ImGui.DragInt("Min", ref min, 1, 0, 255)) 
            {
                _minThreshold = (byte)min;
                thresholdChanged = true;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.DragInt("Max", ref max, 1, 0, 255)) 
            {
                _maxThreshold = (byte)max;
                thresholdChanged = true;
            }
            
            ImGui.SetNextItemWidth(-1);
            int range1 = _minThreshold, range2 = _maxThreshold;
            if (ImGui.DragIntRange2("Range", ref range1, ref range2, 1, 0, 255, "Min: %d", "Max: %d"))
            {
                _minThreshold = (byte)range1;
                _maxThreshold = (byte)range2;
                thresholdChanged = true;
            }

            // Preview controls
            ImGui.Spacing();
            bool prevShowPreview = _showThresholdPreview;
            if (ImGui.Checkbox("Show Preview", ref _showThresholdPreview))
            {
                if (_showThresholdPreview)
                {
                    _ = UpdateThresholdPreviewAsync(ctDataset);
                }
                else
                {
                    ClearThresholdPreview();
                }
            }
            
            if (_showThresholdPreview)
            {
                ImGui.SameLine();
                if (_isGeneratingPreview)
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "Generating...");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "Preview Active");
                    
                    if (thresholdChanged && (DateTime.Now - _lastPreviewUpdate).TotalMilliseconds > PREVIEW_UPDATE_DELAY_MS)
                    {
                        _ = UpdateThresholdPreviewAsync(ctDataset);
                    }
                }
                
                if (_thresholdPreviewMask != null)
                {
                    int voxelCount = _thresholdPreviewMask.Count(v => v > 0);
                    ImGui.Text($"Preview: {voxelCount:N0} voxels selected");
                }
            }

            ImGui.Spacing();

            // Action buttons
            if (_selectedMaterialForThresholding == null)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Add to Material", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                ClearThresholdPreview();
                _ = MaterialOperations.AddVoxelsByThresholdAsync(ctDataset.VolumeData, ctDataset.LabelData,
                    _selectedMaterialForThresholding.ID, _minThreshold, _maxThreshold, ctDataset);
            }

            if (ImGui.Button("Remove from Material", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                ClearThresholdPreview();
                _ = MaterialOperations.RemoveVoxelsByThresholdAsync(ctDataset.VolumeData, ctDataset.LabelData,
                    _selectedMaterialForThresholding.ID, _minThreshold, _maxThreshold, ctDataset);
            }

            if (_selectedMaterialForThresholding == null)
            {
                ImGui.EndDisabled();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // --- INTERACTIVE SEGMENTATION ---
            ImGui.SeparatorText("Interactive Segmentation Tools");
            if (_interactiveSegmentation != null)
            {
                _interactiveSegmentation.DrawSegmentationControls(_selectedMaterialForThresholding);
            }
        }
    }
}