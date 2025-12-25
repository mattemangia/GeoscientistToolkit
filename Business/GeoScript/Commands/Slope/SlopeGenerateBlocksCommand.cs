using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Mesh3D;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_GENERATE_BLOCKS command.
    /// Generates blocks from a mesh using joint sets.
    /// Usage: SLOPE_GENERATE_BLOCKS target_size=1.0 remove_small=true
    /// </summary>
    public class SlopeGenerateBlocksCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_GENERATE_BLOCKS";

        public string HelpText => "Generates blocks from a 3D mesh using joint sets";

        public string Usage => @"SLOPE_GENERATE_BLOCKS target_size=<size> remove_small=<bool> merge_slivers=<bool>
    target_size: Target block size in meters (default: 1.0)
    remove_small: Remove small blocks below minimum volume (default: true)
    merge_slivers: Merge sliver blocks (default: true)

Example:
    mesh3d.obj |> SLOPE_GENERATE_BLOCKS target_size=2.0";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            var inputDataset = context.CurrentDataset;

            if (inputDataset is Mesh3DDataset mesh3D)
            {
                // Create new slope stability dataset from mesh
                var slopeDataset = new SlopeStabilityDataset
                {
                    Name = $"{mesh3D.Name} - Slope Analysis",
                    FilePath = mesh3D.FilePath + ".slope",
                    SourceMeshPath = mesh3D.FilePath
                };

                // Parse parameters
                float targetSize = 1.0f;
                bool removeSmall = true;
                bool mergeSlivers = true;

                if (node is CommandNode cmdNode)
                {
                    foreach (var param in cmdNode.Parameters)
                    {
                        switch (param.Key.ToLower())
                        {
                            case "target_size":
                                targetSize = float.Parse(param.Value);
                                break;
                            case "remove_small":
                                removeSmall = bool.Parse(param.Value);
                                break;
                            case "merge_slivers":
                                mergeSlivers = bool.Parse(param.Value);
                                break;
                        }
                    }
                }

                slopeDataset.BlockGenSettings.TargetBlockSize = targetSize;
                slopeDataset.BlockGenSettings.RemoveSmallBlocks = removeSmall;
                slopeDataset.BlockGenSettings.MergeSliverBlocks = mergeSlivers;

                // Generate blocks
                var generator = new BlockGenerator(slopeDataset.BlockGenSettings);
                slopeDataset.Blocks = generator.GenerateBlocks(mesh3D, slopeDataset.JointSets);

                Console.WriteLine($"Generated {slopeDataset.Blocks.Count} blocks");

                return await Task.FromResult<Dataset>(slopeDataset);
            }
            else if (inputDataset is SlopeStabilityDataset slopeExisting)
            {
                // Regenerate blocks from existing dataset
                if (string.IsNullOrEmpty(slopeExisting.SourceMeshPath))
                    throw new InvalidOperationException("No source mesh available");

                var mesh3D = new Mesh3DDataset(slopeExisting.SourceMeshPath);
                mesh3D.Load();

                var generator = new BlockGenerator(slopeExisting.BlockGenSettings);
                slopeExisting.Blocks = generator.GenerateBlocks(mesh3D, slopeExisting.JointSets);

                Console.WriteLine($"Regenerated {slopeExisting.Blocks.Count} blocks");

                return await Task.FromResult<Dataset>(slopeExisting);
            }
            else
            {
                throw new ArgumentException("Input must be a Mesh3D or SlopeStability dataset");
            }
        }
    }
}
