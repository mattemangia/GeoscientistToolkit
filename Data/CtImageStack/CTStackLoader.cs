// GAIA/Data/CtImageStack/CTStackLoader.cs

using GAIA.Data.VolumeData;
using GAIA.Util;

namespace GAIA.Data.CtImageStack;

/// <summary>
///     Handles loading of CT image stacks with support for binning and progress reporting
/// </summary>
public static class CTStackLoader
{
    /// <summary>
    ///     Loads a CT stack from either a folder of images or a single multi-page TIFF file
    /// </summary>
    public static async Task<ChunkedVolume> LoadCTStackAsync(
        string path,
        double pixelSize,
        int binningFactor,
        bool useMemoryMapping,
        IProgress<float> progress = null,
        string datasetName = null)
    {
        // Check if path is a file or directory
        if (File.Exists(path))
        {
            // Check if it's a multi-page TIFF
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if ((ext == ".tif" || ext == ".tiff") && ImageLoader.IsMultiPageTiff(path))
                return await LoadCTStackFromMultiPageTiffAsync(path, pixelSize, binningFactor,
                    useMemoryMapping, progress, datasetName);

            throw new ArgumentException("The specified file is not a multi-page TIFF file.");
        }

        if (Directory.Exists(path))
            return await LoadCTStackFromFolderAsync(path, pixelSize, binningFactor,
                useMemoryMapping, progress, datasetName);

        throw new FileNotFoundException("The specified path does not exist.");
    }

    /// <summary>
    ///     Loads a CT stack from a multi-page TIFF file
    /// </summary>
    private static async Task<ChunkedVolume> LoadCTStackFromMultiPageTiffAsync(
        string tiffPath,
        double pixelSize,
        int binningFactor,
        bool useMemoryMapping,
        IProgress<float> progress = null,
        string datasetName = null)
    {
        Logger.Log($"[CTStackLoader] Loading CT stack from multi-page TIFF: {tiffPath}");
        Logger.Log(
            $"[CTStackLoader] Pixel size: {pixelSize * 1e6} µm, Binning: {binningFactor}×, Memory mapping: {useMemoryMapping}");

        // Get dataset name
        if (string.IsNullOrEmpty(datasetName)) datasetName = Path.GetFileNameWithoutExtension(tiffPath);

        // Get the parent directory for storing processed files
        var parentDir = Path.GetDirectoryName(tiffPath);

        // Check if binning is needed
        if (binningFactor > 1)
            return await LoadMultiPageTiffWith3DBinningAsync(tiffPath, parentDir, pixelSize, binningFactor,
                useMemoryMapping, progress, datasetName);

        return await LoadMultiPageTiffDirectAsync(tiffPath, parentDir, pixelSize, useMemoryMapping,
            progress, datasetName);
    }

    /// <summary>
    ///     Loads a CT stack from a folder of images
    /// </summary>
    private static async Task<ChunkedVolume> LoadCTStackFromFolderAsync(
        string folderPath,
        double pixelSize,
        int binningFactor,
        bool useMemoryMapping,
        IProgress<float> progress = null,
        string datasetName = null)
    {
        Logger.Log($"[CTStackLoader] Loading CT stack from folder: {folderPath}");
        Logger.Log(
            $"[CTStackLoader] Pixel size: {pixelSize * 1e6} µm, Binning: {binningFactor}×, Memory mapping: {useMemoryMapping}");

        // Get dataset name
        if (string.IsNullOrEmpty(datasetName)) datasetName = Path.GetFileName(folderPath);

        // Get all image files
        var imageFiles = GetSupportedImageFiles(folderPath);
        if (imageFiles.Count == 0)
            throw new FileNotFoundException("No supported image files found in the specified folder.");

        // Sort files numerically
        imageFiles = SortFilesNumerically(imageFiles);
        Logger.Log($"[CTStackLoader] Found {imageFiles.Count} images");

        // Check if binning is needed
        if (binningFactor > 1)
            return await LoadWith3DBinningAsync(folderPath, imageFiles, pixelSize, binningFactor,
                useMemoryMapping, progress, datasetName);

        return await LoadDirectAsync(folderPath, imageFiles, pixelSize, useMemoryMapping,
            progress, datasetName);
    }

