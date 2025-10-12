// GeoscientistToolkit/Analysis/MaterialManager/MaterialManagerTool.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.MaterialManager;

public class MaterialManagerTool : IDatasetTools, IDisposable
{
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

    private readonly Dictionary<byte, int> _voxelCountCache = new();
    private WeakReference<CtImageStackDataset> _currentDatasetRef;
    private string _materialSearchFilter = "";
    private string _newMaterialName = "New Material";
    private bool _pendingSave;
    private string _renameBuf = string.Empty;
    private PhysicalMaterial _selectedLibraryMaterial;
    private int _selectedMaterialId = -1;

    // Material library browser
    private bool _showMaterialLibraryBrowser;
    private byte _targetMaterialIdForAssignment;

    public MaterialManagerTool()
    {
        ProjectManager.Instance.DatasetDataChanged += OnDatasetDataChanged;
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ct) return;

        ImGui.SeparatorText("Material Manager");

        // Quick actions bar
        if (ImGui.Button("Save All Materials", new Vector2(150, 0))) SaveMaterials(ct);
        ImGui.SameLine();
        if (ImGui.Button("Browse Library", new Vector2(150, 0)))
        {
            _targetMaterialIdForAssignment = 0;
            _showMaterialLibraryBrowser = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear All Labels"))
        {
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Hold Ctrl+Shift and click");
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
                var displayName = material.Name;
                if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                    displayName += $" [{material.PhysicalMaterialName}]";
                if (ImGui.Selectable($"{displayName} (ID:{material.ID})", isSelected))
                {
                    _selectedMaterialId = material.ID;
                    _renameBuf = material.Name;
                }

                // Show library link indicator
                if (!string.IsNullOrEmpty(material.PhysicalMaterialName))
                {
                    var libMat = MaterialLibrary.Instance.Find(material.PhysicalMaterialName);
                    if (libMat != null && ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"Linked to: {libMat.Name}");
                        if (libMat.ThermalConductivity_W_mK.HasValue)
                            ImGui.Text($"k = {libMat.ThermalConductivity_W_mK:F4} W/m·K");
                        if (libMat.Density_kg_m3.HasValue)
                            ImGui.Text($"ρ = {libMat.Density_kg_m3:F1} kg/m³");
                        ImGui.EndTooltip();
                    }
                }

                // Right-click context menu
                if (ImGui.BeginPopupContextItem("MaterialContext"))
                {
                    if (ImGui.MenuItem("Assign from Library"))
                    {
                        _targetMaterialIdForAssignment = material.ID;
                        _showMaterialLibraryBrowser = true;
                    }

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

            // Density
            var density = (float)selectedMat.Density;
            ImGui.SetNextItemWidth(150);
            if (ImGui.DragFloat("Density (g/cm³)", ref density, 0.01f, 0.0f, 20.0f, "%.3f"))
            {
                selectedMat.Density = density;
                _pendingSave = true;
            }

            // Library link
            ImGui.Spacing();
            ImGui.SeparatorText("Material Library");

            if (!string.IsNullOrEmpty(selectedMat.PhysicalMaterialName))
            {
                var libMat = MaterialLibrary.Instance.Find(selectedMat.PhysicalMaterialName);
                if (libMat != null)
                {
                    ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"✓ Linked: {libMat.Name}");

                    if (ImGui.BeginTable("LibProps", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 150);
                        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

                        if (libMat.ThermalConductivity_W_mK.HasValue)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text("Thermal Conductivity");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{libMat.ThermalConductivity_W_mK:F4} W/m·K");
                        }

                        if (libMat.SpecificHeatCapacity_J_kgK.HasValue)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text("Specific Heat");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{libMat.SpecificHeatCapacity_J_kgK:F1} J/kg·K");
                        }

                        if (libMat.Density_kg_m3.HasValue)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text("Density");
                            ImGui.TableNextColumn();
                            ImGui.Text(
                                $"{libMat.Density_kg_m3:F1} kg/m³ ({libMat.Density_kg_m3.Value / 1000.0:F3} g/cm³)");
                        }

                        if (libMat.YoungModulus_GPa.HasValue)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text("Young's Modulus");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{libMat.YoungModulus_GPa:F1} GPa");
                        }

