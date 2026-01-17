// GeoscientistToolkit/Data/Loaders/PointCloudLoader.cs

using GeoscientistToolkit.Data.PointCloud;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
/// Loader for point cloud files (XYZ, TXT, CSV, PTS, ASC formats).
/// Creates a PointCloudDataset that can be visualized and processed.
/// </summary>
public class PointCloudLoader : IDataLoader
{
    public string FilePath { get; set; } = "";
    public string Name => "Point Cloud (XYZ/PTS/ASC)";
    public string Description => "Import point cloud files for 3D visualization and mesh generation";

    // Point cloud settings (kept for backwards compatibility with UI)
    public float GridStep { get; set; } = 2.0f;
    public float MaxEdgeLength { get; set; } = 4.0f;
    public float ZDeep { get; set; } = 20.0f;
    public bool CreateSolidMesh { get; set; } = true;
    public bool EnableDownsampling { get; set; } = true;
    public bool TranslateToOrigin { get; set; } = false;
    public float RotationAngle { get; set; } = 0.0f;

    public bool CanImport => !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath) && IsSupported();

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrEmpty(FilePath))
                return "Please select a point cloud file";
            if (!File.Exists(FilePath))
                return "Selected file does not exist";
            if (!IsSupported())
                return "Unsupported file format. Supported: XYZ, TXT, CSV, PTS, ASC";
            return null;
        }
    }

    private static readonly string[] SupportedExtensions = { ".xyz", ".txt", ".csv", ".pts", ".asc" };

    public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
    {
        return await Task.Run(() =>
        {
            try
            {
                progressReporter?.Report((0.1f, "Loading point cloud file..."));

                // Create PointCloudDataset
                var fileName = Path.GetFileNameWithoutExtension(FilePath);
                var dataset = new PointCloudDataset(fileName, FilePath);

                // Configure settings
                dataset.NodataValue = -9999.0f;

                progressReporter?.Report((0.3f, "Reading point data..."));

                // Load the point cloud
                dataset.Load();

                progressReporter?.Report((0.7f, "Processing point cloud..."));

                // Apply translation to origin if requested
                if (TranslateToOrigin)
                {
                    dataset.CenterAtOrigin();
                }

                // Apply downsampling if requested and many points
                if (EnableDownsampling && dataset.PointCount > 500000)
                {
                    progressReporter?.Report((0.8f, "Downsampling large point cloud..."));
                    dataset.Downsample(GridStep);
                }

                // Store import settings in metadata
                dataset.Metadata["ImportGridStep"] = GridStep.ToString();
                dataset.Metadata["ImportMaxEdgeLength"] = MaxEdgeLength.ToString();
                dataset.Metadata["ImportZDeep"] = ZDeep.ToString();
                dataset.Metadata["ImportCreateSolidMesh"] = CreateSolidMesh.ToString();

                progressReporter?.Report((1.0f,
                    $"Point cloud imported: {dataset.PointCount:N0} points" +
                    (dataset.HasColors ? " with colors" : "")));

                Logger.Log($"[PointCloudLoader] Loaded {dataset.PointCount:N0} points from {FilePath}");

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PointCloudLoader] Error importing point cloud: {ex}");
                throw new Exception($"Failed to import point cloud: {ex.Message}", ex);
            }
        });
    }

    public void Reset()
    {
        FilePath = "";
        GridStep = 2.0f;
        MaxEdgeLength = 4.0f;
        ZDeep = 20.0f;
        CreateSolidMesh = true;
        EnableDownsampling = true;
        TranslateToOrigin = false;
        RotationAngle = 0.0f;
    }

    private bool IsSupported()
    {
        if (string.IsNullOrEmpty(FilePath))
            return false;

        var extension = Path.GetExtension(FilePath).ToLower();
        return SupportedExtensions.Contains(extension);
    }

    public PointCloudInfo GetFileInfo()
    {
        if (!File.Exists(FilePath))
            return null;

        try
        {
            var fileInfo = new FileInfo(FilePath);
            var info = new PointCloudInfo
            {
                FileName = Path.GetFileName(FilePath),
                Format = fileInfo.Extension.ToUpper().TrimStart('.'),
                FileSize = fileInfo.Length,
                IsSupported = IsSupported()
            };

            // Try to count lines to estimate point count
            try
            {
                using var reader = new StreamReader(FilePath);
                int lineCount = 0;
                int pointCount = 0;
                string lastLine = null;
                string line;
                while ((line = reader.ReadLine()) != null && lineCount < 100)
                {
                    lineCount++;
                    if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#") && !line.StartsWith("//"))
                    {
                        var parts = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            if (float.TryParse(parts[0], out _) && float.TryParse(parts[1], out _) && float.TryParse(parts[2], out _))
                            {
                                pointCount++;
                                lastLine = line;
                            }
                        }
                    }
                }

                // Estimate total points based on file size and sample
                if (pointCount > 0 && lineCount > 0)
                {
                    var bytesPerLine = fileInfo.Length / Math.Max(1, lineCount);
                    info.EstimatedPointCount = (int)(fileInfo.Length / bytesPerLine);
                    info.HasValidFormat = true;
                    info.HasColors = CheckForColors(lastLine);
                }
            }
            catch
            {
                // Ignore errors in preview
            }

            return info;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[PointCloudLoader] Error reading file info: {ex.Message}");
            return null;
        }
    }

    private bool CheckForColors(string sampleLine)
    {
        if (string.IsNullOrEmpty(sampleLine))
            return false;

        var parts = sampleLine.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 6;
    }

    public class PointCloudInfo
    {
        public string FileName { get; set; }
        public string Format { get; set; }
        public long FileSize { get; set; }
        public bool IsSupported { get; set; }
        public int EstimatedPointCount { get; set; }
        public bool HasColors { get; set; }
        public bool HasValidFormat { get; set; }
    }
}
