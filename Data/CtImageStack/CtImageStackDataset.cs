// GeoscientistToolkit/Data/CtImageStack/CtImageStackDataset.cs
namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackDataset : Dataset, IDisposable
    {
        public int BinningSize { get; set; } = 1;
        public bool LoadFullInMemory { get; set; } = true;
        public float PixelSize { get; set; } = 1.0f;
        public string Unit { get; set; } = "micrometers";
        
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Depth { get; private set; }
        public int BytesPerPixel { get; private set; }
        
        private byte[] _imageData;
        private bool _isLoaded = false;

        public CtImageStackDataset(string name, string folderPath) : base(name, folderPath)
        {
            Type = DatasetType.CtImageStack;
        }

        public override long GetSizeInBytes()
        {
            if (!_isLoaded)
                return EstimateSize();
            
            return _imageData?.Length ?? 0;
        }

        private long EstimateSize()
        {
            if (Directory.Exists(FilePath))
            {
                var imageFiles = Directory.GetFiles(FilePath, "*.tif*")
                    .Concat(Directory.GetFiles(FilePath, "*.png"))
                    .Concat(Directory.GetFiles(FilePath, "*.jpg"))
                    .ToArray();
                
                if (imageFiles.Length > 0)
                {
                    var firstFileSize = new FileInfo(imageFiles[0]).Length;
                    return firstFileSize * imageFiles.Length;
                }
            }
            return 0;
        }

        public override void Load()
        {
            if (_isLoaded) return;
            // TODO: Implement image loading logic.
            _isLoaded = true;
        }

        public override void Unload()
        {
            _imageData = null;
            _isLoaded = false;
            GC.Collect();
        }

        public void Dispose() => Unload();
    }
}