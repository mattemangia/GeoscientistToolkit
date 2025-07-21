// GeoscientistToolkit/Data/Image/ImageDataset.cs
using System;
using System.IO;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Image
{
    public class ImageDataset : Dataset, IDisposable, ISerializableDataset
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int BitDepth { get; set; }
        public float PixelSize { get; set; } // In micrometers, or 0 if not specified
        public string Unit { get; set; }

        public byte[] ImageData { get; private set; }
        
        // --- Added for Histogram ---
        public float[] HistogramLuminance { get; private set; }
        public float[] HistogramR { get; private set; }
        public float[] HistogramG { get; private set; }
        public float[] HistogramB { get; private set; }
        // -------------------------

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
                
                // Calculate histogram after loading data
                CalculateHistograms();
            }
        }

        public override void Unload()
        {
            // Respect the lazy loading setting. If enabled, we dump the image data from memory.
            if (SettingsManager.Instance.Settings.Performance.EnableLazyLoading)
            {
                ImageData = null;
                
                // Clear histogram data
                HistogramLuminance = null;
                HistogramR = null;
                HistogramG = null;
                HistogramB = null;

                GC.Collect();
            }
        }
        
        private void CalculateHistograms()
        {
            if (ImageData == null || ImageData.Length == 0) return;

            // Initialize histogram arrays (256 bins for 8-bit channels)
            HistogramLuminance = new float[256];
            HistogramR = new float[256];
            HistogramG = new float[256];
            HistogramB = new float[256];

            int pixelCount = Width * Height;
            for (int i = 0; i < pixelCount * 4; i += 4)
            {
                byte r = ImageData[i];
                byte g = ImageData[i + 1];
                byte b = ImageData[i + 2];
                // Alpha (ImageData[i + 3]) is ignored

                // Increment channel histograms
                HistogramR[r]++;
                HistogramG[g]++;
                HistogramB[b]++;

                // Calculate and bin luminance (standard formula)
                float luminance = 0.299f * r + 0.587f * g + 0.114f * b;
                HistogramLuminance[(int)luminance]++;
            }
        }

        public void Dispose() => Unload();

        public object ToSerializableObject()
        {
            return new ImageDatasetDTO
            {
                TypeName = nameof(ImageDataset),
                Name = this.Name,
                FilePath = this.FilePath,
                PixelSize = this.PixelSize,
                Unit = this.Unit
            };
        }
    }
}