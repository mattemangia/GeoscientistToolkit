// GeoscientistToolkit/Data/CtImageStack/StreamingCtVolumeDataset.cs
using GeoscientistToolkit.Util;
using System.IO;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class GvtLodInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Depth { get; set; }
        public long FileOffset { get; set; }
    }

    // THE FIX: Add ISerializableDataset interface
    public class StreamingCtVolumeDataset : Dataset, ISerializableDataset
    {
        public int FullWidth { get; private set; }
        public int FullHeight { get; private set; }
        public int FullDepth { get; private set; }
        public int BrickSize { get; private set; }
        public int LodCount { get; private set; }
        public GvtLodInfo[] LodInfos { get; private set; }
        public GvtLodInfo BaseLod => LodInfos[LodCount - 1];
        public byte[] BaseLodVolumeData { get; private set; }

        // --- THE CRUCIAL LINK ---
        /// <summary>
        /// A reference to the corresponding CtImageStackDataset that holds the editable labels and materials.
        /// </summary>
        public CtImageStackDataset EditablePartner { get; set; }

        public StreamingCtVolumeDataset(string name, string filePath) : base(name, filePath)
        {
            Type = DatasetType.CtBinaryFile;
        }

        public override long GetSizeInBytes() => File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;

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
                
                LodInfos = new GvtLodInfo[LodCount];
                for(int i = 0; i < LodCount; i++)
                {
                    LodInfos[i] = new GvtLodInfo
                    {
                        Width = reader.ReadInt32(),
                        Height = reader.ReadInt32(),
                        Depth = reader.ReadInt32(),
                        FileOffset = reader.ReadInt64()
                    };
                }
                
                var baseLodInfo = BaseLod;
                int bricksX = (baseLodInfo.Width + BrickSize - 1) / BrickSize;
                int bricksY = (baseLodInfo.Height + BrickSize - 1) / BrickSize;
                int bricksZ = (baseLodInfo.Depth + BrickSize - 1) / BrickSize;
                long baseLodByteSize = (long)bricksX * bricksY * bricksZ * BrickSize * BrickSize * BrickSize;
                
                BaseLodVolumeData = new byte[baseLodByteSize];
                fs.Seek(baseLodInfo.FileOffset, SeekOrigin.Begin);
                fs.Read(BaseLodVolumeData, 0, (int)baseLodByteSize);
            }
        }
        
        public object ToSerializableObject()
        {
            if (EditablePartner == null)
            {
                Logger.LogWarning($"[StreamingCtVolumeDataset] Cannot serialize '{Name}' because its EditablePartner is not set.");
                return null; // Or handle this case as needed
            }

            return new StreamingCtVolumeDatasetDTO
            {
                TypeName = nameof(StreamingCtVolumeDataset),
                Name = this.Name,
                FilePath = this.FilePath,
                PartnerFilePath = this.EditablePartner.FilePath
            };
        }
        
        public override void Unload()
        {
            BaseLodVolumeData = null;
        }
    }
}