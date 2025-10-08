// GeoscientistToolkit/Analysis/MaterialManager/MaterialManagerTool.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.MaterialManager;

/// <summary>
///     Material Manager tool for creating, editing, and managing materials in CT datasets.
///     Integrated into the segmentation workflow.
/// </summary>
public class MaterialManagerTool : IDatasetTools, IDisposable
{
    // Color presets for quick material creation
    private readonly Vector4[] _colorPresets = new[]
    {
        new Vector4(1.0f, 0.2f, 0.2f, 1.0f), // Red
        new Vector4(0.2f, 1.0f, 0.2f, 1.0f), // Green
        new Vector4(0.2f, 0.2f, 1.0f, 1.0f), // Blue
        new Vector4(1.0f, 1.0f, 0.2f, 1.0f), // Yellow
        new Vector4(1.0f, 0.2f, 1.0f, 1.0f), // Magenta
        new Vector4(0.2f, 1.0f, 1.0f, 1.0f), // Cyan
        new Vector4(1.0f, 0.6f, 0.2f, 1.0f), // Orange
        new Vector4(0.6f, 0.2f, 1.0f, 1.0f) // Purple
    };

    // --- Caching for voxel count statistics ---
    private readonly Dictionary<byte, int> _voxelCountCache = new();
    private WeakReference<CtImageStackDataset> _currentDatasetRef;
    private string _newMaterialName = "New Material";
    private bool _pendingSave;
    private string _renameBuf = string.Empty;
    private int _selectedMaterialId = -1;

    public MaterialManagerTool()
    {
        ProjectManager.Instance.DatasetDataChanged += OnDatasetDataChanged;
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ct) return;

        // Material creation section
        ImGui.SeparatorText("Material Manager");

        // Quick actions bar
        if (ImGui.Button("Save All Materials")) SaveMaterials(ct);
        ImGui.SameLine();
        if (ImGui.Button("Clear All Labels"))
        {
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl+Shift and click to clear all label data");
            if (ImGui.IsKeyDown(ImGuiKey.LeftCtrl) && ImGui.IsKeyDown(ImGuiKey.LeftShift)) ClearAllLabels(ct);
        }

        ImGui.Separator();

        // Add new material section
        ImGui.Text("Add New Material:");
        var availableWidth = ImGui.GetContentRegionAvail().X;
        ImGui.SetNextItemWidth(availableWidth - 80);
        ImGui.InputText("##NewMaterialName", ref _newMaterialName, 100);
        ImGui.SameLine();
        if (ImGui.Button("Add", new Vector2(75, 0))) AddNewMaterial(ct);

        // Quick color presets
        ImGui.Text("Quick Colors:");
        for (var i = 0; i < _colorPresets.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            ImGui.PushID($"preset_{i}");
            ImGui.ColorButton("##preset", _colorPresets[i], ImGuiColorEditFlags.NoTooltip, new Vector2(20, 20));
            if (ImGui.IsItemClicked()) AddNewMaterialWithColor(ct, _colorPresets[i]);
            ImGui.PopID();
        }

        ImGui.Separator();

        // Material list
        ImGui.Text($"Materials ({ct.Materials.Count(m => m.ID != 0)} defined):");

        ImGui.BeginChild("MaterialListFrame", new Vector2(0, 250), ImGuiChildFlags.Border);
        {
            foreach (var material in ct.Materials)
            {
                if (material.ID == 0)
                {
                    ImGui.TextDisabled($"  {material.Name} (Reserved)");
                    continue;
                }

                ImGui.PushID(material.ID);

                var isSelected = material.ID == _selectedMaterialId;

                // Color button
                var color = material.Color;
                ImGui.ColorButton("##color", color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha,
                    new Vector2(20, 20));

                ImGui.SameLine();

                // Visibility checkbox
                var visible = material.IsVisible;
                if (ImGui.Checkbox("##vis", ref visible))
                {
                    material.IsVisible = visible;
                    _pendingSave = true;
                }

                ImGui.SameLine();

                // Material name (selectable)
                if (ImGui.Selectable($"{material.Name} (ID:{material.ID})", isSelected))
                {
                    _selectedMaterialId = material.ID;
                    _renameBuf = material.Name;
                }

                // Right-click context menu
                if (ImGui.BeginPopupContextItem("MaterialContext"))
                {
                    if (ImGui.MenuItem("Duplicate")) DuplicateMaterial(ct, material);
                    ImGui.Separator();
                    if (ImGui.MenuItem("Delete"))
                    {
                        DeleteMaterial(ct, material);
                        ImGui.EndPopup();
                        ImGui.PopID();
                        break;
                    }

                    ImGui.EndPopup();
                }

                ImGui.PopID();
            }
        }
        ImGui.EndChild();

