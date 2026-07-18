// GAIA/Data/CtImageStack/CtStackConverter.cs

using GAIA.Data.VolumeData;
using GAIA.Util;

namespace GAIA.Data.CtImageStack;

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

                    // Process one brick layer (BrickSize Z-slices) at a time: read the slices
                    // with bulk slice reads once, then assemble every brick of the layer from
                    // the slab with Array.Copy instead of per-voxel indexer access.
                    var sliceLength = lodInfo.Width * lodInfo.Height;
                    var slab = new byte[BrickSize][];
                    for (var s = 0; s < BrickSize; s++) slab[s] = new byte[sliceLength];
                    var brick = new byte[BrickSize * BrickSize * BrickSize];
                    var lodVolume = currentLodVolume;

                    for (var bz = 0; bz < bricksZ; bz++)
                    {
                        var startZ = bz * BrickSize;
                        var slabDepth = Math.Min(BrickSize, lodInfo.Depth - startZ);

                        var progress = 0.05f + 0.9f * (i + (float)bz / bricksZ) / numLods;
                        onProgress?.Invoke(progress,
                            $"Writing LOD {i} ({lodInfo.Width}×{lodInfo.Height}×{lodInfo.Depth}) brick layer {bz + 1}/{bricksZ}...");

                        await Task.Run(() => Parallel.For(0, slabDepth,
                            s => lodVolume.ReadSliceZ(startZ + s, slab[s])));

                        for (var by = 0; by < bricksY; by++)
                        for (var bx = 0; bx < bricksX; bx++)
                        {
                            AssembleBrick(slab, slabDepth, lodInfo.Width, lodInfo.Height, bx, by, brick);
                            await fileStream.WriteAsync(brick, 0, brick.Length);
                        }
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

    /// <summary>
    ///     Copy one brick out of a slab of Z-slices using row-wise bulk copies.
    ///     Regions outside the volume stay zero, matching the previous per-voxel behavior.
    /// </summary>
    private static void AssembleBrick(byte[][] slab, int slabDepth, int width, int height,
        int brickX, int brickY, byte[] brick)
    {
        Array.Clear(brick);

        var startX = brickX * BrickSize;
        var startY = brickY * BrickSize;
        var copyWidth = Math.Min(BrickSize, width - startX);
        var copyHeight = Math.Min(BrickSize, height - startY);

        for (var z = 0; z < slabDepth; z++)
        {
            var slice = slab[z];
            var zBase = z * BrickSize * BrickSize;
            for (var y = 0; y < copyHeight; y++)
                Array.Copy(slice, (startY + y) * width + startX, brick, zBase + y * BrickSize, copyWidth);
        }
    }

    private static ChunkedVolume Downsample(ChunkedVolume input)
    {
        var newW = Math.Max(1, input.Width / 2);
        var newH = Math.Max(1, input.Height / 2);
        var newD = Math.Max(1, input.Depth / 2);

        var outputVolume = new ChunkedVolume(newW, newH, newD, input.ChunkDim);
        var srcW = input.Width;
        var srcH = input.Height;
        var srcLength = srcW * srcH;

        // Each worker reuses its buffers: read the two source slices of the 2×2×2 cell with
        // bulk slice reads, average, then bulk-write the output slice.
        Parallel.For(0, newD,
            () => (front: new byte[srcLength], back: new byte[srcLength], outSlice: new byte[newW * newH]),
            (z, _, buffers) =>
            {
                var (front, back, outSlice) = buffers;
                var z0 = z * 2;
                var z1 = Math.Min(z0 + 1, input.Depth - 1);

                input.ReadSliceZ(z0, front);
                var second = front;
                if (z1 != z0)
                {
                    input.ReadSliceZ(z1, back);
                    second = back;
                }

                for (var y = 0; y < newH; y++)
                {
                    var row0 = Math.Min(y * 2, srcH - 1) * srcW;
                    var row1 = Math.Min(y * 2 + 1, srcH - 1) * srcW;
                    var outRow = y * newW;

                    for (var x = 0; x < newW; x++)
                    {
                        var x0 = Math.Min(x * 2, srcW - 1);
                        var x1 = Math.Min(x * 2 + 1, srcW - 1);

                        var sum = front[row0 + x0] + front[row0 + x1] +
                                  front[row1 + x0] + front[row1 + x1] +
                                  second[row0 + x0] + second[row0 + x1] +
                                  second[row1 + x0] + second[row1 + x1];

                        outSlice[outRow + x] = (byte)(sum >> 3);
                    }
                }

                outputVolume.WriteSliceZ(z, outSlice);
                return buffers;
            },
            _ => { });

        return outputVolume;
    }
}