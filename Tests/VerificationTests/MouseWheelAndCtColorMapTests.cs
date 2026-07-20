using GAIA.Data.CtImageStack;
using GAIA.UI.OpenTk;

namespace VerificationTests;

public class MouseWheelAndCtColorMapTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1f, 3f)]
    [InlineData(-1f, -3f)]
    [InlineData(0.25f, 0.75f)]
    public void MouseWheelDelta_IsScaledWithoutLosingFractionalInput(float input, float expected)
    {
        Assert.Equal(expected, ImGuiController.NormalizeMouseWheelDelta(input));
    }

    [Fact]
    public void RainbowColorMap_MatchesVolumeShaderReferenceSamples()
    {
        AssertVector(CtColorMap.Apply(0f, 3), 1f, 0f, 0f);
        AssertVector(CtColorMap.Apply(1f / 3f, 3), 0f, 1f, 0f);
        AssertVector(CtColorMap.Apply(2f / 3f, 3), 0f, 0f, 1f);
    }

    [Fact]
    public void RainbowColorMap_DoesNotFallBackToGrayscale()
    {
        var color = CtColorMap.Apply(0.5f, 3);
        Assert.False(Math.Abs(color.X - color.Y) < 1e-6f && Math.Abs(color.Y - color.Z) < 1e-6f);
    }

    private static void AssertVector(System.Numerics.Vector4 actual, float r, float g, float b)
    {
        Assert.InRange(actual.X, r - 1e-5f, r + 1e-5f);
        Assert.InRange(actual.Y, g - 1e-5f, g + 1e-5f);
        Assert.InRange(actual.Z, b - 1e-5f, b + 1e-5f);
        Assert.InRange(actual.W, 1f - 1e-5f, 1f + 1e-5f);
    }
}
