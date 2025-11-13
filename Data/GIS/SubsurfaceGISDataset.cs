// GeoscientistToolkit/Data/GIS/SubsurfaceGISDataset.cs

using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.GIS;

/// <summary>
/// Represents a 3D voxel grid point in the subsurface model
/// </summary>
public class SubsurfaceVoxel
{
    public Vector3 Position { get; set; }  // X, Y, Z coordinates
    public string LithologyType { get; set; }
    public Dictionary<string, float> Parameters { get; set; } = new();
    public float Confidence { get; set; } = 1.0f; // 0-1, based on distance to nearest borehole
}

/// <summary>
/// Represents a layer boundary surface in the subsurface model
/// </summary>
public class SubsurfaceLayerBoundary
{
    public string LayerName { get; set; }
    public List<Vector3> Points { get; set; } = new();
    public float[,] ElevationGrid { get; set; }
    public BoundingBox GridBounds { get; set; }
}

/// <summary>
/// 3D Subsurface GIS Dataset for geothermal mapping and visualization
/// Interpolates borehole data to create a 3D subsurface model
/// </summary>
public class SubsurfaceGISDataset : GISDataset
{
    public SubsurfaceGISDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.SubsurfaceGIS;
        Tags |= GISTag.ThreeDimensional | GISTag.Generated | GISTag.Subsurface;
    }

    public SubsurfaceGISDataset(Data.SubsurfaceGISDatasetDTO dto) : base(dto.Name, dto.FilePath)
    {
        Type = DatasetType.SubsurfaceGIS;
        Tags |= GISTag.ThreeDimensional | GISTag.Generated | GISTag.Subsurface;
        SourceBoreholeNames = dto.SourceBoreholeNames ?? new List<string>();
        VoxelGrid = dto.VoxelGrid?.Select(v => new SubsurfaceVoxel
        {
            Position = v.Position,
            LithologyType = v.LithologyType,
            Parameters = v.Parameters,
            Confidence = v.Confidence
        }).ToList() ?? new List<SubsurfaceVoxel>();
        LayerBoundaries = dto.LayerBoundaries?.Select(b => new SubsurfaceLayerBoundary
        {
            LayerName = b.LayerName,
            Points = b.Points,
            ElevationGrid = b.ElevationGrid,
            GridBounds = b.GridBounds != null ? new BoundingBox { Min = b.GridBounds.Min, Max = b.GridBounds.Max } : null
        }).ToList() ?? new List<SubsurfaceLayerBoundary>();
        GridOrigin = dto.GridOrigin;
        GridSize = dto.GridSize;
        VoxelSize = dto.VoxelSize;
        GridResolutionX = dto.GridResolutionX;
        GridResolutionY = dto.GridResolutionY;
        GridResolutionZ = dto.GridResolutionZ;
        InterpolationRadius = dto.InterpolationRadius;
        Method = (InterpolationMethod)dto.InterpolationMethod;
        IDWPower = dto.IDWPower;
        HeightmapDatasetName = dto.HeightmapDatasetName;
    }


    // Source boreholes used to create this model
    public List<string> SourceBoreholeNames { get; set; } = new();
    
    // 3D voxel grid for the subsurface
    public List<SubsurfaceVoxel> VoxelGrid { get; set; } = new();
    
    // Layer boundaries interpolated from boreholes
    public List<SubsurfaceLayerBoundary> LayerBoundaries { get; set; } = new();
    
    // Grid parameters
    public Vector3 GridOrigin { get; set; }
    public Vector3 GridSize { get; set; }
    public Vector3 VoxelSize { get; set; }
    public int GridResolutionX { get; set; } = 50;
    public int GridResolutionY { get; set; } = 50;
    public int GridResolutionZ { get; set; } = 100;
    
    // Interpolation settings
    public float InterpolationRadius { get; set; } = 100.0f; // meters
    public InterpolationMethod Method { get; set; } = InterpolationMethod.InverseDistanceWeighted;
    public float IDWPower { get; set; } = 2.0f;
    
    // Heightmap reference (if used)
    public string HeightmapDatasetName { get; set; }
    
    /// <summary>
    /// Converts the dataset to a serializable DTO
    /// </summary>
    public new object ToSerializableObject()
    {
        var baseDto = base.ToSerializableObject() as GISDatasetDTO;
        
        return new SubsurfaceGISDatasetDTO
        {
            TypeName = nameof(SubsurfaceGISDataset),
            Name = Name,
            FilePath = FilePath,
            Layers = baseDto.Layers,
            BasemapType = baseDto.BasemapType,
            BasemapPath = baseDto.BasemapPath,
            ActiveBasemapLayerName = baseDto.ActiveBasemapLayerName,
            Center = baseDto.Center,
            DefaultZoom = baseDto.DefaultZoom,
            Tags = baseDto.Tags,
            GISMetadata = baseDto.GISMetadata,
            SourceBoreholeNames = new List<string>(SourceBoreholeNames),
            VoxelGrid = VoxelGrid.Select(v => new SubsurfaceVoxelDTO
            {
                Position = v.Position,
                LithologyType = v.LithologyType,
                Parameters = new Dictionary<string, float>(v.Parameters),
                Confidence = v.Confidence
            }).ToList(),
            LayerBoundaries = LayerBoundaries.Select(b => new SubsurfaceLayerBoundaryDTO
            {
                LayerName = b.LayerName,
                Points = new List<Vector3>(b.Points),
                ElevationGrid = b.ElevationGrid,
                GridBounds = b.GridBounds != null ? new BoundingBoxDTO 
                { 
                    Min = b.GridBounds.Min, 
                    Max = b.GridBounds.Max 
                } : null
            }).ToList(),
            GridOrigin = GridOrigin,
            GridSize = GridSize,
            VoxelSize = VoxelSize,
            GridResolutionX = GridResolutionX,
            GridResolutionY = GridResolutionY,
            GridResolutionZ = GridResolutionZ,
            InterpolationRadius = InterpolationRadius,
            InterpolationMethod = (int)Method,
            IDWPower = IDWPower,
            HeightmapDatasetName = HeightmapDatasetName
        };
    }
    
    /// <summary>
    /// Build the 3D subsurface model from a list of boreholes
    /// </summary>
    public void BuildFromBoreholes(List<BoreholeDataset> boreholes, GISRasterLayer heightmap = null)
    {
        if (boreholes == null || boreholes.Count == 0)
        {
            Logger.LogError("Cannot build subsurface model: no boreholes provided");
            return;
        }

        Logger.Log($"Building 3D subsurface model from {boreholes.Count} boreholes");

        // Store source borehole names
        SourceBoreholeNames = boreholes.Select(b => b.WellName).ToList();

        // Calculate grid bounds from borehole positions
        CalculateGridBounds(boreholes, heightmap);

        // Place boreholes at correct elevations
        PlaceBoreholes(boreholes, heightmap);

        // Build layer boundaries
        BuildLayerBoundaries(boreholes);

        // Build voxel grid
        BuildVoxelGrid(boreholes);

        Logger.Log($"Subsurface model complete: {VoxelGrid.Count} voxels, {LayerBoundaries.Count} layer boundaries");
    }

    /// <summary>
    /// Calculate the grid bounds based on borehole positions and depths
    /// </summary>
    private void CalculateGridBounds(List<BoreholeDataset> boreholes, GISRasterLayer heightmap)
    {
        if (boreholes.Count == 0) return;

        // Find min/max coordinates
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;

        foreach (var borehole in boreholes)
        {
            var coords = borehole.SurfaceCoordinates;
            var elevation = borehole.Elevation;
            
            minX = Math.Min(minX, coords.X);
            maxX = Math.Max(maxX, coords.X);
            minY = Math.Min(minY, coords.Y);
            maxY = Math.Max(maxY, coords.Y);
            
            // Top is surface elevation
            maxZ = Math.Max(maxZ, elevation);
            
            // Bottom is surface elevation minus total depth
            minZ = Math.Min(minZ, elevation - borehole.TotalDepth);
        }

        // Add buffer around boreholes (20% on each side)
        float bufferX = (maxX - minX) * 0.2f;
        float bufferY = (maxY - minY) * 0.2f;
        
        GridOrigin = new Vector3(minX - bufferX, minY - bufferY, minZ);
        GridSize = new Vector3(
            maxX - minX + 2 * bufferX,
            maxY - minY + 2 * bufferY,
            maxZ - minZ
        );

        VoxelSize = new Vector3(
            GridSize.X / GridResolutionX,
            GridSize.Y / GridResolutionY,
            GridSize.Z / GridResolutionZ
        );

        Logger.Log($"Grid bounds: Origin={GridOrigin}, Size={GridSize}, VoxelSize={VoxelSize}");
    }

    /// <summary>
    /// Place boreholes at correct elevations, interpolating with heightmap if available
    /// </summary>
    private void PlaceBoreholes(List<BoreholeDataset> boreholes, GISRasterLayer heightmap)
    {
        foreach (var borehole in boreholes)
        {
            if (heightmap != null)
            {
                // Interpolate elevation from heightmap at borehole position
                var interpolatedElevation = InterpolateHeightmapAt(
                    heightmap, 
                    borehole.SurfaceCoordinates.X, 
                    borehole.SurfaceCoordinates.Y
                );
                
                if (interpolatedElevation.HasValue)
                {
                    Logger.Log($"Borehole {borehole.WellName}: using heightmap elevation {interpolatedElevation.Value:F2}m " +
                              $"(original: {borehole.Elevation:F2}m)");
                    borehole.Elevation = interpolatedElevation.Value;
                }
            }
            else if (borehole.Elevation == 0)
            {
                // If no heightmap and no elevation set, interpolate from other boreholes
                var interpolatedElevation = InterpolateElevationFromBoreholes(borehole, boreholes);
                if (interpolatedElevation.HasValue)
                {
                    Logger.Log($"Borehole {borehole.WellName}: interpolated elevation {interpolatedElevation.Value:F2}m from nearby boreholes");
                    borehole.Elevation = interpolatedElevation.Value;
                }
            }
        }
    }

    /// <summary>
    /// Interpolate elevation from heightmap at a given position
    /// </summary>
    private float? InterpolateHeightmapAt(GISRasterLayer heightmap, float x, float y)
    {
        if (heightmap == null) return null;

        var bounds = heightmap.Bounds;
        var pixelData = heightmap.GetPixelData();

        // Check if position is within heightmap bounds
        if (x < bounds.Min.X || x > bounds.Max.X || y < bounds.Min.Y || y > bounds.Max.Y)
            return null;

        // Convert world coordinates to pixel coordinates
        float normalizedX = (x - bounds.Min.X) / bounds.Width;
        float normalizedY = (y - bounds.Min.Y) / bounds.Height;

        int pixelX = (int)(normalizedX * heightmap.Width);
        int pixelY = (int)(normalizedY * heightmap.Height);

        // Clamp to valid range
        pixelX = Math.Clamp(pixelX, 0, heightmap.Width - 1);
        pixelY = Math.Clamp(pixelY, 0, heightmap.Height - 1);

        return pixelData[pixelX, pixelY];
    }

    /// <summary>
    /// Interpolate elevation from nearby boreholes
    /// </summary>
    private float? InterpolateElevationFromBoreholes(BoreholeDataset targetBorehole, List<BoreholeDataset> allBoreholes)
    {
        var nearbyBoreholes = allBoreholes
            .Where(b => b != targetBorehole && b.Elevation != 0)
            .Select(b => new
            {
                Borehole = b,
                Distance = Vector2.Distance(targetBorehole.SurfaceCoordinates, b.SurfaceCoordinates)
            })
            .Where(x => x.Distance < InterpolationRadius)
            .OrderBy(x => x.Distance)
            .Take(5)
            .ToList();

        if (nearbyBoreholes.Count == 0)
            return null;

        // IDW interpolation
        float weightSum = 0;
        float elevationSum = 0;

        foreach (var nearby in nearbyBoreholes)
        {
            float weight = nearby.Distance < 0.001f ? 1000f : 1.0f / MathF.Pow(nearby.Distance, IDWPower);
            weightSum += weight;
            elevationSum += nearby.Borehole.Elevation * weight;
        }

        return elevationSum / weightSum;
    }

    /// <summary>
    /// Build layer boundaries from boreholes
    /// </summary>
    private void BuildLayerBoundaries(List<BoreholeDataset> boreholes)
    {
        // Get all unique layer names across all boreholes
        var layerNames = boreholes
            .SelectMany(b => b.LithologyUnits.Select(u => u.LithologyType))
            .Distinct()
            .ToList();

        Logger.Log($"Found {layerNames.Count} unique lithology types");

        foreach (var layerName in layerNames)
        {
            var boundary = new SubsurfaceLayerBoundary
            {
                LayerName = layerName
            };

            // For each borehole, find the top of this layer
            foreach (var borehole in boreholes)
            {
                var unit = borehole.LithologyUnits.FirstOrDefault(u => u.LithologyType == layerName);
                if (unit != null)
                {
                    float topElevation = borehole.Elevation - unit.DepthFrom;
                    boundary.Points.Add(new Vector3(
                        borehole.SurfaceCoordinates.X,
                        borehole.SurfaceCoordinates.Y,
                        topElevation
                    ));
                }
            }

            if (boundary.Points.Count > 0)
            {
                LayerBoundaries.Add(boundary);
            }
        }
    }

    /// <summary>
    /// Build the voxel grid by interpolating borehole data
    /// </summary>
    private void BuildVoxelGrid(List<BoreholeDataset> boreholes)
    {
        Logger.Log($"Building voxel grid: {GridResolutionX}x{GridResolutionY}x{GridResolutionZ}");

        int totalVoxels = GridResolutionX * GridResolutionY * GridResolutionZ;
        int voxelsProcessed = 0;
        int voxelsCreated = 0;

        for (int i = 0; i < GridResolutionX; i++)
        {
            for (int j = 0; j < GridResolutionY; j++)
            {
                for (int k = 0; k < GridResolutionZ; k++)
                {
                    Vector3 position = new Vector3(
                        GridOrigin.X + (i + 0.5f) * VoxelSize.X,
                        GridOrigin.Y + (j + 0.5f) * VoxelSize.Y,
                        GridOrigin.Z + (k + 0.5f) * VoxelSize.Z
                    );

                    var voxel = InterpolateVoxel(boreholes, position);
                    if (voxel != null)
                    {
                        VoxelGrid.Add(voxel);
                        voxelsCreated++;
                    }

                    voxelsProcessed++;

                    // Log progress every 10%
                    if (voxelsProcessed % (totalVoxels / 10) == 0)
                    {
                        float progress = (float)voxelsProcessed / totalVoxels * 100;
                        Logger.Log($"Voxel grid progress: {progress:F1}% ({voxelsCreated} voxels created)");
                    }
                }
            }
        }

        Logger.Log($"Voxel grid complete: {voxelsCreated} voxels created out of {totalVoxels} positions");
    }

    /// <summary>
    /// Interpolate voxel properties from nearby boreholes
    /// </summary>
    private SubsurfaceVoxel InterpolateVoxel(List<BoreholeDataset> boreholes, Vector3 position)
    {
        var targetPos2D = new Vector2(position.X, position.Y);

        // Find nearby boreholes and their lithology at this depth
        var nearbyData = new List<(BoreholeDataset Borehole, LithologyUnit Unit, float Distance, float Depth)>();

        foreach (var borehole in boreholes)
        {
            float distance = Vector2.Distance(targetPos2D, borehole.SurfaceCoordinates);
            if (distance > InterpolationRadius) continue;

            // Calculate depth at this position
            float depth = borehole.Elevation - position.Z;
            
            // Find lithology unit at this depth
            var unit = borehole.LithologyUnits.FirstOrDefault(u => 
                depth >= u.DepthFrom && depth <= u.DepthTo
            );

            if (unit != null)
            {
                nearbyData.Add((borehole, unit, distance, depth));
            }
        }

        if (nearbyData.Count == 0)
            return null;

        // Use Inverse Distance Weighting to interpolate lithology and parameters
        var lithologyWeights = new Dictionary<string, float>();
        var parameterSums = new Dictionary<string, float>();
        var parameterWeightSums = new Dictionary<string, float>();
        float totalWeight = 0;
        float minDistance = nearbyData.Min(d => d.Distance);

        foreach (var (borehole, unit, distance, depth) in nearbyData)
        {
            float weight = distance < 0.001f ? 1000f : 1.0f / MathF.Pow(distance, IDWPower);
            totalWeight += weight;

            // Weight lithology votes
            if (!lithologyWeights.ContainsKey(unit.LithologyType))
                lithologyWeights[unit.LithologyType] = 0;
            lithologyWeights[unit.LithologyType] += weight;

            // Weight parameters
            foreach (var param in unit.Parameters)
            {
                if (!parameterSums.ContainsKey(param.Key))
                {
                    parameterSums[param.Key] = 0;
                    parameterWeightSums[param.Key] = 0;
                }
                parameterSums[param.Key] += param.Value * weight;
                parameterWeightSums[param.Key] += weight;
            }
        }

        // Select dominant lithology
        string dominantLithology = lithologyWeights.OrderByDescending(kvp => kvp.Value).First().Key;

        // Calculate average parameters
        var averageParameters = new Dictionary<string, float>();
        foreach (var param in parameterSums)
        {
            averageParameters[param.Key] = param.Value / parameterWeightSums[param.Key];
        }

        // Calculate confidence based on distance to nearest borehole
        float confidence = 1.0f - Math.Clamp(minDistance / InterpolationRadius, 0, 1);

        return new SubsurfaceVoxel
        {
            Position = position,
            LithologyType = dominantLithology,
            Parameters = averageParameters,
            Confidence = confidence
        };
    }

    /// <summary>
    /// Add interpolated simulation results to the voxel grid
    /// </summary>
    public void AddSimulationResults(List<BoreholeDataset> boreholes, Dictionary<string, GeothermalSimulationResults> boreholeResults)
    {
        if (boreholeResults == null || boreholeResults.Count == 0 || boreholes == null || boreholes.Count == 0)
        {
            Logger.LogWarning("No simulation results or source boreholes to add to subsurface model");
            return;
        }

        Logger.Log($"Adding simulation results from {boreholeResults.Count} boreholes to subsurface model");

        // Create a lookup for borehole positions
        var boreholePositions = boreholes.ToDictionary(b => b.WellName, b => b.SurfaceCoordinates);

        // For each voxel, interpolate simulation results
        foreach (var voxel in VoxelGrid)
        {
            InterpolateSimulationDataToVoxel(voxel, boreholeResults, boreholePositions);
        }

        Logger.Log("Simulation results interpolated to subsurface model");
    }

    /// <summary>
    /// Interpolate simulation results to a specific voxel
    /// </summary>
    private void InterpolateSimulationDataToVoxel(
        SubsurfaceVoxel voxel,
        Dictionary<string, GeothermalSimulationResults> boreholeResults,
        Dictionary<string, Vector2> boreholePositions)
    {
        var targetPos2D = new Vector2(voxel.Position.X, voxel.Position.Y);
        float voxelElevation = voxel.Position.Z;

        var nearbyResults = new List<(string BoreholeName, float Temperature, float Distance)>();

        foreach (var kvp in boreholeResults)
        {
            var boreholeName = kvp.Key;
            var results = kvp.Value;
            var options = results.Options;

            if (options == null || options.BoreholeDataset == null || results.FinalTemperatureField == null)
            {
                continue;
            }

            // Get borehole position
            if (!boreholePositions.TryGetValue(boreholeName, out var boreholePos))
            {
                continue;
            }

            float distance = Vector2.Distance(targetPos2D, boreholePos);
            if (distance > InterpolationRadius)
            {
                continue;
            }

            // Find temperature at the voxel's elevation from this borehole's simulation
            var borehole = options.BoreholeDataset;
            var depth = borehole.Elevation - voxelElevation;

            if (depth < 0 || depth > borehole.TotalDepth)
            {
                continue; // Voxel is outside this borehole's depth range
            }

            var tempField = results.FinalTemperatureField;
            int nz = tempField.GetLength(2);
            int nr = tempField.GetLength(0);
            int nth = tempField.GetLength(1);

            // Find the correct vertical index (k)
            float depthFraction = depth / borehole.TotalDepth;
            int k = Math.Clamp((int)(depthFraction * (nz - 1)), 0, nz - 1);

            // Average the temperature at that depth layer, away from the immediate borehole wall
            float avgTemp = 0;
            int count = 0;
            int radialStart = Math.Min(5, nr - 1); // Start away from the borehole
            for (int i = radialStart; i < nr; i++)
            {
                for (int j = 0; j < nth; j++)
                {
                    avgTemp += (tempField[i, j, k] - 273.15f); // Convert K to C
                    count++;
                }
            }

            if (count > 0)
            {
                nearbyResults.Add((boreholeName, avgTemp / count, distance));
            }
        }

        if (nearbyResults.Count == 0)
            return;

        // IDW interpolation for temperature
        float weightSum = 0;
        float tempSum = 0;

        foreach (var nearby in nearbyResults)
        {
            if (nearby.Distance < 0.001f)
            {
                // If we are right at a borehole, use its value directly
                voxel.Parameters["Temperature"] = nearby.Temperature;
                return;
            }
            float weight = 1.0f / MathF.Pow(nearby.Distance, IDWPower);
            weightSum += weight;
            tempSum += nearby.Temperature * weight;
        }

        if (weightSum > 0)
        {
            voxel.Parameters["Temperature"] = tempSum / weightSum;
        }
    }
}


/// <summary>
/// Interpolation methods for subsurface modeling
/// </summary>
public enum InterpolationMethod
{
    InverseDistanceWeighted,
    NearestNeighbor,
    Kriging,
    NaturalNeighbor
}