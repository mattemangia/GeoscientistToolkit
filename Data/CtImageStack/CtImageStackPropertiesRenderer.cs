// GeoscientistToolkit/Data/CtImageStack/CtImageStackPropertiesRenderer.cs
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackPropertiesRenderer : IDatasetPropertiesRenderer
    {
        // Persist UI state per dataset (survives renderer re-creation / frame refresh)
        private static readonly Dictionary<string, int>    s_selectedByDataset   = new();
        private static readonly Dictionary<string, string> s_renameBufByDataset  = new();
        private static readonly Dictionary<string, string> s_newNameByDataset    = new(); // <--- NEW: Add-material input buffer

        // Per-render pass
        private bool _pendingSave = false;

        public void Draw(Dataset dataset)
        {
            if (dataset is not CtImageStackDataset ct) return;

            if (ImGui.CollapsingHeader("CT Stack Properties", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                if (ct.Width > 0 && ct.Height > 0 && ct.Depth > 0)
                {
                    PropertiesPanel.DrawProperty("Dimensions", $"{ct.Width} × {ct.Height} × {ct.Depth}");
                    PropertiesPanel.DrawProperty("Total Slices", ct.Depth.ToString());
                }

                if (ct.PixelSize > 0)
                {
                    PropertiesPanel.DrawProperty("Pixel Size", $"{ct.PixelSize:F3} {ct.Unit}");
                    PropertiesPanel.DrawProperty("Slice Thickness", $"{ct.SliceThickness:F3} {ct.Unit}");
                }

                if (ct.BinningSize > 0)
                {
                    PropertiesPanel.DrawProperty("Binning", $"{ct.BinningSize}×{ct.BinningSize}");
                }

                PropertiesPanel.DrawProperty("Bit Depth", $"{ct.BitDepth}-bit");

                ImGui.Unindent();
            }

            if (ImGui.CollapsingHeader("Materials", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawMaterialEditor(ct);
            }

            if (ImGui.CollapsingHeader("Acquisition Info"))
            {
                ImGui.Indent();
                ImGui.TextDisabled("Not yet implemented");
                ImGui.Unindent();
            }
        }

        // ----------------- Persistent-state helpers -----------------
        private static string GetDatasetKey(CtImageStackDataset ct)
            => string.IsNullOrEmpty(ct.FilePath) ? (ct.Name ?? "CTStack") : ct.FilePath;

        private static int GetSelected(CtImageStackDataset ct)
        {
            var key = GetDatasetKey(ct);
            if (!s_selectedByDataset.TryGetValue(key, out _))
                s_selectedByDataset[key] = -1;
            return s_selectedByDataset[key];
        }

        private static void SetSelected(CtImageStackDataset ct, int id)
        {
            var key = GetDatasetKey(ct);
            s_selectedByDataset[key] = id;
        }

        private static string GetRenameBuf(CtImageStackDataset ct)
        {
            var key = GetDatasetKey(ct);
            if (!s_renameBufByDataset.TryGetValue(key, out _))
                s_renameBufByDataset[key] = string.Empty;
            return s_renameBufByDataset[key];
        }

        private static void SetRenameBuf(CtImageStackDataset ct, string value)
        {
            var key = GetDatasetKey(ct);
            s_renameBufByDataset[key] = value ?? string.Empty;
        }

        private static string GetNewName(CtImageStackDataset ct)
        {
            var key = GetDatasetKey(ct);
            if (!s_newNameByDataset.TryGetValue(key, out _))
                s_newNameByDataset[key] = "New Material";
            return s_newNameByDataset[key];
        }

        private static void SetNewName(CtImageStackDataset ct, string value)
        {
            var key = GetDatasetKey(ct);
            s_newNameByDataset[key] = string.IsNullOrWhiteSpace(value) ? "New Material" : value;
        }
        // ------------------------------------------------------------

        private void DrawMaterialEditor(CtImageStackDataset ct)
        {
            ImGui.Indent();

            // ---- Add New Material (persist input buffer per dataset) ----
            float availableWidth = ImGui.GetContentRegionAvail().X;
            float buttonWidth = 120f;

            var newName = GetNewName(ct); // pull current buffer
            ImGui.SetNextItemWidth(availableWidth - buttonWidth - ImGui.GetStyle().ItemSpacing.X);
            if (ImGui.InputText("##NewMaterialName", ref newName, 100))
            {
                SetNewName(ct, newName); // push back if edited
            }
            ImGui.SameLine();

            if (ImGui.Button("Add Material", new Vector2(buttonWidth, 0)))
            {
                var candidate = GetNewName(ct).Trim();
                if (!string.IsNullOrWhiteSpace(candidate) && !ct.Materials.Any(m => m.Name == candidate))
                {
                    byte newId = ct.Materials.Any() ? (byte)(ct.Materials.Max(m => (int)m.ID) + 1) : (byte)1;
                    var newMat = new Material(newId, candidate, new Vector4(1f, 0f, 0f, 1f));
                    ct.Materials.Add(newMat);

                    // Reset buffer to default, select the new material
                    SetNewName(ct, "New Material");
                    SetSelected(ct, newId);
                    SetRenameBuf(ct, newMat.Name);

                    _pendingSave = true;
                }
            }

            ImGui.Separator();

            // ---- Material List (stable selection by ID) ----
            ImGui.BeginChild("##MaterialsChild", new Vector2(0, 220), ImGuiChildFlags.Border, ImGuiWindowFlags.None);

            if (ImGui.BeginListBox("##MaterialList", new Vector2(0, 200)))
            {
                foreach (var material in ct.Materials)
                {
                    // Show ID 0 as disabled / non-editable
                    if (material.ID == 0)
                    {
                        ImGui.TextDisabled($"  {material.Name} (ID: {material.ID})");
                        continue;
                    }

                    ImGui.PushID(material.ID);
                    bool isSelected = material.ID == GetSelected(ct);

                    if (ImGui.Selectable(material.Name, isSelected))
                    {
                        SetSelected(ct, material.ID);
                        SetRenameBuf(ct, material.Name);
                    }

                    // Right-click context menu
                    if (ImGui.BeginPopupContextItem("MatContext"))
                    {
                        if (ImGui.MenuItem("Rename"))
                        {
                            SetSelected(ct, material.ID);
                            SetRenameBuf(ct, material.Name);
                        }
                        if (ImGui.MenuItem("Remove"))
                        {
                            RemoveMaterialById(ct, material.ID);
                            ImGui.EndPopup();
                            ImGui.PopID();
                            ImGui.EndListBox();
                            ImGui.EndChild();
                            _pendingSave = true;
                            SaveIfNeeded(ct);
                            ImGui.Unindent();
                            return;
                        }
                        if (ImGui.MenuItem("Change Color"))
                        {
                            SetSelected(ct, material.ID);
                        }
                        ImGui.EndPopup();
                    }

                    ImGui.PopID();
                }
                ImGui.EndListBox();
            }

            ImGui.EndChild();

            // ---- Edit Selected Material ----
            var selectedMat = GetSelectedMaterial(ct);
            if (selectedMat != null && selectedMat.ID != 0)
            {
                ImGui.SeparatorText($"Edit: {selectedMat.Name}");

                // Rename (persist buffer per dataset)
                var renameBuf = GetRenameBuf(ct);
                if (string.IsNullOrEmpty(renameBuf))
                    renameBuf = selectedMat.Name;

                string tempName = renameBuf;
                if (ImGui.InputText("Name", ref tempName, 100))
                {
                    SetRenameBuf(ct, tempName);
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    var candidateName = GetRenameBuf(ct);
                    if (!string.IsNullOrWhiteSpace(candidateName) &&
                        !ct.Materials.Any(m => m.ID != selectedMat.ID && m.Name == candidateName))
                    {
                        if (selectedMat.Name != candidateName)
                        {
                            selectedMat.Name = candidateName;
                            _pendingSave = true;
                        }
                    }
                    else
                    {
                        SetRenameBuf(ct, selectedMat.Name);
                    }
                }

                // Color editor
                Vector3 color = new Vector3(selectedMat.Color.X, selectedMat.Color.Y, selectedMat.Color.Z);
                if (ImGui.ColorEdit3("Color", ref color))
                {
                    selectedMat.Color = new Vector4(color.X, color.Y, color.Z, 1.0f);
                }
                if (ImGui.IsItemDeactivatedAfterEdit())
                {
                    _pendingSave = true;
                }

                ImGui.Spacing();

                // Remove
                if (ImGui.Button("Remove This Material", new Vector2(-1, 0)))
                {
                    RemoveMaterialById(ct, selectedMat.ID);
                    _pendingSave = true;
                }
            }
            else
            {
                ImGui.TextDisabled("Select a material from the list above to edit.");
            }

            // Save batched changes
            SaveIfNeeded(ct);

            ImGui.Unindent();
        }

        private Material GetSelectedMaterial(CtImageStackDataset ct)
        {
            int selId = GetSelected(ct);
            if (selId < 0) return null;
            return ct.Materials.FirstOrDefault(m => m.ID == selId);
        }

        private void RemoveMaterialById(CtImageStackDataset ct, int id)
        {
            var m = ct.Materials.FirstOrDefault(x => x.ID == id);
            if (m != null)
            {
                ct.Materials.Remove(m);
                if (GetSelected(ct) == id)
                    SetSelected(ct, -1);
            }
        }

        private void SaveIfNeeded(CtImageStackDataset ct)
        {
            if (_pendingSave)
            {
                ct.SaveMaterials();
                _pendingSave = false;
            }
        }
    }
}
