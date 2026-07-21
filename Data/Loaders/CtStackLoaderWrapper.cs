// GAIA/Data/Loaders/CTStackLoaderWrapper.cs

using GAIA.Data.CtImageStack;
using GAIA.Util;

namespace GAIA.Data.Loaders;

public class CTStackLoaderWrapper : IDataLoader
{
    public enum LoadMode
    {
        OptimizedFor3D,
        LegacyFor2D
    }

    public LoadMode Mode { get; set; } = LoadMode.OptimizedFor3D;

    private string _sourcePath = "";
    private bool _isMultiPageTiff;
    private volatile bool _scanning;
    private volatile StackInfo _cachedInfo = new();
    private readonly object _scanLock = new();

    /// <summary>
    ///     Setting the source (or toggling multi-page TIFF) kicks a background folder scan. The
    ///     scan enumerates every slice and stats its size, which on a slow or network/NTFS drive
    ///     takes seconds; doing it on the UI thread (as the validation/preview did every frame)
    ///     froze the whole application. The cached result is served instantly instead.
    /// </summary>
    public string SourcePath
    {
        get => _sourcePath;
        set { if (_sourcePath != value) { _sourcePath = value; RequestScan(); } }
    }

    public bool IsMultiPageTiff
    {
        get => _isMultiPageTiff;
        set { if (_isMultiPageTiff != value) { _isMultiPageTiff = value; RequestScan(); } }
    }

    /// <summary>True while the background scan of the current selection is still running.</summary>
    public bool IsScanning => _scanning;

    public float PixelSize { get; set; } = 1.0f;
    public PixelSizeUnit Unit { get; set; } = PixelSizeUnit.Micrometers;
    public int BinningFactor { get; set; } = 1;

    // For returning both datasets when in optimized mode
    public CtImageStackDataset LegacyDataset { get; private set; }
    public StreamingCtVolumeDataset StreamingDataset { get; private set; }

    public string Name => Mode == LoadMode.OptimizedFor3D
        ? "CT Image Stack (Optimized for 3D Streaming)"
        : "CT Image Stack (Legacy for 2D Editing)";

    public string Description => Mode == LoadMode.OptimizedFor3D
        ? "Import CT stack optimized for 3D viewing with streaming capabilities"
        : "Import CT stack for 2D editing and segmentation";

