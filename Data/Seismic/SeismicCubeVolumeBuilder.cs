// GeoscientistToolkit/Data/Seismic/SeismicCubeVolumeBuilder.cs

using System.Numerics;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Data.Seismic;

/// <summary>
/// Builds a regularized 3D seismic volume from multiple intersecting 2D seismic lines.
/// Uses various interpolation methods to fill the volume between lines.
/// </summary>
public class SeismicCubeVolumeBuilder
{
    private readonly SeismicCubeDataset _cube;
    private readonly CubeGridParameters _params;
    private readonly CubeBounds _bounds;

    public SeismicCubeVolumeBuilder(SeismicCubeDataset cube)
    {
        _cube = cube;
        _params = cube.GridParameters;
        _bounds = cube.Bounds;
    }

    /// <summary>
    /// Build the regularized 3D volume
    /// </summary>
    public float[,,] BuildVolume()
    {
        Logger.Log($"[VolumeBuilder] Building {_params.InlineCount}x{_params.CrosslineCount}x{_params.SampleCount} volume");

        // Calculate grid parameters based on bounds
        CalculateGridParameters();

        var volume = new float[_params.InlineCount, _params.CrosslineCount, _params.SampleCount];

        // First pass: project line data onto the grid
        var contributionCount = new int[_params.InlineCount, _params.CrosslineCount, _params.SampleCount];
        var contributionSum = new float[_params.InlineCount, _params.CrosslineCount, _params.SampleCount];

        foreach (var line in _cube.Lines)
        {
            if (line.SeismicData == null) continue;
            ProjectLineToVolume(line, contributionSum, contributionCount);
        }

        // Second pass: average where we have data
        for (int i = 0; i < _params.InlineCount; i++)
        {
            for (int j = 0; j < _params.CrosslineCount; j++)
            {
                for (int k = 0; k < _params.SampleCount; k++)
                {
                    if (contributionCount[i, j, k] > 0)
                    {
                        volume[i, j, k] = contributionSum[i, j, k] / contributionCount[i, j, k];
                    }
                }
            }
        }

        // Third pass: interpolate missing data
        InterpolateVolume(volume, contributionCount);

        Logger.Log("[VolumeBuilder] Volume construction complete");
        return volume;
    }

    /// <summary>
    /// Calculate optimal grid parameters based on bounds and data
    /// </summary>
    private void CalculateGridParameters()
    {
        // Determine optimal spacing from the seismic data
        float minTraceSpacing = float.MaxValue;
        float minSampleInterval = float.MaxValue;
        float maxTime = 0;

        foreach (var line in _cube.Lines)
        {
            if (line.SeismicData == null) continue;

            minTraceSpacing = Math.Min(minTraceSpacing, line.Geometry.TraceSpacing);
            minSampleInterval = Math.Min(minSampleInterval, line.SeismicData.GetSampleIntervalMs());
            maxTime = Math.Max(maxTime, line.SeismicData.GetDurationSeconds() * 1000);
        }

        // Set grid parameters
        if (minTraceSpacing < float.MaxValue)
        {
            _params.InlineSpacing = minTraceSpacing;
            _params.CrosslineSpacing = minTraceSpacing;
        }

        if (minSampleInterval < float.MaxValue)
        {
            _params.SampleInterval = minSampleInterval;
        }

        // Calculate grid dimensions
        _params.InlineCount = Math.Max(10, (int)Math.Ceiling(_bounds.Width / _params.InlineSpacing));
        _params.CrosslineCount = Math.Max(10, (int)Math.Ceiling(_bounds.Height / _params.CrosslineSpacing));
        _params.SampleCount = Math.Max(100, (int)Math.Ceiling(maxTime / _params.SampleInterval));

        // Limit to reasonable sizes
        _params.InlineCount = Math.Min(_params.InlineCount, 500);
        _params.CrosslineCount = Math.Min(_params.CrosslineCount, 500);
        _params.SampleCount = Math.Min(_params.SampleCount, 5000);

        Logger.Log($"[VolumeBuilder] Grid params: IL={_params.InlineCount} ({_params.InlineSpacing}m), " +
                   $"XL={_params.CrosslineCount} ({_params.CrosslineSpacing}m), " +
                   $"Samples={_params.SampleCount} ({_params.SampleInterval}ms)");
    }

