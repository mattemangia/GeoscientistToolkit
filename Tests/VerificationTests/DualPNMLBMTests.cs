using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Data.Pnm;
using Xunit;

namespace VerificationTests;

public class DualPNMLBMTests
{
    [Fact]
    public void DualPNM_LatticeBoltzmann_CalculatesPermeability()
    {
        // Arrange
        var dataset = new DualPNMDataset("TestDualLBM", "");
        dataset.VoxelSize = 1.0f; // 1 um

        // Create a simple pore network aligned along Z axis (Default flow axis)
        // P1 at Z=0, P2 at Z=100
        var p1 = new Pore { ID = 1, Position = new Vector3(50, 50, 0), Radius = 20f, VolumePhysical = 8000f };
        var p2 = new Pore { ID = 2, Position = new Vector3(50, 50, 100), Radius = 20f, VolumePhysical = 8000f };
        dataset.Pores.Add(p1);
        dataset.Pores.Add(p2);

        var t1 = new Throat { ID = 1, Pore1ID = 1, Pore2ID = 2, Radius = 5f }; // Narrow throat
        dataset.Throats.Add(t1);

        // Act
        DualPNMSimulations.CalculateDualPermeability(dataset, PNMPermeabilityMethod.LatticeBoltzmann);

        // Assert
        Assert.True(dataset.LatticeBoltzmannPermeability > 0, $"LatticeBoltzmannPermeability should be > 0. Actual: {dataset.LatticeBoltzmannPermeability}");

        // Note: With the fix, we are ONLY calculating LBM when method=LatticeBoltzmann.
        // Previously it calculated BOTH NS and LBM (well, it set NS flag to true for LBM method).
        // Now: CalculateNavierStokes = method == PNMPermeabilityMethod.NavierStokes
        // So if method is LBM, NS is NOT calculated.

        // Assert.True(dataset.NavierStokesPermeability > 0, ...); // REMOVE this assertion as it's no longer expected.

        // Check Tortuosity
        var tau = dataset.Tortuosity;
        var tau2 = tau * tau;

        var expectedEffective = dataset.LatticeBoltzmannPermeability / tau2;
        var actualEffective = dataset.Coupling.EffectiveMacroPermeability;

        // Verify that the effective macro permeability is derived from LBM results
        Assert.Equal(expectedEffective, actualEffective, 3);

        // Verify that it is NOT zero (sanity check)
        Assert.True(actualEffective > 0, "Effective macro permeability should be positive.");
    }
}
