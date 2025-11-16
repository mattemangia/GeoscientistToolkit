// GeoscientistToolkit/Data/GIS/SubsurfaceGISBuilder.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.GIS;

/// <summary>
/// Builder for creating 3D subsurface GIS datasets from 2D geological data
/// Allows creating 3D geology models from rectangle selections on 2D maps
/// </summary>
public static class SubsurfaceGISBuilder
{
    /// <summary>
    /// Create a 3D subsurface geology dataset from a rectangular region on a 2D geological map
    /// </summary>
    /// <param name="geologicalMap">2D geological map (raster or polygon layer with lithology information)</param>
    /// <param name="rectangle">Rectangle defining the area of interest (min/max X and Y coordinates)</param>
    /// <param name="depthRange">Depth range for the 3D model (min and max depth in meters)</param>
    /// <param name="heightmap">Optional heightmap for surface elevation</param>
    /// <param name="resolutionX">Number of voxels in X direction</param>
    /// <param name="resolutionY">Number of voxels in Y direction</param>
    /// <param name="resolutionZ">Number of voxels in Z direction (depth)</param>
    /// <param name="layerThicknessModel">Model for how lithology extends with depth</param>
    /// <returns>3D subsurface GIS dataset</returns>
    public static SubsurfaceGISDataset CreateFrom2DGeologicalMap(
        GISLayer geologicalMap,
        BoundingBox rectangle,
        (float minDepth, float maxDepth) depthRange,
        GISRasterLayer? heightmap = null,
        int resolutionX = 50,
        int resolutionY = 50,
        int resolutionZ = 30,
        DepthLayeringModel layerThicknessModel = DepthLayeringModel.Uniform)
    {
        if (geologicalMap == null)
            throw new ArgumentNullException(nameof(geologicalMap));
        if (rectangle == null)
            throw new ArgumentNullException(nameof(rectangle));
        if (depthRange.minDepth < 0 || depthRange.maxDepth <= depthRange.minDepth)
            throw new ArgumentException("Invalid depth range. Must be: 0 <= minDepth < maxDepth");
        if (resolutionX < 1 || resolutionY < 1 || resolutionZ < 1)
            throw new ArgumentException("Resolution values must be at least 1");

        var dataset = new SubsurfaceGISDataset(
            $"3D_Geology_{geologicalMap.Name}",
            $"3d_geology_{geologicalMap.Name.ToLower().Replace(" ", "_")}.subgis"
        );

        // Set up grid based on rectangle and depth range
        dataset.GridOrigin = new Vector3(
            rectangle.Min.X,
            rectangle.Min.Y,
            -depthRange.maxDepth
        );

        dataset.GridSize = new Vector3(
            rectangle.Width,
            rectangle.Height,
            depthRange.maxDepth - depthRange.minDepth
        );

        dataset.GridResolutionX = resolutionX;
        dataset.GridResolutionY = resolutionY;
        dataset.GridResolutionZ = resolutionZ;

        dataset.VoxelSize = new Vector3(
            dataset.GridSize.X / resolutionX,
            dataset.GridSize.Y / resolutionY,
            dataset.GridSize.Z / resolutionZ
        );

        Logger.Log($"Creating 3D geological model from 2D map '{geologicalMap.Name}'");
        Logger.Log($"Grid: {resolutionX}x{resolutionY}x{resolutionZ} voxels");
        Logger.Log($"Bounds: X=[{rectangle.Min.X}, {rectangle.Max.X}], Y=[{rectangle.Min.Y}, {rectangle.Max.Y}], Depth=[{depthRange.minDepth}, {depthRange.maxDepth}]");

        // Build voxel grid
        BuildVoxelGridFrom2DMap(dataset, geologicalMap, heightmap, depthRange, layerThicknessModel);

        // Extract layer boundaries
        ExtractLayerBoundariesFrom2DMap(dataset, geologicalMap, heightmap, depthRange);

        return dataset;
    }

