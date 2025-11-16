// GeoscientistToolkit/Data/GIS/BasemapElevationExtractor.cs
//
// Extract elevation data from online basemap tiles for hydrological analysis
//

using System.Numerics;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.UI.GIS;
using GeoscientistToolkit.Util;
using StbImageSharp;

namespace GeoscientistToolkit.Data.GIS;

/// <summary>
/// Extracts and processes elevation data from online basemap tiles
/// </summary>
public class BasemapElevationExtractor
{
    private readonly BasemapManager _basemapManager;

    public BasemapElevationExtractor()
    {
        _basemapManager = BasemapManager.Instance;
    }

    /// <summary>
    /// Extract elevation data from ESRI World Hillshade basemap tiles
    /// </summary>
    public async Task<GISRasterLayer> ExtractElevationFromBasemap(
        BoundingBox bounds,
        int targetWidth,
        int targetHeight,
        int zoomLevel = 10)
    {
        // Switch to elevation provider
        var elevationProvider = BasemapManager.Providers
            .FirstOrDefault(p => p.Id == "esri_hillshade");

        if (elevationProvider == null)
        {
            throw new Exception("Elevation basemap provider not available");
        }

        var originalProvider = _basemapManager.CurrentProvider;
        _basemapManager.CurrentProvider = elevationProvider;

        try
        {
            // Get tile coordinates for the bounding box
            var tiles = _basemapManager.GetVisibleTiles(bounds, zoomLevel);

            Logger.Log($"Extracting elevation from {tiles.Count} tiles at zoom {zoomLevel}");

            // Download all tiles
            var tileDataList = new List<(TileCoordinate coord, TileData data)>();
            foreach (var tileCoord in tiles)
            {
                var tileData = await _basemapManager.GetTileAsync(tileCoord.X, tileCoord.Y, tileCoord.Z);
                if (tileData != null)
                {
                    tileDataList.Add((tileCoord, tileData));
                }
            }

            if (tileDataList.Count == 0)
            {
                throw new Exception("Failed to download any elevation tiles");
            }

            // Mosaic tiles and extract elevation
            var elevation = MosaicAndExtractElevation(tileDataList, bounds, targetWidth, targetHeight, zoomLevel);

            // Create raster layer
            var layer = new GISRasterLayer(elevation, bounds)
            {
                Name = "Extracted Elevation",
                IsVisible = true,
                RasterPath = null // In-memory only
            };

            Logger.Log($"Elevation extraction complete: {targetWidth}x{targetHeight}");
            return layer;
        }
        finally
        {
            _basemapManager.CurrentProvider = originalProvider;
        }
    }

    private float[,] MosaicAndExtractElevation(
        List<(TileCoordinate coord, TileData data)> tiles,
        BoundingBox bounds,
        int targetWidth,
        int targetHeight,
        int zoomLevel)
    {
        var elevation = new float[targetHeight, targetWidth];

        // Find tile extent
        int minTileX = tiles.Min(t => t.coord.X);
        int maxTileX = tiles.Max(t => t.coord.X);
        int minTileY = tiles.Min(t => t.coord.Y);
        int maxTileY = tiles.Max(t => t.coord.Y);

        int tilesWide = maxTileX - minTileX + 1;
        int tilesHigh = maxTileY - minTileY + 1;
        int mosaicWidth = tilesWide * 256;
        int mosaicHeight = tilesHigh * 256;

        // Create mosaic array
        var mosaic = new float[mosaicHeight, mosaicWidth];

        // Process each tile
        foreach (var (coord, data) in tiles)
        {
            int offsetX = (coord.X - minTileX) * 256;
            int offsetY = (coord.Y - minTileY) * 256;

            // Convert hillshade to elevation estimate
            var tileElevation = HillshadeToElevation(data.ImageData);

            // Copy to mosaic
            for (int y = 0; y < 256 && y < tileElevation.GetLength(0); y++)
            {
                for (int x = 0; x < 256 && x < tileElevation.GetLength(1); x++)
                {
                    int mosaicY = offsetY + y;
                    int mosaicX = offsetX + x;

                    if (mosaicY < mosaicHeight && mosaicX < mosaicWidth)
                    {
                        mosaic[mosaicY, mosaicX] = tileElevation[y, x];
                    }
                }
            }
        }

        // Resample mosaic to target size and bounds
        var resampled = ResampleElevation(mosaic, bounds, targetWidth, targetHeight,
            minTileX, minTileY, tilesWide, tilesHigh, zoomLevel);

        return resampled;
    }

