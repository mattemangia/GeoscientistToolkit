using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_SET_ANGLE command.
    /// Sets the slope angle to tilt gravity and trigger natural failure.
    /// Usage: SLOPE_SET_ANGLE angle=30
    /// </summary>
    public class SlopeSetAngleCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_SET_ANGLE";

        public string HelpText => "Sets slope angle to tilt gravity for natural instability analysis";

        public string Usage => @"SLOPE_SET_ANGLE angle=<degrees>
    angle: Slope angle in degrees (0-90). Tilts gravity vector to trigger natural failure.

Example:
    slope_dataset |> SLOPE_SET_ANGLE angle=30

Note: A slope with angle > friction angle will naturally fail without earthquake trigger.";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.CurrentDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            float angle = 0.0f;

            if (node is CommandNode cmdNode)
            {
                if (cmdNode.Parameters.TryGetValue("angle", out string angleValue))
                {
                    angle = float.Parse(angleValue);
                }
            }

            if (angle < 0 || angle > 90)
                throw new ArgumentException("Slope angle must be between 0 and 90 degrees");

            slopeDataset.Parameters.SlopeAngle = angle;
            slopeDataset.Parameters.UseCustomGravityDirection = false;
            slopeDataset.Parameters.UpdateGravityFromSlopeAngle();

            Console.WriteLine($"Slope angle set to {angle}°");
            Console.WriteLine($"Gravity vector: ({slopeDataset.Parameters.Gravity.X:F2}, " +
                            $"{slopeDataset.Parameters.Gravity.Y:F2}, " +
                            $"{slopeDataset.Parameters.Gravity.Z:F2}) m/s²");

            return await Task.FromResult<Dataset>(slopeDataset);
        }
    }
}
