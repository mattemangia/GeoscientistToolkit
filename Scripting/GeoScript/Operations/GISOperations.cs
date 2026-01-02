using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace GeoscientistToolkit.Scripting.GeoScript.Operations
{
    /// <summary>
    /// Create buffer around GIS features
    /// </summary>
    public class BufferOperation : IOperation
    {
        public string Name => "BUFFER";
        public string Description => "Create buffer zone around features";
        public Dictionary<string, string> Parameters => new()
        {
            { "distance", "Buffer distance in map units" }
        };

        public Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not GISDataset gisDataset)
                throw new InvalidOperationException("BUFFER can only be applied to GIS datasets");

            if (parameters.Count < 1)
                throw new ArgumentException("BUFFER requires a distance parameter");

            if (!double.TryParse(parameters[0]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var distance))
                throw new ArgumentException("BUFFER distance must be numeric");

            var output = GISOperationHelpers.CreateOutputDataset(gisDataset, $"{gisDataset.Name}_buffer");
            foreach (var layer in gisDataset.Layers.Where(l => l.Type == LayerType.Vector))
            {
                var newLayer = GISOperationHelpers.CreateLayerFromTemplate(layer, $"{layer.Name}_Buffer");
                foreach (var feature in layer.Features)
                {
                    var geometry = GISOperationHelpers.ConvertToGeometry(feature);
                    var buffered = GISOperationsImpl.BufferGeometry(geometry, distance);
                    foreach (var converted in GISOperationHelpers.ConvertFromGeometry(buffered, feature.Properties))
                        newLayer.Features.Add(converted);
                }
                output.Layers.Add(newLayer);
            }

            output.UpdateBounds();
            return output;
        }

        public bool CanApplyTo(DatasetType type) => type == DatasetType.GIS;
    }

    /// <summary>
    /// Clip GIS features to boundary
    /// </summary>
    public class ClipOperation : IOperation
    {
        public string Name => "CLIP";
        public string Description => "Clip features to a boundary";
        public Dictionary<string, string> Parameters => new()
        {
            { "boundary", "Clipping boundary dataset or extent" }
        };

        public Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not GISDataset gisDataset)
                throw new InvalidOperationException("CLIP can only be applied to GIS datasets");

            if (parameters.Count < 1)
                throw new ArgumentException("CLIP requires a boundary parameter");

            var boundaryGeometry = GISOperationHelpers.ResolveBoundaryGeometry(parameters[0]?.ToString());
            if (boundaryGeometry == null)
                throw new ArgumentException("CLIP boundary could not be resolved");

            var output = GISOperationHelpers.CreateOutputDataset(gisDataset, $"{gisDataset.Name}_clip");
            foreach (var layer in gisDataset.Layers.Where(l => l.Type == LayerType.Vector))
            {
                var newLayer = GISOperationHelpers.CreateLayerFromTemplate(layer, $"{layer.Name}_Clip");
                foreach (var feature in layer.Features)
                {
                    var geometry = GISOperationHelpers.ConvertToGeometry(feature);
                    var clipped = GISOperationsImpl.ClipGeometry(geometry, boundaryGeometry);
                    foreach (var converted in GISOperationHelpers.ConvertFromGeometry(clipped, feature.Properties))
                        newLayer.Features.Add(converted);
                }
                output.Layers.Add(newLayer);
            }

            output.UpdateBounds();
            return output;
        }

        public bool CanApplyTo(DatasetType type) => type == DatasetType.GIS;
    }

    /// <summary>
    /// Union of GIS features
    /// </summary>
    public class UnionOperation : IOperation
    {
        public string Name => "UNION";
        public string Description => "Combine features from multiple layers";
        public Dictionary<string, string> Parameters => new()
        {
            { "layer", "Layer to union with" }
        };

        public Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not GISDataset gisDataset)
                throw new InvalidOperationException("UNION can only be applied to GIS datasets");

            var targetLayer = GISOperationHelpers.ResolveLayer(gisDataset, parameters.Count > 0 ? parameters[0]?.ToString() : null);
            if (targetLayer == null)
                throw new ArgumentException("UNION requires a valid layer name");

            var geometries = targetLayer.Features
                .Select(GISOperationHelpers.ConvertToGeometry)
                .Where(g => g != null)
                .ToList();

            var unioned = GISOperationsImpl.UnionGeometries(geometries);
            var output = GISOperationHelpers.CreateOutputDataset(gisDataset, $"{gisDataset.Name}_union");
            var newLayer = GISOperationHelpers.CreateLayerFromTemplate(targetLayer, $"{targetLayer.Name}_Union");
            foreach (var converted in GISOperationHelpers.ConvertFromGeometry(unioned, new Dictionary<string, object>()))
                newLayer.Features.Add(converted);
            output.Layers.Add(newLayer);

            output.UpdateBounds();
            return output;
        }

        public bool CanApplyTo(DatasetType type) => type == DatasetType.GIS;
    }

    /// <summary>
    /// Intersect GIS features
    /// </summary>
    public class IntersectOperation : IOperation
    {
        public string Name => "INTERSECT";
        public string Description => "Find intersection of features";
        public Dictionary<string, string> Parameters => new()
        {
            { "layer", "Layer to intersect with" }
        };

        public Dataset Execute(Dataset inputDataset, List<object> parameters)
        {
            if (inputDataset is not GISDataset gisDataset)
                throw new InvalidOperationException("INTERSECT can only be applied to GIS datasets");

            var otherLayer = GISOperationHelpers.ResolveLayer(gisDataset, parameters.Count > 0 ? parameters[0]?.ToString() : null);
            if (otherLayer == null)
                throw new ArgumentException("INTERSECT requires a valid layer name");

            var baseLayer = gisDataset.Layers.FirstOrDefault(l => l.Type == LayerType.Vector && l != otherLayer);
            if (baseLayer == null)
                throw new InvalidOperationException("INTERSECT requires at least two vector layers");

            var output = GISOperationHelpers.CreateOutputDataset(gisDataset, $"{gisDataset.Name}_intersect");
            var newLayer = GISOperationHelpers.CreateLayerFromTemplate(baseLayer, $"{baseLayer.Name}_Intersect");

            foreach (var feature in baseLayer.Features)
            {
                var geomA = GISOperationHelpers.ConvertToGeometry(feature);
                if (geomA == null)
                    continue;

                foreach (var other in otherLayer.Features)
                {
                    var geomB = GISOperationHelpers.ConvertToGeometry(other);
                    var intersection = GISOperationsImpl.IntersectGeometries(geomA, geomB);
                    foreach (var converted in GISOperationHelpers.ConvertFromGeometry(intersection, feature.Properties))
                        newLayer.Features.Add(converted);
                }
            }

            output.Layers.Add(newLayer);
            output.UpdateBounds();
            return output;
        }

        public bool CanApplyTo(DatasetType type) => type == DatasetType.GIS;

    }

    internal static class GISOperationHelpers
    {
        public static GISDataset CreateOutputDataset(GISDataset source, string name)
        {
            var output = new GISDataset(name, string.Empty)
            {
                Projection = source.Projection,
                BasemapType = source.BasemapType,
                BasemapPath = source.BasemapPath,
                ActiveBasemapLayerName = source.ActiveBasemapLayerName,
                Tags = source.Tags,
                GISMetadata = new Dictionary<string, object>(source.GISMetadata)
            };
            output.Layers.Clear();
            return output;
        }

        public static GISLayer CreateLayerFromTemplate(GISLayer template, string name)
        {
            return new GISLayer
            {
                Name = name,
                Type = LayerType.Vector,
                IsVisible = template.IsVisible,
                IsEditable = template.IsEditable,
                Color = template.Color,
                LineWidth = template.LineWidth,
                PointSize = template.PointSize,
                Properties = new Dictionary<string, object>(template.Properties)
            };
        }

        public static GISLayer ResolveLayer(GISDataset dataset, string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return dataset.Layers.FirstOrDefault(l => l.Type == LayerType.Vector);

            return dataset.Layers.FirstOrDefault(l =>
                l.Type == LayerType.Vector &&
                l.Name.Equals(layerName, StringComparison.OrdinalIgnoreCase));
        }

        public static Geometry ResolveBoundaryGeometry(string boundaryParam)
        {
            if (string.IsNullOrWhiteSpace(boundaryParam))
                return null;

            var dataset = ProjectManager.Instance.LoadedDatasets
                .OfType<GISDataset>()
                .FirstOrDefault(ds => ds.Name.Equals(boundaryParam, StringComparison.OrdinalIgnoreCase));

            if (dataset != null)
            {
                var geometries = dataset.Layers
                    .Where(l => l.Type == LayerType.Vector)
                    .SelectMany(l => l.Features)
                    .Select(ConvertToGeometry)
                    .Where(g => g != null)
                    .ToList();

                return GISOperationsImpl.UnionGeometries(geometries);
            }

            // Fallback: parse bounding box "minX,minY,maxX,maxY"
            var parts = boundaryParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 4 &&
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var minX) &&
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var minY) &&
                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var maxX) &&
                double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var maxY))
            {
                var coords = new[]
                {
                    new Coordinate(minX, minY),
                    new Coordinate(maxX, minY),
                    new Coordinate(maxX, maxY),
                    new Coordinate(minX, maxY),
                    new Coordinate(minX, minY)
                };
                return new Polygon(new LinearRing(coords));
            }

            return null;
        }

        public static Geometry ConvertToGeometry(GISFeature feature)
        {
            if (feature == null)
                return null;

            return feature.Type switch
            {
                FeatureType.Point => feature.Coordinates.Count > 0
                    ? new Point(feature.Coordinates[0].X, feature.Coordinates[0].Y)
                    : null,
                FeatureType.Line => feature.Coordinates.Count >= 2
                    ? new LineString(feature.Coordinates.Select(c => new Coordinate(c.X, c.Y)).ToArray())
                    : null,
                FeatureType.Polygon => feature.Coordinates.Count >= 3
                    ? new Polygon(new LinearRing(NormalizeRing(feature.Coordinates)))
                    : null,
                _ => null
            };
        }

        private static Coordinate[] NormalizeRing(List<Vector2> coords)
        {
            var list = coords.Select(c => new Coordinate(c.X, c.Y)).ToList();
            if (list.Count > 0 && !list[0].Equals2D(list[^1]))
                list.Add(list[0]);
            return list.ToArray();
        }

        public static IEnumerable<GISFeature> ConvertFromGeometry(Geometry geometry,
            Dictionary<string, object> properties)
        {
            if (geometry == null || geometry.IsEmpty)
                yield break;

            switch (geometry)
            {
                case Point point:
                    yield return new GISFeature
                    {
                        Type = FeatureType.Point,
                        Coordinates = new List<Vector2> { new((float)point.X, (float)point.Y) },
                        Properties = new Dictionary<string, object>(properties)
                    };
                    break;
                case LineString line:
                    yield return new GISFeature
                    {
                        Type = FeatureType.Line,
                        Coordinates = line.Coordinates.Select(c => new Vector2((float)c.X, (float)c.Y)).ToList(),
                        Properties = new Dictionary<string, object>(properties)
                    };
                    break;
                case Polygon polygon:
                    yield return new GISFeature
                    {
                        Type = FeatureType.Polygon,
                        Coordinates = polygon.ExteriorRing.Coordinates
                            .Select(c => new Vector2((float)c.X, (float)c.Y)).ToList(),
                        Properties = new Dictionary<string, object>(properties)
                    };
                    break;
                case MultiPolygon multiPolygon:
                    foreach (Polygon subPolygon in multiPolygon.Geometries)
                        foreach (var feature in ConvertFromGeometry(subPolygon, properties))
                            yield return feature;
                    break;
                case MultiLineString multiLine:
                    foreach (LineString subLine in multiLine.Geometries)
                        foreach (var feature in ConvertFromGeometry(subLine, properties))
                            yield return feature;
                    break;
                case MultiPoint multiPoint:
                    foreach (Point subPoint in multiPoint.Geometries)
                        foreach (var feature in ConvertFromGeometry(subPoint, properties))
                            yield return feature;
                    break;
            }
        }
    }
}