    private float[,] HillshadeToElevation(byte[] imageData)
    {
        // Load image using StbImageSharp
        var image = ImageResult.FromMemory(imageData, ColorComponents.RedGreenBlue);
        int width = image.Width;
        int height = image.Height;
        var elevation = new float[height, width];

        // Hillshade uses grayscale intensity to represent elevation
        // This is an approximation - true elevation would require DEM data
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width + x) * 3; // RGB = 3 bytes per pixel
                byte r = image.Data[pixelIndex];
                byte g = image.Data[pixelIndex + 1];
                byte b = image.Data[pixelIndex + 2];

                // Convert RGB to grayscale intensity
                float intensity = (r * 0.299f + g * 0.587f + b * 0.114f) / 255f;

                // Estimate elevation from hillshade intensity
                // This is approximate - darker areas are generally lower elevation
                // We'll map 0-255 to 0-5000m elevation range
                elevation[y, x] = intensity * 5000f;
            }
        }

        return elevation;
    }

    private float[,] ResampleElevation(
        float[,] mosaic,
        BoundingBox targetBounds,
        int targetWidth,
        int targetHeight,
        int minTileX,
        int minTileY,
        int tilesWide,
        int tilesHigh,
        int zoomLevel)
    {
        var resampled = new float[targetHeight, targetWidth];

        // Get tile bounds in world coordinates
        var tileTL = _basemapManager.TileToLatLon(minTileX, minTileY, zoomLevel);
        var tileBR = _basemapManager.TileToLatLon(minTileX + tilesWide, minTileY + tilesHigh, zoomLevel);

        var tileBounds = new BoundingBox
        {
            Min = new Vector2(tileTL.X, tileBR.Y),
            Max = new Vector2(tileBR.X, tileTL.Y)
        };

        int mosaicHeight = mosaic.GetLength(0);
        int mosaicWidth = mosaic.GetLength(1);

        // Resample using bilinear interpolation
        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                // Map target pixel to world coordinates
                float worldX = targetBounds.Min.X + (x / (float)targetWidth) * (targetBounds.Max.X - targetBounds.Min.X);
                float worldY = targetBounds.Min.Y + (y / (float)targetHeight) * (targetBounds.Max.Y - targetBounds.Min.Y);

                // Map world coordinates to mosaic coordinates
                float mosaicXf = ((worldX - tileBounds.Min.X) / (tileBounds.Max.X - tileBounds.Min.X)) * mosaicWidth;
                float mosaicYf = ((worldY - tileBounds.Min.Y) / (tileBounds.Max.Y - tileBounds.Min.Y)) * mosaicHeight;

                // Bilinear interpolation
                int mx = (int)Math.Floor(mosaicXf);
                int my = (int)Math.Floor(mosaicYf);
                float fx = mosaicXf - mx;
                float fy = mosaicYf - my;

                if (mx >= 0 && mx < mosaicWidth - 1 && my >= 0 && my < mosaicHeight - 1)
                {
                    float v00 = mosaic[my, mx];
                    float v10 = mosaic[my, mx + 1];
                    float v01 = mosaic[my + 1, mx];
                    float v11 = mosaic[my + 1, mx + 1];

                    float v0 = v00 * (1 - fx) + v10 * fx;
                    float v1 = v01 * (1 - fx) + v11 * fx;
                    resampled[y, x] = v0 * (1 - fy) + v1 * fy;
                }
                else if (mx >= 0 && mx < mosaicWidth && my >= 0 && my < mosaicHeight)
                {
                    resampled[y, x] = mosaic[my, mx];
                }
            }
        }

        return resampled;
    }

    /// <summary>
    /// Extract elevation for a specific region
    /// </summary>
    public async Task<GISRasterLayer> ExtractElevationForRegion(
        Vector2 center,
        float radiusKm,
        int resolution = 1000)
    {
        // Calculate bounding box from center and radius
        // Approximate: 1 degree ≈ 111 km at equator
        float radiusDeg = radiusKm / 111f;

        var bounds = new BoundingBox
        {
            Min = new Vector2(center.X - radiusDeg, center.Y - radiusDeg),
            Max = new Vector2(center.X + radiusDeg, center.Y + radiusDeg)
        };

        // Choose zoom level based on desired resolution
        // Higher resolution = higher zoom level
        int zoomLevel = Math.Min(13, Math.Max(8, 15 - (int)Math.Log2(radiusKm)));

        return await ExtractElevationFromBasemap(bounds, resolution, resolution, zoomLevel);
    }

    /// <summary>
    /// Create synthetic DEM for testing (if no elevation data available)
    /// </summary>
    public static GISRasterLayer CreateSyntheticDEM(
        BoundingBox bounds,
        int width,
        int height,
        string terrainType = "hills")
    {
        var elevation = new float[height, width];
        var random = new Random(42);

        switch (terrainType.ToLower())
        {
            case "flat":
                // Flat terrain with small random variations
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        elevation[y, x] = 100f + (float)random.NextDouble() * 10f;
                break;

            case "hills":
                // Rolling hills using Perlin-like noise
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float nx = x / (float)width;
                        float ny = y / (float)height;

                        // Multi-octave noise
                        float e = 0.5f * SimplexNoise(nx * 4, ny * 4, random)
                                + 0.25f * SimplexNoise(nx * 8, ny * 8, random)
                                + 0.125f * SimplexNoise(nx * 16, ny * 16, random);

                        elevation[y, x] = 200f + e * 300f;
                    }
                }
                break;

            case "mountain":
                // Mountainous terrain
                float centerX = width / 2f;
                float centerY = height / 2f;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float dx = (x - centerX) / centerX;
                        float dy = (y - centerY) / centerY;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                        // Mountain peak in center, slopes outward
                        float baseHeight = Math.Max(0, 1000f * (1f - dist));
                        float noise = (float)random.NextDouble() * 100f;

                        elevation[y, x] = baseHeight + noise;
                    }
                }
                break;

            case "valley":
                // Valley with ridge on edges
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float nx = x / (float)width;
                        float ny = y / (float)height;

                        // V-shaped valley
                        float valley = Math.Abs(nx - 0.5f) * 500f;
                        float noise = SimplexNoise(nx * 10, ny * 10, random) * 50f;

                        elevation[y, x] = valley + noise + 100f;
                    }
                }
                break;
        }

        var layer = new GISRasterLayer(elevation, bounds)
        {
            Name = $"Synthetic DEM ({terrainType})",
            IsVisible = true
        };

        return layer;
    }

    private static float SimplexNoise(float x, float y, Random random)
    {
        // Simple pseudo-random noise function
        // In production, use a proper Simplex/Perlin noise library
        int ix = (int)Math.Floor(x);
        int iy = (int)Math.Floor(y);
        float fx = x - ix;
        float fy = y - iy;

        // Hash-based random values
        float v00 = Hash(ix, iy, random);
        float v10 = Hash(ix + 1, iy, random);
        float v01 = Hash(ix, iy + 1, random);
        float v11 = Hash(ix + 1, iy + 1, random);

        // Bilinear interpolation
        float v0 = Lerp(v00, v10, Smooth(fx));
        float v1 = Lerp(v01, v11, Smooth(fx));
        return Lerp(v0, v1, Smooth(fy));
    }

    private static float Hash(int x, int y, Random random)
    {
        int seed = x * 374761393 + y * 668265263;
        return ((seed * seed * seed * 60493) % 1000000) / 1000000f * 2f - 1f;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }
}