    /// <summary>
    ///     Loads multi-page TIFF directly without binning
    /// </summary>
    private static async Task<ChunkedVolume> LoadMultiPageTiffDirectAsync(
        string tiffPath,
        string outputDir,
        double pixelSize,
        bool useMemoryMapping,
        IProgress<float> progress,
        string datasetName)
    {
        // Check for existing volume file
        var volumePath = Path.Combine(outputDir, $"{datasetName}.Volume.bin");

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
                Logger.Log("[CTStackLoader] Regenerating volume from TIFF file...");
            }
        }

        // Load from multi-page TIFF
        // Get dimensions and page count in a single file open
        var (pageCount, width, height) = ImageLoader.GetTiffStackInfo(tiffPath);
        var depth = pageCount;

        Logger.Log($"[CTStackLoader] Multi-page TIFF dimensions: {width}×{height}×{depth}");

        ChunkedVolume newVolume;

        if (useMemoryMapping)
        {
            // Create memory-mapped file
            await CreateMemoryMappedFileAsync(volumePath, width, height, depth,
                ChunkedVolume.DEFAULT_CHUNK_DIM, pixelSize);

            newVolume = await ChunkedVolume.LoadFromBinAsync(volumePath, true);
        }
        else
        {
            newVolume = new ChunkedVolume(width, height, depth)
            {
                PixelSize = pixelSize
            };
        }

        // Decode page ranges in parallel: each worker opens its own TIFF handle, seeks once
        // to the start of its range and then walks pages sequentially (no per-page reopen).
        await Task.Run(() =>
        {
            var processed = 0;
            var workers = Math.Clamp(Environment.ProcessorCount, 1, pageCount);
            var pagesPerWorker = (pageCount + workers - 1) / workers;
            var tasks = new List<Task>();

            for (var w = 0; w < workers; w++)
            {
                var firstPage = w * pagesPerWorker;
                var lastPage = Math.Min(firstPage + pagesPerWorker, pageCount);
                if (firstPage >= lastPage) break;

                tasks.Add(Task.Run(() =>
                    ImageLoader.ReadTiffPageRangeAsGrayscale(tiffPath, firstPage, lastPage,
                        (z, pageData, pageWidth, pageHeight) =>
                        {
                            // Ensure page dimensions match
                            if (pageWidth != width || pageHeight != height)
                            {
                                Logger.LogError(
                                    $"[CTStackLoader] Page {z} has different dimensions ({pageWidth}×{pageHeight}) than expected ({width}×{height})");
                                return;
                            }

                            // Write the page data directly to the volume using WriteSliceZ
                            newVolume.WriteSliceZ(z, pageData);

                            var done = Interlocked.Increment(ref processed);
                            progress?.Report((float)done / pageCount);
                        })));
            }

            Task.WaitAll(tasks.ToArray());
        });

        // Save for future use (only if not memory mapped - it's already saved)
        if (!useMemoryMapping) await newVolume.SaveAsBinAsync(volumePath);

        // Create empty labels file
        await CreateEmptyLabelsFileAsync(outputDir, newVolume, datasetName);

        return newVolume;
    }

    /// <summary>
    ///     Loads multi-page TIFF with 3D binning applied
    /// </summary>
    private static async Task<ChunkedVolume> LoadMultiPageTiffWith3DBinningAsync(
        string tiffPath,
        string outputDir,
        double pixelSize,
        int binningFactor,
        bool useMemoryMapping,
        IProgress<float> progress,
        string datasetName)
    {
        Logger.Log($"[CTStackLoader] Applying {binningFactor}× 3D binning to multi-page TIFF");

        // Check for existing binned volume
        var binnedMarker = Path.Combine(outputDir, $"{datasetName}_binned_{binningFactor}x3d.marker");
        var volumePath = Path.Combine(outputDir, $"{datasetName}.Volume.bin");

        if (File.Exists(binnedMarker) && File.Exists(volumePath))
        {
            Logger.Log("[CTStackLoader] Found existing binned volume");
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

        return await Task.Run(async () =>
        {
            // Get original dimensions in a single file open
            var (pageCount, origWidth, origHeight) = ImageLoader.GetTiffStackInfo(tiffPath);
            var origDepth = pageCount;

            // Calculate binned dimensions - true 3D binning
            var newWidth = Math.Max(1, origWidth / binningFactor);
            var newHeight = Math.Max(1, origHeight / binningFactor);
            var newDepth = Math.Max(1, (origDepth + binningFactor - 1) / binningFactor);

            Logger.Log(
                $"[CTStackLoader] 3D Binning {origWidth}×{origHeight}×{origDepth} → {newWidth}×{newHeight}×{newDepth}");

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
                binnedVolume = new ChunkedVolume(newWidth, newHeight, newDepth)
                {
                    PixelSize = pixelSize * binningFactor
                };
            }

            // Process TIFF pages with 3D binning
            await ProcessMultiPageTiff3DBinningAsync(binnedVolume, tiffPath, binningFactor, pageCount, progress);

            // Save the binned volume (if not memory mapped)
            if (!useMemoryMapping) await binnedVolume.SaveAsBinAsync(volumePath);

            // Create marker file
            await File.WriteAllTextAsync(binnedMarker,
                $"3D Binned with factor {binningFactor} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"Original: {origWidth}×{origHeight}×{origDepth}\n" +
                $"Binned: {newWidth}×{newHeight}×{newDepth}");

            // Create empty labels file
            await CreateEmptyLabelsFileAsync(outputDir, binnedVolume, datasetName);

            return binnedVolume;
        });
    }

    /// <summary>
    ///     Process multi-page TIFF with true 3D binning. Workers decode disjoint page ranges in
    ///     parallel (one TIFF handle each, sequential directory walk) and emit binned slices.
    /// </summary>
    private static async Task ProcessMultiPageTiff3DBinningAsync(
        ChunkedVolume volume,
        string tiffPath,
        int binFactor,
        int pageCount,
        IProgress<float> progress)
    {
        var newDepth = volume.Depth;
        var newWidth = volume.Width;
        var newHeight = volume.Height;

        var processed = 0;
        var workers = Math.Clamp(Environment.ProcessorCount, 1, newDepth);
        var slicesPerWorker = (newDepth + workers - 1) / workers;
        var tasks = new List<Task>();

        for (var w = 0; w < workers; w++)
        {
            var firstOut = w * slicesPerWorker;
            var lastOut = Math.Min(firstOut + slicesPerWorker, newDepth);
            if (firstOut >= lastOut) break;

            tasks.Add(Task.Run(() =>
            {
                var accumulator = new float[newWidth * newHeight];
                var counts = new int[newWidth * newHeight];
                var slice = new byte[newWidth * newHeight];
                var currentOut = firstOut;

                var firstSrc = firstOut * binFactor;
                var lastSrc = Math.Min(lastOut * binFactor, pageCount);

                ImageLoader.ReadTiffPageRangeAsGrayscale(tiffPath, firstSrc, lastSrc,
                    (page, pageData, width, height) =>
                    {
                        AccumulateBinnedSlice(pageData, width, height, accumulator, counts,
                            newWidth, newHeight, binFactor);

                        // Finalize the output slice when its Z bin is complete (or the stack ends)
                        if ((page + 1) % binFactor == 0 || page + 1 == lastSrc)
                        {
                            FinalizeBinnedSlice(accumulator, counts, slice);
                            volume.WriteSliceZ(currentOut, slice);
                            Array.Clear(accumulator);
                            Array.Clear(counts);
                            currentOut++;

                            var done = Interlocked.Increment(ref processed);
                            progress?.Report((float)done / newDepth);
                        }
                    });
            }));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    ///     Accumulate a source slice into flat XY binning accumulators (row-major, cache friendly)
    /// </summary>
    private static void AccumulateBinnedSlice(
        byte[] imageData,
        int imageWidth,
        int imageHeight,
        float[] accumulator,
        int[] counts,
        int newWidth,
        int newHeight,
        int binFactor)
    {
        var maxY = Math.Min(imageHeight, newHeight * binFactor);
        var maxX = Math.Min(imageWidth, newWidth * binFactor);

        for (var srcY = 0; srcY < maxY; srcY++)
        {
            var dstRow = srcY / binFactor * newWidth;
            var srcRow = srcY * imageWidth;

            for (var srcX = 0; srcX < maxX; srcX++)
            {
                var dst = dstRow + srcX / binFactor;
                accumulator[dst] += imageData[srcRow + srcX];
                counts[dst]++;
            }
        }
    }

    /// <summary>
    ///     Average the accumulated values into an 8-bit output slice
    /// </summary>
    private static void FinalizeBinnedSlice(float[] accumulator, int[] counts, byte[] slice)
    {
        for (var i = 0; i < slice.Length; i++)
            slice[i] = counts[i] > 0
                ? (byte)Math.Clamp((int)MathF.Round(accumulator[i] / counts[i]), 0, 255)
                : (byte)0;
    }

    /// <summary>
    ///     Loads images directly without binning
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
        var volumePath = Path.Combine(folderPath, $"{datasetName}.Volume.bin");

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
        if (!useMemoryMapping) await newVolume.SaveAsBinAsync(volumePath);

        // Create empty labels file
        await CreateEmptyLabelsFileAsync(folderPath, newVolume, datasetName);

        return newVolume;
    }

    /// <summary>
    ///     Loads images with 3D binning applied
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
        var binnedMarker = Path.Combine(folderPath, $"binned_{binningFactor}x3d.marker");
        var volumePath = Path.Combine(folderPath, $"{datasetName}.Volume.bin");

        if (File.Exists(binnedMarker) && File.Exists(volumePath))
        {
            Logger.Log("[CTStackLoader] Found existing binned volume");
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
        var origDepth = imageFiles.Count;

        // Calculate binned dimensions - true 3D binning
        var newWidth = Math.Max(1, origWidth / binningFactor);
        var newHeight = Math.Max(1, origHeight / binningFactor);
        var newDepth = Math.Max(1, (origDepth + binningFactor - 1) / binningFactor);

        Logger.Log(
            $"[CTStackLoader] 3D Binning {origWidth}×{origHeight}×{origDepth} → {newWidth}×{newHeight}×{newDepth}");

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
            binnedVolume = new ChunkedVolume(newWidth, newHeight, newDepth)
            {
                PixelSize = pixelSize * binningFactor
            };
        }

        // Process images with 3D binning
        await Process3DBinningAsync(binnedVolume, imageFiles, binningFactor, progress);

        // Save the binned volume (if not memory mapped)
        if (!useMemoryMapping) await binnedVolume.SaveAsBinAsync(volumePath);

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
    ///     Process images with true 3D binning
    /// </summary>
    private static async Task Process3DBinningAsync(
        ChunkedVolume volume,
        List<string> imageFiles,
        int binFactor,
        IProgress<float> progress)
    {
        var origDepth = imageFiles.Count;
        var newDepth = volume.Depth;
        var processed = 0;

        // Process each output slice
        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount);

        for (var newZ = 0; newZ < newDepth; newZ++)
        {
            var z = newZ; // Capture for closure

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
    ///     Process a single 3D binned slice
    /// </summary>
    private static async Task Process3DBinnedSliceAsync(
        ChunkedVolume volume,
        List<string> imageFiles,
        int newZ,
        int binFactor)
    {
        await Task.Run(() =>
        {
            var newWidth = volume.Width;
            var newHeight = volume.Height;
            var srcZStart = newZ * binFactor;
            var srcZEnd = Math.Min(srcZStart + binFactor, imageFiles.Count);

            // Flat accumulators for this slice
            var accumulator = new float[newWidth * newHeight];
            var counts = new int[newWidth * newHeight];

            // Process each source slice in the Z bin
            for (var srcZ = srcZStart; srcZ < srcZEnd; srcZ++)
            {
                var imageData = ImageLoader.LoadGrayscaleImage(imageFiles[srcZ],
                    out var imageWidth, out var imageHeight);
                AccumulateBinnedSlice(imageData, imageWidth, imageHeight, accumulator, counts,
                    newWidth, newHeight, binFactor);
            }

            // Average and bulk-write to volume
            var slice = new byte[newWidth * newHeight];
            FinalizeBinnedSlice(accumulator, counts, slice);
            volume.WriteSliceZ(newZ, slice);
        });
    }

    /// <summary>
    ///     Create an empty labels file
    /// </summary>
    private static async Task CreateEmptyLabelsFileAsync(string folderPath, ChunkedVolume volume, string datasetName)
    {
        var labelsPath = Path.Combine(folderPath, $"{datasetName}.Labels.bin");

        if (!File.Exists(labelsPath))
        {
            Logger.Log("[CTStackLoader] Creating empty labels file");

            // A sparse memory-mapped backing file keeps even multi-terabyte logical label
            // volumes out of managed RAM; untouched regions consume no physical pages.
            using var labels = new ChunkedLabelVolume(
                volume.Width,
                volume.Height,
                volume.Depth,
                volume.ChunkDim,
                true,
                labelsPath);
        }
    }

    /// <summary>
    ///     Create a memory-mapped file with header
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
            var cntX = (width + chunkDim - 1) / chunkDim;
            var cntY = (height + chunkDim - 1) / chunkDim;
            var cntZ = (depth + chunkDim - 1) / chunkDim;
            var chunkSize = (long)chunkDim * chunkDim * chunkDim;
            var totalSize = 40 + cntX * cntY * cntZ * chunkSize; // 40 byte header

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
    ///     Get dimensions of an image
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
    ///     Get all supported image files from a folder
    /// </summary>
    private static List<string> GetSupportedImageFiles(string folderPath)
    {
        return Directory.GetFiles(folderPath)
            .Where(f => ImageLoader.IsSupportedImageFile(f))
            .ToList();
    }

    /// <summary>
    ///     Sort files numerically based on numbers in filename
    /// </summary>
    private static List<string> SortFilesNumerically(List<string> files)
    {
        return files.OrderBy(f =>
        {
            var filename = Path.GetFileNameWithoutExtension(f);
            var numbers = new string(filename.Where(char.IsDigit).ToArray());

            if (int.TryParse(numbers, out var number))
                return number;

            return 0;
        }).ToList();
    }
}
