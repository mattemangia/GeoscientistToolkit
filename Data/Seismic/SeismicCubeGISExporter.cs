// GeoscientistToolkit/Data/Seismic/SeismicCubeGISExporter.cs

using System.Numerics;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Seismic;

/// <summary>
/// Exports seismic cube data to SubsurfaceGIS format for 3D mapping of segmented packages.
/// Creates a voxel-based subsurface model from seismic interpretations.
/// </summary>
public class SeismicCubeGISExporter
{
    private readonly SeismicCubeDataset _cube;

    public SeismicCubeGISExporter(SeismicCubeDataset cube)
    {
        _cube = cube;
    }

    /// <summary>
    /// Export the seismic cube to a SubsurfaceGIS dataset
    /// </summary>
    public SubsurfaceGISDataset ExportToSubsurfaceGIS(string name)
    {
        Logger.Log($"[SeismicCubeGISExporter] Exporting cube to SubsurfaceGIS: {name}");

        var subsurface = new SubsurfaceGISDataset(name, "")
        {
            GridOrigin = new Vector3(_cube.Bounds.MinX, _cube.Bounds.MinY, _cube.Bounds.MinZ),
            GridSize = new Vector3(_cube.Bounds.Width, _cube.Bounds.Height, _cube.Bounds.Depth),
            GridResolutionX = _cube.GridParameters.InlineCount,
            GridResolutionY = _cube.GridParameters.CrosslineCount,
            GridResolutionZ = _cube.GridParameters.SampleCount,
            VoxelSize = new Vector3(
                _cube.GridParameters.InlineSpacing,
                _cube.GridParameters.CrosslineSpacing,
                _cube.GridParameters.SampleInterval
            )
        };

        // Build voxel grid from packages
        BuildVoxelGridFromPackages(subsurface);

        // Build layer boundaries from package horizons
        BuildLayerBoundaries(subsurface);

        // Add amplitude data as parameter
        if (_cube.RegularizedVolume != null)
        {
            AddAmplitudeData(subsurface);
        }

        Logger.Log($"[SeismicCubeGISExporter] Export complete: {subsurface.VoxelGrid.Count} voxels, " +
                   $"{subsurface.LayerBoundaries.Count} layer boundaries");

        return subsurface;
    }

    /// <summary>
    /// Build voxel grid from seismic packages
    /// </summary>
    private void BuildVoxelGridFromPackages(SubsurfaceGISDataset subsurface)
    {
        if (_cube.Packages.Count == 0)
        {
            Logger.LogWarning("[SeismicCubeGISExporter] No packages to export");
            return;
        }

        var grid = _cube.GridParameters;
        var bounds = _cube.Bounds;

        // For each voxel position, determine which package it belongs to
        for (int i = 0; i < grid.InlineCount; i++)
        {
            for (int j = 0; j < grid.CrosslineCount; j++)
            {
                for (int k = 0; k < grid.SampleCount; k++)
                {
                    float x = bounds.MinX + (i + 0.5f) * grid.InlineSpacing;
                    float y = bounds.MinY + (j + 0.5f) * grid.CrosslineSpacing;
                    float z = bounds.MinZ + (k + 0.5f) * grid.SampleInterval;

                    // Find which package this point belongs to
                    var package = FindPackageForPoint(x, y, z);

                    if (package != null)
                    {
                        var voxel = new SubsurfaceVoxel
                        {
                            Position = new Vector3(x, y, z),
                            LithologyType = !string.IsNullOrEmpty(package.LithologyType)
                                ? package.LithologyType
                                : package.Name,
                            Confidence = package.Confidence
                        };

                        // Add seismic facies if defined
                        if (!string.IsNullOrEmpty(package.SeismicFacies))
                        {
                            voxel.Parameters["SeismicFacies"] = 0; // Would need encoding
                        }

                        // Add amplitude if available
                        if (_cube.RegularizedVolume != null &&
                            i < _cube.RegularizedVolume.GetLength(0) &&
                            j < _cube.RegularizedVolume.GetLength(1) &&
                            k < _cube.RegularizedVolume.GetLength(2))
                        {
                            voxel.Parameters["Amplitude"] = _cube.RegularizedVolume[i, j, k];
                        }

                        subsurface.VoxelGrid.Add(voxel);
                    }
                }
            }

            // Progress logging
            if ((i + 1) % 20 == 0)
            {
                float progress = (float)(i + 1) / grid.InlineCount * 100;
                Logger.Log($"[SeismicCubeGISExporter] Voxel grid progress: {progress:F0}%");
            }
        }
    }

