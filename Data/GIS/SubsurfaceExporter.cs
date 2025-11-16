// GeoscientistToolkit/Data/GIS/SubsurfaceExporter.cs

using System.Numerics;
using System.Text;
using GeoscientistToolkit.Util;
using OSGeo.GDAL;

namespace GeoscientistToolkit.Data.GIS;

/// <summary>
/// Exports subsurface geothermal data to various formats for visualization and analysis
/// </summary>
public static class SubsurfaceExporter
{
    /// <summary>
    /// Export subsurface voxel grid to VTK format for 3D visualization in ParaView, Blender, etc.
    /// VTK (Visualization Toolkit) is a standard format for scientific visualization
    /// </summary>
    /// <param name="dataset">The subsurface dataset to export</param>
    /// <param name="path">Output file path (.vtk)</param>
    /// <param name="exportOptions">Options controlling which fields to export</param>
    /// <param name="progress">Progress reporting callback</param>
    /// <param name="token">Cancellation token</param>
    public static async Task ExportToVTKAsync(
        SubsurfaceGISDataset dataset,
        string path,
        VTKExportOptions exportOptions = null,
        IProgress<(float progress, string message)> progress = null,
        CancellationToken token = default)
    {
        await Task.Run(() =>
        {
            progress?.Report((0.0f, "Preparing VTK export..."));
            token.ThrowIfCancellationRequested();

            if (dataset.VoxelGrid == null || dataset.VoxelGrid.Count == 0)
            {
                Logger.LogError("Cannot export: No voxel data available");
                progress?.Report((1.0f, "Error: No voxel data"));
                return;
            }

            Logger.Log($"Exporting {dataset.VoxelGrid.Count} voxels to VTK format...");

            // Create structured grid VTK file
            var sb = new StringBuilder();

            // VTK Header
            sb.AppendLine("# vtk DataFile Version 3.0");
            sb.AppendLine($"Subsurface Geothermal Model - {dataset.Name}");
            sb.AppendLine("ASCII");
            sb.AppendLine("DATASET STRUCTURED_GRID");

            progress?.Report((0.1f, "Writing grid structure..."));
            token.ThrowIfCancellationRequested();

            // Grid dimensions
            int nx = dataset.GridResolutionX;
            int ny = dataset.GridResolutionY;
            int nz = dataset.GridResolutionZ;
            int totalPoints = nx * ny * nz;

            sb.AppendLine($"DIMENSIONS {nx} {ny} {nz}");
            sb.AppendLine($"POINTS {totalPoints} float");

            // Write point coordinates
            progress?.Report((0.2f, "Writing point coordinates..."));
            for (int k = 0; k < nz; k++)
            {
                for (int j = 0; j < ny; j++)
                {
                    for (int i = 0; i < nx; i++)
                    {
                        var x = dataset.GridOrigin.X + (i + 0.5f) * dataset.VoxelSize.X;
                        var y = dataset.GridOrigin.Y + (j + 0.5f) * dataset.VoxelSize.Y;
                        var z = dataset.GridOrigin.Z + (k + 0.5f) * dataset.VoxelSize.Z;
                        sb.AppendLine($"{x} {y} {z}");
                    }
                }

                if (k % 10 == 0)
                {
                    var prog = 0.2f + 0.3f * (float)k / nz;
                    progress?.Report((prog, $"Writing coordinates {k}/{nz}..."));
                    token.ThrowIfCancellationRequested();
                }
            }

            // Point data section
            sb.AppendLine($"POINT_DATA {totalPoints}");

            // Create a lookup for voxels by their grid index
            var voxelLookup = new Dictionary<(int, int, int), SubsurfaceVoxel>();
            foreach (var voxel in dataset.VoxelGrid)
            {
                int i = (int)((voxel.Position.X - dataset.GridOrigin.X) / dataset.VoxelSize.X);
                int j = (int)((voxel.Position.Y - dataset.GridOrigin.Y) / dataset.VoxelSize.Y);
                int k = (int)((voxel.Position.Z - dataset.GridOrigin.Z) / dataset.VoxelSize.Z);

                i = Math.Clamp(i, 0, nx - 1);
                j = Math.Clamp(j, 0, ny - 1);
                k = Math.Clamp(k, 0, nz - 1);

                voxelLookup[(i, j, k)] = voxel;
            }

            // Use default export options if not provided
            if (exportOptions == null)
            {
                exportOptions = new VTKExportOptions();
            }

            var exportedFields = new List<string>();
            float progressStep = 0.5f;
            float currentProgress = 0.5f;

            // Export temperature scalar field
            if (exportOptions.ExportTemperature)
            {
                progress?.Report((currentProgress, "Writing temperature data..."));
                token.ThrowIfCancellationRequested();

                sb.AppendLine("SCALARS Temperature float 1");
                sb.AppendLine("LOOKUP_TABLE default");
                for (int k = 0; k < nz; k++)
                {
                    for (int j = 0; j < ny; j++)
                    {
                        for (int i = 0; i < nx; i++)
                        {
                            if (voxelLookup.TryGetValue((i, j, k), out var voxel) &&
                                voxel.Parameters.TryGetValue("Temperature", out var temp))
                            {
                                sb.AppendLine(temp.ToString("F2"));
                            }
                            else
                            {
                                sb.AppendLine("0.0");
                            }
                        }
                    }
                }
                exportedFields.Add("Temperature");
                currentProgress += 0.1f;
            }

            // Export thermal conductivity
            if (exportOptions.ExportThermalConductivity)
            {
                progress?.Report((currentProgress, "Writing thermal conductivity data..."));
                token.ThrowIfCancellationRequested();

                sb.AppendLine("SCALARS ThermalConductivity float 1");
                sb.AppendLine("LOOKUP_TABLE default");
                for (int k = 0; k < nz; k++)
                {
                    for (int j = 0; j < ny; j++)
                    {
                        for (int i = 0; i < nx; i++)
                        {
                            if (voxelLookup.TryGetValue((i, j, k), out var voxel) &&
                                voxel.Parameters.TryGetValue("Thermal Conductivity", out var k_val))
                            {
                                sb.AppendLine(k_val.ToString("F3"));
                            }
                            else
                            {
                                sb.AppendLine("0.0");
                            }
                        }
                    }
                }
                exportedFields.Add("ThermalConductivity");
                currentProgress += 0.1f;
            }

            // Export porosity
            if (exportOptions.ExportPorosity)
            {
                progress?.Report((currentProgress, "Writing porosity data..."));
                token.ThrowIfCancellationRequested();

                sb.AppendLine("SCALARS Porosity float 1");
                sb.AppendLine("LOOKUP_TABLE default");
                for (int k = 0; k < nz; k++)
                {
                    for (int j = 0; j < ny; j++)
                    {
                        for (int i = 0; i < nx; i++)
                        {
                            if (voxelLookup.TryGetValue((i, j, k), out var voxel) &&
                                voxel.Parameters.TryGetValue("Porosity", out var porosity))
                            {
                                sb.AppendLine(porosity.ToString("F4"));
                            }
                            else
                            {
                                sb.AppendLine("0.0");
                            }
                        }
                    }
                }
                exportedFields.Add("Porosity");
                currentProgress += 0.1f;
            }

            // Export permeability (log scale for better visualization)
            if (exportOptions.ExportPermeability)
            {
                progress?.Report((currentProgress, "Writing permeability data..."));
                token.ThrowIfCancellationRequested();

                sb.AppendLine("SCALARS LogPermeability float 1");
                sb.AppendLine("LOOKUP_TABLE default");
                for (int k = 0; k < nz; k++)
                {
                    for (int j = 0; j < ny; j++)
                    {
                        for (int i = 0; i < nx; i++)
                        {
                            if (voxelLookup.TryGetValue((i, j, k), out var voxel) &&
                                voxel.Parameters.TryGetValue("Permeability", out var perm))
                            {
                                var logPerm = perm > 0 ? Math.Log10(perm) : -20.0;
                                sb.AppendLine(logPerm.ToString("F2"));
                            }
                            else
                            {
                                sb.AppendLine("-20.0");
                            }
                        }
                    }
                }
                exportedFields.Add("LogPermeability");
                currentProgress += 0.1f;
            }

            // Export confidence values
            if (exportOptions.ExportConfidence)
            {
                progress?.Report((currentProgress, "Writing confidence data..."));
                token.ThrowIfCancellationRequested();

                sb.AppendLine("SCALARS Confidence float 1");
                sb.AppendLine("LOOKUP_TABLE default");
                for (int k = 0; k < nz; k++)
                {
                    for (int j = 0; j < ny; j++)
                    {
                        for (int i = 0; i < nx; i++)
                        {
                            if (voxelLookup.TryGetValue((i, j, k), out var voxel))
                            {
                                sb.AppendLine(voxel.Confidence.ToString("F3"));
                            }
                            else
                            {
                                sb.AppendLine("0.0");
                            }
                        }
                    }
                }
                exportedFields.Add("Confidence");
                currentProgress += 0.1f;
            }

            // Write to file
            progress?.Report((0.95f, "Writing VTK file..."));
            token.ThrowIfCancellationRequested();

            File.WriteAllText(path, sb.ToString());

            progress?.Report((1.0f, "VTK export complete"));
            Logger.Log($"Successfully exported subsurface model to VTK: {path}");
            Logger.Log($"  Grid dimensions: {nx}x{ny}x{nz} = {totalPoints} points");
            Logger.Log($"  Exported fields: {string.Join(", ", exportedFields)}");

        }, token);
    }

