using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Loaders;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptMiscDatasetCommands
{
    public class LoadCommand : IGeoScriptCommand
    {
        public string Name => "LOAD";
        public string HelpText => "Loads a dataset from a file. Supports optional type specification.";
        public string Usage => "LOAD path=\"path/to/file\" [AS \"DatasetName\"] [TYPE=<DatasetType>]";

        public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
        {
            var cmd = (CommandNode)node;
            string path = null;
            string datasetName = null;
            string typeStr = null;

            // 1. Try named parameters
            if (cmd.Parameters.ContainsKey("path")) path = cmd.Parameters["path"];
            if (cmd.Parameters.ContainsKey("as")) datasetName = cmd.Parameters["as"];
            if (cmd.Parameters.ContainsKey("type")) typeStr = cmd.Parameters["type"];

            // 2. Parse from FullText if standard parser didn't catch everything or for classic syntax
            // Syntax: LOAD "path" AS "Name" TYPE=Type
            if (string.IsNullOrEmpty(path))
            {
                var pathMatch = Regex.Match(cmd.FullText, @"LOAD\s+""([^""]+)""", RegexOptions.IgnoreCase);
                if (pathMatch.Success) path = pathMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(datasetName))
            {
                var asMatch = Regex.Match(cmd.FullText, @"\s+AS\s+""([^""]+)""", RegexOptions.IgnoreCase);
                if (asMatch.Success) datasetName = asMatch.Groups[1].Value;
                else
                {
                    // Maybe unquoted name?
                    asMatch = Regex.Match(cmd.FullText, @"\s+AS\s+(\w+)", RegexOptions.IgnoreCase);
                    if (asMatch.Success) datasetName = asMatch.Groups[1].Value;
                }
            }

            if (string.IsNullOrEmpty(typeStr))
            {
                var typeMatch = Regex.Match(cmd.FullText, @"\s+TYPE\s*=\s*(\w+)", RegexOptions.IgnoreCase);
                if (typeMatch.Success) typeStr = typeMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("LOAD command requires a file path.");
            }

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                throw new FileNotFoundException($"File not found: {path}");
            }

            DatasetType? preferredType = null;
            if (!string.IsNullOrEmpty(typeStr))
            {
                if (Enum.TryParse<DatasetType>(typeStr, true, out var parsedType))
                {
                    preferredType = parsedType;
                }
                else
                {
                    Logger.LogWarning($"Unknown dataset type '{typeStr}'. Attempting auto-detection.");
                }
            }

            IDataLoader loader = DataLoaderFactory.GetLoaderForFile(path, preferredType);

            if (loader == null && preferredType.HasValue)
            {
                // Try force loading with specific type if automatic detection failed or factory logic was strict
                loader = DataLoaderFactory.GetLoaderForType(preferredType.Value, path);
            }

            if (loader == null)
            {
                throw new NotSupportedException($"No suitable loader found for file '{path}'" + (preferredType.HasValue ? $" with type '{preferredType}'" : "") + ".");
            }

            Logger.Log($"Loading dataset from '{path}' using {loader.Name}...");

            // Create a progress reporter
            var progress = new Progress<(float, string)>(p => { /* Optional: log progress */ });

            Dataset loadedDataset = await loader.LoadAsync(progress);

            if (loadedDataset != null)
            {
                if (!string.IsNullOrEmpty(datasetName))
                {
                    loadedDataset.Name = datasetName;
                }

                // Add to project
                ProjectManager.Instance.AddDataset(loadedDataset);
                Logger.Log($"Successfully loaded dataset: {loadedDataset.Name} ({loadedDataset.Type})");
            }
            else
            {
                throw new Exception($"Failed to load dataset from '{path}'.");
            }

            return loadedDataset;
        }
    }
}
