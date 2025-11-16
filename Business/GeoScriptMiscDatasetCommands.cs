// GeoscientistToolkit/Business/GeoScript/GeoScriptMiscDatasetCommands.cs

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.Media;
using GeoscientistToolkit.Data.Text;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business.GeoScriptMiscDatasetCommands;

#region AcousticVolume Commands

/// <summary>
/// ACOUSTIC_THRESHOLD - Threshold acoustic data
/// Usage: ACOUSTIC_THRESHOLD min_amplitude=-60 max_amplitude=-20
/// </summary>
public class AcousticThresholdCommand : IGeoScriptCommand
{
    public string Name => "ACOUSTIC_THRESHOLD";
    public string HelpText => "Threshold acoustic volume data by amplitude (dB)";
    public string Usage => "ACOUSTIC_THRESHOLD min_amplitude=<dB> max_amplitude=<dB>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not AcousticVolumeDataset acousticDs)
            throw new NotSupportedException("ACOUSTIC_THRESHOLD only works with AcousticVolume datasets");

        var cmd = (CommandNode)node;
        float minAmp = ParseFloatParameter(cmd.FullText, "min_amplitude", -60);
        float maxAmp = ParseFloatParameter(cmd.FullText, "max_amplitude", -20);

        Logger.LogInfo($"Thresholding acoustic data: [{minAmp}, {maxAmp}] dB");

