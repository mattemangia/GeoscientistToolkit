// GeoscientistToolkit/Data/GIS/GISTag.cs

namespace GeoscientistToolkit.Data.GIS;

/// <summary>
///     Defines the type/category of a GIS dataset for specialized processing and operations
/// </summary>
[Flags]
public enum GISTag : long
{
    None = 0,

    // Data Format Types
    Shapefile = 1 << 0,
    GeoJSON = 1 << 1,
    KML = 1 << 2,
    KMZ = 1 << 3,
    GeoTIFF = 1 << 4,
    GeoPackage = 1 << 5,
    FileGDB = 1 << 6,

    // Geometry Types
    VectorData = 1 << 7,
    RasterData = 1 << 8,
    PointCloud = 1 << 9,
    TIN = 1 << 10, // Triangulated Irregular Network

    // Purpose/Content Types
    Topography = 1 << 11,
    Basemap = 1 << 12,
    LandRegister = 1 << 13,
    Cadastral = 1 << 14,
    Satellite = 1 << 15,
    Aerial = 1 << 16,
    Geological = 1 << 17,
    GeologicalMap = 1 << 18,
    StructuralData = 1 << 19,
    Geophysical = 1 << 20,
    Administrative = 1 << 21,
    Infrastructure = 1 << 22,
    Hydrography = 1 << 23,
    Vegetation = 1 << 24,
    LandUse = 1 << 25,
    Bathymetry = 1 << 26,
    Seismic = 1 << 27,

    // Analysis Types
    DEM = 1 << 28, // Digital Elevation Model
    DSM = 1 << 29, // Digital Surface Model
    DTM = 1 << 30, // Digital Terrain Model
    Slope = 1 << 31,
    Aspect = 1L << 32,
    Hillshade = 1L << 33,
    Contours = 1L << 34,
    Watershed = 1L << 35,
    FlowDirection = 1L << 36,

    // Properties
    Georeferenced = 1L << 37,
    Projected = 1L << 38,
    MultiLayer = 1L << 39,
    Editable = 1L << 40,
    Cached = 1L << 41,
    Validated = 1L << 42,
    Cleaned = 1L << 43,
    Attributed = 1L << 44, // Has attribute data
    Styled = 1L << 45, // Has custom styling
    TimeSeries = 1L << 46,
    Multispectral = 1L << 47,
    ThreeDimensional = 1L << 48,

    // Source Types
    Survey = 1L << 49,
    RemoteSensing = 1L << 50,
    Generated = 1L << 51,
    Imported = 1L << 52,
    OpenData = 1L << 53,
    Commercial = 1L << 54,
    FieldData = 1L << 55,
    LiDAR = 1L << 56,
    UAV = 1L << 57, // Unmanned Aerial Vehicle / Drone
    GPS = 1L << 58
}

public static class GISTagExtensions
{
    private static readonly Dictionary<GISTag, string> _displayNames = new()
    {
        // Data Formats
        { GISTag.Shapefile, "Shapefile" },
        { GISTag.GeoJSON, "GeoJSON" },
        { GISTag.KML, "KML (Keyhole Markup Language)" },
        { GISTag.KMZ, "KMZ (Compressed KML)" },
        { GISTag.GeoTIFF, "GeoTIFF" },
        { GISTag.GeoPackage, "GeoPackage" },
        { GISTag.FileGDB, "File Geodatabase" },

        // Geometry Types
        { GISTag.VectorData, "Vector Data" },
        { GISTag.RasterData, "Raster Data" },
        { GISTag.PointCloud, "Point Cloud" },
        { GISTag.TIN, "Triangulated Irregular Network" },

        // Purpose/Content
        { GISTag.Topography, "Topography" },
        { GISTag.Basemap, "Basemap" },
        { GISTag.LandRegister, "Land Register" },
        { GISTag.Cadastral, "Cadastral Map" },
        { GISTag.Satellite, "Satellite Imagery" },
        { GISTag.Aerial, "Aerial Photography" },
        { GISTag.Geological, "Geological Data" },
        { GISTag.GeologicalMap, "Geological Map" },
        { GISTag.StructuralData, "Structural Geology Data" },
        { GISTag.Geophysical, "Geophysical Data" },
        { GISTag.Administrative, "Administrative Boundaries" },
        { GISTag.Infrastructure, "Infrastructure" },
        { GISTag.Hydrography, "Hydrography" },
        { GISTag.Vegetation, "Vegetation/Land Cover" },
        { GISTag.LandUse, "Land Use" },
        { GISTag.Bathymetry, "Bathymetry" },
        { GISTag.Seismic, "Seismic Data" },

        // Analysis Types
        { GISTag.DEM, "Digital Elevation Model" },
        { GISTag.DSM, "Digital Surface Model" },
        { GISTag.DTM, "Digital Terrain Model" },
        { GISTag.Slope, "Slope Analysis" },
        { GISTag.Aspect, "Aspect Analysis" },
        { GISTag.Hillshade, "Hillshade" },
        { GISTag.Contours, "Contour Lines" },
        { GISTag.Watershed, "Watershed Analysis" },
        { GISTag.FlowDirection, "Flow Direction" },

        // Properties
        { GISTag.Georeferenced, "Georeferenced" },
        { GISTag.Projected, "Projected CRS" },
        { GISTag.MultiLayer, "Multi-Layer" },
        { GISTag.Editable, "Editable" },
        { GISTag.Cached, "Cached" },
        { GISTag.Validated, "Validated" },
        { GISTag.Cleaned, "Cleaned" },
        { GISTag.Attributed, "Has Attributes" },
        { GISTag.Styled, "Custom Styled" },
        { GISTag.TimeSeries, "Time Series" },
        { GISTag.Multispectral, "Multispectral" },
        { GISTag.ThreeDimensional, "3D Data" },

        // Source Types
        { GISTag.Survey, "Survey Data" },
        { GISTag.RemoteSensing, "Remote Sensing" },
        { GISTag.Generated, "Generated/Computed" },
        { GISTag.Imported, "Imported" },
        { GISTag.OpenData, "Open Data" },
        { GISTag.Commercial, "Commercial Data" },
        { GISTag.FieldData, "Field Collection" },
        { GISTag.LiDAR, "LiDAR" },
        { GISTag.UAV, "UAV/Drone" },
        { GISTag.GPS, "GPS Data" }
    };

