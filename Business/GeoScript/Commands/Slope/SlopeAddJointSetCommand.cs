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

        public string Usage => @"SLOPE_ADD_JOINT_SET dip=<degrees> dip_dir=<degrees> spacing=<meters> friction=<degrees> cohesion=<MPa> [options...]
    Required:
        dip: Dip angle in degrees (0-90)
        dip_dir: Dip direction/azimuth in degrees (0-360)
        spacing: Joint spacing in meters
        friction: Friction angle in degrees
        cohesion: Cohesion in MPa

    Optional:
        name: Name for joint set
        kn: Normal stiffness in GPa/m (default: 1.0)
        ks: Shear stiffness in GPa/m (default: 0.1)
        tensile: Tensile strength in MPa (default: 0)
        dilation: Dilation angle in degrees (default: 0)
        persistence: Persistence 0-1 (default: 1.0)
        roughness: Joint roughness coefficient JRC (default: 5)

Examples:
    slope_dataset |> SLOPE_ADD_JOINT_SET dip=45 dip_dir=90 spacing=1.0 friction=30 cohesion=0.5 name=""Main Fracture""
    slope_dataset |> SLOPE_ADD_JOINT_SET dip=60 dip_dir=180 spacing=2.0 friction=35 cohesion=1.0 kn=2.0 ks=0.5 tensile=0.1";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            // Parse parameters
            float dip = 45.0f;
            float dipDir = 0.0f;
            float spacing = 1.0f;
            float friction = 30.0f;
            float cohesion = 0.0f;
            float kn = 1.0f;        // GPa/m default
            float ks = 0.1f;        // GPa/m default
            float tensile = 0.0f;   // MPa default
            float dilation = 0.0f;  // degrees default
            float persistence = 1.0f;
            float roughness = 5.0f;
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
                        case "kn":
                            kn = float.Parse(param.Value);
                            break;
                        case "ks":
                            ks = float.Parse(param.Value);
                            break;
                        case "tensile":
                            tensile = float.Parse(param.Value);
                            break;
                        case "dilation":
                            dilation = float.Parse(param.Value);
                            break;
                        case "persistence":
                            persistence = float.Parse(param.Value);
                            break;
                        case "roughness":
                        case "jrc":
                            roughness = float.Parse(param.Value);
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
                Cohesion = cohesion * 1e6f,           // Convert MPa to Pa
                NormalStiffness = kn * 1e9f,          // Convert GPa/m to Pa/m
                ShearStiffness = ks * 1e9f,           // Convert GPa/m to Pa/m
                TensileStrength = tensile * 1e6f,     // Convert MPa to Pa
                Dilation = dilation,
                Persistence = persistence,
                Roughness = roughness
            };

            slopeDataset.JointSets.Add(jointSet);

            Console.WriteLine($"Added joint set '{name}' (Dip: {dip}°, Dir: {dipDir}°, Spacing: {spacing}m)");

            return await Task.FromResult<Dataset>(slopeDataset);
        }
    }
}
