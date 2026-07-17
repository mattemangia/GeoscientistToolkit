using GAIA.Data.VolumeData;
using GAIA.Data.CtImageStack;
using System.Diagnostics;

namespace VerificationTests;

public sealed class CtLabelStorageTests
{
    [Fact]
    public void SparseLabelVolume_RoundTripsDirtyChunksWithoutAllocatingTheEmptyVolume()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gaia-labels-{Guid.NewGuid():N}.bin");
        try
        {
            using (var volume = new ChunkedLabelVolume(19, 13, 9, 8, false))
            {
                Assert.Equal(0, volume[18, 12, 8]);
                volume[1, 2, 3] = 7;
                volume[18, 12, 8] = 11;
                volume.FlushDirtyChunks(path);
            }

            using var loaded = ChunkedLabelVolume.LoadFromBin(path, false);
            Assert.Equal(7, loaded[1, 2, 3]);
            Assert.Equal(11, loaded[18, 12, 8]);
            Assert.Equal(0, loaded[10, 5, 4]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MemoryMappedLoad_PreservesExistingLabelsAndSupportsSliceWrites()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gaia-labels-mm-{Guid.NewGuid():N}.bin");
        try
        {
            using (var source = new ChunkedLabelVolume(11, 7, 5, 4, false))
            {
                source[10, 6, 4] = 23;
                source.SaveAsBin(path);
            }

            using (var mapped = ChunkedLabelVolume.LoadFromBin(path, true))
            {
                Assert.Equal(23, mapped[10, 6, 4]);
                var slice = new byte[11 * 7];
                slice[3 * 11 + 4] = 9;
                mapped.WriteSliceZ(2, slice);
            }

            using var reloaded = ChunkedLabelVolume.LoadFromBin(path, false);
            Assert.Equal(23, reloaded[10, 6, 4]);
            Assert.Equal(9, reloaded[4, 3, 2]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SliceWrite_DoesNotDirtyUnchangedChunks()
    {
        using var volume = new ChunkedLabelVolume(32, 24, 8, 8, false);
        var slice = new byte[32 * 24];
        slice[3] = 4;
        volume.WriteSliceZ(2, slice);
        Assert.Equal(1, volume.DirtyChunkCount);
        Assert.Equal(1, volume.AllocatedChunkCount);

        var path = Path.Combine(Path.GetTempPath(), $"gaia-labels-unchanged-{Guid.NewGuid():N}.bin");
        try
        {
            volume.FlushDirtyChunks(path);
            Assert.Equal(0, volume.DirtyChunkCount);
            volume.WriteSliceZ(2, slice);
            Assert.Equal(0, volume.DirtyChunkCount);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void NewMemoryMappedVolume_UsesDiskBackingWithoutManagedChunks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gaia-labels-new-mm-{Guid.NewGuid():N}.bin");
        try
        {
            using (var volume = new ChunkedLabelVolume(33, 25, 17, 8, true, path))
            {
                Assert.Equal(0, volume.DirtyChunkCount);
                volume[32, 24, 16] = 31;
                volume.FlushDirtyChunks(path);
            }

            using var loaded = ChunkedLabelVolume.LoadFromBin(path, true);
            Assert.Equal(31, loaded[32, 24, 16]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ChangedChunkWrite_LeavesUnmarkedMappedChunksUntouched()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gaia-labels-changed-mm-{Guid.NewGuid():N}.bin");
        try
        {
            using var volume = new ChunkedLabelVolume(16, 8, 2, 8, true, path);
            var initial = new byte[16 * 8];
            initial[1] = 3;
            initial[12] = 7;
            volume.WriteSliceZ(0, initial);

            var changed = (byte[])initial.Clone();
            changed[1] = 9;
            changed[12] = 22; // second chunk is deliberately not marked
            volume.WriteSliceZChangedChunks(0, changed, new[] { true, false });

            var actual = new byte[16 * 8];
            volume.ReadSliceZ(0, actual);
            Assert.Equal(9, actual[1]);
            Assert.Equal(7, actual[12]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MemoryMappedOrthogonalSlices_MatchVoxelCoordinates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gaia-labels-orthogonal-{Guid.NewGuid():N}.bin");
        try
        {
            using var volume = new ChunkedLabelVolume(13, 11, 9, 4, true, path);
            for (var z = 0; z < volume.Depth; z++)
            for (var y = 0; y < volume.Height; y++)
            for (var x = 0; x < volume.Width; x++)
                volume[x, y, z] = (byte)((x + y * 3 + z * 7) % 251);

            var xz = new byte[volume.Width * volume.Depth];
            volume.ReadSliceXZ(6, xz);
            for (var z = 0; z < volume.Depth; z++)
            for (var x = 0; x < volume.Width; x++)
                Assert.Equal(volume[x, 6, z], xz[z * volume.Width + x]);

            var yz = new byte[volume.Height * volume.Depth];
            volume.ReadSliceYZ(8, yz);
            for (var z = 0; z < volume.Depth; z++)
            for (var y = 0; y < volume.Height; y++)
                Assert.Equal(volume[8, y, z], yz[z * volume.Height + y]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ThresholdAssignment_IsLazyAndDoesNotDirtyPhysicalLabelChunks()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gaia-virtual-threshold-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var labelPath = Path.Combine(directory, $"{Path.GetFileName(directory)}.Labels.bin");
        try
        {
            using var grayscale = new ChunkedVolume(32, 24, 5, 8);
            using var labels = new ChunkedLabelVolume(32, 24, 5, 8, true, labelPath);
            var density = new byte[32 * 24];
            for (var i = 0; i < density.Length; i++) density[i] = (byte)(i % 256);
            for (var z = 0; z < 5; z++) grayscale.WriteSliceZ(z, density);
            var dataset = new CtImageStackDataset("virtual", directory)
            {
                Width = 32, Height = 24, Depth = 5, VolumeData = grayscale, LabelData = labels
            };

            var timer = Stopwatch.StartNew();
            await MaterialOperations.AddVoxelsByThresholdAsync(grayscale, labels, 4, 100, 140, dataset);
            timer.Stop();

            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1));
            Assert.Equal(0, labels.DirtyChunkCount);
            var evaluated = new byte[32 * 24];
            labels.ReadSliceZ(2, evaluated);
            for (var i = 0; i < evaluated.Length; i++)
                Assert.Equal(density[i] is >= 100 and <= 140 ? (byte)4 : (byte)0, evaluated[i]);
            Assert.True(File.Exists(labelPath + ".rules.json"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AdaptiveStorage_MigratesManagedVolumesToMemoryMappedWithoutLosingData()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gaia-adaptive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var baseName = Path.GetFileName(directory);
        var volumePath = Path.Combine(directory, $"{baseName}.Volume.bin");
        try
        {
            var grayscale = new ChunkedVolume(12, 10, 4, 4);
            var graySlice = Enumerable.Range(0, 120).Select(value => (byte)value).ToArray();
            for (var z = 0; z < 4; z++) grayscale.WriteSliceZ(z, graySlice);
            await grayscale.SaveAsBinAsync(volumePath);
            var labels = new ChunkedLabelVolume(12, 10, 4, 4, false,
                Path.Combine(directory, $"{baseName}.Labels.bin"));
            labels[7, 6, 3] = 12;
            var dataset = new CtImageStackDataset("adaptive", directory)
            {
                Width = 12, Height = 10, Depth = 4, VolumeData = grayscale, LabelData = labels
            };

            await dataset.ForceMemoryMappedStorageAsync();

            Assert.True(dataset.VolumeData.IsMemoryMapped);
            Assert.True(dataset.LabelData.IsMemoryMapped);
            Assert.Equal(graySlice[6 * 12 + 7], dataset.VolumeData[7, 6, 3]);
            Assert.Equal(12, dataset.LabelData[7, 6, 3]);
            dataset.Unload();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TruncatedLabelMmf_FailsAtOpenWithLayoutErrorInsteadOfAccessorAggregateException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gaia-labels-truncated-{Guid.NewGuid():N}.bin");
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(32); writer.Write(32); writer.Write(8); writer.Write(8);
                writer.Write(4); writer.Write(4); writer.Write(1);
                writer.Write(new byte[10]);
            }
            var error = Assert.Throws<InvalidDataException>(() => ChunkedLabelVolume.LoadFromBin(path, true));
            Assert.Contains("truncated", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