    /// <summary>
    /// Build voxel grid by projecting 2D geological map into 3D
    /// </summary>
    private static void BuildVoxelGridFrom2DMap(
        SubsurfaceGISDataset dataset,
        GISLayer geologicalMap,
        GISRasterLayer? heightmap,
        (float minDepth, float maxDepth) depthRange,
        DepthLayeringModel layeringModel)
    {
        int totalVoxels = dataset.GridResolutionX * dataset.GridResolutionY * dataset.GridResolutionZ;
        int voxelsProcessed = 0;
        int voxelsCreated = 0;

        for (int i = 0; i < dataset.GridResolutionX; i++)
        {
            float x = dataset.GridOrigin.X + (i + 0.5f) * dataset.VoxelSize.X;

            for (int j = 0; j < dataset.GridResolutionY; j++)
            {
                float y = dataset.GridOrigin.Y + (j + 0.5f) * dataset.VoxelSize.Y;

                // Get surface elevation at this position
                float surfaceElevation = 0;
                if (heightmap != null)
                {
                    surfaceElevation = GetHeightmapValue(heightmap, x, y) ?? 0;
                }

                // Get lithology from 2D map at this position
                string? surfaceLithology = GetLithologyAt2DPosition(geologicalMap, x, y);

                if (surfaceLithology == null)
                {
                    voxelsProcessed += dataset.GridResolutionZ;
                    continue; // Skip this column if no lithology data
                }

                // Create voxels at different depths
                for (int k = 0; k < dataset.GridResolutionZ; k++)
                {
                    float depth = depthRange.minDepth + (k + 0.5f) * (depthRange.maxDepth - depthRange.minDepth) / dataset.GridResolutionZ;
                    float z = surfaceElevation - depth;

                    Vector3 position = new Vector3(x, y, z);

                    // Determine lithology at this depth based on layering model
                    var voxel = CreateGeologicalVoxel(
                        position,
                        surfaceLithology,
                        depth,
                        surfaceElevation,
                        layeringModel,
                        geologicalMap
                    );

                    if (voxel != null)
                    {
                        dataset.VoxelGrid.Add(voxel);
                        voxelsCreated++;
                    }

                    voxelsProcessed++;

                    // Log progress every 10%
                    if (totalVoxels >= 10 && voxelsProcessed % (totalVoxels / 10) == 0)
                    {
                        float progress = (float)voxelsProcessed / totalVoxels * 100;
                        Logger.Log($"3D geology grid progress: {progress:F1}% ({voxelsCreated} voxels created)");
                    }
                }
            }
        }

        Logger.Log($"3D geological voxel grid complete: {voxelsCreated} voxels created");
    }

    /// <summary>
    /// Create a geological voxel with lithology properties
    /// </summary>
    private static SubsurfaceVoxel CreateGeologicalVoxel(
        Vector3 position,
        string surfaceLithology,
        float depth,
        float surfaceElevation,
        DepthLayeringModel layeringModel,
        GISLayer geologicalMap)
    {
        var voxel = new SubsurfaceVoxel
        {
            Position = position,
            Parameters = new Dictionary<string, float>()
        };

        // Determine lithology based on depth and layering model
        switch (layeringModel)
        {
            case DepthLayeringModel.Uniform:
                // Same lithology throughout the depth
                voxel.LithologyType = surfaceLithology;
                voxel.Confidence = 1.0f - Math.Clamp(depth / 1000.0f, 0, 0.9f); // Decrease confidence with depth
                break;

            case DepthLayeringModel.LayeredWithTransitions:
                // Create simplified layered geology with transitions
                voxel.LithologyType = GetLayeredLithology(surfaceLithology, depth);
                voxel.Confidence = 1.0f - Math.Clamp(depth / 500.0f, 0, 0.8f);
                break;

            case DepthLayeringModel.WeatheredToFresh:
                // Weathered at surface, transitioning to fresh rock
                if (depth < 10.0f)
                {
                    voxel.LithologyType = $"Weathered_{surfaceLithology}";
                    voxel.Confidence = 0.9f;
                }
                else if (depth < 30.0f)
                {
                    voxel.LithologyType = $"Transitional_{surfaceLithology}";
                    voxel.Confidence = 0.8f;
                }
                else
                {
                    voxel.LithologyType = $"Fresh_{surfaceLithology}";
                    voxel.Confidence = 0.7f - Math.Clamp((depth - 30.0f) / 500.0f, 0, 0.5f);
                }
                break;

            case DepthLayeringModel.SedimentarySequence:
                // Typical sedimentary sequence
                voxel.LithologyType = GetSedimentaryLayerAtDepth(depth);
                voxel.Confidence = 0.6f - Math.Clamp(depth / 1000.0f, 0, 0.4f);
                break;
        }

        // Add depth-dependent properties
        AddLithologyProperties(voxel, depth);

        return voxel;
    }

    /// <summary>
    /// Get lithology for layered model based on depth
    /// </summary>
    private static string GetLayeredLithology(string surfaceLithology, float depth)
    {
        // Simple layering: surface -> intermediate -> basement
        if (depth < 50.0f)
            return surfaceLithology;
        else if (depth < 200.0f)
            return "Sedimentary_Rock";
        else if (depth < 500.0f)
            return "Metamorphic_Rock";
        else
            return "Basement_Rock";
    }

