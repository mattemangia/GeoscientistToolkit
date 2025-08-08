// GeoscientistToolkit/Data/Loaders/CTStackLoaderWrapper.cs
using System;
using System.IO;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders
{
    public class CTStackLoaderWrapper : IDataLoader
    {
        public enum LoadMode
        {
            OptimizedFor3D,
            LegacyFor2D
        }
        
        public string Name => Mode == LoadMode.OptimizedFor3D ? 
            "CT Image Stack (Optimized for 3D Streaming)" : 
            "CT Image Stack (Legacy for 2D Editing)";
            
        public string Description => Mode == LoadMode.OptimizedFor3D ?
            "Import CT stack optimized for 3D viewing with streaming capabilities" :
            "Import CT stack for 2D editing and segmentation";
        
        public LoadMode Mode { get; set; } = LoadMode.OptimizedFor3D;
        public string SourcePath { get; set; } = "";
        public bool IsMultiPageTiff { get; set; } = false;
        public float PixelSize { get; set; } = 1.0f;
        public PixelSizeUnit Unit { get; set; } = PixelSizeUnit.Micrometers;
        public int BinningFactor { get; set; } = 1;
        
        // For returning both datasets when in optimized mode
        public CtImageStackDataset LegacyDataset { get; private set; }
        public StreamingCtVolumeDataset StreamingDataset { get; private set; }
        
        public bool CanImport
        {
            get
            {
                if (string.IsNullOrEmpty(SourcePath))
                    return false;
                    
                if (IsMultiPageTiff)
                {
                    return File.Exists(SourcePath) && ImageLoader.IsMultiPageTiff(SourcePath);
                }
                else
                {
                    return Directory.Exists(SourcePath);
                }
            }
        }
        
        public string ValidationMessage
        {
            get
            {
                if (string.IsNullOrEmpty(SourcePath))
                    return "Please select a source for the CT stack";
                    
                if (IsMultiPageTiff)
                {
                    if (!File.Exists(SourcePath))
                        return "Selected file does not exist";
                    if (!ImageLoader.IsMultiPageTiff(SourcePath))
                        return "Selected TIFF file contains only one page. CT stacks require multiple pages.";
                }
                else
                {
                    if (!Directory.Exists(SourcePath))
                        return "Selected folder does not exist";
                        
                    var info = GetStackInfo();
                    if (info.SliceCount == 0)
                        return "No supported image files found in this folder";
                }
                
                return null;
            }
        }
        
        public StackInfo GetStackInfo()
        {
            try
            {
                if (IsMultiPageTiff && File.Exists(SourcePath))
                {
                    if (ImageLoader.IsMultiPageTiff(SourcePath))
                    {
                        int pageCount = ImageLoader.GetTiffPageCount(SourcePath);
                        var imageInfo = ImageLoader.LoadImageInfo(SourcePath);
                        long fileSize = new FileInfo(SourcePath).Length;
                        
                        return new StackInfo
                        {
                            SliceCount = pageCount,
                            Width = imageInfo.Width,
                            Height = imageInfo.Height,
                            TotalSize = fileSize,
                            FileName = Path.GetFileName(SourcePath)
                        };
                    }
                }
                else if (!IsMultiPageTiff && Directory.Exists(SourcePath))
                {
                    var files = Directory.GetFiles(SourcePath)
                        .Where(ImageLoader.IsSupportedImageFile)
                        .ToArray();
                        
                    return new StackInfo
                    {
                        SliceCount = files.Length,
                        TotalSize = files.Sum(f => new FileInfo(f).Length),
                        FileName = Path.GetFileName(SourcePath)
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CTStackLoader] Error getting stack info: {ex.Message}");
            }
            
            return new StackInfo();
        }
        
        public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
        {
            if (Mode == LoadMode.OptimizedFor3D)
            {
                await LoadOptimizedStackAsync(progressReporter);
                return StreamingDataset; // Return the main dataset
            }
            else
            {
                return await LoadLegacyStackAsync(progressReporter);
            }
        }
        
        private async Task LoadOptimizedStackAsync(IProgress<(float progress, string message)> progressReporter)
        {
            string name = IsMultiPageTiff ? 
                Path.GetFileNameWithoutExtension(SourcePath) : 
                Path.GetFileName(SourcePath);
                
            string outputDir = IsMultiPageTiff ? 
                Path.GetDirectoryName(SourcePath) : 
                SourcePath;
                
            string gvtFileName = BinningFactor > 1 ? 
                $"{name}_bin{BinningFactor}.gvt" : 
                $"{name}.gvt";
                
            string gvtPath = Path.Combine(outputDir, gvtFileName);
            
            // Step 1: Load volume data
            progressReporter?.Report((0.0f, "Loading volume data..."));
            
            var loadProgress = new Progress<float>(p => 
                progressReporter?.Report((p * 0.5f, $"Step 1/2: Loading and processing images... {(int)(p * 100)}%")));
            
            var binnedVolume = await CTStackLoader.LoadCTStackAsync(
                SourcePath, 
                GetPixelSizeInMeters(), 
                BinningFactor, 
                false,
                loadProgress, 
                name);
            
            if (binnedVolume == null || binnedVolume.Width == 0 || binnedVolume.Height == 0 || binnedVolume.Depth == 0)
            {
                throw new InvalidOperationException("Failed to load volume data");
            }
            
            Logger.Log($"[CTStackLoader] Loaded volume: {binnedVolume.Width}×{binnedVolume.Height}×{binnedVolume.Depth}");
            
            // Step 2: Convert to optimized format if needed
            if (!File.Exists(gvtPath))
            {
                progressReporter?.Report((0.5f, "Converting to optimized format..."));
                
                await CtStackConverter.ConvertToStreamableFormat(
                    binnedVolume, 
                    gvtPath,
                    (p, s) => progressReporter?.Report((0.5f + (p * 0.5f), $"Step 2/2: {s}")));
            }
            else
            {
                progressReporter?.Report((1.0f, "Found existing optimized file. Loading..."));
            }
            
            // Create legacy dataset for editing
            double pixelSizeMicrons = (Unit == PixelSizeUnit.Micrometers ? PixelSize : PixelSize * 1000) * BinningFactor;
            
            LegacyDataset = new CtImageStackDataset($"{name} (2D Edit & Segment)", SourcePath)
            {
                Width = binnedVolume.Width,
                Height = binnedVolume.Height,
                Depth = binnedVolume.Depth,
                PixelSize = (float)pixelSizeMicrons,
                SliceThickness = (float)pixelSizeMicrons,
                Unit = "µm",
                BinningSize = BinningFactor
            };
            
            // Create streaming dataset for 3D viewing
            StreamingDataset = new StreamingCtVolumeDataset($"{name} (3D View)", gvtPath)
            {
                EditablePartner = LegacyDataset
            };
            
            progressReporter?.Report((1.0f, "Optimized dataset and editable partner added to project!"));
        }
        
        private async Task<Dataset> LoadLegacyStackAsync(IProgress<(float progress, string message)> progressReporter)
        {
            return await Task.Run(async () =>
            {
                string name = IsMultiPageTiff ? 
                    Path.GetFileNameWithoutExtension(SourcePath) : 
                    Path.GetFileName(SourcePath);
                    
                var loadProgress = new Progress<float>(p => 
                    progressReporter?.Report((p, $"Processing images... {(int)(p * 100)}%")));
                
                var volume = await CTStackLoader.LoadCTStackAsync(
                    SourcePath, 
                    GetPixelSizeInMeters(), 
                    BinningFactor, 
                    false, 
                    loadProgress, 
                    name);
                
                double pixelSizeMicrons = (Unit == PixelSizeUnit.Micrometers ? PixelSize : PixelSize * 1000) * BinningFactor;
                
                var dataset = new CtImageStackDataset($"{name} (2D Edit & Segment)", SourcePath)
                {
                    Width = volume.Width,
                    Height = volume.Height,
                    Depth = volume.Depth,
                    PixelSize = (float)pixelSizeMicrons,
                    SliceThickness = (float)pixelSizeMicrons,
                    Unit = "µm",
                    BinningSize = BinningFactor
                };
                
                progressReporter?.Report((1.0f, "Legacy dataset added to project!"));
                
                return dataset;
            });
        }
        
        private double GetPixelSizeInMeters()
        {
            return Unit == PixelSizeUnit.Micrometers ? 
                PixelSize * 1e-6 :  // micrometers to meters
                PixelSize * 1e-3;   // millimeters to meters
        }
        
        public void Reset()
        {
            SourcePath = "";
            IsMultiPageTiff = false;
            PixelSize = 1.0f;
            Unit = PixelSizeUnit.Micrometers;
            BinningFactor = 1;
            LegacyDataset = null;
            StreamingDataset = null;
        }
        
        public class StackInfo
        {
            public int SliceCount { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public long TotalSize { get; set; }
            public string FileName { get; set; }
        }
    }
}