// GeoscientistToolkit/Data/CtImageStack/MaterialStatisticsWindow.cs

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;

namespace GeoscientistToolkit.Data.CtImageStack;

public class MaterialStatisticsWindow : BasePanel, IDisposable
{
    private readonly CtImageStackDataset _dataset;
    private readonly ImGuiExportFileDialog _exportCsvDialog;
    private readonly ImGuiExportFileDialog _exportImageDialog;
    private readonly Material _exteriorMaterial;
    private readonly Dictionary<byte, MaterialStatistics> _statistics = new();
    private ChartType _chartType = ChartType.PieChart;

    private bool _includeExterior = true;
    private bool _needsRecalculation = true;
    private float _totalVolumeUnits;
    private long _totalVolumeVoxels;

    public MaterialStatisticsWindow(CtImageStackDataset dataset) : base("Material Statistics", new Vector2(800, 600))
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _exteriorMaterial = new Material(0, "Exterior", new Vector4(0.1f, 0.1f, 0.1f, 1.0f));

        _exportImageDialog = new ImGuiExportFileDialog("ExportStatsImage", "Export Chart Image");
        _exportImageDialog.SetExtensions((".png", "PNG Image"), (".jpg", "JPEG Image"));

        _exportCsvDialog = new ImGuiExportFileDialog("ExportStatsCsv", "Export Statistics CSV");
        _exportCsvDialog.SetExtensions((".csv", "CSV File"));

