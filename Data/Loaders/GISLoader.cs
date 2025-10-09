// GeoscientistToolkit/Data/Loaders/GISLoader.cs (Updated)

using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class GISLoader : IDataLoader
{
    public enum GISFileType
    {
        AutoDetect,
        Shapefile,
        GeoJSON,
        KML,
        KMZ,
        GeoTIFF
    }

    public string FilePath { get; set; }
    public GISFileType FileType { get; set; } = GISFileType.AutoDetect;
    public bool CreateEmpty { get; set; }
    public string DatasetName { get; set; }

    // Auto-tagging options
    public bool EnableAutoTagging { get; set; } = true;
    public bool ScanFileContents { get; set; } = true;

    public string Name => "GIS Map Loader";

    public string Description =>
        "Load GIS data from shapefiles, GeoJSON, KML, or GeoTIFF files, or create an empty map with automatic tag detection";

    public bool CanImport => CreateEmpty || (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath));
    public string ValidationMessage { get; private set; }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progress)
    {
        progress?.Report((0, "Starting GIS import..."));

        if (CreateEmpty)
        {
            progress?.Report((0.5f, "Creating empty GIS dataset..."));

            var dataset = new GISDataset(DatasetName ?? "New Map", "");
            dataset.AddTag(GISTag.Editable);

            progress?.Report((1.0f, "Empty GIS dataset created"));
            return dataset;
        }

        if (!File.Exists(FilePath))
            throw new FileNotFoundException($"GIS file not found: {FilePath}");

        var name = string.IsNullOrEmpty(DatasetName)
            ? Path.GetFileNameWithoutExtension(FilePath)
            : DatasetName;

        var gisDataset = new GISDataset(name, FilePath);

        progress?.Report((0.2f, "Loading GIS data..."));

        await Task.Run(() =>
        {
            try
            {
                // Load the dataset
                gisDataset.Load();
                progress?.Report((0.7f, "Analyzing dataset..."));

                // Auto-tag if enabled
                if (EnableAutoTagging)
                {
                    ApplyAutomaticTags(gisDataset, ScanFileContents);
                    progress?.Report((0.9f, "Tags applied"));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load GIS file: {ex.Message}");
                throw;
            }
        });

        progress?.Report((1.0f, $"GIS dataset loaded with {gisDataset.Tags.GetFlags().Count()} tags"));
        return gisDataset;
    }

    public void Reset()
    {
        FilePath = null;
        FileType = GISFileType.AutoDetect;
        CreateEmpty = false;
        DatasetName = null;
        EnableAutoTagging = true;
        ScanFileContents = true;
    }

    public GISFileInfo GetFileInfo()
    {
        var info = new GISFileInfo();

        if (CreateEmpty)
        {
            info.IsValid = true;
            info.Type = "Empty";
            return info;
        }

        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            return info;

        var fileInfo = new FileInfo(FilePath);
        info.FileName = fileInfo.Name;
        info.FileSize = fileInfo.Length;

        var ext = fileInfo.Extension.ToLower();
        info.Type = ext switch
        {
            ".shp" => "Shapefile",
            ".geojson" or ".json" => "GeoJSON",
            ".kml" => "KML",
            ".kmz" => "KMZ (Compressed KML)",
            ".tif" or ".tiff" => "GeoTIFF",
            _ => "Unknown"
        };

        info.IsValid = info.Type != "Unknown";

        // Check for shapefile components
        if (ext == ".shp")
        {
            var basePath = Path.GetFileNameWithoutExtension(FilePath);
            var dir = Path.GetDirectoryName(FilePath);

            info.HasShx = File.Exists(Path.Combine(dir, basePath + ".shx"));
            info.HasDbf = File.Exists(Path.Combine(dir, basePath + ".dbf"));
            info.HasPrj = File.Exists(Path.Combine(dir, basePath + ".prj"));

            if (!info.HasShx || !info.HasDbf)
            {
                info.IsValid = false;
                ValidationMessage = "Missing required shapefile components (.shx, .dbf)";
            }
            else
            {
                ValidationMessage = null;
            }
        }

        // Predict tags
        if (EnableAutoTagging) info.PredictedTags = PredictTags(FilePath, info);

        return info;
    }

    /// <summary>
    ///     Apply automatic tags based on file properties and content
    /// </summary>
    private void ApplyAutomaticTags(GISDataset dataset, bool scanContents)
    {
        Logger.Log("Applying automatic tags...");

        // 1. Basic tags already applied during Load()
        // These include format tags (Shapefile, GeoJSON, etc.) and geometry type (Vector/Raster)

        // 2. Filename-based tags
        ApplyFilenameTags(dataset);

        // 3. Content-based tags
        if (scanContents) ApplyContentTags(dataset);

        // 4. Property-based tags
        ApplyPropertyTags(dataset);

        // 5. Metadata-based tags
        ApplyMetadataTags(dataset);

        var tagCount = dataset.Tags.GetFlags().Count();
        Logger.Log($"Applied {tagCount} automatic tags");
    }

    /// <summary>
    ///     Apply tags based on filename analysis
    /// </summary>
    private void ApplyFilenameTags(GISDataset dataset)
    {
        if (string.IsNullOrEmpty(dataset.FilePath))
            return;

        var filename = Path.GetFileNameWithoutExtension(dataset.FilePath).ToLower();

        // Elevation/Terrain keywords
        if (ContainsAny(filename, "dem", "elevation", "elev", "dtm", "dsm"))
        {
            if (filename.Contains("dem"))
                dataset.AddTag(GISTag.DEM);
            else if (filename.Contains("dsm"))
                dataset.AddTag(GISTag.DSM);
            else if (filename.Contains("dtm"))
                dataset.AddTag(GISTag.DTM);

            dataset.AddTag(GISTag.Topography);
        }

        // Derived products
        if (ContainsAny(filename, "slope"))
            dataset.AddTag(GISTag.Slope);

        if (ContainsAny(filename, "aspect"))
            dataset.AddTag(GISTag.Aspect);

        if (ContainsAny(filename, "hillshade", "shaded", "relief"))
            dataset.AddTag(GISTag.Hillshade);

        if (ContainsAny(filename, "contour", "isolines"))
            dataset.AddTag(GISTag.Contours);

        // Geological
        if (ContainsAny(filename, "geol", "geology", "lithology", "bedrock"))
        {
            dataset.AddTag(GISTag.Geological);
            dataset.AddTag(GISTag.GeologicalMap);
        }

        if (ContainsAny(filename, "struct", "fold", "fault", "lineament"))
            dataset.AddTag(GISTag.StructuralData);

        if (ContainsAny(filename, "seismic", "earthquake", "quake"))
            dataset.AddTag(GISTag.Seismic);

        if (ContainsAny(filename, "gravity", "magnetic", "geophys"))
            dataset.AddTag(GISTag.Geophysical);

        // Administrative/Cadastral
        if (ContainsAny(filename, "cadastr", "parcel", "property", "land_register"))
        {
            dataset.AddTag(GISTag.Cadastral);
            dataset.AddTag(GISTag.LandRegister);
        }

        if (ContainsAny(filename, "admin", "boundary", "border", "district", "municipality"))
            dataset.AddTag(GISTag.Administrative);

        if (ContainsAny(filename, "road", "street", "railway", "infrastructure"))
            dataset.AddTag(GISTag.Infrastructure);

        // Hydrography
        if (ContainsAny(filename, "hydro", "water", "stream", "river", "lake", "watershed", "basin"))
        {
            dataset.AddTag(GISTag.Hydrography);

            if (filename.Contains("watershed") || filename.Contains("basin"))
                dataset.AddTag(GISTag.Watershed);

            if (filename.Contains("flowdir") || filename.Contains("flow_direction"))
                dataset.AddTag(GISTag.FlowDirection);
        }

        if (ContainsAny(filename, "bathymetry", "depth", "bathym"))
            dataset.AddTag(GISTag.Bathymetry);

        // Land cover/use
        if (ContainsAny(filename, "landcover", "land_cover", "lulc", "landuse", "land_use"))
            dataset.AddTag(GISTag.LandUse);

        if (ContainsAny(filename, "vegetation", "ndvi", "forest", "veg"))
            dataset.AddTag(GISTag.Vegetation);

        // Remote sensing
        if (ContainsAny(filename, "satellite", "landsat", "sentinel", "modis", "spot"))
        {
            dataset.AddTag(GISTag.Satellite);
            dataset.AddTag(GISTag.RemoteSensing);
        }

        if (ContainsAny(filename, "aerial", "ortho", "orthophoto"))
        {
            dataset.AddTag(GISTag.Aerial);
            dataset.AddTag(GISTag.RemoteSensing);
        }

        if (ContainsAny(filename, "lidar", "las", "laz", "pointcloud"))
            dataset.AddTag(GISTag.LiDAR);

        if (ContainsAny(filename, "uav", "drone", "rpas", "uas"))
            dataset.AddTag(GISTag.UAV);

        // Basemap indicators
        if (ContainsAny(filename, "basemap", "background", "base_map"))
            dataset.AddTag(GISTag.Basemap);

        // Multispectral
        if (ContainsAny(filename, "multispectral", "multi_spectral", "bands"))
            dataset.AddTag(GISTag.Multispectral);
    }

    /// <summary>
    ///     Apply tags based on dataset content analysis
    /// </summary>
    private void ApplyContentTags(GISDataset dataset)
    {
        // Check for attributes
        var hasAttributes = dataset.Layers
            .Any(l => l.Features.Any(f => f.Properties != null && f.Properties.Count > 0));

        if (hasAttributes)
            dataset.AddTag(GISTag.Attributed);

        // Check for specific attribute patterns
        foreach (var layer in dataset.Layers)
        {
            var attributeNames = layer.Features
                .SelectMany(f => f.Properties.Keys)
                .Distinct()
                .Select(k => k.ToLower())
                .ToList();

            // Geological attributes
            if (attributeNames.Any(a => a.Contains("lithology") || a.Contains("rocktype") ||
                                        a.Contains("formation") || a.Contains("age")))
                dataset.AddTag(GISTag.GeologicalMap);

            // Structural geology attributes
            if (attributeNames.Any(a => a.Contains("dip") || a.Contains("strike") ||
                                        a.Contains("azimuth") || a.Contains("plunge")))
                dataset.AddTag(GISTag.StructuralData);

            // Administrative attributes
            if (attributeNames.Any(a => a.Contains("admin") || a.Contains("district") ||
                                        a.Contains("municipality") || a.Contains("county")))
                dataset.AddTag(GISTag.Administrative);

            // Cadastral attributes
            if (attributeNames.Any(a => a.Contains("parcel") || a.Contains("owner") ||
                                        a.Contains("cadastr") || a.Contains("lot")))
                dataset.AddTag(GISTag.Cadastral);

            // Vegetation/NDVI attributes
            if (attributeNames.Any(a => a.Contains("ndvi") || a.Contains("vegetation") ||
                                        a.Contains("biomass")))
                dataset.AddTag(GISTag.Vegetation);

            // Land use attributes
            if (attributeNames.Any(a => a.Contains("landuse") || a.Contains("land_use") ||
                                        a.Contains("lulc") || a.Contains("class")))
                dataset.AddTag(GISTag.LandUse);
        }

        // Check feature count and complexity
        var totalFeatures = dataset.Layers.Sum(l => l.Features.Count);
        if (totalFeatures > 10000) Logger.Log($"Large dataset detected: {totalFeatures} features");

        // Check for 3D data
        var has3D = dataset.Layers.Any(l => l.Features.Any(f =>
            f.Properties.ContainsKey("elevation") ||
            f.Properties.ContainsKey("height") ||
            f.Properties.ContainsKey("z")));

        if (has3D)
            dataset.AddTag(GISTag.ThreeDimensional);

        // Check for time series data
        var hasTime = dataset.Layers.Any(l => l.Features.Any(f =>
            f.Properties.ContainsKey("date") ||
            f.Properties.ContainsKey("time") ||
            f.Properties.ContainsKey("timestamp")));

        if (hasTime)
            dataset.AddTag(GISTag.TimeSeries);
    }

    /// <summary>
    ///     Apply tags based on dataset properties
    /// </summary>
    private void ApplyPropertyTags(GISDataset dataset)
    {
        // Multi-layer check
        if (dataset.Layers.Count > 1)
            dataset.AddTag(GISTag.MultiLayer);

        // Editability
        if (dataset.IsEditable && dataset.Layers.Any(l => l.IsEditable))
            dataset.AddTag(GISTag.Editable);

        // Projection tags
        if (!string.IsNullOrEmpty(dataset.Projection.EPSG))
        {
            dataset.AddTag(GISTag.Georeferenced);

            if (dataset.Projection.EPSG != "EPSG:4326")
                dataset.AddTag(GISTag.Projected);
        }

        // Import status
        if (!string.IsNullOrEmpty(dataset.FilePath))
            dataset.AddTag(GISTag.Imported);
    }

    /// <summary>
    ///     Apply tags based on metadata
    /// </summary>
    private void ApplyMetadataTags(GISDataset dataset)
    {
        // Check GISMetadata for additional hints
        foreach (var kvp in dataset.GISMetadata)
        {
            var key = kvp.Key.ToLower();
            var value = kvp.Value?.ToString()?.ToLower() ?? "";

            if (key.Contains("source") && value.Contains("survey"))
                dataset.AddTag(GISTag.Survey);

            if (key.Contains("source") && value.Contains("field"))
                dataset.AddTag(GISTag.FieldData);

            if (key.Contains("license") && (value.Contains("open") || value.Contains("public")))
                dataset.AddTag(GISTag.OpenData);

            if (key.Contains("license") && (value.Contains("commercial") || value.Contains("proprietary")))
                dataset.AddTag(GISTag.Commercial);
        }
    }

    /// <summary>
    ///     Predict tags before loading (for preview)
    /// </summary>
    private List<GISTag> PredictTags(string filePath, GISFileInfo fileInfo)
    {
        var tags = new List<GISTag>();

        // Add format tag
        switch (fileInfo.Type)
        {
            case "Shapefile":
                tags.Add(GISTag.Shapefile);
                tags.Add(GISTag.VectorData);
                break;
            case "GeoJSON":
                tags.Add(GISTag.GeoJSON);
                tags.Add(GISTag.VectorData);
                break;
            case "KML":
                tags.Add(GISTag.KML);
                tags.Add(GISTag.VectorData);
                break;
            case "KMZ (Compressed KML)":
                tags.Add(GISTag.KMZ);
                tags.Add(GISTag.VectorData);
                break;
            case "GeoTIFF":
                tags.Add(GISTag.GeoTIFF);
                tags.Add(GISTag.RasterData);
                break;
        }

        // Add filename-based predictions
        var recommendedTags = GISTagExtensions.GetRecommendedTags(filePath, LayerType.Vector);
        tags.AddRange(recommendedTags.Where(t => !tags.Contains(t)));

        return tags;
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public class GISFileInfo
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string Type { get; set; }
        public bool IsValid { get; set; }

        // Shapefile specific
        public bool HasShx { get; set; }
        public bool HasDbf { get; set; }
        public bool HasPrj { get; set; }

        // Predicted tags
        public List<GISTag> PredictedTags { get; set; } = new();

        public string GetSizeFormatted()
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            var order = 0;
            double size = FileSize;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }
    }
}