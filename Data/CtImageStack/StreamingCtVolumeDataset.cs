// GeoscientistToolkit/Data/CtImageStack/StreamingCtVolumeDataset.cs

using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack;

public class GvtLodInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Depth { get; set; }
    public long FileOffset { get; set; }
}

public class StreamingCtVolumeDataset : Dataset, ISerializableDataset
{
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
        if (BaseLodVolumeData != null) return;
        Logger.Log($"[StreamingCtVolumeDataset] Loading header and base LOD from '{FilePath}'");

        using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs))
        {
            FullWidth = reader.ReadInt32();
            FullHeight = reader.ReadInt32();
            FullDepth = reader.ReadInt32();
            BrickSize = reader.ReadInt32();
            LodCount = reader.ReadInt32();

            Logger.Log(
                $"[StreamingCtVolumeDataset] Header: {FullWidth}×{FullHeight}×{FullDepth}, BrickSize={BrickSize}, LODs={LodCount}");

            LodInfos = new GvtLodInfo[LodCount];
            for (var i = 0; i < LodCount; i++)
            {
                LodInfos[i] = new GvtLodInfo
                {
                    Width = reader.ReadInt32(),
                    Height = reader.ReadInt32(),
                    Depth = reader.ReadInt32(),
                    FileOffset = reader.ReadInt64()
                };
                Logger.Log(
                    $"[StreamingCtVolumeDataset] LOD {i}: {LodInfos[i].Width}×{LodInfos[i].Height}×{LodInfos[i].Depth} at offset {LodInfos[i].FileOffset}");
            }

            var baseLodInfo = BaseLod;
            var bricksX = (baseLodInfo.Width + BrickSize - 1) / BrickSize;
            var bricksY = (baseLodInfo.Height + BrickSize - 1) / BrickSize;
            var bricksZ = (baseLodInfo.Depth + BrickSize - 1) / BrickSize;
            var baseLodByteSize = (long)bricksX * bricksY * bricksZ * BrickSize * BrickSize * BrickSize;

            Logger.Log(
                $"[StreamingCtVolumeDataset] Base LOD has {bricksX}×{bricksY}×{bricksZ} bricks, total size: {baseLodByteSize} bytes");

            BaseLodVolumeData = new byte[baseLodByteSize];
            fs.Seek(baseLodInfo.FileOffset, SeekOrigin.Begin);
            var bytesRead = fs.Read(BaseLodVolumeData, 0, (int)baseLodByteSize);

            Logger.Log($"[StreamingCtVolumeDataset] Read {bytesRead} bytes for base LOD");

            // Check if base LOD has any data
            var nonZeroCount = 0;
            for (var i = 0; i < Math.Min(1000, BaseLodVolumeData.Length); i++)
                if (BaseLodVolumeData[i] > 0)
                {
                    nonZeroCount++;
                    if (nonZeroCount <= 10)
                        Logger.Log(
                            $"[StreamingCtVolumeDataset] Found non-zero value {BaseLodVolumeData[i]} at index {i}");
                }

            if (nonZeroCount == 0)
                Logger.LogError("[StreamingCtVolumeDataset] WARNING: Base LOD appears to be empty!");
            else
                Logger.Log(
                    $"[StreamingCtVolumeDataset] Base LOD has {nonZeroCount} non-zero values in first 1000 bytes");
        }
    }

    public override void Unload()
    {
        BaseLodVolumeData = null;
    }
}