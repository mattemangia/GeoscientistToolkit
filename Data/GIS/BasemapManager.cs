// GeoscientistToolkit/UI/GIS/BasemapManager.cs

using System.Numerics;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;
using OSGeo.GDAL;
using Veldrid;

namespace GeoscientistToolkit.UI.GIS;

public class BasemapManager
{
    private static BasemapManager _instance;

    // Popular free basemap providers
    public static readonly List<BasemapProvider> Providers = new()
    {
        new BasemapProvider
        {
            Name = "OpenStreetMap",
            Id = "osm",
            UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
            Attribution = "© OpenStreetMap contributors",
            MaxZoom = 19,
            TileSize = 256
        },
        new BasemapProvider
        {
            Name = "OpenTopoMap",
            Id = "opentopomap",
            UrlTemplate = "https://tile.opentopomap.org/{z}/{x}/{y}.png",
            Attribution = "© OpenTopoMap (CC-BY-SA)",
            MaxZoom = 17,
            TileSize = 256
        },
        new BasemapProvider
        {
            Name = "CartoDB Light",
            Id = "cartodb_light",
            UrlTemplate = "https://cartodb-basemaps-a.global.ssl.fastly.net/light_all/{z}/{x}/{y}.png",
            Attribution = "© CartoDB",
            MaxZoom = 19,
            TileSize = 256
        },
        new BasemapProvider
        {
            Name = "CartoDB Dark",
            Id = "cartodb_dark",
            UrlTemplate = "https://cartodb-basemaps-a.global.ssl.fastly.net/dark_all/{z}/{x}/{y}.png",
            Attribution = "© CartoDB",
            MaxZoom = 19,
            TileSize = 256
        },
        new BasemapProvider
        {
            Name = "ESRI World Imagery",
            Id = "esri_imagery",
            UrlTemplate =
                "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
            Attribution = "© Esri",
            MaxZoom = 19,
            TileSize = 256
        },
        new BasemapProvider
        {
            Name = "ESRI World Street Map",
            Id = "esri_street",
            UrlTemplate =
                "https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}",
            Attribution = "© Esri",
            MaxZoom = 19,
            TileSize = 256
        },
        new BasemapProvider
        {
            Name = "ESRI World Topo",
            Id = "esri_topo",
            UrlTemplate =
                "https://server.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{y}/{x}",
            Attribution = "© Esri",
            MaxZoom = 19,
            TileSize = 256
        },
        new BasemapProvider
        {
            Name = "Stamen Terrain",
            Id = "stamen_terrain",
            UrlTemplate = "https://stamen-tiles.a.ssl.fastly.net/terrain/{z}/{x}/{y}.png",
            Attribution = "© Stamen Design",
            MaxZoom = 18,
            TileSize = 256
        },
        new BasemapProvider
        {
            Name = "Stamen Watercolor",
            Id = "stamen_watercolor",
            UrlTemplate = "https://stamen-tiles.a.ssl.fastly.net/watercolor/{z}/{x}/{y}.jpg",
            Attribution = "© Stamen Design",
            MaxZoom = 18,
            TileSize = 256
        },
        new BasemapProvider
        {
            Name = "OpenWeatherMap Clouds",
            Id = "owm_clouds",
            UrlTemplate = "https://tile.openweathermap.org/map/clouds_new/{z}/{x}/{y}.png?appid={apikey}",
            Attribution = "© OpenWeatherMap",
            MaxZoom = 19,
            TileSize = 256,
            RequiresApiKey = true
        }
    };

    private readonly string _cacheDirectory;

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, Texture> _textureCache = new();
    private readonly Dictionary<string, TileCache> _tileCaches = new();
    private GraphicsDevice _graphicsDevice;
    private bool _isOnline = true;

    private BasemapManager()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GeoscientistToolkit/1.0");

        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeoscientistToolkit", "TileCache");

        Directory.CreateDirectory(_cacheDirectory);

        // Initialize GDAL
        Gdal.AllRegister();

        // Default to OpenStreetMap
        CurrentProvider = Providers[0];

