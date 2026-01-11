// GeoscientistToolkit/Business/GeoScript/GeoScriptSeismicCubeCommands.cs

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.GeoScript;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Seismic;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Util;
using static GeoscientistToolkit.Business.GeoScriptSeismicCommands.SeismicCubeCommandHelpers;

namespace GeoscientistToolkit.Business.GeoScriptSeismicCommands;

public class CubeCreateCommand : IGeoScriptCommand
{
    public string Name => "CUBE_CREATE";
    public string HelpText => "Create a seismic cube dataset";
    public string Usage => "CUBE_CREATE name=\"CubeName\" [survey=\"SurveyName\"] [project=\"ProjectName\"]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var name = GetStringParameter(cmd, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("CUBE_CREATE requires a cube name.");

        var survey = GetStringParameter(cmd, "survey") ?? "";
        var project = GetStringParameter(cmd, "project") ?? "";

        var cube = new SeismicCubeDataset(name, "")
        {
            SurveyName = survey,
            ProjectName = project,
            CreationDate = DateTime.Now
        };

        AddDatasetToProjectAndContext(context, cube);
        Logger.Log($"[GeoScript] Created seismic cube '{cube.Name}'");

        return Task.FromResult<Dataset>(cube);
    }
}

public class CubeAddLineCommand : IGeoScriptCommand
{
    public string Name => "CUBE_ADD_LINE";
    public string HelpText => "Add a seismic line to a cube";
    public string Usage => "CUBE_ADD_LINE cube=\"CubeName\" line=\"LineDataset\" [start_x=<x> start_y=<y> end_x=<x> end_y=<y>] [trace_spacing=<m>] [azimuth=<deg>] [use_headers=true]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var cube = ResolveCube(context, cmd);
        var lineName = GetStringParameter(cmd, "line");
        if (string.IsNullOrWhiteSpace(lineName))
            throw new ArgumentException("CUBE_ADD_LINE requires line=<datasetName>.");

        var lineDataset = ResolveDatasetByName(context, lineName) as SeismicDataset;
        if (lineDataset == null)
            throw new ArgumentException($"CUBE_ADD_LINE could not find seismic dataset '{lineName}'.");

        var useHeaders = GetBoolParameter(cmd, "use_headers", false);

        LineGeometry geometry;
        if (HasCoordinateParameters(cmd))
        {
            geometry = BuildGeometryFromCoordinates(cmd, lineDataset.GetTraceCount());
        }
        else if (useHeaders || CanExtractGeometryFromHeaders(lineDataset))
        {
            geometry = ExtractGeometryFromHeaders(lineDataset);
        }
        else
        {
            throw new ArgumentException("CUBE_ADD_LINE requires geometry parameters or use_headers=true with valid trace headers.");
        }

        cube.AddLine(lineDataset, geometry);

        return Task.FromResult<Dataset>(cube);
    }
}

public class CubeAddPerpendicularCommand : IGeoScriptCommand
{
    public string Name => "CUBE_ADD_PERPENDICULAR";
    public string HelpText => "Add a perpendicular seismic line to an existing cube line";
    public string Usage => "CUBE_ADD_PERPENDICULAR cube=\"CubeName\" base=\"LineName\" trace=<index> line=\"CrosslineDataset\"";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var cube = ResolveCube(context, cmd);
        var baseLineName = GetStringParameter(cmd, "base");
        var lineName = GetStringParameter(cmd, "line");
        var traceIndex = GetIntParameter(cmd, "trace", -1);

        if (string.IsNullOrWhiteSpace(baseLineName))
            throw new ArgumentException("CUBE_ADD_PERPENDICULAR requires base=<lineName>.");
        if (string.IsNullOrWhiteSpace(lineName))
            throw new ArgumentException("CUBE_ADD_PERPENDICULAR requires line=<datasetName>.");
        if (traceIndex < 0)
            throw new ArgumentException("CUBE_ADD_PERPENDICULAR requires trace=<index>.");

