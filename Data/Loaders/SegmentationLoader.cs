// GeoscientistToolkit/Data/Loaders/SegmentationLoader.cs

using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;
// <-- Dataset base type (namespace per Dataset.cs)
// ImageDataset

// Logger

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
///     Loader for standalone segmentation/label images without background.
///     Accepts a single label image (e.g., PNG/TIF) and optional *.materials.json sidecar.
/// </summary>
public class SegmentationLoader : IDataLoader
{
    // === Helpers ===

    private static readonly string[] _allowedExtensions =
    {
        ".png", ".tif", ".tiff", ".jpg", ".jpeg", ".bmp"
    };

    /// <summary>Full path to the segmentation (label) image.</summary>
    public string SegmentationPath { get; set; }

    /// <summary>Optional dataset name. If null/empty, derived from file name.</summary>
    public string DatasetName { get; set; }

    // === IDataLoader ===
    public string Name => "Segmentation Loader";

    public string Description =>
        "Imports a single labeled segmentation image and (if present) a companion .materials.json file.";

    public string ValidationMessage => ValidateInternal();

    public bool CanImport => ValidateInternal() == null;

    public Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        // IMPORTANT: force Task<T> to be Task<Dataset> (not Task<ImageDataset>)
        return Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.05f, "Validating selection..."));
                var validation = ValidateInternal();
                if (validation != null)
                    throw new InvalidOperationException(validation);

                progressReporter?.Report((0.10f, "Loading segmentation file..."));

                // Determine dataset name
                var name = !string.IsNullOrEmpty(DatasetName)
                    ? DatasetName
                    : Path.GetFileNameWithoutExtension(SegmentationPath);

                progressReporter?.Report((0.35f, "Creating segmentation dataset..."));

                // Create the segmentation dataset (returns ImageDataset)
                var dataset = ImageDataset.CreateSegmentationDataset(name, SegmentationPath);

                progressReporter?.Report((0.70f, "Checking materials file..."));

                // Check for materials file (optional)
                var materialsPath = Path.ChangeExtension(SegmentationPath, ".materials.json");
                if (File.Exists(materialsPath))
                    Logger.Log($"[SegmentationLoader] Found materials file: {materialsPath}");

                progressReporter?.Report((1.0f, "Segmentation loaded successfully."));
                Logger.Log($"[SegmentationLoader] Successfully loaded segmentation: {name}");

                // Upcast to Dataset so Task.Run produces Task<Dataset>
                return (Dataset)dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SegmentationLoader] Failed to load segmentation: {ex.Message}");
                throw;
            }
        });
    }

    public void Reset()
    {
        SegmentationPath = null;
        DatasetName = null;
    }

    /// <summary>
    ///     Returns null if valid; otherwise an error message describing why import cannot proceed.
    /// </summary>
    private string ValidateInternal()
    {
        if (string.IsNullOrWhiteSpace(SegmentationPath))
            return "No segmentation file selected.";

        if (!File.Exists(SegmentationPath))
            return $"File not found: {SegmentationPath}";

        var ext = Path.GetExtension(SegmentationPath).ToLowerInvariant();
        var okExt = false;
        foreach (var e in _allowedExtensions)
            if (ext == e)
            {
                okExt = true;
                break;
            }

        if (!okExt)
            return $"Unsupported file extension '{ext}'. Supported: {string.Join(", ", _allowedExtensions)}";

        try
        {
            var info = ImageLoader.LoadImageInfo(SegmentationPath);
            if (info == null || info.Width <= 0 || info.Height <= 0)
                return "Unable to read image size; the file may be corrupted or unsupported.";
        }
        catch (Exception ex)
        {
            return $"Failed to read image info: {ex.Message}";
        }

        return null; // Valid
    }

    public SegmentationInfo GetSegmentationInfo()
    {
        if (!CanImport) return null;

        try
        {
            var fileInfo = new FileInfo(SegmentationPath);
            var imageInfo = ImageLoader.LoadImageInfo(SegmentationPath);
            var materialsPath = Path.ChangeExtension(SegmentationPath, ".materials.json");

            return new SegmentationInfo
            {
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                Width = imageInfo?.Width ?? 0,
                Height = imageInfo?.Height ?? 0,
                HasMaterialsFile = File.Exists(materialsPath)
            };
        }
        catch (Exception ex)
        {
            Logger.LogError($"[SegmentationLoader] Error getting segmentation info: {ex.Message}");
            return null;
        }
    }

    // Optional: lightweight info snapshot usable by UIs
    public class SegmentationInfo
    {
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool HasMaterialsFile { get; set; }
    }
}