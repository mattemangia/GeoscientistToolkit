// GeoscientistToolkit/Data/PointCloud/PointCloudDataset.cs

using System.Globalization;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.PointCloud;

/// <summary>
/// Dataset for 3D point cloud data (XYZ, TXT, CSV, PTS, ASC files)
/// </summary>
public class PointCloudDataset : Dataset, ISerializableDataset
{
    private static readonly string[] SupportedExtensions = { ".xyz", ".txt", ".csv", ".pts", ".asc" };

    public PointCloudDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.PointCloud;
        Points = new List<Vector3>();
        Colors = new List<Vector4>();
        Intensities = new List<float>();

        // Determine format from extension
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        FileFormat = ext.TrimStart('.');
    }

    // Point data
    public List<Vector3> Points { get; private set; }
    public List<Vector4> Colors { get; set; } // Per-point RGBA colors
    public List<float> Intensities { get; set; } // Per-point intensity values (if available)

    // Statistics
    public int PointCount => Points?.Count ?? 0;
    public bool HasColors => Colors?.Count > 0 && Colors.Count == Points?.Count;
    public bool HasIntensities => Intensities?.Count > 0 && Intensities.Count == Points?.Count;

    // Bounding box
    public Vector3 BoundingBoxMin { get; set; }
    public Vector3 BoundingBoxMax { get; set; }
    public Vector3 Center { get; set; }
    public Vector3 Size => BoundingBoxMax - BoundingBoxMin;

    // File info
    public string FileFormat { get; set; }
    public bool IsLoaded { get; private set; }

    // Point cloud-specific settings
    public float PointSize { get; set; } = 1.0f;
    public float Scale { get; set; } = 1.0f;
    public float NodataValue { get; set; } = -9999.0f;

    public object ToSerializableObject()
    {
        return new PointCloudDatasetDTO
        {
            TypeName = nameof(PointCloudDataset),
            Name = Name,
            FilePath = FilePath,
            FileFormat = FileFormat,
            PointCount = PointCount,
            HasColors = HasColors,
            HasIntensities = HasIntensities,
            BoundingBoxMin = BoundingBoxMin,
            BoundingBoxMax = BoundingBoxMax,
            Center = Center,
            PointSize = PointSize,
            Scale = Scale
        };
    }

    public static PointCloudDataset CreateFromPoints(string name, string filePath, List<Vector3> points, List<Vector4> colors = null)
    {
        var dataset = new PointCloudDataset(name, filePath)
        {
            Points = points ?? new List<Vector3>(),
            Colors = colors ?? new List<Vector4>(),
            FileFormat = "xyz",
            IsLoaded = true
        };

        dataset.CalculateBounds();
        return dataset;
    }

    public override long GetSizeInBytes()
    {
        return File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;
    }

    public override void Load()
    {
        if (IsLoaded) return;

        if (!File.Exists(FilePath))
        {
            Logger.LogError($"Point cloud file not found: {FilePath}");
            IsMissing = true;
            return;
        }

        try
        {
            Logger.Log($"Loading point cloud: {FilePath}");
            LoadPointCloudFile();
            CalculateBounds();
            IsLoaded = true;
            Logger.Log($"Point cloud loaded: {PointCount:N0} points");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load point cloud: {ex.Message}");
            throw;
        }
    }

    private void LoadPointCloudFile()
    {
        Points.Clear();
        Colors.Clear();
        Intensities.Clear();

        var lines = File.ReadAllLines(FilePath);
        var culture = CultureInfo.InvariantCulture;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
                continue;

            var parts = line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 3)
            {
                if (float.TryParse(parts[0], NumberStyles.Float, culture, out var x) &&
                    float.TryParse(parts[1], NumberStyles.Float, culture, out var y) &&
                    float.TryParse(parts[2], NumberStyles.Float, culture, out var z))
                {
                    // Skip nodata values
                    if (z <= NodataValue || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                        continue;

                    Points.Add(new Vector3(x, y, z));

                    // Check for intensity value (column 4)
                    if (parts.Length >= 4 && float.TryParse(parts[3], NumberStyles.Float, culture, out var intensity))
                    {
                        // Normalize intensity if > 1
                        if (intensity > 1) intensity /= 255f;
                        Intensities.Add(intensity);
                    }

                    // Check for RGB values (columns 4, 5, 6 or 5, 6, 7)
                    int colorStart = parts.Length >= 7 ? 4 : 3;
                    if (parts.Length >= colorStart + 3)
                    {
                        if (float.TryParse(parts[colorStart], NumberStyles.Float, culture, out var r) &&
                            float.TryParse(parts[colorStart + 1], NumberStyles.Float, culture, out var g) &&
                            float.TryParse(parts[colorStart + 2], NumberStyles.Float, culture, out var b))
                        {
                            // Normalize if values > 1
                            if (r > 1 || g > 1 || b > 1)
                            {
                                r /= 255f;
                                g /= 255f;
                                b /= 255f;
                            }
                            Colors.Add(new Vector4(r, g, b, 1.0f));
                        }
                    }
                }
            }
        }

        // Ensure colors list matches points if partially filled
        if (Colors.Count > 0 && Colors.Count != Points.Count)
        {
            Logger.Log($"Color count ({Colors.Count}) doesn't match point count ({Points.Count}), clearing colors");
            Colors.Clear();
        }

        // Same for intensities
        if (Intensities.Count > 0 && Intensities.Count != Points.Count)
        {
            Logger.Log($"Intensity count ({Intensities.Count}) doesn't match point count ({Points.Count}), clearing intensities");
            Intensities.Clear();
        }
    }

    public void CalculateBounds()
    {
        if (Points.Count == 0)
        {
            BoundingBoxMin = Vector3.Zero;
            BoundingBoxMax = Vector3.Zero;
            Center = Vector3.Zero;
            return;
        }

        BoundingBoxMin = new Vector3(float.MaxValue);
        BoundingBoxMax = new Vector3(float.MinValue);

        foreach (var point in Points)
        {
            BoundingBoxMin = Vector3.Min(BoundingBoxMin, point);
            BoundingBoxMax = Vector3.Max(BoundingBoxMax, point);
        }

        Center = (BoundingBoxMin + BoundingBoxMax) * 0.5f;
    }

    public void Save()
    {
        Save(FilePath);
    }

    public void Save(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            Logger.LogError("Cannot save point cloud: no file path specified");
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var culture = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"# Point cloud exported by GeoscientistToolkit");
            sb.AppendLine($"# Points: {PointCount}");
            sb.AppendLine($"# Format: X Y Z" + (HasColors ? " R G B" : ""));

            for (int i = 0; i < Points.Count; i++)
            {
                var p = Points[i];
                if (HasColors)
                {
                    var c = Colors[i];
                    sb.AppendLine($"{p.X.ToString(culture)} {p.Y.ToString(culture)} {p.Z.ToString(culture)} {(int)(c.X * 255)} {(int)(c.Y * 255)} {(int)(c.Z * 255)}");
                }
                else
                {
                    sb.AppendLine($"{p.X.ToString(culture)} {p.Y.ToString(culture)} {p.Z.ToString(culture)}");
                }
            }

            File.WriteAllText(path, sb.ToString());
            Logger.Log($"Saved point cloud to {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to save point cloud: {ex.Message}");
            throw;
        }
    }

    public override void Unload()
    {
        if (!IsLoaded) return;

        Points.Clear();
        Colors.Clear();
        Intensities.Clear();
        IsLoaded = false;

        Logger.Log($"Point cloud unloaded: {Name}");
    }

    public static bool IsSupportedExtension(string extension)
    {
        return SupportedExtensions.Contains(extension.ToLowerInvariant());
    }

    /// <summary>
    /// Applies a transformation matrix to all points
    /// </summary>
    public void ApplyTransform(Matrix4x4 transform)
    {
        for (int i = 0; i < Points.Count; i++)
        {
            Points[i] = Vector3.Transform(Points[i], transform);
        }
        CalculateBounds();
    }

    /// <summary>
    /// Translates all points by a vector
    /// </summary>
    public void Translate(Vector3 offset)
    {
        for (int i = 0; i < Points.Count; i++)
        {
            Points[i] += offset;
        }
        BoundingBoxMin += offset;
        BoundingBoxMax += offset;
        Center += offset;
    }

    /// <summary>
    /// Centers the point cloud at the origin
    /// </summary>
    public void CenterAtOrigin()
    {
        Translate(-Center);
    }

    /// <summary>
    /// Downsamples the point cloud using a voxel grid
    /// </summary>
    public void Downsample(float gridSize)
    {
        if (gridSize <= 0) return;

        var buckets = new Dictionary<(int, int, int), (Vector3 sum, Vector4 colorSum, int count)>();

        for (int i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            var key = (
                (int)MathF.Floor(p.X / gridSize),
                (int)MathF.Floor(p.Y / gridSize),
                (int)MathF.Floor(p.Z / gridSize)
            );

            if (!buckets.ContainsKey(key))
                buckets[key] = (Vector3.Zero, Vector4.Zero, 0);

            var (sum, colorSum, count) = buckets[key];
            var newColorSum = HasColors ? colorSum + Colors[i] : colorSum;
            buckets[key] = (sum + p, newColorSum, count + 1);
        }

        Points.Clear();
        var hadColors = HasColors;
        Colors.Clear();

        foreach (var kvp in buckets)
        {
            var avg = kvp.Value.sum / kvp.Value.count;
            Points.Add(avg);

            if (hadColors)
            {
                var avgColor = kvp.Value.colorSum / kvp.Value.count;
                Colors.Add(avgColor);
            }
        }

        Intensities.Clear(); // Downsampling loses per-point intensities
        CalculateBounds();
        Logger.Log($"Downsampled point cloud to {PointCount:N0} points");
    }
}

/// <summary>
/// Data Transfer Object for PointCloudDataset serialization
/// </summary>
public class PointCloudDatasetDTO : DatasetDTO
{
    public string FileFormat { get; set; }
    public int PointCount { get; set; }
    public bool HasColors { get; set; }
    public bool HasIntensities { get; set; }
    public Vector3 BoundingBoxMin { get; set; }
    public Vector3 BoundingBoxMax { get; set; }
    public Vector3 Center { get; set; }
    public float PointSize { get; set; }
    public float Scale { get; set; }
}
