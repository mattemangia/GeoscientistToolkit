// GeoscientistToolkit/Data/CtImageStack/CtStackConverter.cs

using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.CtImageStack;

/// <summary>
///     Converts a CT image stack into an optimized, streamable format (.gvt)
///     with LOD mipmaps and chunked bricks.
/// </summary>
public static class CtStackConverter
{
    public const int BrickSize = 64; // 64x64x64 bricks

    /// <summary>
    /// Converts a ChunkedVolume to the streamable .gvt format with LOD mipmaps.
    /// </summary>
    public static async Task ConvertToStreamableFormat(ChunkedVolume sourceVolume, string outputFilePath,
        Action<float, string> onProgress)
    {
        try
        {
            onProgress?.Invoke(0.0f, "Preparing for conversion...");

            var width = sourceVolume.Width;
            var height = sourceVolume.Height;
            var depth = sourceVolume.Depth;

            Logger.Log($"[CtStackConverter] Converting volume {width}×{height}×{depth}");

            using (var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fileStream))
            {
                writer.Write(width);
                writer.Write(height);
                writer.Write(depth);
                writer.Write(BrickSize);

                var maxDim = Math.Max(width, Math.Max(height, depth));
                var numLods = (int)Math.Floor(Math.Log2(maxDim / (float)BrickSize)) + 1;
                writer.Write(numLods);

                Logger.Log($"[CtStackConverter] Writing {numLods} LOD levels");

                var lodTablePosition = fileStream.Position;
                var lodTableSize = (long)numLods * (sizeof(int) * 3 + sizeof(long));
                fileStream.Seek(lodTableSize, SeekOrigin.Current);

                var lodInfos = new GvtLodInfo[numLods];
                var currentLodVolume = sourceVolume;

                for (var i = 0; i < numLods; i++)
                {
                    var lodInfo = new GvtLodInfo
                    {
                        Width = currentLodVolume.Width,
                        Height = currentLodVolume.Height,
                        Depth = currentLodVolume.Depth,
                        FileOffset = fileStream.Position
                    };
                    lodInfos[i] = lodInfo;

                    Logger.Log(
                        $"[CtStackConverter] Writing LOD {i}: {lodInfo.Width}×{lodInfo.Height}×{lodInfo.Depth} at offset {lodInfo.FileOffset}");

                    var bricksX = (lodInfo.Width + BrickSize - 1) / BrickSize;
                    var bricksY = (lodInfo.Height + BrickSize - 1) / BrickSize;
                    var bricksZ = (lodInfo.Depth + BrickSize - 1) / BrickSize;
                    var totalBricks = bricksX * bricksY * bricksZ;

                    Logger.Log($"[CtStackConverter] LOD {i} has {bricksX}×{bricksY}×{bricksZ} = {totalBricks} bricks");

                    for (var b = 0; b < totalBricks; b++)
                    {
                        var bz = b / (bricksX * bricksY);
                        var rem = b % (bricksX * bricksY);
                        var by = rem / bricksX;
                        var bx = rem % bricksX;

                        var progress = 0.05f + 0.9f * (i + (float)b / totalBricks) / numLods;
                        onProgress?.Invoke(progress,
                            $"Writing LOD {i} ({lodInfo.Width}×{lodInfo.Height}×{lodInfo.Depth}) brick {b + 1}/{totalBricks}...");

                        var brick = GetBrick(currentLodVolume, bx, by, bz);
                        await fileStream.WriteAsync(brick, 0, brick.Length);
                    }

                    if (i < numLods - 1)
                    {
                        onProgress?.Invoke(0.95f, $"Downsampling for LOD {i + 1}...");
                        currentLodVolume = await Task.Run(() => Downsample(currentLodVolume));
                    }
                }

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

    private static byte[] GetBrick(ChunkedVolume volume, int brickX, int brickY, int brickZ)
    {
        var brickData = new byte[BrickSize * BrickSize * BrickSize];
        var startX = brickX * BrickSize;
        var startY = brickY * BrickSize;
        var startZ = brickZ * BrickSize;

        for (var z = 0; z < BrickSize; z++)
        for (var y = 0; y < BrickSize; y++)
        for (var x = 0; x < BrickSize; x++)
        {
            var globalX = startX + x;
            var globalY = startY + y;
            var globalZ = startZ + z;
            var brickIndex = z * BrickSize * BrickSize + y * BrickSize + x;

            if (globalX < volume.Width && globalY < volume.Height && globalZ < volume.Depth)
                brickData[brickIndex] = volume[globalX, globalY, globalZ];
        }

        return brickData;
    }

    private static ChunkedVolume Downsample(ChunkedVolume input)
    {
        var newW = Math.Max(1, input.Width / 2);
        var newH = Math.Max(1, input.Height / 2);
        var newD = Math.Max(1, input.Depth / 2);

        var outputData = new byte[newW * newH * newD];

        Parallel.For(0, newD, z =>
        {
            for (var y = 0; y < newH; y++)
            for (var x = 0; x < newW; x++)
            {
                var sum = 0;
                for (var i = 0; i < 8; i++)
                    sum += input[x * 2 + (i & 1), y * 2 + ((i >> 1) & 1), z * 2 + ((i >> 2) & 1)];

                outputData[(z * newH + y) * newW + x] = (byte)(sum / 8);
            }
        });

        var outputVolume = new ChunkedVolume(newW, newH, newD, input.ChunkDim);
        for (var z = 0; z < newD; z++)
        for (var y = 0; y < newH; y++)
        for (var x = 0; x < newW; x++)
            outputVolume[x, y, z] = outputData[(z * newH + y) * newW + x];

        return outputVolume;
    }
}