    /// <summary>
    /// Project a single line onto the volume grid
    /// </summary>
    private void ProjectLineToVolume(SeismicCubeLine line, float[,,] sum, int[,,] count)
    {
        var geometry = line.Geometry;
        var dataset = line.SeismicData;
        if (dataset == null) return;

        int traceCount = dataset.GetTraceCount();
        int sampleCount = dataset.GetSampleCount();
        float sampleIntervalMs = dataset.GetSampleIntervalMs();

        for (int traceIdx = 0; traceIdx < traceCount; traceIdx++)
        {
            var trace = dataset.GetTrace(traceIdx);
            if (trace == null) continue;

            // Get world position of this trace
            var pos = geometry.GetPositionAtTrace(traceIdx);

            // Convert to grid indices
            int i = (int)((pos.X - _bounds.MinX) / _params.InlineSpacing);
            int j = (int)((pos.Y - _bounds.MinY) / _params.CrosslineSpacing);

            if (i < 0 || i >= _params.InlineCount || j < 0 || j >= _params.CrosslineCount)
                continue;

            // Project all samples
            for (int sampleIdx = 0; sampleIdx < Math.Min(sampleCount, trace.Samples.Length); sampleIdx++)
            {
                float timeMs = sampleIdx * sampleIntervalMs;
                int k = (int)(timeMs / _params.SampleInterval);

                if (k >= 0 && k < _params.SampleCount)
                {
                    sum[i, j, k] += trace.Samples[sampleIdx];
                    count[i, j, k]++;
                }
            }
        }

        Logger.Log($"[VolumeBuilder] Projected line '{line.Name}' ({traceCount} traces)");
    }

    /// <summary>
    /// Interpolate missing data in the volume
    /// </summary>
    private void InterpolateVolume(float[,,] volume, int[,,] count)
    {
        Logger.Log("[VolumeBuilder] Interpolating missing data...");

        // Use inverse distance weighted interpolation
        int searchRadius = 10; // Grid cells

        for (int i = 0; i < _params.InlineCount; i++)
        {
            for (int j = 0; j < _params.CrosslineCount; j++)
            {
                for (int k = 0; k < _params.SampleCount; k++)
                {
                    if (count[i, j, k] > 0)
                        continue; // Already have data

                    // Find nearby points with data
                    float weightSum = 0;
                    float valueSum = 0;

                    for (int di = -searchRadius; di <= searchRadius; di++)
                    {
                        for (int dj = -searchRadius; dj <= searchRadius; dj++)
                        {
                            int ni = i + di;
                            int nj = j + dj;

                            if (ni < 0 || ni >= _params.InlineCount ||
                                nj < 0 || nj >= _params.CrosslineCount)
                                continue;

                            if (count[ni, nj, k] > 0)
                            {
                                float dist = MathF.Sqrt(di * di + dj * dj);
                                if (dist > 0)
                                {
                                    float weight = 1.0f / (dist * dist);
                                    weightSum += weight;
                                    valueSum += weight * volume[ni, nj, k];
                                }
                            }
                        }
                    }

                    if (weightSum > 0)
                    {
                        volume[i, j, k] = valueSum / weightSum;
                    }
                }
            }

            // Progress logging
            if ((i + 1) % 50 == 0)
            {
                float progress = (float)(i + 1) / _params.InlineCount * 100;
                Logger.Log($"[VolumeBuilder] Interpolation progress: {progress:F0}%");
            }
        }
    }

