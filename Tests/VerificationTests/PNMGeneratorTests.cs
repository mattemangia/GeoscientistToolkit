using GAIA.Analysis.Pnm;
using GAIA.Data.CtImageStack;
using GAIA.Data.VolumeData;
using Xunit;

namespace VerificationTests;

public sealed class PNMGeneratorTests
{
    private const int PoreMaterialId = 1;

    private static CtImageStackDataset CreateStack(ChunkedLabelVolume labels, int width, int height, int depth,
        string directory)
    {
        return new CtImageStackDataset($"pnm-{Guid.NewGuid():N}", directory)
        {
            Width = width, Height = height, Depth = depth,
            PixelSize = 1f, SliceThickness = 1f,
            LabelData = labels
        };
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gaia-pnmgen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    ///     A plate two voxels thick is thinner than the 26-neighbourhood structuring element, so the
    ///     erosion erases it and it produces no seed. The expansion only grows outwards from seeds, so
    ///     such a region used to keep label 0 and be dropped from the network along with its volume —
    ///     which is most of the network in a sandstone, whose throats are a few voxels across.
    /// </summary>
    [Fact]
    public void Generate_PoreErasedByErosion_IsRecoveredInsteadOfDropped()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var labels = new ChunkedLabelVolume(32, 32, 32, 16, false);
            for (var z = 10; z < 12; z++)
            for (var y = 6; y < 26; y++)
            for (var x = 6; x < 26; x++)
                labels[x, y, z] = PoreMaterialId;

            var ct = CreateStack(labels, 32, 32, 32, directory);
            var options = new PNMGeneratorOptions
            {
                MaterialId = PoreMaterialId,
                Neighborhood = Neighborhood3D.N26,
                Mode = GenerationMode.Conservative
            };

            var pnm = PNMGenerator.Generate(ct, options, null, CancellationToken.None);

            var plateVoxels = 20 * 20 * 2;
            Assert.Single(pnm.Pores);
            Assert.Equal(plateVoxels, pnm.Pores[0].VolumeVoxels);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A region that does not fit the working-set budget must fail loudly. Sampling it down is
    ///     nearest-neighbour decimation of a binary mask: it aliases away the throats and the network
    ///     that comes out describes no real rock.
    /// </summary>
    [Fact]
    public void Generate_RegionOverBudget_FailsInsteadOfDownsamplingSilently()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var labels = new ChunkedLabelVolume(256, 256, 256, 64, false);
            var ct = CreateStack(labels, 256, 256, 256, directory);

            // 256³ = 16.7M voxels × 20 B/voxel ≈ 335 MB, above the 256 MB floor.
            var options = new PNMGeneratorOptions
            {
                MaterialId = PoreMaterialId,
                MaxWorkingSetMB = 256,
                AllowDownsampling = false
            };

            var error = Assert.Throws<InvalidOperationException>(
                () => PNMGenerator.Generate(ct, options, null, CancellationToken.None));
            Assert.Contains("Crop a smaller region", error.Message);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary> Downsampling stays reachable, but only when the caller asks for it explicitly. </summary>
    [Fact]
    public void Generate_RegionOverBudget_ProceedsWhenDownsamplingIsAllowed()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var labels = new ChunkedLabelVolume(256, 256, 256, 64, false);
            for (var z = 100; z < 140; z++)
            for (var y = 100; y < 140; y++)
            for (var x = 100; x < 140; x++)
                labels[x, y, z] = PoreMaterialId;

            var ct = CreateStack(labels, 256, 256, 256, directory);
            var options = new PNMGeneratorOptions
            {
                MaterialId = PoreMaterialId,
                MaxWorkingSetMB = 256,
                AllowDownsampling = true
            };

            var pnm = PNMGenerator.Generate(ct, options, null, CancellationToken.None);

            Assert.NotEmpty(pnm.Pores);
            // Sampled 1/2 per axis, so the analysed grid and its voxel edge both halve.
            Assert.Equal(128, pnm.ImageWidth);
            Assert.Equal(2f, pnm.VoxelSize);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     The seed labelling, the watershed expansion and the unseeded-region recovery must share one
    ///     connectivity. Here a solid block survives erosion and seeds one pore; a thin bar touches it
    ///     only at a single corner, so it is erased by erosion and has no seed of its own. Under N26 the
    ///     watershed reaches the bar across that corner and the whole thing is one pore. If the
    ///     expansion used N6 the bar would stay unreached and be recovered as a spurious second pore.
    /// </summary>
    [Fact]
    public void Generate_DiagonalContact_ExpandsIntoUnseededRegion_NotASpuriousPore()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var labels = new ChunkedLabelVolume(32, 32, 32, 16, false);

            // Solid 10³ block: thick enough to keep a seed after one N26 erosion.
            for (var z = 6; z < 16; z++)
            for (var y = 6; y < 16; y++)
            for (var x = 6; x < 16; x++)
                labels[x, y, z] = PoreMaterialId;

            // A 2-thick bar whose nearest voxel (16,16,16) meets the block corner (15,15,15) only
            // diagonally. Two voxels thick, so erosion erases it and it never gets its own seed.
            for (var x = 16; x < 26; x++)
            for (var y = 16; y < 18; y++)
            for (var z = 16; z < 18; z++)
                labels[x, y, z] = PoreMaterialId;

            var ct = CreateStack(labels, 32, 32, 32, directory);
            var options = new PNMGeneratorOptions
            {
                MaterialId = PoreMaterialId,
                Neighborhood = Neighborhood3D.N26,
                Mode = GenerationMode.Conservative
            };

            var pnm = PNMGenerator.Generate(ct, options, null, CancellationToken.None);

            // One N26-connected component → one pore. N6 expansion would strand the bar → two.
            Assert.Single(pnm.Pores);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     Cropping to a sub-volume is how a plug-sized stack is brought within the budget at full
    ///     resolution, so the region has to bound both the analysis and the reported geometry.
    /// </summary>
    [Fact]
    public void Generate_Roi_RestrictsAnalysisAndGeometryToTheSelectedSubVolume()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var labels = new ChunkedLabelVolume(64, 64, 64, 16, false);
            // A cube of material confined to the low corner.
            for (var z = 4; z < 16; z++)
            for (var y = 4; y < 16; y++)
            for (var x = 4; x < 16; x++)
                labels[x, y, z] = PoreMaterialId;

            var ct = CreateStack(labels, 64, 64, 64, directory);

            var insideOptions = new PNMGeneratorOptions
            {
                MaterialId = PoreMaterialId,
                Roi = new PnmRegion { MinX = 0, MinY = 0, MinZ = 0, MaxX = 32, MaxY = 32, MaxZ = 32 }
            };
            var inside = PNMGenerator.Generate(ct, insideOptions, null, CancellationToken.None);

            Assert.NotEmpty(inside.Pores);
            Assert.Equal(32, inside.ImageWidth);
            Assert.Equal(32, inside.ImageDepth);

            var elsewhereOptions = new PNMGeneratorOptions
            {
                MaterialId = PoreMaterialId,
                Roi = new PnmRegion { MinX = 32, MinY = 32, MinZ = 32, MaxX = 64, MaxY = 64, MaxZ = 64 }
            };
            var elsewhere = PNMGenerator.Generate(ct, elsewhereOptions, null, CancellationToken.None);

            Assert.Empty(elsewhere.Pores);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
