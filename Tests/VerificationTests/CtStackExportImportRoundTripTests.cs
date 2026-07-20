using GAIA.Data.VolumeData;
using GAIA.Util;
using StbImageWriteSharp;

namespace VerificationTests;

/// <summary>
///     Reproduction harness for the reported XZ/YZ stripe artefacts: export a grayscale stack the
///     same way the export tool does (parallel ReadSliceZ -> image files) and reimport it via
///     ChunkedVolume.FromFolderAsync, then check the volume round-trips exactly. A periodic
///     mismatch along Z is exactly what shows up as horizontal stripes in the cross-section views.
/// </summary>
public sealed class CtStackExportImportRoundTripTests
{
    private const int Width = 200;
    private const int Height = 8;
    private const int Depth = 130; // spans several 64-voxel chunks in Z

    // A voxel value that depends on x, y and z so both in-slice and slice-to-slice ordering errors
    // are detectable. Kept below 251 so 8-bit image round-trips are lossless.
    private static byte Expected(int x, int y, int z) => (byte)((z * 7 + y * 3 + x) % 251);

    [Theory]
    [InlineData("tif")]
    [InlineData("png")]
    public async Task ExportThenReimport_RoundTripsExactly(string extension)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ctstack-rt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            using (var source = new ChunkedVolume(Width, Height, Depth, 64))
            {
                var slice = new byte[Width * Height];
                for (var z = 0; z < Depth; z++)
                {
                    for (var y = 0; y < Height; y++)
                    for (var x = 0; x < Width; x++)
                        slice[y * Width + x] = Expected(x, y, z);
                    source.WriteSliceZ(z, slice);
                }

                // Export exactly like CtImageStackExportTool: parallel slice reads, one file per z.
                var baseName = Path.Combine(folder, "sample");
                var maxParallelism = Math.Min(Environment.ProcessorCount, 8);
                using var semaphore = new SemaphoreSlim(maxParallelism);
                var tasks = new List<Task>();
                for (var z = 0; z < Depth; z++)
                {
                    var sliceIndex = z;
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var gray = new byte[Width * Height];
                            source.ReadSliceZ(sliceIndex, gray);
                            var fileName = $"{baseName}_{sliceIndex + 1:D4}.{extension}";
                            SaveGrayscale(fileName, gray, Width, Height, extension);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
            }

            // Reimport the folder the way the CT loader does.
            using var reimported = await ChunkedVolume.FromFolderAsync(folder,
                ChunkedVolume.DEFAULT_CHUNK_DIM, useMemoryMapping: false);

            Assert.Equal(Width, reimported.Width);
            Assert.Equal(Height, reimported.Height);
            Assert.Equal(Depth, reimported.Depth);

            // Voxel-exact round trip.
            for (var z = 0; z < Depth; z++)
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                Assert.Equal(Expected(x, y, z), reimported[x, y, z]);

            // The XZ cross-section (the one that showed stripes) must match analytically.
            var xz = new byte[Width * Depth];
            for (var y = 0; y < Height; y++)
            {
                reimported.ReadSliceXZ(y, xz);
                for (var z = 0; z < Depth; z++)
                for (var x = 0; x < Width; x++)
                    Assert.Equal(Expected(x, y, z), xz[z * Width + x]);
            }
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch { /* best effort cleanup */ }
        }
    }

    [Theory]
    [InlineData("tif")]
    [InlineData("png")]
    public async Task ExportFromMappedThenReimportMapped_RoundTripsExactly(string extension)
    {
        // Larger slices and memory-mapped volumes on both ends: this matches how a real (big) CT
        // stack is exported and reimported, exercising ChunkMappedAccessor under concurrent reads
        // and writes — the path the in-memory test above does not cover.
        const int w = 512, h = 384, d = 90;
        var folder = Path.Combine(Path.GetTempPath(), $"ctstack-mm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var sourcePath = Path.Combine(folder, "source.Volume.bin");
        try
        {
            using (var source = ChunkedVolume.CreateMemoryMapped(sourcePath, w, h, d))
            {
                var slice = new byte[w * h];
                for (var z = 0; z < d; z++)
                {
                    for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                        slice[y * w + x] = (byte)((z * 7 + y * 3 + x) % 251);
                    source.WriteSliceZ(z, slice);
                }

                source.Flush();

                var baseName = Path.Combine(folder, "sample");
                using var semaphore = new SemaphoreSlim(Math.Min(Environment.ProcessorCount, 8));
                var tasks = new List<Task>();
                for (var z = 0; z < d; z++)
                {
                    var sliceIndex = z;
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var gray = new byte[w * h];
                            source.ReadSliceZ(sliceIndex, gray);
                            SaveGrayscale($"{baseName}_{sliceIndex + 1:D4}.{extension}", gray, w, h, extension);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
            }

            File.Delete(sourcePath); // don't let the loader pick up the source as an existing volume

            using var reimported = await ChunkedVolume.FromFolderAsync(folder,
                ChunkedVolume.DEFAULT_CHUNK_DIM, useMemoryMapping: true, datasetName: "reimported");

            var xz = new byte[w * d];
            for (var y = 0; y < h; y++)
            {
                reimported.ReadSliceXZ(y, xz);
                for (var z = 0; z < d; z++)
                for (var x = 0; x < w; x++)
                    Assert.Equal((byte)((z * 7 + y * 3 + x) % 251), xz[z * w + x]);
            }
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch { /* best effort cleanup */ }
        }
    }

    private static void SaveGrayscale(string path, byte[] gray, int width, int height, string extension)
    {
        if (extension == "tif")
        {
            SimpleTiffWriter.WriteTiffGrayscale8(path, gray, width, height);
            return;
        }

        using var stream = File.Create(path);
        new ImageWriter().WritePng(gray, width, height, ColorComponents.Grey, stream);
    }
}
