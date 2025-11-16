// GeoscientistToolkit/Business/GeoScript/GeoScriptUtilityCommands.cs

using System;
using System.Collections.Generic;
using System.Linq;
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

        Logger.LogInfo($"Available operations for {datasetType}:");
        Logger.LogInfo("─────────────────────────────────────────");

        foreach (var command in availableCommands.OrderBy(c => c.Name))
        {
            Logger.LogInfo($"  {command.Name,-25} - {command.HelpText}");
            if (!string.IsNullOrEmpty(command.Usage))
                Logger.LogInfo($"    Usage: {command.Usage}");
        }

        Logger.LogInfo($"\nTotal: {availableCommands.Count} operations available");

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
                "GRAYSCALE", "INVERT", "NORMALIZE", "LISTOPS", "DISPTYPE", "UNLOAD", "INFO" };
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
        if (new[] { "LISTOPS", "DISPTYPE", "UNLOAD", "INFO" }.Contains(command.Name))
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

        Logger.LogInfo($"Dataset Information");
        Logger.LogInfo($"══════════════════════════════════════════════");
        Logger.LogInfo($"Name:         {dataset.Name}");
        Logger.LogInfo($"Type:         {dataset.Type}");
        Logger.LogInfo($"File Path:    {dataset.FilePath}");
        Logger.LogInfo($"Created:      {dataset.DateCreated}");
        Logger.LogInfo($"Modified:     {dataset.DateModified}");
        Logger.LogInfo($"Size:         {FormatBytes(dataset.GetSizeInBytes())}");
        Logger.LogInfo($"Loaded:       {dataset.IsLoaded}");

        // Type-specific information
        if (dataset is Data.Image.ImageDataset imgDs)
        {
            Logger.LogInfo($"\nImage Properties:");
            Logger.LogInfo($"  Dimensions:   {imgDs.Width} x {imgDs.Height}");
            Logger.LogInfo($"  Bit Depth:    {imgDs.BitDepth}");
            Logger.LogInfo($"  Pixel Size:   {imgDs.PixelSize} {imgDs.Unit}");
            Logger.LogInfo($"  Tags:         {imgDs.Tags}");
            Logger.LogInfo($"  Segmentation: {imgDs.HasSegmentation}");
        }
        else if (dataset is Data.CtImageStack.CtImageStackDataset ctDs)
        {
            Logger.LogInfo($"\nCT Image Stack Properties:");
            Logger.LogInfo($"  Dimensions:   {ctDs.Width} x {ctDs.Height} x {ctDs.Depth}");
            Logger.LogInfo($"  Bit Depth:    {ctDs.BitDepth}");
            Logger.LogInfo($"  Voxel Size:   {ctDs.VoxelSize} {ctDs.Unit}");
            Logger.LogInfo($"  Materials:    {ctDs.Materials?.Count ?? 0}");
        }
        else if (dataset is Data.Table.TableDataset tableDs)
        {
            var dt = tableDs.GetDataTable();
            Logger.LogInfo($"\nTable Properties:");
            Logger.LogInfo($"  Rows:         {dt.Rows.Count}");
            Logger.LogInfo($"  Columns:      {dt.Columns.Count}");
            Logger.LogInfo($"  Column Names: {string.Join(", ", dt.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName))}");
        }
        else if (dataset is Data.GIS.GISDataset gisDs)
        {
            Logger.LogInfo($"\nGIS Properties:");
            Logger.LogInfo($"  Layers:       {gisDs.Layers.Count}");
            Logger.LogInfo($"  Features:     {gisDs.Layers.Sum(l => l.Features.Count)}");
            Logger.LogInfo($"  CRS:          {gisDs.CoordinateSystem ?? "Not set"}");
        }

        // Metadata
        if (dataset.Metadata != null && dataset.Metadata.Count > 0)
        {
            Logger.LogInfo($"\nMetadata:");
            foreach (var kvp in dataset.Metadata)
            {
                Logger.LogInfo($"  {kvp.Key}: {kvp.Value}");
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

        if (context.InputDataset.IsLoaded)
        {
            context.InputDataset.Unload();
            Logger.LogInfo($"Unloaded dataset: {context.InputDataset.Name}");
        }
        else
        {
            Logger.LogInfo($"Dataset {context.InputDataset.Name} is already unloaded");
        }

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
        Logger.LogInfo($"{dataset.Name} ({dataset.Type}) - {FormatBytes(dataset.GetSizeInBytes())} - {(dataset.IsLoaded ? "Loaded" : "Unloaded")}");

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
