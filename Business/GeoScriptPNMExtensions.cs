// GeoscientistToolkit/Business/GeoScriptPNMExtensions.cs
//
// GeoScript extensions for PNM reactive transport simulations
// Provides commands to run reactive transport through pore networks

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business;

/// <summary>
/// RUN_PNM_REACTIVE_TRANSPORT: Runs reactive transport simulation through a PNM
/// Usage: RUN_PNM_REACTIVE_TRANSPORT [total_time] [time_step] [temp] [inlet_P] [outlet_P]
/// </summary>
public class RunPNMReactiveTransportCommand : IGeoScriptCommand
{
    public string Name => "RUN_PNM_REACTIVE_TRANSPORT";
    public string HelpText => "Runs reactive transport simulation through a pore network model";
    public string Usage => "RUN_PNM_REACTIVE_TRANSPORT [total_time_s] [time_step_s] [inlet_temp_K] [inlet_pressure_Pa] [outlet_pressure_Pa]";

    public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset dataset)
            throw new ArgumentException("RUN_PNM_REACTIVE_TRANSPORT requires a PNM dataset as input");

        var cmd = (CommandNode)node;

        // Parse: RUN_PNM_REACTIVE_TRANSPORT total_time time_step temp inlet_P outlet_P
        var match = Regex.Match(cmd.FullText,
            @"RUN_PNM_REACTIVE_TRANSPORT\s+([\d\.]+)\s+([\d\.]+)\s+([\d\.]+)\s+([\d\.]+)\s+([\d\.]+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            throw new ArgumentException("RUN_PNM_REACTIVE_TRANSPORT requires 5 arguments: total_time, time_step, inlet_temp, inlet_pressure, outlet_pressure");

        double totalTime = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        double timeStep = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        double inletTemp = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        double inletPressure = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        double outletPressure = double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture);

        var options = new PNMReactiveTransportOptions
        {
            TotalTime = totalTime,
            TimeStep = timeStep,
            OutputInterval = Math.Max(1.0, totalTime / 100.0), // 100 output points max
            InletTemperature = (float)inletTemp,
            OutletTemperature = (float)inletTemp,
            InletPressure = (float)inletPressure,
            OutletPressure = (float)outletPressure,
            FlowAxis = FlowAxis.Z,
            EnableReactions = true,
            UpdateGeometry = true
        };

        Logger.Log($"[RUN_PNM_REACTIVE_TRANSPORT] Starting simulation: {totalTime}s, dt={timeStep}s");
        Logger.Log($"[RUN_PNM_REACTIVE_TRANSPORT] Temperature: {inletTemp}K, Pressure: {inletPressure}->{outletPressure} Pa");

        var progress = new Progress<(float, string)>(p =>
        {
            Logger.Log($"[RUN_PNM_REACTIVE_TRANSPORT] {p.Item1:P0}: {p.Item2}");
        });

        // Run simulation in background
        var results = await Task.Run(() => PNMReactiveTransport.Solve(dataset, options, progress));

        // Store results in dataset
        dataset.ReactiveTransportResults = results;

        // Store final state
        if (results.TimeSteps.Count > 0)
        {
            dataset.ReactiveTransportState = results.TimeSteps[results.TimeSteps.Count - 1];
        }

        Logger.Log($"[RUN_PNM_REACTIVE_TRANSPORT] Complete! Permeability change: {results.PermeabilityChange:P2}");
        Logger.Log($"[RUN_PNM_REACTIVE_TRANSPORT] Initial: {results.InitialPermeability:E3} mD, Final: {results.FinalPermeability:E3} mD");

        // Trigger UI update
        ProjectManager.Instance?.NotifyDatasetDataChanged(dataset);

        return dataset;
    }
}

/// <summary>
/// SET_PNM_SPECIES: Sets initial species concentrations for reactive transport
/// Usage: SET_PNM_SPECIES [species_name] [inlet_conc] [initial_conc]
/// </summary>
public class SetPNMSpeciesCommand : IGeoScriptCommand
{
    public string Name => "SET_PNM_SPECIES";
    public string HelpText => "Sets species concentrations for PNM reactive transport";
    public string Usage => "SET_PNM_SPECIES [species_name] [inlet_concentration_mol/L] [initial_concentration_mol/L]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset dataset)
            throw new ArgumentException("SET_PNM_SPECIES requires a PNM dataset as input");

        var cmd = (CommandNode)node;

