// GAIA.GeoGenesis/GeoGenesisModule.cs

using GAIA.GeoGenesis.Materials;
using GAIA.GeoGenesis.Multiphase;
using GAIA.GeoGenesis.Thermodynamics;

namespace GAIA.GeoGenesis;

/// <summary>
/// Public, in-process facade for the GeoGenesis thermodynamic / reactive-transport simulator,
/// mirroring how <c>DelveModule</c> and <c>QuakeModule</c> expose their engines to PRISM.
/// GeoGenesis is a referenced class library (DLL) with no dependency on the Geoscientist's
/// Toolkit; PRISM (GUI + future CLI) drives every capability through this single entry point.
///
/// Capabilities surfaced here:
///   • the built-in materials / minerals / chemicals database (<see cref="Library"/>);
///   • aqueous speciation and Gibbs-energy-minimisation equilibrium;
///   • mineral saturation indices (the basis for calcite/scaling assessment and mineral formation);
///   • kinetic dissolution/precipitation, 1-D/3-D reactive transport, and multiphase reactive
///     transport (the path intended for coupling with ReservoirFlux multi-well systems and aquifer
///     contaminant transport on mosaic grids).
/// </summary>
public sealed class GeoGenesisModule
{
    /// <summary>Semantic version of the GeoGenesis engine as shipped in this Prism build.</summary>
    public static string Version => "1.0.0";

    /// <summary>
    /// The thermodynamic compound database (minerals, salts, aqueous species, gases) plus the
    /// periodic-table element data. This is the singleton the solvers read from; the PRISM
    /// materials browser/manager edits the same instance.
    /// </summary>
    public static CompoundLibrary Library => CompoundLibrary.Instance;

    /// <summary>Total number of compounds currently loaded in the library.</summary>
    public int CompoundCount => Library.Compounds.Count;

    /// <summary>Total number of periodic-table elements loaded in the library.</summary>
    public int ElementCount => Library.Elements.Count;

    // --- Solver factories ---------------------------------------------------------------------

    /// <summary>Create an equilibrium / speciation solver (Gibbs energy minimisation).</summary>
    public ThermodynamicSolver CreateThermodynamicSolver() => new();

    /// <summary>Create a kinetic dissolution/precipitation solver.</summary>
    public KineticsSolver CreateKineticsSolver() => new();

    /// <summary>Create a single-phase reactive transport solver (aquifer contaminant transport).</summary>
    public ReactiveTransportSolver CreateReactiveTransportSolver() => new();

    /// <summary>Create a multiphase reactive transport solver (e.g. water–CO₂ for geothermal/CCS).</summary>
    public MultiphaseReactiveTransportSolver CreateMultiphaseReactiveTransportSolver(
        MultiphaseFlowSolver.EOSType eos = MultiphaseFlowSolver.EOSType.WaterCO2) => new(eos);

    // --- High-level convenience operations ----------------------------------------------------

    /// <summary>
    /// Solve aqueous speciation for the supplied state (populates activities, ionic strength,
    /// and aqueous properties), returning the same state instance for chaining.
    /// </summary>
    public ThermodynamicState Speciate(ThermodynamicState state)
        => CreateThermodynamicSolver().SolveSpeciation(state);

    /// <summary>Solve full multiphase chemical equilibrium for the supplied state.</summary>
    public ThermodynamicState SolveEquilibrium(ThermodynamicState state)
        => CreateThermodynamicSolver().SolveEquilibrium(state);

    /// <summary>
    /// Compute mineral saturation indices SI = log₁₀(IAP) − log₁₀(Ksp) for the supplied state.
    /// SI &gt; 0 ⇒ supersaturated (scaling / precipitation tendency); SI &lt; 0 ⇒ undersaturated
    /// (dissolution). This is the core quantity for calcite-scaling and mineral-formation studies.
    /// The state must already be speciated (call <see cref="Speciate"/> first).
    /// </summary>
    public IReadOnlyDictionary<string, double> SaturationIndices(ThermodynamicState state)
        => CreateThermodynamicSolver().CalculateSaturationIndices(state);

    /// <summary>
    /// Convenience: speciate the state, then return its saturation indices in one call.
    /// </summary>
    public IReadOnlyDictionary<string, double> AssessScaling(ThermodynamicState state)
    {
        var solver = CreateThermodynamicSolver();
        solver.SolveSpeciation(state);
        return solver.CalculateSaturationIndices(state);
    }
}
