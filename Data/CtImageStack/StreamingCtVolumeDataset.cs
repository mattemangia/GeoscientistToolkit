// GAIA/Data/CtImageStack/StreamingCtVolumeDataset.cs

using GAIA.Data.VolumeData;
using GAIA.Util;

namespace GAIA.Data.CtImageStack;

public class GvtLodInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }
    public long FileOffset { get; set; }
}

public class StreamingCtVolumeDataset : Dataset, ISerializableDataset
{
    private readonly object _loadLock = new();
    public StreamingCtVolumeDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.CtBinaryFile;
    }

    public int FullWidth { get; private set; }
    public int FullHeight { get; private set; }
    public int FullDepth { get; private set; }
    public int BrickSize { get; private set; }
    public int LodCount { get; private set; }
    public GvtLodInfo[] LodInfos { get; private set; }
    public GvtLodInfo BaseLod => LodInfos[LodCount - 1];
    public byte[] BaseLodVolumeData { get; private set; }
    public GvtLodInfo RenderLod { get; private set; }
    public byte[] RenderLodVolumeData { get; private set; }
    public int RenderLodIndex { get; private set; } = -1;
    public IGrayscaleVolumeData VolumeData { get; set; }
    public ILabelVolumeData LabelData { get; set; }
    public List<Material> Materials { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }

    /// <summary>
    ///     A reference to the corresponding CtImageStackDataset that holds the editable labels and materials.
    /// </summary>
    public CtImageStackDataset EditablePartner { get; set; }

    public object ToSerializableObject()
    {
        return new StreamingCtVolumeDatasetDTO
        {
            TypeName = nameof(StreamingCtVolumeDataset),
            Name = Name,
            FilePath = FilePath,
            PartnerFilePath = EditablePartner?.FilePath ?? ""
            // Metadata will be handled by ProjectSerializer
        };
    }

    public override long GetSizeInBytes()
    {
        return File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;
    }

    public override void Load()
    {
        lock (_loadLock)
        {
        if (BaseLodVolumeData != null) return;
        LoadMetadataCore();
        Logger.Log($"[StreamingCtVolumeDataset] Loading base LOD from '{FilePath}'");

        using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs))
        {
            var baseLodInfo = BaseLod;
            var bricksX = (baseLodInfo.Width + BrickSize - 1) / BrickSize;
            var bricksY = (baseLodInfo.Height + BrickSize - 1) / BrickSize;
            var bricksZ = (baseLodInfo.Depth + BrickSize - 1) / BrickSize;
            var baseLodByteSize = (long)bricksX * bricksY * bricksZ * BrickSize * BrickSize * BrickSize;

            Logger.Log(
                $"[StreamingCtVolumeDataset] Base LOD has {bricksX}×{bricksY}×{bricksZ} bricks, total size: {baseLodByteSize} bytes");

            BaseLodVolumeData = new byte[baseLodByteSize];
            fs.Seek(baseLodInfo.FileOffset, SeekOrigin.Begin);
            fs.ReadExactly(BaseLodVolumeData);
            RenderLod = baseLodInfo;
            RenderLodVolumeData = BaseLodVolumeData;
            RenderLodIndex = LodCount - 1;

            Logger.Log($"[StreamingCtVolumeDataset] Read {baseLodByteSize} bytes for base LOD");

            // Scan the whole buffer: the leading bytes are the volume corner, which is
            // air in almost every scan, so a prefix-only check reports false emptiness.
            long nonZeroCount = 0;
            foreach (var value in BaseLodVolumeData)
                if (value > 0)
                    nonZeroCount++;

            if (nonZeroCount == 0)
                Logger.LogError("[StreamingCtVolumeDataset] WARNING: Base LOD appears to be empty!");
            else
                Logger.Log(
                    $"[StreamingCtVolumeDataset] Base LOD has {nonZeroCount} non-zero voxels " +
                    $"({100.0 * nonZeroCount / BaseLodVolumeData.LongLength:F1}% fill)");
        }
        }
    }

    /// <summary>
    /// Reads only the small GVT header and LOD table. This is safe to call immediately after
    /// import and makes dataset-list metadata available without allocating any voxel buffers.
    /// </summary>
    public void LoadMetadata()
    {
        lock (_loadLock) LoadMetadataCore();
    }

    private void LoadMetadataCore()
    {
        if (LodInfos is { Length: > 0 }) return;
        Logger.Log($"[StreamingCtVolumeDataset] Loading metadata from '{FilePath}'");
        using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(fs);
        if (fs.Length < 20) throw new InvalidDataException($"GVT header is incomplete: {FilePath}");
        FullWidth = reader.ReadInt32();
        FullHeight = reader.ReadInt32();
        FullDepth = reader.ReadInt32();
        BrickSize = reader.ReadInt32();
        LodCount = reader.ReadInt32();
        if (FullWidth <= 0 || FullHeight <= 0 || FullDepth <= 0 || BrickSize <= 0 || LodCount <= 0 || LodCount > 64)
            throw new InvalidDataException($"Invalid GVT metadata in '{FilePath}'.");
        if (fs.Length < 20L + LodCount * 20L)
            throw new InvalidDataException($"GVT LOD table is incomplete: {FilePath}");

        Width = FullWidth; Height = FullHeight; Depth = FullDepth;
        LodInfos = new GvtLodInfo[LodCount];
        for (var i = 0; i < LodCount; i++)
        {
            LodInfos[i] = new GvtLodInfo
            {
                Width = reader.ReadInt32(), Height = reader.ReadInt32(), Depth = reader.ReadInt32(),
                FileOffset = reader.ReadInt64()
            };
            var lod = LodInfos[i];
            if (lod.Width <= 0 || lod.Height <= 0 || lod.Depth <= 0 || lod.FileOffset < 20L + LodCount * 20L)
                throw new InvalidDataException($"Invalid GVT LOD {i} metadata in '{FilePath}'.");
        }
        Logger.Log($"[StreamingCtVolumeDataset] Metadata ready: {FullWidth}×{FullHeight}×{FullDepth}, " +
                   $"BrickSize={BrickSize}, LODs={LodCount}");
    }

    /// <summary>Loads the finest complete LOD that fits the renderer's VRAM budget.</summary>
    public void LoadBestRenderLod(long byteBudget, int maxAxisSize)
    {
        Load();
        var selected = LodCount - 1;
        long selectedBytes = BaseLodVolumeData.LongLength;
        for (var i = 0; i < LodCount; i++)
        {
            var lod = LodInfos[i];
            if (lod.Width > maxAxisSize || lod.Height > maxAxisSize || lod.Depth > maxAxisSize) continue;
            var bx = (lod.Width + BrickSize - 1L) / BrickSize;
            var by = (lod.Height + BrickSize - 1L) / BrickSize;
            var bz = (lod.Depth + BrickSize - 1L) / BrickSize;
            var bytes = bx * by * bz * BrickSize * BrickSize * BrickSize;
            if (bytes <= byteBudget)
            {
                selected = i;
                selectedBytes = bytes;
                break;
            }
        }

        if (selected == LodCount - 1) return;
        var data = new byte[selectedBytes];
        using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        fs.Seek(LodInfos[selected].FileOffset, SeekOrigin.Begin);
        fs.ReadExactly(data);
        RenderLod = LodInfos[selected];
        RenderLodVolumeData = data;
        RenderLodIndex = selected;
        Logger.Log($"[StreamingCtVolumeDataset] Selected render LOD {selected}: " +
                   $"{RenderLod.Width}×{RenderLod.Height}×{RenderLod.Depth} ({selectedBytes / 1048576.0:F1} MiB)");
    }

    public override void Unload()
    {
        BaseLodVolumeData = null;
        RenderLodVolumeData = null;
        RenderLod = null;
        RenderLodIndex = -1;
    }
}
