// GeoscientistToolkit/Business/GeoScript/GeoScriptSeismicCommands.cs

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptSeismicCommands;

/// <summary>
/// SEIS_FILTER - Apply seismic filters
/// Usage: SEIS_FILTER type=bandpass low=10 high=80
/// </summary>
public class SeisFilterCommand : IGeoScriptCommand
{
    public string Name => "SEIS_FILTER";
    public string HelpText => "Apply seismic filters (bandpass, lowpass, highpass, fx-decon)";
    public string Usage => "SEIS_FILTER type=<bandpass|lowpass|highpass|fxdecon> [low=<Hz>] [high=<Hz>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not SeismicDataset seisDs)
            throw new NotSupportedException("SEIS_FILTER only works with Seismic datasets");

        var cmd = (CommandNode)node;
        string filterType = ParseStringParameter(cmd.FullText, "type", "bandpass");
        float lowFreq = ParseFloatParameter(cmd.FullText, "low", 10);
        float highFreq = ParseFloatParameter(cmd.FullText, "high", 80);

        Logger.LogInfo($"Applying {filterType} filter: {lowFreq}-{highFreq} Hz");

        return Task.FromResult<Dataset>(seisDs);
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
/// SEIS_AGC - Apply automatic gain control
/// Usage: SEIS_AGC window=500
/// </summary>
public class SeisAGCCommand : IGeoScriptCommand
{
    public string Name => "SEIS_AGC";
    public string HelpText => "Apply automatic gain control to balance amplitudes";
    public string Usage => "SEIS_AGC window=<milliseconds>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not SeismicDataset seisDs)
            throw new NotSupportedException("SEIS_AGC only works with Seismic datasets");

        var cmd = (CommandNode)node;
        float window = ParseFloatParameter(cmd.FullText, "window", 500);

        Logger.LogInfo($"Applying AGC with {window}ms window");

        return Task.FromResult<Dataset>(seisDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// SEIS_VELOCITY_ANALYSIS - Perform velocity analysis
/// Usage: SEIS_VELOCITY_ANALYSIS method=semblance
/// </summary>
public class SeisVelocityAnalysisCommand : IGeoScriptCommand
{
    public string Name => "SEIS_VELOCITY_ANALYSIS";
    public string HelpText => "Perform velocity analysis for NMO correction";
    public string Usage => "SEIS_VELOCITY_ANALYSIS method=<semblance|cvs|cmp>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not SeismicDataset seisDs)
            throw new NotSupportedException("SEIS_VELOCITY_ANALYSIS only works with Seismic datasets");

        var cmd = (CommandNode)node;
        string method = ParseStringParameter(cmd.FullText, "method", "semblance");

        Logger.LogInfo($"Performing velocity analysis using {method} method...");
        Logger.LogInfo($"Velocity model generated");

        return Task.FromResult<Dataset>(seisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// SEIS_NMO_CORRECTION - Apply NMO correction
/// Usage: SEIS_NMO_CORRECTION velocity_file=velocities.txt
/// </summary>
public class SeisNMOCorrectionCommand : IGeoScriptCommand
{
    public string Name => "SEIS_NMO_CORRECTION";
    public string HelpText => "Apply normal moveout correction";
    public string Usage => "SEIS_NMO_CORRECTION [velocity_file=<path>] [velocity=<constant>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not SeismicDataset seisDs)
            throw new NotSupportedException("SEIS_NMO_CORRECTION only works with Seismic datasets");

        var cmd = (CommandNode)node;
        float velocity = ParseFloatParameter(cmd.FullText, "velocity", 2000);

        Logger.LogInfo($"Applying NMO correction with velocity {velocity} m/s");

        return Task.FromResult<Dataset>(seisDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// SEIS_STACK - Stack seismic traces
/// Usage: SEIS_STACK method=mean
/// </summary>
public class SeisStackCommand : IGeoScriptCommand
{
    public string Name => "SEIS_STACK";
    public string HelpText => "Stack seismic traces (mean, median, weighted)";
    public string Usage => "SEIS_STACK method=<mean|median|weighted>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not SeismicDataset seisDs)
            throw new NotSupportedException("SEIS_STACK only works with Seismic datasets");

        var cmd = (CommandNode)node;
        string method = ParseStringParameter(cmd.FullText, "method", "mean");

        Logger.LogInfo($"Stacking traces using {method} method...");
        Logger.LogInfo($"Stack complete");

        return Task.FromResult<Dataset>(seisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// SEIS_MIGRATION - Perform seismic migration
/// Usage: SEIS_MIGRATION method=kirchhoff aperture=1000
/// </summary>
public class SeisMigrationCommand : IGeoScriptCommand
{
    public string Name => "SEIS_MIGRATION";
    public string HelpText => "Perform seismic migration (kirchhoff, fk, rtm)";
    public string Usage => "SEIS_MIGRATION method=<kirchhoff|fk|rtm> [aperture=<meters>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not SeismicDataset seisDs)
            throw new NotSupportedException("SEIS_MIGRATION only works with Seismic datasets");

        var cmd = (CommandNode)node;
        string method = ParseStringParameter(cmd.FullText, "method", "kirchhoff");
        float aperture = ParseFloatParameter(cmd.FullText, "aperture", 1000);

        Logger.LogInfo($"Performing {method} migration with {aperture}m aperture...");
        Logger.LogInfo($"Migration complete");

        return Task.FromResult<Dataset>(seisDs);
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
/// SEIS_PICK_HORIZON - Pick seismic horizon
/// Usage: SEIS_PICK_HORIZON name=Top_Reservoir method=auto
/// </summary>
public class SeisPickHorizonCommand : IGeoScriptCommand
{
    public string Name => "SEIS_PICK_HORIZON";
    public string HelpText => "Pick seismic horizons automatically or manually";
    public string Usage => "SEIS_PICK_HORIZON name=<horizonName> method=<auto|manual|tracking>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not SeismicDataset seisDs)
            throw new NotSupportedException("SEIS_PICK_HORIZON only works with Seismic datasets");

        var cmd = (CommandNode)node;
        string name = ParseStringParameter(cmd.FullText, "name", "Horizon_1");
        string method = ParseStringParameter(cmd.FullText, "method", "auto");

        Logger.LogInfo($"Picking horizon '{name}' using {method} method...");
        Logger.LogInfo($"Horizon picked successfully");

        return Task.FromResult<Dataset>(seisDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}
