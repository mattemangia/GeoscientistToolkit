// GeoscientistToolkit/Analysis/PNM/DualPNMSimulations.cs

using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Pnm;

/// <summary>
/// Simulation extensions for Dual PNM datasets.
/// Provides methods to run standard PNM simulations considering both macro and micro scales.
/// </summary>
public static class DualPNMSimulations
{
    /// <summary>
    /// Calculate absolute permeability for dual PNM considering both scales.
    /// Updates the combined permeability based on coupling mode.
    /// </summary>
    public static void CalculateDualPermeability(DualPNMDataset dataset, PNMPermeabilityMethod method,
        float pressureDiffPa = 100000.0f, float viscosityPas = 0.001f)
    {
        if (dataset == null)
        {
            Logger.LogError("Cannot calculate permeability: dataset is null");
            return;
        }

        Logger.Log($"Calculating dual PNM permeability using {method} method...");

        // Calculate macro-scale permeability (uses existing PNM as base)
        var options = new PermeabilityOptions
        {
            Dataset = dataset,
            InletPressure = pressureDiffPa,
            OutletPressure = 0,
            FluidViscosity = viscosityPas,
            CalculateDarcy = method == PNMPermeabilityMethod.Darcy,
            CalculateNavierStokes = method == PNMPermeabilityMethod.NavierStokes || method == PNMPermeabilityMethod.LatticeBoltzmann,
            CalculateLatticeBoltzmann = method == PNMPermeabilityMethod.LatticeBoltzmann
        };

        if (method == PNMPermeabilityMethod.LatticeBoltzmann)
        {
            Logger.LogWarning("Lattice Boltzmann not yet implemented for dual PNM. Using Navier-Stokes instead.");
        }

        AbsolutePermeability.Calculate(options);
        var results = AbsolutePermeability.GetLastResults();

        float macroPermeability = method switch
        {
            PNMPermeabilityMethod.Darcy => results.DarcyCorrected,
            PNMPermeabilityMethod.NavierStokes => results.NavierStokesCorrected,
            PNMPermeabilityMethod.LatticeBoltzmann => results.NavierStokesCorrected,
            _ => 0
        };

        dataset.DarcyPermeability = macroPermeability;
        dataset.Coupling.EffectiveMacroPermeability = macroPermeability;

        // Calculate micro-scale permeabilities
        if (dataset.MicroNetworks.Count > 0)
        {
            Logger.Log("Calculating micro-scale permeabilities...");

            foreach (var microNet in dataset.MicroNetworks)
            {
                // Simplified micro-permeability calculation based on pore structure
                // In full implementation, would run full flow simulation on micro-network
                microNet.MicroPermeability = EstimateMicroPermeability(microNet);
            }

            dataset.Coupling.EffectiveMicroPermeability =
                dataset.MicroNetworks.Average(mn => mn.MicroPermeability);
        }

        // Recalculate combined properties
        dataset.CalculateCombinedProperties();

        Logger.Log($"Dual PNM permeability calculated:");
        Logger.Log($"  Macro: {dataset.Coupling.EffectiveMacroPermeability:F3} mD");
        Logger.Log($"  Micro: {dataset.Coupling.EffectiveMicroPermeability:F3} mD");
        Logger.Log($"  Combined: {dataset.Coupling.CombinedPermeability:F3} mD");
    }

    /// <summary>
    /// Estimate micro-scale permeability from micro-pore network structure.
    /// Uses simplified Kozeny-Carman approach.
    /// </summary>
    private static float EstimateMicroPermeability(MicroPoreNetwork microNet)
    {
        if (microNet.MicroPores.Count == 0)
            return 0.0f;

        // Calculate average pore radius
        float avgPoreRadius = microNet.MicroPores.Average(p => p.Radius);

        // Kozeny-Carman estimate: k ≈ φ³ * r² / (5 * (1-φ)²)
        // where φ is porosity and r is characteristic pore size
        float porosity = microNet.MicroPorosity;

        if (porosity <= 0 || porosity >= 1)
        {
            // Estimate porosity from pore volumes if not set
            float totalVol = microNet.MicroVolume;
            float poreVol = microNet.MicroPores.Sum(p => p.VolumePhysical);
            porosity = totalVol > 0 ? poreVol / totalVol : 0.1f;
            microNet.MicroPorosity = porosity;
        }

        // Kozeny-Carman formula in µm² then convert to mD
        float k_um2 = (porosity * porosity * porosity * avgPoreRadius * avgPoreRadius) /
                      (5.0f * (1 - porosity) * (1 - porosity));

        // Convert µm² to mD: 1 mD = 0.9869233 µm²
        float k_mD = k_um2 / 0.9869233f;

        return k_mD;
    }

    /// <summary>
    /// Run reactive transport simulation on dual PNM.
    /// Simulates transport in macro-network with micro-porosity as embedded storage.
    /// </summary>
    public static void RunDualReactiveTransport(DualPNMDataset dataset, PNMReactiveTransportOptions options,
        IProgress<(float progress, string message)> progressCallback = null)
    {
        if (dataset == null)
        {
            Logger.LogError("Cannot run reactive transport: dataset is null");
            return;
        }

        Logger.Log("Running dual PNM reactive transport simulation...");
        Logger.Log($"  Coupling mode: {dataset.Coupling.CouplingMode}");

        // For series or mass transfer modes, need to account for micro-porosity storage
        if (dataset.Coupling.CouplingMode == DualPorosityCouplingMode.Series ||
            dataset.Coupling.CouplingMode == DualPorosityCouplingMode.MassTransfer)
        {
            // Modify simulation options to account for dual porosity
            var modifiedOptions = AdjustOptionsForDualPorosity(options, dataset);

            // Run simulation on macro-network with modified parameters
            PNMReactiveTransport.Solve(dataset, modifiedOptions, progressCallback);
        }
        else
        {
            // Parallel mode: run simulation on macro-network directly
            PNMReactiveTransport.Solve(dataset, options, progressCallback);
        }

        Logger.Log("Dual PNM reactive transport simulation complete");
    }

