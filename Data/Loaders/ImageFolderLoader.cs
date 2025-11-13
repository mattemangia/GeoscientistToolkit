// GeoscientistToolkit/Data/Loaders/ImageFolderLoader.cs

using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

public class ImageFolderLoader : IDataLoader
{
    public string FolderPath { get; set; } = "";
    public bool RequiresOrganizer { get; } = true;
    public string Name => "Image Folder (Group)";
    public string Description => "Load multiple images from a folder and organize them into groups";

    public bool CanImport => !string.IsNullOrEmpty(FolderPath) && Directory.Exists(FolderPath);

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(FolderPath))
                return "Please select a folder containing images";
            if (!Directory.Exists(FolderPath))
                return "Selected folder does not exist";

            var info = GetFolderInfo();
            if (info.ImageCount == 0)
                return "No supported image files found in this folder";

            return null;
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        // This loader requires the ImageStackOrganizerDialog to be opened
        // The actual loading is handled by the organizer
        throw new NotImplementedException("ImageFolderLoader requires the ImageStackOrganizerDialog");
    }

    public void Reset()
    {
        FolderPath = "";
    }

    public FolderInfo GetFolderInfo()
    {
        if (!Directory.Exists(FolderPath))
            return new FolderInfo { ImageCount = 0, TotalSize = 0 };

        try
        {
            var files = Directory.GetFiles(FolderPath)
                .Where(ImageLoader.IsSupportedImageFile)
                .ToArray();

            return new FolderInfo
            {
                ImageCount = files.Length,
                TotalSize = files.Sum(f => new FileInfo(f).Length),
                FolderName = Path.GetFileName(FolderPath)
            };
        }
        catch
        {
            return new FolderInfo { ImageCount = 0, TotalSize = 0 };
        }
    }

    public class FolderInfo
    {
        public int ImageCount { get; set; }
        public long TotalSize { get; set; }
        public string FolderName { get; set; }
    }

    /// <summary>
    ///     Helper method to extract GPS metadata for a single image file.
    ///     This can be used by the ImageStackOrganizerDialog or other components
    ///     when creating ImageDataset instances from folder images.
    /// </summary>
    public static void PopulateGPSMetadata(ImageDataset dataset, string imagePath)
    {
        if (dataset == null || string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            return;

        var gpsMetadata = ImageLoader.ExtractGPSMetadata(imagePath);
        if (gpsMetadata.HasGPSData)
        {
            dataset.DatasetMetadata.Latitude = gpsMetadata.Latitude;
            dataset.DatasetMetadata.Longitude = gpsMetadata.Longitude;
            if (gpsMetadata.Altitude.HasValue)
            {
                dataset.DatasetMetadata.Elevation = gpsMetadata.Altitude;
            }
        }
    }
}