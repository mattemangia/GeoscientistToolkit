// GeoscientistToolkit/Business/GeoScript/GeoScriptUtilityCommands.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptUtilityCommands;

/// <summary>
/// LISTOPS command - List all available operations for a dataset type
/// Usage: LISTOPS
/// </summary>
public class ListOpsCommand : IGeoScriptCommand
{
    public string Name => "LISTOPS";
    public string HelpText => "List all available operations for the current dataset type";
    public string Usage => "LISTOPS";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset == null)
            throw new InvalidOperationException("No input dataset provided");

        var datasetType = context.InputDataset.Type;
        var availableCommands = GetAvailableCommandsForType(datasetType);

        Logger.Log($"Available operations for {datasetType}:");
        Logger.Log("─────────────────────────────────────────");

        foreach (var command in availableCommands.OrderBy(c => c.Name))
        {
            Logger.Log($"  {command.Name,-25} - {command.HelpText}");
            if (!string.IsNullOrEmpty(command.Usage))
                Logger.Log($"    Usage: {command.Usage}");
        }

        Logger.Log($"\nTotal: {availableCommands.Count} operations available");

        // Return the same dataset (utility command doesn't modify it)
        return Task.FromResult(context.InputDataset);
    }

    private List<IGeoScriptCommand> GetAvailableCommandsForType(DatasetType type)
    {
        var allCommands = CommandRegistry.GetAllCommands().ToList();
        var availableCommands = new List<IGeoScriptCommand>();

        foreach (var command in allCommands)
        {
            // Try to determine if command works with this dataset type
            // This is a simplified check - in practice you'd want more sophisticated logic
            if (IsCommandApplicable(command, type))
                availableCommands.Add(command);
        }

        return availableCommands;
    }

    private bool IsCommandApplicable(IGeoScriptCommand command, DatasetType type)
    {
        // Image operations
        if (type == DatasetType.SingleImage || type == DatasetType.CtImageStack)
        {
            var imageCommands = new[] { "BRIGHTNESS_CONTRAST", "FILTER", "THRESHOLD", "BINARIZE",
                "GRAYSCALE", "INVERT", "NORMALIZE", "SET_PIXEL_SIZE", "LISTOPS", "DISPTYPE", "UNLOAD", "INFO" };
            if (imageCommands.Contains(command.Name))
                return true;
        }

        // Table operations
        if (type == DatasetType.Table)
        {
            var tableCommands = new[] { "SELECT", "CALCULATE", "SORTBY", "GROUPBY", "RENAME",
                "DROP", "TAKE", "UNIQUE", "JOIN", "LISTOPS", "DISPTYPE", "UNLOAD", "INFO" };
            if (tableCommands.Contains(command.Name))
                return true;
        }

        // GIS operations
        if (type == DatasetType.GIS || type == DatasetType.SubsurfaceGIS)
        {
            var gisCommands = new[] { "SELECT", "BUFFER", "DISSOLVE", "EXPLODE", "CLEAN",
                "RECLASSIFY", "SLOPE", "ASPECT", "CONTOUR", "LISTOPS", "DISPTYPE", "UNLOAD", "INFO" };
            if (gisCommands.Contains(command.Name))
                return true;
        }

        // Utility commands work on all types
        if (new[] { "LISTOPS", "DISPTYPE", "UNLOAD", "INFO", "SET_PIXEL_SIZE" }.Contains(command.Name))
            return true;

        return false;
    }
}

/// <summary>
/// DISPTYPE command - Display dataset type information
/// Usage: DISPTYPE
/// </summary>
public class DispTypeCommand : IGeoScriptCommand
{
    public string Name => "DISPTYPE";
    public string HelpText => "Display detailed information about the dataset type and properties";
    public string Usage => "DISPTYPE";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset == null)
            throw new InvalidOperationException("No input dataset provided");

        var dataset = context.InputDataset;

        Logger.Log($"Dataset Information");
        Logger.Log($"══════════════════════════════════════════════");
        Logger.Log($"Name:         {dataset.Name}");
        Logger.Log($"Type:         {dataset.Type}");
        Logger.Log($"File Path:    {dataset.FilePath}");
        Logger.Log($"Created:      {dataset.DateCreated}");
        Logger.Log($"Modified:     {dataset.DateModified}");
        Logger.Log($"Size:         {FormatBytes(dataset.GetSizeInBytes())}");

        // Type-specific information
        if (dataset is Data.Image.ImageDataset imgDs)
        {
            Logger.Log($"\nImage Properties:");
            Logger.Log($"  Dimensions:   {imgDs.Width} x {imgDs.Height}");
            Logger.Log($"  Bit Depth:    {imgDs.BitDepth}");
            Logger.Log($"  Pixel Size:   {imgDs.PixelSize} {imgDs.Unit}");
            Logger.Log($"  Tags:         {imgDs.Tags}");
            Logger.Log($"  Segmentation: {imgDs.HasSegmentation}");
        }
        else if (dataset is Data.CtImageStack.CtImageStackDataset ctDs)
        {
            Logger.Log($"\nCT Image Stack Properties:");
            Logger.Log($"  Dimensions:   {ctDs.Width} x {ctDs.Height} x {ctDs.Depth}");
            Logger.Log($"  Bit Depth:    {ctDs.BitDepth}");
            Logger.Log($"  Pixel Size:   {ctDs.PixelSize} {ctDs.Unit}");
            Logger.Log($"  Materials:    {ctDs.Materials?.Count ?? 0}");
        }
        else if (dataset is Data.Table.TableDataset tableDs)
        {
            var dt = tableDs.GetDataTable();
            Logger.Log($"\nTable Properties:");
            Logger.Log($"  Rows:         {dt.Rows.Count}");
            Logger.Log($"  Columns:      {dt.Columns.Count}");
            Logger.Log($"  Column Names: {string.Join(", ", dt.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName))}");
        }
        else if (dataset is Data.GIS.GISDataset gisDs)
        {
            Logger.Log($"\nGIS Properties:");
            Logger.Log($"  Layers:       {gisDs.Layers.Count}");
            Logger.Log($"  Features:     {gisDs.Layers.Sum(l => l.Features.Count)}");
            Logger.Log($"  CRS:          {gisDs.Projection?.EPSG ?? "Not set"}");
        }

        // Metadata
        if (dataset.Metadata != null && dataset.Metadata.Count > 0)
        {
            Logger.Log($"\nMetadata:");
            foreach (var kvp in dataset.Metadata)
            {
                Logger.Log($"  {kvp.Key}: {kvp.Value}");
            }
        }

        // Return the same dataset (utility command doesn't modify it)
        return Task.FromResult(dataset);
    }

    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// UNLOAD command - Unload dataset from memory