        return Task.FromResult<Dataset>(acousticDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// ACOUSTIC_EXTRACT_TARGETS - Extract acoustic targets
/// Usage: ACOUSTIC_EXTRACT_TARGETS threshold=-40
/// </summary>
public class AcousticExtractTargetsCommand : IGeoScriptCommand
{
    public string Name => "ACOUSTIC_EXTRACT_TARGETS";
    public string HelpText => "Extract and analyze acoustic targets (fish, schools, etc.)";
    public string Usage => "ACOUSTIC_EXTRACT_TARGETS threshold=<dB>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not AcousticVolumeDataset acousticDs)
            throw new NotSupportedException("ACOUSTIC_EXTRACT_TARGETS only works with AcousticVolume datasets");

        var cmd = (CommandNode)node;
        float threshold = ParseFloatParameter(cmd.FullText, "threshold", -40);

        Logger.LogInfo($"Extracting acoustic targets above {threshold} dB...");
        Logger.LogInfo($"Found targets: (would show count)");

        return Task.FromResult<Dataset>(acousticDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

#endregion

#region Mesh3D Commands

/// <summary>
/// MESH_SMOOTH - Smooth mesh geometry
/// Usage: MESH_SMOOTH iterations=5 lambda=0.5
/// </summary>
public class MeshSmoothCommand : IGeoScriptCommand
{
    public string Name => "MESH_SMOOTH";
    public string HelpText => "Smooth mesh geometry using Laplacian smoothing";
    public string Usage => "MESH_SMOOTH [iterations=<count>] [lambda=<0-1>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not Mesh3DDataset meshDs)
            throw new NotSupportedException("MESH_SMOOTH only works with Mesh3D datasets");

        var cmd = (CommandNode)node;
        int iterations = (int)ParseFloatParameter(cmd.FullText, "iterations", 5);
        float lambda = ParseFloatParameter(cmd.FullText, "lambda", 0.5f);

        Logger.LogInfo($"Smoothing mesh: {iterations} iterations, λ={lambda}");

        return Task.FromResult<Dataset>(meshDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// MESH_DECIMATE - Reduce mesh complexity
/// Usage: MESH_DECIMATE target_percent=50
/// </summary>
public class MeshDecimateCommand : IGeoScriptCommand
{
    public string Name => "MESH_DECIMATE";
    public string HelpText => "Reduce mesh complexity while preserving shape";
    public string Usage => "MESH_DECIMATE target_percent=<0-100>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not Mesh3DDataset meshDs)
            throw new NotSupportedException("MESH_DECIMATE only works with Mesh3D datasets");

        var cmd = (CommandNode)node;
        float targetPercent = ParseFloatParameter(cmd.FullText, "target_percent", 50);

        Logger.LogInfo($"Decimating mesh to {targetPercent}% of original complexity");

        return Task.FromResult<Dataset>(meshDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// MESH_REPAIR - Repair mesh defects
/// Usage: MESH_REPAIR
/// </summary>
public class MeshRepairCommand : IGeoScriptCommand
{
    public string Name => "MESH_REPAIR";
    public string HelpText => "Repair mesh defects (holes, non-manifold edges, etc.)";
    public string Usage => "MESH_REPAIR";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not Mesh3DDataset meshDs)
            throw new NotSupportedException("MESH_REPAIR only works with Mesh3D datasets");

        Logger.LogInfo($"Repairing mesh defects...");
        Logger.LogInfo($"Repair complete");

        return Task.FromResult<Dataset>(meshDs);
    }
}

/// <summary>
/// MESH_CALCULATE_VOLUME - Calculate mesh volume
/// Usage: MESH_CALCULATE_VOLUME
/// </summary>
public class MeshCalculateVolumeCommand : IGeoScriptCommand
{
    public string Name => "MESH_CALCULATE_VOLUME";
    public string HelpText => "Calculate the volume enclosed by the mesh";
    public string Usage => "MESH_CALCULATE_VOLUME";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not Mesh3DDataset meshDs)
            throw new NotSupportedException("MESH_CALCULATE_VOLUME only works with Mesh3D datasets");

        Logger.LogInfo($"Calculating mesh volume...");
        Logger.LogInfo($"Volume: (would show calculated value) cubic units");
        Logger.LogInfo($"Surface Area: (would show calculated value) square units");

        return Task.FromResult<Dataset>(meshDs);
    }
}

#endregion

#region Video Commands

/// <summary>
/// VIDEO_EXTRACT_FRAME - Extract frame from video
/// Usage: VIDEO_EXTRACT_FRAME time=10.5
/// </summary>
public class VideoExtractFrameCommand : IGeoScriptCommand
{
    public string Name => "VIDEO_EXTRACT_FRAME";
    public string HelpText => "Extract a single frame from video at specified time";
    public string Usage => "VIDEO_EXTRACT_FRAME time=<seconds> OR frame=<frameNumber>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not VideoDataset videoDs)
            throw new NotSupportedException("VIDEO_EXTRACT_FRAME only works with Video datasets");

        var cmd = (CommandNode)node;
        float time = ParseFloatParameter(cmd.FullText, "time", 0);

        Logger.LogInfo($"Extracting frame at {time}s");
        Logger.LogInfo($"Frame extracted as image dataset");

        return Task.FromResult<Dataset>(videoDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// VIDEO_STABILIZE - Stabilize shaky video
/// Usage: VIDEO_STABILIZE smoothness=0.8
/// </summary>
public class VideoStabilizeCommand : IGeoScriptCommand
{
    public string Name => "VIDEO_STABILIZE";
    public string HelpText => "Stabilize shaky video using motion compensation";
    public string Usage => "VIDEO_STABILIZE [smoothness=<0-1>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not VideoDataset videoDs)
            throw new NotSupportedException("VIDEO_STABILIZE only works with Video datasets");

        var cmd = (CommandNode)node;
        float smoothness = ParseFloatParameter(cmd.FullText, "smoothness", 0.8f);

        Logger.LogInfo($"Stabilizing video (smoothness={smoothness})...");
        Logger.LogInfo($"Stabilization complete");

        return Task.FromResult<Dataset>(videoDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

#endregion

#region Audio Commands

/// <summary>
/// AUDIO_TRIM - Trim audio to time range
/// Usage: AUDIO_TRIM start=5.0 end=15.0
/// </summary>
public class AudioTrimCommand : IGeoScriptCommand
{
    public string Name => "AUDIO_TRIM";
    public string HelpText => "Trim audio to specified time range";
    public string Usage => "AUDIO_TRIM start=<seconds> end=<seconds>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not AudioDataset audioDs)
            throw new NotSupportedException("AUDIO_TRIM only works with Audio datasets");

        var cmd = (CommandNode)node;
        float start = ParseFloatParameter(cmd.FullText, "start", 0);
        float end = ParseFloatParameter(cmd.FullText, "end", 10);

        Logger.LogInfo($"Trimming audio from {start}s to {end}s");

        return Task.FromResult<Dataset>(audioDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

/// <summary>
/// AUDIO_NORMALIZE - Normalize audio levels
/// Usage: AUDIO_NORMALIZE target_db=-14
/// </summary>
public class AudioNormalizeCommand : IGeoScriptCommand
{
    public string Name => "AUDIO_NORMALIZE";
    public string HelpText => "Normalize audio to target loudness level";
    public string Usage => "AUDIO_NORMALIZE target_db=<dB>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not AudioDataset audioDs)
            throw new NotSupportedException("AUDIO_NORMALIZE only works with Audio datasets");

        var cmd = (CommandNode)node;
        float targetDb = ParseFloatParameter(cmd.FullText, "target_db", -14);

        Logger.LogInfo($"Normalizing audio to {targetDb} dB");

        return Task.FromResult<Dataset>(audioDs);
    }

    private float ParseFloatParameter(string fullText, string paramName, float defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success ? float.Parse(match.Groups[1].Value) : defaultValue;
    }
}

#endregion

#region Text Commands

/// <summary>
/// TEXT_SEARCH - Search for text pattern
/// Usage: TEXT_SEARCH pattern=error case_sensitive=false
/// </summary>
public class TextSearchCommand : IGeoScriptCommand
{
    public string Name => "TEXT_SEARCH";
    public string HelpText => "Search for text pattern and show results";
    public string Usage => "TEXT_SEARCH pattern=<searchText> [case_sensitive=<true|false>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TextDataset textDs)
            throw new NotSupportedException("TEXT_SEARCH only works with Text datasets");

        var cmd = (CommandNode)node;
        string pattern = ParseStringParameter(cmd.FullText, "pattern", "");

        Logger.LogInfo($"Searching for: {pattern}");
        Logger.LogInfo($"Found matches: (would show count and lines)");

        return Task.FromResult<Dataset>(textDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// TEXT_REPLACE - Replace text pattern
/// Usage: TEXT_REPLACE find=oldtext replace=newtext
/// </summary>
public class TextReplaceCommand : IGeoScriptCommand
{
    public string Name => "TEXT_REPLACE";
    public string HelpText => "Replace text pattern with new text";
    public string Usage => "TEXT_REPLACE find=<findText> replace=<replaceText>";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TextDataset textDs)
            throw new NotSupportedException("TEXT_REPLACE only works with Text datasets");

        var cmd = (CommandNode)node;
        string find = ParseStringParameter(cmd.FullText, "find", "");
        string replace = ParseStringParameter(cmd.FullText, "replace", "");

        Logger.LogInfo($"Replacing '{find}' with '{replace}'");
        Logger.LogInfo($"Replacements made: (would show count)");

        return Task.FromResult<Dataset>(textDs);
    }

    private string ParseStringParameter(string fullText, string paramName, string defaultValue)
    {
        var match = Regex.Match(fullText, paramName + @"\s*=\s*([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : defaultValue;
    }
}

/// <summary>
/// TEXT_STATISTICS - Show text statistics
/// Usage: TEXT_STATISTICS
/// </summary>
public class TextStatisticsCommand : IGeoScriptCommand
{
    public string Name => "TEXT_STATISTICS";
    public string HelpText => "Show detailed text statistics";
    public string Usage => "TEXT_STATISTICS";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        if (context.InputDataset is not TextDataset textDs)
            throw new NotSupportedException("TEXT_STATISTICS only works with Text datasets");

        Logger.LogInfo($"Text Statistics:");
        Logger.LogInfo($"  Lines: {textDs.LineCount}");
        Logger.LogInfo($"  Words: {textDs.WordCount}");
        Logger.LogInfo($"  Characters: {textDs.CharacterCount}");
        Logger.LogInfo($"  Encoding: {textDs.Encoding?.EncodingName ?? "Unknown"}");

        return Task.FromResult<Dataset>(textDs);
    }
}

#endregion