        // Parse: SET_PNM_SPECIES species_name inlet_conc initial_conc
        var match = Regex.Match(cmd.FullText,
            @"SET_PNM_SPECIES\s+(\S+)\s+([\d\.Ee\+\-]+)\s+([\d\.Ee\+\-]+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            throw new ArgumentException("SET_PNM_SPECIES requires 3 arguments: species_name, inlet_concentration, initial_concentration");

        string species = match.Groups[1].Value;
        float inletConc = float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        float initialConc = float.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);

        // Store in dataset metadata (will be used by next RUN_PNM_REACTIVE_TRANSPORT command)
        if (dataset.ReactiveTransportState == null)
        {
            // Initialize if not already present
            dataset.ReactiveTransportState = new PNMReactiveTransportState();
        }

        // Initialize all pores with this species
        foreach (var pore in dataset.Pores)
        {
            if (!dataset.ReactiveTransportState.PoreConcentrations.ContainsKey(pore.ID))
                dataset.ReactiveTransportState.PoreConcentrations[pore.ID] = new Dictionary<string, float>();

            dataset.ReactiveTransportState.PoreConcentrations[pore.ID][species] = initialConc;
        }

        Logger.Log($"[SET_PNM_SPECIES] Set {species}: inlet={inletConc} mol/L, initial={initialConc} mol/L");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// SET_PNM_MINERALS: Sets initial mineral content for reactive transport
/// Usage: SET_PNM_MINERALS [mineral_name] [volume_fraction]
/// </summary>
public class SetPNMMineralsCommand : IGeoScriptCommand
{
    public string Name => "SET_PNM_MINERALS";
    public string HelpText => "Sets initial mineral content for PNM reactive transport";
    public string Usage => "SET_PNM_MINERALS [mineral_name] [volume_fraction_or_um3]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset dataset)
            throw new ArgumentException("SET_PNM_MINERALS requires a PNM dataset as input");

        var cmd = (CommandNode)node;

        // Parse: SET_PNM_MINERALS mineral_name volume
        var match = Regex.Match(cmd.FullText,
            @"SET_PNM_MINERALS\s+(\S+)\s+([\d\.Ee\+\-]+)",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            throw new ArgumentException("SET_PNM_MINERALS requires 2 arguments: mineral_name, volume_fraction");

        string mineral = match.Groups[1].Value;
        float volumeFraction = float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);

        // Initialize state if needed
        if (dataset.ReactiveTransportState == null)
        {
            dataset.ReactiveTransportState = new PNMReactiveTransportState();
        }

        // Set for all pores
        foreach (var pore in dataset.Pores)
        {
            if (!dataset.ReactiveTransportState.PoreMinerals.ContainsKey(pore.ID))
                dataset.ReactiveTransportState.PoreMinerals[pore.ID] = new Dictionary<string, float>();

            // Convert fraction to volume (μm³)
            var mineralVolume = pore.VolumePhysical * volumeFraction;
            dataset.ReactiveTransportState.PoreMinerals[pore.ID][mineral] = mineralVolume;
        }

        Logger.Log($"[SET_PNM_MINERALS] Set {mineral} with volume fraction {volumeFraction:P2}");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// EXPORT_PNM_RESULTS: Exports reactive transport results to CSV
/// Usage: EXPORT_PNM_RESULTS [output_path]
/// </summary>
public class ExportPNMResultsCommand : IGeoScriptCommand
{
    public string Name => "EXPORT_PNM_RESULTS";
    public string HelpText => "Exports PNM reactive transport results to CSV files";
    public string Usage => "EXPORT_PNM_RESULTS [output_directory]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset dataset)
            throw new ArgumentException("EXPORT_PNM_RESULTS requires a PNM dataset as input");

        if (dataset.ReactiveTransportResults == null)
            throw new InvalidOperationException("No reactive transport results found. Run RUN_PNM_REACTIVE_TRANSPORT first.");

        var cmd = (CommandNode)node;

        // Parse: EXPORT_PNM_RESULTS output_path
        var match = Regex.Match(cmd.FullText, @"EXPORT_PNM_RESULTS\s+(.+)", RegexOptions.IgnoreCase);

        if (!match.Success)
            throw new ArgumentException("EXPORT_PNM_RESULTS requires 1 argument: output_directory");

        string outputDir = match.Groups[1].Value.Trim();

        // Create directory if it doesn't exist
        System.IO.Directory.CreateDirectory(outputDir);

        var results = dataset.ReactiveTransportResults;

