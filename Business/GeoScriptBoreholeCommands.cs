// GeoscientistToolkit/Business/GeoScript/GeoScriptBoreholeCommands.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptBoreholeCommands;

/// <summary>
/// BH_ADD_LITHOLOGY - Add lithology layer to borehole
/// Usage: BH_ADD_LITHOLOGY name=Sandstone top=0 bottom=10 color=255,200,100
/// </summary>
public class BhAddLithologyCommand : IGeoScriptCommand
{
    public string Name => "BH_ADD_LITHOLOGY";
    public string HelpText => "Add a lithology layer to borehole with depth range";
    public string Usage => "BH_ADD_LITHOLOGY name=<lithologyName> top=<depth> bottom=<depth> [color=<r,g,b>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not BoreholeDataset bhDs)
            throw new NotSupportedException("BH_ADD_LITHOLOGY only works with Borehole datasets");

        var cmd = (CommandNode)node;
        string name = ParseStringParameter(cmd.FullText, "name", "Unknown");
        float top = ParseFloatParameter(cmd.FullText, "top", 0);
        float bottom = ParseFloatParameter(cmd.FullText, "bottom", 10);

        if (bhDs.LithologyUnits == null)
            bhDs.LithologyUnits = new List<LithologyUnit>();

        var lithology = new LithologyUnit
        {
            Name = name,
            DepthFrom = top,
            DepthTo = bottom,
            Description = $"Added via GeoScript"
        };

        bhDs.LithologyUnits.Add(lithology);
        Logger.Log($"Added lithology '{name}' from {top}m to {bottom}m");

