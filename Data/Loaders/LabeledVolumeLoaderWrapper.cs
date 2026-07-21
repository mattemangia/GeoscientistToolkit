// GAIA/Data/Loaders/LabeledVolumeLoaderWrapper.cs

using GAIA.Data.CtImageStack;
using GAIA.Util;

namespace GAIA.Data.Loaders;

public class LabeledVolumeLoaderWrapper : IDataLoader, IAsyncScanningLoader
{
    private readonly AsyncScanCache<VolumeInfo> _scan = new();
    public string SourcePath { get; set; } = "";
    public bool IsMultiPageTiff { get; set; }
    public float PixelSize { get; set; } = 1.0f;
    public PixelSizeUnit Unit { get; set; } = PixelSizeUnit.Micrometers;
    public string Name => "Labeled Volume Stack (Color-coded Materials)";

    public string Description =>
        "Import a stack of labeled images where each unique color represents a different material";

    private string ScanKey => string.IsNullOrEmpty(SourcePath) ? "" : $"{SourcePath}|{IsMultiPageTiff}";

    /// <summary>True while the selection is being scanned on a background thread.</summary>
    public bool IsScanning => _scan.IsScanning;

    public bool CanImport
    {
        get
        {
            var info = _scan.Get(ScanKey, ScanVolume);
            return !_scan.IsScanning && info.SliceCount > 0;
        }
    }

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(SourcePath))
                return "Please select a source for the labeled volume";
            var info = _scan.Get(ScanKey, ScanVolume);
            if (_scan.IsScanning)
                return IsMultiPageTiff ? "Scanning TIFF..." : "Scanning folder...";
            if (info.SliceCount == 0)
                return IsMultiPageTiff
                    ? "Selected TIFF is missing or not a multi-page stack."
                    : "No supported image files found in this folder";

            return null;
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(async () =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Loading labeled volume..."));

                var name = IsMultiPageTiff
                    ? Path.GetFileNameWithoutExtension(SourcePath)
                    : Path.GetFileName(SourcePath);

                var pixelSizeMeters = Unit == PixelSizeUnit.Micrometers
                    ? PixelSize * 1e-6
                    : // micrometers to meters
                    PixelSize * 1e-3; // millimeters to meters

                // Load the labeled volume
                var loadProgress = new Progress<float>(p =>
                    progressReporter?.Report((p, $"Processing labeled images... {(int)(p * 100)}%")));

                var (grayscaleVolume, labelVolume, materials) = await LabeledVolumeLoader.LoadLabeledVolumeAsync(
                    SourcePath,
                    pixelSizeMeters,
                    false, // Don't use memory mapping for now
                    loadProgress,
                    name);

                progressReporter?.Report((0.9f, "Creating dataset..."));

                // Create the dataset
                double pixelSizeMicrons = Unit == PixelSizeUnit.Micrometers ? PixelSize : PixelSize * 1000;

                var dataset = new CtImageStackDataset($"{name} (Labeled)", SourcePath)
                {
                    Width = grayscaleVolume.Width,
                    Height = grayscaleVolume.Height,
                    Depth = grayscaleVolume.Depth,
                    PixelSize = (float)pixelSizeMicrons,
                    SliceThickness = (float)pixelSizeMicrons,
                    Unit = "µm",
                    BinningSize = 1,
                    Materials = materials
                };

                // Save materials to file so they persist
                dataset.SaveMaterials();

                progressReporter?.Report((1.0f,
                    $"Labeled volume imported successfully! Found {materials.Count} unique materials."));

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[LabeledVolumeLoader] Error importing volume: {ex}");
                throw new Exception($"Failed to import labeled volume: {ex.Message}", ex);
            }
        });
    }

    public void Reset()
    {
        SourcePath = "";
        IsMultiPageTiff = false;
        PixelSize = 1.0f;
        Unit = PixelSizeUnit.Micrometers;
    }

    /// <summary>Returns the latest completed background scan; never touches disk on this thread.</summary>
    public VolumeInfo GetVolumeInfo() => _scan.Get(ScanKey, ScanVolume);

    private VolumeInfo ScanVolume()
    {
        try
        {
            if (IsMultiPageTiff && File.Exists(SourcePath))
            {
                if (ImageLoader.IsMultiPageTiff(SourcePath))
                {
                    var pageCount = ImageLoader.GetTiffPageCount(SourcePath);
                    var imageInfo = ImageLoader.LoadImageInfo(SourcePath);
                    var fileSize = new FileInfo(SourcePath).Length;

                    return new VolumeInfo
                    {
                        SliceCount = pageCount,
                        Width = imageInfo.Width,
                        Height = imageInfo.Height,
                        TotalSize = fileSize,
                        FileName = Path.GetFileName(SourcePath),
                        IsReady = true
                    };
                }
            }
            else if (!IsMultiPageTiff && Directory.Exists(SourcePath))
            {
                var files = Directory.GetFiles(SourcePath)
                    .Where(ImageLoader.IsSupportedImageFile)
                    .ToArray();

                return new VolumeInfo
                {
                    SliceCount = files.Length,
                    TotalSize = files.Sum(f => new FileInfo(f).Length),
                    FileName = Path.GetFileName(SourcePath),
                    IsReady = files.Length > 0
                };
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[LabeledVolumeLoader] Error getting volume info: {ex.Message}");
        }

        return new VolumeInfo();
    }

    public class VolumeInfo
    {
        public int SliceCount { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long TotalSize { get; set; }
        public string FileName { get; set; }
        public bool IsReady { get; set; }
    }
}