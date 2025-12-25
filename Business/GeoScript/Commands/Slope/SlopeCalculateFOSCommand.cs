using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_CALCULATE_FOS command.
    /// Calculates factor of safety using strength reduction method.
    /// Usage: SLOPE_CALCULATE_FOS tolerance=0.01 max_iterations=20
    /// </summary>
    public class SlopeCalculateFOSCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_CALCULATE_FOS";

        public string HelpText => "Calculates factor of safety using strength reduction method (SRM)";

        public string Usage => @"SLOPE_CALCULATE_FOS [tolerance=<value>] [min_fos=<value>] [max_fos=<value>] [max_iterations=<num>]
    tolerance: Convergence tolerance (default: 0.01)
    min_fos: Minimum FOS to test (default: 0.5)
    max_fos: Maximum FOS to test (default: 5.0)
    max_iterations: Maximum iterations for binary search (default: 20)

Example:
    slope_dataset |> SLOPE_CALCULATE_FOS tolerance=0.01 max_iterations=20";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.CurrentDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            float tolerance = 0.01f;
            float minFOS = 0.5f;
            float maxFOS = 5.0f;
            int maxIterations = 20;

            if (node is CommandNode cmdNode)
            {
                if (cmdNode.Parameters.TryGetValue("tolerance", out string tolValue))
                    tolerance = float.Parse(tolValue);

                if (cmdNode.Parameters.TryGetValue("min_fos", out string minValue))
                    minFOS = float.Parse(minValue);

                if (cmdNode.Parameters.TryGetValue("max_fos", out string maxValue))
                    maxFOS = float.Parse(maxValue);

                if (cmdNode.Parameters.TryGetValue("max_iterations", out string iterValue))
                    maxIterations = int.Parse(iterValue);
            }

            Console.WriteLine("Calculating factor of safety using Strength Reduction Method...");

            var calculator = new SafetyFactorCalculator(slopeDataset);

            var fosResult = await Task.Run(() =>
            {
                return calculator.CalculateFactorOfSafety(
                    tolerance,
                    minFOS,
                    maxFOS,
                    maxIterations,
                    (progress, status) => Console.WriteLine($"  {status}"));
            });

            Console.WriteLine($"\nFactor of Safety Results:");
            Console.WriteLine($"  FOS = {fosResult.FactorOfSafety:F3}");
            Console.WriteLine($"  Status: {fosResult.GetInterpretation()}");
            Console.WriteLine($"  Converged: {fosResult.Converged}");
            Console.WriteLine($"  Iterations: {fosResult.Iterations}");
            Console.WriteLine($"  Computation time: {fosResult.ComputationTime.TotalSeconds:F2} s");

            return slopeDataset;
        }
    }
}
