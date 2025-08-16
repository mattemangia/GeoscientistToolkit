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
        
        // --- Segmentation Integration ---
        public ImageSegmentationData Segmentation { get; private set; }
        public bool HasSegmentation => Segmentation != null;
        private string _segmentationPath;
        
        // --- Histogram ---
        public float[] HistogramLuminance { get; private set; }
        public float[] HistogramR { get; private set; }
        public float[] HistogramG { get; private set; }
        public float[] HistogramB { get; private set; }

        public ImageDataset(string name, string filePath) : base(name, filePath)
        {
            Type = DatasetType.SingleImage;
        }

        public override long GetSizeInBytes()
        {
            long size = 0;
            if (File.Exists(FilePath)) 
                size += new FileInfo(FilePath).Length;
            
            // Include segmentation file size if it exists
            if (!string.IsNullOrEmpty(_segmentationPath) && File.Exists(_segmentationPath))
                size += new FileInfo(_segmentationPath).Length;
                
            return size;
        }

        public override void Load()
        {
            if (ImageData != null) return;
            
            // Load the image into memory using the StbImageSharp loader
            var imageInfo = ImageLoader.LoadImage(FilePath);
            if (imageInfo != null)
            {
                ImageData = imageInfo.Data;
                Width = imageInfo.Width;
                Height = imageInfo.Height;
                
                // Calculate histogram after loading data
                CalculateHistograms();
                
                // Load segmentation if it exists
                LoadSegmentation();
            }
        }

        public override void Unload()
        {
            // Respect the lazy loading setting
            if (SettingsManager.Instance.Settings.Performance.EnableLazyLoading)
            {
                ImageData = null;
                
                // Clear histogram data
                HistogramLuminance = null;
                HistogramR = null;
                HistogramG = null;
                HistogramB = null;

                // Don't unload segmentation - it's lightweight and may be needed
                // Users can explicitly dispose it if needed
                
                GC.Collect();
            }
        }
        
        /// <summary>
        /// Initialize or get the segmentation data for this image
        /// </summary>
        public ImageSegmentationData GetOrCreateSegmentation()
        {
            if (Segmentation == null)
            {
                Segmentation = new ImageSegmentationData(Width, Height);
                
                // Add default materials
                Segmentation.AddMaterial("Region 1", new System.Numerics.Vector4(1, 0, 0, 0.5f));
                Segmentation.AddMaterial("Region 2", new System.Numerics.Vector4(0, 1, 0, 0.5f));
                Segmentation.AddMaterial("Region 3", new System.Numerics.Vector4(0, 0, 1, 0.5f));
            }
            return Segmentation;
        }
        
        /// <summary>
        /// Load segmentation from a file
        /// </summary>
        public void LoadSegmentationFromFile(string path)
        {
            if (!File.Exists(path))
            {
                Logger.LogWarning($"Segmentation file not found: {path}");
                return;
            }
            
            var imported = ImageSegmentationExporter.ImportLabeledImage(path, Width, Height);
            if (imported != null)
            {
                Segmentation = imported;
                _segmentationPath = path;
                Logger.Log($"Loaded segmentation from: {path}");
            }
        }
        
        /// <summary>
        /// Save segmentation to a file
        /// </summary>
        public void SaveSegmentation(string path)
        {
            if (Segmentation == null)
            {
                Logger.LogWarning("No segmentation data to save");
                return;
            }
            
            ImageSegmentationExporter.ExportLabeledImage(Segmentation, path);
            _segmentationPath = path;
            Logger.Log($"Saved segmentation to: {path}");
        }
        
        /// <summary>
        /// Load segmentation from default location (same folder as image with .labels.png extension)
        /// </summary>
        private void LoadSegmentation()
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            
            // Check for segmentation file with standard naming
            string defaultSegPath = Path.ChangeExtension(FilePath, ".labels.png");
            if (File.Exists(defaultSegPath))
            {
                LoadSegmentationFromFile(defaultSegPath);
            }
            else
            {
                // Also check for .labels.tiff
                defaultSegPath = Path.ChangeExtension(FilePath, ".labels.tiff");
                if (File.Exists(defaultSegPath))
                {
                    LoadSegmentationFromFile(defaultSegPath);
                }
            }
        }
        
        /// <summary>
        /// Clear the segmentation data
        /// </summary>
        public void ClearSegmentation()
        {
            Segmentation?.Dispose();
            Segmentation = null;
            _segmentationPath = null;
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

        public void Dispose()
        {
            Unload();
            ClearSegmentation();
        }

        public object ToSerializableObject()
        {
            return new ImageDatasetDTO
            {
                TypeName = nameof(ImageDataset),
                Name = this.Name,
                FilePath = this.FilePath,
                PixelSize = this.PixelSize,
                Unit = this.Unit,
                SegmentationPath = this._segmentationPath
                // Metadata will be handled by ProjectSerializer
            };
        }
        
        /// <summary>
        /// Create a standalone segmentation dataset (without background image)
        /// </summary>
        public static ImageDataset CreateSegmentationDataset(string name, string segmentationPath)
        {
            if (!File.Exists(segmentationPath))
            {
                throw new FileNotFoundException($"Segmentation file not found: {segmentationPath}");
            }
            
            // Load the segmentation file to get dimensions
            var imageInfo = ImageLoader.LoadImageInfo(segmentationPath);
            if (imageInfo == null)
            {
                throw new InvalidOperationException($"Could not load segmentation file: {segmentationPath}");
            }
            
            // Create a dataset without a source image
            var dataset = new ImageDataset(name, null)
            {
                Width = imageInfo.Width,
                Height = imageInfo.Height,
                BitDepth = 32, // RGBA
                _segmentationPath = segmentationPath
            };
            
            // Load the segmentation data
            dataset.Segmentation = ImageSegmentationExporter.ImportLabeledImage(
                segmentationPath, imageInfo.Width, imageInfo.Height);
            
            return dataset;
        }
    }
}