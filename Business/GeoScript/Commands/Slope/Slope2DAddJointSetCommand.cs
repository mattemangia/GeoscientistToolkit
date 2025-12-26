using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE2D_ADD_JOINT_SET command.
    /// Adds a joint set to a 2D slope stability dataset.
    /// Usage: SLOPE2D_ADD_JOINT_SET name="Bedding" dip=15 dip_dir=90 spacing=2.0 friction=30
    /// </summary>
    public class Slope2DAddJointSetCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE2D_ADD_JOINT_SET";

        public string HelpText => "Adds a joint set to a 2D slope stability dataset";

        public string Usage => @"SLOPE2D_ADD_JOINT_SET name=<string> dip=<degrees> dip_dir=<degrees> spacing=<meters> [friction=<degrees>] [cohesion=<Pa>] [tensile=<Pa>]
    name: Joint set name
    dip: Dip angle in degrees (0=horizontal, 90=vertical)
    dip_dir: Dip direction in degrees (0=North, 90=East, 180=South, 270=West)
    spacing: Joint spacing in meters
    friction: Friction angle in degrees (default: 30)
    cohesion: Cohesion in Pa (default: 10000)
    tensile: Tensile strength in Pa (default: 5000)

Example:
    slope2d |> SLOPE2D_ADD_JOINT_SET name=""Bedding"" dip=10 dip_dir=90 spacing=2.0 friction=30";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStability2DDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability2D dataset");

            // Parse required parameters
            string jointName = null;
            float? dip = null;
            float? dipDir = null;
            float? spacing = null;

            // Optional parameters with defaults
            float friction = 30f;
            float cohesion = 10000f;
            float tensile = 5000f;
            float normalStiffness = 1e7f;
            float shearStiffness = 1e6f;

            if (node is CommandNode cmdNode)
            {
                foreach (var param in cmdNode.Parameters)
                {
                    switch (param.Key.ToLower())
                    {
                        case "name":
                            jointName = param.Value.Trim('"');
                            break;
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
                        case "tensile":
                            tensile = float.Parse(param.Value);
                            break;
                    }
                }
            }

            // Validate required parameters
            if (string.IsNullOrEmpty(jointName))
                throw new ArgumentException("'name' parameter is required");
            if (!dip.HasValue)
                throw new ArgumentException("'dip' parameter is required");
            if (!dipDir.HasValue)
                throw new ArgumentException("'dip_dir' parameter is required");
            if (!spacing.HasValue)
                throw new ArgumentException("'spacing' parameter is required");

            // Create joint set
            var jointSet = new JointSet
            {
                Id = slopeDataset.JointSets.Count,
                Name = jointName,
                Dip = dip.Value,
                DipDirection = dipDir.Value,
                Spacing = spacing.Value,
                FrictionAngle = friction,
                Cohesion = cohesion,
                TensileStrength = tensile,
                NormalStiffness = normalStiffness,
                ShearStiffness = shearStiffness
            };

            slopeDataset.JointSets.Add(jointSet);

            Console.WriteLine($"Added joint set: {jointName}");
            Console.WriteLine($"  Dip: {dip:F1}°, Dip direction: {dipDir:F1}°");
            Console.WriteLine($"  Spacing: {spacing:F2}m, Friction: {friction:F1}°");

            return await Task.FromResult<Dataset>(slopeDataset);
        }
    }
}