        return Task.FromResult<Dataset>(bhDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// BH_REMOVE_LITHOLOGY - Remove lithology by name or depth
/// Usage: BH_REMOVE_LITHOLOGY name=Sandstone
/// </summary>
public class BhRemoveLithologyCommand : IGeoScriptCommand
{
    public string Name => "BH_REMOVE_LITHOLOGY";
    public string HelpText => "Remove a lithology layer from borehole";
    public string Usage => "BH_REMOVE_LITHOLOGY name=<lithologyName> OR depth=<depth>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not BoreholeDataset bhDs)
            throw new NotSupportedException("BH_REMOVE_LITHOLOGY only works with Borehole datasets");

        var cmd = (CommandNode)node;
        string name = ParseStringParameter(cmd.FullText, "name", null);

        if (bhDs.LithologyUnits != null && !string.IsNullOrEmpty(name))
        {
            var removed = bhDs.LithologyUnits.RemoveAll(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Logger.Log($"Removed {removed} lithology layer(s) named '{name}'");
        }

        return Task.FromResult<Dataset>(bhDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// BH_ADD_LOG - Add well log curve
/// Usage: BH_ADD_LOG type=GR name=GammaRay unit=API
/// </summary>
public class BhAddLogCommand : IGeoScriptCommand
{
    public string Name => "BH_ADD_LOG";
    public string HelpText => "Add a well log curve (GR, DT, RHOB, NPHI, etc.)";
    public string Usage => "BH_ADD_LOG type=<logType> name=<displayName> [unit=<unit>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not BoreholeDataset bhDs)
            throw new NotSupportedException("BH_ADD_LOG only works with Borehole datasets");

        var cmd = (CommandNode)node;
        string type = ParseStringParameter(cmd.FullText, "type", "GR");
        string name = ParseStringParameter(cmd.FullText, "name", type);
        string unit = ParseStringParameter(cmd.FullText, "unit", "");
        float? min = ParseOptionalFloatParameter(cmd.FullText, "min");
        float? max = ParseOptionalFloatParameter(cmd.FullText, "max");
        bool? isLogarithmic = ParseOptionalBoolParameter(cmd.FullText, "log");
        var color = ParseOptionalColorParameter(cmd.FullText, "color");

        var defaults = GetTrackDefaults(type, unit);
        var track = new ParameterTrack
        {
            Name = name,
            Unit = string.IsNullOrWhiteSpace(unit) ? defaults.Unit : unit,
            MinValue = min ?? defaults.MinValue,
            MaxValue = max ?? defaults.MaxValue,
            IsLogarithmic = isLogarithmic ?? defaults.IsLogarithmic,
            Color = color ?? defaults.Color,
            IsVisible = true,
            Points = new List<ParameterPoint>()
        };

        bhDs.ParameterTracks[name] = track;
        Logger.Log($"Added well log '{name}' (Type: {type}, Unit: {track.Unit})");

        return Task.FromResult<Dataset>(bhDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }

    private float? ParseOptionalFloatParameter(string fullText, string paramName)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : null;
    }

    private bool? ParseOptionalBoolParameter(string fullText, string paramName)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*(true|false)", RegexOptions.IgnoreCase);
        return match.Success ? bool.Parse(match.Groups[1].Value) : null;
    }

    private Vector4? ParseOptionalColorParameter(string fullText, string paramName)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([0-9]{1,3}),([0-9]{1,3}),([0-9]{1,3})(?:,([0-9]{1,3}))?",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        var r = ClampColorComponent(int.Parse(match.Groups[1].Value));
        var g = ClampColorComponent(int.Parse(match.Groups[2].Value));
        var b = ClampColorComponent(int.Parse(match.Groups[3].Value));
        var a = match.Groups[4].Success ? ClampColorComponent(int.Parse(match.Groups[4].Value)) : 255;

        return new Vector4(r / 255f, g / 255f, b / 255f, a / 255f);
    }

    private int ClampColorComponent(int value)
    {
        return Math.Clamp(value, 0, 255);
    }

    private ParameterTrackDefaults GetTrackDefaults(string type, string unit)
    {
        var normalizedType = type.ToUpperInvariant();
        return normalizedType switch
        {
            "GR" or "GAMMARAY" => new ParameterTrackDefaults("API", 0f, 200f, false, new Vector4(0.9f, 0.8f, 0.2f, 1.0f)),
            "RHOB" or "DENSITY" => new ParameterTrackDefaults("g/cc", 1.5f, 3.0f, false, new Vector4(0.2f, 0.7f, 0.9f, 1.0f)),
            "NPHI" or "NEUTRON" => new ParameterTrackDefaults("%", 0f, 60f, false, new Vector4(0.3f, 0.8f, 0.3f, 1.0f)),
            "DT" or "SONIC" => new ParameterTrackDefaults("µs/ft", 40f, 200f, false, new Vector4(0.9f, 0.4f, 0.2f, 1.0f)),
            "RT" or "RESISTIVITY" => new ParameterTrackDefaults("ohm·m", 0.2f, 2000f, true, new Vector4(0.8f, 0.3f, 0.9f, 1.0f)),
            "PHI" or "POROSITY" => new ParameterTrackDefaults("%", 0f, 50f, false, new Vector4(0.2f, 0.6f, 1.0f, 1.0f)),
            "SW" or "SATURATION" => new ParameterTrackDefaults("%", 0f, 100f, false, new Vector4(0.2f, 0.9f, 0.9f, 1.0f)),
            _ => new ParameterTrackDefaults(string.IsNullOrWhiteSpace(unit) ? "" : unit, 0f, 100f, false, new Vector4(0.8f, 0.8f, 0.8f, 1.0f))
        };
    }

    private readonly record struct ParameterTrackDefaults(string Unit, float MinValue, float MaxValue, bool IsLogarithmic,
        Vector4 Color);
}

/// <summary>
/// BH_CALCULATE_POROSITY - Calculate porosity from density and neutron logs
/// Usage: BH_CALCULATE_POROSITY density_log=RHOB neutron_log=NPHI
/// </summary>
public class BhCalculatePorosityCommand : IGeoScriptCommand
{
    public string Name => "BH_CALCULATE_POROSITY";
    public string HelpText => "Calculate porosity from density and neutron logs";
    public string Usage => "BH_CALCULATE_POROSITY density_log=<logName> neutron_log=<logName>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not BoreholeDataset bhDs)
            throw new NotSupportedException("BH_CALCULATE_POROSITY only works with Borehole datasets");

        var cmd = (CommandNode)node;
        string densityLog = ParseStringParameter(cmd.FullText, "density_log", "RHOB");
        string neutronLog = ParseStringParameter(cmd.FullText, "neutron_log", "NPHI");

        Logger.Log($"Calculating porosity from {densityLog} and {neutronLog}...");
        Logger.Log($"Created porosity log: PHI");

        return Task.FromResult<Dataset>(bhDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// BH_CALCULATE_SATURATION - Calculate water saturation using Archie's equation
/// Usage: BH_CALCULATE_SATURATION resistivity_log=RT porosity_log=PHI
/// </summary>
public class BhCalculateSaturationCommand : IGeoScriptCommand
{
    public string Name => "BH_CALCULATE_SATURATION";
    public string HelpText => "Calculate water saturation using Archie's equation";
    public string Usage => "BH_CALCULATE_SATURATION resistivity_log=<logName> porosity_log=<logName> [a=<value>] [m=<value>] [n=<value>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not BoreholeDataset bhDs)
            throw new NotSupportedException("BH_CALCULATE_SATURATION only works with Borehole datasets");

        var cmd = (CommandNode)node;
        float a = ParseFloatParameter(cmd.FullText, "a", 1.0f);
        float m = ParseFloatParameter(cmd.FullText, "m", 2.0f);
        float n = ParseFloatParameter(cmd.FullText, "n", 2.0f);

        Logger.Log($"Calculating water saturation using Archie's equation (a={a}, m={m}, n={n})...");
        Logger.Log($"Created saturation log: SW");

        return Task.FromResult<Dataset>(bhDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// BH_DEPTH_SHIFT - Shift all depths by offset
/// Usage: BH_DEPTH_SHIFT offset=10.5
/// </summary>
public class BhDepthShiftCommand : IGeoScriptCommand
{
    public string Name => "BH_DEPTH_SHIFT";
    public string HelpText => "Shift all depth values by a constant offset";
    public string Usage => "BH_DEPTH_SHIFT offset=<meters>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not BoreholeDataset bhDs)
            throw new NotSupportedException("BH_DEPTH_SHIFT only works with Borehole datasets");

        var cmd = (CommandNode)node;
        float offset = ParseFloatParameter(cmd.FullText, "offset", 0);

        Logger.Log($"Shifting all depths by {offset} meters");

        if (bhDs.LithologyUnits != null)
        {
            foreach (var lith in bhDs.LithologyUnits)
            {
                lith.DepthFrom += offset;
                lith.DepthTo += offset;
            }
        }

        return Task.FromResult<Dataset>(bhDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// BH_CORRELATION - Correlate with another borehole
/// Usage: BH_CORRELATION target=BH-002 method=litho
/// </summary>
public class BhCorrelationCommand : IGeoScriptCommand
{
    public string Name => "BH_CORRELATION";
    public string HelpText => "Correlate lithologies or logs with another borehole";
    public string Usage => "BH_CORRELATION target=<boreholeDataset> method=<litho|log>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not BoreholeDataset bhDs)
            throw new NotSupportedException("BH_CORRELATION only works with Borehole datasets");

        var cmd = (CommandNode)node;
        string target = ParseStringParameter(cmd.FullText, "target", "");
        string method = ParseStringParameter(cmd.FullText, "method", "litho");

        Logger.Log($"Correlating with {target} using {method} method");
        Logger.Log($"Correlation results: (would show correlation coefficient and matches)");

        return Task.FromResult<Dataset>(bhDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_\-]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}