    /// <summary>
    /// Get sedimentary layer at specific depth (typical sequence)
    /// </summary>
    private static string GetSedimentaryLayerAtDepth(float depth)
    {
        if (depth < 10.0f)
            return "Soil_and_Regolith";
        else if (depth < 50.0f)
            return "Quaternary_Deposits";
        else if (depth < 150.0f)
            return "Tertiary_Sediments";
        else if (depth < 400.0f)
            return "Mesozoic_Sediments";
        else if (depth < 800.0f)
            return "Paleozoic_Sediments";
        else
            return "Basement_Complex";
    }

    /// <summary>
    /// Add lithology-specific properties to voxel
    /// </summary>
    private static void AddLithologyProperties(SubsurfaceVoxel voxel, float depth)
    {
        // Default geological properties based on common lithologies
        var defaultProps = GetDefaultLithologyProperties(voxel.LithologyType);

        foreach (var prop in defaultProps)
        {
            voxel.Parameters[prop.Key] = prop.Value;
        }

        // Add depth
        voxel.Parameters["Depth_m"] = depth;

        // Pressure estimate (lithostatic: ~25 MPa/km)
        voxel.Parameters["Lithostatic_Pressure_MPa"] = depth * 0.025f;

        // Temperature estimate (geothermal gradient: ~25°C/km)
        voxel.Parameters["Estimated_Temperature_C"] = 15.0f + depth * 0.025f;
    }

    /// <summary>
    /// Get default properties for common lithologies
    /// </summary>
    private static Dictionary<string, float> GetDefaultLithologyProperties(string lithology)
    {
        var props = new Dictionary<string, float>();

        // Simplified property lookup - can be expanded
        if (lithology.Contains("Sandstone") || lithology.Contains("Sand"))
        {
            props["Porosity"] = 0.25f;
            props["Permeability_mD"] = 100.0f;
            props["Density_g_cm3"] = 2.3f;
        }
        else if (lithology.Contains("Shale") || lithology.Contains("Clay"))
        {
            props["Porosity"] = 0.15f;
            props["Permeability_mD"] = 0.001f;
            props["Density_g_cm3"] = 2.5f;
        }
        else if (lithology.Contains("Limestone") || lithology.Contains("Carbonate"))
        {
            props["Porosity"] = 0.10f;
            props["Permeability_mD"] = 10.0f;
            props["Density_g_cm3"] = 2.6f;
        }
        else if (lithology.Contains("Granite") || lithology.Contains("Basement"))
        {
            props["Porosity"] = 0.01f;
            props["Permeability_mD"] = 0.0001f;
            props["Density_g_cm3"] = 2.7f;
        }
        else
        {
            // Default rock properties
            props["Porosity"] = 0.10f;
            props["Permeability_mD"] = 1.0f;
            props["Density_g_cm3"] = 2.5f;
        }

        return props;
    }

    /// <summary>
    /// Get lithology at a 2D position from geological map
    /// </summary>
    private static string? GetLithologyAt2DPosition(GISLayer layer, float x, float y)
    {
        if (layer is GISRasterLayer rasterLayer)
        {
            return GetLithologyFromRaster(rasterLayer, x, y);
        }
        else if (layer.Type == LayerType.Vector && layer.Features.Any(f => f.Type == FeatureType.Polygon))
        {
            return GetLithologyFromPolygons(layer, x, y);
        }

        return null;
    }

    /// <summary>
    /// Get lithology from raster layer
    /// </summary>
    private static string? GetLithologyFromRaster(GISRasterLayer raster, float x, float y)
    {
        var bounds = raster.Bounds;

        if (x < bounds.Min.X || x > bounds.Max.X || y < bounds.Min.Y || y > bounds.Max.Y)
            return null;

        // Avoid division by zero
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        // Convert world coordinates to pixel coordinates
        float normalizedX = (x - bounds.Min.X) / bounds.Width;
        float normalizedY = (y - bounds.Min.Y) / bounds.Height;

        int pixelX = (int)(normalizedX * raster.Width);
        int pixelY = (int)(normalizedY * raster.Height);

        pixelX = Math.Clamp(pixelX, 0, raster.Width - 1);
        pixelY = Math.Clamp(pixelY, 0, raster.Height - 1);

        // Get pixel value and map to lithology
        // Assuming raster has properties mapping pixel values to lithology names
        var pixelData = raster.GetPixelData();
        float pixelValue = pixelData[pixelX, pixelY];

        // Try to get lithology from properties
        if (raster.Properties.ContainsKey($"Lithology_{(int)pixelValue}"))
        {
            return raster.Properties[$"Lithology_{(int)pixelValue}"]?.ToString();
        }

        // Default: use pixel value as lithology ID
        return $"Lithology_{(int)pixelValue}";
    }

