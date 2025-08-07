// GeoscientistToolkit/Data/Dataset.cs
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
        Mesh3D
    }

    public abstract class Dataset
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public DatasetType Type { get; protected set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
        public bool IsMissing { get; set; } = false; // To mark if the source file is not found on load

        protected Dataset(string name, string filePath)
        {
            Name = name;
            FilePath = filePath;
            
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