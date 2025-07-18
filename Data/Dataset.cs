// GeoscientistToolkit/Data/Dataset.cs
// Base class for all dataset types

namespace GeoscientistToolkit.Data
{
    public enum DatasetType
    {
        CtImageStack,
        CtBinaryFile,
        MicroXrf,
        PointCloud,
        Mesh,
        SingleImage // New Type
    }

    public abstract class Dataset
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public DatasetType Type { get; protected set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }

        protected Dataset(string name, string filePath)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            DateCreated = DateTime.Now;
            DateModified = DateTime.Now;
        }

        public abstract long GetSizeInBytes();
        public abstract void Load();
        public abstract void Unload();
    }
}