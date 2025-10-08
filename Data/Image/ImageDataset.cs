// GeoscientistToolkit/Data/Image/ImageDataset.cs (Updated)

using System.Numerics;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

// Added for Linq

namespace GeoscientistToolkit.Data.Image;

public class ImageDataset : Dataset, IDisposable, ISerializableDataset
{
    private string _segmentationPath;

    public ImageDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.SingleImage;
        Unit = "µm"; // Default unit
    }

    public int Width { get; set; }
    public int Height { get; set; }
    public int BitDepth { get; set; }
    public float PixelSize { get; set; } // In micrometers
    public string Unit { get; set; }

    // Tag system
    public ImageTag Tags { get; set; } = ImageTag.None;
    public Dictionary<string, object> ImageMetadata { get; set; } = new();

    public byte[] ImageData { get; private set; }

    // FIX: Added a shared property to control segmentation visibility across UI panels
    public bool ShowSegmentationOverlay { get; set; } = true;

    // Segmentation Integration
    public ImageSegmentationData Segmentation { get; private set; }
    public bool HasSegmentation => Segmentation != null;

    // Histogram
    public float[] HistogramLuminance { get; private set; }
    public float[] HistogramR { get; private set; }
    public float[] HistogramG { get; private set; }
    public float[] HistogramB { get; private set; }

    public void Dispose()
    {
        Unload();
        ClearSegmentation();
    }

    public object ToSerializableObject()
    {
        return new ImageDatasetDTO
        {
            TypeName = nameof(ImageDataset),
            Name = Name,
            FilePath = FilePath,
            PixelSize = PixelSize,
            Unit = Unit,
            SegmentationPath = _segmentationPath,
            Tags = (long)Tags,
            ImageMetadata = new Dictionary<string, string>(
                ImageMetadata.Select(kvp => new KeyValuePair<string, string>(
                    kvp.Key, kvp.Value?.ToString() ?? "")))
        };
    }

    public void AddTag(ImageTag tag)
    {
        Tags |= tag;
    }

    public void RemoveTag(ImageTag tag)
    {
        Tags &= ~tag;
    }

    public bool HasTag(ImageTag tag)
    {
        return Tags.HasFlag(tag);
    }

    public void SetCalibration(float pixelSize, string unit)
    {
        PixelSize = pixelSize;
        Unit = unit;
        AddTag(ImageTag.Calibrated);
    }

    public override long GetSizeInBytes()
    {
        long size = 0;
        if (File.Exists(FilePath))
            size += new FileInfo(FilePath).Length;

        if (!string.IsNullOrEmpty(_segmentationPath) && File.Exists(_segmentationPath))
            size += new FileInfo(_segmentationPath).Length;

        return size;
    }

    public override void Load()
    {
        if (ImageData != null) return;

        var imageInfo = ImageLoader.LoadImage(FilePath);
        if (imageInfo != null)
        {
            ImageData = imageInfo.Data;
            Width = imageInfo.Width;
            Height = imageInfo.Height;

            CalculateHistograms();
            LoadSegmentation();
        }
    }

    public override void Unload()
    {
        if (SettingsManager.Instance.Settings.Performance.EnableLazyLoading)
        {
            ImageData = null;
            HistogramLuminance = null;
            HistogramR = null;
            HistogramG = null;
            HistogramB = null;
            GC.Collect();
        }
    }

    public ImageSegmentationData GetOrCreateSegmentation()
    {
        if (Segmentation == null)
        {
            Segmentation = new ImageSegmentationData(Width, Height);
            Segmentation.AddMaterial("Region 1", new Vector4(1, 0, 0, 0.5f));
            Segmentation.AddMaterial("Region 2", new Vector4(0, 1, 0, 0.5f));
            Segmentation.AddMaterial("Region 3", new Vector4(0, 0, 1, 0.5f));
        }

        return Segmentation;
    }

    public void LoadSegmentationFromFile(string path)
    {
        if (!File.Exists(path))
        {
            Logger.LogWarning($"Segmentation file not found: {path}");
            return;
        }

        var imported = ImageSegmentationExporter.ImportLabeledImage(path, Width, Height);
        if (imported != null)
        {
            Segmentation = imported;
            _segmentationPath = path;
            Logger.Log($"Loaded segmentation from: {path}");
        }
    }

    public void SaveSegmentation(string path)
    {
        if (Segmentation == null)
        {
            Logger.LogWarning("No segmentation data to save");
            return;
        }

        ImageSegmentationExporter.ExportLabeledImage(Segmentation, path);
        _segmentationPath = path;
        Logger.Log($"Saved segmentation to: {path}");
    }

    private void LoadSegmentation()
    {
        if (string.IsNullOrEmpty(FilePath)) return;

        var defaultSegPath = Path.ChangeExtension(FilePath, ".labels.png");
        if (File.Exists(defaultSegPath))
        {
            LoadSegmentationFromFile(defaultSegPath);
        }
        else
        {
            defaultSegPath = Path.ChangeExtension(FilePath, ".labels.tiff");
            if (File.Exists(defaultSegPath)) LoadSegmentationFromFile(defaultSegPath);
        }
    }

    public void ClearSegmentation()
    {
        Segmentation?.Dispose();
        Segmentation = null;
        _segmentationPath = null;
    }

    private void CalculateHistograms()
    {
        if (ImageData == null || ImageData.Length == 0) return;

        HistogramLuminance = new float[256];
        HistogramR = new float[256];
        HistogramG = new float[256];
        HistogramB = new float[256];

        var pixelCount = Width * Height;
        for (var i = 0; i < pixelCount * 4; i += 4)
        {
            var r = ImageData[i];
            var g = ImageData[i + 1];
            var b = ImageData[i + 2];

            HistogramR[r]++;
            HistogramG[g]++;
            HistogramB[b]++;

            var luminance = 0.299f * r + 0.587f * g + 0.114f * b;
            HistogramLuminance[(int)luminance]++;
        }
    }

    public static ImageDataset CreateSegmentationDataset(string name, string segmentationPath)
    {
        if (!File.Exists(segmentationPath))
            throw new FileNotFoundException($"Segmentation file not found: {segmentationPath}");

        var imageInfo = ImageLoader.LoadImageInfo(segmentationPath);
        if (imageInfo == null)
            throw new InvalidOperationException($"Could not load segmentation file: {segmentationPath}");

        var dataset = new ImageDataset(name, null)
        {
            Width = imageInfo.Width,
            Height = imageInfo.Height,
            BitDepth = 32,
            _segmentationPath = segmentationPath
        };

        dataset.Segmentation = ImageSegmentationExporter.ImportLabeledImage(
            segmentationPath, imageInfo.Width, imageInfo.Height);

        return dataset;
    }
}