        ProjectManager.Instance.DatasetDataChanged += OnDatasetDataChanged;
    }

    private void OnDatasetDataChanged(Dataset dataset)
    {
        // If our dataset changes and the panel is open (docked or popped), mark for recalc.
        if (dataset == _dataset && _isOpen) MarkForRecalculation();
    }

    /// <summary>
    ///     Call this method in the UI loop to draw the panel.
    /// </summary>
    public void Submit(ref bool pOpen)
    {
        // If the window is being opened for the first time, force a recalculation.
        if (pOpen && !_isOpen) MarkForRecalculation();

        // Let the base panel handle drawing, state, and pop-out logic.
        base.Submit(ref pOpen);
    }

    /// <summary>
    ///     This method is automatically called by BasePanel to draw the window's contents.
    /// </summary>
    protected override void DrawContent()
    {
        if (_needsRecalculation) _ = CalculateStatisticsAsync();

        // --- TOOLBAR ---
        if (ImGui.RadioButton("Pie Chart", _chartType == ChartType.PieChart)) _chartType = ChartType.PieChart;
        ImGui.SameLine();
        if (ImGui.RadioButton("Histogram", _chartType == ChartType.Histogram)) _chartType = ChartType.Histogram;
        ImGui.SameLine();
        ImGui.Dummy(new Vector2(20, 0));
        ImGui.SameLine();
        if (ImGui.Checkbox("Include Exterior", ref _includeExterior))
        {
            // No need to manually recalc, the chart/table logic will just filter differently.
            // Forcing a full recalc is only needed if the underlying data changes.
        }

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(20, 0));
        ImGui.SameLine();

        if (ImGui.Button("Export Chart")) _exportImageDialog.Open("material_statistics_chart");
        ImGui.SameLine();
        if (ImGui.Button("Export CSV")) _exportCsvDialog.Open("material_statistics");

        // --- LAYOUT & CONTENT ---
        if (ImGui.BeginTable("StatsLayout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Chart", ImGuiTableColumnFlags.WidthFixed, 350);
            ImGui.TableSetupColumn("Table", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawChart();
            ImGui.TableNextColumn();
            DrawStatisticsTable();
            ImGui.EndTable();
        }

        if (_exportImageDialog.Submit()) ExportChartImage(_exportImageDialog.SelectedPath);
        if (_exportCsvDialog.Submit()) ExportStatisticsCsv(_exportCsvDialog.SelectedPath);
    }

    private async Task CalculateStatisticsAsync()
    {
        _needsRecalculation = false;
        _statistics.Clear();

        await Task.Run(() =>
        {
            var materialsById = _dataset.Materials.ToDictionary(m => m.ID);
            if (!materialsById.ContainsKey(0)) materialsById[0] = _exteriorMaterial;

            foreach (var mat in materialsById.Values)
                _statistics[mat.ID] = new MaterialStatistics
                {
                    Material = mat,
                    VoxelCount = 0,
                    CenterOfMass = Vector3.Zero,
                    BoundingBoxMin = new Vector3(float.MaxValue),
                    BoundingBoxMax = new Vector3(float.MinValue)
                };

            var labels = _dataset.LabelData;
            var centerAccumulators = materialsById.ToDictionary(kvp => kvp.Key, kvp => Vector3.Zero);

            for (var z = 0; z < _dataset.Depth; z++)
            {
                var slice = new byte[_dataset.Width * _dataset.Height];
                labels.ReadSliceZ(z, slice);
                for (var y = 0; y < _dataset.Height; y++)
                for (var x = 0; x < _dataset.Width; x++)
                {
                    var materialId = slice[y * _dataset.Width + x];
                    if (_statistics.TryGetValue(materialId, out var stats))
                    {
                        stats.VoxelCount++;
                        var pos = new Vector3(x, y, z);
                        stats.BoundingBoxMin = Vector3.Min(stats.BoundingBoxMin, pos);
                        stats.BoundingBoxMax = Vector3.Max(stats.BoundingBoxMax, pos);
                        centerAccumulators[materialId] += pos;
                    }
                }
            }

            _totalVolumeVoxels = (long)_dataset.Width * _dataset.Height * _dataset.Depth;
            var voxelVolume = _dataset.PixelSize * _dataset.PixelSize * _dataset.SliceThickness;
            var unit = _dataset.Unit ?? "µm";
            var unitConversion = unit == "µm" || unit == "μm" || unit == "um" ? 1e-9f : 1.0f;
            _totalVolumeUnits = _totalVolumeVoxels * voxelVolume * unitConversion;

            foreach (var stat in _statistics.Values)
                if (stat.VoxelCount > 0)
                {
                    stat.Volume = stat.VoxelCount * voxelVolume * unitConversion;
                    stat.Percentage = _totalVolumeVoxels > 0 ? (float)stat.VoxelCount / _totalVolumeVoxels * 100f : 0;
                    stat.CenterOfMass = centerAccumulators[stat.Material.ID] / stat.VoxelCount;
                }
        });

        Logger.Log($"[MaterialStatistics] Recalculated statistics for {_statistics.Count} materials.");
    }

    private void DrawChart()
    {
        var availableSize = ImGui.GetContentRegionAvail();
        availableSize.Y -= 10; // Padding
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();

        ImGui.BeginChild("ChartArea", availableSize, ImGuiChildFlags.Border);

        var statsToDraw = _statistics.Values
            .Where(s => (_includeExterior || s.Material.ID != 0) && s.VoxelCount > 0)
            .ToList();

        if (statsToDraw.Any())
        {
            if (_chartType == ChartType.PieChart)
                DrawPieChart(drawList, pos, ImGui.GetContentRegionAvail(), statsToDraw);
            else
                DrawHistogram(drawList, pos, ImGui.GetContentRegionAvail(), statsToDraw);
        }
        else
        {
            ImGui.Text("No material data to display.");
        }

        ImGui.EndChild();
    }

    private void DrawPieChart(ImDrawListPtr drawList, Vector2 pos, Vector2 size, List<MaterialStatistics> statsToDraw)
    {
        var radius = Math.Min(size.X, size.Y) * 0.4f;
        var center = pos + size * 0.5f;

        var totalDisplayedVolume = statsToDraw.Sum(s => s.Volume);
        if (totalDisplayedVolume <= 0) return;

        var sortedStats = statsToDraw.OrderByDescending(s => s.Volume).ToList();
        var currentAngle = -MathF.PI / 2;

        foreach (var stat in sortedStats)
        {
            var percentageOfPie = stat.Volume / totalDisplayedVolume;
            var angleSpan = percentageOfPie * 2 * MathF.PI;
            DrawPieSlice(drawList, center, radius, currentAngle, currentAngle + angleSpan,
                ImGui.ColorConvertFloat4ToU32(stat.Material.Color));

            if (percentageOfPie * 100f > 3)
            {
                var labelAngle = currentAngle + angleSpan / 2;
                var labelPos = center + new Vector2(MathF.Cos(labelAngle), MathF.Sin(labelAngle)) * radius * 0.7f;
                var label = $"{percentageOfPie * 100f:F1}%";
                var textSize = ImGui.CalcTextSize(label);
                drawList.AddText(labelPos - textSize * 0.5f, 0xFFFFFFFF, label);
            }

            currentAngle += angleSpan;
        }

        var legendPos = pos + new Vector2(10, size.Y - Math.Min(100, size.Y * 0.3f));
        var column = 0;
        foreach (var stat in sortedStats.Take(8))
        {
            var x = legendPos.X + column % 2 * (size.X / 2 - 10);
            var y = legendPos.Y + column / 2 * 20;
            if (y > pos.Y + size.Y - 20) continue;

            drawList.AddRectFilled(new Vector2(x, y), new Vector2(x + 15, y + 15),
                ImGui.ColorConvertFloat4ToU32(stat.Material.Color));
            var name = stat.Material.Name.Length > 15
                ? stat.Material.Name.Substring(0, 15) + "..."
                : stat.Material.Name;
            drawList.AddText(new Vector2(x + 20, y), 0xFFFFFFFF, name);
            column++;
        }
    }

    private void DrawPieSlice(ImDrawListPtr drawList, Vector2 center, float radius, float startAngle, float endAngle,
        uint color)
    {
        const int segments = 32;
        var points = new Vector2[segments + 2];
        points[0] = center;
        var angleStep = (endAngle - startAngle) / segments;
        for (var i = 0; i <= segments; i++)
        {
            var angle = startAngle + i * angleStep;
            points[i + 1] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        drawList.AddConvexPolyFilled(ref points[0], segments + 2, color);
        for (var i = 1; i < segments + 2; i++) drawList.AddLine(points[i - 1], points[i], 0xFF000000, 1.5f);
        drawList.AddLine(points[segments + 1], center, 0xFF000000, 1.5f);
    }

    private void DrawHistogram(ImDrawListPtr drawList, Vector2 pos, Vector2 size, List<MaterialStatistics> statsToDraw)
    {
        var sortedStats = statsToDraw.OrderByDescending(s => s.Volume).ToList();
        if (!sortedStats.Any()) return;

        var maxVolume = sortedStats[0].Volume;
        var barWidth = size.X / sortedStats.Count * 0.8f;
        var spacing = size.X / sortedStats.Count * 0.2f;

        for (var i = 0; i < sortedStats.Count; i++)
        {
            var stat = sortedStats[i];
            var barHeight = stat.Volume / maxVolume * (size.Y - 50); // reserve space for text
            var x = pos.X + i * (barWidth + spacing) + spacing / 2;
            var y = pos.Y + size.Y - barHeight;

            drawList.AddRectFilled(new Vector2(x, y), new Vector2(x + barWidth, pos.Y + size.Y),
                ImGui.ColorConvertFloat4ToU32(stat.Material.Color));
            drawList.AddRect(new Vector2(x, y), new Vector2(x + barWidth, pos.Y + size.Y), 0xFF000000);
            var value = $"{stat.Volume:F2}";
            var textSize = ImGui.CalcTextSize(value);
            drawList.AddText(new Vector2(x + barWidth / 2 - textSize.X / 2, y - 20), 0xFFFFFFFF, value);
        }

        var unit = _dataset.Unit == "µm" || _dataset.Unit == "μm" || _dataset.Unit == "um"
            ? "mm³"
            : $"{_dataset.Unit}³";
        drawList.AddText(pos + new Vector2(5, 5), 0xFFFFFFFF, $"Volume ({unit})");
    }

    private void DrawStatisticsTable()
    {
        var volumeUnit = _dataset.Unit == "µm" || _dataset.Unit == "μm" || _dataset.Unit == "um"
            ? "mm³"
            : $"{_dataset.Unit}³";
        ImGui.Text($"Total Volume (Dataset): {_totalVolumeUnits:F3} {volumeUnit}");
        ImGui.Separator();

        if (ImGui.BeginTable("MaterialStats", 7,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.DefaultSort);
            ImGui.TableSetupColumn("Voxels");
            ImGui.TableSetupColumn($"Volume ({volumeUnit})", ImGuiTableColumnFlags.DefaultSort);
            ImGui.TableSetupColumn("% of Total Volume");
            ImGui.TableSetupColumn("Center X,Y,Z");
            ImGui.TableSetupColumn("Size X,Y,Z");
            ImGui.TableSetupColumn("Color");
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            var sortedStats = _statistics.Values
                .Where(s => (_includeExterior || s.Material.ID != 0) && s.VoxelCount > 0)
                .OrderByDescending(s => s.Volume)
                .ToList();

            foreach (var stat in sortedStats)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(stat.Material.Name);
                ImGui.TableNextColumn();
                ImGui.Text($"{stat.VoxelCount:N0}");
                ImGui.TableNextColumn();
                ImGui.Text($"{stat.Volume:F3}");
                ImGui.TableNextColumn();
                ImGui.Text($"{stat.Percentage:F2}%");
                ImGui.TableNextColumn();
                ImGui.Text($"{stat.CenterOfMass.X:F1}, {stat.CenterOfMass.Y:F1}, {stat.CenterOfMass.Z:F1}");
                ImGui.TableNextColumn();
                var size = stat.BoundingBoxMax - stat.BoundingBoxMin + Vector3.One;
                ImGui.Text($"{size.X:F0}, {size.Y:F0}, {size.Z:F0}");
                ImGui.TableNextColumn();
                ImGui.ColorButton($"##color{stat.Material.ID}", stat.Material.Color,
                    ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker,
                    new Vector2(ImGui.GetContentRegionAvail().X, 20));
            }

            ImGui.EndTable();
        }
    }

    private enum ChartType
    {
        PieChart,
        Histogram
    }

    private class MaterialStatistics
    {
        public Material Material { get; set; }
        public long VoxelCount { get; set; }
        public float Volume { get; set; }
        public float Percentage { get; set; } // Percentage of total dataset volume
        public Vector3 CenterOfMass { get; set; }
        public Vector3 BoundingBoxMin { get; set; }
        public Vector3 BoundingBoxMax { get; set; }
    }

    /// <summary> A simple software rasterizer for drawing primitives to a byte buffer. </summary>
    private static class SoftwareRenderer
    {
        public static uint Vector4ToUint(Vector4 v)
        {
            v.X = Math.Clamp(v.X, 0, 1);
            v.Y = Math.Clamp(v.Y, 0, 1);
            v.Z = Math.Clamp(v.Z, 0, 1);
            v.W = Math.Clamp(v.W, 0, 1);
            var r = (uint)(v.X * 255);
            var g = (uint)(v.Y * 255);
            var b = (uint)(v.Z * 255);
            var a = (uint)(v.W * 255);
            return (a << 24) | (b << 16) | (g << 8) | r;
        }

        public static void Fill(byte[] buffer, int width, int height, uint color)
        {
            var r = (byte)(color & 0xFF);
            var g = (byte)((color >> 8) & 0xFF);
            var b = (byte)((color >> 16) & 0xFF);
            var a = (byte)((color >> 24) & 0xFF);
            for (var i = 0; i < width * height; i++)
            {
                buffer[i * 4 + 0] = r;
                buffer[i * 4 + 1] = g;
                buffer[i * 4 + 2] = b;
                buffer[i * 4 + 3] = a;
            }
        }

        public static void SetPixel(byte[] buffer, int width, int height, int x, int y, uint color)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            var index = (y * width + x) * 4;
            buffer[index + 0] = (byte)(color & 0xFF);
            buffer[index + 1] = (byte)((color >> 8) & 0xFF);
            buffer[index + 2] = (byte)((color >> 16) & 0xFF);
            buffer[index + 3] = (byte)((color >> 24) & 0xFF);
        }

        public static void FillRectangle(byte[] buffer, int width, int height, int x1, int y1, int x2, int y2,
            uint color)
        {
            var startX = Math.Max(0, Math.Min(x1, x2));
            var endX = Math.Min(width, Math.Max(x1, x2));
            var startY = Math.Max(0, Math.Min(y1, y2));
            var endY = Math.Min(height, Math.Max(y1, y2));
            for (var y = startY; y < endY; y++)
            for (var x = startX; x < endX; x++)
                SetPixel(buffer, width, height, x, y, color);
        }

        public static void FillPieSlice(byte[] buffer, int width, int height, Vector2 center, float radius,
            float startAngle, float endAngle, uint color)
        {
            const int segments = 32;
            var angleStep = (endAngle - startAngle) / segments;
            for (var i = 0; i < segments; i++)
            {
                var a1 = startAngle + i * angleStep;
                var a2 = startAngle + (i + 1) * angleStep;
                var p1 = center;
                var p2 = center + new Vector2(MathF.Cos(a1) * radius, MathF.Sin(a1) * radius);
                var p3 = center + new Vector2(MathF.Cos(a2) * radius, MathF.Sin(a2) * radius);
                FillTriangle(buffer, width, height, p1, p2, p3, color);
            }
        }

        private static void FillTriangle(byte[] buffer, int width, int height, Vector2 v1, Vector2 v2, Vector2 v3,
            uint color)
        {
            var p = new[] { v1, v2, v3 }.OrderBy(v => v.Y).ToArray();
            Vector2 p1 = p[0], p2 = p[1], p3 = p[2];
            if (MathF.Abs(p1.Y - p3.Y) < 0.1f) return;
            var dx13 = (p3.X - p1.X) / (p3.Y - p1.Y);
            var dx12 = p1.Y == p2.Y ? 0 : (p2.X - p1.X) / (p2.Y - p1.Y);
            var dx23 = p2.Y == p3.Y ? 0 : (p3.X - p2.X) / (p3.Y - p2.Y);
            float wx1 = p1.X, wx2 = p1.X;
            int y_start = (int)p1.Y, y_mid = (int)p2.Y, y_end = (int)p3.Y;
            for (var y = y_start; y < y_mid; y++)
            {
                FillHorizontalLine(buffer, width, height, y, (int)wx1, (int)wx2, color);
                wx1 += dx13;
                wx2 += dx12;
            }

            wx2 = p2.X;
            for (var y = y_mid; y < y_end; y++)
            {
                FillHorizontalLine(buffer, width, height, y, (int)wx1, (int)wx2, color);
                wx1 += dx13;
                wx2 += dx23;
            }
        }

        private static void FillHorizontalLine(byte[] buffer, int width, int height, int y, int x1, int x2, uint color)
        {
            if (y < 0 || y >= height) return;
            var startX = Math.Max(0, Math.Min(x1, x2));
            var endX = Math.Min(width, Math.Max(x1, x2));
            for (var x = startX; x < endX; x++) SetPixel(buffer, width, height, x, y, color);
        }
    }

    #region Export and Other Public Methods

    public void MarkForRecalculation()
    {
        _needsRecalculation = true;
    }

    private void ExportChartImage(string path)
    {
        const int imageWidth = 800;
        const int imageHeight = 600;
        var rgbaData = new byte[imageWidth * imageHeight * 4];
        DrawChartToBitmap(rgbaData, imageWidth, imageHeight);

        try
        {
            using var stream = File.Create(path);
            var writer = new ImageWriter();
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".png")
                writer.WritePng(rgbaData, imageWidth, imageHeight, ColorComponents.RedGreenBlueAlpha, stream);
            else if (ext == ".jpg" || ext == ".jpeg")
                writer.WriteJpg(rgbaData, imageWidth, imageHeight, ColorComponents.RedGreenBlueAlpha, stream, 95);
            else Logger.LogError($"[MaterialStatistics] Unsupported image format for export: {ext}");
            Logger.Log($"[MaterialStatistics] Exported chart image to: {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MaterialStatistics] Failed to export chart image: {ex.Message}");
        }
    }

    private void DrawChartToBitmap(byte[] buffer, int width, int height)
    {
        SoftwareRenderer.Fill(buffer, width, height, 0xFF_1E1E1E);
        var statsToDraw = _statistics.Values.Where(s => (_includeExterior || s.Material.ID != 0) && s.VoxelCount > 0)
            .ToList();
        if (!statsToDraw.Any()) return;
        if (_chartType == ChartType.PieChart) DrawPieChartToBitmap(buffer, width, height, statsToDraw);
        else DrawHistogramToBitmap(buffer, width, height, statsToDraw);
    }

    private void DrawPieChartToBitmap(byte[] buffer, int width, int height, List<MaterialStatistics> statsToDraw)
    {
        var radius = Math.Min(width, height) * 0.45f;
        var center = new Vector2(width / 2.0f, height / 2.0f);
        var totalDisplayedVolume = statsToDraw.Sum(s => s.Volume);
        if (totalDisplayedVolume <= 0) return;

        var sortedStats = statsToDraw.OrderByDescending(s => s.Volume).ToList();
        var currentAngle = -MathF.PI / 2;

        foreach (var stat in sortedStats)
        {
            var angleSpan = stat.Volume / totalDisplayedVolume * 2f * MathF.PI;
            var color = SoftwareRenderer.Vector4ToUint(stat.Material.Color);
            SoftwareRenderer.FillPieSlice(buffer, width, height, center, radius, currentAngle, currentAngle + angleSpan,
                color);
            currentAngle += angleSpan;
        }
    }

    private void DrawHistogramToBitmap(byte[] buffer, int width, int height, List<MaterialStatistics> statsToDraw)
    {
        var sortedStats = statsToDraw.OrderByDescending(s => s.Volume).ToList();
        if (!sortedStats.Any()) return;

        var maxVolume = sortedStats[0].Volume;
        float padding = 30;
        var chartWidth = width - padding * 2;
        var chartHeight = height - padding * 2;
        var barTotalWidth = chartWidth / sortedStats.Count;
        var spacing = barTotalWidth * 0.2f;
        var barWidth = barTotalWidth - spacing;

        for (var i = 0; i < sortedStats.Count; i++)
        {
            var stat = sortedStats[i];
            var barHeight = stat.Volume / maxVolume * chartHeight;
            var x1 = (int)(padding + i * barTotalWidth);
            var y1 = (int)(height - padding - barHeight);
            var x2 = (int)(x1 + barWidth);
            var y2 = (int)(height - padding);
            var color = SoftwareRenderer.Vector4ToUint(stat.Material.Color);
            SoftwareRenderer.FillRectangle(buffer, width, height, x1, y1, x2, y2, color);
        }
    }

    private void ExportStatisticsCsv(string path)
    {
        try
        {
            var sb = new StringBuilder();
            var volumeUnit = _dataset.Unit == "µm" || _dataset.Unit == "μm" || _dataset.Unit == "um"
                ? "mm³"
                : $"{_dataset.Unit}³";
            sb.AppendLine(
                $"Material,Voxel Count,Volume ({volumeUnit}),% of Total Volume,Center X,Center Y,Center Z,Size X,Size Y,Size Z,Color R,Color G,Color B");

            var sortedStats = _statistics.Values
                .Where(s => (_includeExterior || s.Material.ID != 0) && s.VoxelCount > 0)
                .OrderByDescending(s => s.Volume);
            foreach (var stat in sortedStats)
            {
                var size = stat.BoundingBoxMax - stat.BoundingBoxMin + Vector3.One;
                sb.AppendLine(
                    $"{stat.Material.Name},{stat.VoxelCount},{stat.Volume:F6},{stat.Percentage:F4},{stat.CenterOfMass.X:F2},{stat.CenterOfMass.Y:F2},{stat.CenterOfMass.Z:F2},{size.X:F0},{size.Y:F0},{size.Z:F0},{stat.Material.Color.X:F3},{stat.Material.Color.Y:F3},{stat.Material.Color.Z:F3}");
            }

            sb.AppendLine();
            sb.AppendLine($"Dataset Total,{_totalVolumeVoxels},{_totalVolumeUnits:F6},100.0000,,,,,,,,");

            File.WriteAllText(path, sb.ToString());
            Logger.Log($"[MaterialStatistics] Exported statistics to: {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[MaterialStatistics] Failed to export CSV: {ex.Message}");
        }
    }

    public override void Dispose()
    {
        ProjectManager.Instance.DatasetDataChanged -= OnDatasetDataChanged;
        base.Dispose();
    }

    #endregion
}