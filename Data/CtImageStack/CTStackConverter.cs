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

                    // Leave space for the LOD information table
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

                        int bricksX = (lodInfo.Width + BrickSize - 1) / BrickSize;
                        int bricksY = (lodInfo.Height + BrickSize - 1) / BrickSize;
                        int bricksZ = (lodInfo.Depth + BrickSize - 1) / BrickSize;
                        int totalBricks = bricksX * bricksY * bricksZ;

                        for (int b = 0; b < totalBricks; b++)
                        {
                            int bz = b / (bricksX * bricksY);
                            int rem = b % (bricksX * bricksY);
                            int by = rem / bricksX;
                            int bx = rem % bricksX;
                            
                            float progress = 0.05f + (0.9f * (i + (float)b / totalBricks) / numLods);
                            onProgress?.Invoke(progress, $"Writing LOD {i} ({lodInfo.Width}x{lodInfo.Height}x{lodInfo.Depth}) brick {b+1}/{totalBricks}...");
                            
                            var brick = GetBrick(currentLodVolume, bx, by, bz);
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
                    fileStream.Seek(20, SeekOrigin.Begin); // Seek past main header (5 ints)
                    foreach (var info in lodInfos)
                    {
                        writer.Write(info.Width);
                        writer.Write(info.Height);
                        writer.Write(info.Depth);
                        writer.Write(info.FileOffset);
                    }
                }
                onProgress?.Invoke(1.0f, "Conversion Complete!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[CtStackConverter] Conversion failed: {ex.Message}");
                throw;
            }
        }

        private static byte[] GetBrick(ChunkedVolume volume, int brickX, int brickY, int brickZ)
        {
            byte[] brickData = new byte[BrickSize * BrickSize * BrickSize];
            int startX = brickX * BrickSize;
            int startY = brickY * BrickSize;
            int startZ = brickZ * BrickSize;
            
            Parallel.For(0, BrickSize, z =>
            {
                for (int y = 0; y < BrickSize; y++)
                for (int x = 0; x < BrickSize; x++)
                {
                    brickData[(z * BrickSize * BrickSize) + (y * BrickSize) + x] = 
                        volume[startX + x, startY + y, startZ + z];
                }
            });
            return brickData;
        }

        private static ChunkedVolume Downsample(ChunkedVolume input)
        {
            int newW = Math.Max(1, input.Width / 2);
            int newH = Math.Max(1, input.Height / 2);
            int newD = Math.Max(1, input.Depth / 2);
            var output = new ChunkedVolume(newW, newH, newD, input.ChunkDim);

            Parallel.For(0, newD, z =>
            {
                for (int y = 0; y < newH; y++)
                for (int x = 0; x < newW; x++)
                {
                    int sum = 0;
                    for(int i = 0; i < 8; i++)
                        sum += input[x * 2 + (i & 1), y * 2 + ((i >> 1) & 1), z * 2 + ((i >> 2) & 1)];
                    output[x, y, z] = (byte)(sum / 8);
                }
            });
            return output;
        }
    }
}