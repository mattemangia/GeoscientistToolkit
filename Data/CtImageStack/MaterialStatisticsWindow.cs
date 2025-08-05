// GeoscientistToolkit/Data/CtImageStack/MaterialStatisticsWindow.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class MaterialStatisticsWindow
    {
        private readonly CtImageStackDataset _dataset;
        private bool _isOpen = false;
        private bool _needsRecalculation = true;
        private Dictionary<byte, MaterialStatistics> _statistics = new Dictionary<byte, MaterialStatistics>();
        private float _totalVolume = 0;
        private long _totalVoxels = 0;
        private ChartType _chartType = ChartType.PieChart;
        private readonly ImGuiExportFileDialog _exportImageDialog;
        private readonly ImGuiExportFileDialog _exportCsvDialog;
        
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
            public float Percentage { get; set; }
            public float VolumePercentage { get; set; }
            public Vector3 CenterOfMass { get; set; }
            public Vector3 BoundingBoxMin { get; set; }
            public Vector3 BoundingBoxMax { get; set; }
        }
        
        public MaterialStatisticsWindow(CtImageStackDataset dataset)
        {
            _dataset = dataset;
            _exportImageDialog = new ImGuiExportFileDialog("ExportStatsImage", "Export Chart Image");
            _exportImageDialog.SetExtensions(
                (".png", "PNG Image"),
                (".jpg", "JPEG Image")
            );
            
            _exportCsvDialog = new ImGuiExportFileDialog("ExportStatsCsv", "Export Statistics CSV");
            _exportCsvDialog.SetExtensions((".csv", "CSV File"));
        }
        
        public void Open()
        {
            _isOpen = true;
            _needsRecalculation = true;
        }
        
        public void Draw()
        {
            if (!_isOpen) return;
            
            ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Material Statistics", ref _isOpen))
            {
                if (_needsRecalculation)
                {
                    _ = CalculateStatisticsAsync();
                }
                
                // Chart type selection
                if (ImGui.RadioButton("Pie Chart", _chartType == ChartType.PieChart))
                    _chartType = ChartType.PieChart;
                ImGui.SameLine();
                if (ImGui.RadioButton("Histogram", _chartType == ChartType.Histogram))
                    _chartType = ChartType.Histogram;
                
                ImGui.SameLine();
                ImGui.Dummy(new Vector2(100, 0)); // Spacer
                ImGui.SameLine();
                
                // Export buttons
                if (ImGui.Button("Export Chart"))
                {
                    _exportImageDialog.Open("material_statistics_chart");
                }
                ImGui.SameLine();
                if (ImGui.Button("Export CSV"))
                {
                    _exportCsvDialog.Open("material_statistics");
                }
                ImGui.SameLine();
                
                // TODO: Import as Table Dataset
                ImGui.BeginDisabled();
                if (ImGui.Button("Import as Table Dataset"))
                {
                    // TODO: Implement table dataset import
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.SetTooltip("Table Datasets feature coming soon");
                }
                ImGui.EndDisabled();
                
                ImGui.Separator();
                
                // Draw content in two columns
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
            }
            ImGui.End();
            
            // Handle export dialogs
            if (_exportImageDialog.Submit())
            {
                ExportChartImage(_exportImageDialog.SelectedPath);
            }
            
            if (_exportCsvDialog.Submit())
            {
                ExportStatisticsCsv(_exportCsvDialog.SelectedPath);
            }
        }
        
        private async Task CalculateStatisticsAsync()
        {
            _needsRecalculation = false;
            _statistics.Clear();
            
            await Task.Run(() =>
            {
                var labels = _dataset.LabelData;
                var materials = _dataset.Materials.Where(m => m.ID != 0).ToDictionary(m => m.ID);
                
                // Initialize statistics
                foreach (var mat in materials.Values)
                {
                    _statistics[mat.ID] = new MaterialStatistics
                    {
                        Material = mat,
                        VoxelCount = 0,
                        Volume = 0,
                        BoundingBoxMin = new Vector3(float.MaxValue),
                        BoundingBoxMax = new Vector3(float.MinValue)
                    };
                }
                
                // Calculate voxel size
                float voxelVolume = _dataset.PixelSize * _dataset.PixelSize * _dataset.SliceThickness;
                
                // For center of mass calculation
                var centerAccumulators = new Dictionary<byte, Vector3>();
                foreach (var id in materials.Keys)
                {
                    centerAccumulators[id] = Vector3.Zero;
                }
                
                // First pass: count voxels and accumulate positions
                for (int z = 0; z < _dataset.Depth; z++)
                {
                    var slice = new byte[_dataset.Width * _dataset.Height];
                    labels.ReadSliceZ(z, slice);
                    
                    for (int y = 0; y < _dataset.Height; y++)
                    {
                        for (int x = 0; x < _dataset.Width; x++)
                        {
                            byte materialId = slice[y * _dataset.Width + x];
                            if (materialId != 0 && _statistics.ContainsKey(materialId))
                            {
                                var stats = _statistics[materialId];
                                stats.VoxelCount++;
                                
                                // Update bounding box
                                stats.BoundingBoxMin = Vector3.Min(stats.BoundingBoxMin, new Vector3(x, y, z));
                                stats.BoundingBoxMax = Vector3.Max(stats.BoundingBoxMax, new Vector3(x, y, z));
                                
                                // Accumulate for center of mass
                                centerAccumulators[materialId] += new Vector3(x, y, z);
                            }
                        }
                    }
                }
                
                // Calculate totals and derived statistics
                _totalVoxels = _statistics.Values.Sum(s => s.VoxelCount);
                _totalVolume = 0;
                
                foreach (var stat in _statistics.Values)
                {
                    if (stat.VoxelCount > 0)
                    {
                        // Volume in cubic units (convert from micrometers to millimeters if needed)
                        float volumeInUnits = stat.VoxelCount * voxelVolume;
                        if (_dataset.Unit == "µm" || _dataset.Unit == "μm" || _dataset.Unit == "um")
                        {
                            stat.Volume = volumeInUnits / 1e9f; // Convert to mm³
                        }
                        else
                        {
                            stat.Volume = volumeInUnits;
                        }
                        
                        _totalVolume += stat.Volume;
                        
                        // Center of mass
                        stat.CenterOfMass = centerAccumulators[stat.Material.ID] / stat.VoxelCount;
                        
                        // Voxel percentage
                        stat.Percentage = (float)stat.VoxelCount / _totalVoxels * 100f;
                    }
                }
                
                // Calculate volume percentages
                if (_totalVolume > 0)
                {
                    foreach (var stat in _statistics.Values)
                    {
                        stat.VolumePercentage = stat.Volume / _totalVolume * 100f;
                    }
                }
            });
            
            Logger.Log($"[MaterialStatistics] Calculated statistics for {_statistics.Count} materials");
        }
        
        private void DrawChart()
        {
            var availableSize = new Vector2(330, 400);
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetCursorScreenPos();
            
            ImGui.BeginChild("ChartArea", availableSize, ImGuiChildFlags.Border);
            
            if (_statistics.Count > 0)
            {
                if (_chartType == ChartType.PieChart)
                {
                    DrawPieChart(drawList, pos + new Vector2(10, 10), availableSize - new Vector2(20, 20));
                }
                else
                {
                    DrawHistogram(drawList, pos + new Vector2(10, 10), availableSize - new Vector2(20, 20));
                }
            }
            else
            {
                ImGui.Text("Calculating statistics...");
            }
            
            ImGui.EndChild();
        }
        
        private void DrawPieChart(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
        {
            float radius = Math.Min(size.X, size.Y) * 0.4f;
            Vector2 center = pos + size * 0.5f;
            
            // Sort materials by volume for better visualization
            var sortedStats = _statistics.Values
                .Where(s => s.VoxelCount > 0)
                .OrderByDescending(s => s.Volume)
                .ToList();
            
            float currentAngle = -MathF.PI / 2; // Start at top
            
            foreach (var stat in sortedStats)
            {
                float angleSpan = stat.VolumePercentage / 100f * 2 * MathF.PI;
                
                // Draw pie slice
                DrawPieSlice(drawList, center, radius, currentAngle, currentAngle + angleSpan, 
                    ImGui.ColorConvertFloat4ToU32(stat.Material.Color));
                
                // Draw label if slice is big enough
                if (stat.VolumePercentage > 3)
                {
                    float labelAngle = currentAngle + angleSpan / 2;
                    Vector2 labelPos = center + new Vector2(
                        MathF.Cos(labelAngle) * radius * 0.7f,
                        MathF.Sin(labelAngle) * radius * 0.7f
                    );
                    
                    string label = $"{stat.VolumePercentage:F1}%";
                    var textSize = ImGui.CalcTextSize(label);
                    drawList.AddText(labelPos - textSize * 0.5f, 0xFFFFFFFF, label);
                }
                
                currentAngle += angleSpan;
            }
            
            // Draw legend
            Vector2 legendPos = pos + new Vector2(0, size.Y - 100);
            int column = 0;
            foreach (var stat in sortedStats.Take(6)) // Show top 6 materials
            {
                float x = legendPos.X + (column % 2) * 160;
                float y = legendPos.Y + (column / 2) * 20;
                
                // Color box
                drawList.AddRectFilled(
                    new Vector2(x, y),
                    new Vector2(x + 15, y + 15),
                    ImGui.ColorConvertFloat4ToU32(stat.Material.Color)
                );
                
                // Material name
                drawList.AddText(new Vector2(x + 20, y), 0xFFFFFFFF, 
                    stat.Material.Name.Length > 15 ? stat.Material.Name.Substring(0, 15) + "..." : stat.Material.Name);
                
                column++;
            }
        }
        
        private void DrawPieSlice(ImDrawListPtr drawList, Vector2 center, float radius, 
            float startAngle, float endAngle, uint color)
        {
            const int segments = 32;
            var points = new List<Vector2> { center };
            
            float angleStep = (endAngle - startAngle) / segments;
            for (int i = 0; i <= segments; i++)
            {
                float angle = startAngle + i * angleStep;
                points.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
            }
            
            // Draw filled polygon
            for (int i = 1; i < points.Count - 1; i++)
            {
                drawList.AddTriangleFilled(center, points[i], points[i + 1], color);
            }
            
            // Draw outline
            for (int i = 1; i < points.Count; i++)
            {
                drawList.AddLine(points[i - 1], points[i], 0xFF000000, 2);
            }
        }
        
        private void DrawHistogram(ImDrawListPtr drawList, Vector2 pos, Vector2 size)
        {
            var sortedStats = _statistics.Values
                .Where(s => s.VoxelCount > 0)
                .OrderByDescending(s => s.Volume)
                .ToList();
            
            if (sortedStats.Count == 0) return;
            
            float maxVolume = sortedStats[0].Volume;
            float barWidth = size.X / sortedStats.Count * 0.8f;
            float spacing = size.X / sortedStats.Count * 0.2f;
            
            for (int i = 0; i < sortedStats.Count; i++)
            {
                var stat = sortedStats[i];
                float barHeight = (stat.Volume / maxVolume) * size.Y * 0.8f;
                float x = pos.X + i * (barWidth + spacing);
                float y = pos.Y + size.Y - barHeight;
                
                // Draw bar
                drawList.AddRectFilled(
                    new Vector2(x, y),
                    new Vector2(x + barWidth, pos.Y + size.Y),
                    ImGui.ColorConvertFloat4ToU32(stat.Material.Color)
                );
                
                // Draw outline
                drawList.AddRect(
                    new Vector2(x, y),
                    new Vector2(x + barWidth, pos.Y + size.Y),
                    0xFF000000
                );
                
                // Draw value on top
                string value = $"{stat.Volume:F2}";
                var textSize = ImGui.CalcTextSize(value);
                drawList.AddText(new Vector2(x + barWidth/2 - textSize.X/2, y - 20), 0xFFFFFFFF, value);
            }
            
            // Y-axis label
            string unit = _dataset.Unit == "µm" || _dataset.Unit == "μm" || _dataset.Unit == "um" ? "mm³" : $"{_dataset.Unit}³";
            drawList.AddText(new Vector2(pos.X, pos.Y - 30), 0xFFFFFFFF, $"Volume ({unit})");
        }
        
        private void DrawStatisticsTable()
        {
            ImGui.BeginChild("TableArea", new Vector2(0, 0), ImGuiChildFlags.Border);
            
            // Summary information
            ImGui.Text($"Total Voxels: {_totalVoxels:N0}");
            string volumeUnit = _dataset.Unit == "µm" || _dataset.Unit == "μm" || _dataset.Unit == "um" ? "mm³" : $"{_dataset.Unit}³";
            ImGui.Text($"Total Volume: {_totalVolume:F3} {volumeUnit}");
            ImGui.Separator();
            
            // Detailed table
            if (ImGui.BeginTable("MaterialStats", 7, 
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | 
                ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.DefaultSort);
                ImGui.TableSetupColumn("Voxels", ImGuiTableColumnFlags.DefaultSort);
                ImGui.TableSetupColumn("Volume", ImGuiTableColumnFlags.DefaultSort);
                ImGui.TableSetupColumn("Vol %", ImGuiTableColumnFlags.DefaultSort);
                ImGui.TableSetupColumn("Center X,Y,Z");
                ImGui.TableSetupColumn("Size X,Y,Z");
                ImGui.TableSetupColumn("Color");
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                
                var sortedStats = _statistics.Values
                    .Where(s => s.VoxelCount > 0)
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
                    ImGui.Text($"{stat.Volume:F3} {volumeUnit}");
                    
                    ImGui.TableNextColumn();
                    ImGui.Text($"{stat.VolumePercentage:F2}%");
                    
                    ImGui.TableNextColumn();
                    ImGui.Text($"{stat.CenterOfMass.X:F1}, {stat.CenterOfMass.Y:F1}, {stat.CenterOfMass.Z:F1}");
                    
                    ImGui.TableNextColumn();
                    var size = stat.BoundingBoxMax - stat.BoundingBoxMin + Vector3.One;
                    ImGui.Text($"{size.X:F0}, {size.Y:F0}, {size.Z:F0}");
                    
                    ImGui.TableNextColumn();
                    Vector4 color = stat.Material.Color;
                    ImGui.ColorButton($"##color{stat.Material.ID}", color, ImGuiColorEditFlags.NoTooltip, new Vector2(30, 20));
                }
                
                ImGui.EndTable();
            }
            
            ImGui.EndChild();
        }
        
        private void ExportChartImage(string path)
        {
            // TODO: Implement actual image export
            // For now, just log the action
            Logger.Log($"[MaterialStatistics] Export chart image to: {path}");
            
            // In a real implementation, you would:
            // 1. Render the chart to a texture
            // 2. Read the texture data
            // 3. Save as PNG/JPEG using an image library
        }
        
        private void ExportStatisticsCsv(string path)
        {
            try
            {
                var sb = new StringBuilder();
                
                // Header
                string volumeUnit = _dataset.Unit == "µm" || _dataset.Unit == "μm" || _dataset.Unit == "um" ? "mm³" : $"{_dataset.Unit}³";
                sb.AppendLine($"Material,Voxel Count,Volume ({volumeUnit}),Volume %,Center X,Center Y,Center Z,Size X,Size Y,Size Z,Color R,Color G,Color B");
                
                // Data rows
                var sortedStats = _statistics.Values
                    .Where(s => s.VoxelCount > 0)
                    .OrderByDescending(s => s.Volume);
                
                foreach (var stat in sortedStats)
                {
                    var size = stat.BoundingBoxMax - stat.BoundingBoxMin + Vector3.One;
                    sb.AppendLine($"{stat.Material.Name}," +
                        $"{stat.VoxelCount}," +
                        $"{stat.Volume:F6}," +
                        $"{stat.VolumePercentage:F4}," +
                        $"{stat.CenterOfMass.X:F2}," +
                        $"{stat.CenterOfMass.Y:F2}," +
                        $"{stat.CenterOfMass.Z:F2}," +
                        $"{size.X:F0}," +
                        $"{size.Y:F0}," +
                        $"{size.Z:F0}," +
                        $"{stat.Material.Color.X:F3}," +
                        $"{stat.Material.Color.Y:F3}," +
                        $"{stat.Material.Color.Z:F3}");
                }
                
                // Summary row
                sb.AppendLine();
                sb.AppendLine($"Total,{_totalVoxels},{_totalVolume:F6},100.0000,,,,,,,,");
                
                File.WriteAllText(path, sb.ToString());
                Logger.Log($"[MaterialStatistics] Exported statistics to: {path}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[MaterialStatistics] Failed to export CSV: {ex.Message}");
            }
        }
        
        public void MarkForRecalculation()
        {
            _needsRecalculation = true;
        }
    }
}