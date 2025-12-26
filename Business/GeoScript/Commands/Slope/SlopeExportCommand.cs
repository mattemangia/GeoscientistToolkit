using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.SlopeStability;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;

namespace GeoscientistToolkit.Business.GeoScript.Commands.Slope
{
    /// <summary>
    /// SLOPE_EXPORT command.
    /// Exports slope stability results.
    /// Usage: SLOPE_EXPORT path="results.csv" format=csv
    /// </summary>
    public class SlopeExportCommand : IGeoScriptCommand
    {
        public string Name => "SLOPE_EXPORT";

        public string HelpText => "Exports slope stability analysis results";

        public string Usage => @"SLOPE_EXPORT path=<filename> [format=<csv|vtk|json|binary>]
    path: Output file path
    format: Export format - csv, vtk, json, or binary (default: csv)

Example:
    slope_dataset |> SLOPE_EXPORT path=""results.csv"" format=csv";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset is not SlopeStabilityDataset slopeDataset)
                throw new ArgumentException("Input must be a SlopeStability dataset");

            if (!slopeDataset.HasResults)
                throw new InvalidOperationException("No results available. Run simulation first.");

            string path = "";
            string format = "csv";

            if (node is CommandNode cmdNode)
            {
                if (cmdNode.Parameters.TryGetValue("path", out string pathValue))
                    path = pathValue.Trim('"');

                if (cmdNode.Parameters.TryGetValue("format", out string formatValue))
                    format = formatValue.ToLower();
            }

            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Output path required");

            switch (format)
            {
                case "csv":
                    await ExportCSV(slopeDataset, path);
                    break;
                case "vtk":
                    await ExportVTK(slopeDataset, path);
                    break;
                case "json":
                    await ExportJSON(slopeDataset, path);
                    break;
                case "binary":
                case "bin":
                case "ssr":
                    ExportBinary(slopeDataset, path);
                    break;
                default:
                    throw new ArgumentException($"Unknown format: {format}");
            }

            Console.WriteLine($"Exported results to {path}");

            return slopeDataset;
        }

        private async Task ExportCSV(SlopeStabilityDataset dataset, string path)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("BlockID,InitialX,InitialY,InitialZ,FinalX,FinalY,FinalZ," +
                         "DisplacementX,DisplacementY,DisplacementZ,DisplacementMag," +
                         "VelocityX,VelocityY,VelocityZ,VelocityMag,Mass,IsFixed,HasFailed");

            // Data
            foreach (var blockResult in dataset.Results.BlockResults)
            {
                sb.AppendLine($"{blockResult.BlockId}," +
                    $"{blockResult.InitialPosition.X},{blockResult.InitialPosition.Y},{blockResult.InitialPosition.Z}," +
                    $"{blockResult.FinalPosition.X},{blockResult.FinalPosition.Y},{blockResult.FinalPosition.Z}," +
                    $"{blockResult.Displacement.X},{blockResult.Displacement.Y},{blockResult.Displacement.Z}," +
                    $"{blockResult.Displacement.Length()}," +
                    $"{blockResult.Velocity.X},{blockResult.Velocity.Y},{blockResult.Velocity.Z}," +
                    $"{blockResult.Velocity.Length()}," +
                    $"{blockResult.Mass},{blockResult.IsFixed},{blockResult.HasFailed}");
            }

            await File.WriteAllTextAsync(path, sb.ToString());
        }

        private async Task ExportVTK(SlopeStabilityDataset dataset, string path)
        {
            var sb = new StringBuilder();

            // VTK header
            sb.AppendLine("# vtk DataFile Version 3.0");
            sb.AppendLine("Slope Stability Results");
            sb.AppendLine("ASCII");
            sb.AppendLine("DATASET POLYDATA");

            // Points
            sb.AppendLine($"POINTS {dataset.Results.BlockResults.Count} float");
            foreach (var blockResult in dataset.Results.BlockResults)
            {
                sb.AppendLine($"{blockResult.FinalPosition.X} {blockResult.FinalPosition.Y} {blockResult.FinalPosition.Z}");
            }

            // Point data
            sb.AppendLine($"POINT_DATA {dataset.Results.BlockResults.Count}");

            // Displacement magnitude
            sb.AppendLine("SCALARS DisplacementMagnitude float 1");
            sb.AppendLine("LOOKUP_TABLE default");
            foreach (var blockResult in dataset.Results.BlockResults)
            {
                sb.AppendLine($"{blockResult.Displacement.Length()}");
            }

            // Displacement vectors
            sb.AppendLine("VECTORS Displacement float");
            foreach (var blockResult in dataset.Results.BlockResults)
            {
                sb.AppendLine($"{blockResult.Displacement.X} {blockResult.Displacement.Y} {blockResult.Displacement.Z}");
            }

            await File.WriteAllTextAsync(path, sb.ToString());
        }

        private async Task ExportJSON(SlopeStabilityDataset dataset, string path)
        {
            var dto = dataset.ToDTO();
            var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(path, json);
        }

        private void ExportBinary(SlopeStabilityDataset dataset, string path)
        {
            SlopeStabilityResultsBinarySerializer.Write(path, dataset.Results);
        }
    }
}
