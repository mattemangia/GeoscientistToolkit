using System;
using System.Linq;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_SET_MATERIAL command.
    /// Sets material properties for blocks.
    /// Usage: SLOPE_SET_MATERIAL preset=granite | density=2700 young=50 poisson=0.25 cohesion=10 friction=35
    /// </summary>
    public class SlopeSetMaterialCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_SET_MATERIAL";

        public string HelpText => "Sets material properties for slope blocks";

        public string Usage => @"SLOPE_SET_MATERIAL [preset=<name>] | [density=<kg/m³>] [young=<GPa>] [poisson=<ratio>] [cohesion=<MPa>] [friction=<degrees>]
    preset: Use preset material (granite, limestone, sandstone, shale, clay, sand, etc.)
    OR specify custom properties:
    density: Material density in kg/m³
    young: Young's modulus in GPa
    poisson: Poisson's ratio (0-0.5)
    cohesion: Cohesion in MPa
    friction: Friction angle in degrees

Example:
    slope_dataset |> SLOPE_SET_MATERIAL preset=granite
    slope_dataset |> SLOPE_SET_MATERIAL density=2500 young=40 poisson=0.28 cohesion=5 friction=32";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            SlopeStabilityMaterial material = null;

            if (node is CommandNode cmdNode)
            {
                // Check for preset
                if (cmdNode.Parameters.TryGetValue("preset", out string presetName))
                {
                    if (Enum.TryParse<MaterialPreset>(presetName, true, out var preset))
                    {
                        material = SlopeStabilityMaterial.CreatePreset(preset);
                        material.Id = slopeDataset.Materials.Count;
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown material preset: {presetName}");
                    }
                }
                else
                {
                    // Custom material
                    material = new SlopeStabilityMaterial
                    {
                        Id = slopeDataset.Materials.Count,
                        Name = "Custom Material"
                    };

                    foreach (var param in cmdNode.Parameters)
                    {
                        switch (param.Key.ToLower())
                        {
                            case "density":
                                material.Density = float.Parse(param.Value);
                                break;
                            case "young":
                                material.YoungModulus = float.Parse(param.Value) * 1e9f;  // GPa to Pa
                                material.ConstitutiveModel.YoungModulus = material.YoungModulus;
                                break;
                            case "poisson":
                                material.PoissonRatio = float.Parse(param.Value);
                                material.ConstitutiveModel.PoissonRatio = material.PoissonRatio;
                                break;
                            case "cohesion":
                                material.Cohesion = float.Parse(param.Value) * 1e6f;  // MPa to Pa
                                material.ConstitutiveModel.Cohesion = material.Cohesion;
                                break;
                            case "friction":
                                material.FrictionAngle = float.Parse(param.Value);
                                material.ConstitutiveModel.FrictionAngle = material.FrictionAngle;
                                break;
                            case "name":
                                material.Name = param.Value.Trim('"');
                                break;
                        }
                    }

                    material.ConstitutiveModel.UpdateDerivedProperties();
                }
            }

            if (material != null)
            {
                slopeDataset.Materials.Add(material);

                // Assign to all blocks (or first unassigned)
                int assignedCount = 0;
                foreach (var block in slopeDataset.Blocks)
                {
                    if (block.MaterialId < 0 || block.MaterialId >= slopeDataset.Materials.Count - 1)
                    {
                        block.MaterialId = material.Id;
                        block.Density = material.Density;
                        assignedCount++;
                    }
                }

                Console.WriteLine($"Added material '{material.Name}' (ID: {material.Id})");
                Console.WriteLine($"Assigned to {assignedCount} blocks");
            }

            return await Task.FromResult<Dataset>(slopeDataset);
        }
    }
}
