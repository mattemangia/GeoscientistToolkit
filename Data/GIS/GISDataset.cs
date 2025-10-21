// GeoscientistToolkit/Data/GIS/GISDataset.cs (Updated)

using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.UI.GIS;
using GeoscientistToolkit.Util;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ProjNet.CoordinateSystems;


// Required for GISOperationsImpl
// Required for BasemapManager

namespace GeoscientistToolkit.Data.GIS;

public class GISDataset : Dataset, ISerializableDataset
{
    // Geometry factory for NetTopologySuite
    private static readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326);

    public GISDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.GIS;

        // Initialize with a default vector layer
        var defaultLayer = new GISLayer
        {
            Name = "Default Layer",
            Type = LayerType.Vector,
            IsVisible = true,
            IsEditable = true,
            Color = new Vector4(0.2f, 0.5f, 1.0f, 1.0f)
        };
        Layers.Add(defaultLayer);
    }

    // Map properties
    public List<GISLayer> Layers { get; set; } = new();
    public GISProjection Projection { get; set; } = new();
    public BoundingBox Bounds { get; set; } = new();
    public Vector2 Center { get; set; }
    public float DefaultZoom { get; set; } = 1.0f;

    // Basemap
    public BasemapType BasemapType { get; set; } = BasemapType.None;
    public string BasemapPath { get; set; }
    public string ActiveBasemapLayerName { get; set; }


    // Edit state
    public bool IsEditable { get; set; } = true;

    // Tag System
    public GISTag Tags { get; set; } = GISTag.None;
    public Dictionary<string, object> GISMetadata { get; set; } = new();


    public object ToSerializableObject()
    {
        return new GISDatasetDTO
        {
            TypeName = nameof(GISDataset),
            Name = Name,
            FilePath = FilePath,
            Layers = Layers.Select(l => new GISLayerDTO
            {
                Name = l.Name,
                Type = l.Type.ToString(),
                IsVisible = l.IsVisible,
                IsEditable = l.IsEditable,
                Color = l.Color,
                Features = l.Features.Select(f =>
                {
                    var dto = new GISFeatureDTO
                    {
                        Type = f.Type,
                        Coordinates = new List<Vector2>(f.Coordinates),
                        Properties = new Dictionary<string, object>(f.Properties),
                        Id = f.Id
                    };

                    if (f is GeologicalMapping.GeologicalFeature geoFeature)
                    {
                        dto.GeologicalType = geoFeature.GeologicalType;
                        dto.Strike = geoFeature.Strike;
                        dto.Dip = geoFeature.Dip;
                        dto.DipDirection = geoFeature.DipDirection;
                        dto.Plunge = geoFeature.Plunge;
                        dto.Trend = geoFeature.Trend;
                        dto.FormationName = geoFeature.FormationName;
                        dto.BoreholeName = geoFeature.BoreholeName;
                        dto.LithologyCode = geoFeature.LithologyCode;
                        dto.AgeCode = geoFeature.AgeCode;
                        dto.Description = geoFeature.Description;
                        dto.Thickness = geoFeature.Thickness;
                        dto.Displacement = geoFeature.Displacement;
                        dto.MovementSense = geoFeature.MovementSense;
                        dto.IsInferred = geoFeature.IsInferred;
                        dto.IsCovered = geoFeature.IsCovered;
                    }

                    return dto;
                }).ToList()
            }).ToList(),
            BasemapType = BasemapType.ToString(),
            BasemapPath = BasemapPath,
            ActiveBasemapLayerName = ActiveBasemapLayerName,
            Center = Center,
            DefaultZoom = DefaultZoom,
            Tags = (long)Tags,
            GISMetadata = new Dictionary<string, string>(
                GISMetadata.Select(kvp => new KeyValuePair<string, string>(
                    kvp.Key, kvp.Value?.ToString() ?? "")))
        };
    }

    // --- NEW METHOD: CloneWithFeatures ---
    /// <summary>
    ///     Creates a clone of the dataset's structure but with a new set of features.
    /// </summary>
    public GISDataset CloneWithFeatures(List<GISFeature> features, string nameSuffix)
    {
        var newName = $"{Name}{nameSuffix}";
        var newDataset = new GISDataset(newName, "")
        {
            Projection = Projection,
            Tags = Tags | GISTag.Generated
        };
        newDataset.Layers.Clear(); // Remove the default layer

        var newLayer = new GISLayer
        {
            Name = newName,
            Type = LayerType.Vector,
            Features = features
        };
        newDataset.Layers.Add(newLayer);
        newDataset.UpdateBounds();
        return newDataset;
    }

    // Tag Management Methods
    public void AddTag(GISTag tag)
    {
        Tags |= tag;
        Logger.Log($"Added tag '{tag.GetDisplayName()}' to {Name}");
    }

    public void RemoveTag(GISTag tag)
    {
        Tags &= ~tag;
        Logger.Log($"Removed tag '{tag.GetDisplayName()}' from {Name}");
    }

    public bool HasTag(GISTag tag)
    {
        return Tags.HasFlag(tag);
    }

    public void ClearTags()
    {
        Tags = GISTag.None;
    }

    public void SetGeoreference(string epsg, string projectionName)
    {
        Projection.EPSG = epsg;
        Projection.Name = projectionName;
        AddTag(GISTag.Georeferenced);

        if (!string.IsNullOrEmpty(epsg) && epsg != "EPSG:4326")
            AddTag(GISTag.Projected);
    }

    public string[] GetAvailableOperations()
    {
        return Tags.GetAvailableOperations();
    }

    public override long GetSizeInBytes()
    {
        if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
            return new FileInfo(FilePath).Length;

        // Estimate size for in-memory data
        long size = 0;
        foreach (var layer in Layers)
            size += layer.Features.Count * 100; // Rough estimate per feature
        return size;
    }

    public override void Load()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            // New empty dataset
            AddTag(GISTag.Editable);
            Logger.Log($"Created new GIS dataset: {Name}");
            return;
        }

        if (!File.Exists(FilePath))
        {
            Logger.LogError($"GIS file not found: {FilePath}");
            IsMissing = true;
            return;
        }

        try
        {
            var extension = Path.GetExtension(FilePath).ToLower();

            // Auto-detect and assign format tags
            switch (extension)
            {
                case ".shp":
                    LoadShapefile();
                    AddTag(GISTag.Shapefile);
                    AddTag(GISTag.VectorData);
                    break;
                case ".geojson":
                case ".json":
                    LoadGeoJSON();
                    AddTag(GISTag.GeoJSON);
                    AddTag(GISTag.VectorData);
                    break;
                case ".kmz":
                    LoadKMZ();
                    AddTag(GISTag.KMZ);
                    AddTag(GISTag.VectorData);
                    break;
                case ".kml":
                    LoadKML();
                    AddTag(GISTag.KML);
                    AddTag(GISTag.VectorData);
                    break;
                case ".tif":
                case ".tiff":
                    LoadGeoTIFF();
                    AddTag(GISTag.GeoTIFF);
                    AddTag(GISTag.RasterData);
                    break;
                default:
                    throw new NotSupportedException($"File format '{extension}' is not supported for GIS datasets.");
            }

            // Auto-detect recommended tags based on filename and content
            var recommendedTags = GISTagExtensions.GetRecommendedTags(
                FilePath,
                Layers.FirstOrDefault()?.Type ?? LayerType.Vector);

            foreach (var tag in recommendedTags)
                if (!HasTag(tag))
                    AddTag(tag);

            // Check for attributes
            if (Layers.Any(l => l.Features.Any(f => f.Properties.Count > 0)))
                AddTag(GISTag.Attributed);

            // Check for multi-layer
            if (Layers.Count > 1)
                AddTag(GISTag.MultiLayer);

            // Mark as imported
            AddTag(GISTag.Imported);

            UpdateBounds();
            Logger.Log(
                $"Loaded GIS dataset: {Name} with {Layers.Count} layers and tags: {string.Join(", ", Tags.GetFlags().Select(t => t.GetDisplayName()))}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load GIS dataset '{Name}': {ex.Message}");
            throw;
        }
    }

    // --- Load methods are unchanged ---
    private void LoadShapefile()
    {
        Logger.Log($"Loading shapefile: {FilePath}");

        var layer = new GISLayer
        {
            Name = Path.GetFileNameWithoutExtension(FilePath),
            Type = LayerType.Vector,
            IsVisible = true,
            Color = new Vector4(0.2f, 0.5f, 1.0f, 1.0f)
        };

        try
        {
            // Use NetTopologySuite's ShapefileDataReader
            var shpReader = new ShapefileDataReader(FilePath, _geometryFactory);
            var header = shpReader.DbaseHeader;

            // Read projection if .prj file exists
            var prjPath = Path.ChangeExtension(FilePath, ".prj");
            if (File.Exists(prjPath))
            {
                var wkt = File.ReadAllText(prjPath);
                var csFactory = new CoordinateSystemFactory();
                try
                {
                    var cs = csFactory.CreateFromWkt(wkt);
                    Projection.Name = cs.Name;
                    Projection.EPSG = cs.AuthorityCode > 0 ? $"EPSG:{cs.AuthorityCode}" : "Custom";
                    AddTag(GISTag.Georeferenced);

                    if (Projection.EPSG != "EPSG:4326")
                        AddTag(GISTag.Projected);
                }
                catch
                {
                    Logger.LogWarning("Could not parse projection file");
                }
            }

            // Read features
            while (shpReader.Read())
            {
                var geometry = shpReader.Geometry;
                var attributes = new Dictionary<string, object>();

                // Read attributes
                for (var i = 0; i < header.NumFields; i++)
                {
                    var fieldName = header.Fields[i].Name;
                    var value = shpReader.GetValue(i);
                    if (value != null) attributes[fieldName] = value;
                }

                // Convert NTS geometry to our GISFeature
                var feature = ConvertNTSGeometry(geometry, attributes);
                if (feature != null) layer.Features.Add(feature);
            }

            shpReader.Dispose();

            Layers.Clear();
            Layers.Add(layer);

            Logger.Log($"Loaded {layer.Features.Count} features from shapefile");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading shapefile: {ex.Message}");
            throw;
        }
    }

    private void LoadGeoJSON()
    {
        Logger.Log($"Loading GeoJSON: {FilePath}");

        var json = File.ReadAllText(FilePath);
        var layer = new GISLayer
        {
            Name = Path.GetFileNameWithoutExtension(FilePath),
            Type = LayerType.Vector,
            IsVisible = true,
            Color = new Vector4(0.2f, 0.8f, 0.2f, 1.0f)
        };

        try
        {
            // Parse GeoJSON using System.Text.Json
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeElement))
            {
                var geoJsonType = typeElement.GetString();

                if (geoJsonType == "FeatureCollection" && root.TryGetProperty("features", out var features))
                {
                    foreach (var featureElement in features.EnumerateArray())
                    {
                        var feature = ParseGeoJsonFeature(featureElement);
                        if (feature != null) layer.Features.Add(feature);
                    }
                }
                else if (geoJsonType == "Feature")
                {
                    var feature = ParseGeoJsonFeature(root);
                    if (feature != null) layer.Features.Add(feature);
                }
            }

            Layers.Clear();
            Layers.Add(layer);

            Logger.Log($"Loaded {layer.Features.Count} features from GeoJSON");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading GeoJSON: {ex.Message}");
            throw;
        }
    }

    private GISFeature ParseGeoJsonFeature(JsonElement featureElement)
    {
        try
        {
            if (!featureElement.TryGetProperty("geometry", out var geometry))
                return null;

            if (!geometry.TryGetProperty("type", out var geomType))
                return null;

            var geometryType = geomType.GetString();
            var coordinates = new List<Vector2>();

            if (geometry.TryGetProperty("coordinates", out var coordsElement))
                switch (geometryType)
                {
                    case "Point":
                        var coords = ParseCoordinate(coordsElement);
                        if (coords.HasValue)
                            coordinates.Add(coords.Value);
                        break;

                    case "LineString":
                        coordinates = ParseCoordinateList(coordsElement);
                        break;

                    case "Polygon":
                        // Take first ring (exterior)
                        if (coordsElement.GetArrayLength() > 0)
                            coordinates = ParseCoordinateList(coordsElement[0]);
                        break;

                    case "MultiPoint":
                        coordinates = ParseCoordinateList(coordsElement);
                        break;
                }

            // Parse properties
            var properties = new Dictionary<string, object>();
            if (featureElement.TryGetProperty("properties", out var propsElement))
                foreach (var prop in propsElement.EnumerateObject())
                    properties[prop.Name] = ParseJsonValue(prop.Value);

            var featureType = geometryType switch
            {
                "Point" => FeatureType.Point,
                "LineString" => FeatureType.Line,
                "Polygon" => FeatureType.Polygon,
                "MultiPoint" => FeatureType.MultiPoint,
                "MultiLineString" => FeatureType.MultiLine,
                "MultiPolygon" => FeatureType.MultiPolygon,
                _ => FeatureType.Point
            };

            return new GISFeature
            {
                Type = featureType,
                Coordinates = coordinates,
                Properties = properties
            };
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to parse GeoJSON feature: {ex.Message}");
            return null;
        }
    }

    private Vector2? ParseCoordinate(JsonElement element)
    {
        try
        {
            if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() >= 2)
            {
                var lon = (float)element[0].GetDouble();
                var lat = (float)element[1].GetDouble();
                return new Vector2(lon, lat);
            }
        }
        catch
        {
        }

        return null;
    }

    private List<Vector2> ParseCoordinateList(JsonElement element)
    {
        var coords = new List<Vector2>();
        foreach (var coord in element.EnumerateArray())
        {
            var parsed = ParseCoordinate(coord);
            if (parsed.HasValue)
                coords.Add(parsed.Value);
        }

        return coords;
    }

    private object ParseJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private void LoadKML()
    {
        Logger.Log($"Loading KML: {FilePath}");
        LoadKMLFromStream(File.OpenRead(FilePath));
    }

    private void LoadKMZ()
    {
        Logger.Log($"Loading KMZ: {FilePath}");

        using var archive = ZipFile.OpenRead(FilePath);
        var kmlEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".kml", StringComparison.OrdinalIgnoreCase));

        if (kmlEntry == null) throw new InvalidOperationException("No KML file found in KMZ archive");

        using var stream = kmlEntry.Open();
        LoadKMLFromStream(stream);
    }

    private void LoadKMLFromStream(Stream stream)
    {
        var layer = new GISLayer
        {
            Name = Path.GetFileNameWithoutExtension(FilePath),
            Type = LayerType.Vector,
            IsVisible = true,
            Color = new Vector4(0.8f, 0.2f, 0.2f, 1.0f)
        };

        try
        {
            var doc = XDocument.Load(stream);
            XNamespace kml = "http://www.opengis.net/kml/2.2";

            // Find all Placemarks
            var placemarks = doc.Descendants(kml + "Placemark");

            foreach (var placemark in placemarks)
            {
                var feature = ParseKMLPlacemark(placemark, kml);
                if (feature != null) layer.Features.Add(feature);
            }

            Layers.Clear();
            Layers.Add(layer);

            Logger.Log($"Loaded {layer.Features.Count} features from KML/KMZ");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error loading KML: {ex.Message}");
            throw;
        }
    }

    private GISFeature ParseKMLPlacemark(XElement placemark, XNamespace kml)
    {
        try
        {
            var feature = new GISFeature();
            var properties = new Dictionary<string, object>();

            // Get name
            var name = placemark.Element(kml + "name")?.Value;
            if (!string.IsNullOrEmpty(name))
                properties["name"] = name;

            // Get description
            var description = placemark.Element(kml + "description")?.Value;
            if (!string.IsNullOrEmpty(description))
                properties["description"] = description;

            // Parse geometry
            var point = placemark.Element(kml + "Point");
            if (point != null)
            {
                feature.Type = FeatureType.Point;
                var coordsText = point.Element(kml + "coordinates")?.Value;
                if (!string.IsNullOrEmpty(coordsText))
                {
                    var coords = ParseKMLCoordinates(coordsText);
                    if (coords.Count > 0)
                        feature.Coordinates.Add(coords[0]);
                }
            }

            var lineString = placemark.Element(kml + "LineString");
            if (lineString != null)
            {
                feature.Type = FeatureType.Line;
                var coordsText = lineString.Element(kml + "coordinates")?.Value;
                if (!string.IsNullOrEmpty(coordsText))
                    feature.Coordinates = ParseKMLCoordinates(coordsText);
            }

            var polygon = placemark.Element(kml + "Polygon");
            if (polygon != null)
            {
                feature.Type = FeatureType.Polygon;
                var outerBoundary = polygon.Element(kml + "outerBoundaryIs");
                if (outerBoundary != null)
                {
                    var linearRing = outerBoundary.Element(kml + "LinearRing");
                    if (linearRing != null)
                    {
                        var coordsText = linearRing.Element(kml + "coordinates")?.Value;
                        if (!string.IsNullOrEmpty(coordsText))
                            feature.Coordinates = ParseKMLCoordinates(coordsText);
                    }
                }
            }

            feature.Properties = properties;
            return feature.Coordinates.Count > 0 ? feature : null;
        }
        catch
        {
            return null;
        }
    }

    private List<Vector2> ParseKMLCoordinates(string coordsText)
    {
        var coords = new List<Vector2>();
        var parts = coordsText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var values = part.Split(',');
            if (values.Length >= 2)
                if (float.TryParse(values[0], out var lon) && float.TryParse(values[1], out var lat))
                    coords.Add(new Vector2(lon, lat));
        }

        return coords;
    }

    // --- MODIFIED: LoadGeoTIFF now loads pixel data into a GISRasterLayer ---
    private void LoadGeoTIFF()
    {
        Logger.Log($"Loading GeoTIFF: {FilePath}");

        var geoTiffData = BasemapManager.Instance.LoadGeoTiff(FilePath);
        if (geoTiffData == null)
            throw new InvalidOperationException("Failed to load GeoTIFF data using BasemapManager.");

        // Convert byte[] RGBA to float[,] for the first band (typical for DEM)
        var width = geoTiffData.Width;
        var height = geoTiffData.Height;
        var pixelData = new float[width, height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = (y * width + x) * 4;
            pixelData[x, y] = geoTiffData.Data[index]; // Using Red channel as grayscale value
        }

        var bounds = new BoundingBox
        {
            Min = new Vector2((float)geoTiffData.OriginX,
                (float)(geoTiffData.OriginY + geoTiffData.PixelHeight * height)),
            Max = new Vector2((float)(geoTiffData.OriginX + geoTiffData.PixelWidth * width), (float)geoTiffData.OriginY)
        };

        var layer = new GISRasterLayer(pixelData, bounds)
        {
            Name = Path.GetFileNameWithoutExtension(FilePath),
            IsVisible = true,
            RasterPath = FilePath
        };

        Layers.Clear();
        Layers.Add(layer);
    }

    public Geometry ConvertToNTSGeometry(GISFeature feature)
    {
        try
        {
            switch (feature.Type)
            {
                case FeatureType.Point:
                    if (feature.Coordinates.Count > 0)
                    {
                        var coord = feature.Coordinates[0];
                        return _geometryFactory.CreatePoint(new Coordinate(coord.X, coord.Y));
                    }

                    break;
                case FeatureType.Line:
                    if (feature.Coordinates.Count >= 2)
                    {
                        var coords = feature.Coordinates.Select(c => new Coordinate(c.X, c.Y)).ToArray();
                        return _geometryFactory.CreateLineString(coords);
                    }

                    break;
                case FeatureType.Polygon:
                    if (feature.Coordinates.Count >= 3)
                    {
                        var coords = feature.Coordinates.Select(c => new Coordinate(c.X, c.Y)).ToList();
                        // Ensure the polygon is a closed ring for NTS
                        if (!coords[0].Equals2D(coords[^1])) coords.Add(new Coordinate(coords[0].X, coords[0].Y));
                        if (coords.Count >= 4)
                        {
                            var ring = _geometryFactory.CreateLinearRing(coords.ToArray());
                            return _geometryFactory.CreatePolygon(ring);
                        }
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to convert feature to NTS geometry: {ex.Message}");
        }

        return null;
    }

    public static GISFeature ConvertNTSGeometry(Geometry geometry, Dictionary<string, object> attributes)
    {
        if (geometry == null) return null;

        var feature = new GISFeature
        {
            Properties = attributes
        };

        switch (geometry.GeometryType)
        {
            case "Point":
                feature.Type = FeatureType.Point;
                var point = (Point)geometry;
                feature.Coordinates.Add(new Vector2((float)point.X, (float)point.Y));
                break;

            case "LineString":
                feature.Type = FeatureType.Line;
                var line = (LineString)geometry;
                foreach (var coord in line.Coordinates)
                    feature.Coordinates.Add(new Vector2((float)coord.X, (float)coord.Y));
                break;

            case "Polygon":
                feature.Type = FeatureType.Polygon;
                var polygon = (Polygon)geometry;
                if (polygon.ExteriorRing != null)
                    foreach (var coord in polygon.ExteriorRing.Coordinates)
                        feature.Coordinates.Add(new Vector2((float)coord.X, (float)coord.Y));

                break;

            case "MultiPoint":
                feature.Type = FeatureType.MultiPoint;
                var multiPoint = (MultiPoint)geometry;
                foreach (Point pt in multiPoint.Geometries)
                    feature.Coordinates.Add(new Vector2((float)pt.X, (float)pt.Y));
                break;

            case "MultiLineString":
                feature.Type = FeatureType.MultiLine;
                var multiLine = (MultiLineString)geometry;
                foreach (LineString ls in multiLine.Geometries)
                foreach (var coord in ls.Coordinates)
                    feature.Coordinates.Add(new Vector2((float)coord.X, (float)coord.Y));

                break;

            case "MultiPolygon":
                feature.Type = FeatureType.MultiPolygon;
                var multiPolygon = (MultiPolygon)geometry;
                // Take first polygon's exterior ring for simplicity
                if (multiPolygon.Geometries.Length > 0)
                {
                    var firstPoly = (Polygon)multiPolygon.Geometries[0];
                    if (firstPoly.ExteriorRing != null)
                        foreach (var coord in firstPoly.ExteriorRing.Coordinates)
                            feature.Coordinates.Add(new Vector2((float)coord.X, (float)coord.Y));
                }

                break;
        }

        return feature.Coordinates.Count > 0 ? feature : null;
    }

    public override void Unload()
    {
        foreach (var layer in Layers) layer.Features.Clear();
        Logger.Log($"Unloaded GIS dataset: {Name}");
    }

    public void AddFeature(GISLayer layer, GISFeature feature)
    {
        if (layer == null || !Layers.Contains(layer))
        {
            Logger.LogError("Cannot add feature to invalid layer");
            return;
        }

        layer.Features.Add(feature);
        UpdateBounds();
        Logger.Log($"Added {feature.Type} feature to layer '{layer.Name}'");
    }

    public void RemoveFeature(GISLayer layer, GISFeature feature)
    {
        if (layer == null || !layer.Features.Contains(feature))
            return;

        layer.Features.Remove(feature);
        UpdateBounds();
    }

    public GISLayer CreateLayerFromMetadata(List<Dataset> datasets)
    {
        var layer = new GISLayer
        {
            Name = "Sample Locations",
            Type = LayerType.Vector,
            IsVisible = true,
            IsEditable = false,
            Color = new Vector4(1.0f, 0.2f, 0.2f, 1.0f)
        };

        foreach (var dataset in datasets)
        {
            var meta = dataset.DatasetMetadata;
            if (meta?.Latitude != null && meta?.Longitude != null)
            {
                var feature = new GISFeature
                {
                    Type = FeatureType.Point,
                    Coordinates = new List<Vector2>
                    {
                        new((float)meta.Longitude.Value, (float)meta.Latitude.Value)
                    },
                    Properties = new Dictionary<string, object>
                    {
                        ["name"] = dataset.Name,
                        ["sample"] = meta.SampleName ?? "",
                        ["location"] = meta.LocationName ?? "",
                        ["depth"] = meta.Depth?.ToString() ?? "",
                        ["date"] = meta.CollectionDate?.ToString("yyyy-MM-dd") ?? "",
                        ["collector"] = meta.Collector ?? ""
                    }
                };
                layer.Features.Add(feature);
            }
        }

        return layer;
    }

    public void UpdateBounds()
    {
        if (Layers.Count == 0 || Layers.All(l => l.Type == LayerType.Raster && l.Features.Count == 0))
        {
            // If only raster layers, use their bounds
            if (Layers.Any(l => l is GISRasterLayer))
            {
                var rasterLayers = Layers.OfType<GISRasterLayer>().ToList();
                var minX = rasterLayers.Min(l => l.Bounds.Min.X);
                var minY = rasterLayers.Min(l => l.Bounds.Min.Y);
                var maxX = rasterLayers.Max(l => l.Bounds.Max.X);
                var maxY = rasterLayers.Max(l => l.Bounds.Max.Y);
                Bounds = new BoundingBox { Min = new Vector2(minX, minY), Max = new Vector2(maxX, maxY) };
            }
            else
            {
                Bounds = new BoundingBox { Min = Vector2.Zero, Max = Vector2.One * 100 };
            }

            Center = Bounds.Center;
            return;
        }

        float minx = float.MaxValue, miny = float.MaxValue;
        float maxx = float.MinValue, maxy = float.MinValue;

        var allCoords = Layers.Where(l => l.Type == LayerType.Vector).SelectMany(l => l.Features)
            .SelectMany(f => f.Coordinates);

        if (!allCoords.Any())
        {
            // Handle case with layers but no vector features
            Bounds = new BoundingBox { Min = Vector2.Zero, Max = Vector2.One * 100 };
            Center = Bounds.Center;
            return;
        }


        foreach (var coord in allCoords)
        {
            minx = Math.Min(minx, coord.X);
            miny = Math.Min(miny, coord.Y);
            maxx = Math.Max(maxx, coord.X);
            maxy = Math.Max(maxy, coord.Y);
        }

        Bounds = new BoundingBox
        {
            Min = new Vector2(minx, miny),
            Max = new Vector2(maxx, maxy)
        };

        Center = (Bounds.Min + Bounds.Max) * 0.5f;
    }

    // --- EXPORT METHODS REMOVED, a synchronous GeoJSON export is kept for now ---

    public void SaveAsGeoJSON(string path)
    {
        Logger.Log($"Exporting to GeoJSON: {path}");

        try
        {
            var features = new List<Dictionary<string, object>>();

            foreach (var layer in Layers.Where(l => l.Type == LayerType.Vector))
            foreach (var feature in layer.Features)
            {
                var geoFeature = new Dictionary<string, object>
                {
                    ["type"] = "Feature",
                    ["geometry"] = CreateGeoJsonGeometry(feature),
                    ["properties"] = feature.Properties
                };
                features.Add(geoFeature);
            }

            var featureCollection = new Dictionary<string, object>
            {
                ["type"] = "FeatureCollection",
                ["features"] = features
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(featureCollection, options);
            File.WriteAllText(path, json);

            Logger.Log($"Exported {features.Count} features to GeoJSON");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export GeoJSON: {ex.Message}");
            throw;
        }
    }

    public void SaveLayerAsCsv(GISLayer layer, string path)
    {
        if (layer.Type != LayerType.Vector)
        {
            Logger.LogError("Can only export attributes from vector layers.");
            return;
        }

        Logger.Log($"Exporting attributes for layer '{layer.Name}' to CSV: {path}");

        try
        {
            // First pass: find all unique attribute keys (headers)
            var headers = new HashSet<string>();
            foreach (var feature in layer.Features)
            foreach (var key in feature.Properties.Keys)
                headers.Add(key);

            var orderedHeaders = headers.OrderBy(h => h).ToList();

            var csv = new StringBuilder();

            // Write header row
            csv.AppendLine(string.Join(",", orderedHeaders.Select(h => $"\"{h.Replace("\"", "\"\"")}\"")));

            // Write data rows
            foreach (var feature in layer.Features)
            {
                var row = new List<string>();
                foreach (var header in orderedHeaders)
                    if (feature.Properties.TryGetValue(header, out var value) && value != null)
                    {
                        var cellValue = value.ToString().Replace("\"", "\"\"");
                        // Enclose if it contains a comma or quote
                        if (cellValue.Contains(',') || cellValue.Contains('"'))
                            row.Add($"\"{cellValue}\"");
                        else
                            row.Add(cellValue);
                    }
                    else
                    {
                        row.Add(""); // Empty cell for missing attribute
                    }

                csv.AppendLine(string.Join(",", row));
            }

            File.WriteAllText(path, csv.ToString());
            Logger.Log($"Successfully exported {layer.Features.Count} records to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export layer attributes to CSV: {ex.Message}");
            throw;
        }
    }

    private Dictionary<string, object> CreateGeoJsonGeometry(GISFeature feature)
    {
        var geometry = new Dictionary<string, object>();

        switch (feature.Type)
        {
            case FeatureType.Point:
                geometry["type"] = "Point";
                if (feature.Coordinates.Count > 0)
                {
                    var coord = feature.Coordinates[0];
                    geometry["coordinates"] = new[] { coord.X, coord.Y };
                }

                break;

            case FeatureType.Line:
                geometry["type"] = "LineString";
                geometry["coordinates"] = feature.Coordinates.Select(c => new[] { c.X, c.Y }).ToArray();
                break;

            case FeatureType.Polygon:
                geometry["type"] = "Polygon";
                var ring = feature.Coordinates.Select(c => new[] { c.X, c.Y }).ToArray();
                geometry["coordinates"] = new[] { ring };
                break;

            default:
                geometry["type"] = "Point";
                geometry["coordinates"] = new[] { 0.0, 0.0 };
                break;
        }

        return geometry;
    }
}

