using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.TwoDGeology;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE2D_FROM_GEOLOGY command.
    /// Creates a 2D slope stability dataset from a TwoDGeologyDataset.
    /// Usage: SLOPE2D_FROM_GEOLOGY thickness=1.0 name="Slope Analysis"
    /// </summary>
    public class Slope2DFromGeologyCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE2D_FROM_GEOLOGY";

        public string HelpText => "Creates a 2D slope stability dataset from a geological profile";

        public string Usage => @"SLOPE2D_FROM_GEOLOGY [thickness=<meters>] [name=<string>]
    thickness: Section thickness perpendicular to profile (default: 1.0m)
    name: Name for the slope stability dataset (default: auto-generated)

Example:
    geology_profile.2dg |> SLOPE2D_FROM_GEOLOGY thickness=1.5 name=""Valley Slope Analysis""";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not TwoDGeologyDataset geologyDataset)
                throw new ArgumentException("Input must be a TwoDGeology dataset");

            if (geologyDataset.ProfileData == null)
                throw new InvalidOperationException("Geology dataset has no profile data loaded");

            // Parse parameters
            float thickness = 1.0f;
            string datasetName = $"{geologyDataset.Name} - Slope 2D";

            if (node is CommandNode cmdNode)
            {
                foreach (var param in cmdNode.Parameters)
                {
                    switch (param.Key.ToLower())
                    {
                        case "thickness":
                            thickness = float.Parse(param.Value);
                            break;
                        case "name":
                            datasetName = param.Value.Trim('"');
                            break;
                    }
                }
            }

            // Create 2D slope stability dataset
            var slopeDataset = SlopeStability2DIntegration.CreateFromTwoDGeologyDataset(
                geologyDataset, datasetName, thickness);

            if (slopeDataset == null)
                throw new InvalidOperationException("Failed to create 2D slope stability dataset");

            Console.WriteLine($"Created 2D slope stability dataset: {slopeDataset.Name}");
            Console.WriteLine($"  Section length: {slopeDataset.GeologicalSection.TotalLength:F1}m");
            Console.WriteLine($"  Thickness: {thickness:F2}m");
            Console.WriteLine($"  Materials: {slopeDataset.Materials.Count}");

            return await Task.FromResult<Dataset>(slopeDataset);
        }
    }
}
