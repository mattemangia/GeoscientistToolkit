using GAIA.Analysis.Pnm;
using GAIA.Data.CtImageStack;
using GAIA.Data.VolumeData;
using Xunit;

namespace VerificationTests;

/// <summary>
///     The out-of-core path must be an implementation detail, not a different algorithm: streaming a
///     volume through RAM in blocks may not change what network comes out. These tests force multiple
///     blocks with a tiny working-set budget and compare against the in-RAM generator.
/// </summary>
public sealed class OutOfCorePNMGeneratorTests
{
    private const int PoreMaterialId = 1;

    private static CtImageStackDataset CreateStack(ChunkedLabelVolume labels, int width, int height, int depth,
        string directory)
    {
        return new CtImageStackDataset($"pnm-ooc-{Guid.NewGuid():N}", directory)
        {
            Width = width, Height = height, Depth = depth,
            PixelSize = 1f, SliceThickness = 1f,
            LabelData = labels
        };
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gaia-pnmooc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    ///     A lattice of solid pore bodies joined by 2-voxel bars (thin enough that erosion erases
    ///     them, as sandstone throats are). The bodies and bars straddle the block cuts that a 1 MB
    ///     budget forces, so pore identity, volume and throat topology all depend on the face
    ///     stitching being exact.
    /// </summary>
    [Fact]
    public void GenerateOutOfCore_MultiBlock_MatchesInRamNetwork()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            const int size = 64;
            using var labels = new ChunkedLabelVolume(size, size, size, 16, false);

            // 3×3×3 grid of 8³ pore bodies centred every 20 voxels starting at 6.
            var centers = new[] { 10, 30, 50 };
            foreach (var cxb in centers)
            foreach (var cyb in centers)
            foreach (var czb in centers)
                for (var z = czb - 4; z < czb + 4; z++)
                for (var y = cyb - 4; y < cyb + 4; y++)
                for (var x = cxb - 4; x < cxb + 4; x++)
                    labels[x, y, z] = PoreMaterialId;

            // 2×2 bars along each axis between neighbouring bodies.
            foreach (var a in centers)
            foreach (var b in centers)
            {
                for (var x = 14; x < 46; x++)
                for (var y = a - 1; y < a + 1; y++)
                for (var z = b - 1; z < b + 1; z++)
                    labels[x, y, z] = PoreMaterialId;
                for (var y = 14; y < 46; y++)
                for (var x = a - 1; x < a + 1; x++)
                for (var z = b - 1; z < b + 1; z++)
                    labels[x, y, z] = PoreMaterialId;
                for (var z = 14; z < 46; z++)
                for (var x = a - 1; x < a + 1; x++)
                for (var y = b - 1; y < b + 1; y++)
                    labels[x, y, z] = PoreMaterialId;
            }

            var ct = CreateStack(labels, size, size, size, directory);

            PNMGeneratorOptions Options(bool outOfCore)
            {
                return new PNMGeneratorOptions
                {
                    MaterialId = PoreMaterialId,
                    Neighborhood = Neighborhood3D.N26,
                    Mode = GenerationMode.Conservative,
                    OutOfCoreStreaming = outOfCore,
                    // 1 MB forces the planner to cut several blocks out of 64³; the in-RAM
                    // reference uses a budget the whole volume fits into.
                    MaxWorkingSetMB = outOfCore ? 1 : 2048,
                    OutOfCoreHaloVoxels = 4
                };
            }

            var reference = PNMGenerator.Generate(ct, Options(false), null, CancellationToken.None);
            var streamed = PNMGenerator.Generate(ct, Options(true), null, CancellationToken.None);

            // Topology and mass balance must match exactly.
            Assert.Equal(reference.Pores.Count, streamed.Pores.Count);
            Assert.Equal(reference.Throats.Count, streamed.Throats.Count);
            Assert.Equal(reference.Pores.Sum(p => (double)p.VolumeVoxels),
                streamed.Pores.Sum(p => (double)p.VolumeVoxels));

            // Pore-by-pore volumes match within the contested band at watershed fronts that fall on
            // a cut: those voxels may be attributed to the neighbouring pore, never lost (the total
            // above is exact). A bar cross-section is 4 voxels, so allow a few bar-lengths of drift.
            var refVolumes = reference.Pores.Select(p => p.VolumeVoxels).OrderBy(v => v).ToArray();
            var oocVolumes = streamed.Pores.Select(p => p.VolumeVoxels).OrderBy(v => v).ToArray();
            for (var i = 0; i < refVolumes.Length; i++)
                Assert.True(Math.Abs(refVolumes[i] - oocVolumes[i]) <= 64,
                    $"Pore volume drifted beyond the contested band: {refVolumes[i]} vs {oocVolumes[i]}");

            // Coordination numbers must survive the stitching too.
            var refConnections = reference.Pores.Select(p => p.Connections).OrderBy(v => v).ToArray();
            var oocConnections = streamed.Pores.Select(p => p.Connections).OrderBy(v => v).ToArray();
            Assert.Equal(refConnections, oocConnections);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A single solid body straddling a forced block cut: without exact face stitching it would
    ///     come out as two pores, or with the wrong volume if core voxels were double counted.
    /// </summary>
    [Fact]
    public void GenerateOutOfCore_PoreSpanningBlockCut_IsOnePoreWithExactVolume()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            const int size = 64;
            using var labels = new ChunkedLabelVolume(size, size, size, 16, false);
            // 10×10×40 bar through the middle of the stack, crossing every Z cut a 1 MB budget makes.
            for (var z = 12; z < 52; z++)
            for (var y = 27; y < 37; y++)
            for (var x = 27; x < 37; x++)
                labels[x, y, z] = PoreMaterialId;

            var ct = CreateStack(labels, size, size, size, directory);
            var pnm = PNMGenerator.Generate(ct, new PNMGeneratorOptions
            {
                MaterialId = PoreMaterialId,
                Neighborhood = Neighborhood3D.N26,
                Mode = GenerationMode.Conservative,
                OutOfCoreStreaming = true,
                MaxWorkingSetMB = 1,
                OutOfCoreHaloVoxels = 4
            }, null, CancellationToken.None);

            Assert.Single(pnm.Pores);
            Assert.Equal(10 * 10 * 40, pnm.Pores[0].VolumeVoxels);
            Assert.Empty(pnm.Throats);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     With a budget the whole volume fits into, the streaming path degenerates to a single block
    ///     and must reproduce the in-RAM network exactly — including radii, which have no stitching
    ///     approximation to hide behind.
    /// </summary>
    [Fact]
    public void GenerateOutOfCore_SingleBlock_IsExactlyTheInRamNetwork()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            const int size = 48;
            using var labels = new ChunkedLabelVolume(size, size, size, 16, false);
            for (var z = 6; z < 20; z++)
            for (var y = 6; y < 20; y++)
            for (var x = 6; x < 42; x++)
                labels[x, y, z] = PoreMaterialId;
            for (var z = 26; z < 42; z++)
            for (var y = 26; y < 42; y++)
            for (var x = 26; x < 42; x++)
                labels[x, y, z] = PoreMaterialId;

            var ct = CreateStack(labels, size, size, size, directory);

            PNMGeneratorOptions Options(bool outOfCore)
            {
                return new PNMGeneratorOptions
                {
                    MaterialId = PoreMaterialId,
                    Neighborhood = Neighborhood3D.N26,
                    Mode = GenerationMode.Conservative,
                    OutOfCoreStreaming = outOfCore,
                    MaxWorkingSetMB = 2048
                };
            }

            var reference = PNMGenerator.Generate(ct, Options(false), null, CancellationToken.None);
            var streamed = PNMGenerator.Generate(ct, Options(true), null, CancellationToken.None);

            Assert.Equal(reference.Pores.Count, streamed.Pores.Count);
            Assert.Equal(reference.Throats.Count, streamed.Throats.Count);

            var refPores = reference.Pores.OrderBy(p => p.Position.Z).ThenBy(p => p.Position.Y)
                .ThenBy(p => p.Position.X).ToArray();
            var oocPores = streamed.Pores.OrderBy(p => p.Position.Z).ThenBy(p => p.Position.Y)
                .ThenBy(p => p.Position.X).ToArray();
            for (var i = 0; i < refPores.Length; i++)
            {
                Assert.Equal(refPores[i].VolumeVoxels, oocPores[i].VolumeVoxels);
                Assert.Equal((double)refPores[i].Radius, oocPores[i].Radius, 3);
                Assert.Equal((double)refPores[i].Position.X, oocPores[i].Position.X, 3);
                Assert.Equal((double)refPores[i].Position.Y, oocPores[i].Position.Y, 3);
                Assert.Equal((double)refPores[i].Position.Z, oocPores[i].Position.Z, 3);
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