    /// <summary>
    /// Adjust simulation options to account for micro-porosity storage and exchange.
    /// </summary>
    private static PNMReactiveTransportOptions AdjustOptionsForDualPorosity(
        PNMReactiveTransportOptions baseOptions, DualPNMDataset dataset)
    {
        var options = new PNMReactiveTransportOptions
        {
            TotalTime = baseOptions.TotalTime,
            TimeStep = baseOptions.TimeStep,
            OutputInterval = baseOptions.OutputInterval,
            InletPressure = baseOptions.InletPressure,
            OutletPressure = baseOptions.OutletPressure,
            FluidViscosity = baseOptions.FluidViscosity,
            FluidDensity = baseOptions.FluidDensity,
            InletTemperature = baseOptions.InletTemperature,
            ThermalConductivity = baseOptions.ThermalConductivity,
            SpecificHeat = baseOptions.SpecificHeat,
            MolecularDiffusivity = baseOptions.MolecularDiffusivity,
            Dispersivity = baseOptions.Dispersivity,
            EnableReactions = baseOptions.EnableReactions,
            ReactionMinerals = baseOptions.ReactionMinerals,
            MinThroatRadius = baseOptions.MinThroatRadius,
            InitialConcentrations = new Dictionary<string, float>(baseOptions.InitialConcentrations),
            InletConcentrations = new Dictionary<string, float>(baseOptions.InletConcentrations)
        };

        // Adjust time step to account for mass transfer between scales
        if (dataset.Coupling.CouplingMode == DualPorosityCouplingMode.MassTransfer)
        {
            // Reduce time step for numerical stability with mass transfer
            options.TimeStep = baseOptions.TimeStep * 0.5f;
            Logger.Log($"  Adjusted time step for mass transfer: {options.TimeStep:F3} s");
        }

        // Adjust dispersivity to account for dual porosity
        float microPorosityFraction = dataset.Coupling.TotalMicroPorosity;
        if (microPorosityFraction > 0)
        {
            options.Dispersivity *= (1.0f + microPorosityFraction);
            Logger.Log($"  Adjusted dispersivity for dual porosity: {options.Dispersivity:E3} m");
        }

        return options;
    }

    /// <summary>
    /// Calculate molecular diffusivity for dual PNM considering both scales.
    /// </summary>
    public static DiffusivityResults CalculateDualDiffusivity(DualPNMDataset dataset,
        DiffusivityOptions options, Action<string> progressCallback = null)
    {
        if (dataset == null)
        {
            Logger.LogError("Cannot calculate diffusivity: dataset is null");
            return null;
        }

        Logger.Log("Calculating dual PNM molecular diffusivity...");

        // Calculate macro-scale diffusivity
        var diffOptions = new DiffusivityOptions { Dataset = dataset };
        var macroResults = MolecularDiffusivity.Calculate(diffOptions, progressCallback);

        if (macroResults == null)
        {
            Logger.LogError("Failed to calculate macro-scale diffusivity");
            return null;
        }

        // For dual PNM, effective diffusivity is reduced by micro-porosity storage
        if (dataset.MicroNetworks.Count > 0)
        {
            float microPorosityFraction = dataset.Coupling.TotalMicroPorosity;

            // Dual porosity correction: D_eff_dual = D_eff_macro * (1 - α) + D_eff_micro * α
            // For now, assume micro-scale diffusivity is lower due to higher tortuosity
            float microDiffusivityFactor = 0.1f; // Micro-pores have ~10x higher tortuosity

            float dualEffectiveDiffusivity = macroResults.EffectiveDiffusivity * (1 - microPorosityFraction) +
                                            (options.BulkDiffusivity * microDiffusivityFactor * microPorosityFraction);

            float dualFormationFactor = options.BulkDiffusivity / dualEffectiveDiffusivity;
            float dualTortuosity = dualFormationFactor; // Simplified

            Logger.Log("Dual porosity diffusivity correction applied:");
            Logger.Log($"  Macro effective diffusivity: {macroResults.EffectiveDiffusivity:E3} m²/s");
            Logger.Log($"  Dual effective diffusivity: {dualEffectiveDiffusivity:E3} m²/s");
            Logger.Log($"  Dual formation factor: {dualFormationFactor:F3}");

            dataset.EffectiveDiffusivity = dualEffectiveDiffusivity;
            dataset.FormationFactor = dualFormationFactor;
            dataset.TransportTortuosity = dualTortuosity;

            return new DiffusivityResults
            {
                EffectiveDiffusivity = dualEffectiveDiffusivity,
                FormationFactor = dualFormationFactor,
                GeometricTortuosity = dualTortuosity
            };
        }

        // No micro-networks, return macro results
        return macroResults;
    }
}

/// <summary>
/// Permeability calculation method options
/// </summary>
public enum PNMPermeabilityMethod
{
    Darcy,
    NavierStokes,
    LatticeBoltzmann
}
