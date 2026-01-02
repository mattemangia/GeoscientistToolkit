using System;
using System.Globalization;
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
            string pixelSizeStr = null;
            string unitStr = null;

            // 1. Try named parameters
            if (cmd.Parameters.ContainsKey("path")) path = cmd.Parameters["path"];
            if (cmd.Parameters.ContainsKey("as")) datasetName = cmd.Parameters["as"];
            if (cmd.Parameters.ContainsKey("type")) typeStr = cmd.Parameters["type"];
            if (cmd.Parameters.ContainsKey("pixelsize")) pixelSizeStr = cmd.Parameters["pixelsize"];
            if (cmd.Parameters.ContainsKey("unit")) unitStr = cmd.Parameters["unit"];

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

            if (string.IsNullOrEmpty(pixelSizeStr))
            {
                var pixelSizeMatch = Regex.Match(cmd.FullText, @"\s+PIXELSIZE\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
                if (pixelSizeMatch.Success) pixelSizeStr = pixelSizeMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(unitStr))
            {
                var unitMatch = Regex.Match(cmd.FullText, @"\s+UNIT\s*=\s*""?([a-zA-Zµ]+)""?", RegexOptions.IgnoreCase);
                if (unitMatch.Success) unitStr = unitMatch.Groups[1].Value;
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

            var pixelSize = ParsePixelSize(pixelSizeStr);
            var unit = NormalizeUnit(unitStr);
            if (pixelSize.HasValue)
            {
                ApplyPixelSizeToLoader(loader, pixelSize.Value, unit);
            }

            // Create a progress reporter
            var progress = new Progress<(float, string)>(p => { /* Optional: log progress */ });

            Dataset loadedDataset = await loader.LoadAsync(progress);

            if (loadedDataset != null)
            {
                if (pixelSize.HasValue)
                {
                    ApplyPixelSizeToDataset(loadedDataset, pixelSize.Value, unit);
                }

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

        private static float? ParsePixelSize(string pixelSizeStr)
        {
            if (string.IsNullOrWhiteSpace(pixelSizeStr))
                return null;

            if (float.TryParse(pixelSizeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value > 0 ? value : null;

            return null;
        }

        private static string NormalizeUnit(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit))
                return "µm";

            var normalized = unit.Trim().ToLowerInvariant();
            return normalized switch
            {
                "um" => "µm",
                "micrometer" => "µm",
                "micrometers" => "µm",
                "micron" => "µm",
                "microns" => "µm",
                "mm" => "mm",
                "millimeter" => "mm",
                "millimeters" => "mm",
                _ => unit
            };
        }

        private static void ApplyPixelSizeToLoader(IDataLoader loader, float pixelSize, string unit)
        {
            if (loader is SingleImageLoader singleImageLoader)
            {
                singleImageLoader.PixelSize = pixelSize;
                singleImageLoader.Unit = ParsePixelSizeUnit(unit);
                return;
            }

            if (loader is CTStackLoaderWrapper ctStackLoader)
            {
                ctStackLoader.PixelSize = pixelSize;
                ctStackLoader.Unit = ParsePixelSizeUnit(unit);
                return;
            }

            if (loader is LabeledVolumeLoaderWrapper labeledVolumeLoader)
            {
                labeledVolumeLoader.PixelSize = pixelSize;
                labeledVolumeLoader.Unit = ParsePixelSizeUnit(unit);
                return;
            }

            if (loader is CtStackFileLoader ctStackFileLoader)
            {
                ctStackFileLoader.PixelSizeOverride = pixelSize;
                ctStackFileLoader.UnitOverride = unit;
            }
        }

        private static void ApplyPixelSizeToDataset(Dataset dataset, float pixelSize, string unit)
        {
            if (dataset is Data.Image.ImageDataset imageDataset)
            {
                imageDataset.PixelSize = pixelSize;
                imageDataset.Unit = unit;
                return;
            }

            if (dataset is Data.CtImageStack.CtImageStackDataset ctDataset)
            {
                ctDataset.PixelSize = pixelSize;
                ctDataset.SliceThickness = pixelSize;
                ctDataset.Unit = unit;
            }
        }

        private static PixelSizeUnit ParsePixelSizeUnit(string unit)
        {
            return unit.Equals("mm", StringComparison.OrdinalIgnoreCase)
                ? PixelSizeUnit.Millimeters
                : PixelSizeUnit.Micrometers;
        }
    }
}
