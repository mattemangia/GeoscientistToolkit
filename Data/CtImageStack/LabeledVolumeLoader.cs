// GeoscientistToolkit/Data/CtImageStack/LabeledVolumeLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using StbImageSharp;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Handles loading of labeled volume stacks where each unique color represents a material.
    /// Creates materials automatically and generates both label and grayscale volumes.
    /// </summary>
    public static class LabeledVolumeLoader
    {
        /// <summary>
        /// Loads a labeled volume stack from either a folder of images or a multi-page TIFF.
        /// </summary>
        public static async Task<(ChunkedVolume grayscaleVolume, ChunkedLabelVolume labelVolume, List<Material> materials)> 
            LoadLabeledVolumeAsync(
                string path, 
                double pixelSize,
                bool useMemoryMapping,
                IProgress<float> progress = null,
                string datasetName = null)
        {
            // Check if path is a file or directory
            if (File.Exists(path))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if ((ext == ".tif" || ext == ".tiff") && ImageLoader.IsMultiPageTiff(path))
                {
                    return await LoadLabeledVolumeFromMultiPageTiffAsync(
                        path, pixelSize, useMemoryMapping, progress, datasetName);
                }
                else
                {
                    throw new ArgumentException("The specified file is not a multi-page TIFF file.");
                }
            }
            else if (Directory.Exists(path))
            {
                return await LoadLabeledVolumeFromFolderAsync(
                    path, pixelSize, useMemoryMapping, progress, datasetName);
            }
            else
            {
                throw new FileNotFoundException("The specified path does not exist.");
            }
        }

        /// <summary>
        /// Loads labeled volume from a folder of images.
        /// </summary>
        private static async Task<(ChunkedVolume, ChunkedLabelVolume, List<Material>)> 
            LoadLabeledVolumeFromFolderAsync(
                string folderPath,
                double pixelSize,
                bool useMemoryMapping,
                IProgress<float> progress,
                string datasetName)
        {
            Logger.Log($"[LabeledVolumeLoader] Loading labeled volume from folder: {folderPath}");
            
            if (string.IsNullOrEmpty(datasetName))
            {
                datasetName = Path.GetFileName(folderPath);
            }

            // Get all image files and sort them
            var imageFiles = GetSupportedImageFiles(folderPath);
            if (imageFiles.Count == 0)
            {
                throw new FileNotFoundException("No supported image files found in the specified folder.");
            }
            
            imageFiles = SortFilesNumerically(imageFiles);
            Logger.Log($"[LabeledVolumeLoader] Found {imageFiles.Count} images");

            // Get dimensions from first image
            var firstImageInfo = ImageLoader.LoadImageInfo(imageFiles[0]);
            int width = firstImageInfo.Width;
            int height = firstImageInfo.Height;
            int depth = imageFiles.Count;

            // Process images to extract unique colors and create volumes
            return await ProcessLabeledImagesAsync(
                imageFiles, width, height, depth, pixelSize, 
                useMemoryMapping, folderPath, datasetName, progress);
        }

        /// <summary>
        /// Loads labeled volume from a multi-page TIFF.
        /// </summary>
        private static async Task<(ChunkedVolume, ChunkedLabelVolume, List<Material>)> 
            LoadLabeledVolumeFromMultiPageTiffAsync(
                string tiffPath,
                double pixelSize,
                bool useMemoryMapping,
                IProgress<float> progress,
                string datasetName)
        {
            Logger.Log($"[LabeledVolumeLoader] Loading labeled volume from multi-page TIFF: {tiffPath}");
            
            if (string.IsNullOrEmpty(datasetName))
            {
                datasetName = Path.GetFileNameWithoutExtension(tiffPath);
            }

            string parentDir = Path.GetDirectoryName(tiffPath);
            
            // Get dimensions
            int pageCount = ImageLoader.GetTiffPageCount(tiffPath);
            var firstPageInfo = ImageLoader.LoadImageInfo(tiffPath);
            int width = firstPageInfo.Width;
            int height = firstPageInfo.Height;
            int depth = pageCount;

            // Process TIFF pages to extract unique colors and create volumes
            return await ProcessLabeledTiffAsync(
                tiffPath, width, height, depth, pixelSize, 
                useMemoryMapping, parentDir, datasetName, progress);
        }

        /// <summary>
        /// Process labeled images from a folder.
        /// </summary>
        private static async Task<(ChunkedVolume, ChunkedLabelVolume, List<Material>)> 
            ProcessLabeledImagesAsync(
                List<string> imageFiles,
                int width, int height, int depth,
                double pixelSize,
                bool useMemoryMapping,
                string outputDir,
                string datasetName,
                IProgress<float> progress)
        {
            return await Task.Run(async () =>
            {
                Logger.Log($"[LabeledVolumeLoader] Processing {depth} labeled images ({width}×{height})");
                
                // Step 1: Scan all images to find unique colors
                progress?.Report(0.1f);
                var colorToMaterialMap = new Dictionary<uint, byte>();
                var materials = new List<Material>();
                
                Logger.Log("[LabeledVolumeLoader] Scanning for unique colors...");
                await ScanForUniqueColorsInFolder(imageFiles, colorToMaterialMap, materials, 
                    p => progress?.Report(0.1f + p * 0.3f));
                
                Logger.Log($"[LabeledVolumeLoader] Found {materials.Count} unique materials");

                // Step 2: Create volumes
                string volumePath = Path.Combine(outputDir, $"{datasetName}.Volume.bin");
                string labelPath = Path.Combine(outputDir, $"{datasetName}.Labels.bin");
                
                ChunkedVolume grayscaleVolume;
                ChunkedLabelVolume labelVolume;
                
                if (useMemoryMapping)
                {
                    await CreateMemoryMappedFileAsync(volumePath, width, height, depth, 
                        ChunkedVolume.DEFAULT_CHUNK_DIM, pixelSize);
                    await CreateMemoryMappedLabelFileAsync(labelPath, width, height, depth, 
                        ChunkedVolume.DEFAULT_CHUNK_DIM);
                    
                    grayscaleVolume = await ChunkedVolume.LoadFromBinAsync(volumePath, true);
                    labelVolume = ChunkedLabelVolume.LoadFromBin(labelPath, true);
                }
                else
                {
                    grayscaleVolume = new ChunkedVolume(width, height, depth, ChunkedVolume.DEFAULT_CHUNK_DIM)
                    {
                        PixelSize = pixelSize
                    };
                    labelVolume = new ChunkedLabelVolume(width, height, depth, ChunkedVolume.DEFAULT_CHUNK_DIM, false);
                }

                // Step 3: Process each image and populate volumes
                Logger.Log("[LabeledVolumeLoader] Processing images and creating volumes...");
                for (int z = 0; z < imageFiles.Count; z++)
                {
                    await ProcessLabeledImageSlice(imageFiles[z], z, grayscaleVolume, labelVolume, 
                        colorToMaterialMap, materials);
                    
                    float progressVal = 0.4f + (float)(z + 1) / imageFiles.Count * 0.5f;
                    progress?.Report(progressVal);
                }

                // Step 4: Save volumes if not memory mapped
                if (!useMemoryMapping)
                {
                    progress?.Report(0.95f);
                    await grayscaleVolume.SaveAsBinAsync(volumePath);
                    labelVolume.SaveAsBin(labelPath);
                }

                progress?.Report(1.0f);
                Logger.Log($"[LabeledVolumeLoader] Successfully created volumes with {materials.Count} materials");
                
                return (grayscaleVolume, labelVolume, materials);
            });
        }

        /// <summary>
        /// Process labeled multi-page TIFF.
        /// </summary>
        private static async Task<(ChunkedVolume, ChunkedLabelVolume, List<Material>)> 
            ProcessLabeledTiffAsync(
                string tiffPath,
                int width, int height, int depth,
                double pixelSize,
                bool useMemoryMapping,
                string outputDir,
                string datasetName,
                IProgress<float> progress)
        {
            return await Task.Run(async () =>
            {
                Logger.Log($"[LabeledVolumeLoader] Processing labeled TIFF with {depth} pages ({width}×{height})");
                
                // Step 1: Scan all pages to find unique colors
                progress?.Report(0.1f);
                var colorToMaterialMap = new Dictionary<uint, byte>();
                var materials = new List<Material>();
                
                Logger.Log("[LabeledVolumeLoader] Scanning TIFF pages for unique colors...");
                await ScanForUniqueColorsInTiff(tiffPath, depth, colorToMaterialMap, materials, 
                    p => progress?.Report(0.1f + p * 0.3f));
                
                Logger.Log($"[LabeledVolumeLoader] Found {materials.Count} unique materials");

                // Step 2: Create volumes
                string volumePath = Path.Combine(outputDir, $"{datasetName}.Volume.bin");
                string labelPath = Path.Combine(outputDir, $"{datasetName}.Labels.bin");
                
                ChunkedVolume grayscaleVolume;
                ChunkedLabelVolume labelVolume;
                
                if (useMemoryMapping)
                {
                    await CreateMemoryMappedFileAsync(volumePath, width, height, depth, 
                        ChunkedVolume.DEFAULT_CHUNK_DIM, pixelSize);
                    await CreateMemoryMappedLabelFileAsync(labelPath, width, height, depth, 
                        ChunkedVolume.DEFAULT_CHUNK_DIM);
                    
                    grayscaleVolume = await ChunkedVolume.LoadFromBinAsync(volumePath, true);
                    labelVolume = ChunkedLabelVolume.LoadFromBin(labelPath, true);
                }
                else
                {
                    grayscaleVolume = new ChunkedVolume(width, height, depth, ChunkedVolume.DEFAULT_CHUNK_DIM)
                    {
                        PixelSize = pixelSize
                    };
                    labelVolume = new ChunkedLabelVolume(width, height, depth, ChunkedVolume.DEFAULT_CHUNK_DIM, false);
                }

                // Step 3: Process each page and populate volumes
                Logger.Log("[LabeledVolumeLoader] Processing TIFF pages and creating volumes...");
                for (int z = 0; z < depth; z++)
                {
                    await ProcessLabeledTiffPage(tiffPath, z, grayscaleVolume, labelVolume, 
                        colorToMaterialMap, materials);
                    
                    float progressVal = 0.4f + (float)(z + 1) / depth * 0.5f;
                    progress?.Report(progressVal);
                }

                // Step 4: Save volumes if not memory mapped
                if (!useMemoryMapping)
                {
                    progress?.Report(0.95f);
                    await grayscaleVolume.SaveAsBinAsync(volumePath);
                    labelVolume.SaveAsBin(labelPath);
                }

                progress?.Report(1.0f);
                Logger.Log($"[LabeledVolumeLoader] Successfully created volumes with {materials.Count} materials");
                
                return (grayscaleVolume, labelVolume, materials);
            });
        }

        /// <summary>
