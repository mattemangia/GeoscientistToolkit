// GeoscientistToolkit/Data/Loaders/LabeledVolumeLoaderWrapper.cs
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.Image;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Loaders
{
    public class LabeledVolumeLoaderWrapper : IDataLoader
    {
        public string Name => "Labeled Volume Stack (Color-coded Materials)";
        public string Description => "Import a stack of labeled images where each unique color represents a different material";
        
        public string SourcePath { get; set; } = "";
        public bool IsMultiPageTiff { get; set; } = false;
        public float PixelSize { get; set; } = 1.0f;
        public PixelSizeUnit Unit { get; set; } = PixelSizeUnit.Micrometers;
        
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
                    return "Please select a source for the labeled volume";
                    
                if (IsMultiPageTiff)
                {
                    if (!File.Exists(SourcePath))
                        return "Selected file does not exist";
                    if (!ImageLoader.IsMultiPageTiff(SourcePath))
                        return "Selected TIFF file contains only one page.";
                }
                else
                {
                    if (!Directory.Exists(SourcePath))
                        return "Selected folder does not exist";
                        
                    var info = GetVolumeInfo();
                    if (info.SliceCount == 0)
                        return "No supported image files found in this folder";
                }
                
                return null;
            }
        }
        
        public VolumeInfo GetVolumeInfo()
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
                        
                        return new VolumeInfo
                        {
                            SliceCount = pageCount,
                            Width = imageInfo.Width,
                            Height = imageInfo.Height,
                            TotalSize = fileSize,
                            FileName = Path.GetFileName(SourcePath),
                            IsReady = true
                        };
                    }
                }
                else if (!IsMultiPageTiff && Directory.Exists(SourcePath))
                {
                    var files = Directory.GetFiles(SourcePath)
                        .Where(ImageLoader.IsSupportedImageFile)
                        .ToArray();
                        
                    return new VolumeInfo
                    {
                        SliceCount = files.Length,
                        TotalSize = files.Sum(f => new FileInfo(f).Length),
                        FileName = Path.GetFileName(SourcePath),
                        IsReady = files.Length > 0
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[LabeledVolumeLoader] Error getting volume info: {ex.Message}");
            }
            
            return new VolumeInfo();
        }
        
        public async Task<Dataset> LoadAsync(IProgress<(float progress, string message)> progressReporter)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    progressReporter?.Report((0.1f, "Loading labeled volume..."));
                    
                    string name = IsMultiPageTiff ? 
                        Path.GetFileNameWithoutExtension(SourcePath) : 
                        Path.GetFileName(SourcePath);
                    
                    double pixelSizeMeters = Unit == PixelSizeUnit.Micrometers ? 
                        PixelSize * 1e-6 :  // micrometers to meters
                        PixelSize * 1e-3;   // millimeters to meters
                    
                    // Load the labeled volume
                    var loadProgress = new Progress<float>(p =>
                        progressReporter?.Report((p, $"Processing labeled images... {(int)(p * 100)}%")));
                    
                    var (grayscaleVolume, labelVolume, materials) = await LabeledVolumeLoader.LoadLabeledVolumeAsync(
                        SourcePath, 
                        pixelSizeMeters, 
                        false, // Don't use memory mapping for now
                        loadProgress,
                        name);
                    
                    progressReporter?.Report((0.9f, "Creating dataset..."));
                    
                    // Create the dataset
                    double pixelSizeMicrons = Unit == PixelSizeUnit.Micrometers ? PixelSize : PixelSize * 1000;
                    
                    var dataset = new CtImageStackDataset($"{name} (Labeled)", SourcePath)
                    {
                        Width = grayscaleVolume.Width,
                        Height = grayscaleVolume.Height,
                        Depth = grayscaleVolume.Depth,
                        PixelSize = (float)pixelSizeMicrons,
                        SliceThickness = (float)pixelSizeMicrons,
                        Unit = "µm",
                        BinningSize = 1,
                        Materials = materials
                    };
                    
                    // Save materials to file so they persist
                    dataset.SaveMaterials();
                    
                    progressReporter?.Report((1.0f, $"Labeled volume imported successfully! Found {materials.Count} unique materials."));
                    
                    return dataset;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[LabeledVolumeLoader] Error importing volume: {ex}");
                    throw new Exception($"Failed to import labeled volume: {ex.Message}", ex);
                }
            });
        }
        
        public void Reset()
        {
            SourcePath = "";
            IsMultiPageTiff = false;
            PixelSize = 1.0f;
            Unit = PixelSizeUnit.Micrometers;
        }
        
        public class VolumeInfo
        {
            public int SliceCount { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public long TotalSize { get; set; }
            public string FileName { get; set; }
            public bool IsReady { get; set; }
        }
    }
}