    /// <summary>
    /// Export subsurface voxel data to CSV format for analysis in Excel, Python, R, etc.
    /// </summary>
    public static async Task ExportToCSVAsync(
        SubsurfaceGISDataset dataset,
        string path,
        IProgress<(float progress, string message)> progress = null,
        CancellationToken token = default)
    {
        await Task.Run(() =>
        {
            progress?.Report((0.0f, "Preparing CSV export..."));
            token.ThrowIfCancellationRequested();

            if (dataset.VoxelGrid == null || dataset.VoxelGrid.Count == 0)
            {
                Logger.LogError("Cannot export: No voxel data available");
                progress?.Report((1.0f, "Error: No voxel data"));
                return;
            }

            Logger.Log($"Exporting {dataset.VoxelGrid.Count} voxels to CSV format...");

            var sb = new StringBuilder();

            // Header row - collect all unique parameter names
            var allParameterNames = dataset.VoxelGrid
                .SelectMany(v => v.Parameters.Keys)
                .Distinct()
                .OrderBy(k => k)
                .ToList();

            sb.Append("X,Y,Z,LithologyType,Confidence");
            foreach (var param in allParameterNames)
            {
                sb.Append($",{param}");
            }
            sb.AppendLine();

            progress?.Report((0.1f, "Writing voxel data..."));
            token.ThrowIfCancellationRequested();

            // Data rows
            int processed = 0;
            int total = dataset.VoxelGrid.Count;

            foreach (var voxel in dataset.VoxelGrid)
            {
                sb.Append($"{voxel.Position.X:F2}");
                sb.Append($",{voxel.Position.Y:F2}");
                sb.Append($",{voxel.Position.Z:F2}");
                sb.Append($",\"{voxel.LithologyType}\"");
                sb.Append($",{voxel.Confidence:F4}");

                foreach (var paramName in allParameterNames)
                {
                    if (voxel.Parameters.TryGetValue(paramName, out var value))
                    {
                        sb.Append($",{value}");
                    }
                    else
                    {
                        sb.Append(",");
                    }
                }
                sb.AppendLine();

                processed++;
                if (processed % 1000 == 0)
                {
                    var prog = 0.1f + 0.8f * (float)processed / total;
                    progress?.Report((prog, $"Writing voxels {processed}/{total}..."));
                    token.ThrowIfCancellationRequested();
                }
            }

            progress?.Report((0.9f, "Writing CSV file..."));
            token.ThrowIfCancellationRequested();

            File.WriteAllText(path, sb.ToString());

            progress?.Report((1.0f, "CSV export complete"));
            Logger.Log($"Successfully exported voxel data to CSV: {path}");
            Logger.Log($"  Total voxels: {dataset.VoxelGrid.Count}");
            Logger.Log($"  Columns: X, Y, Z, LithologyType, Confidence, {string.Join(", ", allParameterNames)}");

        }, token);
    }