        var baseLine = cube.Lines.FirstOrDefault(l =>
            string.Equals(l.Name, baseLineName, StringComparison.OrdinalIgnoreCase));
        if (baseLine == null)
            throw new ArgumentException($"Base line '{baseLineName}' not found in cube '{cube.Name}'.");

        var lineDataset = ResolveDatasetByName(context, lineName) as SeismicDataset;
        if (lineDataset == null)
            throw new ArgumentException($"CUBE_ADD_PERPENDICULAR could not find seismic dataset '{lineName}'.");

        cube.AddPerpendicularLine(lineDataset, baseLine.Id, traceIndex);
        return Task.FromResult<Dataset>(cube);
    }
}

public class CubeDetectIntersectionsCommand : IGeoScriptCommand
{
    public string Name => "CUBE_DETECT_INTERSECTIONS";
    public string HelpText => "Detect intersections between cube lines";
    public string Usage => "CUBE_DETECT_INTERSECTIONS cube=\"CubeName\"";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cube = ResolveCube(context, (CommandNode)node);
        cube.DetectIntersections();
        return Task.FromResult<Dataset>(cube);
    }
}

public class CubeNormalizeCommand : IGeoScriptCommand
{
    public string Name => "CUBE_NORMALIZE";
    public string HelpText => "Apply normalization at cube line intersections";
    public string Usage => "CUBE_NORMALIZE cube=\"CubeName\"";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cube = ResolveCube(context, (CommandNode)node);
        cube.ApplyNormalization();
        return Task.FromResult<Dataset>(cube);
    }
}

public class CubeSetNormalizationCommand : IGeoScriptCommand
{
    public string Name => "CUBE_SET_NORMALIZATION";
    public string HelpText => "Configure cube normalization settings";
    public string Usage => "CUBE_SET_NORMALIZATION cube=\"CubeName\" [normalize_amplitude=true] [amplitude_method=<rms|mean|peak|median|balanced>] [match_frequency=true] [frequency_low=<Hz>] [frequency_high=<Hz>] [match_phase=true] [transition_traces=<count>] [window_traces=<count>] [window_ms=<ms>] [smooth_transitions=true]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var cube = ResolveCube(context, cmd);

        var settings = cube.NormalizationSettings;
        settings.NormalizeAmplitude = GetBoolParameter(cmd, "normalize_amplitude", settings.NormalizeAmplitude);
        settings.MatchFrequency = GetBoolParameter(cmd, "match_frequency", settings.MatchFrequency);
        settings.MatchPhase = GetBoolParameter(cmd, "match_phase", settings.MatchPhase);
        settings.SmoothTransitions = GetBoolParameter(cmd, "smooth_transitions", settings.SmoothTransitions);

        var amplitudeMethod = GetStringParameter(cmd, "amplitude_method");
        if (!string.IsNullOrWhiteSpace(amplitudeMethod) &&
            Enum.TryParse<AmplitudeNormalizationMethod>(amplitudeMethod, true, out var method))
        {
            settings.AmplitudeMethod = method;
        }

        settings.TargetFrequencyLow = GetFloatParameter(cmd, "frequency_low", settings.TargetFrequencyLow);
        settings.TargetFrequencyHigh = GetFloatParameter(cmd, "frequency_high", settings.TargetFrequencyHigh);
        settings.TransitionZoneTraces = GetIntParameter(cmd, "transition_traces", settings.TransitionZoneTraces);
        settings.MatchingWindowTraces = GetIntParameter(cmd, "window_traces", settings.MatchingWindowTraces);
        settings.MatchingWindowMs = GetFloatParameter(cmd, "window_ms", settings.MatchingWindowMs);

        Logger.Log($"[GeoScript] Updated normalization settings for cube '{cube.Name}'.");
        return Task.FromResult<Dataset>(cube);
    }
}

public class CubeBuildVolumeCommand : IGeoScriptCommand
{
    public string Name => "CUBE_BUILD_VOLUME";
    public string HelpText => "Build the regularized 3D volume for a cube";
    public string Usage => "CUBE_BUILD_VOLUME cube=\"CubeName\" [inline_count=<n>] [crossline_count=<n>] [sample_count=<n>] [inline_spacing=<m>] [crossline_spacing=<m>] [sample_interval=<ms>]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var cube = ResolveCube(context, cmd);