        // Edit selected material
        var selectedMat = ct.Materials.FirstOrDefault(m => m.ID == _selectedMaterialId);
        if (selectedMat != null && selectedMat.ID != 0)
        {
            ImGui.Separator();
            ImGui.Text($"Edit Material: {selectedMat.Name}");

            // Name
            var tempName = _renameBuf;
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputText("Name", ref tempName, 100)) _renameBuf = tempName;
            if (ImGui.IsItemDeactivatedAfterEdit())
                if (!string.IsNullOrWhiteSpace(_renameBuf) &&
                    !ct.Materials.Any(m => m.ID != selectedMat.ID && m.Name == _renameBuf))
                {
                    selectedMat.Name = _renameBuf;
                    _pendingSave = true;
                }

            // Color
            var color = new Vector3(selectedMat.Color.X, selectedMat.Color.Y, selectedMat.Color.Z);
            if (ImGui.ColorEdit3("Color", ref color))
            {
                selectedMat.Color = new Vector4(color.X, color.Y, color.Z, 1.0f);
                _pendingSave = true;
            }

            // Density (for acoustic simulations)
            var density = (float)selectedMat.Density;
            ImGui.SetNextItemWidth(150);
            if (ImGui.DragFloat("Density (g/cm³)", ref density, 0.01f, 0.0f, 10.0f, "%.3f"))
            {
                selectedMat.Density = density;
                _pendingSave = true;
            }

            // Statistics
            ImGui.Separator();
            ImGui.Text("Statistics:");
            var voxelCount = GetVoxelCount(ct, selectedMat.ID);
            ImGui.Text($"  Voxels: {voxelCount:N0}");
            if (ct.PixelSize > 0 && ct.SliceThickness > 0)
            {
                var volumeMm3 = voxelCount * ct.PixelSize * ct.PixelSize * ct.SliceThickness / 1000000.0;
                ImGui.Text($"  Volume: {volumeMm3:F2} mm³");
            }

            ImGui.Separator();