        // Export summary
        var summaryPath = System.IO.Path.Combine(outputDir, "summary.csv");
        using (var writer = new System.IO.StreamWriter(summaryPath))
        {
            writer.WriteLine("Metric,Value");
            writer.WriteLine($"Initial Permeability (mD),{results.InitialPermeability}");
            writer.WriteLine($"Final Permeability (mD),{results.FinalPermeability}");
            writer.WriteLine($"Permeability Change (%),{results.PermeabilityChange * 100}");
            writer.WriteLine($"Computation Time (s),{results.ComputationTime.TotalSeconds}");
            writer.WriteLine($"Total Iterations,{results.TotalIterations}");
            writer.WriteLine($"Converged,{results.Converged}");
        }

        // Export time series
        var timeSeriesPath = System.IO.Path.Combine(outputDir, "time_series.csv");
        using (var writer = new System.IO.StreamWriter(timeSeriesPath))
        {
            writer.WriteLine("Time (s),Permeability (mD)");
            foreach (var state in results.TimeSteps)
            {
                writer.WriteLine($"{state.CurrentTime},{state.CurrentPermeability}");
            }
        }

        // Export final state
        if (results.TimeSteps.Count > 0)
        {
            var finalState = results.TimeSteps[results.TimeSteps.Count - 1];

            // Export pore data
            var poresPath = System.IO.Path.Combine(outputDir, "final_pores.csv");
            using (var writer = new System.IO.StreamWriter(poresPath))
            {
                // Build header
                var header = new List<string> { "PoreID", "X", "Y", "Z", "Radius", "Volume", "Pressure", "Temperature" };

                // Add species columns
                var species = new HashSet<string>();
                foreach (var concs in finalState.PoreConcentrations.Values)
                    foreach (var sp in concs.Keys)
                        species.Add(sp);

                header.AddRange(species.OrderBy(s => s));

                // Add mineral columns
                var minerals = new HashSet<string>();
                foreach (var mins in finalState.PoreMinerals.Values)
                    foreach (var min in mins.Keys)
                        minerals.Add(min);

                header.AddRange(minerals.OrderBy(m => m).Select(m => $"{m}_Volume"));

                writer.WriteLine(string.Join(",", header));

                // Write data
                foreach (var pore in dataset.Pores)
                {
                    var row = new List<string>
                    {
                        pore.ID.ToString(),
                        pore.Position.X.ToString("F3"),
                        pore.Position.Y.ToString("F3"),
                        pore.Position.Z.ToString("F3"),
                        finalState.PoreRadii.GetValueOrDefault(pore.ID, pore.Radius).ToString("F3"),
                        finalState.PoreVolumes.GetValueOrDefault(pore.ID, pore.VolumePhysical).ToString("F3"),
                        finalState.PorePressures.GetValueOrDefault(pore.ID, 0).ToString("E3"),
                        finalState.PoreTemperatures.GetValueOrDefault(pore.ID, 0).ToString("F2")
                    };

                    // Add species concentrations
                    foreach (var sp in species.OrderBy(s => s))
                    {
                        var conc = 0f;
                        if (finalState.PoreConcentrations.TryGetValue(pore.ID, out var concs))
                            conc = concs.GetValueOrDefault(sp, 0);
                        row.Add(conc.ToString("E3"));
                    }

                    // Add mineral volumes
                    foreach (var min in minerals.OrderBy(m => m))
                    {
                        var vol = 0f;
                        if (finalState.PoreMinerals.TryGetValue(pore.ID, out var mins))
                            vol = mins.GetValueOrDefault(min, 0);
                        row.Add(vol.ToString("E3"));
                    }

                    writer.WriteLine(string.Join(",", row));
                }
            }
        }

        Logger.Log($"[EXPORT_PNM_RESULTS] Exported results to {outputDir}");
        Logger.Log($"  - summary.csv");
        Logger.Log($"  - time_series.csv");
        Logger.Log($"  - final_pores.csv");

        return Task.FromResult<Dataset>(dataset);
    }
}

/// <summary>
/// Registration class for PNM GeoScript extensions
/// </summary>
public static class PNMGeoScriptExtensions
{
    public static void Register(GeoScriptInterpreter interpreter)
    {
        interpreter.RegisterCommand(new RunPNMReactiveTransportCommand());
        interpreter.RegisterCommand(new SetPNMSpeciesCommand());
        interpreter.RegisterCommand(new SetPNMMineralsCommand());
        interpreter.RegisterCommand(new ExportPNMResultsCommand());

        Logger.Log("[PNMGeoScriptExtensions] Registered PNM reactive transport commands");
    }
}
