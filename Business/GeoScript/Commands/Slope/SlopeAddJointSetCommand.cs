using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_ADD_JOINT_SET command.
    /// Adds a joint set to a slope stability dataset.
    /// Usage: SLOPE_ADD_JOINT_SET dip=45 dip_dir=90 spacing=1.0 friction=30 cohesion=0.5
    /// </summary>
    public class SlopeAddJointSetCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_ADD_JOINT_SET";

        public string HelpText => "Adds a joint set to slope stability analysis";

        public string Usage => @"SLOPE_ADD_JOINT_SET dip=<degrees> dip_dir=<degrees> spacing=<meters> friction=<degrees> cohesion=<MPa> [name=<string>]
    dip: Dip angle in degrees (0-90)
    dip_dir: Dip direction/azimuth in degrees (0-360)
    spacing: Joint spacing in meters
    friction: Friction angle in degrees
    cohesion: Cohesion in MPa
    name: Optional name for joint set

Example:
    slope_dataset |> SLOPE_ADD_JOINT_SET dip=45 dip_dir=90 spacing=1.0 friction=30 cohesion=0.5 name=""Main Fracture""";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.CurrentDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            // Parse parameters
            float dip = 45.0f;
            float dipDir = 0.0f;
            float spacing = 1.0f;
            float friction = 30.0f;
            float cohesion = 0.0f;
            string name = $"Joint Set {slopeDataset.JointSets.Count + 1}";

            if (node is CommandNode cmdNode)
            {
                foreach (var param in cmdNode.Parameters)
                {
                    switch (param.Key.ToLower())
                    {
                        case "dip":
                            dip = float.Parse(param.Value);
                            break;
                        case "dip_dir":
                        case "dipdir":
                            dipDir = float.Parse(param.Value);
                            break;
                        case "spacing":
                            spacing = float.Parse(param.Value);
                            break;
                        case "friction":
                            friction = float.Parse(param.Value);
                            break;
                        case "cohesion":
                            cohesion = float.Parse(param.Value);
                            break;
                        case "name":
                            name = param.Value.Trim('"');
                            break;
                    }
                }
            }

            var jointSet = new JointSet
            {
                Id = slopeDataset.JointSets.Count,
                Name = name,
                Dip = dip,
                DipDirection = dipDir,
                Spacing = spacing,
                FrictionAngle = friction,
                Cohesion = cohesion * 1e6f  // Convert MPa to Pa
            };

            slopeDataset.JointSets.Add(jointSet);

            Console.WriteLine($"Added joint set '{name}' (Dip: {dip}°, Dir: {dipDir}°, Spacing: {spacing}m)");

            return await Task.FromResult<Dataset>(slopeDataset);
        }
    }
}