    /// <summary>
    /// Build a time slice at a specific time
    /// </summary>
    public float[,] BuildTimeSlice(float timeMs)
    {
        CalculateGridParameters();
        var slice = new float[_params.InlineCount, _params.CrosslineCount];
        var count = new int[_params.InlineCount, _params.CrosslineCount];

        foreach (var line in _cube.Lines)
        {
            if (line.SeismicData == null) continue;

            var geometry = line.Geometry;
            var dataset = line.SeismicData;
            int traceCount = dataset.GetTraceCount();
            float sampleIntervalMs = dataset.GetSampleIntervalMs();

            int sampleIdx = (int)(timeMs / sampleIntervalMs);

            for (int traceIdx = 0; traceIdx < traceCount; traceIdx++)
            {
                var trace = dataset.GetTrace(traceIdx);
                if (trace == null || sampleIdx >= trace.Samples.Length)
                    continue;

                var pos = geometry.GetPositionAtTrace(traceIdx);
                int i = (int)((pos.X - _bounds.MinX) / _params.InlineSpacing);
                int j = (int)((pos.Y - _bounds.MinY) / _params.CrosslineSpacing);

                if (i >= 0 && i < _params.InlineCount && j >= 0 && j < _params.CrosslineCount)
                {
                    slice[i, j] += trace.Samples[sampleIdx];
                    count[i, j]++;
                }
            }
        }

        // Average and interpolate
        for (int i = 0; i < _params.InlineCount; i++)
        {
            for (int j = 0; j < _params.CrosslineCount; j++)
            {
                if (count[i, j] > 0)
                {
                    slice[i, j] /= count[i, j];
                }
            }
        }

        InterpolateSlice(slice, count);
        return slice;
    }

    /// <summary>
    /// Interpolate missing data in a 2D slice
    /// </summary>
    private void InterpolateSlice(float[,] slice, int[,] count)
    {
        int searchRadius = 15;

        for (int i = 0; i < _params.InlineCount; i++)
        {
            for (int j = 0; j < _params.CrosslineCount; j++)
            {
                if (count[i, j] > 0)
                    continue;

                float weightSum = 0;
                float valueSum = 0;

                for (int di = -searchRadius; di <= searchRadius; di++)
                {
                    for (int dj = -searchRadius; dj <= searchRadius; dj++)
                    {
                        int ni = i + di;
                        int nj = j + dj;

                        if (ni < 0 || ni >= _params.InlineCount ||
                            nj < 0 || nj >= _params.CrosslineCount)
                            continue;

                        if (count[ni, nj] > 0)
                        {
                            float dist = MathF.Sqrt(di * di + dj * dj);
                            if (dist > 0)
                            {
                                float weight = 1.0f / (dist * dist);
                                weightSum += weight;
                                valueSum += weight * slice[ni, nj];
                            }
                        }
                    }
                }

                if (weightSum > 0)
                {
                    slice[i, j] = valueSum / weightSum;
                }
            }
        }
    }
}

/// <summary>
/// Builder for creating a SeismicCubeDataset from multiple seismic lines
/// </summary>
public class SeismicCubeBuilder
{
    private readonly List<(SeismicDataset Dataset, LineGeometry Geometry)> _lines = new();
    private readonly CubeNormalizationSettings _normalizationSettings = new();
    private string _cubeName = "Seismic Cube";
    private string _surveyName = "";

    /// <summary>
    /// Set the cube name
    /// </summary>
    public SeismicCubeBuilder WithName(string name)
    {
        _cubeName = name;
        return this;
    }

    /// <summary>
    /// Set the survey name
    /// </summary>
    public SeismicCubeBuilder WithSurveyName(string surveyName)
    {
        _surveyName = surveyName;
        return this;
    }

    /// <summary>
    /// Add a seismic line with its geometry
    /// </summary>
    public SeismicCubeBuilder AddLine(SeismicDataset dataset, LineGeometry geometry)
    {
        _lines.Add((dataset, geometry));
        return this;
    }

    /// <summary>
    /// Add a seismic line with coordinates from trace headers
    /// </summary>
    public SeismicCubeBuilder AddLineFromHeaders(SeismicDataset dataset)
    {
        var geometry = ExtractGeometryFromHeaders(dataset);
        _lines.Add((dataset, geometry));
        return this;
    }