    public bool CanImport
    {
        get
        {
            if (string.IsNullOrEmpty(SourcePath) || _scanning) return false;
            return _cachedInfo.SliceCount > 0;
        }
    }

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(SourcePath))
                return "Please select a source for the CT stack";
            if (_scanning)
                return IsMultiPageTiff ? "Scanning TIFF..." : "Scanning folder...";

            var info = _cachedInfo;
            if (info.SliceCount == 0)
                return IsMultiPageTiff
                    ? "Selected TIFF is missing or not a multi-page stack. CT stacks require multiple pages."
                    : "No supported image files found in this folder";

            return null;
        }
    }

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        if (Mode == LoadMode.OptimizedFor3D)
        {
            await LoadOptimizedStackAsync(progressReporter);
            return StreamingDataset; // Return the main dataset
        }

        return await LoadLegacyStackAsync(progressReporter);
    }

    public void Reset()
    {
        _sourcePath = "";
        _isMultiPageTiff = false;
        _scanning = false;
        _cachedInfo = new StackInfo();
        PixelSize = 1.0f;
        Unit = PixelSizeUnit.Micrometers;
        BinningFactor = 1;
        LegacyDataset = null;
        StreamingDataset = null;
    }

    /// <summary>Returns the most recent completed scan of the selection; never touches disk.</summary>
    public StackInfo GetStackInfo() => _cachedInfo;

    /// <summary>Rescans the current selection off the UI thread and publishes the result to the
    /// cache. Only the newest request wins, so rapid selection changes never show stale counts.</summary>
    private void RequestScan()
    {
        var path = _sourcePath;
        var tiff = _isMultiPageTiff;
        if (string.IsNullOrEmpty(path))
        {
            _cachedInfo = new StackInfo();
            _scanning = false;
            return;
        }

        _scanning = true;
        lock (_scanLock)
        {
            Task.Run(() =>
            {
                var info = ScanStackInfo(path, tiff);
                if (path == _sourcePath && tiff == _isMultiPageTiff) _cachedInfo = info;
            }).ContinueWith(_ =>
            {
                if (path == _sourcePath && tiff == _isMultiPageTiff) _scanning = false;
            });
        }
    }

    private static StackInfo ScanStackInfo(string path, bool isMultiPageTiff)
    {
        try
        {
            if (isMultiPageTiff && File.Exists(path))
            {
                if (ImageLoader.IsMultiPageTiff(path))
                {
                    var pageCount = ImageLoader.GetTiffPageCount(path);
                    var imageInfo = ImageLoader.LoadImageInfo(path);
                    var fileSize = new FileInfo(path).Length;

                    return new StackInfo
                    {
                        SliceCount = pageCount,
                        Width = imageInfo.Width,
                        Height = imageInfo.Height,
                        TotalSize = fileSize,
                        FileName = Path.GetFileName(path)
                    };
                }
            }
            else if (!isMultiPageTiff && Directory.Exists(path))
            {
                var files = Directory.GetFiles(path)
                    .Where(ImageLoader.IsSupportedImageFile)
                    .ToArray();

                return new StackInfo
                {
                    SliceCount = files.Length,
                    TotalSize = files.Sum(f => new FileInfo(f).Length),
                    FileName = Path.GetFileName(path)
                };
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CTStackLoader] Error getting stack info: {ex.Message}");
        }

        return new StackInfo();
    }

    private async Task LoadOptimizedStackAsync(IProgress<(float progress, string message)> progressReporter)
    {
        var name = IsMultiPageTiff ? Path.GetFileNameWithoutExtension(SourcePath) : Path.GetFileName(SourcePath);

        var outputDir = IsMultiPageTiff ? Path.GetDirectoryName(SourcePath) : SourcePath;

        var gvtFileName = BinningFactor > 1 ? $"{name}_bin{BinningFactor}.gvt" : $"{name}.gvt";

        var gvtPath = Path.Combine(outputDir, gvtFileName);

        // Step 1: Load volume data
        progressReporter?.Report((0.0f, "Loading volume data..."));

        var loadProgress = new Progress<float>(p =>
            progressReporter?.Report((p * 0.5f, $"Step 1/2: Loading and processing images... {(int)(p * 100)}%")));

        var binnedVolume = await CTStackLoader.LoadCTStackAsync(
            SourcePath,
            GetPixelSizeInMeters(),
            BinningFactor,
            false,
            loadProgress,
            name);

        if (binnedVolume == null || binnedVolume.Width == 0 || binnedVolume.Height == 0 || binnedVolume.Depth == 0)
            throw new InvalidOperationException("Failed to load volume data");

        Logger.Log($"[CTStackLoader] Loaded volume: {binnedVolume.Width}×{binnedVolume.Height}×{binnedVolume.Depth}");

        // Step 2: Convert to optimized format if needed
        if (!File.Exists(gvtPath))
        {
            progressReporter?.Report((0.5f, "Converting to optimized format..."));

            await CtStackConverter.ConvertToStreamableFormat(
                binnedVolume,
                gvtPath,
                (p, s) => progressReporter?.Report((0.5f + p * 0.5f, $"Step 2/2: {s}")));
        }
        else
        {
            progressReporter?.Report((1.0f, "Found existing optimized file. Loading..."));
        }

        // Create legacy dataset for editing
        double pixelSizeMicrons = (Unit == PixelSizeUnit.Micrometers ? PixelSize : PixelSize * 1000) * BinningFactor;

        LegacyDataset = new CtImageStackDataset($"{name} (2D Edit & Segment)", SourcePath)
        {
            Width = binnedVolume.Width,
            Height = binnedVolume.Height,
            Depth = binnedVolume.Depth,
            PixelSize = (float)pixelSizeMicrons,
            SliceThickness = (float)pixelSizeMicrons,
            Unit = "µm",
            BinningSize = BinningFactor
        };

        // Create streaming dataset for 3D viewing
        StreamingDataset = new StreamingCtVolumeDataset($"{name} (3D View)", gvtPath)
        {
            EditablePartner = LegacyDataset
        };
        // Populate dimensions and the LOD table now, while leaving all voxel payloads unloaded.
        // The dataset list can then show correct information before the viewer is opened.
        StreamingDataset.LoadMetadata();

        progressReporter?.Report((1.0f, "Optimized dataset and editable partner added to project!"));
    }

    private async Task<Dataset> LoadLegacyStackAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(async () =>
        {
            var name = IsMultiPageTiff ? Path.GetFileNameWithoutExtension(SourcePath) : Path.GetFileName(SourcePath);

            var loadProgress = new Progress<float>(p =>
                progressReporter?.Report((p, $"Processing images... {(int)(p * 100)}%")));

            var volume = await CTStackLoader.LoadCTStackAsync(
                SourcePath,
                GetPixelSizeInMeters(),
                BinningFactor,
                false,
                loadProgress,
                name);

            double pixelSizeMicrons =
                (Unit == PixelSizeUnit.Micrometers ? PixelSize : PixelSize * 1000) * BinningFactor;

            var dataset = new CtImageStackDataset($"{name} (2D Edit & Segment)", SourcePath)
            {
                Width = volume.Width,
                Height = volume.Height,
                Depth = volume.Depth,
                PixelSize = (float)pixelSizeMicrons,
                SliceThickness = (float)pixelSizeMicrons,
                Unit = "µm",
                BinningSize = BinningFactor
            };

            progressReporter?.Report((1.0f, "Legacy dataset added to project!"));

            return dataset;
        });
    }

    private double GetPixelSizeInMeters()
    {
        return Unit == PixelSizeUnit.Micrometers
            ? PixelSize * 1e-6
            : // micrometers to meters
            PixelSize * 1e-3; // millimeters to meters
    }

    public class StackInfo
    {
        public int SliceCount { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long TotalSize { get; set; }
        public string FileName { get; set; }
    }
}