    /// <summary>
    /// Find which package a point belongs to based on horizon picking
    /// </summary>
    private SeismicCubePackage? FindPackageForPoint(float x, float y, float z)
    {
        // Simple approach: find the package whose horizon is closest above this point
        SeismicCubePackage? bestPackage = null;
        float minDistance = float.MaxValue;

        foreach (var package in _cube.Packages)
        {
            if (package.HorizonPoints.Count == 0 && package.HorizonGrid == null)
            {
                // Package without horizon - use time-based assignment
                continue;
            }

            float? horizonZ = GetHorizonZAtPosition(package, x, y);
            if (horizonZ.HasValue)
            {
                // Point is below this horizon
                if (z <= horizonZ.Value)
                {
                    float distance = horizonZ.Value - z;
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestPackage = package;
                    }
                }
            }
        }

        // If no horizon-based assignment, use first package as default
        if (bestPackage == null && _cube.Packages.Count > 0)
        {
            return _cube.Packages[0];
        }

        return bestPackage;
    }

    /// <summary>
    /// Get horizon Z value at a specific XY position
    /// </summary>
    private float? GetHorizonZAtPosition(SeismicCubePackage package, float x, float y)
    {
        // Try grid interpolation first
        if (package.HorizonGrid != null)
        {
            int nx = package.HorizonGrid.GetLength(0);
            int ny = package.HorizonGrid.GetLength(1);

            float normX = (x - _cube.Bounds.MinX) / _cube.Bounds.Width;
            float normY = (y - _cube.Bounds.MinY) / _cube.Bounds.Height;

            int i = Math.Clamp((int)(normX * (nx - 1)), 0, nx - 1);
            int j = Math.Clamp((int)(normY * (ny - 1)), 0, ny - 1);

            return package.HorizonGrid[i, j];
        }

        // Fall back to IDW interpolation from horizon points
        if (package.HorizonPoints.Count > 0)
        {
            return InterpolateHorizon(package.HorizonPoints, x, y);
        }

        return null;
    }

    /// <summary>
    /// Interpolate horizon Z from scattered points using IDW
    /// </summary>
    private float InterpolateHorizon(List<Vector3> points, float x, float y)
    {
        float weightSum = 0;
        float zSum = 0;
        float power = 2.0f;

        foreach (var point in points)
        {
            float dx = point.X - x;
            float dy = point.Y - y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist < 0.001f)
            {
                return point.Z;
            }

            float weight = 1.0f / MathF.Pow(dist, power);
            weightSum += weight;
            zSum += point.Z * weight;
        }

        return weightSum > 0 ? zSum / weightSum : 0;
    }

    /// <summary>
    /// Build layer boundaries from package horizons
    /// </summary>
    private void BuildLayerBoundaries(SubsurfaceGISDataset subsurface)
    {
        foreach (var package in _cube.Packages)
        {
            if (package.HorizonPoints.Count == 0 && package.HorizonGrid == null)
            {
                continue;
            }

            var boundary = new SubsurfaceLayerBoundary
            {
                LayerName = package.Name,
                Points = new List<Vector3>(package.HorizonPoints)
            };

            // Copy or build horizon grid
            if (package.HorizonGrid != null)
            {
                boundary.ElevationGrid = (float[,])package.HorizonGrid.Clone();
                boundary.GridBounds = new BoundingBox
                {
                    Min = new Vector2(_cube.Bounds.MinX, _cube.Bounds.MinY),
                    Max = new Vector2(_cube.Bounds.MaxX, _cube.Bounds.MaxY)
                };
            }
            else if (package.HorizonPoints.Count > 2)
            {
                // Build grid from points
                boundary.ElevationGrid = BuildHorizonGrid(package.HorizonPoints);
                boundary.GridBounds = new BoundingBox
                {
                    Min = new Vector2(_cube.Bounds.MinX, _cube.Bounds.MinY),
                    Max = new Vector2(_cube.Bounds.MaxX, _cube.Bounds.MaxY)
                };
            }

            subsurface.LayerBoundaries.Add(boundary);
        }
    }

    /// <summary>
    /// Build a regular grid from scattered horizon points
    /// </summary>
    private float[,] BuildHorizonGrid(List<Vector3> points)
    {
        int nx = _cube.GridParameters.InlineCount;
        int ny = _cube.GridParameters.CrosslineCount;
        var grid = new float[nx, ny];

        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                float x = _cube.Bounds.MinX + (i + 0.5f) * _cube.GridParameters.InlineSpacing;
                float y = _cube.Bounds.MinY + (j + 0.5f) * _cube.GridParameters.CrosslineSpacing;
                grid[i, j] = InterpolateHorizon(points, x, y);
            }
        }

        return grid;
    }

    /// <summary>
    /// Add amplitude data as a parameter to each voxel
    /// </summary>
    private void AddAmplitudeData(SubsurfaceGISDataset subsurface)
    {
        if (_cube.RegularizedVolume == null) return;

        var grid = _cube.GridParameters;
        var bounds = _cube.Bounds;

        foreach (var voxel in subsurface.VoxelGrid)
        {
            // Convert position to grid indices
            int i = (int)((voxel.Position.X - bounds.MinX) / grid.InlineSpacing);
            int j = (int)((voxel.Position.Y - bounds.MinY) / grid.CrosslineSpacing);
            int k = (int)((voxel.Position.Z - bounds.MinZ) / grid.SampleInterval);

            if (i >= 0 && i < _cube.RegularizedVolume.GetLength(0) &&
                j >= 0 && j < _cube.RegularizedVolume.GetLength(1) &&
                k >= 0 && k < _cube.RegularizedVolume.GetLength(2))
            {
                voxel.Parameters["Amplitude"] = _cube.RegularizedVolume[i, j, k];
            }
        }
    }

    /// <summary>
    /// Export a time slice as a GIS raster layer
    /// </summary>
    public GISRasterLayer ExportTimeSliceAsRaster(float timeMs, string name)
    {
        if (_cube.RegularizedVolume == null)
        {
            Logger.LogError("[SeismicCubeGISExporter] Cannot export: volume not built");
            return null;
        }

        var slice = _cube.GetTimeSlice(timeMs);
        if (slice == null)
        {
            Logger.LogError($"[SeismicCubeGISExporter] Cannot get time slice at {timeMs}ms");
            return null;
        }

        int nx = slice.GetLength(0);
        int ny = slice.GetLength(1);

        // Create raster data
        var pixelData = new float[nx, ny];
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                pixelData[i, j] = slice[i, j];
            }
        }

        var bounds = new BoundingBox
        {
            Min = new Vector2(_cube.Bounds.MinX, _cube.Bounds.MinY),
            Max = new Vector2(_cube.Bounds.MaxX, _cube.Bounds.MaxY)
        };

        var layer = new GISRasterLayer(pixelData, bounds)
        {
            Name = $"{name}_T{timeMs}ms"
        };

        Logger.Log($"[SeismicCubeGISExporter] Exported time slice at {timeMs}ms as raster layer");

        return layer;
    }

    /// <summary>
    /// Export amplitude statistics for each package
    /// </summary>
    public Dictionary<string, PackageAmplitudeStatistics> GetPackageStatistics()
    {
        var statistics = new Dictionary<string, PackageAmplitudeStatistics>();

        if (_cube.RegularizedVolume == null)
        {
            return statistics;
        }

        foreach (var package in _cube.Packages)
        {
            var stats = new PackageAmplitudeStatistics
            {
                PackageName = package.Name,
                LithologyType = package.LithologyType
            };

            var amplitudes = new List<float>();

            // Collect amplitudes for this package's voxels
            // (simplified - would need proper package-voxel mapping)
            var grid = _cube.GridParameters;
            var bounds = _cube.Bounds;

            for (int i = 0; i < grid.InlineCount; i++)
            {
                for (int j = 0; j < grid.CrosslineCount; j++)
                {
                    for (int k = 0; k < grid.SampleCount; k++)
                    {
                        float x = bounds.MinX + (i + 0.5f) * grid.InlineSpacing;
                        float y = bounds.MinY + (j + 0.5f) * grid.CrosslineSpacing;
                        float z = bounds.MinZ + (k + 0.5f) * grid.SampleInterval;

                        var pkg = FindPackageForPoint(x, y, z);
                        if (pkg == package)
                        {
                            amplitudes.Add(_cube.RegularizedVolume[i, j, k]);
                        }
                    }
                }
            }

            if (amplitudes.Count > 0)
            {
                stats.VoxelCount = amplitudes.Count;
                stats.MinAmplitude = amplitudes.Min();
                stats.MaxAmplitude = amplitudes.Max();
                stats.MeanAmplitude = amplitudes.Average();
                stats.RmsAmplitude = (float)Math.Sqrt(amplitudes.Average(a => a * a));

                amplitudes.Sort();
                stats.MedianAmplitude = amplitudes[amplitudes.Count / 2];
            }

            statistics[package.Name] = stats;
        }

        return statistics;
    }
}

/// <summary>
/// Amplitude statistics for a seismic package
/// </summary>
public class PackageAmplitudeStatistics
{
    public string PackageName { get; set; } = "";
    public string LithologyType { get; set; } = "";
    public int VoxelCount { get; set; }
    public float MinAmplitude { get; set; }
    public float MaxAmplitude { get; set; }
    public float MeanAmplitude { get; set; }
    public float MedianAmplitude { get; set; }
    public float RmsAmplitude { get; set; }
}
