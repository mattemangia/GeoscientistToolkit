// GAIA/Data/CtImageStack/CtImageStackPropertiesRenderer.cs

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using GAIA.UI;
using GAIA.UI.Interfaces;
using ImGuiNET;

namespace GAIA.Data.CtImageStack;

public class CtImageStackPropertiesRenderer : IDatasetPropertiesRenderer
{
    private static readonly ConditionalWeakTable<CtImageStackDataset, HistogramState> s_histograms = new();

    // Persist UI state per dataset (survives renderer re-creation / frame refresh)
    private static readonly Dictionary<string, int> s_selectedByDataset = new();
    private static readonly Dictionary<string, string> s_renameBufByDataset = new();

    private static readonly Dictionary<string, string>
        s_newNameByDataset = new(); // <--- NEW: Add-material input buffer

    // Per-render pass
    private bool _pendingSave;

    public void Draw(Dataset dataset)
    {
        // The factory routes the streaming dataset here too, and it keeps the dimensions and
        // calibration on its editable partner: without resolving it the panel stayed blank.
        var ct = dataset as CtImageStackDataset ?? (dataset as StreamingCtVolumeDataset)?.EditablePartner;
        if (ct == null) return;

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

            if (ct.BinningSize > 0) PropertiesPanel.DrawProperty("Binning", $"{ct.BinningSize}×{ct.BinningSize}");

            PropertiesPanel.DrawProperty("Bit Depth", $"{ct.BitDepth}-bit");

            ImGui.Unindent();
        }

        if (ImGui.CollapsingHeader("Grayscale Histogram", ImGuiTreeNodeFlags.DefaultOpen))
            DrawGrayscaleHistogram(ct);

        if (ImGui.CollapsingHeader("Materials", ImGuiTreeNodeFlags.DefaultOpen)) DrawMaterialEditor(ct);

