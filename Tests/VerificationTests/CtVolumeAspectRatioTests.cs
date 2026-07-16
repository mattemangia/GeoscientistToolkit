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

    private static void AssertVector(Vector3 actual, Vector3 expected)
    {
        const float tolerance = 0.00001f;
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Z, expected.Z - tolerance, expected.Z + tolerance);
    }
}
