using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Util;

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
            // Simplified shapefile loading - in production you'd use a library like NetTopologySuite
            var layer = new GISLayer
            {
                Name = Path.GetFileNameWithoutExtension(FilePath),
                Type = LayerType.Vector,
                IsVisible = true,
                Color = new Vector4(0.2f, 0.5f, 1.0f, 1.0f)
            };
            
            // Read .shp file (simplified - actual implementation would parse binary format)
            // For now, create some sample features
            Logger.Log($"Loading shapefile: {FilePath}");
            
            // This is a placeholder - actual shapefile reading would go here
            // You would typically use a library like DotSpatial or NetTopologySuite
            
            Layers.Clear();
            Layers.Add(layer);
        }
        
        private void LoadGeoJSON()
        {
            var json = File.ReadAllText(FilePath);
            var layer = new GISLayer
            {
                Name = Path.GetFileNameWithoutExtension(FilePath),
                Type = LayerType.Vector,
                IsVisible = true,
                Color = new Vector4(0.2f, 0.8f, 0.2f, 1.0f)
            };
            
            // Parse GeoJSON (simplified - you'd use Newtonsoft.Json or System.Text.Json)
            Logger.Log($"Loading GeoJSON: {FilePath}");
            
            Layers.Clear();
            Layers.Add(layer);
        }
        
        private void LoadKML()
        {
            Logger.Log($"Loading KML/KMZ: {FilePath}");
            // KML/KMZ loading implementation would go here
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
            // Simplified shapefile export
            Logger.Log($"Exporting to shapefile: {path}");
            
            // In production, you'd use a proper shapefile writer
            // This would write .shp, .shx, .dbf files
        }
        
        public void SaveAsGeoJSON(string path)
        {
            Logger.Log($"Exporting to GeoJSON: {path}");
            
            // Build GeoJSON structure
            var geoJson = new
            {
                type = "FeatureCollection",
                features = new List<object>()
            };
            
            foreach (var layer in Layers.Where(l => l.Type == LayerType.Vector))
            {
                foreach (var feature in layer.Features)
                {
                    var geoFeature = new
                    {
                        type = "Feature",
                        geometry = new
                        {
                            type = feature.Type.ToString(),
                            coordinates = feature.Type == FeatureType.Point
                                ? (object)new[] { feature.Coordinates[0].X, feature.Coordinates[0].Y }
                                : feature.Coordinates.Select(c => new[] { c.X, c.Y }).ToArray()
                        },
                        properties = feature.Properties
                    };
                    // Would add to features list
                }
            }
            
            // Write JSON to file
            File.WriteAllText(path, "{}"); // Placeholder - would serialize geoJson object
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