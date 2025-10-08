// GeoscientistToolkit/UI/Tools/DensityCalibrationTool.cs
//
// Density calibration with working mini-preview + ROI selection,
// and an integration shim so the main CT viewers can forward clicks
// to this tool when "Region Selection" is enabled.
//
// Assumptions:
// - Dataset type: CtImageStackDataset, with VolumeData (byte) and LabelData (byte) providing ReadSliceZ(z, buffer)
// - TextureManager: utility that can create/dispose ImGui textures from RGBA byte[] (uint width/height)
// - Materials collection available on dataset, with ID (byte) and Color (Vector4: 0..1)
// - Logger class available
// - ImGui.NET in use
// - Viewers can (optionally) call CalibrationIntegration.OnViewerClick(dataset, axis, sliceZ, pixelX, pixelY)
//   when CalibrationIntegration.IsRegionSelectionEnabled(dataset) is true.
//
// If your viewers already integrate via CtImageStackTools.PreviewChanged/GetPreviewData,
// this class also publishes the preview there for backwards-compatibility.

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Tools;

public class DensityCalibrationTool : IDatasetTools, IDisposable
{
    // curve points: X=gray, Y=density
    private readonly List<Vector2> _curve = new();

    // stats cache by material id
    private readonly Dictionary<byte, (float mean, float std, int count)> _matStats = new();
    private readonly List<ManualRegion> _regions = new();
    private readonly List<DensitySample> _samples = new();
    private readonly float _wl = 128f;
    private readonly float _ww = 255f;
    private Vector2 _dragEndPx;

    // rectangle selection inside preview widget
    private bool _dragging;
    private Vector2 _dragStartPx;

    // ---------- State ----------
    private CtImageStackDataset _ds;
    private CalibMode _mode = CalibMode.ManualSelection;
    private bool _previewDirty = true;
    private int _previewSliceZ;

    // mini-preview texture
    private TextureManager _previewTex;

    private bool _showPreview = true;
    private int _testGray = 128;

    // selection enable for main viewers
    private bool _viewerSelectionEnabled;

