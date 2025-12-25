using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_SET_WATER command.
    /// Sets water table elevation for pore pressure calculations.
    /// Usage: SLOPE_SET_WATER elevation=10.0 density=1000
    /// </summary>
    public class SlopeSetWaterCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_SET_WATER";

        public string HelpText => "Sets water table elevation for pore pressure calculation";

        public string Usage => @"SLOPE_SET_WATER elevation=<meters> [density=<kg/m3>]
    elevation: Water table elevation in meters
    density: Water density in kg/m³ (default: 1000)

Example:
    slope_dataset |> SLOPE_SET_WATER elevation=15.0 density=1000";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.CurrentDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            float elevation = 0.0f;
            float density = 1000.0f;

            if (node is CommandNode cmdNode)
            {
                if (cmdNode.Parameters.TryGetValue("elevation", out string elevValue))
                    elevation = float.Parse(elevValue);

                if (cmdNode.Parameters.TryGetValue("density", out string densValue))
                    density = float.Parse(densValue);
            }

            // Enable fluid pressure
            slopeDataset.Parameters.IncludeFluidPressure = true;
            slopeDataset.Parameters.WaterTableZ = elevation;
            slopeDataset.Parameters.WaterDensity = density;

            Console.WriteLine($"Water table set to {elevation} m elevation (density: {density} kg/m³)");
            Console.WriteLine("Pore pressure calculations enabled");

            return await Task.FromResult(slopeDataset);
        }
    }
}