/// Usage: UNLOAD
/// </summary>
public class UnloadCommand : IGeoScriptCommand
{
    public string Name => "UNLOAD";
    public string HelpText => "Unload dataset from memory to free up resources";
    public string Usage => "UNLOAD";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset == null)
            throw new InvalidOperationException("No input dataset provided");

        context.InputDataset.Unload();
        Logger.Log($"Unloaded dataset: {context.InputDataset.Name}");

        return Task.FromResult(context.InputDataset);
    }
}

/// <summary>
/// INFO command - Quick info about dataset
/// Usage: INFO
/// </summary>
public class InfoCommand : IGeoScriptCommand
{
    public string Name => "INFO";
    public string HelpText => "Display quick summary information about the dataset";
    public string Usage => "INFO";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset == null)
            throw new InvalidOperationException("No input dataset provided");

        var dataset = context.InputDataset;
        Logger.Log($"{dataset.Name} ({dataset.Type}) - {FormatBytes(dataset.GetSizeInBytes())}");

        return Task.FromResult(dataset);
    }

    private string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }
}

/// <summary>
/// SET_PIXEL_SIZE command - Update pixel size metadata on a dataset
/// Usage: SET_PIXEL_SIZE value=<size> [UNIT=um|mm]
/// </summary>
public class SetPixelSizeCommand : IGeoScriptCommand
{
    public string Name => "SET_PIXEL_SIZE";
    public string HelpText => "Set pixel size metadata on image or CT datasets";
    public string Usage => "SET_PIXEL_SIZE value=<size> [UNIT=um|mm]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset == null)
            throw new InvalidOperationException("No input dataset provided");

        var cmd = (CommandNode)node;
        var valueStr = cmd.Parameters.ContainsKey("value") ? cmd.Parameters["value"] : null;
        var unitStr = cmd.Parameters.ContainsKey("unit") ? cmd.Parameters["unit"] : null;

        if (string.IsNullOrEmpty(valueStr))
        {
            var match = Regex.Match(cmd.FullText, @"SET_PIXEL_SIZE\s+([0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
            if (match.Success) valueStr = match.Groups[1].Value;
        }

        if (string.IsNullOrEmpty(unitStr))
        {
            var unitMatch = Regex.Match(cmd.FullText, @"\s+UNIT\s*=\s*""?([a-zA-Zµ]+)""?", RegexOptions.IgnoreCase);
            if (unitMatch.Success) unitStr = unitMatch.Groups[1].Value;
        }

        if (!float.TryParse(valueStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pixelSize) || pixelSize <= 0)
            throw new ArgumentException("SET_PIXEL_SIZE requires a positive numeric value.");

        var unit = NormalizeUnit(unitStr);

        if (context.InputDataset is Data.Image.ImageDataset imageDataset)
        {
            imageDataset.PixelSize = pixelSize;
            imageDataset.Unit = unit;
            Logger.Log($"Updated image pixel size to {pixelSize} {unit}/pixel");
            return Task.FromResult<Dataset>(imageDataset);
        }

        if (context.InputDataset is Data.CtImageStack.CtImageStackDataset ctDataset)
        {
            ctDataset.PixelSize = pixelSize;
            ctDataset.SliceThickness = pixelSize;
            ctDataset.Unit = unit;
            Logger.Log($"Updated CT voxel size to {pixelSize} {unit}");
            return Task.FromResult<Dataset>(ctDataset);
        }

        throw new NotSupportedException("SET_PIXEL_SIZE is supported only for image and CT datasets.");
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
}
