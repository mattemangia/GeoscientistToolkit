// GeoscientistToolkit/Data/CtImageStack/CTStackLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using StbImageSharp;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Handles loading of CT image stacks with support for binning and progress reporting
    /// </summary>
    public static class CTStackLoader
    {
        /// <summary>
        /// Loads a CT stack from a folder of images
        /// </summary>
        public static async Task<ChunkedVolume> LoadCTStackAsync(
            string folderPath, 
            double pixelSize, 
            int binningFactor,
            bool useMemoryMapping,
            IProgress<float> progress = null,
            string datasetName = null)
        {
            Logger.Log($"[CTStackLoader] Loading CT stack from: {folderPath}");
            Logger.Log($"[CTStackLoader] Pixel size: {pixelSize * 1e6} µm, Binning: {binningFactor}×, Memory mapping: {useMemoryMapping}");

            // Get dataset name
            if (string.IsNullOrEmpty(datasetName))
            {
                datasetName = Path.GetFileName(folderPath);
            }

            // Get all image files
            var imageFiles = GetSupportedImageFiles(folderPath);
            if (imageFiles.Count == 0)
            {
                throw new FileNotFoundException("No supported image files found in the specified folder.");
            }

            // Sort files numerically
            imageFiles = SortFilesNumerically(imageFiles);
            Logger.Log($"[CTStackLoader] Found {imageFiles.Count} images");

            // Check if binning is needed
            if (binningFactor > 1)
            {
                return await LoadWith3DBinningAsync(folderPath, imageFiles, pixelSize, binningFactor, 
                    useMemoryMapping, progress, datasetName);
            }
            else
            {
                return await LoadDirectAsync(folderPath, imageFiles, pixelSize, useMemoryMapping, 
                    progress, datasetName);
            }
        }

        /// <summary>
        /// Loads images directly without binning
        /// </summary>
        private static async Task<ChunkedVolume> LoadDirectAsync(
            string folderPath,
            List<string> imageFiles,
            double pixelSize,
            bool useMemoryMapping,
            IProgress<float> progress,
            string datasetName)
        {
            // Check for existing volume file
            string volumePath = Path.Combine(folderPath, $"{datasetName}.Volume.bin");

            if (File.Exists(volumePath))
            {
                Logger.Log($"[CTStackLoader] Found existing volume file: {volumePath}");
                try
                {
                    var volume = await ChunkedVolume.LoadFromBinAsync(volumePath, useMemoryMapping);
                    volume.PixelSize = pixelSize;
                    progress?.Report(1.0f);
                    return volume;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[CTStackLoader] Error loading existing volume: {ex.Message}");
                    Logger.Log("[CTStackLoader] Regenerating volume from images...");
                }
            }

            // Load from images
            var newVolume = await ChunkedVolume.FromFolderAsync(folderPath, 
                ChunkedVolume.DEFAULT_CHUNK_DIM, useMemoryMapping, progress, datasetName);
            newVolume.PixelSize = pixelSize;

            // Save for future use (only if not memory mapped - it's already saved)
            if (!useMemoryMapping)
            {
                await newVolume.SaveAsBinAsync(volumePath);
            }
            
            // Create empty labels file
            await CreateEmptyLabelsFileAsync(folderPath, newVolume, datasetName);
            
            return newVolume;
        }

        /// <summary>
        /// Loads images with 3D binning applied
        /// </summary>
        private static async Task<ChunkedVolume> LoadWith3DBinningAsync(
            string folderPath,
            List<string> imageFiles,
            double pixelSize,
            int binningFactor,
            bool useMemoryMapping,
            IProgress<float> progress,
            string datasetName)
        {
            Logger.Log($"[CTStackLoader] Applying {binningFactor}× 3D binning");

            // Check for existing binned volume
            string binnedMarker = Path.Combine(folderPath, $"binned_{binningFactor}x3d.marker");
            string volumePath = Path.Combine(folderPath, $"{datasetName}.Volume.bin");

            if (File.Exists(binnedMarker) && File.Exists(volumePath))
            {
                Logger.Log($"[CTStackLoader] Found existing binned volume");
                try
                {
                    var volume = await ChunkedVolume.LoadFromBinAsync(volumePath, useMemoryMapping);
                    volume.PixelSize = pixelSize * binningFactor;
                    progress?.Report(1.0f);
                    return volume;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[CTStackLoader] Error loading existing binned volume: {ex.Message}");
                }
            }

            // Get original dimensions
            var (origWidth, origHeight) = await GetImageDimensionsAsync(imageFiles[0]);
            int origDepth = imageFiles.Count;

            // Calculate binned dimensions - true 3D binning
            int newWidth = Math.Max(1, origWidth / binningFactor);
            int newHeight = Math.Max(1, origHeight / binningFactor);
            int newDepth = Math.Max(1, (origDepth + binningFactor - 1) / binningFactor);

            Logger.Log($"[CTStackLoader] 3D Binning {origWidth}×{origHeight}×{origDepth} → {newWidth}×{newHeight}×{newDepth}");

            // Create binned volume
            ChunkedVolume binnedVolume;
            
            if (useMemoryMapping)
            {
                // Create memory-mapped file
                await CreateMemoryMappedFileAsync(volumePath, newWidth, newHeight, newDepth, 
                    ChunkedVolume.DEFAULT_CHUNK_DIM, pixelSize * binningFactor);
                    
                binnedVolume = await ChunkedVolume.LoadFromBinAsync(volumePath, true);
            }
            else
            {
                binnedVolume = new ChunkedVolume(newWidth, newHeight, newDepth, ChunkedVolume.DEFAULT_CHUNK_DIM)
                {
                    PixelSize = pixelSize * binningFactor
                };
            }

            // Process images with 3D binning
            await Process3DBinningAsync(binnedVolume, imageFiles, binningFactor, progress);

            // Save the binned volume (if not memory mapped)
            if (!useMemoryMapping)
            {
                await binnedVolume.SaveAsBinAsync(volumePath);
            }

            // Create marker file
            await File.WriteAllTextAsync(binnedMarker, 
                $"3D Binned with factor {binningFactor} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"Original: {origWidth}×{origHeight}×{origDepth}\n" +
                $"Binned: {newWidth}×{newHeight}×{newDepth}");
                
            // Create empty labels file
            await CreateEmptyLabelsFileAsync(folderPath, binnedVolume, datasetName);

            return binnedVolume;
        }

        /// <summary>
        /// Process images with true 3D binning
        /// </summary>
        private static async Task Process3DBinningAsync(
            ChunkedVolume volume,
            List<string> imageFiles,
            int binFactor,
            IProgress<float> progress)
        {
            int origDepth = imageFiles.Count;
            int newDepth = volume.Depth;
            int processed = 0;

            // Process each output slice
            var tasks = new List<Task>();
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount);

            for (int newZ = 0; newZ < newDepth; newZ++)
            {
                int z = newZ; // Capture for closure
                
                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await Process3DBinnedSliceAsync(volume, imageFiles, z, binFactor);
                        
                        var currentProgress = Interlocked.Increment(ref processed);
                        progress?.Report((float)currentProgress / newDepth);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Process a single 3D binned slice
        /// </summary>
        private static async Task Process3DBinnedSliceAsync(
            ChunkedVolume volume,
            List<string> imageFiles,
            int newZ,
            int binFactor)
        {
            int srcZStart = newZ * binFactor;
            int srcZEnd = Math.Min(srcZStart + binFactor, imageFiles.Count);

            // Create accumulator for this slice
            float[,] accumulator = new float[volume.Width, volume.Height];
            int[,] counts = new int[volume.Width, volume.Height];

            // Process each source slice in the Z bin
            for (int srcZ = srcZStart; srcZ < srcZEnd; srcZ++)
            {
                await AccumulateSliceAsync(imageFiles[srcZ], accumulator, counts, binFactor);
            }

            // Average and write to volume
            for (int y = 0; y < volume.Height; y++)
            {
                for (int x = 0; x < volume.Width; x++)
                {
                    byte value = 0;
                    if (counts[x, y] > 0)
                    {
                        value = (byte)Math.Min(255, Math.Max(0, 
                            Math.Round(accumulator[x, y] / counts[x, y])));
                    }
                    volume[x, y, newZ] = value;
                }
            }
        }

        /// <summary>
        /// Accumulate a source image into the binning accumulator
        /// </summary>
        private static async Task AccumulateSliceAsync(
    string imagePath,
    float[,] accumulator,
    int[,] counts,
    int binFactor)
        {
            await Task.Run(() =>
            {
                // Use the new ImageLoader that supports TIF
                var imageData = ImageLoader.LoadGrayscaleImage(imagePath, out int imageWidth, out int imageHeight);

                int newWidth = accumulator.GetLength(0);
                int newHeight = accumulator.GetLength(1);

                // Accumulate binned pixels
                for (int y = 0; y < newHeight; y++)
                {
                    for (int x = 0; x < newWidth; x++)
                    {
                        // Sum pixels in the XY bin
                        float sum = 0;
                        int count = 0;

                        for (int by = 0; by < binFactor; by++)
                        {
                            int srcY = y * binFactor + by;
                            if (srcY >= imageHeight) continue;

                            for (int bx = 0; bx < binFactor; bx++)
                            {
                                int srcX = x * binFactor + bx;
                                if (srcX >= imageWidth) continue;

                                int index = srcY * imageWidth + srcX;
                                sum += imageData[index];
                                count++;
                            }
                        }

                        if (count > 0)
                        {
                            accumulator[x, y] += sum;
                            counts[x, y] += count;
                        }
                    }
                }
            });
        }

        /// <summary>
        /// Create an empty labels file
        /// </summary>
        private static async Task CreateEmptyLabelsFileAsync(string folderPath, ChunkedVolume volume, string datasetName)
        {
            string labelsPath = Path.Combine(folderPath, $"{datasetName}.Labels.bin");
            
            if (!File.Exists(labelsPath))
            {
                Logger.Log($"[CTStackLoader] Creating empty labels file");
                
                var labels = new ChunkedLabelVolume(
                    volume.Width, 
                    volume.Height, 
                    volume.Depth, 
                    volume.ChunkDim, 
                    false); // Always create labels in memory
                    
                labels.SaveAsBin(labelsPath);
                labels.Dispose();
            }
        }

        /// <summary>
        /// Create a memory-mapped file with header
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
        /// Get dimensions of an image
        /// </summary>
        private static async Task<(int width, int height)> GetImageDimensionsAsync(string imagePath)
        {
            return await Task.Run(() =>
            {
                var info = ImageLoader.LoadImageInfo(imagePath);
                return (info.Width, info.Height);
            });
        }

        /// <summary>
        /// Get all supported image files from a folder
        /// </summary>
        private static List<string> GetSupportedImageFiles(string folderPath)
        {
            return Directory.GetFiles(folderPath)
                .Where(f => ImageLoader.IsSupportedImageFile(f))
                .ToList();
        }

        /// <summary>
        /// Sort files numerically based on numbers in filename
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