        if (ImGui.CollapsingHeader("Acquisition Info"))
        {
            ImGui.Indent();
            var metadata = ct.DatasetMetadata;
            if (!string.IsNullOrWhiteSpace(metadata.SampleName))
                PropertiesPanel.DrawProperty("Sample", metadata.SampleName);
            if (!string.IsNullOrWhiteSpace(metadata.LocationName))
                PropertiesPanel.DrawProperty("Location", metadata.LocationName);
            if (metadata.Depth.HasValue)
                PropertiesPanel.DrawProperty("Depth", $"{metadata.Depth.Value:F2} m");
            if (metadata.CollectionDate.HasValue)
                PropertiesPanel.DrawProperty("Collection Date", metadata.CollectionDate.Value.ToShortDateString());
            if (!string.IsNullOrWhiteSpace(metadata.Collector))
                PropertiesPanel.DrawProperty("Collector", metadata.Collector);
            if (!string.IsNullOrWhiteSpace(metadata.Notes))
                PropertiesPanel.DrawProperty("Notes", metadata.Notes);
            if (metadata.CustomFields.Count > 0)
            {
                ImGui.SeparatorText("Custom Fields");
                foreach (var kvp in metadata.CustomFields)
                    PropertiesPanel.DrawProperty(kvp.Key, kvp.Value);
            }
            ImGui.Unindent();
        }
    }

    private static void DrawGrayscaleHistogram(CtImageStackDataset ct)
    {
        ImGui.Indent();
        var state = s_histograms.GetValue(ct, _ => new HistogramState());
        if (state.Calculation == null && ct.VolumeData != null)
            StartHistogramCalculation(ct, state);

        if (state.Calculation == null)
        {
            ImGui.TextDisabled("Grayscale volume is not loaded.");
            ImGui.Unindent();
            return;
        }

        if (!state.Calculation.IsCompleted)
        {
            ImGui.TextDisabled("Calculating histogram in background...");
            var phase = (float)(ImGui.GetTime() % 1.0);
            ImGui.ProgressBar(phase, new Vector2(-1, 0), string.Empty);
            ImGui.Unindent();
            return;
        }

        if (state.Calculation.IsFaulted)
        {
            var message = state.Calculation.Exception?.GetBaseException().Message ?? "Unknown error";
            ImGui.TextWrapped($"Histogram calculation failed: {message}");
            if (ImGui.Button("Retry", new Vector2(-1, 0))) StartHistogramCalculation(ct, state);
            ImGui.Unindent();
            return;
        }

        var logarithmic = state.Logarithmic;
        if (ImGui.Checkbox("Log scale", ref logarithmic)) state.Logarithmic = logarithmic;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Applies log10(1 + count) to the vertical axis.");

        var histogram = state.Calculation.Result;
        var values = new float[histogram.Length];
        long total = 0;
        var peakBin = 0;
        for (var i = 0; i < histogram.Length; i++)
        {
            total += histogram[i];
            if (histogram[i] > histogram[peakBin]) peakBin = i;
            values[i] = state.Logarithmic
                ? (float)Math.Log10(1d + histogram[i])
                : histogram[i];
        }

        var max = values.Max();
        ImGui.PlotHistogram("##CtGrayscaleHistogram", ref values[0], values.Length, 0,
            state.Logarithmic ? "log10(1 + count)" : "voxel count", 0f, Math.Max(1f, max),
            new Vector2(-1, 150));
        ImGui.TextDisabled("0");
        ImGui.SameLine();
        var labelWidth = ImGui.CalcTextSize("255").X;
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - labelWidth));
        ImGui.TextDisabled("255");
        PropertiesPanel.DrawProperty("Voxels", PropertiesPanel.FormatNumber(total));
        PropertiesPanel.DrawProperty("Peak value", $"{peakBin} ({PropertiesPanel.FormatNumber(histogram[peakBin])})");

        if (ImGui.Button("Recalculate", new Vector2(-1, 0))) StartHistogramCalculation(ct, state);
        ImGui.Unindent();
    }

    private static void StartHistogramCalculation(CtImageStackDataset ct, HistogramState state)
    {
        var volume = ct.VolumeData;
        var width = ct.Width;
        var height = ct.Height;
        var depth = ct.Depth;
        state.Calculation = Task.Run(() => CalculateHistogram(volume, width, height, depth));
    }

    private static long[] CalculateHistogram(VolumeData.ChunkedVolume volume, int width, int height, int depth)
    {
        if (volume == null || width <= 0 || height <= 0 || depth <= 0)
            throw new InvalidOperationException("Grayscale volume is not available.");

        var histogram = new long[256];
        var sliceLength = checked(width * height);
        var slice = ArrayPool<byte>.Shared.Rent(sliceLength);
        try
        {
            for (var z = 0; z < depth; z++)
            {
                volume.ReadSliceZ(z, slice);
                for (var i = 0; i < sliceLength; i++) histogram[slice[i]]++;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(slice);
        }

        return histogram;
    }

    private sealed class HistogramState
    {
        public Task<long[]> Calculation { get; set; }
        public bool Logarithmic { get; set; }
    }

    // ----------------- Persistent-state helpers -----------------
    private static string GetDatasetKey(CtImageStackDataset ct)
    {
        return string.IsNullOrEmpty(ct.FilePath) ? ct.Name ?? "CTStack" : ct.FilePath;
    }

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
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var buttonWidth = 120f;

        var newName = GetNewName(ct); // pull current buffer
        ImGui.SetNextItemWidth(availableWidth - buttonWidth - ImGui.GetStyle().ItemSpacing.X);
        if (ImGui.InputText("##NewMaterialName", ref newName, 100)) SetNewName(ct, newName); // push back if edited
        ImGui.SameLine();

        if (ImGui.Button("Add Material", new Vector2(buttonWidth, 0)))
        {
            var candidate = GetNewName(ct).Trim();
            if (!string.IsNullOrWhiteSpace(candidate) && !ct.Materials.Any(m => m.Name == candidate))
            {
                var newId = ct.Materials.Any() ? (byte)(ct.Materials.Max(m => (int)m.ID) + 1) : (byte)1;
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
                var isSelected = material.ID == GetSelected(ct);

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

                    if (ImGui.MenuItem("Change Color")) SetSelected(ct, material.ID);
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

            var tempName = renameBuf;
            if (ImGui.InputText("Name", ref tempName, 100)) SetRenameBuf(ct, tempName);
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
            var color = new Vector3(selectedMat.Color.X, selectedMat.Color.Y, selectedMat.Color.Z);
            if (ImGui.ColorEdit3("Color", ref color)) selectedMat.Color = new Vector4(color.X, color.Y, color.Z, 1.0f);
            if (ImGui.IsItemDeactivatedAfterEdit()) _pendingSave = true;

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
        var selId = GetSelected(ct);
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