    private static readonly Dictionary<GISTag, string[]> _availableOperations = new()
    {
        // Raster Operations
        {
            GISTag.RasterData,
            new[] { "Reclassify", "Resample", "Mosaic", "Extract by Mask", "Raster Calculator", "Zonal Statistics" }
        },
        {
            GISTag.GeoTIFF,
            new[] { "Histogram Stretch", "Band Math", "Color Relief", "Extract Bands", "Convert to Vector" }
        },

        // Elevation/Terrain Operations
        {
            GISTag.DEM,
            new[]
            {
                "Slope Analysis", "Aspect Analysis", "Hillshade", "Contour Generation", "Viewshed", "Cut/Fill",
                "Profile Extraction"
            }
        },
        {
            GISTag.Topography,
            new[]
            {
                "Terrain Analysis", "Roughness", "TPI/TRI", "Curvature", "Drainage Network", "Watershed Delineation"
            }
        },
        {
            GISTag.Bathymetry,
            new[] { "Depth Contours", "Volume Calculation", "Cross Sections", "3D Visualization" }
        },
        {
            GISTag.Hillshade,
            new[] { "Adjust Illumination", "Multi-directional", "Combine with Color Relief" }
        },
        {
            GISTag.Slope,
            new[] { "Classify Slopes", "Stability Analysis", "Erosion Risk" }
        },

        // Vector Operations
        {
            GISTag.VectorData,
            new[] { "Buffer", "Clip", "Intersect", "Union", "Dissolve", "Merge", "Split", "Simplify" }
        },
        {
            GISTag.Shapefile,
            new[] { "Attribute Table", "Join", "Spatial Join", "Convert Format", "Repair Geometry" }
        },

        // Geological Operations
        {
            GISTag.GeologicalMap,
            new[]
            {
                "Lithology Classification", "Structural Analysis", "Cross Section", "Stratigraphic Column", "Age Dating"
            }
        },
        {
            GISTag.StructuralData,
            new[] { "Stereonet", "Rose Diagram", "Fabric Analysis", "Lineament Extraction", "Fold Analysis" }
        },
        {
            GISTag.Seismic,
            new[] { "Event Mapping", "Magnitude Analysis", "Depth Distribution", "Fault Plane Solutions" }
        },
        {
            GISTag.Geophysical,
            new[] { "Anomaly Detection", "Gradient Analysis", "Upward Continuation", "Grid to Points" }
        },

        // Land/Environmental Operations
        {
            GISTag.LandUse,
            new[] { "Classification", "Change Detection", "Area Statistics", "Suitability Analysis" }
        },
        {
            GISTag.Vegetation,
            new[] { "NDVI Calculation", "Classification", "Change Detection", "Biomass Estimation" }
        },
        {
            GISTag.Hydrography,
            new[] { "Stream Order", "Flow Accumulation", "Drainage Density", "Basin Analysis" }
        },
        {
            GISTag.Watershed,
            new[] { "Basin Delineation", "Pour Points", "Sub-basins", "Drainage Area" }
        },

        // Administrative/Cadastral
        {
            GISTag.Cadastral,
            new[] { "Parcel Analysis", "Ownership Query", "Boundary Verification", "Area Calculation" }
        },
        {
            GISTag.LandRegister,
            new[] { "Property Search", "Boundary Display", "Legal Description", "Zoning Info" }
        },
        {
            GISTag.Administrative,
            new[] { "Boundary Analysis", "Jurisdiction Query", "Aggregation", "Demographic Join" }
        },

        // Remote Sensing
        {
            GISTag.Satellite,
            new[]
            {
                "Band Combination", "Atmospheric Correction", "Orthorectification", "Change Detection", "Classification"
            }
        },
        {
            GISTag.Multispectral,
            new[]
            {
                "Band Math", "Index Calculation", "PCA", "Supervised Classification", "Unsupervised Classification"
            }
        },
        {
            GISTag.LiDAR,
            new[]
            {
                "Point Cloud Filtering", "DEM Generation", "Building Extraction", "Vegetation Height",
                "Intensity Analysis"
            }
        },
        {
            GISTag.UAV,
            new[] { "Orthomosaic", "DSM Generation", "3D Model", "Volume Measurement", "Change Detection" }
        },

        // Analysis/Processing
        {
            GISTag.PointCloud,
            new[] { "Ground Classification", "Vegetation Extraction", "Building Detection", "Thin", "Interpolate" }
        },
        {
            GISTag.TIN,
            new[] { "Surface Analysis", "Volume Calculation", "Contour Generation", "3D Visualization" }
        }
    };

