// GeoscientistToolkit/Data/Image/ImageDataset.cs
using System;
using System.IO;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Image
{
    public class ImageDataset : Dataset, IDisposable
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int BitDepth { get; set; }
        public float PixelSize { get; set; } // In micrometers, or 0 if not specified
        public string Unit { get; set; }

        public byte[] ImageData { get; private set; }

        public ImageDataset(string name, string filePath) : base(name, filePath)
        {
            Type = DatasetType.SingleImage;
        }

        public override long GetSizeInBytes()
        {
            if (File.Exists(FilePath)) return new FileInfo(FilePath).Length;
            return 0;
        }

        public override void Load()
        {
            if (ImageData != null) return;
            
            // Load the image into memory using the StbImageSharp loader.
            var imageInfo = ImageLoader.LoadImage(FilePath);
            if (imageInfo != null)
            {
                ImageData = imageInfo.Data;
                Width = imageInfo.Width;
                Height = imageInfo.Height;
            }
        }

        public override void Unload()
        {
            ImageData = null;
            GC.Collect();
        }

        public void Dispose() => Unload();
    }
}