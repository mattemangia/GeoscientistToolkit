using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GAIA.Analysis.Pnm;
using GAIA.Data.Pnm;
using GAIA.Data.Loaders;
using Xunit;

namespace VerificationTests;

public class DualPNMVerificationTests
{
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
            source.Pores.Add(new Pore { ID = 1, Position = Vector3.Zero, Radius = 2 });
            source.Pores.Add(new Pore { ID = 2, Position = new Vector3(10, 0, 0), Radius = 3 });
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
            Assert.Equal(source.DarcyPermeability, reopened.DarcyPermeability);
            Assert.Equal(source.EffectiveDiffusivity, reopened.EffectiveDiffusivity);
            Assert.Equal(source.FormationFactor, reopened.FormationFactor);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            var imported = Assert.IsType<PNMDataset>(new PNMLoader(path).LoadAsync(null).GetAwaiter().GetResult());
            Assert.Equal(2, imported.Pores.Count);
            Assert.Single(imported.Throats);
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