    private static readonly Dictionary<GISTag, string> _categoryDescriptions = new()
    {
        { GISTag.Topography, "Terrain and elevation data for surface analysis and modeling" },
        { GISTag.Geological, "Rock formations, mineral deposits, and subsurface structures" },
        { GISTag.GeologicalMap, "Surface geology representation with lithology and structures" },
        { GISTag.Cadastral, "Property boundaries and land ownership information" },
        { GISTag.Hydrography, "Water features including rivers, lakes, and watersheds" },
        { GISTag.Satellite, "Satellite-acquired Earth observation imagery" },
        { GISTag.LiDAR, "Laser scanning point cloud data for high-resolution topography" },
        { GISTag.Seismic, "Earthquake and tectonic activity data" },
        { GISTag.Geophysical, "Gravity, magnetic, electrical, and other geophysical measurements" }
    };

    public static string GetDisplayName(this GISTag tag)
    {
        return _displayNames.TryGetValue(tag, out var name) ? name : tag.ToString();
    }

    public static string GetCategoryDescription(this GISTag tag)
    {
        return _categoryDescriptions.TryGetValue(tag, out var desc) ? desc : "";
    }

    public static string[] GetAvailableOperations(this GISTag tag)
    {
        var operations = new HashSet<string>
        {
            // Always available basic operations
            "View Properties",
            "Export",
            "Coordinate Transform",
            "Clip to Extent"
        };

        foreach (var flag in GetFlags(tag))
            if (_availableOperations.TryGetValue(flag, out var specificOps))
                foreach (var op in specificOps)
                    operations.Add(op);

        return operations.OrderBy(o => o).ToArray();
    }

    public static IEnumerable<GISTag> GetFlags(this GISTag tags)
    {
        foreach (GISTag value in Enum.GetValues(typeof(GISTag)))
            if (value != GISTag.None && tags.HasFlag(value))
                yield return value;
    }

    public static bool IsFormatTag(this GISTag tag)
    {
        return tag == GISTag.Shapefile ||
               tag == GISTag.GeoJSON ||
               tag == GISTag.KML ||
               tag == GISTag.KMZ ||
               tag == GISTag.GeoTIFF ||
               tag == GISTag.GeoPackage ||
               tag == GISTag.FileGDB;
    }

    public static bool IsGeometryTypeTag(this GISTag tag)
    {
        return tag == GISTag.VectorData ||
               tag == GISTag.RasterData ||
               tag == GISTag.PointCloud ||
               tag == GISTag.TIN;
    }

    public static bool IsAnalysisTag(this GISTag tag)
    {
        return tag == GISTag.DEM ||
               tag == GISTag.DSM ||
               tag == GISTag.DTM ||
               tag == GISTag.Slope ||
               tag == GISTag.Aspect ||
               tag == GISTag.Hillshade ||
               tag == GISTag.Contours ||
               tag == GISTag.Watershed ||
               tag == GISTag.FlowDirection;
    }

    public static bool IsSourceTag(this GISTag tag)
    {
        return tag == GISTag.Survey ||
               tag == GISTag.RemoteSensing ||
               tag == GISTag.Generated ||
               tag == GISTag.Imported ||
               tag == GISTag.OpenData ||
               tag == GISTag.Commercial ||
               tag == GISTag.FieldData ||
               tag == GISTag.LiDAR ||
               tag == GISTag.UAV ||
               tag == GISTag.GPS;
    }

