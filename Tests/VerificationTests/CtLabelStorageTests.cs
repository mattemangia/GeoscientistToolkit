using GAIA.Data.VolumeData;

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
}
