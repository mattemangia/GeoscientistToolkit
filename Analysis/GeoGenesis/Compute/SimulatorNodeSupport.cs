// GAIA.GeoGenesis/Compute/SimulatorNodeSupport.cs
//
// Self-contained replacement for the GeoscientistToolkit.Network.SimulatorNodeSupport base
// class the solvers were written against. In the Geoscientist's Toolkit this provided opt-in
// distribution of simulation jobs across a cluster of compute nodes; that distributed
// infrastructure is intentionally NOT carried into GAIA.GeoGenesis.
//
// The solvers (ThermodynamicSolver, KineticsSolver, ReactiveTransportSolver,
// MultiphaseReactiveTransportSolver) still derive from this type and read the protected
// _useNodes flag, so the base is preserved here as a local-execution-only stub. All
// computation runs in-process; node distribution can be re-introduced later by PRISM's own
// orchestration layer without touching the engine.

namespace GAIA.GeoGenesis.Compute;

/// <summary>
///     Base class for GeoGenesis solvers. In this build it always runs locally
///     (<see cref="UseNodes"/> == false); the constructor signature is retained for source
///     compatibility with the ported solver hierarchy.
/// </summary>
public abstract class SimulatorNodeSupport
{
    /// <summary>Always false in GAIA.GeoGenesis: every solver runs in-process.</summary>
    protected readonly bool _useNodes;

    protected SimulatorNodeSupport(bool? useNodesOverride = null)
    {
        // Distributed execution is not part of the GeoGenesis port.
        _useNodes = false;
        if (useNodesOverride == true)
        {
            Logger.LogWarning(
                "SimulatorNodeSupport: distributed node execution is not available in GAIA.GeoGenesis; " +
                "falling back to local in-process computation.");
        }
    }

    /// <summary>Whether this solver distributes work across compute nodes. Always false here.</summary>
    public bool UseNodes => _useNodes;
}
