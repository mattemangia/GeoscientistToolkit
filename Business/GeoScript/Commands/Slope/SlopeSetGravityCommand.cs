using System;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_SET_GRAVITY command.
    /// Sets custom gravitational acceleration for slope stability simulation.
    /// Usage: SLOPE_SET_GRAVITY magnitude=1.62 OR SLOPE_SET_GRAVITY x=0 y=0 z=-3.72
    /// </summary>
    public class SlopeSetGravityCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_SET_GRAVITY";

        public string HelpText => "Set custom gravitational acceleration for slope stability simulation";

        public string Usage => @"SLOPE_SET_GRAVITY [magnitude=<m/s²>] [x=<m/s²>] [y=<m/s²>] [z=<m/s²>] [preset=<earth|moon|mars|venus|jupiter>] [custom=<true|false>]
    magnitude: Gravity magnitude (direction is downward based on slope angle)
    x, y, z: Custom gravity vector components (requires custom=true)
    preset: Use planetary preset (earth=9.81, moon=1.62, mars=3.72, venus=8.87, jupiter=24.79)
    custom: If true, use x/y/z directly instead of slope-adjusted direction

Examples:
    SLOPE_SET_GRAVITY magnitude=1.62        # Moon gravity
    SLOPE_SET_GRAVITY preset=mars           # Mars gravity (3.72 m/s²)
    SLOPE_SET_GRAVITY x=0 y=0 z=-3.72 custom=true  # Custom direction";

        public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            float magnitude = 9.81f;
            bool useCustomDirection = false;
            float gx = 0, gy = 0, gz = -9.81f;

            if (node is CommandNode cmdNode)
            {
                foreach (var param in cmdNode.Parameters)
                {
                    switch (param.Key.ToLower())
                    {
                        case "magnitude":
                            magnitude = float.Parse(param.Value);
                            break;
                        case "preset":
                            magnitude = param.Value.ToLower() switch
                            {
                                "earth" => 9.81f,
                                "moon" => 1.62f,
                                "mars" => 3.72f,
                                "venus" => 8.87f,
                                "jupiter" => 24.79f,
                                "saturn" => 10.44f,
                                "mercury" => 3.70f,
                                _ => throw new ArgumentException($"Unknown preset: {param.Value}")
                            };
                            Console.WriteLine($"Using {param.Value} gravity preset: {magnitude} m/s²");
                            break;
                        case "x":
                            gx = float.Parse(param.Value);
                            useCustomDirection = true;
                            break;
                        case "y":
                            gy = float.Parse(param.Value);
                            useCustomDirection = true;
                            break;
                        case "z":
                            gz = float.Parse(param.Value);
                            useCustomDirection = true;
                            break;
                        case "custom":
                            useCustomDirection = bool.Parse(param.Value);
                            break;
                    }
                }
            }

            // Set the gravity
            slopeDataset.Parameters.GravityMagnitude = magnitude;

            if (useCustomDirection)
            {
                slopeDataset.Parameters.UseCustomGravityDirection = true;
                slopeDataset.Parameters.Gravity = new Vector3(gx, gy, gz);
                Console.WriteLine($"Set custom gravity vector: ({gx}, {gy}, {gz}) m/s²");
            }
            else
            {
                slopeDataset.Parameters.SetGravityMagnitude(magnitude);
                Console.WriteLine($"Set gravity magnitude: {magnitude} m/s²");
            }

            return Task.FromResult<Dataset>(slopeDataset);
        }
    }

    /// <summary>
    /// SLOPE2D_SET_GRAVITY command.
    /// Sets custom gravitational acceleration for 2D slope stability simulation.
    /// Usage: SLOPE2D_SET_GRAVITY magnitude=1.62 OR SLOPE2D_SET_GRAVITY x=0 z=-3.72
    /// </summary>
    public class Slope2DSetGravityCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE2D_SET_GRAVITY";

        public string HelpText => "Set custom gravitational acceleration for 2D slope stability simulation";

        public string Usage => @"SLOPE2D_SET_GRAVITY [magnitude=<m/s²>] [x=<m/s²>] [z=<m/s²>] [preset=<earth|moon|mars|venus|jupiter>] [custom=<true|false>]
    magnitude: Gravity magnitude (direction is downward based on slope angle)
    x, z: Custom gravity vector components (z is vertical in 2D, becomes Y)
    preset: Use planetary preset (earth=9.81, moon=1.62, mars=3.72, venus=8.87, jupiter=24.79)
    custom: If true, use x/z directly instead of slope-adjusted direction

Examples:
    SLOPE2D_SET_GRAVITY magnitude=1.62        # Moon gravity
    SLOPE2D_SET_GRAVITY preset=mars           # Mars gravity (3.72 m/s²)
    SLOPE2D_SET_GRAVITY x=0 z=-3.72 custom=true  # Custom direction";

        public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStability2DDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability2D dataset");

            float magnitude = 9.81f;
            bool useCustomDirection = false;
            float gx = 0, gz = -9.81f;

            if (node is CommandNode cmdNode)
            {
                foreach (var param in cmdNode.Parameters)
                {
                    switch (param.Key.ToLower())
                    {
                        case "magnitude":
                            magnitude = float.Parse(param.Value);
                            break;
                        case "preset":
                            magnitude = param.Value.ToLower() switch
                            {
                                "earth" => 9.81f,
                                "moon" => 1.62f,
                                "mars" => 3.72f,
                                "venus" => 8.87f,
                                "jupiter" => 24.79f,
                                "saturn" => 10.44f,
                                "mercury" => 3.70f,
                                _ => throw new ArgumentException($"Unknown preset: {param.Value}")
                            };
                            Console.WriteLine($"Using {param.Value} gravity preset: {magnitude} m/s²");
                            break;
                        case "x":
                            gx = float.Parse(param.Value);
                            useCustomDirection = true;
                            break;
                        case "z":
                            gz = float.Parse(param.Value);
                            useCustomDirection = true;
                            break;
                        case "custom":
                            useCustomDirection = bool.Parse(param.Value);
                            break;
                    }
                }
            }

            // Set the gravity
            slopeDataset.Parameters.GravityMagnitude = magnitude;

            if (useCustomDirection)
            {
                slopeDataset.Parameters.UseCustomGravityDirection = true;
                slopeDataset.Parameters.Gravity = new Vector3(gx, 0, gz);
                Console.WriteLine($"Set custom gravity vector: ({gx}, 0, {gz}) m/s²");
            }
            else
            {
                slopeDataset.Parameters.SetGravityMagnitude(magnitude);
                Console.WriteLine($"Set gravity magnitude: {magnitude} m/s²");
            }

            return Task.FromResult<Dataset>(slopeDataset);
        }
    }
}
