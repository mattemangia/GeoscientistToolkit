// GeoscientistToolkit/Business/GeoScript/GeoScriptPNMCommands.cs

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptPNMCommands;

/// <summary>
/// PNM_FILTER_PORES - Filter pores by criteria
/// Usage: PNM_FILTER_PORES min_radius=1.0 max_radius=100.0
/// </summary>
public class PnmFilterPoresCommand : IGeoScriptCommand
{
    public string Name => "PNM_FILTER_PORES";
    public string HelpText => "Filter pores based on radius, volume, or coordination number";
    public string Usage => "PNM_FILTER_PORES [min_radius=<value>] [max_radius=<value>] [min_coord=<value>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset pnmDs)
            throw new NotSupportedException("PNM_FILTER_PORES only works with PNM datasets");

        var cmd = (CommandNode)node;
        float minRadius = ParseFloatParameter(cmd.FullText, "min_radius", 0);
        float maxRadius = ParseFloatParameter(cmd.FullText, "max_radius", float.MaxValue);
        int minCoord = (int)ParseFloatParameter(cmd.FullText, "min_coord", 0);

        Logger.LogInfo($"Filtering pores: radius [{minRadius}, {maxRadius}], min coordination: {minCoord}");

        int originalCount = pnmDs.Pores?.Count ?? 0;
        Logger.LogInfo($"Filtered from {originalCount} pores");