    /// <summary>
    /// Generate 2D geothermal potential maps at different depth slices
    /// Returns a list of raster layers that can be exported to GeoTIFF
    /// </summary>
    /// <param name="dataset">The subsurface dataset</param>
    /// <param name="depthSlices">Depths (in meters below surface) for horizontal slices</param>
    /// <returns>Dictionary of depth -> geothermal potential map (temperature grid)</returns>
    public static Dictionary<float, GeothermalPotentialMap> GenerateGeothermalPotentialMaps(
        SubsurfaceGISDataset dataset,
        float[] depthSlices = null)
    {
        Logger.Log("Generating geothermal potential maps...");

        // Default depth slices if not provided
        if (depthSlices == null || depthSlices.Length == 0)
        {
            depthSlices = new float[] { 500, 1000, 1500, 2000, 2500, 3000 }; // meters
        }

        var maps = new Dictionary<float, GeothermalPotentialMap>();

        foreach (var depth in depthSlices)
        {
            Logger.Log($"Generating map at {depth}m depth...");

            var map = new GeothermalPotentialMap
            {
                Depth = depth,
                Width = dataset.GridResolutionX,
                Height = dataset.GridResolutionY,
                OriginX = dataset.GridOrigin.X,
                OriginY = dataset.GridOrigin.Y,
                PixelWidth = dataset.VoxelSize.X,
                PixelHeight = dataset.VoxelSize.Y,
                TemperatureGrid = new float[dataset.GridResolutionX, dataset.GridResolutionY],
                ThermalConductivityGrid = new float[dataset.GridResolutionX, dataset.GridResolutionY],
                HeatFlowGrid = new float[dataset.GridResolutionX, dataset.GridResolutionY],
                ConfidenceGrid = new float[dataset.GridResolutionX, dataset.GridResolutionY]
            };

            // Find the elevation corresponding to this depth
            // We'll interpolate voxels at approximately this elevation
            for (int i = 0; i < dataset.GridResolutionX; i++)
            {
                for (int j = 0; j < dataset.GridResolutionY; j++)
                {
                    // Find voxels at this X,Y position and closest to target depth
                    var x = dataset.GridOrigin.X + (i + 0.5f) * dataset.VoxelSize.X;
                    var y = dataset.GridOrigin.Y + (j + 0.5f) * dataset.VoxelSize.Y;

                    // Find voxel closest to this depth
                    // Since depth is measured from surface, we need to find the corresponding Z elevation
                    // For now, we'll sample at a fixed Z based on depth
                    var targetZ = dataset.GridOrigin.Z + dataset.GridSize.Z - depth;

                    var closestVoxel = dataset.VoxelGrid
                        .Where(v =>
                            Math.Abs(v.Position.X - x) < dataset.VoxelSize.X * 0.6f &&
                            Math.Abs(v.Position.Y - y) < dataset.VoxelSize.Y * 0.6f)
                        .OrderBy(v => Math.Abs(v.Position.Z - targetZ))
                        .FirstOrDefault();

                    if (closestVoxel != null)
                    {
                        // Temperature
                        if (closestVoxel.Parameters.TryGetValue("Temperature", out var temp))
                        {
                            map.TemperatureGrid[i, j] = temp;
                        }

                        // Thermal conductivity
                        if (closestVoxel.Parameters.TryGetValue("Thermal Conductivity", out var k))
                        {
                            map.ThermalConductivityGrid[i, j] = k;
                        }

                        // Heat flow (q = k * grad(T), simplified as k * geothermal_gradient)
                        // Assuming average geothermal gradient of 30°C/km = 0.03 K/m
                        var geothermalGradient = 0.03f;
                        map.HeatFlowGrid[i, j] = map.ThermalConductivityGrid[i, j] * geothermalGradient * 1000; // mW/m²

                        // Confidence
                        map.ConfidenceGrid[i, j] = closestVoxel.Confidence;
                    }
                }
            }

            // Calculate statistics
            var temps = map.TemperatureGrid.Cast<float>().Where(t => t > 0).ToArray();
            if (temps.Length > 0)
            {
                map.MinTemperature = temps.Min();
                map.MaxTemperature = temps.Max();
                map.MeanTemperature = temps.Average();
            }

            var heatFlows = map.HeatFlowGrid.Cast<float>().Where(h => h > 0).ToArray();
            if (heatFlows.Length > 0)
            {
                map.MinHeatFlow = heatFlows.Min();
                map.MaxHeatFlow = heatFlows.Max();
                map.MeanHeatFlow = heatFlows.Average();
            }

            maps[depth] = map;

            Logger.Log($"  Map at {depth}m: T = {map.MinTemperature:F1}-{map.MaxTemperature:F1}°C, " +
                      $"q = {map.MinHeatFlow:F1}-{map.MaxHeatFlow:F1} mW/m²");
        }

        Logger.Log($"Generated {maps.Count} geothermal potential maps");
        return maps;
    }

