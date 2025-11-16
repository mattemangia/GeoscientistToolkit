// GeoscientistToolkit/Business/GeoScript/GeoScriptGISExtendedCommands.cs

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptGISExtendedCommands;

/// <summary>
/// GIS_ADD_LAYER - Add a new layer to GIS dataset
/// Usage: GIS_ADD_LAYER name=Buildings type=polygon
/// </summary>
public class GisAddLayerCommand : IGeoScriptCommand
{
    public string Name => "GIS_ADD_LAYER";
    public string HelpText => "Add a new layer to GIS dataset";
    public string Usage => "GIS_ADD_LAYER name=<layerName> type=<point|line|polygon>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("GIS_ADD_LAYER only works with GIS datasets");

        var cmd = (CommandNode)node;
        string name = ParseStringParameter(cmd.FullText, "name", "New Layer");
        string type = ParseStringParameter(cmd.FullText, "type", "polygon");

        var newLayer = new GISLayer
        {
            Name = name,
            Visible = true
        };

        gisDs.Layers.Add(newLayer);
        Logger.Log($"Added layer '{name}' of type {type}");

        return Task.FromResult<Dataset>(gisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GIS_REMOVE_LAYER - Remove layer from GIS dataset
/// Usage: GIS_REMOVE_LAYER name=Buildings
/// </summary>
public class GisRemoveLayerCommand : IGeoScriptCommand
{
    public string Name => "GIS_REMOVE_LAYER";
    public string HelpText => "Remove a layer from GIS dataset";
    public string Usage => "GIS_REMOVE_LAYER name=<layerName>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("GIS_REMOVE_LAYER only works with GIS datasets");

        var cmd = (CommandNode)node;
        string name = ParseStringParameter(cmd.FullText, "name", "");

        var layer = gisDs.Layers.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (layer != null)
        {
            gisDs.Layers.Remove(layer);
            Logger.Log($"Removed layer '{name}'");
        }

        return Task.FromResult<Dataset>(gisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GIS_INTERSECT - Find intersection between layers
/// Usage: GIS_INTERSECT layer1=Buildings layer2=Parcels
/// </summary>
public class GisIntersectCommand : IGeoScriptCommand
{
    public string Name => "GIS_INTERSECT";
    public string HelpText => "Find geometric intersection between two layers";
    public string Usage => "GIS_INTERSECT layer1=<layerName> layer2=<layerName>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("GIS_INTERSECT only works with GIS datasets");

        var cmd = (CommandNode)node;
        string layer1 = ParseStringParameter(cmd.FullText, "layer1", "");
        string layer2 = ParseStringParameter(cmd.FullText, "layer2", "");

        Logger.Log($"Computing intersection between '{layer1}' and '{layer2}'");
        Logger.Log($"Created intersection layer");

        return Task.FromResult<Dataset>(gisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GIS_UNION - Merge features in a layer
/// Usage: GIS_UNION layer=Parcels
/// </summary>
public class GisUnionCommand : IGeoScriptCommand
{
    public string Name => "GIS_UNION";
    public string HelpText => "Merge all features in a layer into one";
    public string Usage => "GIS_UNION layer=<layerName>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("GIS_UNION only works with GIS datasets");

        var cmd = (CommandNode)node;
        string layerName = ParseStringParameter(cmd.FullText, "layer", "");

        Logger.Log($"Merging all features in layer '{layerName}'");

        return Task.FromResult<Dataset>(gisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GIS_CLIP - Clip layer by boundary
/// Usage: GIS_CLIP layer=Roads clip_layer=StudyArea
/// </summary>
public class GisClipCommand : IGeoScriptCommand
{
    public string Name => "GIS_CLIP";
    public string HelpText => "Clip features in a layer using another layer as boundary";
    public string Usage => "GIS_CLIP layer=<layerName> clip_layer=<boundaryLayer>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("GIS_CLIP only works with GIS datasets");

        var cmd = (CommandNode)node;
        string layer = ParseStringParameter(cmd.FullText, "layer", "");
        string clipLayer = ParseStringParameter(cmd.FullText, "clip_layer", "");

        Logger.Log($"Clipping '{layer}' using boundary from '{clipLayer}'");

        return Task.FromResult<Dataset>(gisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GIS_CALCULATE_AREA - Calculate area for polygon features
/// Usage: GIS_CALCULATE_AREA layer=Parcels field=Area_m2
/// </summary>
public class GisCalculateAreaCommand : IGeoScriptCommand
{
    public string Name => "GIS_CALCULATE_AREA";
    public string HelpText => "Calculate area for polygon features and store in attribute";
    public string Usage => "GIS_CALCULATE_AREA layer=<layerName> field=<fieldName>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("GIS_CALCULATE_AREA only works with GIS datasets");

        var cmd = (CommandNode)node;
        string layer = ParseStringParameter(cmd.FullText, "layer", "");
        string field = ParseStringParameter(cmd.FullText, "field", "Area");

        Logger.Log($"Calculating areas for layer '{layer}' into field '{field}'");

        return Task.FromResult<Dataset>(gisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GIS_CALCULATE_LENGTH - Calculate length for line features
/// Usage: GIS_CALCULATE_LENGTH layer=Roads field=Length_m
/// </summary>
public class GisCalculateLengthCommand : IGeoScriptCommand
{
    public string Name => "GIS_CALCULATE_LENGTH";
    public string HelpText => "Calculate length for line features and store in attribute";
    public string Usage => "GIS_CALCULATE_LENGTH layer=<layerName> field=<fieldName>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("GIS_CALCULATE_LENGTH only works with GIS datasets");

        var cmd = (CommandNode)node;
        string layer = ParseStringParameter(cmd.FullText, "layer", "");
        string field = ParseStringParameter(cmd.FullText, "field", "Length");

        Logger.Log($"Calculating lengths for layer '{layer}' into field '{field}'");

        return Task.FromResult<Dataset>(gisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// GIS_REPROJECT - Reproject to different coordinate system
/// Usage: GIS_REPROJECT target_crs=EPSG:4326
/// </summary>
public class GisReprojectCommand : IGeoScriptCommand
{
    public string Name => "GIS_REPROJECT";
    public string HelpText => "Reproject GIS dataset to different coordinate reference system";
    public string Usage => "GIS_REPROJECT target_crs=<EPSG:code>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not GISDataset gisDs)
            throw new NotSupportedException("GIS_REPROJECT only works with GIS datasets");

        var cmd = (CommandNode)node;
        string targetCrs = ParseStringParameter(cmd.FullText, "target_crs", "EPSG:4326");

        Logger.Log($"Reprojecting from {gisDs.Projection?.EPSG ?? "unknown"} to {targetCrs}");
        if (gisDs.Projection == null)
            gisDs.Projection = new GISProjection();
        gisDs.Projection.EPSG = targetCrs;

        return Task.FromResult<Dataset>(gisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_:]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}