                        if (libMat.PoissonRatio.HasValue)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.Text("Poisson Ratio");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{libMat.PoissonRatio:F3}");
                        }

                        ImGui.EndTable();
                    }

                    if (ImGui.Button("Clear Library Link", new Vector2(150, 0)))
                    {
                        selectedMat.PhysicalMaterialName = null;
                        _pendingSave = true;
                        Logger.Log($"[MaterialManager] Cleared library link for {selectedMat.Name}");
                    }
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 0.8f, 0, 1),
                        $"⚠ Library material '{selectedMat.PhysicalMaterialName}' not found");
                    if (ImGui.Button("Clear Invalid Link", new Vector2(150, 0)))
                    {
                        selectedMat.PhysicalMaterialName = null;
                        _pendingSave = true;
                    }
                }
            }
            else
            {
                ImGui.TextDisabled("Not linked to library");
            }

            if (ImGui.Button("Browse Library", new Vector2(150, 0)))
            {
                _targetMaterialIdForAssignment = selectedMat.ID;
                _showMaterialLibraryBrowser = true;
            }

            // Statistics
            ImGui.Spacing();
            ImGui.SeparatorText("Statistics");
            var voxelCount = GetVoxelCount(ct, selectedMat.ID);
            ImGui.Text($"Voxels: {voxelCount:N0}");
            if (ct.PixelSize > 0 && ct.SliceThickness > 0)
            {
                var volumeMm3 = voxelCount * ct.PixelSize * ct.PixelSize * ct.SliceThickness / 1000000.0;
                ImGui.Text($"Volume: {volumeMm3:F2} mm³");

                if (selectedMat.Density > 0)
                {
                    var massGrams = volumeMm3 * selectedMat.Density / 1000.0;
                    ImGui.Text($"Estimated Mass: {massGrams:F4} g");
                }
            }

            ImGui.Separator();

            // Actions
            if (ImGui.Button("Clear from Volume", new Vector2(150, 0))) ClearMaterialFromVolume(ct, selectedMat.ID);
            ImGui.SameLine();
            if (ImGui.Button("Delete Material", new Vector2(150, 0))) DeleteMaterial(ct, selectedMat);
        }

        // Auto-save pending changes
        if (_pendingSave) SaveMaterials(ct);

        // Material library browser modal
        if (_showMaterialLibraryBrowser) DrawMaterialLibraryBrowser(ct);
    }

    public void Dispose()
    {
        ProjectManager.Instance.DatasetDataChanged -= OnDatasetDataChanged;
    }

    private void DrawMaterialLibraryBrowser(CtImageStackDataset ct)
    {
        ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
        var isOpen = true;
        if (ImGui.Begin("Material Library##MaterialManagerBrowser", ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            var targetMaterial = ct.Materials.FirstOrDefault(m => m.ID == _targetMaterialIdForAssignment);
            if (targetMaterial != null)
                ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Assigning to: {targetMaterial.Name}");
            else
                ImGui.Text("Select a material from the library:");

            ImGui.Separator();

            // Search filter
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##search", "Search materials...", ref _materialSearchFilter, 256);

            ImGui.Spacing();

            // Split view
            if (ImGui.BeginTable("LibraryTable", 2, ImGuiTableFlags.Resizable))
            {
                ImGui.TableSetupColumn("Materials", ImGuiTableColumnFlags.WidthFixed, 300);
                ImGui.TableSetupColumn("Properties", ImGuiTableColumnFlags.WidthStretch);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                // Material list
                if (ImGui.BeginChild("MatList", new Vector2(0, -80), ImGuiChildFlags.Border))
                {
                    var materials = MaterialLibrary.Instance.Materials
                        .Where(m => string.IsNullOrEmpty(_materialSearchFilter) ||
                                    m.Name.Contains(_materialSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                                    m.Phase.ToString().Contains(_materialSearchFilter,
                                        StringComparison.OrdinalIgnoreCase))
                        .OrderBy(m => m.Phase)
                        .ThenBy(m => m.Name)
                        .ToList();

                    var currentPhase = "";
                    foreach (var mat in materials)
                    {
                        if (mat.Phase.ToString() != currentPhase)
                        {
                            currentPhase = mat.Phase.ToString();
                            ImGui.SeparatorText(currentPhase);
                        }

                        var isSelected = _selectedLibraryMaterial == mat;
                        if (ImGui.Selectable($"{mat.Name}##{mat.Name}", isSelected)) _selectedLibraryMaterial = mat;
                    }
                }

                ImGui.EndChild();

                ImGui.TableNextColumn();

                // Property details
                if (ImGui.BeginChild("PropDetails", new Vector2(0, -80), ImGuiChildFlags.Border))
                {
                    if (_selectedLibraryMaterial != null)
                    {
                        var mat = _selectedLibraryMaterial;

                        ImGui.TextColored(new Vector4(0.5f, 1, 1, 1), mat.Name);
                        ImGui.TextDisabled($"Phase: {mat.Phase}");

                        if (!string.IsNullOrEmpty(mat.Notes))
                        {
                            ImGui.Spacing();
                            ImGui.TextWrapped(mat.Notes);
                        }

                        ImGui.Spacing();
                        ImGui.SeparatorText("Properties");

                        if (ImGui.BeginTable("Props", 2,
                                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
                        {
                            ImGui.TableSetupColumn("Property");
                            ImGui.TableSetupColumn("Value");
                            ImGui.TableHeadersRow();

                            void AddRow(string name, string value)
                            {
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                ImGui.Text(name);
                                ImGui.TableNextColumn();
                                ImGui.Text(value);
                            }

                            if (mat.Density_kg_m3.HasValue)
                                AddRow("Density", $"{mat.Density_kg_m3:F1} kg/m³");
                            if (mat.ThermalConductivity_W_mK.HasValue)
                                AddRow("Thermal Conductivity", $"{mat.ThermalConductivity_W_mK:F4} W/m·K");
                            if (mat.SpecificHeatCapacity_J_kgK.HasValue)
                                AddRow("Specific Heat", $"{mat.SpecificHeatCapacity_J_kgK:F1} J/kg·K");
                            if (mat.YoungModulus_GPa.HasValue)
                                AddRow("Young's Modulus", $"{mat.YoungModulus_GPa:F1} GPa");
                            if (mat.PoissonRatio.HasValue)
                                AddRow("Poisson Ratio", $"{mat.PoissonRatio:F3}");
                            if (mat.MohsHardness.HasValue)
                                AddRow("Mohs Hardness", $"{mat.MohsHardness:F1}");
                            if (mat.Vp_m_s.HasValue)
                                AddRow("P-wave Velocity", $"{mat.Vp_m_s:F0} m/s");
                            if (mat.Vs_m_s.HasValue)
                                AddRow("S-wave Velocity", $"{mat.Vs_m_s:F0} m/s");

                            ImGui.EndTable();
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled("Select a material to view properties");
                    }
                }

                ImGui.EndChild();

                ImGui.EndTable();
            }

            ImGui.Separator();

            // Action buttons
            ImGui.BeginDisabled(_selectedLibraryMaterial == null);
            if (_targetMaterialIdForAssignment > 0 && targetMaterial != null)
                if (ImGui.Button($"Assign to {targetMaterial.Name}", new Vector2(-130, 0)))
                {
                    AssignLibraryMaterial(targetMaterial, _selectedLibraryMaterial);
                    _showMaterialLibraryBrowser = false;
                }

            ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Close", new Vector2(120, 0))) _showMaterialLibraryBrowser = false;
        }

        ImGui.End();

        if (!isOpen) _showMaterialLibraryBrowser = false;
    }

    private void AssignLibraryMaterial(Material material, PhysicalMaterial libraryMaterial)
    {
        if (material == null || libraryMaterial == null) return;

        // Link to library
        material.PhysicalMaterialName = libraryMaterial.Name;

        // Assign properties
        if (libraryMaterial.Density_kg_m3.HasValue)
            material.Density = libraryMaterial.Density_kg_m3.Value / 1000.0; // kg/m³ to g/cm³

        _pendingSave = true;

        Logger.Log($"[MaterialManager] Assigned '{libraryMaterial.Name}' to '{material.Name}'");
        if (libraryMaterial.Density_kg_m3.HasValue)
            Logger.Log($"  ρ = {libraryMaterial.Density_kg_m3:F1} kg/m³");
        if (libraryMaterial.ThermalConductivity_W_mK.HasValue)
            Logger.Log($"  k = {libraryMaterial.ThermalConductivity_W_mK:F4} W/m·K");
    }

    private void OnDatasetDataChanged(Dataset dataset)
    {
        if (_currentDatasetRef != null && _currentDatasetRef.TryGetTarget(out var cachedDs) &&
            ReferenceEquals(cachedDs, dataset))
            _voxelCountCache.Clear();
    }

    private void AddNewMaterial(CtImageStackDataset ct)
    {
        var name = _newMaterialName.Trim();
        if (string.IsNullOrWhiteSpace(name) || ct.Materials.Any(m => m.Name == name))
        {
            Logger.LogWarning($"[MaterialManager] Cannot add: invalid or duplicate name '{name}'");
            return;
        }

        var newId = GetNextAvailableId(ct);
        var color = GetNextColor(ct.Materials.Count);
        var material = new Material(newId, name, color) { IsVisible = true };

        ct.Materials.Add(material);
        _selectedMaterialId = newId;
        _renameBuf = name;
        _newMaterialName = $"Material {ct.Materials.Count}";
        _pendingSave = true;

        Logger.Log($"[MaterialManager] Added '{name}' ID={newId}");
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
    }

    private void DuplicateMaterial(CtImageStackDataset ct, Material original)
    {
        var newId = GetNextAvailableId(ct);
        var duplicate = new Material(newId, $"{original.Name} Copy", original.Color)
        {
            Density = original.Density,
            IsVisible = original.IsVisible,
            MinValue = original.MinValue,
            MaxValue = original.MaxValue,
            PhysicalMaterialName = original.PhysicalMaterialName
        };

        ct.Materials.Add(duplicate);
        _selectedMaterialId = newId;
        _renameBuf = duplicate.Name;
        _pendingSave = true;
    }

    private void DeleteMaterial(CtImageStackDataset ct, Material material)
    {
        if (material == null || material.ID == 0) return;
        ClearMaterialFromVolume(ct, material.ID);
        ct.Materials.Remove(material);
        if (_selectedMaterialId == material.ID) _selectedMaterialId = -1;
        _pendingSave = true;
    }

    private void ClearMaterialFromVolume(CtImageStackDataset ct, byte materialId)
    {
        if (ct.LabelData == null) return;

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
    }

    private void ClearAllLabels(CtImageStackDataset ct)
    {
        if (ct.LabelData == null) return;
        var emptySlice = new byte[ct.Width * ct.Height];
        for (var z = 0; z < ct.Depth; z++) ct.LabelData.WriteSliceZ(z, emptySlice);
        ct.SaveLabelData();
        ProjectManager.Instance.NotifyDatasetDataChanged(ct);
    }

    private void SaveMaterials(CtImageStackDataset ct)
    {
        ct.SaveMaterials();
        ct.SaveLabelData();
        _pendingSave = false;
    }

    private int GetVoxelCount(CtImageStackDataset ct, byte materialId)
    {
        if (_currentDatasetRef == null || !_currentDatasetRef.TryGetTarget(out var cachedDs) ||
            !ReferenceEquals(cachedDs, ct))
        {
            _voxelCountCache.Clear();
            _currentDatasetRef = new WeakReference<CtImageStackDataset>(ct);
        }

        if (_voxelCountCache.TryGetValue(materialId, out var cachedCount)) return cachedCount;
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
        if (index < _colorPresets.Length)
            return _colorPresets[index];

        var hue = index * 137.5f % 360f;
        return HsvToRgb(hue / 360f, 0.8f, 0.9f);
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