    public static bool RequiresGeoreference(this GISTag tag)
    {
        return tag.HasFlag(GISTag.Topography) ||
               tag.HasFlag(GISTag.Satellite) ||
               tag.HasFlag(GISTag.Aerial) ||
               tag.HasFlag(GISTag.Cadastral) ||
               tag.HasFlag(GISTag.GeologicalMap);
    }

    public static bool SupportsTerrainAnalysis(this GISTag tag)
    {
        return tag.HasFlag(GISTag.DEM) ||
               tag.HasFlag(GISTag.DSM) ||
               tag.HasFlag(GISTag.DTM) ||
               tag.HasFlag(GISTag.Topography) ||
               tag.HasFlag(GISTag.Bathymetry);
    }

    public static bool SupportsAttributeData(this GISTag tag)
    {
        return tag.HasFlag(GISTag.VectorData) ||
               tag.HasFlag(GISTag.Shapefile) ||
               tag.HasFlag(GISTag.GeoJSON) ||
               tag.HasFlag(GISTag.Attributed);
    }

    public static string GetColorScheme(this GISTag tag)
    {
        // Suggest appropriate color schemes for different data types
        if (tag.HasFlag(GISTag.DEM) || tag.HasFlag(GISTag.Topography))
            return "Elevation (Brown-Green-White)";

        if (tag.HasFlag(GISTag.Slope))
            return "Slope (Yellow-Orange-Red)";

        if (tag.HasFlag(GISTag.Bathymetry))
            return "Depth (Light Blue-Dark Blue)";

        if (tag.HasFlag(GISTag.GeologicalMap))
            return "Geological Age/Lithology";

        if (tag.HasFlag(GISTag.Vegetation))
            return "Vegetation Index (Brown-Yellow-Green)";

        if (tag.HasFlag(GISTag.Satellite) || tag.HasFlag(GISTag.Multispectral))
            return "False Color Composite";

        return "Default";
    }

    public static GISTag[] GetRecommendedTags(string filePath, LayerType layerType)
    {
        var tags = new List<GISTag>();
        var extension = Path.GetExtension(filePath).ToLower();

        // Format tags based on file extension
        switch (extension)
        {
            case ".shp":
                tags.Add(GISTag.Shapefile);
                tags.Add(GISTag.VectorData);
                break;
            case ".geojson":
            case ".json":
                tags.Add(GISTag.GeoJSON);
                tags.Add(GISTag.VectorData);
                break;
            case ".kml":
                tags.Add(GISTag.KML);
                tags.Add(GISTag.VectorData);
                break;
            case ".kmz":
                tags.Add(GISTag.KMZ);
                tags.Add(GISTag.VectorData);
                break;
            case ".tif":
            case ".tiff":
                tags.Add(GISTag.GeoTIFF);
                tags.Add(GISTag.RasterData);
                break;
        }

        // Geometry type tags
        if (layerType == LayerType.Vector)
            tags.Add(GISTag.VectorData);
        else if (layerType == LayerType.Raster)
            tags.Add(GISTag.RasterData);

        // Check filename for common keywords
        var filename = Path.GetFileNameWithoutExtension(filePath).ToLower();

        if (filename.Contains("dem") || filename.Contains("elevation"))
            tags.Add(GISTag.DEM);

        if (filename.Contains("dsm") || filename.Contains("surface"))
            tags.Add(GISTag.DSM);

        if (filename.Contains("dtm") || filename.Contains("terrain"))
            tags.Add(GISTag.DTM);

        if (filename.Contains("slope"))
            tags.Add(GISTag.Slope);

        if (filename.Contains("aspect"))
            tags.Add(GISTag.Aspect);

        if (filename.Contains("hillshade"))
            tags.Add(GISTag.Hillshade);

        if (filename.Contains("contour"))
            tags.Add(GISTag.Contours);

        if (filename.Contains("geol") || filename.Contains("lithology"))
            tags.Add(GISTag.GeologicalMap);

        if (filename.Contains("cadastr") || filename.Contains("parcel"))
            tags.Add(GISTag.Cadastral);

        if (filename.Contains("satellite") || filename.Contains("landsat") || filename.Contains("sentinel"))
            tags.Add(GISTag.Satellite);

        if (filename.Contains("lidar"))
            tags.Add(GISTag.LiDAR);

        if (filename.Contains("uav") || filename.Contains("drone"))
            tags.Add(GISTag.UAV);

        if (filename.Contains("topo") || filename.Contains("elevation"))
            tags.Add(GISTag.Topography);

        if (filename.Contains("hydro") || filename.Contains("water") || filename.Contains("stream"))
            tags.Add(GISTag.Hydrography);

        if (filename.Contains("admin") || filename.Contains("boundary") || filename.Contains("border"))
            tags.Add(GISTag.Administrative);

        return tags.ToArray();
    }
}