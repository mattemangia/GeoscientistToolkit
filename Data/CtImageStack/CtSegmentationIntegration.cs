// GeoscientistToolkit/Data/CtImageStack/CtSegmentationIntegration.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack.Segmentation;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtSegmentationIntegration : IDisposable
    {
        private SegmentationManager _segmentationManager;
        private InterpolationManager _interpolationManager;
        private ISegmentationTool _activeTool;
        private bool _isSegmentationActive = false;
        private byte[] _currentPreviewMask;
        private int _currentPreviewSlice;
        private int _currentPreviewView;
        
        // Tool instances
        private readonly BrushTool _brushTool;
        private readonly LassoTool _lassoTool;
        private readonly MagneticLassoTool _magneticLassoTool;
        private readonly MagicWandTool _magicWandTool;
        
        // Interpolation state
        private bool _interpolationMode = false;
        private byte[] _interpolationStartMask;
        private int _interpolationStartSlice;
        private int _interpolationStartView;
        
        // Merge materials state
        private bool _showMergeDialog = false;
        private Material _mergeSource;
        private Material _mergeTarget;
        
        // Select only from material option
        private bool _selectOnlyFromMaterial = false;

        // Static instance management
        private static readonly Dictionary<CtImageStackDataset, CtSegmentationIntegration> _activeInstances = new Dictionary<CtImageStackDataset, CtSegmentationIntegration>();

        // Public properties
        public bool HasActiveSelection => _activeTool?.HasActiveSelection ?? false;

        public byte TargetMaterialId
        {
            get => _segmentationManager.TargetMaterialId;
            set => _segmentationManager.TargetMaterialId = value;
        }

        public static void Initialize(CtImageStackDataset dataset)
        {
            if (!_activeInstances.ContainsKey(dataset))
            {
                _activeInstances[dataset] = new CtSegmentationIntegration(dataset);
            }
        }

        public static CtSegmentationIntegration GetInstance(CtImageStackDataset dataset)
        {
            _activeInstances.TryGetValue(dataset, out var instance);
            return instance;
        }

        public static void Cleanup(CtImageStackDataset dataset)
        {
            if (_activeInstances.TryGetValue(dataset, out var instance))
            {
                instance.Dispose();
                _activeInstances.Remove(dataset);
            }
        }
        
        private CtSegmentationIntegration(CtImageStackDataset dataset)
        {
            _segmentationManager = new SegmentationManager(dataset);
            _interpolationManager = new InterpolationManager(dataset, _segmentationManager);
            
            _brushTool = new BrushTool();
            _lassoTool = new LassoTool();
            _magneticLassoTool = new MagneticLassoTool();
            _magicWandTool = new MagicWandTool();
            
            _segmentationManager.SelectionPreviewChanged += OnSelectionPreviewChanged;
            _segmentationManager.SelectionCompleted += OnSelectionCompleted;
        }
        
        public void DrawSegmentationControls(Material selectedMaterial)
        {
            if (ImGui.CollapsingHeader("Interactive Segmentation", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                
                // Tool selection
                ImGui.Text("Select Tool:");
                
                if (ImGui.Button("Brush", new Vector2(65, 30))) { SetActiveTool(_brushTool); }
                ImGui.SameLine();
                if (ImGui.Button("Lasso", new Vector2(65, 30))) { SetActiveTool(_lassoTool); }
                ImGui.SameLine();
                if (ImGui.Button("Magnetic", new Vector2(65, 30))) { SetActiveTool(_magneticLassoTool); }
                ImGui.SameLine();
                if (ImGui.Button("Wand", new Vector2(65, 30))) { SetActiveTool(_magicWandTool); }
                ImGui.SameLine();
                if (ImGui.Button("Off", new Vector2(40, 30))) { SetActiveTool(null); }

                ImGui.Separator();
                
                // Mode selection
                bool isAdd = _segmentationManager.IsAddMode;
                if (ImGui.RadioButton("Add", isAdd)) { _segmentationManager.IsAddMode = true; }
                ImGui.SameLine();
                if (ImGui.RadioButton("Remove", !isAdd)) { _segmentationManager.IsAddMode = false; }
                
                // Select only from material checkbox
                ImGui.SameLine();
                if (ImGui.Checkbox("Select only from material", ref _selectOnlyFromMaterial))
                {
                    // Update magic wand setting
                    _magicWandTool.SelectOnlyFromCurrentMaterial = _selectOnlyFromMaterial;
                }
                
                // Target material
                if (selectedMaterial != null)
                {
                    _segmentationManager.TargetMaterialId = selectedMaterial.ID;
                    ImGui.Text($"Target: {selectedMaterial.Name}");
                } else
                {
                     ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "Select a material to enable tools.");
                }
                
                ImGui.Separator();
                
                if (selectedMaterial == null) ImGui.BeginDisabled();
                DrawToolControls();
                if (selectedMaterial == null) ImGui.EndDisabled();

                ImGui.Separator();
                
                // Action buttons
                ImGui.Text("Actions:");
                
                if (ImGui.Button("Extract Selection", new Vector2(140, 0)))
                {
                    _ = _segmentationManager.ExtractSelectionToNewMaterialAsync();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Create a new material from the current selection");
                }
                
                ImGui.SameLine();
                if (ImGui.Button("Erase Masks", new Vector2(140, 0)))
                {
                    _ = _segmentationManager.EraseActiveSelectionsAsync();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Remove all active selection masks from their slices");
                }
                
                if (ImGui.Button("Merge Materials...", new Vector2(140, 0)))
                {
                    _showMergeDialog = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Merge one material into another");
                }
                
                ImGui.SameLine();
                if (ImGui.Button("Clear Selections", new Vector2(140, 0)))
                {
                    _segmentationManager.ClearActiveSelections();
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Clear all active selections without applying them");
                }
                
                ImGui.Separator();
                
                DrawInterpolationControls();
                
                ImGui.Separator();
                
                if (ImGui.Button("Undo")) { _segmentationManager.Undo(); }
                ImGui.SameLine();
                if (ImGui.Button("Redo")) { _segmentationManager.Redo(); }
                
                ImGui.Unindent();
            }
            
            // Draw merge dialog
            DrawMergeDialog();
        }
        
        private void DrawToolControls()
        {
            if (_activeTool is BrushTool brush)
            {
                ImGui.Text("Brush Settings:");
                float brushSize = brush.BrushSize;
                float hardness = brush.Hardness;
                if (ImGui.SliderFloat("Size", ref brushSize, 1.0f, 100.0f)) { brush.BrushSize = brushSize; }
                if (ImGui.SliderFloat("Hardness", ref hardness, 0.0f, 1.0f)) { brush.Hardness = hardness; }
                
                int shape = (int)brush.Shape;
                if (ImGui.Combo("Shape", ref shape, "Circle\0Square\0"))
                {
                    brush.Shape = (BrushTool.BrushShape)shape;
                }
            }
            else if (_activeTool is MagneticLassoTool magnetic)
            {
                ImGui.Text("Magnetic Lasso Settings:");
                float edgeSensitivity = magnetic.EdgeSensitivity;
                float searchRadius = magnetic.SearchRadius;
                if(ImGui.SliderFloat("Edge Sensitivity", ref edgeSensitivity, 0.0f, 1.0f)) { magnetic.EdgeSensitivity = edgeSensitivity; }
                if(ImGui.SliderFloat("Search Radius", ref searchRadius, 10.0f, 100.0f)) { magnetic.SearchRadius = searchRadius; }
            }
            else if (_activeTool is MagicWandTool wand)
            {
                ImGui.Text("Magic Wand Settings:");
                int tolerance = wand.Tolerance;
                if (ImGui.SliderInt("Tolerance", ref tolerance, 0, 100))
                {
                    wand.Tolerance = (byte)tolerance;
                }
                
                // The checkbox state is already synced from the main UI
                bool selectOnly = wand.SelectOnlyFromCurrentMaterial;
                if (selectOnly)
                {
                    ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f), "Selecting only from current material");
                }
            }
        }
        
        private void DrawInterpolationControls()
        {
            ImGui.Text("Interpolation:");
            
            if (ImGui.Checkbox("Interpolation Mode", ref _interpolationMode))
            {
                if (!_interpolationMode) { _interpolationStartMask = null; }
            }
            
            if (_interpolationMode)
            {
                ImGui.TextWrapped("Select two slices with the same tool to interpolate between them.");
                
                if (_interpolationStartMask != null)
                {
                    ImGui.Text($"Start slice: {_interpolationStartSlice}");
                    ImGui.Text("Select end slice...");
                }
                
                int interpType = 0;
                if (ImGui.Combo("Type", ref interpType, "Linear 2D\0Shape-based\0Morphological 3D\0")) { /* Store preference */ }
            }
        }
        
        private void DrawMergeDialog()
        {
            if (!_showMergeDialog) return;
            
            ImGui.SetNextWindowSize(new Vector2(400, 200), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Merge Materials", ref _showMergeDialog))
            {
                var dataset = _segmentationManager.GetDataset();
                var materials = dataset.Materials.Where(m => m.ID != 0).ToList();
                
                ImGui.Text("Select materials to merge:");
                ImGui.Separator();
                
                // Source material selection
                ImGui.Text("Merge from:");
                ImGui.SetNextItemWidth(200);
                if (ImGui.BeginCombo("##source", _mergeSource?.Name ?? "Select source..."))
                {
                    foreach (var mat in materials)
                    {
                        if (ImGui.Selectable(mat.Name, mat == _mergeSource))
                        {
                            _mergeSource = mat;
                        }
                    }
                    ImGui.EndCombo();
                }
                
                // Target material selection
                ImGui.Text("Merge into:");
                ImGui.SetNextItemWidth(200);
                if (ImGui.BeginCombo("##target", _mergeTarget?.Name ?? "Select target..."))
                {
                    foreach (var mat in materials.Where(m => m != _mergeSource))
                    {
                        if (ImGui.Selectable(mat.Name, mat == _mergeTarget))
                        {
                            _mergeTarget = mat;
                        }
                    }
                    ImGui.EndCombo();
                }
                
                ImGui.Separator();
                
                if (_mergeSource == null || _mergeTarget == null)
                {
                    ImGui.BeginDisabled();
                }
                
                if (ImGui.Button("Merge", new Vector2(100, 0)))
                {
                    _ = _segmentationManager.MergeMaterialsAsync(_mergeSource.ID, _mergeTarget.ID);
                    _showMergeDialog = false;
                    _mergeSource = null;
                    _mergeTarget = null;
                }
                
                if (_mergeSource == null || _mergeTarget == null)
                {
                    ImGui.EndDisabled();
                }
                
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(100, 0)))
                {
                    _showMergeDialog = false;
                }
                
                if (_mergeSource != null && _mergeTarget != null)
                {
                    ImGui.Separator();
                    ImGui.TextWrapped($"This will merge all voxels from '{_mergeSource.Name}' into '{_mergeTarget.Name}' and remove '{_mergeSource.Name}' from the material list.");
                }
            }
            ImGui.End();
        }
        
        private void SetActiveTool(ISegmentationTool tool)
        {
            _activeTool?.CancelSelection();
            _activeTool = tool;
            _segmentationManager.CurrentTool = tool;
            _isSegmentationActive = tool != null;
            
            // Apply the select only from material setting to magic wand
            if (tool is MagicWandTool wand)
            {
                wand.SelectOnlyFromCurrentMaterial = _selectOnlyFromMaterial;
            }
        }
        
        public void HandleMouseInput(Vector2 mousePos, int sliceIndex, int viewIndex, bool isDown, bool isDragging, bool isReleased)
        {
            if (!_isSegmentationActive || _activeTool == null) return;
            
            if (isDown) { _segmentationManager.StartSelection(mousePos, sliceIndex, viewIndex); }
            else if (isDragging) { _segmentationManager.UpdateSelection(mousePos); }
            else if (isReleased)
            {
                if (_interpolationMode && _activeTool.HasActiveSelection) { HandleInterpolation(sliceIndex, viewIndex); }
                else { _segmentationManager.EndSelection(); }
            }
        }
        
        private void HandleInterpolation(int sliceIndex, int viewIndex)
        {
            var currentMask = _activeTool.GetSelectionMask();
            
            if (_interpolationStartMask == null)
            {
                _interpolationStartMask = (byte[])currentMask.Clone();
                _interpolationStartSlice = sliceIndex;
                _interpolationStartView = viewIndex;
                _segmentationManager.EndSelection();
            }
            else if (viewIndex == _interpolationStartView)
            {
                _interpolationManager.InterpolateSlicesAsync(
                    _interpolationStartMask, _interpolationStartSlice,
                    currentMask, sliceIndex,
                    viewIndex, InterpolationManager.InterpolationType.ShapeInterpolation);
                
                _interpolationStartMask = null;
                _segmentationManager.EndSelection();
            }
        }
        
        private void OnSelectionPreviewChanged(byte[] mask, int sliceIndex, int viewIndex)
        {
            _currentPreviewMask = mask;
            _currentPreviewSlice = sliceIndex;
            _currentPreviewView = viewIndex;
        }
        
        private void OnSelectionCompleted() { _currentPreviewMask = null; }
        
        public byte[] GetPreviewMask(int sliceIndex, int viewIndex)
        {
            if (_currentPreviewSlice == sliceIndex && _currentPreviewView == viewIndex) { return _currentPreviewMask; }
            return null;
        }
        
        public void Dispose()
        {
            _brushTool?.Dispose();
            _lassoTool?.Dispose();
            _magneticLassoTool?.Dispose();
            _magicWandTool?.Dispose();
            _segmentationManager?.Dispose();
        }
    }
}