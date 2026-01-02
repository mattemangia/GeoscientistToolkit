// GeoscientistToolkit/Business/GIS/GISExporter.cs

using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.GIS;
using GeoscientistToolkit.Util;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using OSGeo.GDAL;

namespace GeoscientistToolkit.Data.GIS;

/// <summary>
///     Provides functionality to export GIS datasets to various formats.
/// </summary>
public static class GISExporter
{
    private static readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326);

    /// <summary>
    ///     Asynchronously exports vector layers of a GISDataset to an ESRI Shapefile.
    /// </summary>
    /// <param name="dataset">The dataset to export.</param>
    /// <param name="path">The file path for the exported shapefile.</param>
    /// <param name="layerName">The specific layer name to export (optional).</param>
    /// <param name="progress">Handler for reporting progress updates.</param>
    /// <param name="token">A cancellation token to observe.</param>
    public static async Task ExportToShapefileAsync(GISDataset dataset, string path, string layerName,
        IProgress<float> progress, CancellationToken token = default)
    {
        await Task.Run(() =>
        {
            progress?.Report(0.0f);
            token.ThrowIfCancellationRequested();

            var allFeatures = new List<IFeature>();
            foreach (var layer in dataset.Layers.Where(l => l.Type == LayerType.Vector))
            foreach (var feature in layer.Features)
            {
                var ntsGeometry = ConvertToNTSGeometry(feature);
                if (ntsGeometry != null)
                {
                    var ntsFeature = new Feature(ntsGeometry, new AttributesTable(feature.Properties));
                    allFeatures.Add(ntsFeature);
                }
            }

            if (allFeatures.Count == 0)
            {
                progress?.Report(1.0f);
                Logger.LogWarning("No vector features found to export to shapefile.");
                return;
            }

            progress?.Report(0.2f);
            token.ThrowIfCancellationRequested();

            var writer = new ShapefileDataWriter(path, _geometryFactory)
            {
                Header = ShapefileDataWriter.GetHeader(allFeatures[0], allFeatures.Count)
            };
            writer.Write(allFeatures);

            progress?.Report(0.9f);
            token.ThrowIfCancellationRequested();

            var prjPath = Path.ChangeExtension(path, ".prj");
            File.WriteAllText(prjPath, GetProjectionWKT());


            progress?.Report(1.0f);
            Logger.Log($"Exported {allFeatures.Count} features to shapefile: {path}");
        }, token);
    }

    /// <summary>
    ///     Asynchronously exports the first raster layer of a GISDataset to a GeoTIFF file.
    /// </summary>
    /// <param name="dataset">The dataset containing the raster layer.</param>
    /// <param name="path">The file path for the exported GeoTIFF.</param>
    /// <param name="progress">Handler for reporting progress updates.</param>
    /// <param name="token">A cancellation token to observe.</param>
    public static async Task ExportToGeoTiffAsync(GISDataset dataset, string path,
        IProgress<float> progress, CancellationToken token = default)
    {
        await Task.Run(() =>
        {
            progress?.Report(0.0f);
            var rasterLayer = dataset.Layers.FirstOrDefault(l =>
                l.Type == LayerType.Raster && !string.IsNullOrEmpty(l.RasterPath) && File.Exists(l.RasterPath));

            if (rasterLayer == null)
            {
                progress?.Report(1.0f);
                Logger.LogError("Could not find a valid raster layer with a file path to export.");
                return;
            }

            token.ThrowIfCancellationRequested();
            progress?.Report(0.1f);

            var geoTiffData = BasemapManager.Instance.LoadGeoTiff(rasterLayer.RasterPath);
            if (geoTiffData == null)
            {
                progress?.Report(1.0f);
                Logger.LogError($"Failed to load GeoTIFF data from {rasterLayer.RasterPath} for export.");
                return;
            }

            token.ThrowIfCancellationRequested();
            progress?.Report(0.3f);

            using var driver = Gdal.GetDriverByName("GTiff");
            using var destDataset = driver.Create(path, geoTiffData.Width, geoTiffData.Height, geoTiffData.BandCount,
                DataType.GDT_Byte, null);

            destDataset.SetProjection(geoTiffData.Projection);
            var transform = new[]
                { geoTiffData.OriginX, geoTiffData.PixelWidth, 0, geoTiffData.OriginY, 0, geoTiffData.PixelHeight };
            destDataset.SetGeoTransform(transform);

            var bandData = new byte[geoTiffData.Width * geoTiffData.Height];

            for (var b = 1; b <= geoTiffData.BandCount; b++)
            {
                token.ThrowIfCancellationRequested();
                var progressValue = 0.3f + 0.7f * b / geoTiffData.BandCount;
                progress?.Report(progressValue);

                using var band = destDataset.GetRasterBand(b);

                // Extract single band data from the source RGBA buffer
                for (var i = 0; i < bandData.Length; i++)
                    if (b <= 4) // Assume max 4 bands (RGBA) from loader
                        bandData[i] = geoTiffData.Data[i * 4 + (b - 1)];
                    else
                        bandData[i] = 0; // Default for bands > 4

                band.WriteRaster(0, 0, geoTiffData.Width, geoTiffData.Height, bandData, geoTiffData.Width,
                    geoTiffData.Height, 0, 0);
            }

            destDataset.FlushCache();
            progress?.Report(1.0f);
            Logger.Log($"Exported raster layer to GeoTIFF: {path}");
        }, token);
    }

    private static Geometry ConvertToNTSGeometry(GISFeature feature)
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
                        if (!coords[0].Equals2D(coords[^1]))
                            coords.Add(coords[0]);
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

    private static string GetProjectionWKT()
    {
        return
            @"GEOGCS[""WGS 84"",DATUM[""WGS_1984"",SPHEROID[""WGS 84"",6378137,298.257223563,AUTHORITY[""EPSG"",""7030""]],AUTHORITY[""EPSG"",""6326""]],PRIMEM[""Greenwich"",0,AUTHORITY[""EPSG"",""8901""]],UNIT[""degree"",0.0174532925199433,AUTHORITY[""EPSG"",""9122""]],AUTHORITY[""EPSG"",""4326""]]";
    }
}