    /// <summary>
    /// Get lithology from polygon layer
    /// </summary>
    private static string? GetLithologyFromPolygons(GISLayer polygonLayer, float x, float y)
    {
        var point = new Vector2(x, y);

        foreach (var feature in polygonLayer.Features.Where(f => f.Type == FeatureType.Polygon))
        {
            if (IsPointInPolygon(point, feature.Coordinates))
            {
                // Check for lithology attribute
                if (feature.Properties.ContainsKey("Lithology"))
                {
                    return feature.Properties["Lithology"]?.ToString();
                }
                else if (feature.Properties.ContainsKey("Type"))
                {
                    return feature.Properties["Type"]?.ToString();
                }
                else if (feature.Properties.ContainsKey("Formation"))
                {
                    return feature.Properties["Formation"]?.ToString();
                }

                return "Unknown_Lithology";
            }
        }

        return null;
    }

    /// <summary>
    /// Check if point is inside polygon using ray casting algorithm
    /// </summary>
    private static bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        int j = polygon.Count - 1;

        for (int i = 0; i < polygon.Count; i++)
        {
            if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) /
                         (polygon[j].Y - polygon[i].Y) + polygon[i].X)
            {
                inside = !inside;
            }
            j = i;
        }

        return inside;
    }

    /// <summary>
    /// Get heightmap value at position
    /// </summary>
    private static float? GetHeightmapValue(GISRasterLayer heightmap, float x, float y)
    {
        var bounds = heightmap.Bounds;

        if (x < bounds.Min.X || x > bounds.Max.X || y < bounds.Min.Y || y > bounds.Max.Y)
            return null;

        // Avoid division by zero
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return null;

        float normalizedX = (x - bounds.Min.X) / bounds.Width;
        float normalizedY = (y - bounds.Min.Y) / bounds.Height;

        int pixelX = (int)(normalizedX * heightmap.Width);
        int pixelY = (int)(normalizedY * heightmap.Height);

        pixelX = Math.Clamp(pixelX, 0, heightmap.Width - 1);
        pixelY = Math.Clamp(pixelY, 0, heightmap.Height - 1);

        var pixelData = heightmap.GetPixelData();
        return pixelData[pixelX, pixelY];
    }

    /// <summary>
    /// Extract layer boundaries from 2D geological map
    /// </summary>
    private static void ExtractLayerBoundariesFrom2DMap(
        SubsurfaceGISDataset dataset,
        GISLayer geologicalMap,
        GISRasterLayer? heightmap,
        (float minDepth, float maxDepth) depthRange)
    {
        // Get unique lithologies from voxel grid
        var uniqueLithologies = dataset.VoxelGrid
            .Select(v => v.LithologyType)
            .Distinct()
            .ToList();

        Logger.Log($"Found {uniqueLithologies.Count} unique lithologies for layer boundaries");

        foreach (var lithology in uniqueLithologies)
        {
            var boundary = new SubsurfaceLayerBoundary
            {
                LayerName = lithology
            };

            // Find top surface of this lithology
            var topVoxels = dataset.VoxelGrid
                .Where(v => v.LithologyType == lithology)
                .GroupBy(v => new { X = v.Position.X, Y = v.Position.Y })
                .Select(g => g.OrderByDescending(v => v.Position.Z).First())
                .ToList();

            boundary.Points = topVoxels.Select(v => v.Position).ToList();

            if (boundary.Points.Count > 0)
            {
                dataset.LayerBoundaries.Add(boundary);
            }
        }

        Logger.Log($"Created {dataset.LayerBoundaries.Count} layer boundaries");
    }
}

/// <summary>
/// Models for how lithology extends with depth
/// </summary>
public enum DepthLayeringModel
{
    /// <summary>Uniform lithology throughout depth</summary>
    Uniform,

    /// <summary>Layered geology with transitions</summary>
    LayeredWithTransitions,

    /// <summary>Weathered at surface transitioning to fresh rock</summary>
    WeatheredToFresh,

    /// <summary>Typical sedimentary sequence</summary>
    SedimentarySequence
}
