using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Data.Text;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Analysis.SlopeStability;
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

            var outputPath = ResolveOutputPath(path, format);

            if (context.InputDataset is Data.Image.ImageDataset imageDs)
            {
                await Task.Run(() =>
                {
                    var exporter = new Data.Image.ImageExporter();
                    exporter.Export(imageDs, outputPath, false, false);
                });
                Logger.Log("Image export completed.");
            }
            else if (context.InputDataset is Data.GIS.GISDataset gisDs)
            {
                if (outputPath.EndsWith(".shp", StringComparison.OrdinalIgnoreCase))
                {
                    await Data.GIS.GISExporter.ExportToShapefileAsync(
                        gisDs,
                        outputPath,
                        gisDs.Layers.FirstOrDefault()?.Name ?? "Layer1",
                        new Progress<float>());
                    Logger.Log("Shapefile export completed.");
                }
                else if (outputPath.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)
                         || outputPath.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                {
                    await Data.GIS.GISExporter.ExportToGeoTiffAsync(gisDs, outputPath, new Progress<float>());
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
                    exporter.Export(pcDs, outputPath);
                });
                Logger.Log("Tough2 export completed.");
            }
            else if (context.InputDataset is BoreholeDataset boreholeDs)
            {
                await Task.Run(() => ExportBoreholeDataset(boreholeDs, outputPath, format));
            }
            else if (context.InputDataset is TextDataset textDs)
            {
                await Task.Run(() => textDs.Export(outputPath));
                Logger.Log("Text export completed.");
            }
            else if (context.InputDataset is TableDataset tableDs)
            {
                await Task.Run(() => ExportTableDataset(tableDs, outputPath, format));
            }
            else if (context.InputDataset is TwoDGeologyDataset twoDGeology)
            {
                await Task.Run(() => SaveTwoDGeologyDataset(twoDGeology, outputPath));
            }
            else if (context.InputDataset is SlopeStabilityDataset slopeDs)
            {
                await Task.Run(() => slopeDs.Save(outputPath));
                Logger.Log("Slope stability dataset export completed.");
            }
            else if (context.InputDataset is ISerializableDataset serializable)
            {
                await Task.Run(() => ExportSerializableDataset(serializable, outputPath));
                Logger.Log("Dataset export completed.");
            }
            else
            {
                Logger.LogWarning($"SAVE not supported for dataset type {context.InputDataset.Type}.");
            }

            return context.InputDataset;
        }

        private static string ResolveOutputPath(string path, string format)
        {
            if (string.IsNullOrWhiteSpace(format))
                return path;

            var extension = format.StartsWith(".") ? format : $".{format}";
            var currentExtension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(currentExtension))
                return path + extension;

            return currentExtension.Equals(extension, StringComparison.OrdinalIgnoreCase)
                ? path
                : Path.ChangeExtension(path, extension);
        }

        private static void ExportSerializableDataset(ISerializableDataset dataset, string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(dataset.ToSerializableObject(), options);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        private static void SaveTwoDGeologyDataset(TwoDGeologyDataset dataset, string path)
        {
            var originalPath = dataset.FilePath;
            dataset.FilePath = path;
            dataset.Save();
            dataset.FilePath = originalPath;
            Logger.Log("2D geology dataset export completed.");
        }

        private static void ExportTableDataset(TableDataset dataset, string path, string format)
        {
            var extension = Path.GetExtension(path);
            var targetExtension = ResolveTableExtension(extension, format);
            var outputPath = extension.Equals(targetExtension, StringComparison.OrdinalIgnoreCase)
                ? path
                : Path.ChangeExtension(path, targetExtension);

            dataset.SaveAsCsv(outputPath);
            Logger.Log("Table export completed.");
        }

        private static string ResolveTableExtension(string extension, string format)
        {
            if (!string.IsNullOrEmpty(format))
                return format.StartsWith(".") ? format : $".{format}";

            return string.IsNullOrEmpty(extension) ? ".csv" : extension;
        }

        private static void ExportBoreholeDataset(BoreholeDataset dataset, string path, string format)
        {
            var extension = Path.GetExtension(path);
            var selectedFormat = string.IsNullOrEmpty(format) ? extension.TrimStart('.').ToLowerInvariant() : format.ToLowerInvariant();

            switch (selectedFormat)
            {
                case "bhb":
                case "binary":
                    dataset.SaveToBinaryFile(EnsureExtension(path, ".bhb"));
                    Logger.Log("Borehole dataset exported to binary.");
                    break;
                case "csv":
                    ExportBoreholeToCsv(dataset, EnsureExtension(path, ".csv"));
                    break;
                case "las":
                    ExportBoreholeToLas(dataset, EnsureExtension(path, ".las"));
                    break;
                case "json":
                case "bhjson":
                case "":
                    dataset.SaveToFile(EnsureExtension(path, ".json"));
                    Logger.Log("Borehole dataset exported to JSON.");
                    break;
                default:
                    dataset.SaveToFile(path);
                    Logger.Log("Borehole dataset export completed.");
                    break;
            }
        }

        private static string EnsureExtension(string path, string extension)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return path;

            return Path.ChangeExtension(path, extension);
        }

        private static void ExportBoreholeToCsv(BoreholeDataset borehole, string path)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine($"# Well Name: {borehole.WellName}");
            writer.WriteLine($"# Field: {borehole.Field}");
            writer.WriteLine($"# Total Depth: {borehole.TotalDepth}");

            var tracks = borehole.ParameterTracks.Values.Where(t => t.Points.Any()).ToList();
            if (!tracks.Any())
            {
                writer.WriteLine("# No parameter data to export.");
                Logger.Log("Borehole dataset has no parameter tracks to export.");
                return;
            }

            var headers = "Depth (m)," + string.Join(",",
                tracks.Select(t => $"{t.Name.Replace(',', ' ')} ({t.Unit.Replace(',', ' ')})"));
            writer.WriteLine(headers);

            var allDepths = tracks.SelectMany(t => t.Points.Select(p => p.Depth)).Distinct().OrderBy(d => d).ToList();
            var step = 1.0f;
            var startDepth = allDepths.Min();
            var endDepth = allDepths.Max();

            for (var depth = startDepth; depth <= endDepth; depth += step)
            {
                var values = new List<string> { depth.ToString("F4") };
                foreach (var track in tracks)
                {
                    var interpolatedValue = borehole.GetParameterValueAtDepth(track.Name, depth);
                    values.Add(interpolatedValue.HasValue ? interpolatedValue.Value.ToString("F4") : "");
                }

                writer.WriteLine(string.Join(",", values));
            }

            Logger.Log($"Exported borehole data to CSV: {path}");
        }

        private static void ExportBoreholeToLas(BoreholeDataset borehole, string path)
        {
            using var writer = new StreamWriter(path, false, Encoding.UTF8);
            writer.WriteLine("~VERSION INFORMATION");
            writer.WriteLine(" VERS.                2.0 : CWLS LOG ASCII STANDARD - VERSION 2.0");
            writer.WriteLine(" WRAP.                 NO : ONE LINE PER DEPTH STEP");
            writer.WriteLine("~WELL INFORMATION");
            var tracks = borehole.ParameterTracks.Values.Where(t => t.Points.Any()).ToList();
            var startDepth = tracks.Any() ? tracks.SelectMany(t => t.Points).Min(p => (float?)p.Depth) ?? 0.0f : 0.0f;
            writer.WriteLine($" STRT.M {startDepth:F4} : START DEPTH");
            writer.WriteLine($" STOP.M {borehole.TotalDepth:F4} : STOP DEPTH");
            writer.WriteLine(" STEP.M              -999.25 : STEP (VARIABLE)");
            writer.WriteLine(" NULL.              -999.25 : NULL VALUE");
            writer.WriteLine($" WELL.   {borehole.WellName,-20} : WELL NAME");
            writer.WriteLine($" FLD.    {borehole.Field,-20} : FIELD NAME");
            writer.WriteLine("~CURVE INFORMATION");
            foreach (var track in tracks)
            {
                var mnemonic = new string(track.Name.Replace(" ", "_").Take(8).ToArray()).ToUpper();
                writer.WriteLine($" {mnemonic,-8}.{track.Unit,-15}       : {track.Name}");
            }

            writer.WriteLine("~PARAMETER INFORMATION");
            writer.WriteLine("~A  DEPTH" + string.Concat(tracks.Select(t =>
                $" {new string(t.Name.Replace(" ", "_").Take(8).ToArray()).ToUpper(),-15}")));

            var allDepths = tracks.SelectMany(t => t.Points.Select(p => p.Depth)).Distinct().OrderBy(d => d).ToList();

            foreach (var depth in allDepths)
            {
                var line = new StringBuilder();
                line.Append($"{depth,-16:F4}");
                foreach (var track in tracks)
                {
                    var val = borehole.GetParameterValueAtDepth(track.Name, depth);
                    line.Append(val.HasValue ? $"{val.Value,-16:F4}" : $"{"-999.25",-16}");
                }

                writer.WriteLine(line.ToString());
            }

            Logger.Log($"Exported borehole data to LAS: {path}");
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
