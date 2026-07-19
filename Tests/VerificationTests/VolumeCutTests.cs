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
