using System.Numerics;
using GAIA.Data.Pnm;
using GAIA.UI;

namespace VerificationTests;

public sealed class PnmViewerColorTests
{
    [Fact]
    public void MappingReportsIndependentPhysicalQuantities()
    {
        var first = new Pore { ID = 1, Position = Vector3.Zero };
        var second = new Pore { ID = 2, Position = new Vector3(3, 4, 0) };
        var throat = new Throat { ID = 7, Pore1ID = 1, Pore2ID = 2, Radius = 2 };
        var pressures = new Dictionary<int, float> { [1] = 120, [2] = 95 };
        var flows = new Dictionary<int, float> { [7] = -3.5f };

        Assert.Equal(4f, PnmThroatColorMapping.GetValue(ThroatColorMode.Radius, throat, first, second, 2, pressures, flows));
        Assert.Equal(10f, PnmThroatColorMapping.GetValue(ThroatColorMode.Length, throat, first, second, 2, pressures, flows));
        Assert.Equal(25f, PnmThroatColorMapping.GetValue(ThroatColorMode.PressureDrop, throat, first, second, 2, pressures, flows));
        Assert.Equal(3.5f, PnmThroatColorMapping.GetValue(ThroatColorMode.FlowRate, throat, first, second, 2, pressures, flows));
    }

    [Fact]
    public void RangeIsFiniteAndExpandsConstantData()
    {
        Assert.Equal((0f, 1f), PnmThroatColorMapping.GetRange(new[] { float.NaN, float.PositiveInfinity }));
        Assert.Equal((2f, 3f), PnmThroatColorMapping.GetRange(new[] { 2f, 2f }));
        Assert.Equal((1f, 4f), PnmThroatColorMapping.GetRange(new[] { 1f, 4f, 2f }));
    }
}
