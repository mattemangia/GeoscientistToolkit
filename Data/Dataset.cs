// GeoscientistToolkit/Data/Dataset.cs (Modified)
using System;
using System.Collections.Generic;
using System.IO;

namespace GeoscientistToolkit.Data
{
    public enum DatasetType
    {
        CtImageStack,
        CtBinaryFile,
        MicroXrf,
        PointCloud,
        Mesh,
        SingleImage,
        Group,
        Mesh3D,
        Table,
        GIS,
        AcousticVolume,
        PNM
    }

    public abstract class Dataset
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public DatasetType Type { get; protected set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public bool IsMissing { get; set; } = false;
        
        // NEW: Dataset-specific metadata
        public DatasetMetadata DatasetMetadata { get; set; } = new DatasetMetadata();

        protected Dataset(string name, string filePath)
        {
            Name = name;
            FilePath = filePath;
            
            // Initialize DatasetMetadata with sample name
            DatasetMetadata.SampleName = name;
            
            if (File.Exists(filePath))
            {
                var info = new FileInfo(filePath);
                DateCreated = info.CreationTime;
                DateModified = info.LastWriteTime;
            }
            else if (Directory.Exists(filePath))
            {
                var info = new DirectoryInfo(filePath);
                DateCreated = info.CreationTime;
                DateModified = info.LastWriteTime;
            }
            else
            {
                DateCreated = DateTime.Now;
                DateModified = DateTime.Now;
            }
        }

        public abstract long GetSizeInBytes();
        public abstract void Load();
        public abstract void Unload();
    }
}