    /// <summary>
    /// Add a perpendicular line to an existing line at a specific trace
    /// </summary>
    public SeismicCubeBuilder AddPerpendicularLine(SeismicDataset dataset, int baseLineIndex, int traceIndex)
    {
        if (baseLineIndex < 0 || baseLineIndex >= _lines.Count)
        {
            Logger.LogError($"[CubeBuilder] Invalid base line index: {baseLineIndex}");
            return this;
        }

        var baseLine = _lines[baseLineIndex];
        var intersectionPoint = baseLine.Geometry.GetPositionAtTrace(traceIndex);
        var baseDirection = baseLine.Geometry.Direction;

        // Calculate perpendicular direction
        var perpDirection = new Vector3(-baseDirection.Y, baseDirection.X, 0);
        perpDirection = Vector3.Normalize(perpDirection);

        // Calculate geometry
        int traceCount = dataset.GetTraceCount();
        float traceSpacing = baseLine.Geometry.TraceSpacing;
        float halfLength = (traceCount * traceSpacing) / 2.0f;

        var geometry = new LineGeometry
        {
            StartPoint = intersectionPoint - perpDirection * halfLength,
            EndPoint = intersectionPoint + perpDirection * halfLength,
            TraceSpacing = traceSpacing,
            Azimuth = baseLine.Geometry.Azimuth + 90f
        };

        _lines.Add((dataset, geometry));
        return this;
    }

    /// <summary>
    /// Configure normalization settings
    /// </summary>
    public SeismicCubeBuilder WithNormalization(Action<CubeNormalizationSettings> configure)
    {
        configure(_normalizationSettings);
        return this;
    }

    /// <summary>
    /// Build the seismic cube
    /// </summary>
    public SeismicCubeDataset Build()
    {
        Logger.Log($"[CubeBuilder] Building cube '{_cubeName}' with {_lines.Count} lines");

        var cube = new SeismicCubeDataset(_cubeName, "")
        {
            SurveyName = _surveyName,
            NormalizationSettings = _normalizationSettings,
            CreationDate = DateTime.Now
        };

        // Add all lines
        foreach (var (dataset, geometry) in _lines)
        {
            cube.AddLine(dataset, geometry);
        }

        // Apply normalization at intersections
        if (_normalizationSettings.NormalizeAmplitude ||
            _normalizationSettings.MatchFrequency ||
            _normalizationSettings.MatchPhase)
        {
            cube.ApplyNormalization();
        }

        Logger.Log($"[CubeBuilder] Cube built with {cube.Lines.Count} lines, {cube.Intersections.Count} intersections");
        return cube;
    }

    /// <summary>
    /// Extract line geometry from trace header coordinates
    /// </summary>
    private LineGeometry ExtractGeometryFromHeaders(SeismicDataset dataset)
    {
        var geometry = new LineGeometry();

        if (dataset.SegyData?.Traces == null || dataset.SegyData.Traces.Count < 2)
        {
            Logger.LogWarning("[CubeBuilder] Cannot extract geometry: insufficient traces");
            return geometry;
        }

        var traces = dataset.SegyData.Traces;
        var firstTrace = traces[0];
        var lastTrace = traces[traces.Count - 1];

        // Get coordinates from trace headers
        var (x1, y1) = firstTrace.GetScaledSourceCoordinates();
        var (x2, y2) = lastTrace.GetScaledSourceCoordinates();

        // If source coordinates are zero, try CDP coordinates
        if (Math.Abs(x1) < 1e-6 && Math.Abs(y1) < 1e-6)
        {
            x1 = firstTrace.CdpX;
            y1 = firstTrace.CdpY;
            x2 = lastTrace.CdpX;
            y2 = lastTrace.CdpY;
        }

        geometry.StartPoint = new Vector3((float)x1, (float)y1, 0);
        geometry.EndPoint = new Vector3((float)x2, (float)y2, 0);

        // Calculate trace spacing
        if (traces.Count > 1)
        {
            geometry.TraceSpacing = geometry.Length / (traces.Count - 1);
        }

        // Calculate azimuth
        float dx = geometry.EndPoint.X - geometry.StartPoint.X;
        float dy = geometry.EndPoint.Y - geometry.StartPoint.Y;
        geometry.Azimuth = MathF.Atan2(dx, dy) * 180f / MathF.PI;
        if (geometry.Azimuth < 0) geometry.Azimuth += 360f;

        Logger.Log($"[CubeBuilder] Extracted geometry: Start=({x1:F1},{y1:F1}), End=({x2:F1},{y2:F1}), " +
                   $"Spacing={geometry.TraceSpacing:F1}m, Azimuth={geometry.Azimuth:F1}°");

        return geometry;
    }
}
