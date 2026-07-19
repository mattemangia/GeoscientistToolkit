using GAIA;
using GAIA.Analysis.AmbientOcclusionSegmentation;
using GAIA.Analysis.ParticleSeparator;
using GAIA.Data.CtImageStack;
using GAIA.Data.VolumeData;

namespace VerificationTests;

public sealed class ParticleSeparationTests
{
    private static CtImageStackDataset CreateDataset(int width, int height, int depth,
        out ChunkedVolume grayscale, out ChunkedLabelVolume labels)
    {
        grayscale = new ChunkedVolume(width, height, depth, 8);
        labels = new ChunkedLabelVolume(width, height, depth, 8, false);
        return new CtImageStackDataset($"particles-{Guid.NewGuid():N}", Path.GetTempPath())
        {
            Width = width, Height = height, Depth = depth, PixelSize = 1,
            VolumeData = grayscale, LabelData = labels
        };
    }

    private static void FillBox(ChunkedLabelVolume labels, byte id,
        int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
    {
        for (var z = minZ; z <= maxZ; z++)
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
            labels[x, y, z] = id;
    }

    [Fact]
    public void OpeningFilter_SplitsParticlesJoinedByThinBridge()
    {
        var dataset = CreateDataset(24, 12, 12, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        // Two 5x5x5 blobs joined by a 1-voxel-thick bridge.
        FillBox(labels, 6, 2, 6, 3, 7, 3, 7);
        FillBox(labels, 6, 15, 19, 3, 7, 3, 7);
        FillBox(labels, 6, 7, 14, 5, 5, 5, 5);
        var material = new Material("grains", System.Numerics.Vector4.One, 0, 255, 6);
        using var processor = new AcceleratedProcessor();

        using var unfiltered = processor.SeparateParticles(dataset, material, true, false, 1, 0,
            null, CancellationToken.None);
        Assert.Single(unfiltered.Particles);

        var filters = new ParticleSeparationFilters { MorphologicalOpening = true, OpeningRadius = 1 };
        using var opened = processor.SeparateParticles(dataset, material, true, false, 1, 0,
            filters, CancellationToken.None);
        Assert.Equal(2, opened.Particles.Count);
        // Opening must keep the blob cores in place.
        Assert.All(opened.Particles, particle => Assert.True(particle.VoxelCount > 20));
    }

    [Fact]
    public void DespeckleFilter_RemovesIsolatedNoiseVoxels()
    {
        var dataset = CreateDataset(16, 16, 8, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        FillBox(labels, 3, 2, 8, 2, 8, 1, 6); // solid blob
        labels[13, 13, 3] = 3; // isolated speck
        var material = new Material("grains", System.Numerics.Vector4.One, 0, 255, 3);
        using var processor = new AcceleratedProcessor();

        using var raw = processor.SeparateParticles(dataset, material, true, false, 1, 0,
            null, CancellationToken.None);
        Assert.Equal(2, raw.Particles.Count);

        var filters = new ParticleSeparationFilters { Despeckle = true };
        using var despeckled = processor.SeparateParticles(dataset, material, true, false, 1, 0,
            filters, CancellationToken.None);
        Assert.Single(despeckled.Particles);
    }

    [Fact]
    public void SingleSliceMode_AppliesFiltersInPlane()
    {
        var dataset = CreateDataset(20, 14, 4, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        // Two 4x4 squares on slice z=2 joined by a 1-voxel-wide bridge, plus a speck.
        FillBox(labels, 7, 2, 5, 4, 7, 2, 2);
        FillBox(labels, 7, 12, 15, 4, 7, 2, 2);
        for (var x = 5; x <= 12; x++) labels[x, 5, 2] = 7;
        labels[18, 12, 2] = 7;
        var material = new Material("grains", System.Numerics.Vector4.One, 0, 255, 7);
        using var processor = new AcceleratedProcessor();
        var filters = new ParticleSeparationFilters { MorphologicalOpening = true, OpeningRadius = 1 };

        using var raw = processor.SeparateParticles(dataset, material, false, false, 1, 2,
            null, CancellationToken.None);
        using var opened = processor.SeparateParticles(dataset, material, false, false, 1, 2,
            filters, CancellationToken.None);

        Assert.Equal(2, raw.Particles.Count); // bridged pair + speck
        Assert.Equal(2, opened.Particles.Count); // pair split in-plane, speck eroded away
        Assert.All(opened.Particles, particle => Assert.True(particle.VoxelCount >= 4));
        Assert.All(opened.Particles, particle => Assert.Equal(2, particle.Center.Z));
    }

    [Fact]
    public void ApplyParticleLabels_WritesEachParticleOutOfCore()
    {
        var dataset = CreateDataset(20, 10, 6, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        FillBox(labels, 5, 1, 4, 1, 4, 1, 4);   // 64 voxels
        FillBox(labels, 5, 10, 12, 2, 4, 2, 3); // 18 voxels
        var material = new Material("grains", System.Numerics.Vector4.One, 0, 255, 5);
        using var processor = new AcceleratedProcessor();
        using var result = processor.SeparateParticles(dataset, material, true, false, 1, 0,
            null, CancellationToken.None);
        Assert.Equal(2, result.Particles.Count);

        var large = result.Particles.OrderByDescending(p => p.VoxelCount).First();
        var small = result.Particles.OrderBy(p => p.VoxelCount).First();
        var map = new Dictionary<int, byte> { [large.SourceLabel] = 40, [small.SourceLabel] = 41 };
        processor.ApplyParticleLabels(dataset, result, map, CancellationToken.None);

        Assert.Equal(40, labels[2, 2, 2]);
        Assert.Equal(41, labels[11, 3, 2]);
        Assert.Equal(0, labels[19, 9, 5]);
    }

    [Fact]
    public void AmbientOcclusion_SegmentsEnclosedCavityButNotOpenExterior()
    {
        const int size = 40;
        using var grayscale = new ChunkedVolume(size, size, size, 8);
        using var labels = new ChunkedLabelVolume(size, size, size, 8, false);
        // Solid box with an enclosed 3x3x3 cavity at its center.
        var slice = new byte[size * size];
        for (var z = 0; z < size; z++)
        {
            Array.Clear(slice);
            if (z is >= 8 and <= 31)
                for (var y = 8; y <= 31; y++)
                for (var x = 8; x <= 31; x++)
                    slice[y * size + x] = 255;
            if (z is >= 19 and <= 21)
                for (var y = 19; y <= 21; y++)
                for (var x = 19; x <= 21; x++)
                    slice[y * size + x] = 0;
            grayscale.WriteSliceZ(z, slice);
        }
        var dataset = new CtImageStackDataset($"ao-cavity-{Guid.NewGuid():N}", Path.GetTempPath())
        {
            Width = size, Height = size, Depth = size, VolumeData = grayscale, LabelData = labels
        };
        using var processor = new AmbientOcclusionSegmentation();
        var settings = new AmbientOcclusionSettings
        {
            UseGpu = false, RayCount = 64, RayLength = size, MaterialThreshold = 128,
            SegmentationThreshold = 0.5f, MaxWorkingSetMB = 64
        };

        var result = processor.ComputeAmbientOcclusion(dataset, settings);

        Assert.Equal(1, result.SamplingStep);
        // Enclosed cavity: rays blocked in every direction -> low openness -> pore.
        Assert.True(result.SegmentationMask[20, 20, 20], "cavity center must be segmented as pore");
        // Open exterior corner: most rays leave the volume -> high openness -> not a pore.
        Assert.False(result.SegmentationMask[1, 1, 1], "open exterior must not be segmented");
        // Material voxel: excluded from segmentation entirely.
        Assert.False(result.SegmentationMask[10, 10, 10], "material must not be segmented");
        Assert.Equal(1f, result.AoField[10, 10, 10]);
        Assert.True(result.AoField[20, 20, 20] < 0.1f);
        Assert.True(result.AoField[1, 1, 1] > 0.5f);
    }
}
