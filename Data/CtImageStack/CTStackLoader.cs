// GeoscientistToolkit/Data/CtImageStack/CTStackLoader.cs

using System.Text.Json;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack;

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
            var ext = Path.GetExtension(path).ToLowerInvariant();

            // Check if it's a multi-page TIFF
            if ((ext == ".tif" || ext == ".tiff") && ImageLoader.IsMultiPageTiff(path))
                return await LoadCTStackFromMultiPageTiffAsync(path, pixelSize, binningFactor,
                    useMemoryMapping, progress, datasetName);

            // Check if it's a .ctstack definition file
            if (ext == ".ctstack")
                return await LoadCTStackFromDefinitionFileAsync(path, pixelSize, binningFactor, useMemoryMapping, progress, datasetName);

            throw new ArgumentException($"The specified file type ({ext}) is not supported as a stack volume.");
        }

        if (Directory.Exists(path))
            return await LoadCTStackFromFolderAsync(path, pixelSize, binningFactor,
                useMemoryMapping, progress, datasetName);

        throw new FileNotFoundException("The specified path does not exist.");
    }

    private static async Task<ChunkedVolume> LoadCTStackFromDefinitionFileAsync(
        string path,
        double pixelSize,
        int binningFactor,
        bool useMemoryMapping,
        IProgress<float> progress,
        string datasetName)
    {
        Logger.Log($"[CTStackLoader] Loading CT stack from definition file: {path}");

        if (string.IsNullOrEmpty(datasetName))
            datasetName = Path.GetFileNameWithoutExtension(path);

        string json = await File.ReadAllTextAsync(path);

        // Simple JSON parsing
        List<string> slicePaths = new List<string>();
        double definedPixelSize = pixelSize;

        try
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("slices", out var slicesElement) && slicesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in slicesElement.EnumerateArray())
                    {
                        slicePaths.Add(element.GetString());
                    }
                }

                if (root.TryGetProperty("pixelSize", out var psElement))
                {
                    definedPixelSize = psElement.GetDouble();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CTStackLoader] Failed to parse .ctstack file: {ex.Message}");
            throw;
        }

        // Resolve relative paths
        var baseDir = Path.GetDirectoryName(path);
        var resolvedPaths = slicePaths.Select(p => Path.IsPathRooted(p) ? p : Path.Combine(baseDir, p)).ToList();

        // Filter existing
        var validFiles = resolvedPaths.Where(File.Exists).ToList();

        if (validFiles.Count == 0)
            throw new FileNotFoundException("No valid image files found in .ctstack definition.");

        // Use the defined pixel size if the user didn't override it (passed 0 or default?)
        // The calling code passes pixelSize based on UI settings. We usually prefer the file's metadata if available?
        // Let's log it.
        Logger.Log($"[CTStackLoader] .ctstack metadata pixel size: {definedPixelSize}. Using provided: {pixelSize}");

        // Call internal loader logic
        // We can reuse LoadDirectAsync or LoadWith3DBinningAsync, but they are private and take 'folderPath'.
        // We need to refactor slightly or copy logic.
        // Since we can't easily change the signature of those private methods without potentially breaking other things (though they are private),
        // let's create a new internal method that takes List<string> directly.

        if (binningFactor > 1)
            return await LoadFromFilesWith3DBinningAsync(baseDir, validFiles, pixelSize, binningFactor, useMemoryMapping, progress, datasetName);

        return await LoadFromFilesDirectAsync(baseDir, validFiles, pixelSize, useMemoryMapping, progress, datasetName);
    }

    // Refactored from LoadCTStackFromFolderAsync

    private static async Task<ChunkedVolume> LoadFromFilesDirectAsync(
        string workingDir,
        List<string> imageFiles,
        double pixelSize,
        bool useMemoryMapping,
        IProgress<float> progress,
        string datasetName)
    {
        // Check for existing volume file in the working directory
        var volumePath = Path.Combine(workingDir, $"{datasetName}.Volume.bin");

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
            }
        }

        // Load from images
        // We need a ChunkedVolume.FromFilesAsync? Or reuse FromFolderAsync logic but pass files?
        // ChunkedVolume.FromFolderAsync takes a folder path.
        // We should probably implement loading from file list here manually as FromFolderAsync does.

        // Reuse logic from LoadDirectAsync (which was private static).
        // I will copy the logic here essentially.

        // Get dimensions
        var (width, height) = await GetImageDimensionsAsync(imageFiles[0]);
        var depth = imageFiles.Count;

        ChunkedVolume newVolume;

        if (useMemoryMapping)
        {
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

        // Load pages directly into the volume
        await Task.Run(() =>
        {
            for (var z = 0; z < depth; z++)
            {
                var pageData = ImageLoader.LoadGrayscaleImage(imageFiles[z], out var pageWidth, out var pageHeight);

                if (pageWidth != width || pageHeight != height)
                {
                    Logger.LogError($"[CTStackLoader] Slice {z} has different dimensions ({pageWidth}×{pageHeight}) than expected ({width}×{height})");
                    // Resize? Or skip? For now, we continue, but this might crash WriteSliceZ if strictly checked.
                    // Ideally we resize.
                }

                newVolume.WriteSliceZ(z, pageData);
                progress?.Report((float)(z + 1) / depth);
            }
        });

        if (!useMemoryMapping) await newVolume.SaveAsBinAsync(volumePath);
        await CreateEmptyLabelsFileAsync(workingDir, newVolume, datasetName);

        return newVolume;
    }

    private static async Task<ChunkedVolume> LoadFromFilesWith3DBinningAsync(
        string workingDir,
        List<string> imageFiles,
        double pixelSize,
        int binningFactor,
        bool useMemoryMapping,
        IProgress<float> progress,
        string datasetName)
    {
         // Same logic as LoadWith3DBinningAsync but public/internal reused
         // I'll just copy the implementation of LoadWith3DBinningAsync here but using the file list

        Logger.Log($"[CTStackLoader] Applying {binningFactor}× 3D binning");

        var binnedMarker = Path.Combine(workingDir, $"binned_{binningFactor}x3d.marker");
        var volumePath = Path.Combine(workingDir, $"{datasetName}.Volume.bin");

        if (File.Exists(binnedMarker) && File.Exists(volumePath))
        {
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

        var (origWidth, origHeight) = await GetImageDimensionsAsync(imageFiles[0]);
        var origDepth = imageFiles.Count;

        var newWidth = Math.Max(1, origWidth / binningFactor);
        var newHeight = Math.Max(1, origHeight / binningFactor);
        var newDepth = Math.Max(1, (origDepth + binningFactor - 1) / binningFactor);

        ChunkedVolume binnedVolume;

        if (useMemoryMapping)
        {
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

        await Process3DBinningAsync(binnedVolume, imageFiles, binningFactor, progress);

        if (!useMemoryMapping) await binnedVolume.SaveAsBinAsync(volumePath);

        await File.WriteAllTextAsync(binnedMarker,
            $"3D Binned with factor {binningFactor} at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
            $"Original: {origWidth}×{origHeight}×{origDepth}\n" +
            $"Binned: {newWidth}×{newHeight}×{newDepth}");

        await CreateEmptyLabelsFileAsync(workingDir, binnedVolume, datasetName);

        return binnedVolume;
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
            return await LoadFromFilesWith3DBinningAsync(folderPath, imageFiles, pixelSize, binningFactor,
                useMemoryMapping, progress, datasetName);

        return await LoadFromFilesDirectAsync(folderPath, imageFiles, pixelSize, useMemoryMapping,
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
        // Get dimensions and page count
        var pageCount = ImageLoader.GetTiffPageCount(tiffPath);
        var firstPageInfo = ImageLoader.LoadImageInfo(tiffPath);
        var width = firstPageInfo.Width;
        var height = firstPageInfo.Height;
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

        // Load pages directly into the volume
        await Task.Run(() =>
        {
            for (var z = 0; z < pageCount; z++)
            {
                var pageData = ImageLoader.LoadTiffPageAsGrayscale(tiffPath, z, out var pageWidth, out var pageHeight);

                // Ensure page dimensions match
                if (pageWidth != width || pageHeight != height)
                {
                    Logger.LogError(
                        $"[CTStackLoader] Page {z} has different dimensions ({pageWidth}×{pageHeight}) than expected ({width}×{height})");
                    continue;
                }

                // Write the page data directly to the volume using WriteSliceZ
                newVolume.WriteSliceZ(z, pageData);

                // Report progress
                progress?.Report((float)(z + 1) / pageCount);
            }
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
            // Get original dimensions
            var pageCount = ImageLoader.GetTiffPageCount(tiffPath);
            var firstPageInfo = ImageLoader.LoadImageInfo(tiffPath);
            var origWidth = firstPageInfo.Width;
            var origHeight = firstPageInfo.Height;
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
            await ProcessMultiPageTiff3DBinningAsync(binnedVolume, tiffPath, binningFactor, progress);

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
    ///     Process multi-page TIFF with true 3D binning
    /// </summary>
    private static async Task ProcessMultiPageTiff3DBinningAsync(
        ChunkedVolume volume,
        string tiffPath,
        int binFactor,
        IProgress<float> progress)
    {
        var newDepth = volume.Depth;
        var pageCount = ImageLoader.GetTiffPageCount(tiffPath);

        // Process each output slice
        for (var newZ = 0; newZ < newDepth; newZ++)
        {
            var srcZStart = newZ * binFactor;
            var srcZEnd = Math.Min(srcZStart + binFactor, pageCount);

            // Create accumulator for this slice
            var accumulator = new float[volume.Width, volume.Height];
            var counts = new int[volume.Width, volume.Height];

            // Process each source slice in the Z bin
            for (var srcZ = srcZStart; srcZ < srcZEnd; srcZ++)
            {
                var pageData = ImageLoader.LoadTiffPageAsGrayscale(tiffPath, srcZ, out var width, out var height);
                AccumulateSliceData(pageData, width, height, accumulator, counts, binFactor);
            }

            // Average and write to volume
            for (var y = 0; y < volume.Height; y++)
            for (var x = 0; x < volume.Width; x++)
            {
                byte value = 0;
                if (counts[x, y] > 0)
                    value = (byte)Math.Min(255, Math.Max(0,
                        Math.Round(accumulator[x, y] / counts[x, y])));
                volume[x, y, newZ] = value;
            }

            progress?.Report((float)(newZ + 1) / newDepth);
        }
    }

    /// <summary>
    ///     Accumulate slice data into the binning accumulator
    /// </summary>
    private static void AccumulateSliceData(
        byte[] imageData,
        int imageWidth,
        int imageHeight,
        float[,] accumulator,
        int[,] counts,
        int binFactor)
    {
        var newWidth = accumulator.GetLength(0);
        var newHeight = accumulator.GetLength(1);

        // Accumulate binned pixels
        for (var y = 0; y < newHeight; y++)
        for (var x = 0; x < newWidth; x++)
        {
            // Sum pixels in the XY bin
            float sum = 0;
            var count = 0;

            for (var by = 0; by < binFactor; by++)
            {
                var srcY = y * binFactor + by;
                if (srcY >= imageHeight) continue;

                for (var bx = 0; bx < binFactor; bx++)
                {
                    var srcX = x * binFactor + bx;
                    if (srcX >= imageWidth) continue;

                    var index = srcY * imageWidth + srcX;
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
        var srcZStart = newZ * binFactor;
        var srcZEnd = Math.Min(srcZStart + binFactor, imageFiles.Count);

        // Create accumulator for this slice
        var accumulator = new float[volume.Width, volume.Height];
        var counts = new int[volume.Width, volume.Height];

        // Process each source slice in the Z bin
        for (var srcZ = srcZStart; srcZ < srcZEnd; srcZ++)
            await AccumulateSliceAsync(imageFiles[srcZ], accumulator, counts, binFactor);

        // Average and write to volume
        for (var y = 0; y < volume.Height; y++)
        for (var x = 0; x < volume.Width; x++)
        {
            byte value = 0;
            if (counts[x, y] > 0)
                value = (byte)Math.Min(255, Math.Max(0,
                    Math.Round(accumulator[x, y] / counts[x, y])));
            volume[x, y, newZ] = value;
        }
    }

    /// <summary>
    ///     Accumulate a source image into the binning accumulator
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
            var imageData = ImageLoader.LoadGrayscaleImage(imagePath, out var imageWidth, out var imageHeight);

            var newWidth = accumulator.GetLength(0);
            var newHeight = accumulator.GetLength(1);

            // Accumulate binned pixels
            for (var y = 0; y < newHeight; y++)
            for (var x = 0; x < newWidth; x++)
            {
                // Sum pixels in the XY bin
                float sum = 0;
                var count = 0;

                for (var by = 0; by < binFactor; by++)
                {
                    var srcY = y * binFactor + by;
                    if (srcY >= imageHeight) continue;

                    for (var bx = 0; bx < binFactor; bx++)
                    {
                        var srcX = x * binFactor + bx;
                        if (srcX >= imageWidth) continue;

                        var index = srcY * imageWidth + srcX;
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