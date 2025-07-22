// GeoscientistToolkit/Data/CtImageStack/CtImageStackDataset.cs
using GeoscientistToolkit.Data.VolumeData;

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
        
        // Volume data
        private ChunkedVolume _volumeData;
        public ChunkedVolume VolumeData => _volumeData;

        public CtImageStackDataset(string name, string folderPath) : base(name, folderPath)
        {
            Type = DatasetType.CtImageStack;
        }

        public override long GetSizeInBytes()
        {
            // Check for volume file first
            string volumePath = GetVolumePath();
            if (File.Exists(volumePath))
            {
                return new FileInfo(volumePath).Length;
            }
            
            // Otherwise calculate total size of all image files
            long totalSize = 0;
            
            if (Directory.Exists(FilePath))
            {
                var imageFiles = Directory.GetFiles(FilePath)
                    .Where(f => IsImageFile(f))
                    .ToList();
                    
                foreach (var file in imageFiles)
                {
                    totalSize += new FileInfo(file).Length;
                }
            }
            
            return totalSize;
        }

        public override void Load()
        {
            if (_volumeData != null) return; // Already loaded
            
            var volumePath = GetVolumePath();
            if (File.Exists(volumePath))
            {
                // Load the volume asynchronously
                var loadTask = ChunkedVolume.LoadFromBinAsync(volumePath, false);
                _volumeData = loadTask.GetAwaiter().GetResult();
                
                // Update dimensions if needed
                if (_volumeData != null)
                {
                    Width = _volumeData.Width;
                    Height = _volumeData.Height;
                    Depth = _volumeData.Depth;
                }
            }
        }

        public override void Unload()
        {
            _volumeData?.Dispose();
            _volumeData = null;
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
        
        private string GetVolumePath()
        {
            string folderName = Path.GetFileName(FilePath);
            return Path.Combine(FilePath, $"{folderName}.Volume.bin");
        }
        
        private bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || 
                   ext == ".bmp" || ext == ".tif" || ext == ".tiff" || 
                   ext == ".tga" || ext == ".gif";
        }
    }
}