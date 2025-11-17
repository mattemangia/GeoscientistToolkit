using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace GeoscientistToolkit.NodeEndpoint.Services;

/// <summary>
/// Manages data references to avoid transmitting large datasets over the network.
/// Data is stored in shared storage accessible by all nodes.
/// </summary>
public class DataReferenceService
{
    private readonly ConcurrentDictionary<string, DataReference> _references = new();
    private readonly string _sharedStoragePath;

    public DataReferenceService(string? sharedStoragePath = null)
    {
        _sharedStoragePath = sharedStoragePath ?? Path.Combine(Path.GetTempPath(), "GTK_SharedData");
        Directory.CreateDirectory(_sharedStoragePath);
        Console.WriteLine($"[DataReferenceService] Shared storage path: {_sharedStoragePath}");
    }

    /// <summary>
    /// Register a data file and return a reference ID
    /// </summary>
    public string RegisterDataFile(string filePath, DataType dataType, Dictionary<string, object>? metadata = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Data file not found: {filePath}");

        var referenceId = GenerateReferenceId(filePath);
        var reference = new DataReference
        {
            ReferenceId = referenceId,
            OriginalPath = filePath,
            DataType = dataType,
            Metadata = metadata ?? new Dictionary<string, object>(),
            RegisteredAt = DateTime.UtcNow,
            FileSize = new FileInfo(filePath).Length
        };

        _references.TryAdd(referenceId, reference);
        Console.WriteLine($"[DataReferenceService] Registered {dataType}: {referenceId} ({FormatBytes(reference.FileSize)})");

        return referenceId;
    }

    /// <summary>
    /// Get data reference by ID
    /// </summary>
    public DataReference? GetReference(string referenceId)
    {
        _references.TryGetValue(referenceId, out var reference);
        return reference;
    }

    /// <summary>
    /// Resolve a reference to the actual file path
    /// </summary>
    public string? ResolvePath(string referenceId)
    {
        if (_references.TryGetValue(referenceId, out var reference))
        {
            return reference.OriginalPath;
        }
        return null;
    }

    /// <summary>
    /// Copy data to shared storage if not already there
    /// </summary>
    public string EnsureInSharedStorage(string referenceId)
    {
        if (!_references.TryGetValue(referenceId, out var reference))
            throw new InvalidOperationException($"Reference not found: {referenceId}");

        var sharedPath = Path.Combine(_sharedStoragePath, $"{referenceId}{Path.GetExtension(reference.OriginalPath)}");

        if (!File.Exists(sharedPath))
        {
            Console.WriteLine($"[DataReferenceService] Copying to shared storage: {referenceId}");
            File.Copy(reference.OriginalPath, sharedPath, overwrite: true);
        }

        reference.SharedPath = sharedPath;
        return sharedPath;
    }

