// GeoscientistToolkit/Data/CtImageStack/CtImageStackDataset.cs
namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackDataset : Dataset, ISerializableDataset
    {
        // Dimensions
        public int Width { get; set; }
        public int Height { get; set; }
        public int Depth { get; set; } // Number of slices
        
        // Pixel/Voxel information
        public float PixelSize { get; set; } // In-plane pixel size
        public float SliceThickness { get; set; } // Distance between slices
        public string Unit { get; set; } = "mm";
        public int BitDepth { get; set; } = 16;
        
        // CT-specific properties
        public int BinningSize { get; set; } = 1;
        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        
        // File paths for the image stack
        public List<string> ImagePaths { get; set; } = new List<string>();

        public CtImageStackDataset(string name, string folderPath) : base(name, folderPath)
        {
            Type = DatasetType.CtImageStack;
        }

        public override long GetSizeInBytes()
        {
            // Calculate total size of all image files
            long totalSize = 0;
            foreach (var path in ImagePaths)
            {
                if (File.Exists(path))
                {
                    totalSize += new FileInfo(path).Length;
                }
            }
            return totalSize;
        }

        public override void Load()
        {
            // Load metadata or prepare for slice loading
            // Actual image data loading would be done on-demand per slice
        }

        public override void Unload()
        {
            // Clean up any cached data
        }
        
        public object ToSerializableObject()
        {
            return new CtImageStackDatasetDTO
            {
                TypeName = nameof(CtImageStackDataset),
                Name = this.Name,
                FilePath = this.FilePath,
                PixelSize = this.PixelSize,
                SliceThickness = this.SliceThickness,
                Unit = this.Unit,
                BinningSize = this.BinningSize
            };
        }
    }
}