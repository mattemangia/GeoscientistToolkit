using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_FILTER_BLOCKS command.
    /// Filters blocks by volume, aspect ratio, or other criteria.
    /// Usage: SLOPE_FILTER_BLOCKS min_volume=0.01 max_volume=100
    /// </summary>
    public class SlopeFilterBlocksCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_FILTER_BLOCKS";

        public string HelpText => "Filters blocks to clean up mesh before simulation";

        public string Usage => @"SLOPE_FILTER_BLOCKS [min_volume=<m3>] [max_volume=<m3>] [max_aspect=<ratio>] [remove_degenerate=<true|false>]
    min_volume: Minimum block volume in m³ (default: 0.001)
    max_volume: Maximum block volume in m³ (default: 1000)
    max_aspect: Maximum aspect ratio (default: 10)
    remove_degenerate: Remove degenerate geometry (default: true)

Example:
    slope_dataset |> SLOPE_FILTER_BLOCKS min_volume=0.01 max_volume=100 max_aspect=10";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            var criteria = new FilterCriteria
            {
                FilterByVolume = true,
                MinVolume = 0.001f,
                MaxVolume = 1000.0f,
                RemoveSmall = true,
                RemoveLarge = false,
                FilterByAspectRatio = false,
                MaxAspectRatio = 10.0f,
                FilterDegenerateGeometry = true
            };

            if (node is CommandNode cmdNode)
            {
                if (cmdNode.Parameters.TryGetValue("min_volume", out string minVol))
                {
                    criteria.MinVolume = float.Parse(minVol);
                    criteria.RemoveSmall = true;
                }

                if (cmdNode.Parameters.TryGetValue("max_volume", out string maxVol))
                {
                    criteria.MaxVolume = float.Parse(maxVol);
                    criteria.RemoveLarge = true;
                }

                if (cmdNode.Parameters.TryGetValue("max_aspect", out string maxAspect))
                {
                    criteria.MaxAspectRatio = float.Parse(maxAspect);
                    criteria.FilterByAspectRatio = true;
                }

                if (cmdNode.Parameters.TryGetValue("remove_degenerate", out string removeDegen))
                {
                    criteria.FilterDegenerateGeometry = bool.Parse(removeDegen);
                }
            }

            int originalCount = slopeDataset.Blocks.Count;

            // Apply filtering
            var result = BlockFilter.FilterBlocks(slopeDataset.Blocks, criteria);
            slopeDataset.Blocks = result.Blocks;

            Console.WriteLine($"Block filtering completed:");
            Console.WriteLine($"  Original: {originalCount} blocks");
            Console.WriteLine($"  Filtered: {result.FinalCount} blocks");
            Console.WriteLine($"  Removed: {result.TotalRemoved} blocks");

            return await Task.FromResult(slopeDataset);
        }
    }
}
