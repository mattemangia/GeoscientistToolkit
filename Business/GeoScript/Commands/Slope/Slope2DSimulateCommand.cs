using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE2D_SIMULATE command.
    /// Runs 2D slope stability simulation.
    /// Usage: SLOPE2D_SIMULATE time=10.0 timestep=0.001 mode=dynamic
    /// </summary>
    public class Slope2DSimulateCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE2D_SIMULATE";

        public string HelpText => "Runs 2D slope stability simulation with rigid body dynamics";

        public string Usage => @"SLOPE2D_SIMULATE [time=<seconds>] [timestep=<seconds>] [mode=<dynamic|quasistatic|static>] [damping=<float>]
    time: Total simulation time in seconds (default: 10.0)
    timestep: Time step in seconds (default: 0.001)
    mode: Simulation mode - dynamic, quasistatic, or static (default: dynamic)
    damping: Local damping factor 0-1 (default: 0.05)

Example:
    slope2d |> SLOPE2D_SIMULATE time=20.0 timestep=0.0005 mode=quasistatic";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStability2DDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability2D dataset");

            if (slopeDataset.Blocks == null || slopeDataset.Blocks.Count == 0)
                throw new InvalidOperationException("No blocks in dataset. Run SLOPE2D_GENERATE_BLOCKS first.");

            // Parse parameters
            float time = 10.0f;
            float timestep = 0.001f;
            SimulationMode mode = SimulationMode.Dynamic;
            float damping = 0.05f;

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
                        case "damping":
                            damping = float.Parse(param.Value);
                            break;
                    }
                }
            }

            // Update parameters
            slopeDataset.Parameters.TotalTime = time;
            slopeDataset.Parameters.TimeStep = timestep;
            slopeDataset.Parameters.SimulationMode = mode;
            slopeDataset.Parameters.LocalDamping = damping;
            slopeDataset.Parameters.RecordTimeHistory = true;

            Console.WriteLine($"Running 2D slope stability simulation...");
            Console.WriteLine($"  Blocks: {slopeDataset.Blocks.Count}");
            Console.WriteLine($"  Time: {time}s, Timestep: {timestep}s");
            Console.WriteLine($"  Mode: {mode}, Damping: {damping}");

            // Run simulation
            var results = await Task.Run(() =>
            {
                return SlopeStability2DIntegration.RunSimulation(
                    slopeDataset,
                    progress => Console.Write($"\r  Progress: {progress * 100:F0}%"));
            });

            Console.WriteLine();
            Console.WriteLine($"Simulation completed!");
            Console.WriteLine($"  Total iterations: {results.TotalIterations}");
            Console.WriteLine($"  Final time: {results.TotalSimulationTime:F3}s");
            Console.WriteLine($"  Max displacement: {results.MaxDisplacement:F4}m");
            Console.WriteLine($"  Converged: {results.Converged}");

            return await Task.FromResult<Dataset>(slopeDataset);
        }
    }
}