        var grid = cube.GridParameters;
        grid.InlineCount = GetIntParameter(cmd, "inline_count", grid.InlineCount);
        grid.CrosslineCount = GetIntParameter(cmd, "crossline_count", grid.CrosslineCount);
        grid.SampleCount = GetIntParameter(cmd, "sample_count", grid.SampleCount);
        grid.InlineSpacing = GetFloatParameter(cmd, "inline_spacing", grid.InlineSpacing);
        grid.CrosslineSpacing = GetFloatParameter(cmd, "crossline_spacing", grid.CrosslineSpacing);
        grid.SampleInterval = GetFloatParameter(cmd, "sample_interval", grid.SampleInterval);

        cube.BuildRegularizedVolume();
        return Task.FromResult<Dataset>(cube);
    }
}

public class CubeExportGisCommand : IGeoScriptCommand
{
    public string Name => "CUBE_EXPORT_GIS";
    public string HelpText => "Export a cube to a Subsurface GIS dataset";
    public string Usage => "CUBE_EXPORT_GIS cube=\"CubeName\" output=\"SubsurfaceGISName\"";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var cube = ResolveCube(context, cmd);
        var outputName = GetStringParameter(cmd, "output") ?? $"{cube.Name}_SubsurfaceGIS";

        var exporter = new SeismicCubeGISExporter(cube);
        var subsurface = exporter.ExportToSubsurfaceGIS(outputName);

        AddDatasetToProjectAndContext(context, subsurface);
        return Task.FromResult<Dataset>(subsurface);
    }
}

public class CubeExportSliceCommand : IGeoScriptCommand
{
    public string Name => "CUBE_EXPORT_SLICE";
    public string HelpText => "Export a cube time slice as a GIS raster dataset";
    public string Usage => "CUBE_EXPORT_SLICE cube=\"CubeName\" time=<ms> [output=\"SliceName\"]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var cube = ResolveCube(context, cmd);
        var timeMs = GetFloatParameter(cmd, "time", -1f);
        if (timeMs <= 0)
            throw new ArgumentException("CUBE_EXPORT_SLICE requires time=<ms>.");

        var outputName = GetStringParameter(cmd, "output") ?? $"{cube.Name}_T{timeMs:0}ms";
        var exporter = new SeismicCubeGISExporter(cube);
        var layer = exporter.ExportTimeSliceAsRaster(timeMs, outputName);
        if (layer == null)
            throw new InvalidOperationException("Failed to export time slice.");

        var gisDataset = new GISDataset(outputName, "")
        {
            Bounds = layer.Bounds
        };
        gisDataset.Layers.Clear();
        gisDataset.Layers.Add(layer);

        AddDatasetToProjectAndContext(context, gisDataset);
        return Task.FromResult<Dataset>(gisDataset);
    }
}

public class CubeStatisticsCommand : IGeoScriptCommand
{
    public string Name => "CUBE_STATISTICS";
    public string HelpText => "Summarize cube statistics as a table";
    public string Usage => "CUBE_STATISTICS cube=\"CubeName\" [output=\"StatsName\"]";

    public Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        var cmd = (CommandNode)node;
        var cube = ResolveCube(context, cmd);
        var outputName = GetStringParameter(cmd, "output") ?? $"{cube.Name}_Stats";

