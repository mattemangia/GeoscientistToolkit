// GeoscientistToolkit/Data/Loaders/PointCloudLoader.cs

using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders;

/// <summary>
/// Loader for point cloud files (XYZ, TXT, CSV, PTS, ASC formats).
/// Generates a Mesh3D dataset from point cloud data using Delaunay triangulation.
/// </summary>
public class PointCloudLoader : IDataLoader
{
    public string FilePath { get; set; } = "";
    public string Name => "Point Cloud (XYZ/PTS/ASC)";
    public string Description => "Import point cloud files and convert to 3D mesh using Delaunay triangulation";

    // Mesh generation parameters
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
                progressReporter?.Report((0.05f, "Initializing point cloud processor..."));

                var parameters = new PointCloudMeshGenerator.MeshGenerationParameters
                {
                    GridStep = GridStep,
                    MaxEdgeLength = MaxEdgeLength,
                    ZDeep = ZDeep,
                    CreateSolidMesh = CreateSolidMesh,
                    EnableDownsampling = EnableDownsampling,
                    TranslateToOrigin = TranslateToOrigin,
                    RotationAngleDegrees = RotationAngle
                };

                var generator = new PointCloudMeshGenerator(parameters);
                generator.SetProgressCallback((message, progress) =>
                {
                    progressReporter?.Report((progress * 0.8f + 0.1f, message));
                });

                progressReporter?.Report((0.1f, "Processing point cloud..."));
                var result = generator.GenerateFromFile(FilePath);

                if (!result.Success)
                {
                    throw new Exception(result.StatusMessage);
                }

                progressReporter?.Report((0.9f, "Creating Mesh3D dataset..."));

                // Create OBJ content in memory
                var objContent = GenerateObjContent(result);

                // Create temporary OBJ file
                var tempPath = Path.Combine(Path.GetTempPath(), $"pointcloud_{Guid.NewGuid()}.obj");
                File.WriteAllText(tempPath, objContent);

                // Create Mesh3DDataset
                var fileName = Path.GetFileNameWithoutExtension(FilePath);
                var dataset = new Mesh3DDataset(fileName, tempPath);
                dataset.Load();

                // Store original source info
                dataset.Metadata["SourceFile"] = FilePath;
                dataset.Metadata["OriginalPointCount"] = result.OriginalPointCount.ToString();
                dataset.Metadata["FilteredPointCount"] = result.FilteredPointCount.ToString();
                dataset.Metadata["GeneratedFrom"] = "PointCloudLoader";

                progressReporter?.Report((1.0f,
                    $"Point cloud imported successfully! ({result.Vertices.Count} vertices, {result.Faces.Count} faces)"));

                return dataset;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PointCloudLoader] Error importing point cloud: {ex}");
                throw new Exception($"Failed to import point cloud: {ex.Message}", ex);
            }
        });
    }

    private string GenerateObjContent(PointCloudMeshGenerator.MeshGenerationResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Generated from point cloud by GeoscientistToolkit");
        sb.AppendLine($"# Vertices: {result.Vertices.Count}");
        sb.AppendLine($"# Faces: {result.Faces.Count}");
        sb.AppendLine();

        // Write vertices
        foreach (var v in result.Vertices)
        {
            sb.AppendLine($"v {v.X:F6} {v.Y:F6} {v.Z:F6}");
        }

        sb.AppendLine();

        // Write faces (OBJ uses 1-based indexing)
        foreach (var face in result.Faces)
        {
            sb.AppendLine($"f {face[0] + 1} {face[1] + 1} {face[2] + 1}");
        }

        return sb.ToString();
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
                                pointCount++;
                        }
                    }
                }

                // Estimate total points based on file size and sample
                if (pointCount > 0 && lineCount > 0)
                {
                    var bytesPerLine = fileInfo.Length / Math.Max(1, lineCount);
                    info.EstimatedPointCount = (int)(fileInfo.Length / bytesPerLine);
                    info.HasValidFormat = true;
                    info.HasColors = CheckForColors(line);
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
