using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptMiscDatasetCommands
{
    public class SaveCommand : IGeoScriptCommand
    {
        public string Name => "SAVE";
        public string HelpText => "Saves a dataset to a file.";
        public string Usage => "SAVE \"path/to/file\" [FORMAT=\"format\"]";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset == null)
                throw new InvalidOperationException("No input dataset provided to SAVE.");

            var cmd = (CommandNode)node;
            string path = null;
            string format = null;

            // 1. Try named parameters
            if (cmd.Parameters.ContainsKey("path")) path = cmd.Parameters["path"];
            if (cmd.Parameters.ContainsKey("format")) format = cmd.Parameters["format"];

            // 2. Parse from FullText if needed
            // Syntax: SAVE "path" FORMAT="format"
            if (string.IsNullOrEmpty(path))
            {
                var pathMatch = Regex.Match(cmd.FullText, @"SAVE\s+""([^""]+)""", RegexOptions.IgnoreCase);
                if (pathMatch.Success) path = pathMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(format))
            {
                var formatMatch = Regex.Match(cmd.FullText, @"\s+FORMAT\s*=\s*""?([^""\s]+)""?", RegexOptions.IgnoreCase);
                if (formatMatch.Success) format = formatMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("SAVE command requires a file path.");
            }

            Logger.Log($"Saving dataset '{context.InputDataset.Name}' to '{path}'" + (format != null ? $" (Format: {format})" : "") + "...");

            if (context.InputDataset is Data.Image.ImageDataset imageDs)
            {
                await Task.Run(() =>
                {
                    var exporter = new Data.Image.ImageExporter();
                    exporter.Export(imageDs, path, false, false);
                });
                Logger.Log("Image export completed.");
            }
            else if (context.InputDataset is Data.GIS.GISDataset gisDs)
            {
                if (path.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
                {
                    await Data.GIS.GISExporter.ExportToShapefileAsync(gisDs, path, gisDs.Layers.FirstOrDefault()?.Name ?? "Layer1", new Progress<float>());
                    Logger.Log("Shapefile export completed.");
                }
                else if (path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase))
                {
                    await Data.GIS.GISExporter.ExportToGeoTiffAsync(gisDs, path, new Progress<float>());
                    Logger.Log("GeoTIFF export completed.");
                }
                else
                {
                    throw new NotSupportedException("Unsupported GIS export format. Use .shp or .tif");
                }
            }
            else if (context.InputDataset is Data.PhysicoChem.PhysicoChemDataset pcDs)
            {
                await Task.Run(() =>
                {
                    var exporter = new Data.Exporters.Tough2Exporter();
                    exporter.Export(pcDs, path);
                });
                Logger.Log("Tough2 export completed.");
            }
            else
            {
                Logger.LogWarning($"SAVE not fully implemented for dataset type {context.InputDataset.Type}. Simulation only.");
            }

            return context.InputDataset;
        }
    }

    public class CopyCommand : IGeoScriptCommand
    {
        public string Name => "COPY";
        public string HelpText => "Duplicates the current dataset.";
        public string Usage => "COPY [AS \"NewName\"]";

        public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset == null)
                throw new InvalidOperationException("No input dataset provided to COPY.");

            var cmd = (CommandNode)node;
            string newName = null;

            if (cmd.Parameters.ContainsKey("as")) newName = cmd.Parameters["as"];

            if (string.IsNullOrEmpty(newName))
            {
                var asMatch = Regex.Match(cmd.FullText, @"COPY\s+AS\s+""?([^""\s]+)""?", RegexOptions.IgnoreCase);
                if (asMatch.Success) newName = asMatch.Groups[1].Value;
                else newName = context.InputDataset.Name + "_Copy";
            }

            Logger.Log($"Copying dataset '{context.InputDataset.Name}' to '{newName}'...");

            // In a real implementation, we would need a deep clone method on Dataset
            // For now, we will create a shallow copy wrapper or similar if possible, or just fail gracefully if cloning isn't supported.
            // Since Dataset doesn't have a Clone method in the snippet, we'll assume we can't fully clone it here without more logic.
            // But to satisfy the command existence:

            // Use the Clone method from Dataset base class
            // It defaults to shallow copy but can be overridden by specific types for deep copy
            var copy = context.InputDataset.Clone();
            copy.Name = newName;

            ProjectManager.Instance.AddDataset(copy);
            Logger.Log($"Dataset duplicated as: {copy.Name}");

            return Task.FromResult(copy);
        }
    }

    public class DeleteCommand : IGeoScriptCommand
    {
        public string Name => "DELETE";
        public string HelpText => "Removes the dataset from the project.";
        public string Usage => "DELETE";

        public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            if (context.InputDataset == null)
                throw new InvalidOperationException("No input dataset provided to DELETE.");

            Logger.Log($"Deleting dataset '{context.InputDataset.Name}' from project...");
            ProjectManager.Instance.RemoveDataset(context.InputDataset);

            // Return null or the dataset (it's removed from project but still in memory for the moment)
            return Task.FromResult(context.InputDataset);
        }
    }
}
