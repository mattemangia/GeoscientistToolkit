// GeoscientistToolkit/Data/GIS/GISDataset.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.IO.Compression;
using System.Xml.Linq;
using GeoscientistToolkit.Util;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace GeoscientistToolkit.Data.GIS
{
    public class GISDataset : Dataset, ISerializableDataset
    {
        // Map properties
        public List<GISLayer> Layers { get; set; } = new List<GISLayer>();
        public GISProjection Projection { get; set; } = new GISProjection();
        public BoundingBox Bounds { get; set; } = new BoundingBox();
        public Vector2 Center { get; set; }
        public float DefaultZoom { get; set; } = 1.0f;
        
        // Basemap
        public BasemapType BasemapType { get; set; } = BasemapType.None;
        public string BasemapPath { get; set; }
        
        // Edit state
        public bool IsEditable { get; set; } = true;
        
        // Geometry factory for NetTopologySuite
        private static readonly GeometryFactory _geometryFactory = new GeometryFactory(new PrecisionModel(), 4326);
        
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
        
        public override long GetSizeInBytes()
        {
            if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
            {
                return new FileInfo(FilePath).Length;
            }
            
            // Estimate size for in-memory data
            long size = 0;
            foreach (var layer in Layers)
            {
                size += layer.Features.Count * 100; // Rough estimate per feature
            }
            return size;
        }
        
        public override void Load()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                // New empty dataset
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
                string extension = Path.GetExtension(FilePath).ToLower();
                switch (extension)
                {
                    case ".shp":
                        LoadShapefile();
                        break;
                    case ".geojson":
                    case ".json":
                        LoadGeoJSON();
                        break;
                    case ".kmz":
                        LoadKMZ();
                        break;
                    case ".kml":
                        LoadKML();
                        break;
                    case ".tif":
                    case ".tiff":
                        LoadGeoTIFF();
                        break;
                    default:
                        throw new NotSupportedException($"File format '{extension}' is not supported for GIS datasets.");
                }
                
                UpdateBounds();
                Logger.Log($"Loaded GIS dataset: {Name} with {Layers.Count} layers");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load GIS dataset '{Name}': {ex.Message}");
                throw;
            }
        }
        
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
                string prjPath = Path.ChangeExtension(FilePath, ".prj");
                if (File.Exists(prjPath))
                {
                    string wkt = File.ReadAllText(prjPath);
                    var csFactory = new CoordinateSystemFactory();
                    try
                    {
                        var cs = csFactory.CreateFromWkt(wkt);
                        Projection.Name = cs.Name;
                        Projection.EPSG = cs.AuthorityCode > 0 ? $"EPSG:{cs.AuthorityCode}" : "Custom";
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
                    for (int i = 0; i < header.NumFields; i++)
                    {
                        string fieldName = header.Fields[i].Name;
                        var value = shpReader.GetValue(i);
                        if (value != null)
                        {
                            attributes[fieldName] = value;
                        }
                    }
                    
                    // Convert NTS geometry to our GISFeature
                    var feature = ConvertNTSGeometry(geometry, attributes);
                    if (feature != null)
                    {
                        layer.Features.Add(feature);
                    }
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
                    string geoJsonType = typeElement.GetString();
                    
                    if (geoJsonType == "FeatureCollection" && root.TryGetProperty("features", out var features))
                    {
                        foreach (var featureElement in features.EnumerateArray())
                        {
                            var feature = ParseGeoJsonFeature(featureElement);
                            if (feature != null)
                            {
                                layer.Features.Add(feature);
                            }
                        }
                    }
                    else if (geoJsonType == "Feature")
                    {
                        var feature = ParseGeoJsonFeature(root);
                        if (feature != null)
                        {
                            layer.Features.Add(feature);
                        }
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
                
                string geometryType = geomType.GetString();
                var coordinates = new List<Vector2>();
                
                if (geometry.TryGetProperty("coordinates", out var coordsElement))
                {
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
                            {
                                coordinates = ParseCoordinateList(coordsElement[0]);
                            }
                            break;
                            
                        case "MultiPoint":
                            coordinates = ParseCoordinateList(coordsElement);
                            break;
                    }
                }
                
                // Parse properties
                var properties = new Dictionary<string, object>();
                if (featureElement.TryGetProperty("properties", out var propsElement))
                {
                    foreach (var prop in propsElement.EnumerateObject())
                    {
                        properties[prop.Name] = ParseJsonValue(prop.Value);
                    }
                }
                
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
                    float lon = (float)element[0].GetDouble();
                    float lat = (float)element[1].GetDouble();
                    return new Vector2(lon, lat);
                }
            }
            catch { }
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
                JsonValueKind.Number => element.TryGetInt32(out int intVal) ? intVal : element.GetDouble(),
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
            
            if (kmlEntry == null)
            {
                throw new InvalidOperationException("No KML file found in KMZ archive");
            }
            
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
                    if (feature != null)
                    {
                        layer.Features.Add(feature);
                    }
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
                    {
                        feature.Coordinates = ParseKMLCoordinates(coordsText);
                    }
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
                            {
                                feature.Coordinates = ParseKMLCoordinates(coordsText);
                            }
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
                {
                    if (float.TryParse(values[0], out float lon) && float.TryParse(values[1], out float lat))
                    {
                        coords.Add(new Vector2(lon, lat));
                    }
                }
            }
            
            return coords;
        }
        
        private void LoadGeoTIFF()
        {
            var layer = new GISLayer
            {
                Name = Path.GetFileNameWithoutExtension(FilePath),
                Type = LayerType.Raster,
                IsVisible = true,
                RasterPath = FilePath
            };
            
            Logger.Log($"Loading GeoTIFF as basemap: {FilePath}");
            BasemapType = BasemapType.GeoTIFF;
            BasemapPath = FilePath;
            
            Layers.Clear();
            Layers.Add(layer);
        }
        
        private GISFeature ConvertNTSGeometry(Geometry geometry, Dictionary<string, object> attributes)
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
                    {
                        feature.Coordinates.Add(new Vector2((float)coord.X, (float)coord.Y));
                    }
                    break;
                    
                case "Polygon":
                    feature.Type = FeatureType.Polygon;
                    var polygon = (Polygon)geometry;
                    if (polygon.ExteriorRing != null)
                    {
                        foreach (var coord in polygon.ExteriorRing.Coordinates)
                        {
                            feature.Coordinates.Add(new Vector2((float)coord.X, (float)coord.Y));
                        }
                    }
                    break;
                    
                case "MultiPoint":
                    feature.Type = FeatureType.MultiPoint;
                    var multiPoint = (MultiPoint)geometry;
                    foreach (Point pt in multiPoint.Geometries)
                    {
                        feature.Coordinates.Add(new Vector2((float)pt.X, (float)pt.Y));
                    }
                    break;
                    
                case "MultiLineString":
                    feature.Type = FeatureType.MultiLine;
                    var multiLine = (MultiLineString)geometry;
                    foreach (LineString ls in multiLine.Geometries)
                    {
                        foreach (var coord in ls.Coordinates)
                        {
                            feature.Coordinates.Add(new Vector2((float)coord.X, (float)coord.Y));
                        }
                    }
                    break;
                    
                case "MultiPolygon":
                    feature.Type = FeatureType.MultiPolygon;
                    var multiPolygon = (MultiPolygon)geometry;
                    // Take first polygon's exterior ring for simplicity
                    if (multiPolygon.Geometries.Length > 0)
                    {
                        var firstPoly = (Polygon)multiPolygon.Geometries[0];
                        if (firstPoly.ExteriorRing != null)
                        {
                            foreach (var coord in firstPoly.ExteriorRing.Coordinates)
                            {
                                feature.Coordinates.Add(new Vector2((float)coord.X, (float)coord.Y));
                            }
                        }
                    }
                    break;
            }
            
            return feature.Coordinates.Count > 0 ? feature : null;
        }
        
        public override void Unload()
        {
            foreach (var layer in Layers)
            {
                layer.Features.Clear();
            }
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
                            new Vector2((float)meta.Longitude.Value, (float)meta.Latitude.Value)
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
            if (Layers.Count == 0 || Layers.All(l => l.Features.Count == 0))
            {
                Bounds = new BoundingBox { Min = Vector2.Zero, Max = Vector2.One * 100 };
                Center = new Vector2(0, 0);
                return;
            }
            
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            
            foreach (var layer in Layers.Where(l => l.Type == LayerType.Vector))
            {
                foreach (var feature in layer.Features)
                {
                    foreach (var coord in feature.Coordinates)
                    {
                        minX = Math.Min(minX, coord.X);
                        minY = Math.Min(minY, coord.Y);
                        maxX = Math.Max(maxX, coord.X);
                        maxY = Math.Max(maxY, coord.Y);
                    }
                }
            }
            
            Bounds = new BoundingBox
            {
                Min = new Vector2(minX, minY),
                Max = new Vector2(maxX, maxY)
            };
            
            Center = (Bounds.Min + Bounds.Max) * 0.5f;
        }
        
        public void SaveAsShapefile(string path)
        {
            Logger.Log($"Exporting to shapefile: {path}");
            
            try
            {
                // Combine all vector layers
                var allFeatures = new List<IFeature>();
                var attributeTable = new AttributesTable();
                
                foreach (var layer in Layers.Where(l => l.Type == LayerType.Vector))
                {
                    foreach (var feature in layer.Features)
                    {
                        var ntsGeometry = ConvertToNTSGeometry(feature);
                        if (ntsGeometry != null)
                        {
                            var ntsFeature = new Feature(ntsGeometry, new AttributesTable(feature.Properties));
                            allFeatures.Add(ntsFeature);
                        }
                    }
                }
                
                if (allFeatures.Count == 0)
                {
                    Logger.LogWarning("No features to export");
                    return;
                }
                
                // Write shapefile
                var writer = new ShapefileDataWriter(path, _geometryFactory)
                {
                    Header = ShapefileDataWriter.GetHeader(allFeatures[0], allFeatures.Count)
                };
                
                writer.Write(allFeatures);
                
                // Write projection file if we have projection info
                if (!string.IsNullOrEmpty(Projection.EPSG) && Projection.EPSG != "EPSG:4326")
                {
                    string prjPath = Path.ChangeExtension(path, ".prj");
                    // In production, you'd write the actual WKT projection string here
                    File.WriteAllText(prjPath, GetProjectionWKT());
                }
                
                Logger.Log($"Exported {allFeatures.Count} features to shapefile");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export shapefile: {ex.Message}");
                throw;
            }
        }
        
        public void SaveAsGeoJSON(string path)
        {
            Logger.Log($"Exporting to GeoJSON: {path}");
            
            try
            {
                var features = new List<Dictionary<string, object>>();
                
                foreach (var layer in Layers.Where(l => l.Type == LayerType.Vector))
                {
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
                
                string json = JsonSerializer.Serialize(featureCollection, options);
                File.WriteAllText(path, json);
                
                Logger.Log($"Exported {features.Count} features to GeoJSON");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export GeoJSON: {ex.Message}");
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
        
        private Geometry ConvertToNTSGeometry(GISFeature feature)
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
                            // Ensure closed ring
                            if (!coords[0].Equals2D(coords[coords.Count - 1]))
                            {
                                coords.Add(coords[0]);
                            }
                            if (coords.Count >= 4) // Minimum for a valid polygon
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
        
        private string GetProjectionWKT()
        {
            // Return WGS84 WKT as default
            return @"GEOGCS[""WGS 84"",DATUM[""WGS_1984"",SPHEROID[""WGS 84"",6378137,298.257223563,AUTHORITY[""EPSG"",""7030""]],AUTHORITY[""EPSG"",""6326""]],PRIMEM[""Greenwich"",0,AUTHORITY[""EPSG"",""8901""]],UNIT[""degree"",0.0174532925199433,AUTHORITY[""EPSG"",""9122""]],AUTHORITY[""EPSG"",""4326""]]";
        }
        
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
                    FeatureCount = l.Features.Count
                }).ToList(),
                BasemapType = BasemapType.ToString(),
                BasemapPath = BasemapPath,
                Center = Center,
                DefaultZoom = DefaultZoom
            };
        }
    }
    
    public class GISLayer
    {
        public string Name { get; set; }
        public LayerType Type { get; set; }
        public List<GISFeature> Features { get; set; } = new List<GISFeature>();
        public bool IsVisible { get; set; } = true;
        public bool IsEditable { get; set; } = true;
        public Vector4 Color { get; set; } = new Vector4(0.2f, 0.5f, 1.0f, 1.0f);
        public float LineWidth { get; set; } = 2.0f;
        public float PointSize { get; set; } = 5.0f;
        public string RasterPath { get; set; } // For raster layers
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }
    
    public class GISFeature
    {
        public FeatureType Type { get; set; }
        public List<Vector2> Coordinates { get; set; } = new List<Vector2>();
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
        public bool IsSelected { get; set; }
        public string Id { get; set; } = Guid.NewGuid().ToString();
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
        public Vector2 Center { get; set; }
        public float DefaultZoom { get; set; }
    }
    
    public class GISLayerDTO
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsVisible { get; set; }
        public bool IsEditable { get; set; }
        public Vector4 Color { get; set; }
        public int FeatureCount { get; set; }
    }
}