        // Check internet connectivity
        Task.Run(CheckConnectivity);
    }

    public static BasemapManager Instance => _instance ??= new BasemapManager();

    public BasemapProvider CurrentProvider { get; set; }
    public string ApiKey { get; set; } // For providers that require API keys

    public void Initialize(GraphicsDevice device)
    {
        _graphicsDevice = device;
    }

    private async Task CheckConnectivity()
    {
        try
        {
            var response = await _httpClient.GetAsync("https://tile.openstreetmap.org/0/0/0.png");
            _isOnline = response.IsSuccessStatusCode;
        }
        catch
        {
            _isOnline = false;
        }
    }

    public async Task<TileData> GetTileAsync(int x, int y, int z)
    {
        if (CurrentProvider == null) return null;

        var cacheKey = $"{CurrentProvider.Id}_{z}_{x}_{y}";
        var cachePath = Path.Combine(_cacheDirectory, CurrentProvider.Id, $"{z}", $"{x}_{y}.png");

        // Check cache first
        if (File.Exists(cachePath))
            try
            {
                var data = await File.ReadAllBytesAsync(cachePath);
                return new TileData { X = x, Y = y, Z = z, ImageData = data };
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to load cached tile: {ex.Message}");
            }

        // Download if online
        if (_isOnline)
            try
            {
                var url = CurrentProvider.UrlTemplate
                    .Replace("{x}", x.ToString())
                    .Replace("{y}", y.ToString())
                    .Replace("{z}", z.ToString());

                if (CurrentProvider.RequiresApiKey && !string.IsNullOrEmpty(ApiKey))
                    url = url.Replace("{apikey}", ApiKey);

                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var data = await response.Content.ReadAsByteArrayAsync();

                    // Cache the tile
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                    await File.WriteAllBytesAsync(cachePath, data);

                    return new TileData { X = x, Y = y, Z = z, ImageData = data };
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to download tile: {ex.Message}");
            }

        return null;
    }

    public List<TileCoordinate> GetVisibleTiles(BoundingBox bounds, int zoomLevel)
    {
        var tiles = new List<TileCoordinate>();

        // Convert lat/lon bounds to tile coordinates
        var minTile = LatLonToTile(bounds.Min.Y, bounds.Min.X, zoomLevel);
        var maxTile = LatLonToTile(bounds.Max.Y, bounds.Max.X, zoomLevel);

        for (var x = minTile.X; x <= maxTile.X; x++)
        for (var y = maxTile.Y; y <= minTile.Y; y++)
            tiles.Add(new TileCoordinate { X = x, Y = y, Z = zoomLevel });

        return tiles;
    }

    private TileCoordinate LatLonToTile(double lat, double lon, int zoom)
    {
        var n = 1 << zoom;
        var x = (int)((lon + 180.0) / 360.0 * n);
        var y = (int)((1.0 - Math.Log(Math.Tan(lat * Math.PI / 180.0) +
                                      1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * n);

        return new TileCoordinate { X = x, Y = y, Z = zoom };
    }

    public Vector2 TileToLatLon(int x, int y, int z)
    {
        var n = Math.Pow(2.0, z);
        var lon = x / n * 360.0 - 180.0;
        var lat = Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / n))) * 180.0 / Math.PI;
        return new Vector2((float)lon, (float)lat);
    }

    // GeoTIFF support using GDAL
    public GeoTiffData LoadGeoTiff(string path)
    {
        if (!File.Exists(path))
        {
            Logger.LogError($"GeoTIFF file not found: {path}");
            return null;
        }

        try
        {
            using var dataset = Gdal.Open(path, Access.GA_ReadOnly);
            if (dataset == null)
            {
                Logger.LogError("Failed to open GeoTIFF");
                return null;
            }

            var geoTiff = new GeoTiffData
            {
                Width = dataset.RasterXSize,
                Height = dataset.RasterYSize,
                BandCount = dataset.RasterCount
            };

            // Get geotransform
            var transform = new double[6];
            dataset.GetGeoTransform(transform);

            geoTiff.OriginX = transform[0];
            geoTiff.OriginY = transform[3];
            geoTiff.PixelWidth = transform[1];
            geoTiff.PixelHeight = transform[5];

            // Get projection
            geoTiff.Projection = dataset.GetProjection();

            // Read raster data (simplified - reading first 3 bands for RGB)
            var bands = Math.Min(3, geoTiff.BandCount);
            geoTiff.Data = new byte[geoTiff.Width * geoTiff.Height * 4]; // RGBA

            for (var band = 1; band <= bands; band++)
            {
                using var rasterBand = dataset.GetRasterBand(band);
                var buffer = new byte[geoTiff.Width * geoTiff.Height];

                rasterBand.ReadRaster(0, 0, geoTiff.Width, geoTiff.Height,
                    buffer, geoTiff.Width, geoTiff.Height, 0, 0);

                // Copy to RGBA buffer
                for (var i = 0; i < buffer.Length; i++) geoTiff.Data[i * 4 + (band - 1)] = buffer[i];
            }

            // Set alpha channel
            for (var i = 0; i < geoTiff.Width * geoTiff.Height; i++) geoTiff.Data[i * 4 + 3] = 255;

            Logger.Log($"Loaded GeoTIFF: {geoTiff.Width}x{geoTiff.Height}, {geoTiff.BandCount} bands");
            return geoTiff;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load GeoTIFF: {ex.Message}");
            return null;
        }
    }

    public void ClearCache()
    {
        try
        {
            Directory.Delete(_cacheDirectory, true);
            Directory.CreateDirectory(_cacheDirectory);
            _tileCaches.Clear();
            _textureCache.Clear();
            Logger.Log("Tile cache cleared");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to clear cache: {ex.Message}");
        }
    }

    public long GetCacheSize()
    {
        try
        {
            var dir = new DirectoryInfo(_cacheDirectory);
            return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
}

public class BasemapProvider
{
    public string Name { get; set; }
    public string Id { get; set; }
    public string UrlTemplate { get; set; }
    public string Attribution { get; set; }
    public int MaxZoom { get; set; }
    public int TileSize { get; set; }
    public bool RequiresApiKey { get; set; }
}

public class TileCache
{
    public Dictionary<string, TileData> Tiles { get; } = new();
    public DateTime LastAccess { get; set; }
}

public class TileData
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public byte[] ImageData { get; set; }
    public IntPtr TextureId { get; set; }
}

public class TileCoordinate
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public class GeoTiffData
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int BandCount { get; set; }
    public byte[] Data { get; set; }
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    public double PixelWidth { get; set; }
    public double PixelHeight { get; set; }
    public string Projection { get; set; }
}