    /// <summary>
    /// Export a geothermal potential map to GeoTIFF format
    /// </summary>
    public static async Task ExportGeothermalMapToGeoTiffAsync(
        GeothermalPotentialMap map,
        string path,
        GeothermalMapType mapType = GeothermalMapType.Temperature,
        IProgress<(float progress, string message)> progress = null,
        CancellationToken token = default)
    {
        await Task.Run(() =>
        {
            progress?.Report((0.0f, $"Exporting {mapType} map to GeoTIFF..."));
            token.ThrowIfCancellationRequested();

            try
            {
                Gdal.AllRegister();

                var driver = Gdal.GetDriverByName("GTiff");
                if (driver == null)
                {
                    Logger.LogError("GDAL GTiff driver not available");
                    progress?.Report((1.0f, "Error: GDAL driver not available"));
                    return;
                }

                progress?.Report((0.2f, "Creating GeoTIFF dataset..."));

                // Create the output dataset
                using var dataset = driver.Create(
                    path,
                    map.Width,
                    map.Height,
                    1, // Single band
                    DataType.GDT_Float32,
                    null);

                if (dataset == null)
                {
                    Logger.LogError($"Failed to create GeoTIFF file: {path}");
                    progress?.Report((1.0f, "Error: Failed to create file"));
                    return;
                }

                // Set geotransform (defines pixel -> world coordinates)
                var geoTransform = new double[]
                {
                    map.OriginX,        // Top left X
                    map.PixelWidth,     // Pixel width
                    0,                  // Rotation (0 for north-up)
                    map.OriginY + map.Height * map.PixelHeight, // Top left Y
                    0,                  // Rotation (0 for north-up)
                    -map.PixelHeight    // Pixel height (negative for north-up)
                };
                dataset.SetGeoTransform(geoTransform);

                // Set projection (WGS84 / EPSG:4326)
                dataset.SetProjection(
                    @"GEOGCS[""WGS 84"",DATUM[""WGS_1984"",SPHEROID[""WGS 84"",6378137,298.257223563]]," +
                    @"PRIMEM[""Greenwich"",0],UNIT[""degree"",0.0174532925199433],AUTHORITY[""EPSG"",""4326""]]");

                progress?.Report((0.4f, "Writing raster data..."));
                token.ThrowIfCancellationRequested();

                // Get the data to write based on map type
                float[,] dataGrid = mapType switch
                {
                    GeothermalMapType.Temperature => map.TemperatureGrid,
                    GeothermalMapType.ThermalConductivity => map.ThermalConductivityGrid,
                    GeothermalMapType.HeatFlow => map.HeatFlowGrid,
                    GeothermalMapType.Confidence => map.ConfidenceGrid,
                    _ => map.TemperatureGrid
                };

                // Convert 2D array to 1D for GDAL
                var flatData = new float[map.Width * map.Height];
                for (int j = 0; j < map.Height; j++)
                {
                    for (int i = 0; i < map.Width; i++)
                    {
                        flatData[j * map.Width + i] = dataGrid[i, j];
                    }
                }

                // Write the band
                using var band = dataset.GetRasterBand(1);
                band.WriteRaster(0, 0, map.Width, map.Height, flatData, map.Width, map.Height, 0, 0);

                // Set band metadata
                var unitStr = mapType switch
                {
                    GeothermalMapType.Temperature => "degrees_Celsius",
                    GeothermalMapType.ThermalConductivity => "W/m/K",
                    GeothermalMapType.HeatFlow => "mW/m²",
                    GeothermalMapType.Confidence => "dimensionless",
                    _ => "unknown"
                };
                band.SetUnitType(unitStr);
                band.SetDescription($"{mapType} at {map.Depth}m depth");

                // Calculate and set statistics
                double min, max, mean, stddev;
                band.GetStatistics(0, 1, out min, out max, out mean, out stddev);
                band.SetStatistics(min, max, mean, stddev);

                progress?.Report((0.9f, "Finalizing GeoTIFF..."));
                dataset.FlushCache();

                progress?.Report((1.0f, "GeoTIFF export complete"));
                Logger.Log($"Exported {mapType} map to GeoTIFF: {path}");
                Logger.Log($"  Dimensions: {map.Width}x{map.Height}");
                Logger.Log($"  Value range: {min:F2} - {max:F2} {unitStr}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export GeoTIFF: {ex.Message}");
                progress?.Report((1.0f, $"Error: {ex.Message}"));
            }
        }, token);
    }
}

