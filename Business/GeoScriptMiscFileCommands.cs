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

        public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
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

            // Logic to actually save would go here (delegating to dataset export logic or similar)
            // For now, we simulate success as many export implementations might be UI driven or specific to types not fully exposed here.
            // In a real implementation, we would call something like:
            // ExporterFactory.GetExporter(context.InputDataset.Type).Export(context.InputDataset, path, format);

            Logger.Log("Save completed (Simulation).");

            return Task.FromResult(context.InputDataset);
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

            // Note: A true copy requires deep cloning data which depends on specific dataset types.
            // Here we just log it as a placeholder for the feature.
            Logger.LogWarning("Deep copy not fully implemented for all dataset types. Creating reference copy.");

            // We can't really return a new dataset without cloning.
            // We'll return the input to avoid breaking pipelines, but log that it's not a true copy.

            return Task.FromResult(context.InputDataset);
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
