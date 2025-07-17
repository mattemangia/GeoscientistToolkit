// GeoscientistToolkit/Data/Dataset.cs
// This file defines the base class for all datasets handled by the application,
// as well as the enumeration for the different types of datasets.

namespace GeoscientistToolkit.Data
{
    public enum DatasetType
    {
        CtImageStack,
        CtSegmentedVolume,
        CtBinaryFile,
        SingleImage,
        PointCloud,
        Mesh
    }

    public abstract class Dataset
    {
        public string Name { get; set; }
        public DatasetType Type { get; }
        public string FilePath { get; set; }

        protected Dataset(string name, DatasetType type, string filePath)
        {
            Name = name;
            Type = type;
            FilePath = filePath;
        }
    }

    // Example of a specific dataset implementation
    public class CtImageStackDataset : Dataset
    {
        public int BinningSize { get; set; }
        public bool LoadFullInMemory { get; set; }
        public float PixelSize { get; set; }
        public string Unit { get; set; } = "micrometers";

        public CtImageStackDataset(string name, string filePath) : base(name, DatasetType.CtImageStack, filePath)
        {
        }
    }
}