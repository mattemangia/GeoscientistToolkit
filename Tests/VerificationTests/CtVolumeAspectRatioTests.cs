using System.Numerics;
using GAIA.Data.CtImageStack;

namespace VerificationTests;

public class CtVolumeAspectRatioTests
{
    [Fact]
    public void LongCore_PreservesPhysicalLengthRelativeToDiameter()
    {
        var scale = CtVolume3DViewer.CalculateNormalizedPhysicalScale(512, 512, 2048, 1f, 1f);

        AssertVector(scale, new Vector3(0.25f, 0.25f, 1f));
    }

    [Fact]
    public void AnisotropicSlices_UseSliceThicknessForDepth()
    {
        var scale = CtVolume3DViewer.CalculateNormalizedPhysicalScale(100, 100, 100, 0.5f, 2f);

        AssertVector(scale, new Vector3(0.25f, 0.25f, 1f));
    }

    [Fact]
    public void MissingSliceThickness_FallsBackToInPlanePixelSize()
    {
        var scale = CtVolume3DViewer.CalculateNormalizedPhysicalScale(200, 100, 50, 0.2f, 0f);

        AssertVector(scale, new Vector3(1f, 0.5f, 0.25f));
    }

    [Fact]
    public void OtsuThreshold_SeparatesAirFromDenseCore()
    {
        var voxels = new byte[10_000];
        Array.Fill(voxels, (byte)8, 0, 7_000);
        Array.Fill(voxels, (byte)180, 7_000, 3_000);

        var threshold = CtVolume3DViewer.CalculateOtsuThreshold(voxels);

        Assert.InRange(threshold, (byte)8, (byte)179);
    }

    private static void AssertVector(Vector3 actual, Vector3 expected)
    {
        const float tolerance = 0.00001f;
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Z, expected.Z - tolerance, expected.Z + tolerance);
    }
}
