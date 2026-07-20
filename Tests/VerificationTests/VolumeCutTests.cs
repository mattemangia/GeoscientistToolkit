using System.Numerics;
using GAIA.Analysis.VolumeCut;
using GAIA.Data.CtImageStack;
using GAIA.Data.VolumeData;

namespace VerificationTests;

public sealed class VolumeCutTests
{
    private static CtImageStackDataset CreateDataset(int width, int height, int depth,
        out ChunkedVolume grayscale, out ChunkedLabelVolume labels)
    {
        grayscale = new ChunkedVolume(width, height, depth, 8);
        labels = new ChunkedLabelVolume(width, height, depth, 8, false);
        var slice = new byte[width * height];
        Array.Fill(slice, (byte)200);
        for (var z = 0; z < depth; z++)
        {
            grayscale.WriteSliceZ(z, slice);
            labels.WriteSliceZ(z, slice); // label id 200 everywhere
        }

        return new CtImageStackDataset($"cut-{Guid.NewGuid():N}", Path.GetTempPath())
        {
            Width = width, Height = height, Depth = depth, PixelSize = 1,
            VolumeData = grayscale, LabelData = labels
        };
    }

    [Fact]
    public void BoxKeepInside_ClearsEverythingOutsideTheBox()
    {
        var dataset = CreateDataset(20, 16, 12, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        var state = new VolumeCutState
        {
            Shape = VolumeCutShapeKind.Box,
            KeepMode = VolumeCutKeepMode.KeepInside,
            BoxMin = new Vector3(5, 4, 3),
            BoxMax = new Vector3(14, 11, 8)
        };

        VolumeCutProcessor.Apply(dataset, state, CancellationToken.None);

        Assert.Equal(200, grayscale[10, 8, 5]);
        Assert.Equal(200, labels[5, 4, 3]);
        Assert.Equal(200, grayscale[14, 11, 8]);
        Assert.Equal(0, grayscale[4, 8, 5]);
        Assert.Equal(0, grayscale[10, 8, 2]);
        Assert.Equal(0, labels[15, 8, 5]);
        Assert.Equal(0, labels[10, 12, 5]);
    }

    [Fact]
    public void BoxKeepOutside_ClearsOnlyTheBoxInterior()
    {
        var dataset = CreateDataset(20, 16, 12, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        var state = new VolumeCutState
        {
            Shape = VolumeCutShapeKind.Box,
            KeepMode = VolumeCutKeepMode.KeepOutside,
            BoxMin = new Vector3(5, 4, 3),
            BoxMax = new Vector3(14, 11, 8)
        };

        VolumeCutProcessor.Apply(dataset, state, CancellationToken.None);

        Assert.Equal(0, grayscale[10, 8, 5]);
        Assert.Equal(0, labels[10, 8, 5]);
        Assert.Equal(200, grayscale[4, 8, 5]);
        Assert.Equal(200, grayscale[10, 8, 2]);
        Assert.Equal(200, labels[0, 0, 0]);
        Assert.Equal(200, labels[19, 15, 11]);
    }

    [Fact]
    public void SphereKeepInside_MatchesAnalyticMembership()
    {
        var dataset = CreateDataset(24, 24, 24, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        var state = new VolumeCutState
        {
            Shape = VolumeCutShapeKind.Sphere,
            KeepMode = VolumeCutKeepMode.KeepInside,
            SphereCenter = new Vector3(12, 12, 12),
            SphereRadius = 6
        };

        VolumeCutProcessor.Apply(dataset, state, CancellationToken.None);

        for (var z = 0; z < 24; z += 3)
        for (var y = 0; y < 24; y += 3)
        for (var x = 0; x < 24; x += 3)
        {
            var expected = state.InsideShape(x, y, z) ? 200 : 0;
            Assert.Equal(expected, grayscale[x, y, z]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CylinderKeepInside_RespectsAxisAndExtent(int axis)
    {
        var dataset = CreateDataset(20, 20, 20, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        var state = new VolumeCutState
        {
            Shape = VolumeCutShapeKind.Cylinder,
            KeepMode = VolumeCutKeepMode.KeepInside,
            CylinderAxis = axis,
            CylinderCenter = new Vector3(10, 10, 10),
            CylinderRadius = 5,
            CylinderAxisMin = 4,
            CylinderAxisMax = 15
        };

        VolumeCutProcessor.Apply(dataset, state, CancellationToken.None);

        Assert.Equal(200, grayscale[10, 10, 10]); // center always inside
        for (var z = 0; z < 20; z += 2)
        for (var y = 0; y < 20; y += 2)
        for (var x = 0; x < 20; x += 2)
        {
            var expected = state.InsideShape(x, y, z) ? 200 : 0;
            Assert.Equal(expected, grayscale[x, y, z]);
        }
    }

    [Fact]
    public void LockAspectRatio_BoxEdgeDragRescalesOtherAxesAboutTheirCenters()
    {
        var start = new VolumeCutState
        {
            BoxMin = new Vector3(10, 20, 30),
            BoxMax = new Vector3(30, 40, 50) // 20 voxels per axis
        };
        var state = new VolumeCutState
        {
            LockAspectRatio = true,
            BoxMin = start.BoxMin,
            BoxMax = start.BoxMax
        };

        // Drag the X-max edge from 30 to 50: X size 20 -> 40, factor 2.
        state.BoxMax = state.BoxMax with { X = 50 };
        state.ApplyBoxAspect(start, 0);

        Assert.Equal(50, state.BoxMax.X);
        Assert.Equal(10, state.BoxMin.X); // dragged axis keeps its opposite edge
        Assert.Equal(10, state.BoxMin.Y); // Y: center 30, half 10*2 -> [10, 50]
        Assert.Equal(50, state.BoxMax.Y);
        Assert.Equal(20, state.BoxMin.Z); // Z: center 40, half 10*2 -> [20, 60]
        Assert.Equal(60, state.BoxMax.Z);

        // Without the lock nothing else moves.
        var free = new VolumeCutState { BoxMin = start.BoxMin, BoxMax = start.BoxMax };
        free.BoxMax = free.BoxMax with { X = 50 };
        free.ApplyBoxAspect(start, 0);
        Assert.Equal(start.BoxMin.Y, free.BoxMin.Y);
        Assert.Equal(start.BoxMax.Z, free.BoxMax.Z);
    }

    [Fact]
    public void LockAspectRatio_CylinderRadiusAndExtentStayLinked()
    {
        var start = new VolumeCutState
        {
            Shape = VolumeCutShapeKind.Cylinder,
            CylinderRadius = 10,
            CylinderAxisMin = 20,
            CylinderAxisMax = 60 // extent 40, center 40
        };

        var fromRadius = new VolumeCutState
        {
            LockAspectRatio = true,
            CylinderRadius = 20, // doubled by a drag
            CylinderAxisMin = start.CylinderAxisMin,
            CylinderAxisMax = start.CylinderAxisMax
        };
        fromRadius.ApplyCylinderAspectFromRadius(start);
        Assert.Equal(0, fromRadius.CylinderAxisMin);  // center 40, half 20*2
        Assert.Equal(80, fromRadius.CylinderAxisMax);

        var fromExtent = new VolumeCutState
        {
            LockAspectRatio = true,
            CylinderRadius = start.CylinderRadius,
            CylinderAxisMin = 30,
            CylinderAxisMax = 50 // extent halved: 40 -> 20
        };
        fromExtent.ApplyCylinderAspectFromExtent(start);
        Assert.Equal(5, fromExtent.CylinderRadius);
    }

    [Fact]
    public void BoxKeepInside_CropResizesDatasetToTheBoxAndKeepsContent()
    {
        var dataset = CreateDataset(20, 16, 12, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        var state = new VolumeCutState
        {
            Shape = VolumeCutShapeKind.Box,
            KeepMode = VolumeCutKeepMode.KeepInside,
            CropToRegion = true,
            BoxMin = new Vector3(5, 4, 3),
            BoxMax = new Vector3(14, 11, 8)
        };

        var result = VolumeCutProcessor.Apply(dataset, state, CancellationToken.None);
        Assert.NotNull(result);
        result.CommitTo(dataset);

        // Dataset shrank to the box (inclusive bounds -> 10x8x6).
        Assert.Equal(10, dataset.Width);
        Assert.Equal(8, dataset.Height);
        Assert.Equal(6, dataset.Depth);
        Assert.Equal(10, dataset.VolumeData.Width);
        Assert.Equal(6, dataset.LabelData.Depth);

        // Every kept voxel survived the crop; there is no empty border left.
        for (var z = 0; z < dataset.Depth; z++)
        for (var y = 0; y < dataset.Height; y++)
        for (var x = 0; x < dataset.Width; x++)
        {
            Assert.Equal(200, dataset.VolumeData[x, y, z]);
            Assert.Equal(200, dataset.LabelData[x, y, z]);
        }
    }

    [Fact]
    public void SphereKeepInside_CropResizesToBoundingBoxAndClearsCorners()
    {
        var dataset = CreateDataset(24, 24, 24, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        var state = new VolumeCutState
        {
            Shape = VolumeCutShapeKind.Sphere,
            KeepMode = VolumeCutKeepMode.KeepInside,
            CropToRegion = true,
            SphereCenter = new Vector3(12, 12, 12),
            SphereRadius = 6
        };

        var result = VolumeCutProcessor.Apply(dataset, state, CancellationToken.None);
        Assert.NotNull(result);
        result.CommitTo(dataset);

        // Bounding box of a radius-6 sphere centred at 12 -> [6, 18], 13 voxels per axis.
        Assert.Equal(13, dataset.Width);
        Assert.Equal(13, dataset.Height);
        Assert.Equal(13, dataset.Depth);

        // Centre of the cropped volume is inside the sphere; the corners of the box are not.
        Assert.Equal(200, dataset.VolumeData[6, 6, 6]);
        Assert.Equal(0, dataset.VolumeData[0, 0, 0]);
        Assert.Equal(0, dataset.VolumeData[12, 12, 12]);
    }

    [Fact]
    public void KeepOutside_CropLeavesDimensionsUnchanged()
    {
        var dataset = CreateDataset(20, 16, 12, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        var state = new VolumeCutState
        {
            Shape = VolumeCutShapeKind.Box,
            KeepMode = VolumeCutKeepMode.KeepOutside,
            CropToRegion = true,
            BoxMin = new Vector3(5, 4, 3),
            BoxMax = new Vector3(14, 11, 8)
        };

        // Keep-outside keeps the whole exterior, so there is nothing to trim: applied in place.
        var result = VolumeCutProcessor.Apply(dataset, state, CancellationToken.None);
        Assert.Null(result);
        Assert.Equal(20, dataset.Width);
        Assert.Equal(0, grayscale[10, 8, 5]);
        Assert.Equal(200, grayscale[0, 0, 0]);
    }

    [Fact]
    public void TargetSelection_LabelsOnlyLeavesGrayscaleUntouched()
    {
        var dataset = CreateDataset(16, 16, 8, out var grayscale, out var labels);
        using var _ = grayscale;
        using var __ = labels;
        var state = new VolumeCutState
        {
            Shape = VolumeCutShapeKind.Box,
            KeepMode = VolumeCutKeepMode.KeepInside,
            ApplyToGrayscale = false,
            BoxMin = new Vector3(4, 4, 2),
            BoxMax = new Vector3(11, 11, 5)
        };

        VolumeCutProcessor.Apply(dataset, state, CancellationToken.None);

        Assert.Equal(200, grayscale[0, 0, 0]);
        Assert.Equal(200, grayscale[15, 15, 7]);
        Assert.Equal(0, labels[0, 0, 0]);
        Assert.Equal(200, labels[5, 5, 3]);
    }
}
