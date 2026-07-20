using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GAIA.Analysis.Pnm;
using GAIA.Data;
using GAIA.Data.Pnm;
using GAIA.Data.Loaders;
using GAIA.Interop.GaiaPrism;
using Xunit;

namespace VerificationTests;

public class DualPNMVerificationTests
{
    /// <summary>
    /// Builds a deterministic dual network with a 10×10×10 voxel, 1 µm/voxel bulk (1000 µm³) and
    /// two micro-networks whose micro-phase volumes are known, so the corrected effective-medium
    /// physics (audit C2/C4/C5) can be checked against hand-computed values.
    /// </summary>
    private static DualPNMDataset MakeDeterministicDual(DualPorosityCouplingMode mode)
    {
        var d = new DualPNMDataset("DetDual", "")
        {
            ImageWidth = 10, ImageHeight = 10, ImageDepth = 10, VoxelSize = 1.0f,
            DarcyPermeability = 10f // macro k
        };
        // Micro pore vols: 50 + 30 = 80 → bulk micro-porosity = 80/1000 = 0.08.
        // Phase vols: 50/0.5 + 30/0.3 = 100 + 100 = 200 → f = 200/1000 = 0.2.
        // k_micro (equal-weight geometric of 4 and 1) = 2 mD.
        d.AddMicroNetwork(1, new MicroPoreNetwork { MacroPoreID = 1, MicroVolume = 50f, MicroPorosity = 0.5f, MicroPermeability = 4f });
        d.AddMicroNetwork(2, new MicroPoreNetwork { MacroPoreID = 2, MicroVolume = 30f, MicroPorosity = 0.3f, MicroPermeability = 1f });
        d.Coupling.CouplingMode = mode;
        d.CalculateCombinedProperties();
        return d;
    }

    [Fact]
    public void DualPNM_CombinedProperties_UseBulkFractionAndVolumeWeightedGeometricMicroK()
    {
        var d = MakeDeterministicDual(DualPorosityCouplingMode.Parallel);

        // C4: TotalMicroPorosity is now the BULK micro-porosity fraction (Σ MicroVolume / bulk).
        Assert.Equal(0.08, d.Coupling.TotalMicroPorosity, 4);
        // C5: volume-weighted geometric micro-permeability = sqrt(4*1) = 2 mD.
        Assert.Equal(2.0, d.Coupling.EffectiveMicroPermeability, 4);
        // C2: parallel = (1-f)*k_macro + f*k_micro with phase fraction f = 0.2:
        // 0.8*10 + 0.2*2 = 8.4 mD.
        Assert.Equal(8.4, d.Coupling.CombinedPermeability, 4);
    }

    [Fact]
    public void DualPNM_SeriesIsHarmonic_AndBoundsBracketCombined()
    {
        var parallel = MakeDeterministicDual(DualPorosityCouplingMode.Parallel).Coupling.CombinedPermeability;
        var series = MakeDeterministicDual(DualPorosityCouplingMode.Series).Coupling.CombinedPermeability;

        // Series harmonic: 1 / (0.8/10 + 0.2/2) = 1/0.18 = 5.5556 mD.
        Assert.Equal(5.55556, series, 4);
        // Wiener bound ordering: harmonic (series) <= arithmetic (parallel).
        Assert.True(series <= parallel + 1e-4f, $"series {series} should be <= parallel {parallel}");

        // MassTransfer keeps its serialized name but is the weighted-arithmetic approximation,
        // so it must fall within the bounds too (equals the parallel value here).
        var massTransfer = MakeDeterministicDual(DualPorosityCouplingMode.MassTransfer).Coupling.CombinedPermeability;
        Assert.True(massTransfer >= series - 1e-4f && massTransfer <= parallel + 1e-4f);
    }

