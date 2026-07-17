using GAIA.Data.VolumeData;
using GAIA.Data.CtImageStack;
using System.Diagnostics;
using GAIA.Analysis.RemoveSmallIslands;
using GAIA;
using GAIA.Analysis.ParticleSeparator;
using GAIA.Data.CtImageStack.Segmentation;
using GAIA.Analysis.AmbientOcclusionSegmentation;

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

    [Fact]
    public async Task MemoryMappedVolumes_MapOnlyOneChunkAndSupportConcurrentBoundaryReads()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gaia-windowed-mmf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var grayPath = Path.Combine(directory, "gray.bin");
        var labelPath = Path.Combine(directory, "labels.bin");
        try
        {
            using (var source = new ChunkedVolume(35, 29, 18, 8))
            {
                for (var z = 0; z < source.Depth; z++)
                {
                    var slice = new byte[source.Width * source.Height];
                    for (var y = 0; y < source.Height; y++)
                    for (var x = 0; x < source.Width; x++)
                        slice[y * source.Width + x] = (byte)((x * 3 + y * 5 + z * 7) % 251);
                    source.WriteSliceZ(z, slice);
                }
                await source.SaveAsBinAsync(grayPath);
            }

            using var gray = await ChunkedVolume.LoadFromBinAsync(grayPath, true);
            using var labels = new ChunkedLabelVolume(35, 29, 18, 8, true, labelPath);
            Assert.Equal(512, gray.MappedWindowSize);
            Assert.Equal(512, labels.MappedWindowSize);

            Parallel.For(0, 18, z =>
            {
                var slice = new byte[35 * 29];
                gray.ReadSliceZ(z, slice);
                Assert.Equal((byte)((34 * 3 + 28 * 5 + z * 7) % 251), slice[28 * 35 + 34]);
                slice[28 * 35 + 34] = (byte)(z + 1);
                labels.WriteSliceZChangedChunks(z, slice,
                    Enumerable.Repeat(true, labels.ChunkCountX * labels.ChunkCountY).ToArray());
            });

            var yz = new byte[labels.Height * labels.Depth];
            labels.ReadSliceYZ(34, yz);
            for (var z = 0; z < labels.Depth; z++)
                Assert.Equal((byte)(z + 1), yz[z * labels.Height + 28]);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void BulkLabelWrites_ReportOnlyChangedSlicesForIncrementalCaches()
    {
        using var labels = new ChunkedLabelVolume(17, 13, 5, 8, false);
        var changed = new List<int>();
        labels.SliceChanged += changed.Add;
        var slice = new byte[17 * 13];

        labels.WriteSliceZ(1, slice); // all-zero sparse slice remains unchanged
        slice[7] = 4;
        labels.WriteSliceZ(3, slice);
        labels.WriteSliceZ(3, slice); // identical rewrite remains unchanged

        Assert.Equal(new[] { 3 }, changed);
    }

    [Fact]
    public void IslandAnalysis_StreamsCrossSliceComponentsAndCleansOnlySmallOnes()
    {
        using var grayscale = new ChunkedVolume(10, 8, 3, 4);
        using var labels = new ChunkedLabelVolume(10, 8, 3, 4, false);
        // Two-voxel island.
        labels[1, 1, 0] = 5;
        labels[2, 1, 0] = 5;
        // Four-voxel component connected through Z.
        labels[7, 5, 0] = 5;
        labels[7, 5, 1] = 5;
        labels[8, 5, 1] = 5;
        labels[8, 5, 2] = 5;
        var dataset = new CtImageStackDataset("islands", Path.GetTempPath())
        {
            Width = 10, Height = 8, Depth = 3, VolumeData = grayscale, LabelData = labels
        };
        using var processor = new IslandAnalysisProcessor();
        var material = new Material("target", System.Numerics.Vector4.One, 0, 255, 5);

        var result = processor.Analyze(dataset, material, CancellationToken.None);
        Assert.Equal(new long[] { 2, 4 }, result.Particles.Select(p => p.VoxelCount).Order().ToArray());
        var small = result.Particles.Where(p => p.VoxelCount < 3).Select(p => p.Id).ToHashSet();
        var preview = processor.GeneratePreviewMask(dataset, result, small, CancellationToken.None);
        Assert.IsType<SparseSliceCtPreviewVolume>(preview);
        Assert.Equal(255, preview.GetVoxel(1, 1, 0));
        Assert.Equal(0, preview.GetVoxel(7, 5, 0));
        processor.ApplyCleaning(dataset, result, small, CancellationToken.None);

        Assert.Equal(0, labels[1, 1, 0]);
        Assert.Equal(0, labels[2, 1, 0]);
        Assert.Equal(5, labels[7, 5, 0]);
        Assert.Equal(5, labels[8, 5, 2]);
    }

    [Fact]
    public void ParticleSeparator_StreamsComponentsWithoutAllocatingLabelVolume()
    {
        using var grayscale = new ChunkedVolume(12, 9, 4, 4);
        using var labels = new ChunkedLabelVolume(12, 9, 4, 4, false);
        labels[1, 1, 0] = 6; labels[2, 1, 0] = 6; labels[2, 2, 0] = 6;
        labels[8, 6, 1] = 6; labels[8, 6, 2] = 6; labels[9, 6, 2] = 6; labels[9, 6, 3] = 6;
        var dataset = new CtImageStackDataset("particles", Path.GetTempPath())
        {
            Width = 12, Height = 9, Depth = 4, PixelSize = 1,
            VolumeData = grayscale, LabelData = labels
        };
        var material = new Material("particles", System.Numerics.Vector4.One, 0, 255, 6);
        using var processor = new AcceleratedProcessor { SelectedAcceleration = AccelerationType.CPU };

        var result = processor.SeparateParticles(dataset, material, true, false, 1, 0,
            CancellationToken.None);

        Assert.Null(result.LabelVolume);
        Assert.Equal(new long[] { 3, 4 }, result.Particles.Select(p => p.VoxelCount).Order().ToArray());
        var crossSlice = result.Particles.Single(p => p.VoxelCount == 4);
        Assert.Equal(1, crossSlice.Bounds.MinZ);
        Assert.Equal(3, crossSlice.Bounds.MaxZ);
    }

    [Fact]
    public void MorphologicalInterpolation_UsesBoundedSignedDistanceSlices()
    {
        using var grayscale = new ChunkedVolume(24, 18, 6, 8);
        using var labels = new ChunkedLabelVolume(24, 18, 6, 8, false);
        var dataset = new CtImageStackDataset("interpolation", Path.GetTempPath())
        {
            Width = 24, Height = 18, Depth = 6, VolumeData = grayscale, LabelData = labels
        };
        using var manager = new SegmentationManager(dataset);
        var interpolation = new InterpolationManager(dataset, manager);
        var start = new byte[24 * 18];
        var end = new byte[24 * 18];
        for (var y = 5; y <= 11; y++)
        for (var x = 3; x <= 9; x++) start[y * 24 + x] = 255;
        for (var y = 5; y <= 11; y++)
        for (var x = 13; x <= 19; x++) end[y * 24 + x] = 255;

        var result = interpolation.InterpolateSlices(start, 0, end, 4, 0,
            InterpolationManager.InterpolationType.Morphological3D);

        Assert.Equal(3, result.Count);
        var middle = result[(2, 0)];
        Assert.True(middle.Any(value => value != 0));
        Assert.True(middle.Skip(6 * 24).Take(6 * 24).Any(value => value != 0));
    }

    [Fact]
    public void AmbientOcclusion_AdaptiveGridCoversWholeDatasetWhenApplied()
    {
        using var grayscale = new ChunkedVolume(64, 64, 20, 16);
        grayscale.Fill(255);
        using var labels = new ChunkedLabelVolume(64, 64, 20, 16, false);
        var dataset = new CtImageStackDataset("ao-adaptive", Path.GetTempPath())
        {
            Width = 64, Height = 64, Depth = 20, VolumeData = grayscale, LabelData = labels
        };
        using var processor = new AmbientOcclusionSegmentation();
        var settings = new AmbientOcclusionSettings
        {
            UseGpu = false, RayCount = 1, RayLength = 1, MaxWorkingSetMB = 1,
            MaterialThreshold = 1, SegmentationThreshold = 1
        };

        var result = processor.ComputeAmbientOcclusion(dataset, settings);
        Assert.True(result.SamplingStep > 1);
        Assert.Equal((64 + result.SamplingStep - 1) / result.SamplingStep, result.AoField.GetLength(0));
        for (var z = 0; z < result.SegmentationMask.GetLength(2); z++)
        for (var y = 0; y < result.SegmentationMask.GetLength(1); y++)
        for (var x = 0; x < result.SegmentationMask.GetLength(0); x++)
            result.SegmentationMask[x, y, z] = true;

        processor.ApplySegmentation(dataset, result, 9);

        Assert.Equal(9, labels[0, 0, 0]);
        Assert.Equal(9, labels[63, 63, 19]);
    }

    [Fact]
    public async Task ExplicitPersistence_SavesManagedGrayscaleAndLabelsWithProgress()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gaia-persist-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using var grayscale = new ChunkedVolume(15, 11, 4, 4);
            var graySlice = Enumerable.Range(0, 15 * 11).Select(i => (byte)(i % 251)).ToArray();
            for (var z = 0; z < 4; z++) grayscale.WriteSliceZ(z, graySlice);
            using var labels = new ChunkedLabelVolume(15, 11, 4, 4, false);
            labels[14, 10, 3] = 12;
            var dataset = new CtImageStackDataset("persist", directory)
            {
                Width = 15, Height = 11, Depth = 4, VolumeData = grayscale, LabelData = labels
            };
            var reported = new List<float>();
            await dataset.PersistCtDataAsync(progress: new InlineProgress<float>(reported.Add));
            var baseName = Path.GetFileName(directory);
            var volumePath = Path.Combine(directory, baseName + ".Volume.bin");
            var labelPath = Path.Combine(directory, baseName + ".Labels.bin");

            Assert.True(File.Exists(volumePath));
            Assert.True(File.Exists(labelPath));
            using var reloadedGray = await ChunkedVolume.LoadFromBinAsync(volumePath, true);
            using var reloadedLabels = ChunkedLabelVolume.LoadFromBin(labelPath, true);
            Assert.Equal(graySlice[10 * 15 + 14], reloadedGray[14, 10, 3]);
            Assert.Equal(12, reloadedLabels[14, 10, 3]);
            Assert.Contains(reported, value => value >= 1);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CtOperationCoordinator_AlwaysRunsOffCallerAndSerializesOperations()
    {
        using var grayscale = new ChunkedVolume(2, 2, 1, 2);
        using var labels = new ChunkedLabelVolume(2, 2, 1, 2, false);
        var dataset = new CtImageStackDataset("queue", Path.GetTempPath())
        { Width = 2, Height = 2, Depth = 1, VolumeData = grayscale, LabelData = labels };
        var order = new List<int>();
        var gate = new object();
        var context = new RejectingSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        CtOperationHandle first;
        CtOperationHandle second;
        try
        {
            first = CtOperationCoordinator.For(dataset).Enqueue("first", async (_, progress) =>
            {
                lock (gate) order.Add(1);
                progress.Report(.5f);
                await Task.Delay(20).ConfigureAwait(false);
                lock (gate) order.Add(2);
            });
            second = CtOperationCoordinator.For(dataset).Enqueue("second", (_, _) =>
            {
                lock (gate) order.Add(3);
                return Task.CompletedTask;
            });
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }

        await Task.WhenAll(first.Completion, second.Completion);
        Assert.Equal(0, context.PostCount);
        Assert.Equal(new[] { 1, 2, 3 }, order);
        Assert.Equal(CtOperationStatus.Completed, second.Status);
        Assert.Equal(1, first.Progress);
    }

    private sealed class RejectingSynchronizationContext : SynchronizationContext
    {
        public int PostCount;
        public override void Post(SendOrPostCallback d, object state)
        {
            Interlocked.Increment(ref PostCount);
            throw new InvalidOperationException("CT operation attempted to resume on the caller synchronization context.");
        }
    }

    [Fact]
    public void VirtualPreviewVolume_ExtractsOrthogonalSlicesAndCompleteLod()
    {
        var preview = new FunctionalCtPreviewVolume(20, 16, 12,
            (x, y, z) => x >= 15 && y >= 12 && z >= 9 ? (byte)255 : (byte)0);
        var xy = preview.ReadSlice(0, 10, 20, 16);
        var xz = preview.ReadSlice(1, 14, 20, 12);
        var yz = preview.ReadSlice(2, 18, 16, 12);
        var lod = preview.BuildLod(10, 8, 6);

        Assert.Equal(255, xy[15 * 20 + 19]);
        Assert.Equal(255, xz[11 * 20 + 19]);
        Assert.Equal(255, yz[11 * 16 + 15]);
        Assert.Equal(255, lod[^1]);
    }

    [Fact]
    public async Task StackRegistration_CombinesXAndYUsingBulkSlices()
    {
        using var firstVolume = new ChunkedVolume(4, 3, 2, 2);
        using var secondVolume = new ChunkedVolume(4, 3, 2, 2);
        firstVolume.Fill(11); secondVolume.Fill(22);
        var first = new CtImageStackDataset("first", Path.GetTempPath())
        { Width = 4, Height = 3, Depth = 2, VolumeData = firstVolume };
        var second = new CtImageStackDataset("second", Path.GetTempPath())
        { Width = 4, Height = 3, Depth = 2, VolumeData = secondVolume };
        var registration = new StackRegistration(RegistrationMethod.CPU_SIMD);

        using var alongX = await registration.RegisterStacksAsync(first, second,
            RegistrationAlignment.AlongX, 0);
        using var alongY = await registration.RegisterStacksAsync(first, second,
            RegistrationAlignment.AlongY, 0);

        Assert.Equal((byte)11, alongX[0, 0, 1]);
        Assert.Equal((byte)22, alongX[7, 2, 1]);
        Assert.Equal((byte)11, alongY[0, 0, 1]);
        Assert.Equal((byte)22, alongY[3, 5, 1]);
    }

    [Fact]
    public void LegacyGigabyteChunks_AreSplitIntoBoundedMmfWindows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gaia-large-chunk-{Guid.NewGuid():N}.bin");
        try
        {
            using var labels = new ChunkedLabelVolume(1025, 1, 1, 1024, true, path);
            Assert.Equal(64L * 1024 * 1024, labels.MappedWindowSize);
            labels[1024, 0, 0] = 37; // jumps beyond the first 1 GiB padded chunk
            Assert.Equal(37, labels[1024, 0, 0]);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
