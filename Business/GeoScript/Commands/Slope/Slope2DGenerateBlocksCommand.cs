using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE2D_GENERATE_BLOCKS command.
    /// Generates 2D blocks from geological section and joint sets.
    /// Usage: SLOPE2D_GENERATE_BLOCKS min_area=0.1 max_area=100
    /// </summary>
    public class Slope2DGenerateBlocksCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE2D_GENERATE_BLOCKS";

        public string HelpText => "Generates 2D blocks from geological section and joint sets";

        public string Usage => @"SLOPE2D_GENERATE_BLOCKS [min_area=<m²>] [max_area=<m²>] [remove_small=<bool>] [use_formations=<bool>]
    min_area: Minimum block area in m² (default: 0.1)
    max_area: Maximum block area in m² (default: 100)
    remove_small: Remove blocks below minimum area (default: true)
    use_formations: Use formation boundaries as constraints (default: true)

Example:
    slope2d |> SLOPE2D_GENERATE_BLOCKS min_area=0.5 max_area=50";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStability2DDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability2D dataset");

            // Parse parameters
            float minArea = 0.1f;
            float maxArea = 100.0f;
            bool removeSmall = true;
            bool useFormations = true;

            if (node is CommandNode cmdNode)
            {
                foreach (var param in cmdNode.Parameters)
                {
                    switch (param.Key.ToLower())
                    {
                        case "min_area":
                            minArea = float.Parse(param.Value);
                            break;
                        case "max_area":
                            maxArea = float.Parse(param.Value);
                            break;
                        case "remove_small":
                            removeSmall = bool.Parse(param.Value);
                            break;
                        case "use_formations":
                            useFormations = bool.Parse(param.Value);
                            break;
                    }
                }
            }

            // Update block generation settings
            slopeDataset.BlockGenSettings.MinimumBlockArea = minArea;
            slopeDataset.BlockGenSettings.MaximumBlockArea = maxArea;
            slopeDataset.BlockGenSettings.RemoveSmallBlocks = removeSmall;
            slopeDataset.BlockGenSettings.UseFormationBoundaries = useFormations;

            // Generate blocks
            Console.WriteLine("Generating 2D blocks...");
            SlopeStability2DIntegration.GenerateBlocks(slopeDataset);

            Console.WriteLine($"Generated {slopeDataset.Blocks.Count} blocks");
            if (slopeDataset.Blocks.Count > 0)
            {
                float totalArea = 0;
                foreach (var block in slopeDataset.Blocks)
                    totalArea += block.Area;

                Console.WriteLine($"  Total area: {totalArea:F1}m²");
                Console.WriteLine($"  Average block area: {totalArea / slopeDataset.Blocks.Count:F2}m²");
            }

            return await Task.FromResult<Dataset>(slopeDataset);
        }
    }
}