    /// <summary>
    /// Create a partitioned reference for splitting work across nodes
    /// </summary>
    public List<DataPartition> CreatePartitions(string referenceId, int partitionCount, PartitionStrategy strategy = PartitionStrategy.SpatialZ)
    {
        if (!_references.TryGetValue(referenceId, out var reference))
            throw new InvalidOperationException($"Reference not found: {referenceId}");

        var partitions = new List<DataPartition>();

        // Get data dimensions from metadata
        var hasX = reference.Metadata.TryGetValue("width", out var widthObj);
        var hasY = reference.Metadata.TryGetValue("height", out var heightObj);
        var hasZ = reference.Metadata.TryGetValue("depth", out var depthObj);

        if (!hasX || !hasY || !hasZ)
        {
            // For non-volumetric data, use simple count-based partitioning
            for (int i = 0; i < partitionCount; i++)
            {
                partitions.Add(new DataPartition
                {
                    PartitionId = i,
                    ReferenceId = referenceId,
                    TotalPartitions = partitionCount,
                    Strategy = strategy,
                    Metadata = new Dictionary<string, object>
                    {
                        ["partitionIndex"] = i,
                        ["partitionCount"] = partitionCount
                    }
                });
            }
            return partitions;
        }

        int width = Convert.ToInt32(widthObj);
        int height = Convert.ToInt32(heightObj);
        int depth = Convert.ToInt32(depthObj);

        // Spatial partitioning for volumetric data
        switch (strategy)
        {
            case PartitionStrategy.SpatialZ:
                {
                    int slicesPerPartition = depth / partitionCount;
                    int remainder = depth % partitionCount;

                    int currentZ = 0;
                    for (int i = 0; i < partitionCount; i++)
                    {
                        int slices = slicesPerPartition + (i < remainder ? 1 : 0);
                        partitions.Add(new DataPartition
                        {
                            PartitionId = i,
                            ReferenceId = referenceId,
                            TotalPartitions = partitionCount,
                            Strategy = strategy,
                            Start = new int[] { 0, 0, currentZ },
                            Size = new int[] { width, height, slices },
                            Metadata = new Dictionary<string, object>
                            {
                                ["zStart"] = currentZ,
                                ["zEnd"] = currentZ + slices,
                                ["slices"] = slices
                            }
                        });
                        currentZ += slices;
                    }
                    break;
                }

            case PartitionStrategy.SpatialXY:
                {
                    // Split in XY plane (for 2D slices)
                    int tilesPerSide = (int)Math.Ceiling(Math.Sqrt(partitionCount));
                    int tileWidth = width / tilesPerSide;
                    int tileHeight = height / tilesPerSide;

                    int partitionId = 0;
                    for (int ty = 0; ty < tilesPerSide && partitionId < partitionCount; ty++)
                    {
                        for (int tx = 0; tx < tilesPerSide && partitionId < partitionCount; tx++)
                        {
                            partitions.Add(new DataPartition
                            {
                                PartitionId = partitionId++,
                                ReferenceId = referenceId,
                                TotalPartitions = partitionCount,
                                Strategy = strategy,
                                Start = new int[] { tx * tileWidth, ty * tileHeight, 0 },
                                Size = new int[] { tileWidth, tileHeight, depth },
                                Metadata = new Dictionary<string, object>
                                {
                                    ["tileX"] = tx,
                                    ["tileY"] = ty
                                }
                            });
                        }
                    }
                    break;
                }
        }

        Console.WriteLine($"[DataReferenceService] Created {partitions.Count} partitions using {strategy} strategy");
        return partitions;
    }

    /// <summary>
    /// Remove old references (cleanup)
    /// </summary>
    public void CleanupOldReferences(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var oldRefs = _references.Values
            .Where(r => r.RegisteredAt < cutoff)
            .Select(r => r.ReferenceId)
            .ToList();

        foreach (var refId in oldRefs)
        {
            if (_references.TryRemove(refId, out var reference) && reference.SharedPath != null)
            {
                try
                {
                    File.Delete(reference.SharedPath);
                }
                catch { }
            }
        }

        if (oldRefs.Count > 0)
        {
            Console.WriteLine($"[DataReferenceService] Cleaned up {oldRefs.Count} old references");
        }
    }

    private string GenerateReferenceId(string filePath)
    {
        var hashInput = $"{filePath}_{DateTime.UtcNow.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hash)[..16].ToLower();
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

public class DataReference
{
    public required string ReferenceId { get; set; }
    public required string OriginalPath { get; set; }
    public string? SharedPath { get; set; }
    public required DataType DataType { get; set; }
    public required Dictionary<string, object> Metadata { get; set; }
    public DateTime RegisteredAt { get; set; }
    public long FileSize { get; set; }
}

public class DataPartition
{
    public int PartitionId { get; set; }
    public required string ReferenceId { get; set; }
    public int TotalPartitions { get; set; }
    public PartitionStrategy Strategy { get; set; }
    public int[]? Start { get; set; }  // [x, y, z] start indices
    public int[]? Size { get; set; }   // [width, height, depth] size
    public required Dictionary<string, object> Metadata { get; set; }
}

public enum DataType
{
    CTVolume,
    Mesh,
    PNMDataset,
    PointCloud,
    Image,
    Other
}

public enum PartitionStrategy
{
    SpatialZ,      // Split along Z axis (depth slices)
    SpatialXY,     // Split in XY plane (tiles)
    SpatialOctree, // Octree-based spatial partitioning
    Temporal,      // Split by time steps
    Random         // Random distribution
}