            // Actions
            if (ImGui.Button("Clear from Volume", new Vector2(150, 0))) ClearMaterialFromVolume(ct, selectedMat.ID);
            ImGui.SameLine();
            if (ImGui.Button("Delete Material", new Vector2(150, 0))) DeleteMaterial(ct, selectedMat);
        }

        // Auto-save pending changes
        if (_pendingSave) SaveMaterials(ct);
    }

    public void Dispose()
    {
        ProjectManager.Instance.DatasetDataChanged -= OnDatasetDataChanged;
    }

    private void OnDatasetDataChanged(Dataset dataset)
    {
        // If the changed dataset is the one we're caching for, clear the cache.
        if (_currentDatasetRef != null && _currentDatasetRef.TryGetTarget(out var cachedDs) &&
            ReferenceEquals(cachedDs, dataset))
        {
            _voxelCountCache.Clear();
            Logger.Log("[MaterialManager] Voxel count cache cleared due to dataset change notification.");
        }
    }

    private void AddNewMaterial(CtImageStackDataset ct)
    {
        var name = _newMaterialName.Trim();
        if (string.IsNullOrWhiteSpace(name) || ct.Materials.Any(m => m.Name == name))
        {
            Logger.LogWarning($"[MaterialManager] Cannot add material: name '{name}' is invalid or already exists");
            return;
        }

        var newId = GetNextAvailableId(ct);
        var color = GetNextColor(ct.Materials.Count);
        var material = new Material(newId, name, color);

        ct.Materials.Add(material);
        _selectedMaterialId = newId;
        _renameBuf = name;
        _newMaterialName = $"Material {ct.Materials.Count}";
        _pendingSave = true;

        // Force visibility for new materials
        material.IsVisible = true;

        Logger.Log($"[MaterialManager] Added material '{name}' with ID {newId}");
    }

    private void AddNewMaterialWithColor(CtImageStackDataset ct, Vector4 color)
    {
        var name = $"Material {ct.Materials.Count}";
        var newId = GetNextAvailableId(ct);
        var material = new Material(newId, name, color);

        ct.Materials.Add(material);
        _selectedMaterialId = newId;
        _renameBuf = name;
        _pendingSave = true;

        Logger.Log($"[MaterialManager] Added material '{name}' with preset color");
    }

    private void DuplicateMaterial(CtImageStackDataset ct, Material original)
    {
        var newId = GetNextAvailableId(ct);
        var duplicate = new Material(newId, $"{original.Name} Copy", original.Color)
        {
            Density = original.Density,
            IsVisible = original.IsVisible,
            MinValue = original.MinValue,
            MaxValue = original.MaxValue
        };

        ct.Materials.Add(duplicate);
        _selectedMaterialId = newId;
        _renameBuf = duplicate.Name;
        _pendingSave = true;

        Logger.Log($"[MaterialManager] Duplicated material '{original.Name}'");
    }

    private void DeleteMaterial(CtImageStackDataset ct, Material material)
    {
        if (material == null || material.ID == 0) return;

        // First clear from volume
        ClearMaterialFromVolume(ct, material.ID);

        // Then remove from list
        ct.Materials.Remove(material);
        if (_selectedMaterialId == material.ID) _selectedMaterialId = -1;
        _pendingSave = true;

        Logger.Log($"[MaterialManager] Deleted material '{material.Name}'");
    }

    private void ClearMaterialFromVolume(CtImageStackDataset ct, byte materialId)
    {
        if (ct.LabelData == null) return;

        Logger.Log($"[MaterialManager] Clearing material {materialId} from volume...");

        var cleared = 0;
        for (var z = 0; z < ct.Depth; z++)
        {
            var slice = new byte[ct.Width * ct.Height];
            ct.LabelData.ReadSliceZ(z, slice);

            var modified = false;
            for (var i = 0; i < slice.Length; i++)
                if (slice[i] == materialId)
                {
                    slice[i] = 0;
                    modified = true;
                    cleared++;
                }

            if (modified) ct.LabelData.WriteSliceZ(z, slice);
        }

        if (cleared > 0)
        {
            ct.SaveLabelData();
            ProjectManager.Instance.NotifyDatasetDataChanged(ct);
        }

        Logger.Log($"[MaterialManager] Cleared {cleared:N0} voxels of material {materialId}");
    }

    private void ClearAllLabels(CtImageStackDataset ct)
    {
        if (ct.LabelData == null) return;

        Logger.Log("[MaterialManager] Clearing all labels...");

        var emptySlice = new byte[ct.Width * ct.Height];
        for (var z = 0; z < ct.Depth; z++) ct.LabelData.WriteSliceZ(z, emptySlice);

        ct.SaveLabelData();
        ProjectManager.Instance.NotifyDatasetDataChanged(ct);

        Logger.Log("[MaterialManager] All labels cleared");
    }

    private void SaveMaterials(CtImageStackDataset ct)
    {
        ct.SaveMaterials();
        ct.SaveLabelData(); // Also save label data to ensure consistency
        _pendingSave = false;
        Logger.Log($"[MaterialManager] Saved {ct.Materials.Count} materials");
    }

    private int GetVoxelCount(CtImageStackDataset ct, byte materialId)
    {
        // Check if dataset has changed since last time, invalidate cache if so.
        if (_currentDatasetRef == null || !_currentDatasetRef.TryGetTarget(out var cachedDs) ||
            !ReferenceEquals(cachedDs, ct))
        {
            _voxelCountCache.Clear();
            _currentDatasetRef = new WeakReference<CtImageStackDataset>(ct);
        }

        if (_voxelCountCache.TryGetValue(materialId, out var cachedCount)) return cachedCount;

        // If not in cache, calculate, store, and return.
        if (ct.LabelData == null) return 0;

        var count = 0;
        var slice = new byte[ct.Width * ct.Height];

        for (var z = 0; z < ct.Depth; z++)
        {
            ct.LabelData.ReadSliceZ(z, slice);
            for (var i = 0; i < slice.Length; i++)
                if (slice[i] == materialId)
                    count++;
        }

        _voxelCountCache[materialId] = count;
        return count;
    }

    private byte GetNextAvailableId(CtImageStackDataset ct)
    {
        for (byte i = 1; i < 255; i++)
            if (!ct.Materials.Any(m => m.ID == i))
                return i;
        throw new InvalidOperationException("No available material IDs");
    }

    private Vector4 GetNextColor(int index)
    {
        // Cycle through presets, then generate variations
        if (index < _colorPresets.Length)
            return _colorPresets[index];

        // Generate color based on HSV
        var hue = index * 137.5f % 360f; // Golden angle
        var sat = 0.8f;
        var val = 0.9f;

        return HsvToRgb(hue / 360f, sat, val);
    }

    private static Vector4 HsvToRgb(float h, float s, float v)
    {
        float r, g, b;
        var i = (int)(h * 6);
        var f = h * 6 - i;
        var p = v * (1 - s);
        var q = v * (1 - f * s);
        var t = v * (1 - (1 - f) * s);

        switch (i % 6)
        {
            case 0:
                r = v;
                g = t;
                b = p;
                break;
            case 1:
                r = q;
                g = v;
                b = p;
                break;
            case 2:
                r = p;
                g = v;
                b = t;
                break;
            case 3:
                r = p;
                g = q;
                b = v;
                break;
            case 4:
                r = t;
                g = p;
                b = v;
                break;
            default:
                r = v;
                g = p;
                b = q;
                break;
        }

        return new Vector4(r, g, b, 1.0f);
    }
}