        var table = BuildStatisticsTable(cube, outputName);
        AddDatasetToProjectAndContext(context, table);
        return Task.FromResult<Dataset>(table);
    }

    private static TableDataset BuildStatisticsTable(SeismicCubeDataset cube, string name)
    {
        var dataTable = new DataTable(name);
        dataTable.Columns.Add("Metric", typeof(string));
        dataTable.Columns.Add("Value", typeof(string));

        dataTable.Rows.Add("Lines", cube.Lines.Count.ToString(CultureInfo.InvariantCulture));
        dataTable.Rows.Add("Intersections", cube.Intersections.Count.ToString(CultureInfo.InvariantCulture));
        dataTable.Rows.Add("Packages", cube.Packages.Count.ToString(CultureInfo.InvariantCulture));
        dataTable.Rows.Add("VolumeBuilt", (cube.RegularizedVolume != null).ToString());
        dataTable.Rows.Add("InlineCount", cube.GridParameters.InlineCount.ToString(CultureInfo.InvariantCulture));
        dataTable.Rows.Add("CrosslineCount", cube.GridParameters.CrosslineCount.ToString(CultureInfo.InvariantCulture));
        dataTable.Rows.Add("SampleCount", cube.GridParameters.SampleCount.ToString(CultureInfo.InvariantCulture));
        dataTable.Rows.Add("InlineSpacing", cube.GridParameters.InlineSpacing.ToString(CultureInfo.InvariantCulture));
        dataTable.Rows.Add("CrosslineSpacing", cube.GridParameters.CrosslineSpacing.ToString(CultureInfo.InvariantCulture));
        dataTable.Rows.Add("SampleInterval", cube.GridParameters.SampleInterval.ToString(CultureInfo.InvariantCulture));

        return new TableDataset(name, dataTable);
    }
}

internal static class SeismicCubeCommandHelpers
{
    public static SeismicCubeDataset ResolveCube(GeoScriptContext context, CommandNode cmd)
    {
        var cubeName = GetStringParameter(cmd, "cube");
        if (!string.IsNullOrWhiteSpace(cubeName))
        {
            var cube = ResolveDatasetByName(context, cubeName) as SeismicCubeDataset;
            if (cube == null)
                throw new ArgumentException($"Cube dataset '{cubeName}' was not found.");
            return cube;
        }

        if (context.InputDataset is SeismicCubeDataset inputCube)
            return inputCube;

        throw new ArgumentException("No seismic cube dataset specified or active.");
    }

    public static void AddDatasetToProjectAndContext(GeoScriptContext context, Dataset dataset)
    {
        ProjectManager.Instance.AddDataset(dataset);

        if (context.AvailableDatasets == null)
            context.AvailableDatasets = new Dictionary<string, Dataset>();

        context.AvailableDatasets[dataset.Name] = dataset;
    }