    [Fact]
    public void DualPNM_CreateSummary_CarriesCombinedPermeabilityAndTotalPorosityToPrism()
    {
        // 100^3 voxels at 2 µm → bulk = 8e6 µm³. One macro pore φ_macro = 0.2.
        var d = new DualPNMDataset("DualUpscale", "dual.pnm")
        {
            ImageWidth = 100, ImageHeight = 100, ImageDepth = 100, VoxelSize = 2.0f,
            DarcyPermeability = 50f
        };
        double bulk = 100.0 * 100 * 100 * System.Math.Pow(2.0, 3);
        d.Pores.Add(new Pore { ID = 1, Position = new Vector3(1, 1, 1), Radius = 5, VolumePhysical = (float)(bulk * 0.2) });
        // Micro pore vol 0.05*bulk, local porosity 0.4 → phase vol 0.125*bulk → f = 0.125.
        d.AddMicroNetwork(1, new MicroPoreNetwork
        {
            MacroPoreID = 1, MicroVolume = (float)(bulk * 0.05), MicroPorosity = 0.4f, MicroPermeability = 5f
        });

        var summary = UpscalingGpexExporter.CreateSummary(d);

        // C1: the dual-scale effective permeability reaches the summary and wins the Preferred selector.
        Assert.NotNull(summary.CombinedPermeabilityMilliDarcy);
        // parallel: (1-0.125)*50 + 0.125*5 = 44.375 mD.
        Assert.Equal(44.375, summary.CombinedPermeabilityMilliDarcy!.Value, 3);
        Assert.Equal(44.375, summary.PreferredPermeabilityMilliDarcy!.Value, 3);
        Assert.Equal(50.0, summary.DarcyPermeabilityMilliDarcy!.Value, 3);
        // Total porosity = φ_macro (0.2) + bulk micro-porosity (0.05) = 0.25.
        Assert.Equal(0.25, summary.PorosityFraction!.Value, 4);
    }

    [Fact]
    public void DualPNM_SaveLoadRoundTrip_PreservesMicroNetworksAndCoupling()
    {
        var d = new DualPNMDataset("RoundTrip", "rt.pnm")
        {
            ImageWidth = 10, ImageHeight = 10, ImageDepth = 10, VoxelSize = 1.0f, DarcyPermeability = 7f
        };
        d.Pores.Add(new Pore { ID = 1, Position = new Vector3(2, 3, 4), Radius = 2, VolumePhysical = 100f });
        d.AddMicroNetwork(5, new MicroPoreNetwork
        {
            MacroPoreID = 5, MicroVolume = 40f, MicroPorosity = 0.4f, MicroPermeability = 3f,
            MicroPores = { new Pore { ID = 11, Position = new Vector3(1, 1, 0), Radius = 0.5f, VolumePhysical = 2f } }
        });
        d.Coupling.CouplingMode = DualPorosityCouplingMode.Series;
        d.CalculateCombinedProperties();
        var expectedCombined = d.Coupling.CombinedPermeability;

        // G5: the polymorphic ISerializableDataset call must dispatch to the DUAL override.
        var dto = Assert.IsType<DualPNMDatasetDTO>(((ISerializableDataset)d).ToSerializableObject());
        Assert.Equal(nameof(DualPNMDataset), dto.TypeName);
        Assert.Single(dto.MicroNetworks);

        var reloaded = new DualPNMDataset(dto.Name, dto.FilePath);
        reloaded.ImportFromDTO(dto);

        Assert.Single(reloaded.MicroNetworks);
        Assert.Equal(5, reloaded.MicroNetworks[0].MacroPoreID);
        Assert.Equal(40f, reloaded.MicroNetworks[0].MicroVolume);
        Assert.Single(reloaded.MicroNetworks[0].MicroPores);
        Assert.Equal(DualPorosityCouplingMode.Series, reloaded.Coupling.CouplingMode);
        Assert.Equal(expectedCombined, reloaded.Coupling.CombinedPermeability);
        Assert.NotNull(reloaded.GetMicroNetwork(5));
    }

