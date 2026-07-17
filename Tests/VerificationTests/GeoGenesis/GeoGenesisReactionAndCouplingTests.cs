using GAIA.GeoGenesis;
using GAIA.GeoGenesis.Materials;
using GAIA.GeoGenesis.Thermodynamics;

namespace GAIA.VerificationTests;

/// <summary>
/// Tests for the GeoGenesis reaction builder, reaction-path simulator, geothermal coupling
/// (scaling + element extraction) and CRAFT aquifer thermodynamics, against peer-reviewed values.
/// </summary>
public class GeoGenesisReactionAndCouplingTests
{
    static GeoGenesisReactionAndCouplingTests() => Logger.EchoToConsole = false;

    private static readonly CompoundLibrary Lib = CompoundLibrary.Instance;
    private static ReactionGenerator Gen() => new(Lib);

    private static WaterComposition Brine() => new WaterComposition { pH = 7.5 }
        .Set("Calcium Ion", 0.01).Set("Bicarbonate", 0.02).Set("Sodium Ion", 0.01).Set("Chloride Ion", 0.01);

    [Theory]
    [InlineData("Calcite = Ca+2 + CO3-2")]
    [InlineData("Calcite + 2 H+ = Ca+2 + CO2(aq) + H2O")]
    [InlineData("Halite = Na+ + Cl-")]
    [InlineData("Gypsum = Ca+2 + SO4-2 + 2 H2O")]
    [InlineData("Dolomite -> Ca+2 + Mg+2 + 2 CO3-2")]
    public void EquationParser_BalancesCommonReactions(string equation)
    {
        var parsed = Gen().ParseEquation(equation);
        Assert.True(parsed.Ok, parsed.Error);
        Assert.True(parsed.MassBalanced, parsed.BalanceDetail);
        Assert.True(parsed.ChargeBalanced, parsed.BalanceDetail);
        Assert.True(double.IsFinite(parsed.Reaction!.LogK_25C));
    }

    [Fact]
    public void EquationParser_AcceptsBothChargeNotations_AndNames()
    {
        // "Ca+2" (sign-first) and "Ca2+" (number-first) must resolve to the same species.
        var a = Gen().ParseEquation("Calcite = Ca+2 + CO3-2");
        var b = Gen().ParseEquation("Calcite = Ca2+ + CO32-");
        Assert.True(a.Ok && b.Ok);
        Assert.Equal(a.Reaction!.LogK_25C, b.Reaction!.LogK_25C, 6);
    }

    [Fact]
    public void EquationParser_RejectsUnknownSpecies()
    {
        var p = Gen().ParseEquation("Unobtainium = Ca+2");
        Assert.False(p.Ok);
        Assert.Contains("Unknown", p.Error);
    }

    [Fact]
    public void ReactionPath_HeatingCalciteWater_PrecipitatesCalcite()
    {
        // Calcite has retrograde solubility: heating a Ca–HCO3 water supersaturates it, so along a
        // 25→90 °C path calcite must precipitate (positive net moles). (Plummer & Busenberg, 1982.)
        var sim = new ReactionPathSimulator(Lib);
        var schedule = ReactionPathSimulator.LinearSchedule(298.15, 363.15, 1.0, 1.0, 8, 3600);
        var result = sim.Run(Brine().ToState(Lib, Gen()), schedule, new[] { "Calcite" }, precipitate: true);

        Assert.Equal(8, result.Points.Count);
        Assert.All(result.Points, p => Assert.True(double.IsFinite(p.SaturationIndices["Calcite"])));
        Assert.True(result.NetMineralMoles.GetValueOrDefault("Calcite") > 0,
            $"expected calcite precipitation on heating, got {result.NetMineralMoles.GetValueOrDefault("Calcite"):E2} mol");
    }

    [Fact]
    public void GeothermalCoupler_HeatingTrajectory_DepositsCalciteScale()
    {
        var coupler = new GeothermalCoupler(Lib);
        var traj = new GeothermalTrajectory();
        for (int y = 0; y <= 10; y++) traj.Add(y, 298.15 + y * 6, 50, 50.0); // heating, 50 kg/s
        var scaling = coupler.EvaluateScaling(Brine(), traj, new[] { "Calcite" });

        Assert.Single(scaling);
        Assert.Equal(11, scaling[0].SaturationIndex.Count);
        Assert.All(scaling[0].SaturationIndex, si => Assert.True(double.IsFinite(si)));
        Assert.True(scaling[0].TotalScale_kg > 0, $"expected calcite scale, got {scaling[0].TotalScale_kg:F2} kg");
    }

    [Fact]
    public void GeothermalCoupler_LithiumExtraction_MatchesMassBalance()
    {
        // Recoverable Li = c·Q·recovery·t. For 200 mg/L, 50 kg/s, 90 %, 10 yr:
        //   200e-6 kg/kg · 50 kg/s · 0.9 · (10·365.25·86400 s) / 1000 ≈ 2840 tonnes.
        var coupler = new GeothermalCoupler(Lib);
        var traj = new GeothermalTrajectory();
        for (int y = 0; y <= 10; y++) traj.Add(y, 350, 50, 50.0);
        var li = coupler.EvaluateExtraction(traj, "Li", 200.0, 0.90);

        var expected = 200e-6 * 50.0 * 0.90 * (10 * 365.25 * 86400.0) / 1000.0;
        Assert.True(Math.Abs(li.Total_tonnes - expected) < 10.0, $"got {li.Total_tonnes:F1}, expected ~{expected:F1} t");
    }

    [Fact]
    public void AquiferModel_LithologyMapping_AndSegmentEvaluation()
    {
        var model = new AquiferThermoModel(Lib);
        Assert.Contains("Calcite", model.MineralsForLithology("limestone"));
        Assert.Contains("Quartz", model.MineralsForLithology("sandstone"));

        var seg = new AquiferSegment
        {
            Name = "S1", LithologyCode = "limestone", Porosity = 0.15,
            BulkDensity_kg_m3 = 2700, Temperature_K = 323.15, Pressure_bar = 20, Volume_m3 = 1000
        };
        var r = model.EvaluateSegment(seg, Brine());

        Assert.True(r.IonicStrength_molkg is > 0 and < 0.2, $"ionic strength {r.IonicStrength_molkg}");
        Assert.True(double.IsFinite(r.SaturationIndices["Calcite"]));
        Assert.True(r.ReactiveSurfaceArea_m2 > 0);
        Assert.Equal(150.0, r.PoreVolume_m3, 6); // φ·V = 0.15·1000
    }

    [Fact]
    public void WaterProperties_DensityAndDielectric_ArePhysical()
    {
        // Regression guard for the fixed IAPWS water-properties bug (was ρ≈5 kg/m³, ε≈−3.5).
        var (eps, rho) = WaterPropertiesIAPWS.GetWaterProperties(298.15, 1.0);
        Assert.InRange(rho, 990.0, 1000.0); // ~997 kg/m³
        Assert.InRange(eps, 76.0, 80.0);    // ~78.3
    }
}
