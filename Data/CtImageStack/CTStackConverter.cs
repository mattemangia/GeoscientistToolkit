// GeoscientistToolkit/Data/CtImageStack/CtStackConverter.cs
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GeoscientistToolkit.Data.CtImageStack
{
    /// <summary>
    /// Converts a CT image stack into an optimized, streamable format (.gvt)
    /// with LOD mipmaps and chunked bricks.
    /// </summary>
    public static class CtStackConverter
    {
        public const int BrickSize = 64; // 64x64x64 bricks

        /// <summary>
        /// New primary conversion method that works from an already loaded ChunkedVolume.
        /// This is more efficient as it avoids re-reading images.
        /// </summary>
        public static async Task ConvertToStreamableFormat(ChunkedVolume sourceVolume, string outputFilePath, Action<float, string> onProgress)
{
    try
    {
        onProgress?.Invoke(0.0f, "Preparing for conversion...");

        int width = sourceVolume.Width;
        int height = sourceVolume.Height;
        int depth = sourceVolume.Depth;

        Logger.Log($"[CtStackConverter] Starting conversion for volume {width}×{height}×{depth}");

        // Verify source volume has data
        bool hasData = false;
        int nonZeroCount = 0;
        for (int z = 0; z < Math.Min(10, depth); z++)
        {
            for (int y = 0; y < Math.Min(10, height); y++)
            {
                for (int x = 0; x < Math.Min(10, width); x++)
                {
                    byte value = sourceVolume[x, y, z];
                    if (value > 0)
                    {
                        hasData = true;
                        nonZeroCount++;
                        if (nonZeroCount <= 5)
                        {
                            Logger.Log($"[CtStackConverter] Sample data at ({x},{y},{z}) = {value}");
                        }
                    }
                }
            }
        }

        if (!hasData)
        {
            Logger.LogError("[CtStackConverter] WARNING: Source volume appears to be empty!");
        }

        using (var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fileStream))
        {
            // --- Write Header ---
            writer.Write(width);
            writer.Write(height);
            writer.Write(depth);
            writer.Write(BrickSize);

            int maxDim = Math.Max(width, Math.Max(height, depth));
            int numLods = (int)Math.Floor(Math.Log2(maxDim / (float)BrickSize)) + 1;
            writer.Write(numLods);

            Logger.Log($"[CtStackConverter] Writing {numLods} LOD levels");

            // Leave space for the LOD information table
            long lodTablePosition = fileStream.Position;
            long lodTableSize = (long)numLods * (sizeof(int) * 3 + sizeof(long));
            fileStream.Seek(lodTableSize, SeekOrigin.Current);
            
            var lodInfos = new GvtLodInfo[numLods];
            ChunkedVolume currentLodVolume = sourceVolume;

            // --- Generate and Write Each LOD Level ---
            for (int i = 0; i < numLods; i++)
            {
                var lodInfo = new GvtLodInfo
                {
                    Width = currentLodVolume.Width,
                    Height = currentLodVolume.Height,
                    Depth = currentLodVolume.Depth,
                    FileOffset = fileStream.Position
                };
                lodInfos[i] = lodInfo;

                Logger.Log($"[CtStackConverter] Writing LOD {i}: {lodInfo.Width}×{lodInfo.Height}×{lodInfo.Depth} at offset {lodInfo.FileOffset}");

                int bricksX = (lodInfo.Width + BrickSize - 1) / BrickSize;
                int bricksY = (lodInfo.Height + BrickSize - 1) / BrickSize;
                int bricksZ = (lodInfo.Depth + BrickSize - 1) / BrickSize;
                int totalBricks = bricksX * bricksY * bricksZ;

                Logger.Log($"[CtStackConverter] LOD {i} has {bricksX}×{bricksY}×{bricksZ} = {totalBricks} bricks");

                for (int b = 0; b < totalBricks; b++)
                {
                    int bz = b / (bricksX * bricksY);
                    int rem = b % (bricksX * bricksY);
                    int by = rem / bricksX;
                    int bx = rem % bricksX;
                    
                    float progress = 0.05f + (0.9f * (i + (float)b / totalBricks) / numLods);
                    onProgress?.Invoke(progress, $"Writing LOD {i} ({lodInfo.Width}×{lodInfo.Height}×{lodInfo.Depth}) brick {b+1}/{totalBricks}...");
                    
                    var brick = GetBrickDebug(currentLodVolume, bx, by, bz, i == numLods - 1 && b == 0);
                    await fileStream.WriteAsync(brick, 0, brick.Length);
                }

                // Create the next (smaller) LOD level for the next iteration
                if (i < numLods - 1)
                {
                    onProgress?.Invoke(0.95f, $"Downsampling for LOD {i + 1}...");
                    currentLodVolume = await Task.Run(() => Downsample(currentLodVolume));
                }
            }

            // --- Write the Final LOD Table at the Beginning of the File ---
            onProgress?.Invoke(0.99f, "Finalizing file header...");
            fileStream.Seek(lodTablePosition, SeekOrigin.Begin);
            foreach (var info in lodInfos)
            {
                writer.Write(info.Width);
                writer.Write(info.Height);
                writer.Write(info.Depth);
                writer.Write(info.FileOffset);
            }
        }
        
        // Verify file was created and has content
        var fileInfo = new FileInfo(outputFilePath);
        Logger.Log($"[CtStackConverter] Created GVT file: {fileInfo.Length} bytes");
        
        onProgress?.Invoke(1.0f, "Conversion Complete!");
    }
    catch (Exception ex)
    {
        Logger.LogError($"[CtStackConverter] Conversion failed: {ex.Message}");
        throw;
    }
}

private static byte[] GetBrickDebug(ChunkedVolume volume, int brickX, int brickY, int brickZ, bool debug)
{
    byte[] brickData = new byte[BrickSize * BrickSize * BrickSize];
    int startX = brickX * BrickSize;
    int startY = brickY * BrickSize;
    int startZ = brickZ * BrickSize;
    
    int nonZeroCount = 0;
    
    for (int z = 0; z < BrickSize; z++)
    {
        for (int y = 0; y < BrickSize; y++)
        {
            for (int x = 0; x < BrickSize; x++)
            {
                int globalX = startX + x;
                int globalY = startY + y;
                int globalZ = startZ + z;
                
                int brickIndex = (z * BrickSize * BrickSize) + (y * BrickSize) + x;
                
                if (globalX < volume.Width && globalY < volume.Height && globalZ < volume.Depth)
                {
                    byte value = volume[globalX, globalY, globalZ];
                    brickData[brickIndex] = value;
                    
                    if (value > 0)
                    {
                        nonZeroCount++;
                        if (debug && nonZeroCount <= 10)
                        {
                            Logger.Log($"[GetBrick] Brick({brickX},{brickY},{brickZ}) has value {value} at local({x},{y},{z})/global({globalX},{globalY},{globalZ})");
                        }
                    }
                }
                else
                {
                    brickData[brickIndex] = 0;
                }
            }
        }
    }
    
    if (debug)
    {
        Logger.Log($"[GetBrick] Brick({brickX},{brickY},{brickZ}) has {nonZeroCount} non-zero values out of {BrickSize * BrickSize * BrickSize}");
    }
    
    return brickData;
}

        private static byte[] GetBrick(ChunkedVolume volume, int brickX, int brickY, int brickZ)
        {
            byte[] brickData = new byte[BrickSize * BrickSize * BrickSize];
            int startX = brickX * BrickSize;
            int startY = brickY * BrickSize;
            int startZ = brickZ * BrickSize;
    
            // Important: Initialize with zeros for partial bricks
            for (int i = 0; i < brickData.Length; i++)
            {
                brickData[i] = 0;
            }
    
            // Don't use Parallel.For here - it might cause issues with the ChunkedVolume indexer
            for (int z = 0; z < BrickSize; z++)
            {
                for (int y = 0; y < BrickSize; y++)
                {
                    for (int x = 0; x < BrickSize; x++)
                    {
                        int globalX = startX + x;
                        int globalY = startY + y;
                        int globalZ = startZ + z;
                
                        // Check bounds before accessing volume data
                        if (globalX < volume.Width && globalY < volume.Height && globalZ < volume.Depth)
                        {
                            int brickIndex = (z * BrickSize * BrickSize) + (y * BrickSize) + x;
                            byte value = volume[globalX, globalY, globalZ];
                            brickData[brickIndex] = value;
                    
                            // Debug: Log first non-zero value found
                            if (value > 0 && brickX == 0 && brickY == 0 && brickZ == 0 && x < 10 && y < 10 && z < 10)
                            {
                                Logger.Log($"[GetBrick] Found non-zero value {value} at ({globalX},{globalY},{globalZ})");
                            }
                        }
                    }
                }
            }
    
            return brickData;
        }

        // In CtStackConverter.cs

        private static ChunkedVolume Downsample(ChunkedVolume input)
        {
            int newW = Math.Max(1, input.Width / 2);
            int newH = Math.Max(1, input.Height / 2);
            int newD = Math.Max(1, input.Depth / 2);

            // --- FIX: Step 1 ---
            // Create a simple, flat byte array. Writes to this array are thread-safe 
            // as long as each thread accesses a unique index, which Parallel.For guarantees here.
            var outputData = new byte[newW * newH * newD];

            Parallel.For(0, newD, z =>
            {
                for (int y = 0; y < newH; y++)
                {
                    for (int x = 0; x < newW; x++)
                    {
                        // Kernel 2x2x2 averaging from the input volume
                        int sum = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            sum += input[x * 2 + (i & 1), y * 2 + ((i >> 1) & 1), z * 2 + ((i >> 2) & 1)];
                        }

                        // Write to the thread-safe flat array, not the complex ChunkedVolume object.
                        outputData[(z * newH + y) * newW + x] = (byte)(sum / 8);
                    }
                }
            });

            // --- FIX: Step 2 ---
            // Now that all parallel computation is done and the data is safely in outputData,
            // create the new ChunkedVolume and populate it in a fast, single-threaded copy.
            var outputVolume = new ChunkedVolume(newW, newH, newD, input.ChunkDim);
            for (int z = 0; z < newD; z++)
            {
                for (int y = 0; y < newH; y++)
                {
                    for (int x = 0; x < newW; x++)
                    {
                        // This copy is now safe and correct.
                        outputVolume[x, y, z] = outputData[(z * newH + y) * newW + x];
                    }
                }
            }

            // Optional but recommended: Log to confirm the fix
            long nonZeroValues = 0;
            foreach (byte val in outputData)
            {
                if (val != 0) nonZeroValues++;
            }
            Logger.Log($"[CtStackConverter] Downsampled LOD ({newW}x{newH}x{newD}) safely generated with {nonZeroValues} non-zero values.");

            return outputVolume;
        }
    }
}