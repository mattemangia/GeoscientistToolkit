// GeoscientistToolkit/Data/CtImageStack/CtImageStackTools.cs
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackTools : IDatasetTools
    {
        private CtImageStackDataset _currentDataset;

        // Thresholding state
        private byte _minThreshold = 30;
        private byte _maxThreshold = 200;
        private bool _showPreview = false;
        private bool _show3DPreview = false;
        private byte[] _previewMask = null;
        private byte[] _preview3DMask = null;
        private bool _previewNeedsUpdate = true;
        private bool _preview3DNeedsUpdate = true;
        private int _previewSlice = -1;

        // Material selection state
        private Material _selectedMaterialForEditing;
        private Material _selectedMaterialForThresholding;

        // Preview change events
        public static event Action<CtImageStackDataset> PreviewChanged;
        public static event Action<CtImageStackDataset, byte[], Vector4> Preview3DChanged;

        // Static instance tracking for accessing preview data
        private static readonly Dictionary<CtImageStackDataset, CtImageStackTools> _activeTools = new Dictionary<CtImageStackDataset, CtImageStackTools>();

        // --- FIX: Statistics window management ---
        private MaterialStatisticsWindow _statisticsWindow;
        private bool _isStatsWindowOpen = false;


        public CtImageStackTools()
        {
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ctDataset) return;

            if (_currentDataset != ctDataset)
            {
                _currentDataset = ctDataset;
                // --- FIX: Initialize window instance, but don't show it yet ---
                _statisticsWindow = new MaterialStatisticsWindow(_currentDataset);
                _isStatsWindowOpen = false; // Ensure it's closed when switching datasets
            }

            // Register this instance for static access
            _activeTools[ctDataset] = this;

            // Initialize material selections if they are null
            if (_selectedMaterialForEditing == null && _currentDataset.Materials.Any(m => m.ID != 0))
                _selectedMaterialForEditing = _currentDataset.Materials.First(m => m.ID != 0);
            if (_selectedMaterialForThresholding == null && _currentDataset.Materials.Any(m => m.ID != 0))
                _selectedMaterialForThresholding = _currentDataset.Materials.First(m => m.ID != 0);

            DrawVolumeInfo();
            ImGui.Separator();
            DrawMaterialEditor();
            ImGui.Separator();
            DrawSegmentationTools();

            // --- FIX: Correctly submit the statistics window for drawing ---
            // The Submit method from BasePanel handles everything (drawing, state, pop-out).
            if (_isStatsWindowOpen)
            {
                _statisticsWindow?.Submit(ref _isStatsWindowOpen);
            }
        }

        public static (bool isActive, byte[] mask, Vector4 color) GetPreviewData(CtImageStackDataset dataset, int sliceZ)
        {
            if (_activeTools.TryGetValue(dataset, out var tools))
            {
                tools.UpdatePreviewForSlice(sliceZ);
                return (tools.IsPreviewActive(), tools.GetPreviewMask(), tools.GetPreviewColor());
            }
            return (false, null, Vector4.Zero);
        }

        public static (bool isActive, byte[] mask, Vector4 color) Get3DPreviewData(CtImageStackDataset dataset)
        {
            if (_activeTools.TryGetValue(dataset, out var tools))
            {
                return (tools.Is3DPreviewActive(), tools.Get3DPreviewMask(), tools.GetPreviewColor());
            }
            return (false, null, Vector4.Zero);
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
                ImGui.Spacing();

                // --- FIX: Set state flag to true instead of calling Open() ---
                if (ImGui.Button("Material Statistics..."))
                {
                    _isStatsWindowOpen = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Calculate and display statistics for each material.");
                }

                ImGui.Unindent();
            }
        }

        private void DrawMaterialEditor()
        {
            if (ImGui.CollapsingHeader("Material Editor", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                if (ImGui.BeginChild("MaterialList", new Vector2(0, 150), ImGuiChildFlags.Border))
                {
                    if (ImGui.BeginTable("MaterialTable", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                    {
                        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 30);
                        ImGui.TableSetupColumn("Visible", ImGuiTableColumnFlags.WidthFixed, 50);
                        ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 20);
                        ImGui.TableSetupScrollFreeze(0, 1);
                        ImGui.TableHeadersRow();

                        foreach (var material in _currentDataset.Materials.ToList())
                        {
                            if (material.ID == 0) continue;
                            ImGui.TableNextRow();
                            ImGui.PushID((int)material.ID);
                            ImGui.TableSetColumnIndex(0);
                            bool isSelected = _selectedMaterialForEditing == material;
                            if (ImGui.Selectable(material.Name, isSelected, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap))
                            {
                                _selectedMaterialForEditing = material;
                            }
                            ImGui.TableSetColumnIndex(1);
                            Vector4 color = material.Color;
                            if (ImGui.ColorEdit4($"##color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
                            {
                                material.Color = color;
                            }
                            ImGui.TableSetColumnIndex(2);
                            bool isVisible = material.IsVisible;
                            if (ImGui.Checkbox($"##vis", ref isVisible))
                            {
                                material.IsVisible = isVisible;
                            }
                            ImGui.PopID();
                        }
                        ImGui.EndTable();
                    }
                }
                ImGui.EndChild();

                if (ImGui.Button("Add Material"))
                {
                    byte newId = MaterialOperations.GetNextMaterialID(_currentDataset.Materials);
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
                    ImGui.OpenPopup("Rename Material");
                }
                if (_selectedMaterialForEditing == null || _selectedMaterialForEditing.ID == 0) ImGui.EndDisabled();
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
                    _previewNeedsUpdate = true;
                    _preview3DNeedsUpdate = true;
                    if (_showPreview) NotifyViewersOfPreviewChange();
                }

                string preview = _selectedMaterialForThresholding != null ? _selectedMaterialForThresholding.Name : "Select a material...";
                if (ImGui.BeginCombo("Target Material", preview))
                {
                    foreach (var mat in _currentDataset.Materials.Where(m => m.ID != 0))
                    {
                        if (ImGui.Selectable(mat.Name, mat == _selectedMaterialForThresholding))
                        {
                            _selectedMaterialForThresholding = mat;
                            if (_showPreview)
                            {
                                _previewNeedsUpdate = true;
                                _preview3DNeedsUpdate = true;
                                NotifyViewersOfPreviewChange();
                            }
                        }
                    }
                    ImGui.EndCombo();
                }

                if (ImGui.Checkbox("Preview Threshold (2D)", ref _showPreview))
                {
                    _previewNeedsUpdate = true;
                    if (!_showPreview) _show3DPreview = false;
                    NotifyViewersOfPreviewChange();
                }
                if (!_showPreview) ImGui.BeginDisabled();
                ImGui.SameLine();
                if (ImGui.Checkbox("3D Preview", ref _show3DPreview))
                {
                    _preview3DNeedsUpdate = true;
                    NotifyViewersOf3DPreviewChange();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Enable real-time 3D preview (may impact performance)");
                if (!_showPreview) ImGui.EndDisabled();

                if (_selectedMaterialForThresholding == null)
                {
                    ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), "Create a material to enable thresholding.");
                    ImGui.BeginDisabled();
                }
                if (ImGui.Button("Add by Threshold [+]"))
                {
                    _selectedMaterialForThresholding.MinValue = _minThreshold;
                    _selectedMaterialForThresholding.MaxValue = _maxThreshold;
                    _ = MaterialOperations.AddVoxelsByThresholdAsync(_currentDataset.VolumeData, _currentDataset.LabelData, _selectedMaterialForThresholding.ID, _minThreshold, _maxThreshold, _currentDataset);
                    _showPreview = false; _show3DPreview = false; _previewMask = null; _preview3DMask = null;
                    NotifyViewersOfPreviewChange(); NotifyViewersOf3DPreviewChange();
                }
                ImGui.SameLine();
                if (ImGui.Button("Remove by Threshold [-]"))
                {
                    _ = MaterialOperations.RemoveVoxelsByThresholdAsync(_currentDataset.VolumeData, _currentDataset.LabelData, _selectedMaterialForThresholding.ID, _minThreshold, _maxThreshold, _currentDataset);
                    _showPreview = false; _show3DPreview = false; _previewMask = null; _preview3DMask = null;
                    NotifyViewersOfPreviewChange(); NotifyViewersOf3DPreviewChange();
                }
                if (_selectedMaterialForThresholding == null) ImGui.EndDisabled();
                if (_showPreview || _show3DPreview)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1.0f), "Preview Active");
                    if (_show3DPreview) ImGui.TextWrapped("3D preview may impact performance. Disable if experiencing slowdowns.");
                }
                ImGui.Unindent();
            }
        }

        public void UpdatePreviewForSlice(int sliceZ) { if (!_showPreview || _currentDataset.VolumeData == null) { _previewMask = null; return; } if (_previewSlice != sliceZ || _previewNeedsUpdate) { _previewSlice = sliceZ; _previewNeedsUpdate = false; int width = _currentDataset.Width; int height = _currentDataset.Height; _previewMask = new byte[width * height]; var graySlice = new byte[width * height]; _currentDataset.VolumeData.ReadSliceZ(sliceZ, graySlice); for (int i = 0; i < graySlice.Length; i++) { byte gray = graySlice[i]; _previewMask[i] = (gray >= _minThreshold && gray <= _maxThreshold) ? (byte)255 : (byte)0; } } }
        private void Update3DPreview() { if (!_show3DPreview || _currentDataset.VolumeData == null) { _preview3DMask = null; return; } if (_preview3DNeedsUpdate) { _preview3DNeedsUpdate = false; int width = _currentDataset.Width; int height = _currentDataset.Height; int depth = _currentDataset.Depth; _preview3DMask = new byte[width * height * depth]; Parallel.For(0, depth, z => { for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) { byte gray = _currentDataset.VolumeData[x, y, z]; int index = z * width * height + y * width + x; _preview3DMask[index] = (gray >= _minThreshold && gray <= _maxThreshold) ? (byte)255 : (byte)0; } } }); Preview3DChanged?.Invoke(_currentDataset, _preview3DMask, GetPreviewColor()); } }
        public Material GetSelectedMaterialForThresholding() => _selectedMaterialForThresholding;
        public byte[] GetPreviewMask() => _previewMask;
        public byte[] Get3DPreviewMask() { if (_show3DPreview && _preview3DNeedsUpdate) { Update3DPreview(); } return _preview3DMask; }
        public bool IsPreviewActive() => _showPreview;
        public bool Is3DPreviewActive() => _show3DPreview;
        public Vector4 GetPreviewColor() => _selectedMaterialForThresholding?.Color ?? new Vector4(1, 0, 0, 0.5f);
        private void NotifyViewersOfPreviewChange() => PreviewChanged?.Invoke(_currentDataset);
        private void NotifyViewersOf3DPreviewChange() { if (_show3DPreview) { Update3DPreview(); } else { _preview3DMask = null; Preview3DChanged?.Invoke(_currentDataset, null, Vector4.Zero); } }
        public static void CleanupTools(CtImageStackDataset dataset) => _activeTools.Remove(dataset);
    }
}