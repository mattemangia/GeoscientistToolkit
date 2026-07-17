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
}