/// Scan images in a folder for unique colors and create materials.
/// </summary>
private static async Task ScanForUniqueColorsInFolder(
    List<string> imageFiles,
    Dictionary<uint, byte> colorToMaterialMap,
    List<Material> materials,
    Action<float> progressCallback)
{
    // Material 0 = exterior/void
    materials.Add(new Material(0, "Exterior", new Vector4(0, 0, 0, 0)));
    byte nextMaterialId = 1;

    // Sample every N-th image so very large stacks finish quickly
    int sampleRate = Math.Max(1, imageFiles.Count / 20);

    // Declare once so the lambda can fill them in
    int w = 0, h = 0, channels = 0;

    for (int i = 0; i < imageFiles.Count; i += sampleRate)
    {
        // Load slice on a worker thread
        byte[] rgbData = await Task.Run(() =>
            ImageLoader.LoadColorImage(imageFiles[i], out w, out h, out channels));

        // Step through the pixel data using the channel count
        for (int j = 0; j < rgbData.Length; j += channels)
        {
            byte r = rgbData[j];
            byte g = channels > 1 ? rgbData[j + 1] : r;
            byte b = channels > 2 ? rgbData[j + 2] : r;

            uint colorKey = ((uint)r << 16) | ((uint)g << 8) | b;

            // Ignore pure black (commonly background)
            if (colorKey == 0) continue;

            if (!colorToMaterialMap.ContainsKey(colorKey))
            {
                if (nextMaterialId >= 255)
                {
                    Logger.LogWarning("[LabeledVolumeLoader] Maximum number of materials (254) reached!");
                    break;
                }

                colorToMaterialMap[colorKey] = nextMaterialId;

                var color = new Vector4(r / 255f, g / 255f, b / 255f, 1f);
                materials.Add(new Material(nextMaterialId, $"Material_{nextMaterialId}", color));

                nextMaterialId++;
            }
        }

        progressCallback?.Invoke((float)(i + 1) / imageFiles.Count);

        if (nextMaterialId >= 255) break; // Safety exit
    }
}


        /// <summary>
        /// Scan TIFF pages for unique colors and create materials.
        /// </summary>
        private static async Task ScanForUniqueColorsInTiff(
            string tiffPath,
            int pageCount,
            Dictionary<uint, byte> colorToMaterialMap,
            List<Material> materials,
            Action<float> progressCallback)
        {
            // Always add the exterior material (ID 0) first
            materials.Add(new Material(0, "Exterior", new Vector4(0, 0, 0, 0)));
            byte nextMaterialId = 1;

            // Sample every Nth page for performance
            int sampleRate = Math.Max(1, pageCount / 20); // Sample ~20 pages max
            
            await Task.Run(() =>
            {
                for (int i = 0; i < pageCount; i += sampleRate)
                {
                    var rgbData = ImageLoader.LoadTiffPageAsColor(tiffPath, i, 
                        out int w, out int h, out int channels);
                    
                    // Process RGB data to find unique colors
                    for (int j = 0; j < rgbData.Length; j += channels)
                    {
                        byte r = rgbData[j];
                        byte g = channels > 1 ? rgbData[j + 1] : r;
                        byte b = channels > 2 ? rgbData[j + 2] : r;
                        
                        uint colorKey = ((uint)r << 16) | ((uint)g << 8) | (uint)b;
                        
                        // Skip black (usually background)
                        if (colorKey == 0) continue;
                        
                        if (!colorToMaterialMap.ContainsKey(colorKey))
                        {
                            if (nextMaterialId >= 255)
                            {
                                Logger.LogWarning("[LabeledVolumeLoader] Maximum number of materials (254) reached!");
                                break;
                            }
                            
                            colorToMaterialMap[colorKey] = nextMaterialId;
                            
                            // Create material with the color
                            var color = new Vector4(r / 255f, g / 255f, b / 255f, 1.0f);
                            var material = new Material(nextMaterialId, $"Material_{nextMaterialId}", color);
                            materials.Add(material);
                            
                            nextMaterialId++;
                        }
                    }
                    
                    progressCallback?.Invoke((float)(i + 1) / pageCount);
                    
                    if (nextMaterialId >= 255) break;
                }
            });
        }

        /// <summary>
        /// Process a single labeled image slice.
        /// </summary>
        private static async Task ProcessLabeledImageSlice(
            string imagePath,
            int zIndex,
            ChunkedVolume grayscaleVolume,
            ChunkedLabelVolume labelVolume,
            Dictionary<uint, byte> colorToMaterialMap,
            List<Material> materials)
        {
            await Task.Run(() =>
            {
                var rgbData = ImageLoader.LoadColorImage(imagePath, out int width, out int height, out int channels);
                
                var grayscaleSlice = new byte[width * height];
                var labelSlice = new byte[width * height];
                
                for (int i = 0; i < width * height; i++)
                {
                    int pixelIndex = i * channels;
                    byte r = rgbData[pixelIndex];
                    byte g = channels > 1 ? rgbData[pixelIndex + 1] : r;
                    byte b = channels > 2 ? rgbData[pixelIndex + 2] : r;
                    
                    uint colorKey = ((uint)r << 16) | ((uint)g << 8) | (uint)b;
                    
                    // Set label based on color
                    if (colorKey == 0)
                    {
                        labelSlice[i] = 0; // Exterior
                        grayscaleSlice[i] = 0;
                    }
                    else if (colorToMaterialMap.TryGetValue(colorKey, out byte materialId))
                    {
                        labelSlice[i] = materialId;
                        // Create a dummy grayscale value based on material ID
                        // Spread materials across grayscale range
                        grayscaleSlice[i] = (byte)(50 + (materialId * 200 / materials.Count));
                    }
                    else
                    {
                        // Unknown color - assign to exterior
                        labelSlice[i] = 0;
                        grayscaleSlice[i] = 0;
                    }
                }
                
                grayscaleVolume.WriteSliceZ(zIndex, grayscaleSlice);
                labelVolume.WriteSliceZ(zIndex, labelSlice);
            });
        }

        /// <summary>
        /// Process a single labeled TIFF page.
        /// </summary>
        private static async Task ProcessLabeledTiffPage(
            string tiffPath,
            int pageIndex,
            ChunkedVolume grayscaleVolume,
            ChunkedLabelVolume labelVolume,
            Dictionary<uint, byte> colorToMaterialMap,
            List<Material> materials)
        {
            await Task.Run(() =>
            {
                var rgbData = ImageLoader.LoadTiffPageAsColor(tiffPath, pageIndex, 
                    out int width, out int height, out int channels);
                
                var grayscaleSlice = new byte[width * height];
                var labelSlice = new byte[width * height];
                
                for (int i = 0; i < width * height; i++)
                {
                    int pixelIndex = i * channels;
                    byte r = rgbData[pixelIndex];
                    byte g = channels > 1 ? rgbData[pixelIndex + 1] : r;
                    byte b = channels > 2 ? rgbData[pixelIndex + 2] : r;
                    
                    uint colorKey = ((uint)r << 16) | ((uint)g << 8) | (uint)b;
                    
                    // Set label based on color
                    if (colorKey == 0)
                    {
                        labelSlice[i] = 0; // Exterior
                        grayscaleSlice[i] = 0;
                    }
                    else if (colorToMaterialMap.TryGetValue(colorKey, out byte materialId))
                    {
                        labelSlice[i] = materialId;
                        // Create a dummy grayscale value based on material ID
                        // Spread materials across grayscale range
                        grayscaleSlice[i] = (byte)(50 + (materialId * 200 / materials.Count));
                    }
                    else
                    {
                        // Unknown color - assign to exterior
                        labelSlice[i] = 0;
                        grayscaleSlice[i] = 0;
                    }
                }
                
                grayscaleVolume.WriteSliceZ(pageIndex, grayscaleSlice);
                labelVolume.WriteSliceZ(pageIndex, labelSlice);
            });
        }

        /// <summary>
        /// Create a memory-mapped file for grayscale volume.
        /// </summary>
        private static async Task CreateMemoryMappedFileAsync(
            string path, 
            int width, 
            int height, 
            int depth, 
            int chunkDim,
            double pixelSize)
        {
            await Task.Run(() =>
            {
                int cntX = (width + chunkDim - 1) / chunkDim;
                int cntY = (height + chunkDim - 1) / chunkDim;
                int cntZ = (depth + chunkDim - 1) / chunkDim;
                long chunkSize = (long)chunkDim * chunkDim * chunkDim;
                long totalSize = 40 + (cntX * cntY * cntZ * chunkSize); // 40 byte header
                
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    fs.SetLength(totalSize);
                    
                    using (var bw = new BinaryWriter(fs))
                    {
                        bw.Write(width);
                        bw.Write(height);
                        bw.Write(depth);
                        bw.Write(chunkDim);
                        bw.Write(8); // bits per pixel
                        bw.Write(pixelSize);
                        bw.Write(cntX);
                        bw.Write(cntY);
                        bw.Write(cntZ);
                    }
                }
            });
        }

        /// <summary>
        /// Create a memory-mapped file for label volume.
        /// </summary>
        private static async Task CreateMemoryMappedLabelFileAsync(
            string path, 
            int width, 
            int height, 
            int depth, 
            int chunkDim)
        {
            await Task.Run(() =>
            {
                int cntX = (width + chunkDim - 1) / chunkDim;
                int cntY = (height + chunkDim - 1) / chunkDim;
                int cntZ = (depth + chunkDim - 1) / chunkDim;
                long chunkSize = (long)chunkDim * chunkDim * chunkDim;
                long totalSize = 28 + (cntX * cntY * cntZ * chunkSize); // 28 byte header for labels
                
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    fs.SetLength(totalSize);
                    
                    using (var bw = new BinaryWriter(fs))
                    {
                        bw.Write(width);
                        bw.Write(height);
                        bw.Write(depth);
                        bw.Write(chunkDim);
                        bw.Write(cntX);
                        bw.Write(cntY);
                        bw.Write(cntZ);
                    }
                }
            });
        }

        /// <summary>
        /// Get all supported image files from a folder.
        /// </summary>
        private static List<string> GetSupportedImageFiles(string folderPath)
        {
            return Directory.GetFiles(folderPath)
                .Where(f => ImageLoader.IsSupportedImageFile(f))
                .ToList();
        }

        /// <summary>
        /// Sort files numerically based on numbers in filename.
        /// </summary>
        private static List<string> SortFilesNumerically(List<string> files)
        {
            return files.OrderBy(f =>
            {
                string filename = Path.GetFileNameWithoutExtension(f);
                string numbers = new string(filename.Where(char.IsDigit).ToArray());
                
                if (int.TryParse(numbers, out int number))
                    return number;
                
                return 0;
            }).ToList();
        }
    }
}