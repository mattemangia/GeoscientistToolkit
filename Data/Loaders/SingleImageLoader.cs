// GeoscientistToolkit/Data/Loaders/SingleImageLoader.cs

using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class SingleImageLoader : IDataLoader
{
    public string ImagePath { get; set; } = "";
    public float PixelSize { get; set; } = 1.0f;
    public PixelSizeUnit Unit { get; set; } = PixelSizeUnit.Micrometers;
    public string Name => "Single Image";

    public SingleImageLoader() { }

    public SingleImageLoader(string imagePath)
    {
        ImagePath = imagePath;
    }
    public string Description => "Import a single image file (PNG, JPG, TIFF, etc.)";

    public bool CanImport => !string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath);

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(ImagePath))
                return "Please select an image file";
            if (!File.Exists(ImagePath))
                return "Selected file does not exist";
            return null;
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Loading image..."));

                var fileName = Path.GetFileName(ImagePath);
                var dataset = new ImageDataset(Path.GetFileNameWithoutExtension(fileName), ImagePath);

                progressReporter?.Report((0.3f, "Reading image properties..."));

                var imageInfo = ImageLoader.LoadImageInfo(ImagePath);
                dataset.Width = imageInfo.Width;
                dataset.Height = imageInfo.Height;
                dataset.BitDepth = imageInfo.BitsPerChannel * imageInfo.Channels;

                if (PixelSize > 0)
                {
                    dataset.PixelSize = Unit == PixelSizeUnit.Micrometers ? PixelSize : PixelSize * 1000;
                    dataset.Unit = "µm";
                }

                progressReporter?.Report((0.6f, "Extracting EXIF metadata..."));

                // Extract GPS metadata from EXIF if available
                var gpsMetadata = ImageLoader.ExtractGPSMetadata(ImagePath);
                if (gpsMetadata.HasGPSData)
                {
                    dataset.DatasetMetadata.Latitude = gpsMetadata.Latitude;
                    dataset.DatasetMetadata.Longitude = gpsMetadata.Longitude;
                    if (gpsMetadata.Altitude.HasValue)
                    {
                        dataset.DatasetMetadata.Elevation = gpsMetadata.Altitude;
                    }
                    
                    Logger.Log($"[SingleImageLoader] Applied GPS coordinates to dataset: " +
                              $"Lat={gpsMetadata.Latitude:F6}, Lon={gpsMetadata.Longitude:F6}" +
                              (gpsMetadata.Altitude.HasValue ? $", Elevation={gpsMetadata.Altitude:F2}m" : ""));
                }

                progressReporter?.Report((1.0f, "Single image imported successfully!"));

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SingleImageLoader] Error importing image: {ex}");
                throw new Exception($"Failed to import image: {ex.Message}", ex);
            }
        });
    }

    public void Reset()
    {
        ImagePath = "";
        PixelSize = 1.0f;
        Unit = PixelSizeUnit.Micrometers;
    }

    public ImageInfo GetImageInfo()
    {
        if (!CanImport) return null;

        try
        {
            var info = ImageLoader.LoadImageInfo(ImagePath);
            return new ImageInfo
            {
                Width = info.Width,
                Height = info.Height,
                BitDepth = info.BitsPerChannel * info.Channels,
                FileSize = new FileInfo(ImagePath).Length,
                FileName = Path.GetFileName(ImagePath)
            };
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SingleImageLoader] Error reading image info: {ex.Message}");
            return null;
        }
    }

    public class ImageInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int BitDepth { get; set; }
        public long FileSize { get; set; }
        public string FileName { get; set; }
    }
}

public enum PixelSizeUnit
{
    Micrometers,
    Millimeters
}