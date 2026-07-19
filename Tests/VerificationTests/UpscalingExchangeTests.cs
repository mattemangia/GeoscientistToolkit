using System.Numerics;
using GAIA.Data.Borehole;
using GAIA.Data.Pnm;
using GAIA.Interop.GaiaPrism;

namespace VerificationTests;

public sealed class UpscalingExchangeTests
{
    [Fact]
    public void CreateSummary_ConvertsPnmDatasetFaithfully()
    {
        var pnm = MakePnm("Sample A", porosityTarget: 0.25, darcyMilliDarcy: 120);
        var summary = UpscalingGpexExporter.CreateSummary(pnm);
        Assert.Equal("sample-a", summary.Id);
        Assert.Equal(120, summary.DarcyPermeabilityMilliDarcy!.Value, 3);
        Assert.Equal(0.25, summary.PorosityFraction!.Value, 6);
        Assert.Equal(120, summary.PreferredPermeabilityMilliDarcy!.Value, 3);
        Assert.Null(summary.LatticeBoltzmannPermeabilityMilliDarcy);
    }

    [Fact]
    public void CreateWellRecord_MapsUnitsTracksAndPnmAssignments()
    {
        var pnm = MakePnm("Plug 12", porosityTarget: 0.2, darcyMilliDarcy: 50);
        var summary = UpscalingGpexExporter.CreateSummary(pnm);
        var summaries = new Dictionary<string, PnmNetworkSummary> { [summary.Id] = summary };
        var borehole = MakeBorehole();
        var assignments = new[] { new UpscalingGpexExporter.PnmWellAssignment(pnm, 10, 20) };

        var well = UpscalingGpexExporter.CreateWellRecord(borehole, assignments, summaries, [pnm]);

        Assert.Equal(2, well.Intervals.Count);
        var upper = well.Intervals[0];
        var lower = well.Intervals[1];
        // Upper unit keeps its measured parameters (porosity percent → fraction, mD → m²).
        Assert.Equal(0.30, IntervalUpscaler.ScalarProperty(upper, UpscalingPropertyNames.Porosity)!.Value, 6);
        Assert.Equal(200, IntervalUpscaler.PermeabilityMilliDarcy(upper)!.Value, 6);
        Assert.Empty(upper.PnmAssignments);
        // Lower unit had no measurements: the assigned PNM fills them by upscaling.
        Assert.Single(lower.PnmAssignments);
        Assert.Equal(summary.Id, lower.PnmAssignments[0].PnmId);
        Assert.Equal(UpscalingGpexExporter.PnmUpscaledOrigin, lower.PropertyOrigin);
        Assert.Equal(0.2, IntervalUpscaler.ScalarProperty(lower, UpscalingPropertyNames.Porosity)!.Value, 6);
        Assert.Equal(50, IntervalUpscaler.PermeabilityMilliDarcy(lower)!.Value, 4);
        // Parameter track became a log.
        Assert.Contains(well.Logs, l => l.Name == "Gamma" && l.DepthsMetres.Length == 2);
    }

