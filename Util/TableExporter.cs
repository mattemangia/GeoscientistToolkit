// GeoscientistToolkit/Data/Table/TableExporter.cs

using System.Collections.Concurrent;
using System.Data;
using System.Numerics;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Table;

/// <summary>
///     Static helper class for exporting data to table formats
/// </summary>
public static class TableExporter
{
    /// <summary>
    ///     Exports material statistics from a CT dataset to a table dataset
    /// </summary>
    public static TableDataset ExportMaterialStatistics(CtImageStackDataset ctDataset)
    {
        try
        {
            var table = new DataTable($"{ctDataset.Name}_MaterialStats");

            // Define columns
            table.Columns.Add("Material ID", typeof(int));
            table.Columns.Add("Material Name", typeof(string));
            table.Columns.Add("Color", typeof(string));
            table.Columns.Add("Min Value", typeof(int));
            table.Columns.Add("Max Value", typeof(int));
            table.Columns.Add("Voxel Count", typeof(long));
            table.Columns.Add("Volume (mm³)", typeof(double));
            table.Columns.Add("Volume (%)", typeof(double));
            table.Columns.Add("Density", typeof(double));
            table.Columns.Add("Mass (g)", typeof(double));
            table.Columns.Add("Is Exterior", typeof(bool));
            table.Columns.Add("Is Visible", typeof(bool));

            // Calculate voxel size in mm³
            double voxelVolumeMm3 = 0;
            if (ctDataset.Unit == "µm")
                voxelVolumeMm3 = ctDataset.PixelSize / 1000.0 *
                                 (ctDataset.PixelSize / 1000.0) *
                                 (ctDataset.SliceThickness / 1000.0);
            else if (ctDataset.Unit == "mm")
                voxelVolumeMm3 = ctDataset.PixelSize *
                                 ctDataset.PixelSize *
                                 ctDataset.SliceThickness;

            // Count voxels for each material
            var materialCounts = new Dictionary<byte, long>();
            long totalVoxels = 0;

            // Count actual voxels from label data
            if (ctDataset.LabelData != null)
            {
                // Initialize counts for all materials
                foreach (var material in ctDataset.Materials)
                {
                    materialCounts[material.ID] = 0;
                }

                // Count voxels in parallel
                var localCounts = new ConcurrentDictionary<byte, long>();
                Parallel.For(0, ctDataset.Depth, z =>
                {
                    for (var y = 0; y < ctDataset.Height; y++)
                    {
                        for (var x = 0; x < ctDataset.Width; x++)
                        {
                            var label = ctDataset.LabelData[x, y, z];
                            localCounts.AddOrUpdate(label, 1, (key, oldValue) => oldValue + 1);
                        }
                    }
                });

                // Merge parallel results
                foreach (var kvp in localCounts)
                {
                    if (materialCounts.ContainsKey(kvp.Key))
                    {
                        materialCounts[kvp.Key] = kvp.Value;
                        totalVoxels += kvp.Value;
                    }
                }
            }
            else
            {
                Logger.LogWarning("No label data available for material statistics");
                foreach (var material in ctDataset.Materials)
                {
                    materialCounts[material.ID] = 0;
                }
            }

            // Add rows for each material
            foreach (var material in ctDataset.Materials)
            {
                var voxelCount = materialCounts.GetValueOrDefault(material.ID, 0);
                var volume = voxelCount * voxelVolumeMm3;
                var volumePercent = totalVoxels > 0 ? 100.0 * voxelCount / totalVoxels : 0;
                var mass = volume * material.Density / 1000.0; // Convert to grams

                table.Rows.Add(
                    material.ID,
                    material.Name,
                    $"#{(int)(material.Color.X * 255):X2}{(int)(material.Color.Y * 255):X2}{(int)(material.Color.Z * 255):X2}",
                    material.MinValue,
                    material.MaxValue,
                    voxelCount,
                    volume,
                    volumePercent,
                    material.Density,
                    mass,
                    material.IsExterior,
                    material.IsVisible
                );
            }

            var tableDataset = new TableDataset($"{ctDataset.Name}_MaterialStats", table);
            Logger.Log($"Created material statistics table with {table.Rows.Count} materials");

            return tableDataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export material statistics: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Exports slice statistics from a CT dataset
    /// </summary>
    public static TableDataset ExportSliceStatistics(CtImageStackDataset ctDataset)
    {
        try
        {
            var table = new DataTable($"{ctDataset.Name}_SliceStats");

            // Define columns
            table.Columns.Add("Slice Index", typeof(int));
            table.Columns.Add("Z Position (mm)", typeof(double));
            table.Columns.Add("Min Value", typeof(int));
            table.Columns.Add("Max Value", typeof(int));
            table.Columns.Add("Mean Value", typeof(double));
            table.Columns.Add("Std Dev", typeof(double));
            table.Columns.Add("Non-Zero Pixels", typeof(int));
            table.Columns.Add("Fill Ratio (%)", typeof(double));

            // Calculate statistics for each slice in parallel
            int numSlices = ctDataset.Depth;
            var sliceStats = new (int min, int max, double mean, double stdDev, int nonZero, double fillRatio)[numSlices];

            Parallel.For(0, numSlices, z =>
            {
                // Get slice data
                byte[] sliceData = null;
                try
                {
                    if (ctDataset.VolumeData != null)
                    {
                        sliceData = new byte[ctDataset.Width * ctDataset.Height];
                        ctDataset.VolumeData.ReadSliceZ(z, sliceData);
                    }
                }
                catch
                {
                    // If VolumeData access fails, use default values
                }

                int minValue = int.MaxValue;
                int maxValue = int.MinValue;
                long sum = 0;
                long sumSquares = 0;
                int nonZeroPixels = 0;
                int totalPixels = ctDataset.Width * ctDataset.Height;

                if (sliceData != null)
                {
                    for (var i = 0; i < sliceData.Length; i++)
                    {
                        var value = sliceData[i];

                        if (value < minValue) minValue = value;
                        if (value > maxValue) maxValue = value;

                        sum += value;
                        sumSquares += (long)value * value;

                        if (value > 0) nonZeroPixels++;
                    }
                }
                else
                {
                    // Fallback if no slice data available
                    minValue = 0;
                    maxValue = 0;
                }

                double meanValue = totalPixels > 0 ? (double)sum / totalPixels : 0;
                double variance = totalPixels > 0
                    ? (double)sumSquares / totalPixels - meanValue * meanValue
                    : 0;
                double stdDev = Math.Sqrt(Math.Max(0, variance));
                double fillRatio = totalPixels > 0 ? 100.0 * nonZeroPixels / totalPixels : 0;

                sliceStats[z] = (minValue, maxValue, meanValue, stdDev, nonZeroPixels, fillRatio);
            });

            // Add rows to table
            for (var z = 0; z < ctDataset.Depth; z++)
            {
                double zPosition = 0;
                if (ctDataset.Unit == "µm")
                    zPosition = z * ctDataset.SliceThickness / 1000.0; // Convert to mm
                else if (ctDataset.Unit == "mm")
                    zPosition = z * ctDataset.SliceThickness;

                var stats = sliceStats[z];
                table.Rows.Add(
                    z,
                    zPosition,
                    stats.min,
                    stats.max,
                    stats.mean,
                    stats.stdDev,
                    stats.nonZero,
                    stats.fillRatio
                );
            }

            var tableDataset = new TableDataset($"{ctDataset.Name}_SliceStats", table);
            Logger.Log($"Created slice statistics table with {table.Rows.Count} slices");

            return tableDataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export slice statistics: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Creates a histogram table from volume data
    /// </summary>
    public static TableDataset CreateHistogramTable(string name, int[] histogram, int binSize = 1)
    {
        try
        {
            var table = new DataTable($"{name}_Histogram");

            table.Columns.Add("Bin Start", typeof(int));
            table.Columns.Add("Bin End", typeof(int));
            table.Columns.Add("Count", typeof(int));
            table.Columns.Add("Frequency (%)", typeof(double));
            table.Columns.Add("Cumulative (%)", typeof(double));

            var totalCount = histogram.Sum(h => (long)h);
            long cumulativeCount = 0;

            for (var i = 0; i < histogram.Length; i++)
            {
                if (histogram[i] == 0) continue; // Skip empty bins

                var binStart = i * binSize;
                var binEnd = binStart + binSize - 1;
                cumulativeCount += histogram[i];

                var frequency = totalCount > 0 ? 100.0 * histogram[i] / totalCount : 0;
                var cumulative = totalCount > 0 ? 100.0 * cumulativeCount / totalCount : 0;

                table.Rows.Add(binStart, binEnd, histogram[i], frequency, cumulative);
            }

            var tableDataset = new TableDataset($"{name}_Histogram", table);
            Logger.Log($"Created histogram table with {table.Rows.Count} bins");

            return tableDataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create histogram table: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Exports measurement results to a table
    /// </summary>
    public static TableDataset ExportMeasurements(string name, List<Measurement> measurements)
    {
        try
        {
            var table = new DataTable($"{name}_Measurements");

            table.Columns.Add("ID", typeof(int));
            table.Columns.Add("Type", typeof(string));
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Value", typeof(double));
            table.Columns.Add("Unit", typeof(string));
            table.Columns.Add("X1", typeof(double));
            table.Columns.Add("Y1", typeof(double));
            table.Columns.Add("Z1", typeof(double));
            table.Columns.Add("X2", typeof(double));
            table.Columns.Add("Y2", typeof(double));
            table.Columns.Add("Z2", typeof(double));
            table.Columns.Add("Notes", typeof(string));
            table.Columns.Add("Timestamp", typeof(DateTime));

            foreach (var measurement in measurements)
                table.Rows.Add(
                    measurement.ID,
                    measurement.Type.ToString(),
                    measurement.Name,
                    measurement.Value,
                    measurement.Unit,
                    measurement.StartPoint.X,
                    measurement.StartPoint.Y,
                    measurement.StartPoint.Z,
                    measurement.EndPoint.X,
                    measurement.EndPoint.Y,
                    measurement.EndPoint.Z,
                    measurement.Notes,
                    measurement.Timestamp
                );

            var tableDataset = new TableDataset($"{name}_Measurements", table);
            Logger.Log($"Created measurements table with {table.Rows.Count} measurements");

            return tableDataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export measurements: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Creates a comparison table between multiple datasets
    /// </summary>
    public static TableDataset CreateComparisonTable(string name, List<Dataset> datasets)
    {
        try
        {
            var table = new DataTable($"{name}_Comparison");

            table.Columns.Add("Dataset Name", typeof(string));
            table.Columns.Add("Type", typeof(string));
            table.Columns.Add("Size (MB)", typeof(double));
            table.Columns.Add("Created", typeof(DateTime));
            table.Columns.Add("Modified", typeof(DateTime));
            table.Columns.Add("Path", typeof(string));

            // Add type-specific columns
            table.Columns.Add("Width", typeof(int));
            table.Columns.Add("Height", typeof(int));
            table.Columns.Add("Depth", typeof(int));
            table.Columns.Add("Pixel Size", typeof(double));
            table.Columns.Add("Unit", typeof(string));

            foreach (var dataset in datasets)
            {
                var row = table.NewRow();
                row["Dataset Name"] = dataset.Name;
                row["Type"] = dataset.Type.ToString();
                row["Size (MB)"] = dataset.GetSizeInBytes() / (1024.0 * 1024.0);
                row["Created"] = dataset.DateCreated;
                row["Modified"] = dataset.DateModified;
                row["Path"] = dataset.FilePath;

                // Add type-specific data
                if (dataset is CtImageStackDataset ctDataset)
                {
                    row["Width"] = ctDataset.Width;
                    row["Height"] = ctDataset.Height;
                    row["Depth"] = ctDataset.Depth;
                    row["Pixel Size"] = ctDataset.PixelSize;
                    row["Unit"] = ctDataset.Unit;
                }
                else
                {
                    row["Width"] = DBNull.Value;
                    row["Height"] = DBNull.Value;
                    row["Depth"] = DBNull.Value;
                    row["Pixel Size"] = DBNull.Value;
                    row["Unit"] = DBNull.Value;
                }

                table.Rows.Add(row);
            }

            var tableDataset = new TableDataset($"{name}_Comparison", table);
            Logger.Log($"Created comparison table for {datasets.Count} datasets");

            return tableDataset;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create comparison table: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
///     Represents a measurement for export
/// </summary>
public class Measurement
{
    public int ID { get; set; }
    public MeasurementType Type { get; set; }
    public string Name { get; set; }
    public double Value { get; set; }
    public string Unit { get; set; }
    public Vector3 StartPoint { get; set; }
    public Vector3 EndPoint { get; set; }
    public string Notes { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum MeasurementType
{
    Distance,
    Angle,
    Area,
    Volume,
    Intensity,
    Profile
}