    // ---------- Public Draw ----------
    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ct)
        {
            ImGui.TextColored(new Vector4(1, 0.7f, 0.2f, 1), "Density calibration requires a CT Image Stack dataset.");
            return;
        }

        if (!ReferenceEquals(_ds, ct))
        {
            _ds = ct;
            _regions.Clear();
            _samples.Clear();
            _matStats.Clear();
            _curve.Clear();
            _previewSliceZ = 0;
            _previewDirty = true;
        }

        ImGui.SeparatorText("Density Calibration");

        // --- Mode ---
        ImGui.Text("Calibration Mode:");
        ImGui.SameLine();
        RadioMode("Grayscale Mapping", CalibMode.GrayscaleMapping);
        ImGui.SameLine();
        RadioMode("Mean Density", CalibMode.MeanDensity);
        ImGui.SameLine();
        RadioMode("Manual Selection", CalibMode.ManualSelection);

        // --- Slice chooser for preview ---
        ImGui.Text($"Slice: {_previewSliceZ}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Previous"))
        {
            _previewSliceZ = Math.Max(0, _previewSliceZ - 1);
            _previewDirty = true;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Next"))
        {
            _previewSliceZ = Math.Min(_ds.Depth - 1, _previewSliceZ + 1);
            _previewDirty = true;
        }

        // --- Regions (manual mode) ---
        if (_mode == CalibMode.ManualSelection)
        {
            ImGui.Separator();
            ImGui.Text("Manual Regions:");
            if (ImGui.SmallButton("Add Region"))
            {
                _regions.Add(new ManualRegion
                {
                    Name = $"Region {_regions.Count + 1}",
                    SliceZ = _previewSliceZ,
                    X0 = 0, Y0 = 0, X1 = Math.Min(_ds.Width - 1, 32), Y1 = Math.Min(_ds.Height - 1, 32)
                });
                _previewDirty = true;
                Publish3DPreviewMask(); // show something immediately
            }

            DrawRegionsTable();
        }

        // --- Selection in main viewers toggle ---
        ImGui.Separator();
        if (ImGui.Checkbox("Enable Region Selection in Main Viewers", ref _viewerSelectionEnabled))
        {
            if (_viewerSelectionEnabled)
                CalibrationIntegration.EnableRegionSelection(_ds, OnViewerRegionClick);
            else
                CalibrationIntegration.DisableRegionSelection(_ds, OnViewerRegionClick);
        }

        // --- Curve & apply ---
        ImGui.Separator();
        DrawCalibrationSection();

        // --- Actions ---
        ImGui.Separator();
        if (ImGui.Button("Apply Calibration")) ApplyCalibration();
        ImGui.SameLine();
        if (ImGui.Button("Reset"))
        {
            _regions.Clear();
            _samples.Clear();
            _curve.Clear();
            _matStats.Clear();
            _previewDirty = true;
            CalibrationIntegration.ClearPreview(_ds);
        }
    }

    // ---------- IDisposable ----------
    public void Dispose()
    {
        _previewTex?.Dispose();
        _previewTex = null;
        if (_ds != null)
        {
            CalibrationIntegration.DisableRegionSelection(_ds, OnViewerRegionClick);
            CalibrationIntegration.ClearPreview(_ds);
        }
    }

    // ---------- UI helpers ----------
    private void RadioMode(string label, CalibMode m)
    {
        var selected = _mode == m;
        if (ImGui.RadioButton(label, selected))
        {
            _mode = m;
            _previewDirty = true;
        }
    }

    private void DrawRegionsTable()
    {
        if (_regions.Count == 0)
        {
            ImGui.TextDisabled("No regions added yet.");
            return;
        }

        if (ImGui.BeginTable("Regions", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Slice", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Density", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Color", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Rect (x0,y0)-(x1,y1)", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableHeadersRow();

            for (var i = 0; i < _regions.Count; i++)
            {
                var r = _regions[i];
                ImGui.TableNextRow();

                // Name
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText($"##name{i}", ref r.Name, 128);

                // Slice
                ImGui.TableNextColumn();
                var s = r.SliceZ;
                if (ImGui.DragInt($"##slice{i}", ref s, 1, 0, Math.Max(0, _ds.Depth - 1)))
                {
                    r.SliceZ = Math.Clamp(s, 0, _ds.Depth - 1);
                    _previewDirty = true;
                }

                // Density
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputFloat($"##dens{i}", ref r.Density, 0, 0, "%.1f");

                // Color
                ImGui.TableNextColumn();
                ImGui.ColorEdit4($"##col{i}", ref r.Color);

                // Rect
                ImGui.TableNextColumn();
                ImGui.Text($"{r.X0},{r.Y0} - {r.X1},{r.Y1}");

                // Actions
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Select##sel{i}"))
                {
                    // arm the main-viewer selection for this region (updates on next click)
                    _viewerSelectionEnabled = true;
                    CalibrationIntegration.EnableRegionSelection(_ds, OnViewerRegionClick, i);
                    Logger.Log($"[DensityCalibration] Click a main viewer slice to set region #{i} rectangle.");
                }

                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##rem{i}"))
                {
                    _regions.RemoveAt(i);
                    _previewDirty = true;
                    Publish3DPreviewMask();
                    i--;
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawPreviewWidget(int maxW, int maxH)
    {
        if (_ds == null || _ds.VolumeData == null) return;

        var w = _ds.Width;
        var h = _ds.Height;

        // rebuild RGBA preview when needed
        if (_previewDirty || _previewTex == null || !_previewTex.IsValid)
            try
            {
                var gray = new byte[w * h];
                _ds.VolumeData.ReadSliceZ(Math.Clamp(_previewSliceZ, 0, _ds.Depth - 1), gray);
                ApplyWindowLevel(gray, _wl, _ww);

                var rgba = new byte[w * h * 4];
                for (var i = 0; i < gray.Length; i++)
                {
                    var g = gray[i];
                    var o = i * 4;
                    rgba[o + 0] = g;
                    rgba[o + 1] = g;
                    rgba[o + 2] = g;
                    rgba[o + 3] = 255;
                }

                // draw all regions that belong to this slice
                foreach (var r in _regions.Where(rr => rr.SliceZ == _previewSliceZ)) DrawRectOnRgba(rgba, w, h, r, 1);

                _previewTex?.Dispose();
                _previewTex = TextureManager.CreateFromPixelData(rgba, (uint)w, (uint)h);
                _previewDirty = false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DensityCalibration] Preview build failed: {ex.Message}");
                return;
            }

        // geometry
        var avail = ImGui.GetContentRegionAvail();
        var scale = Math.Min(Math.Min(avail.X / w, avail.Y / h), Math.Min(maxW / (float)w, maxH / (float)h));
        if (scale <= 0) scale = 1f;

        var draw = ImGui.GetWindowDrawList();
        var imgSize = new Vector2(w * scale, h * scale);
        var p0 = ImGui.GetCursorScreenPos();

        // input surface
        ImGui.InvisibleButton("##CalPrevCanvas", imgSize);
        var hovered = ImGui.IsItemHovered();

        // image
        draw.AddImage(_previewTex.GetImGuiTextureId(), p0, p0 + imgSize, Vector2.Zero, Vector2.One, 0xFFFFFFFF);

        // mouse → image px
        Vector2 MouseToPx(Vector2 m)
        {
            return new Vector2(
                Math.Clamp((m.X - p0.X) / scale, 0, w - 1),
                Math.Clamp((m.Y - p0.Y) / scale, 0, h - 1));
        }

        // rect drag selection
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _dragging = true;
            _dragStartPx = _dragEndPx = MouseToPx(ImGui.GetIO().MousePos);
        }

        if (_dragging && hovered && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            _dragEndPx = MouseToPx(ImGui.GetIO().MousePos);
        if (_dragging && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _dragging = false;

            var rect = NormalizeRect(_dragStartPx, _dragEndPx, w, h);
            // create/update a region for this slice
            _regions.Add(new ManualRegion
            {
                Name = $"Region {_regions.Count + 1}",
                SliceZ = _previewSliceZ,
                X0 = rect.x0, Y0 = rect.y0, X1 = rect.x1, Y1 = rect.y1,
                Density = 2700f,
                Color = new Vector4(0.0f, 0.8f, 1.0f, 1.0f)
            });
            _previewDirty = true;
            Publish3DPreviewMask();
        }

        // draw current drag rect overlay
        if (_dragging)
        {
            var a = p0 + new Vector2(_dragStartPx.X * scale, _dragStartPx.Y * scale);
            var b = p0 + new Vector2(_dragEndPx.X * scale, _dragEndPx.Y * scale);
            draw.AddRect(a, b, 0xFF00FFFF, 0, 0, 2f);
        }
    }

    private (int x0, int y0, int x1, int y1) NormalizeRect(Vector2 a, Vector2 b, int w, int h)
    {
        var x0 = (int)Math.Floor(Math.Min(a.X, b.X));
        var y0 = (int)Math.Floor(Math.Min(a.Y, b.Y));
        var x1 = (int)Math.Ceiling(Math.Max(a.X, b.X));
        var y1 = (int)Math.Ceiling(Math.Max(a.Y, b.Y));
        x0 = Math.Clamp(x0, 0, w - 1);
        y0 = Math.Clamp(y0, 0, h - 1);
        x1 = Math.Clamp(x1, 0, w - 1);
        y1 = Math.Clamp(y1, 0, h - 1);
        return (x0, y0, x1, y1);
    }

    private void DrawRectOnRgba(byte[] rgba, int w, int h, ManualRegion r, int thickness)
    {
        // Guard
        var x0 = Math.Clamp(r.X0, 0, w - 1);
        var x1 = Math.Clamp(r.X1, 0, w - 1);
        var y0 = Math.Clamp(r.Y0, 0, h - 1);
        var y1 = Math.Clamp(r.Y1, 0, h - 1);

        var rr = (byte)(r.Color.X * 255);
        var gg = (byte)(r.Color.Y * 255);
        var bb = (byte)(r.Color.Z * 255);

        void SetPx(int x, int y)
        {
            var i = (y * w + x) * 4;
            rgba[i + 0] = rr;
            rgba[i + 1] = gg;
            rgba[i + 2] = bb;
            rgba[i + 3] = 255;
        }

        for (var t = 0; t < thickness; t++)
        {
            for (var x = x0; x <= x1; x++)
            {
                SetPx(x, y0 + t);
                SetPx(x, y1 - t);
            }

            for (var y = y0; y <= y1; y++)
            {
                SetPx(x0 + t, y);
                SetPx(x1 - t, y);
            }
        }
    }

    // ---------- Calibration curve & apply ----------
    private void DrawCalibrationSection()
    {
        ImGui.Text("Calibration Curve");
        if (ImGui.SmallButton("Build Curve From Regions"))
        {
            _curve.Clear();
            foreach (var r in _regions)
            {
                var (mean, _, count) = ComputeGrayStatsForRect(r.SliceZ, r.X0, r.Y0, r.X1, r.Y1);
                if (count > 0)
                    _curve.Add(new Vector2(mean, r.Density));
            }

            Logger.Log($"[DensityCalibration] Built calibration curve with {_curve.Count} points.");
        }

        if (_curve.Count >= 2)
        {
            var (m, q) = LinearFit(_curve);
            ImGui.Text($"Linear Fit: ρ = {m:F6} · Gray + {q:F3}");
            ImGui.SetNextItemWidth(90);
            ImGui.InputInt("Test Gray", ref _testGray);
            _testGray = Math.Clamp(_testGray, 0, 255);
            var pred = m * _testGray + q;
            ImGui.SameLine();
            ImGui.Text($"→ {pred:F3}");
        }
        else
        {
            ImGui.TextDisabled("Need at least 2 regions to fit a line.");
        }
    }

    private void ApplyCalibration()
    {
        if (_curve.Count < 2)
        {
            Logger.LogWarning("[DensityCalibration] No calibration curve. Nothing to apply.");
            return;
        }

        var (m, q) = LinearFit(_curve);

        // Use material label statistics to set density
        foreach (var mat in _ds.Materials.Where(mm => mm.ID != 0))
        {
            var (mean, _, count) = GetMatStats(mat.ID);
            if (count == 0) continue;
            var d = Math.Max(0.001f, m * mean + q);
            var old = (float)mat.Density;
            mat.Density = d;
            Logger.Log($"[DensityCalibration] {mat.Name}: gray={mean:F1} (n={count}) → ρ={d:F3} (was {old:F3})");
        }

        _ds.SaveMaterials();
    }

    // ---------- Computation helpers ----------
    private (float mean, float std, int count) GetMatStats(byte id)
    {
        if (!_matStats.TryGetValue(id, out var s))
        {
            s = CalcMatStats(id);
            _matStats[id] = s;
        }

        return s;
    }

    private (float mean, float std, int count) CalcMatStats(byte id)
    {
        if (_ds?.VolumeData == null || _ds.LabelData == null) return (0, 0, 0);
        var vals = new List<float>(4096);

        int w = _ds.Width, h = _ds.Height, d = _ds.Depth;
        var zStep = Math.Max(1, d / 50); // ~50 slices

        for (var z = 0; z < d; z += zStep)
        {
            var gray = new byte[w * h];
            var lab = new byte[w * h];
            _ds.VolumeData.ReadSliceZ(z, gray);
            _ds.LabelData.ReadSliceZ(z, lab);

            for (var i = 0; i < gray.Length; i++)
                if (lab[i] == id)
                    vals.Add(gray[i]);
        }

        if (vals.Count == 0) return (0, 0, 0);
        var mean = vals.Average();
        float var = 0;
        foreach (var v in vals)
        {
            var dlt = v - mean;
            var += dlt * dlt;
        }

        var /= vals.Count;
        return (mean, (float)Math.Sqrt(var), vals.Count);
    }

    private (float mean, float std, int count) ComputeGrayStatsForRect(int z, int x0, int y0, int x1, int y1)
    {
        if (_ds?.VolumeData == null) return (0, 0, 0);
        x0 = Math.Clamp(x0, 0, _ds.Width - 1);
        x1 = Math.Clamp(x1, 0, _ds.Width - 1);
        y0 = Math.Clamp(y0, 0, _ds.Height - 1);
        y1 = Math.Clamp(y1, 0, _ds.Height - 1);

        var gray = new byte[_ds.Width * _ds.Height];
        _ds.VolumeData.ReadSliceZ(Math.Clamp(z, 0, _ds.Depth - 1), gray);

        var vals = new List<float>((x1 - x0 + 1) * (y1 - y0 + 1));
        for (var y = y0; y <= y1; y++)
        {
            var row = y * _ds.Width;
            for (var x = x0; x <= x1; x++)
                vals.Add(gray[row + x]);
        }

        if (vals.Count == 0) return (0, 0, 0);
        var mean = vals.Average();
        float var = 0;
        foreach (var v in vals)
        {
            var d = v - mean;
            var += d * d;
        }

        var /= vals.Count;
        return (mean, (float)Math.Sqrt(var), vals.Count);
    }

    private static (float m, float q) LinearFit(List<Vector2> pts)
    {
        float n = pts.Count;
        float sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var p in pts)
        {
            sx += p.X;
            sy += p.Y;
            sxx += p.X * p.X;
            sxy += p.X * p.Y;
        }

        var denom = n * sxx - sx * sx;
        if (Math.Abs(denom) < 1e-6f) return (0, sy / n);
        var m = (n * sxy - sx * sy) / denom;
        var q = (sy - m * sx) / n;
        return (m, q);
    }

    private static void ApplyWindowLevel(byte[] data, float wl, float ww)
    {
        var min = wl - ww / 2f;
        var max = wl + ww / 2f;
        var range = Math.Max(1e-5f, max - min);
        for (var i = 0; i < data.Length; i++)
        {
            var v = (data[i] - min) / range * 255f;
            data[i] = (byte)Math.Clamp(v, 0f, 255f);
        }
    }

    // ---------- Viewer click integration ----------
    private void OnViewerRegionClick(CtImageStackDataset ds, string axis, int sliceZ, int xPx, int yPx,
        int? targetRegionIndex)
    {
        if (!ReferenceEquals(_ds, ds)) return;

        // If a specific region index is requested (user clicked "Select" for that row),
        // set that rectangle to a small box centered at click (user can refine later in preview).
        if (targetRegionIndex.HasValue && targetRegionIndex.Value >= 0 && targetRegionIndex.Value < _regions.Count)
        {
            var r = _regions[targetRegionIndex.Value];
            r.SliceZ = sliceZ;
            var half = 8;
            r.X0 = Math.Clamp(xPx - half, 0, _ds.Width - 1);
            r.Y0 = Math.Clamp(yPx - half, 0, _ds.Height - 1);
            r.X1 = Math.Clamp(xPx + half, 0, _ds.Width - 1);
            r.Y1 = Math.Clamp(yPx + half, 0, _ds.Height - 1);
            _previewSliceZ = sliceZ;
        }
        else
        {
            // Otherwise, create a new region centered at the click.
            var half = 10;
            _regions.Add(new ManualRegion
            {
                Name = $"Region {_regions.Count + 1}",
                SliceZ = sliceZ,
                X0 = Math.Clamp(xPx - half, 0, _ds.Width - 1),
                Y0 = Math.Clamp(yPx - half, 0, _ds.Height - 1),
                X1 = Math.Clamp(xPx + half, 0, _ds.Width - 1),
                Y1 = Math.Clamp(yPx + half, 0, _ds.Height - 1),
                Color = new Vector4(0.0f, 0.8f, 1.0f, 1.0f),
                Density = 2700f
            });
            _previewSliceZ = sliceZ;
        }

        _previewDirty = true;
        Publish3DPreviewMask();
    }

    private void Publish3DPreviewMask()
    {
        if (_ds == null) return;

        int w = _ds.Width, h = _ds.Height, d = _ds.Depth;
        var mask = new byte[w * h * d];

        foreach (var r in _regions)
        {
            if (r.SliceZ < 0 || r.SliceZ >= d) continue;
            var zOff = r.SliceZ * w * h;
            var x0 = Math.Clamp(r.X0, 0, w - 1);
            var x1 = Math.Clamp(r.X1, 0, w - 1);
            var y0 = Math.Clamp(r.Y0, 0, h - 1);
            var y1 = Math.Clamp(r.Y1, 0, h - 1);

            for (var y = y0; y <= y1; y++)
            {
                var row = zOff + y * w;
                for (var x = x0; x <= x1; x++)
                    mask[row + x] = 255;
            }
        }

        // publish preview via integration shim (and CtImageStackTools if present)
        CalibrationIntegration.SetPreview(_ds, mask, new Vector4(0, 1, 1, 1));
    }

    // ---------- Models ----------
    private sealed class ManualRegion
    {
        public Vector4 Color = new(0.0f, 0.8f, 1.0f, 1.0f);
        public float Density = 2700f; // kg/m^3 or g/m^3 depending on your convention; shown as-is
        public string Name = "New Region";
        public int SliceZ; // slice where ROI lives
        public int X0, Y0, X1, Y1; // inclusive rectangle in pixel coords (dataset space)
    }

    private sealed class DensitySample
    {
        public int Count;
        public float KnownDensity;
        public string Label;
        public float MeanGray;
        public Vector2 PosPx;
        public int Radius = 5;
        public int SliceZ;
        public float StdGray;
        public DateTime Timestamp;
    }

    // UI flags/state
    private enum CalibMode
    {
        GrayscaleMapping,
        MeanDensity,
        ManualSelection
    }
}

/// <summary>
///     Integration shim between the tool and your viewers.
///     - Lets the tool enable/disable "region selection" mode.
///     - Provides a 3D preview mask the viewers can overlay.
///     - Mirrors to CtImageStackTools.* if that static class exists in your project already.
///     Viewers should:
///     1) before processing a click normally, check CalibrationIntegration.IsRegionSelectionEnabled(dataset).
///     2) if enabled, call CalibrationIntegration.OnViewerClick(dataset, axis, sliceZ, xPx, yPx).
///     Return early to avoid the default behaviour.
///     3) also subscribe/refresh when CalibrationIntegration.PreviewChanged is raised and call GetPreview(dataset).
/// </summary>
public static class CalibrationIntegration
{
    public delegate void ViewerClickHandler(CtImageStackDataset ds, string axis, int sliceZ, int xPx, int yPx,
        int? targetRegionIndex);

    private static readonly
        Dictionary<CtImageStackDataset, (bool enabled, ViewerClickHandler handler, int? targetIndex)> _sel
            = new();

    private static readonly Dictionary<CtImageStackDataset, (byte[] mask3D, Vector4 color, bool active)> _preview
        = new();

    public static event Action<CtImageStackDataset> PreviewChanged;

    // ---- Selection enable/disable ----
    public static void EnableRegionSelection(CtImageStackDataset ds, ViewerClickHandler handler,
        int? targetRegionIndex = null)
    {
        if (ds == null) return;
        _sel[ds] = (true, handler, targetRegionIndex);
    }

    public static void DisableRegionSelection(CtImageStackDataset ds, ViewerClickHandler handler)
    {
        if (ds == null) return;
        if (_sel.TryGetValue(ds, out var s) && s.handler == handler)
            _sel[ds] = (false, null, null);
    }

    public static bool IsRegionSelectionEnabled(CtImageStackDataset ds)
    {
        return ds != null && _sel.TryGetValue(ds, out var s) && s.enabled && s.handler != null;
    }

    // Viewers should call this when a click occurs on a slice
    public static void OnViewerClick(CtImageStackDataset ds, string axis, int sliceZ, int xPx, int yPx)
    {
        if (IsRegionSelectionEnabled(ds))
        {
            var (enabled, handler, targetIdx) = _sel[ds];
            handler?.Invoke(ds, axis, sliceZ, xPx, yPx, targetIdx);
            // disable targeted mode after first use
            _sel[ds] = (enabled, handler, null);
        }
    }

    // ---- Preview publishing ----
    public static void SetPreview(CtImageStackDataset ds, byte[] mask3D, Vector4 color)
    {
        if (ds == null) return;
        _preview[ds] = (mask3D, color, mask3D != null);
        PreviewChanged?.Invoke(ds);

        // Mirror to CtImageStackTools if your viewers already listen there
        TryMirrorToCtImageStackTools(ds, mask3D, color);
    }

    public static (bool active, byte[] mask3D, Vector4 color) GetPreview(CtImageStackDataset ds)
    {
        if (ds != null && _preview.TryGetValue(ds, out var p) && p.active) return (true, p.mask3D, p.color);
        return (false, null, default);
    }

    public static void ClearPreview(CtImageStackDataset ds)
    {
        if (ds == null) return;
        _preview[ds] = (null, default, false);
        PreviewChanged?.Invoke(ds);

        TryMirrorToCtImageStackTools(ds, null, default);
    }

    // ---- Optional mirror to CtImageStackTools ----
    private static void TryMirrorToCtImageStackTools(CtImageStackDataset ds, byte[] mask, Vector4 color)
    {
        // If your project defines GeoscientistToolkit.CtImageStackTools with SetPreviewData & RaisePreviewChanged,
        // reflectively call those so existing viewers keep working with zero code changes.
        var toolsType = Type.GetType("GeoscientistToolkit.CtImageStackTools, GeoscientistToolkit");
        if (toolsType == null) return;

        try
        {
            var setPrev = toolsType.GetMethod("SetPreviewData",
                new[] { typeof(CtImageStackDataset), typeof(byte[]), typeof(Vector4) });
            var raise = toolsType.GetMethod("RaisePreviewChanged", new[] { typeof(CtImageStackDataset) });
            setPrev?.Invoke(null, new object[] { ds, mask, color });
            raise?.Invoke(null, new object[] { ds });
        }
        catch
        {
            /* best-effort mirror */
        }
    }
}