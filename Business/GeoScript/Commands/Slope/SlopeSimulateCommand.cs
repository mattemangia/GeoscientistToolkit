using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_SIMULATE command.
    /// Runs slope stability simulation.
    /// Usage: SLOPE_SIMULATE time=10.0 timestep=0.001 mode=dynamic
    /// </summary>
    public class SlopeSimulateCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_SIMULATE";

        public string HelpText => "Runs slope stability simulation with DEM";

        public string Usage => @"SLOPE_SIMULATE [time=<seconds>] [timestep=<seconds>] [mode=<dynamic|quasistatic|static>] [threads=<num>]
    time: Total simulation time in seconds (default: 10.0)
    timestep: Time step in seconds (default: 0.001)
    mode: Simulation mode - dynamic, quasistatic, or static (default: dynamic)
    threads: Number of threads, 0=auto (default: 0)

Example:
    slope_dataset |> SLOPE_SIMULATE time=20.0 timestep=0.0005 mode=dynamic";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            // Parse parameters
            float time = 10.0f;
            float timestep = 0.001f;
            SimulationMode mode = SimulationMode.Dynamic;
            int threads = 0;

            if (node is CommandNode cmdNode)
            {
                foreach (var param in cmdNode.Parameters)
                {
                    switch (param.Key.ToLower())
                    {
                        case "time":
                            time = float.Parse(param.Value);
                            break;
                        case "timestep":
                            timestep = float.Parse(param.Value);
                            break;
                        case "mode":
                            mode = Enum.Parse<SimulationMode>(param.Value, true);
                            break;
                        case "threads":
                            threads = int.Parse(param.Value);
                            break;
                    }
                }
            }

            // Update parameters
            slopeDataset.Parameters.TotalTime = time;
            slopeDataset.Parameters.TimeStep = timestep;
            slopeDataset.Parameters.Mode = mode;
            slopeDataset.Parameters.NumThreads = threads;

            Console.WriteLine($"Running slope stability simulation...");
            Console.WriteLine($"Time: {time}s, Timestep: {timestep}s, Mode: {mode}");

            // Run simulation
            var simulator = new SlopeStabilitySimulator(slopeDataset, slopeDataset.Parameters);

            var results = await Task.Run(() =>
            {
                return simulator.RunSimulation(
                    progress => Console.Write($"\rProgress: {progress * 100:F0}%"),
                    status => Console.WriteLine($"  {status}"));
            });

            Console.WriteLine($"\nSimulation completed in {results.ComputationTimeSeconds:F2}s");
            Console.WriteLine($"Max displacement: {results.MaxDisplacement:F4}m");
            Console.WriteLine($"Failed blocks: {results.NumFailedBlocks}/{results.BlockResults.Count}");

            slopeDataset.Results = results;
            slopeDataset.HasResults = true;

            return slopeDataset;
        }
    }
}