    public static Dataset ResolveDatasetByName(GeoScriptContext context, string name)
    {
        if (context.AvailableDatasets != null)
        {
            if (context.AvailableDatasets.TryGetValue(name, out var dataset))
                return dataset;

            var match = context.AvailableDatasets.FirstOrDefault(
                kvp => string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(match.Key))
                return match.Value;
        }

        return ProjectManager.Instance.LoadedDatasets
            .FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static bool CanExtractGeometryFromHeaders(SeismicDataset dataset)
    {
        return dataset.SegyData?.Traces != null && dataset.SegyData.Traces.Count >= 2;
    }

    public static LineGeometry ExtractGeometryFromHeaders(SeismicDataset dataset)
    {
        if (!CanExtractGeometryFromHeaders(dataset))
        {
            throw new InvalidOperationException("Insufficient trace headers to derive line geometry.");
        }

        var traces = dataset.SegyData!.Traces;
        var firstTrace = traces[0];
        var lastTrace = traces[traces.Count - 1];

        var (x1, y1) = firstTrace.GetScaledSourceCoordinates();
        var (x2, y2) = lastTrace.GetScaledSourceCoordinates();

        if (Math.Abs(x1) < 1e-6 && Math.Abs(y1) < 1e-6)
        {
            x1 = firstTrace.CdpX;
            y1 = firstTrace.CdpY;
            x2 = lastTrace.CdpX;
            y2 = lastTrace.CdpY;
        }

        var geometry = new LineGeometry
        {
            StartPoint = new Vector3((float)x1, (float)y1, 0f),
            EndPoint = new Vector3((float)x2, (float)y2, 0f)
        };

        if (traces.Count > 1)
            geometry.TraceSpacing = geometry.Length / (traces.Count - 1);

        float dx = geometry.EndPoint.X - geometry.StartPoint.X;
        float dy = geometry.EndPoint.Y - geometry.StartPoint.Y;
        geometry.Azimuth = MathF.Atan2(dx, dy) * 180f / MathF.PI;
        if (geometry.Azimuth < 0) geometry.Azimuth += 360f;

        return geometry;
    }

    public static LineGeometry BuildGeometryFromCoordinates(CommandNode cmd, int traceCount)
    {
        var startX = GetFloatParameter(cmd, "start_x", float.NaN);
        var startY = GetFloatParameter(cmd, "start_y", float.NaN);
        var endX = GetFloatParameter(cmd, "end_x", float.NaN);
        var endY = GetFloatParameter(cmd, "end_y", float.NaN);
        var traceSpacing = GetFloatParameter(cmd, "trace_spacing", 12.5f);
        var azimuth = GetFloatParameter(cmd, "azimuth", float.NaN);

        if (float.IsNaN(startX) || float.IsNaN(startY) || float.IsNaN(endX) || float.IsNaN(endY))
            throw new ArgumentException("CUBE_ADD_LINE requires start_x/start_y/end_x/end_y parameters.");

        var geometry = new LineGeometry
        {
            StartPoint = new Vector3(startX, startY, 0f),
            EndPoint = new Vector3(endX, endY, 0f),
            TraceSpacing = traceSpacing
        };

        if (float.IsNaN(azimuth))
        {
            float dx = geometry.EndPoint.X - geometry.StartPoint.X;
            float dy = geometry.EndPoint.Y - geometry.StartPoint.Y;
            geometry.Azimuth = MathF.Atan2(dx, dy) * 180f / MathF.PI;
            if (geometry.Azimuth < 0) geometry.Azimuth += 360f;
        }
        else
        {
            geometry.Azimuth = azimuth;
        }

        if (traceCount > 1 && geometry.TraceSpacing <= 0)
            geometry.TraceSpacing = geometry.Length / (traceCount - 1);

        return geometry;
    }

    public static bool HasCoordinateParameters(CommandNode cmd)
    {
        return HasParameter(cmd, "start_x") || HasParameter(cmd, "start_y") ||
               HasParameter(cmd, "end_x") || HasParameter(cmd, "end_y");
    }

    public static bool HasParameter(CommandNode cmd, string name)
    {
        if (cmd.Parameters.ContainsKey(name))
            return true;

        return Regex.IsMatch(cmd.FullText, name + @"\s*=", RegexOptions.IgnoreCase);
    }

    public static string GetStringParameter(CommandNode cmd, string name)
    {
        if (cmd.Parameters.TryGetValue(name, out var value))
            return value;

        var match = Regex.Match(cmd.FullText, name + @"\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value;

        match = Regex.Match(cmd.FullText, name + @"\s*=\s*([a-zA-Z0-9_\\.\\-]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static int GetIntParameter(CommandNode cmd, string name, int defaultValue)
    {
        if (cmd.Parameters.TryGetValue(name, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        var match = Regex.Match(cmd.FullText, name + @"\s*=\s*([-+]?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : defaultValue;
    }

    public static float GetFloatParameter(CommandNode cmd, string name, float defaultValue)
    {
        if (cmd.Parameters.TryGetValue(name, out var value) &&
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        var match = Regex.Match(cmd.FullText, name + @"\s*=\s*([-+]?[0-9]*\.?[0-9]+)", RegexOptions.IgnoreCase);
        return match.Success && float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : defaultValue;
    }

    public static bool GetBoolParameter(CommandNode cmd, string name, bool defaultValue)
    {
        if (cmd.Parameters.TryGetValue(name, out var value))
        {
            if (bool.TryParse(value, out var parsed))
                return parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                return intValue != 0;
        }

        var match = Regex.Match(cmd.FullText, name + @"\s*=\s*(true|false|0|1)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return defaultValue;

        var raw = match.Groups[1].Value;
        if (bool.TryParse(raw, out var boolValue))
            return boolValue;
        return raw != "0";
    }
}