    [Fact]
    public void ExportAndRead_RoundTripsThroughGpexArchive()
    {
        var root = Path.Combine(Path.GetTempPath(), "gaia-upscaling-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pnm = MakePnm("Plug 12", porosityTarget: 0.2, darcyMilliDarcy: 50);
            var borehole = MakeBorehole();
            var package = Path.Combine(root, "upscaling.gpex");
            var manifest = UpscalingGpexExporter.Export(package, "project-x", [borehole], [pnm],
                [new UpscalingGpexExporter.PnmWellAssignment(pnm, 10, 20)]);
            Assert.Equal(UpscalingExchangeContract.DomainId, manifest.Domain);
            Assert.Equal(ExchangeDirection.GaiaToPrism, manifest.Direction);
            Assert.Contains(manifest.EffectiveProperties, p => p.Name == UpscalingPropertyNames.HorizontalPermeability && p.Unit == "m2");

            var read = UpscalingGpexImporter.Read(package);
            Assert.Single(read.Wells.Wells);
            Assert.Single(read.PnmSummaries);
            Assert.Equal(2, read.Wells.Wells[0].Intervals.Count);
            Assert.True(read.Validation.IsValid);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ImportWell_PopulatesBoreholeDatasetWithGaiaUnits()
    {
        var interval = new WellIntervalRecord
        {
            Id = "layer-1", TopDepthMetres = 0, BottomDepthMetres = 40, Lithology = "Sandstone",
            Properties = new Dictionary<string, QuantityValue>
            {
                [UpscalingPropertyNames.Porosity] = new([0.18], "1"),
                [UpscalingPropertyNames.Permeability] = new([PetrophysicalUnits.MilliDarcyToSquareMetres(75)], "m2"),
                [UpscalingPropertyNames.Density] = new([2350.0], "kg/m3")
            },
            PropertyOrigin = "PrismModel"
        };
        var well = new WellRecord
        {
            Id = "w-1", Name = "PRISM Well", TotalDepthMetres = 40,
            Intervals = [interval],
            Logs = [new WellLogRecord { Name = "temperature", Unit = "degC", DepthsMetres = [0, 40], Values = [12, 14] }]
        };

        var borehole = UpscalingGpexImporter.ToBoreholeDataset(well, Path.Combine(Path.GetTempPath(), "prism-well.borehole"));

        var unit = borehole.GetLithologyUnitAtDepth(20);
        Assert.NotNull(unit);
        Assert.Equal("Sandstone", unit!.LithologyType);
        Assert.Equal(18.0, unit.Parameters["Porosity"], 3);       // percent
        Assert.Equal(75.0, unit.Parameters["Permeability"], 3);   // millidarcy
        Assert.Equal(2350.0, unit.Parameters["Density"], 1);
        Assert.True(borehole.ParameterTracks.ContainsKey("temperature"));
    }

    [Fact]
    public void SharedContract_MatchesPrismCanonicalUnits()
    {
        Assert.Equal("m2", UpscalingPropertyNames.CanonicalUnits[UpscalingPropertyNames.Permeability]);
        Assert.Equal("1", UpscalingPropertyNames.CanonicalUnits[UpscalingPropertyNames.Porosity]);
        Assert.Equal(9.869233e-16, PetrophysicalUnits.MilliDarcyInSquareMetres);
        Assert.Equal("upscaling", UpscalingExchangeContract.DomainId);
        Assert.Equal("1.0.0", UpscalingExchangeContract.Version);
    }

    private static PNMDataset MakePnm(string name, double porosityTarget, float darcyMilliDarcy)
    {
        var pnm = new PNMDataset(name, name + ".pnm")
        {
            VoxelSize = 2.0f, ImageWidth = 100, ImageHeight = 100, ImageDepth = 100,
            DarcyPermeability = darcyMilliDarcy, Tortuosity = 1.5f, FormationFactor = 14f
        };
        // One synthetic pore holding the whole target pore volume (µm³).
        var bulk = 100.0 * 100 * 100 * Math.Pow(2.0, 3);
        pnm.Pores.Add(new Pore
        {
            ID = 1, Position = new Vector3(50, 50, 50), Radius = 10,
            VolumePhysical = (float)(bulk * porosityTarget)
        });
        pnm.Throats.Add(new Throat { ID = 1, Pore1ID = 1, Pore2ID = 1, Radius = 2 });
        return pnm;
    }

    private static BoreholeDataset MakeBorehole()
    {
        var borehole = BoreholeDataset.CreateEmpty("Well-1", "well1.borehole");
        borehole.WellName = "Well-1";
        borehole.TotalDepth = 20;
        var upper = new LithologyUnit { Name = "Sand", LithologyType = "Sandstone", DepthFrom = 0, DepthTo = 10 };
        upper.Parameters["Porosity"] = 30f;        // GAIA stores percent
        upper.Parameters["Permeability"] = 200f;   // millidarcy
        var lower = new LithologyUnit { Name = "Lime", LithologyType = "Limestone", DepthFrom = 10, DepthTo = 20 };
        borehole.AddLithologyUnit(upper);
        borehole.AddLithologyUnit(lower);
        borehole.ParameterTracks["Gamma"] = new ParameterTrack
        {
            Name = "Gamma", Unit = "API", Color = new Vector4(1, 0, 0, 1),
            Points = [new ParameterPoint { Depth = 0, Value = 40 }, new ParameterPoint { Depth = 20, Value = 60 }]
        };
        return borehole;
    }
}
