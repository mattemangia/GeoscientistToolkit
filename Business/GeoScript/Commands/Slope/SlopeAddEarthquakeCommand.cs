using System;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_ADD_EARTHQUAKE command.
    /// Adds earthquake loading to slope stability analysis.
    /// Usage: SLOPE_ADD_EARTHQUAKE magnitude=5.5 epicenter_x=0 epicenter_y=0 epicenter_z=0
    /// </summary>
    public class SlopeAddEarthquakeCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_ADD_EARTHQUAKE";

        public string HelpText => "Adds earthquake loading to trigger slope failure";

        public string Usage => @"SLOPE_ADD_EARTHQUAKE magnitude=<Mw> [epicenter_x=<m>] [epicenter_y=<m>] [epicenter_z=<m>] [start_time=<s>]
    magnitude: Earthquake moment magnitude (Mw)
    epicenter_x: Epicenter X coordinate in meters (default: 0)
    epicenter_y: Epicenter Y coordinate in meters (default: 0)
    epicenter_z: Epicenter Z coordinate in meters (default: 0)
    start_time: Time to trigger earthquake in seconds (default: 1.0)

Example:
    slope_dataset |> SLOPE_ADD_EARTHQUAKE magnitude=6.0 epicenter_x=100 epicenter_y=50 start_time=2.0";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            // Parse parameters
            float magnitude = 5.0f;
            float x = 0.0f;
            float y = 0.0f;
            float z = 0.0f;
            float startTime = 1.0f;

            if (node is CommandNode cmdNode)
            {
                if (cmdNode.Parameters.TryGetValue("magnitude", out string magnitudeValue))
                {
                    magnitude = float.Parse(magnitudeValue);
                }

                if (cmdNode.Parameters.TryGetValue("epicenter_x", out string xValue)
                    || cmdNode.Parameters.TryGetValue("x", out xValue))
                {
                    x = float.Parse(xValue);
                }

                if (cmdNode.Parameters.TryGetValue("epicenter_y", out string yValue)
                    || cmdNode.Parameters.TryGetValue("y", out yValue))
                {
                    y = float.Parse(yValue);
                }

                if (cmdNode.Parameters.TryGetValue("epicenter_z", out string zValue)
                    || cmdNode.Parameters.TryGetValue("z", out zValue))
                {
                    z = float.Parse(zValue);
                }

                if (cmdNode.Parameters.TryGetValue("start_time", out string startValue))
                {
                    startTime = float.Parse(startValue);
                }
            }

            var earthquake = EarthquakeLoad.CreatePreset(magnitude, new Vector3(x, y, z));
            earthquake.StartTime = startTime;

            slopeDataset.Parameters.EarthquakeLoads.Add(earthquake);
            slopeDataset.Parameters.EnableEarthquakeLoading = true;

            Console.WriteLine($"Added M{magnitude:F1} earthquake at ({x}, {y}, {z}), start time: {startTime}s");
            Console.WriteLine($"PGA: {earthquake.PeakGroundAcceleration:F2} m/s²");

            return await Task.FromResult<Dataset>(slopeDataset);
        }
    }
}
