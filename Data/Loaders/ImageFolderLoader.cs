// GeoscientistToolkit/Data/Loaders/ImageFolderLoader.cs

using GeoscientistToolkit.Data;
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
        if (string.IsNullOrEmpty(FolderPath) || !Directory.Exists(FolderPath))
            throw new InvalidOperationException("Folder path is invalid or does not exist");

        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.05f, "Scanning folder for images..."));

                var files = Directory.GetFiles(FolderPath)
                    .Where(ImageLoader.IsSupportedImageFile)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (files.Length == 0)
                    throw new InvalidOperationException("No supported image files found in the selected folder");

                var datasets = new List<Dataset>();
                for (int i = 0; i < files.Length; i++)
                {
                    var file = files[i];
                    var name = Path.GetFileNameWithoutExtension(file);

                    progressReporter?.Report((0.1f + 0.8f * (i / (float)files.Length), $"Reading {name}..."));

                    var info = ImageLoader.LoadImageInfo(file);
                    var dataset = new ImageDataset(name, file)
                    {
                        Width = info.Width,
                        Height = info.Height,
                        BitDepth = info.BitsPerChannel * info.Channels
                    };

                    PopulateGPSMetadata(dataset, file);
                    datasets.Add(dataset);
                }

                progressReporter?.Report((0.95f, "Creating dataset group..."));
                var groupName = Path.GetFileName(FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var group = new DatasetGroup(string.IsNullOrEmpty(groupName) ? "Image Folder" : groupName, datasets);

                progressReporter?.Report((1.0f, "Image folder imported successfully!"));
                return group;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ImageFolderLoader] Error importing image folder: {ex}");
                throw new Exception($"Failed to import image folder: {ex.Message}", ex);
            }
        });
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