// --- Other classes in GISDataset.cs are unchanged ---

public class GISLayer
{
    public string Name { get; set; }
    public LayerType Type { get; set; }
    public List<GISFeature> Features { get; set; } = new();
    public bool IsVisible { get; set; } = true;
    public bool IsEditable { get; set; } = true;
    public Vector4 Color { get; set; } = new(0.2f, 0.5f, 1.0f, 1.0f);
    public float LineWidth { get; set; } = 2.0f;
    public float PointSize { get; set; } = 5.0f;
    public string RasterPath { get; set; } // For raster layers
    public Dictionary<string, object> Properties { get; set; } = new();
}

// --- NEW CLASS: GISRasterLayer ---
public class GISRasterLayer : GISLayer
{
    private readonly float[,] _pixelData;

    public GISRasterLayer(float[,] pixelData, BoundingBox bounds)
    {
        Type = LayerType.Raster;
        _pixelData = pixelData;
        Width = pixelData.GetLength(0);
        Height = pixelData.GetLength(1);
        Bounds = bounds;
    }

    public int Width { get; }
    public int Height { get; }
    public BoundingBox Bounds { get; }

    public float[,] GetPixelData()
    {
        return _pixelData;
    }
}

public class GISFeature
{
    public FeatureType Type { get; set; }
    public List<Vector2> Coordinates { get; set; } = new();
    public Dictionary<string, object> Properties { get; set; } = new();
    public bool IsSelected { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // --- NEW METHOD: Clone ---
    public GISFeature Clone()
    {
        return new GISFeature
        {
            Type = Type,
            // Create new collections to ensure a deep copy
            Coordinates = new List<Vector2>(Coordinates),
            Properties = new Dictionary<string, object>(Properties),
            IsSelected = IsSelected,
            Id = Id
        };
    }
}

public class GISProjection
{
    public string EPSG { get; set; } = "EPSG:4326"; // WGS84 by default
    public string Name { get; set; } = "WGS 84";
    public ProjectionType Type { get; set; } = ProjectionType.Geographic;
}

public class BoundingBox
{
    public Vector2 Min { get; set; }
    public Vector2 Max { get; set; }

    public float Width => Max.X - Min.X;
    public float Height => Max.Y - Min.Y;
    public Vector2 Center => (Min + Max) * 0.5f;
}

public enum LayerType
{
    Vector,
    Raster,
    Basemap
}

public enum FeatureType
{
    Point,
    Line,
    Polygon,
    MultiPoint,
    MultiLine,
    MultiPolygon
}

public enum BasemapType
{
    None,
    GeoTIFF,
    TileServer,
    WMS
}

public enum ProjectionType
{
    Geographic,
    Projected
}

// DTOs for serialization
public class GISDatasetDTO : DatasetDTO
{
    public List<GISLayerDTO> Layers { get; set; }
    public string BasemapType { get; set; }
    public string BasemapPath { get; set; }
    public string ActiveBasemapLayerName { get; set; }
    public Vector2 Center { get; set; }
    public float DefaultZoom { get; set; }
    public long Tags { get; set; }
    public Dictionary<string, string> GISMetadata { get; set; } = new();
}