    [Fact]
    public void PNM_AtomicDiskPersistence_ReloadsCompleteNetworkAndDimensions()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gaia-pnm-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "network.pnm.json");
        try
        {
            var source = new PNMDataset("PersistentNetwork", "")
            {
                VoxelSize = 2.5f, ImageWidth = 40, ImageHeight = 30, ImageDepth = 20,
                Tortuosity = 1.7f, DarcyPermeability = 12.5f, BulkDiffusivity = 2.3e-9f,
                EffectiveDiffusivity = 8.1e-10f, FormationFactor = 2.84f, TransportTortuosity = 1.91f
            };
            // Distinct non-zero coordinates on every axis: Vector3 exposes X/Y/Z as fields, which
            // System.Text.Json drops without a converter, and the network then collapses onto the
            // origin. Positions left at Vector3.Zero would let that regression pass unnoticed.
            var firstPosition = new Vector3(12.5f, 7.25f, 31f);
            var secondPosition = new Vector3(22.5f, 17.75f, 3f);
            source.Pores.Add(new Pore { ID = 1, Position = firstPosition, Radius = 2 });
            source.Pores.Add(new Pore { ID = 2, Position = secondPosition, Radius = 3 });
            source.Throats.Add(new Throat { ID = 1, Pore1ID = 1, Pore2ID = 2, Radius = 1 });
            source.InitializeFromCurrentLists();

            source.ExportToJson(path);
            var reopened = new PNMDataset("Reopened", path);
            reopened.Load();

            Assert.Equal(Path.GetFullPath(path), source.FilePath);
            Assert.Equal(2, reopened.Pores.Count);
            Assert.Single(reopened.Throats);
            Assert.Equal(40, reopened.ImageWidth);
            Assert.Equal(20, reopened.ImageDepth);
            Assert.Equal(2.5f, reopened.VoxelSize);
            Assert.Equal(source.DarcyPermeability, reopened.DarcyPermeability);
            Assert.Equal(source.EffectiveDiffusivity, reopened.EffectiveDiffusivity);
            Assert.Equal(source.FormationFactor, reopened.FormationFactor);
            Assert.Equal(firstPosition, reopened.Pores.Single(p => p.ID == 1).Position);
            Assert.Equal(secondPosition, reopened.Pores.Single(p => p.ID == 2).Position);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            var imported = Assert.IsType<PNMDataset>(new PNMLoader(path).LoadAsync(null).GetAwaiter().GetResult());
            Assert.Equal(2, imported.Pores.Count);
            Assert.Single(imported.Throats);
            Assert.Equal(firstPosition, imported.Pores.Single(p => p.ID == 1).Position);
            Assert.Equal(secondPosition, imported.Pores.Single(p => p.ID == 2).Position);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void DualPNM_MicroPermeability_UsesFullSimulation()
    {
        // Arrange
        var dataset = new DualPNMDataset("TestDual", "");

        // Create a micro-network that is disconnected but has porosity
        // Kozeny-Carman (current implementation) will predict non-zero permeability based on porosity and radius
        // Full simulation should predict zero permeability because there are no throats/connections

        var microNet = new MicroPoreNetwork
        {
            MacroPoreID = 1,
            SEMPixelSize = 1.0f, // 1 um per pixel
            MicroPorosity = 0.2f,
            MicroVolume = 1000f
        };

        // Add some isolated pores
        microNet.MicroPores.Add(new Pore
        {
            ID = 1,
            Position = new Vector3(10, 10, 0),
            Radius = 5f,
            VolumePhysical = 100f
        });
        microNet.MicroPores.Add(new Pore
        {
            ID = 2,
            Position = new Vector3(90, 10, 0),
            Radius = 5f,
            VolumePhysical = 100f
        });

        // NO THROATS added -> Disconnected

        dataset.AddMicroNetwork(1, microNet);

        // Act
        // Use Darcy method for macro (doesn't matter here as we focus on micro)
        DualPNMSimulations.CalculateDualPermeability(dataset, PNMPermeabilityMethod.Darcy);

        // Assert
        var microPerm = dataset.MicroNetworks[0].MicroPermeability;

        // With Kozeny-Carman, this will be > 0
        // With Full Simulation, this should be 0 (or very close to it)
        // We assert that it is 0 to verify we switched to simulation
        Assert.Equal(0f, microPerm);
    }

    [Fact]
    public void DualPNM_MicroPermeability_SimulationReturnsValidValueForConnectedNetwork()
    {
        // Arrange
        var dataset = new DualPNMDataset("TestDualConnected", "");

        var microNet = new MicroPoreNetwork
        {
            MacroPoreID = 1,
            SEMPixelSize = 1.0f // 1 um per pixel
        };

        // Create a simple connected system (flow along X)
        // Pore 1 at X=0
        microNet.MicroPores.Add(new Pore
        {
            ID = 1,
            Position = new Vector3(0, 50, 0),
            Radius = 10f,
            VolumePhysical = 1000f
        });

        // Pore 2 at X=100
        microNet.MicroPores.Add(new Pore
        {
            ID = 2,
            Position = new Vector3(100, 50, 0),
            Radius = 10f,
            VolumePhysical = 1000f
        });

        // Throat connecting them
        microNet.MicroThroats.Add(new Throat
        {
            ID = 1,
            Pore1ID = 1,
            Pore2ID = 2,
            Radius = 5f
        });

        // Calculate porosity manually to satisfy KC if it were running
        // Total volume approx 100*100*1 (if 2D) or similar.
        // Let's just set a dummy porosity
        microNet.MicroPorosity = 0.1f;
        microNet.MicroVolume = 20000f; // total volume

        dataset.AddMicroNetwork(1, microNet);

        // Act
        DualPNMSimulations.CalculateDualPermeability(dataset, PNMPermeabilityMethod.Darcy);

        // Assert
        var microPerm = dataset.MicroNetworks[0].MicroPermeability;

        Assert.True(microPerm > 0, "Permeability should be positive for connected network");
    }
}