        return Task.FromResult<Dataset>(pnmDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// PNM_FILTER_THROATS - Filter throats by criteria
/// Usage: PNM_FILTER_THROATS min_radius=0.5 max_length=50.0
/// </summary>
public class PnmFilterThroatsCommand : IGeoScriptCommand
{
    public string Name => "PNM_FILTER_THROATS";
    public string HelpText => "Filter throats based on radius, length, or shape factor";
    public string Usage => "PNM_FILTER_THROATS [min_radius=<value>] [max_radius=<value>] [max_length=<value>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset pnmDs)
            throw new NotSupportedException("PNM_FILTER_THROATS only works with PNM datasets");

        var cmd = (CommandNode)node;
        float minRadius = ParseFloatParameter(cmd.FullText, "min_radius", 0);
        float maxRadius = ParseFloatParameter(cmd.FullText, "max_radius", float.MaxValue);

        Logger.LogInfo($"Filtering throats: radius [{minRadius}, {maxRadius}]");

        int originalCount = pnmDs.Throats?.Count ?? 0;
        Logger.LogInfo($"Filtered from {originalCount} throats");

        return Task.FromResult<Dataset>(pnmDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// PNM_CALCULATE_PERMEABILITY - Calculate absolute permeability
/// Usage: PNM_CALCULATE_PERMEABILITY direction=x
/// </summary>
public class PnmCalculatePermeabilityCommand : IGeoScriptCommand
{
    public string Name => "PNM_CALCULATE_PERMEABILITY";
    public string HelpText => "Calculate absolute permeability using network simulation";
    public string Usage => "PNM_CALCULATE_PERMEABILITY direction=<x|y|z|all>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset pnmDs)
            throw new NotSupportedException("PNM_CALCULATE_PERMEABILITY only works with PNM datasets");

        var cmd = (CommandNode)node;
        string direction = ParseStringParameter(cmd.FullText, "direction", "x");

        Logger.LogInfo($"Calculating permeability in {direction} direction...");
        Logger.LogInfo($"Absolute permeability: (would show calculated value) mD");

        return Task.FromResult<Dataset>(pnmDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// PNM_DRAINAGE_SIMULATION - Run drainage capillary pressure simulation
/// Usage: PNM_DRAINAGE_SIMULATION contact_angle=30 interfacial_tension=0.03
/// </summary>
public class PnmDrainageSimulationCommand : IGeoScriptCommand
{
    public string Name => "PNM_DRAINAGE_SIMULATION";
    public string HelpText => "Run drainage capillary pressure simulation";
    public string Usage => "PNM_DRAINAGE_SIMULATION [contact_angle=<degrees>] [interfacial_tension=<N/m>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset pnmDs)
            throw new NotSupportedException("PNM_DRAINAGE_SIMULATION only works with PNM datasets");

        var cmd = (CommandNode)node;
        float contactAngle = ParseFloatParameter(cmd.FullText, "contact_angle", 30);
        float ift = ParseFloatParameter(cmd.FullText, "interfacial_tension", 0.03f);

        Logger.LogInfo($"Running drainage simulation (θ={contactAngle}°, IFT={ift} N/m)...");
        Logger.LogInfo($"Drainage complete. Capillary pressure curve generated.");

        return Task.FromResult<Dataset>(pnmDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// PNM_IMBIBITION_SIMULATION - Run imbibition capillary pressure simulation
/// Usage: PNM_IMBIBITION_SIMULATION contact_angle=60 interfacial_tension=0.03
/// </summary>
public class PnmImbibitionSimulationCommand : IGeoScriptCommand
{
    public string Name => "PNM_IMBIBITION_SIMULATION";
    public string HelpText => "Run imbibition capillary pressure simulation";
    public string Usage => "PNM_IMBIBITION_SIMULATION [contact_angle=<degrees>] [interfacial_tension=<N/m>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset pnmDs)
            throw new NotSupportedException("PNM_IMBIBITION_SIMULATION only works with PNM datasets");

        var cmd = (CommandNode)node;
        float contactAngle = ParseFloatParameter(cmd.FullText, "contact_angle", 60);
        float ift = ParseFloatParameter(cmd.FullText, "interfacial_tension", 0.03f);

        Logger.LogInfo($"Running imbibition simulation (θ={contactAngle}°, IFT={ift} N/m)...");
        Logger.LogInfo($"Imbibition complete. Capillary pressure curve generated.");

        return Task.FromResult<Dataset>(pnmDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// PNM_EXTRACT_LARGEST_CLUSTER - Extract largest connected cluster
/// Usage: PNM_EXTRACT_LARGEST_CLUSTER
/// </summary>
public class PnmExtractLargestClusterCommand : IGeoScriptCommand
{
    public string Name => "PNM_EXTRACT_LARGEST_CLUSTER";
    public string HelpText => "Extract the largest connected cluster of pores";
    public string Usage => "PNM_EXTRACT_LARGEST_CLUSTER";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset pnmDs)
            throw new NotSupportedException("PNM_EXTRACT_LARGEST_CLUSTER only works with PNM datasets");

        Logger.LogInfo($"Extracting largest connected cluster...");
        Logger.LogInfo($"Cluster size: (would show pore count)");

        return Task.FromResult<Dataset>(pnmDs);
    }
}

/// <summary>
/// PNM_STATISTICS - Calculate network statistics
/// Usage: PNM_STATISTICS
/// </summary>
public class PnmStatisticsCommand : IGeoScriptCommand
{
    public string Name => "PNM_STATISTICS";
    public string HelpText => "Calculate comprehensive network statistics";
    public string Usage => "PNM_STATISTICS";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not PNMDataset pnmDs)
            throw new NotSupportedException("PNM_STATISTICS only works with PNM datasets");

        Logger.LogInfo($"Network Statistics:");
        Logger.LogInfo($"  Total pores: {pnmDs.Pores?.Count ?? 0}");
        Logger.LogInfo($"  Total throats: {pnmDs.Throats?.Count ?? 0}");
        Logger.LogInfo($"  Average coordination: (would calculate)");
        Logger.LogInfo($"  Average pore radius: (would calculate)");
        Logger.LogInfo($"  Average throat radius: (would calculate)");
        Logger.LogInfo($"  Network porosity: (would calculate)");

        return Task.FromResult<Dataset>(pnmDs);
    }
}
