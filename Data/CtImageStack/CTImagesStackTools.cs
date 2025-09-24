// GeoscientistToolkit/UI/Tools/CtImageStackTools.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit
{
    /// <summary>
    /// Provides all segmentation tools (threshold and interactive) for CtImageStackDataset.
    /// Manages 3D previews for segmentation and other analysis tools.
    /// </summary>
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


        /// <summary>
        /// Allows external tools to update a 3D preview mask for a specific dataset.
        /// </summary>
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
                // Notify viewers that preview data has changed
                Business.ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
                Preview3DChanged?.Invoke(dataset, mask, color);
                PreviewChanged?.Invoke(dataset);
            }
        }

        /// <summary>
        /// Gets the current preview data for a given dataset.
        /// </summary>
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

        /// <summary>
        /// Draws the complete segmentation tools UI, including thresholding and interactive tools.
        /// </summary>
        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ctDataset) return;
            
            // --- FIXED: Ensure we have the correct singleton instance for the current dataset ---
            if (_currentDataset != ctDataset)
            {
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
            string[] materialNames = materials.Select(m => m.Name).ToArray();
            ImGui.SetNextItemWidth(-1);

            // Ensure index is valid
            if (_selectedMaterialIndex >= materialNames.Length)
            {
                _selectedMaterialIndex = 0;
            }

            if (ImGui.Combo("Target Material##Threshold", ref _selectedMaterialIndex, materialNames, materialNames.Length))
            {
                _selectedMaterialForThresholding = materials[_selectedMaterialIndex];
            }
            _selectedMaterialForThresholding ??= materials.FirstOrDefault();

            ImGui.Text("Grayscale Range:");
            int min = _minThreshold, max = _maxThreshold;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f - 5);
            if (ImGui.DragInt("Min", ref min, 1, 0, 255)) _minThreshold = (byte)min;
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (ImGui.DragInt("Max", ref max, 1, 0, 255)) _maxThreshold = (byte)max;

            ImGui.Spacing();

            if (_selectedMaterialForThresholding == null)
            {
                ImGui.BeginDisabled();
            }

            if (ImGui.Button("Add to Material", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
                _ = MaterialOperations.AddVoxelsByThresholdAsync(ctDataset.VolumeData, ctDataset.LabelData,
                    _selectedMaterialForThresholding.ID, _minThreshold, _maxThreshold, ctDataset);
            }

            if (ImGui.Button("Remove from Material", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            {
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
                // Pass the material selected in the thresholding tool to link the two sections.
                _interactiveSegmentation.DrawSegmentationControls(_selectedMaterialForThresholding);
            }
        }
    }
}