/// <summary>
/// Represents a 2D geothermal potential map at a specific depth
/// </summary>
public class GeothermalPotentialMap
{
    public float Depth { get; set; } // Depth in meters below surface
    public int Width { get; set; }
    public int Height { get; set; }
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    public double PixelWidth { get; set; }
    public double PixelHeight { get; set; }

    // Data grids
    public float[,] TemperatureGrid { get; set; } // °C
    public float[,] ThermalConductivityGrid { get; set; } // W/m·K
    public float[,] HeatFlowGrid { get; set; } // mW/m²
    public float[,] ConfidenceGrid { get; set; } // 0-1

    // Statistics
    public float MinTemperature { get; set; }
    public float MaxTemperature { get; set; }
    public float MeanTemperature { get; set; }
    public float MinHeatFlow { get; set; }
    public float MaxHeatFlow { get; set; }
    public float MeanHeatFlow { get; set; }
}

/// <summary>
/// Type of geothermal map to export
/// </summary>
public enum GeothermalMapType
{
    Temperature,
    ThermalConductivity,
    HeatFlow,
    Confidence
}

/// <summary>
/// Options for VTK export
/// </summary>
public class VTKExportOptions
{
    public bool ExportTemperature { get; set; } = true;
    public bool ExportThermalConductivity { get; set; } = true;
    public bool ExportPorosity { get; set; } = true;
    public bool ExportPermeability { get; set; } = true;
    public bool ExportConfidence { get; set; } = true;
}
