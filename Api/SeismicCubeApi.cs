using System;
using System.Numerics;
using System.Threading.Tasks;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Seismic;

namespace GeoscientistToolkit.Api;

/// <summary>
///     Provides automation helpers for building and exporting seismic cubes.
/// </summary>
public class SeismicCubeApi
{
    /// <summary>
    ///     Creates an empty seismic cube dataset.
    /// </summary>
    public SeismicCubeDataset CreateCube(string name, string surveyName = "", string projectName = "")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Cube name is required.", nameof(name));

        return new SeismicCubeDataset(name, "")
        {
            SurveyName = surveyName ?? "",
            ProjectName = projectName ?? ""
        };
    }

    /// <summary>
    ///     Adds a seismic line to the cube with explicit geometry.
    /// </summary>
    public void AddLine(SeismicCubeDataset cube, SeismicDataset line, LineGeometry geometry)
    {
        if (cube == null) throw new ArgumentNullException(nameof(cube));
        if (line == null) throw new ArgumentNullException(nameof(line));
        if (geometry == null) throw new ArgumentNullException(nameof(geometry));

        cube.AddLine(line, geometry);
    }

    /// <summary>
    ///     Adds a seismic line using geometry inferred from trace headers.
    /// </summary>
    public void AddLineFromHeaders(SeismicCubeDataset cube, SeismicDataset line)
    {
        if (cube == null) throw new ArgumentNullException(nameof(cube));
        if (line == null) throw new ArgumentNullException(nameof(line));

        var geometry = BuildGeometryFromHeaders(line);
        cube.AddLine(line, geometry);
    }

    /// <summary>
    ///     Adds a perpendicular seismic line at a trace index of an existing line.
    /// </summary>
    public void AddPerpendicularLine(SeismicCubeDataset cube, SeismicDataset line, string baseLineId, int traceIndex)
    {
        if (cube == null) throw new ArgumentNullException(nameof(cube));
        if (line == null) throw new ArgumentNullException(nameof(line));
        if (string.IsNullOrWhiteSpace(baseLineId))
            throw new ArgumentException("Base line id is required.", nameof(baseLineId));

        cube.AddPerpendicularLine(line, baseLineId, traceIndex);
    }

    /// <summary>
    ///     Detects intersections between cube lines.
    /// </summary>
    public void DetectIntersections(SeismicCubeDataset cube)
    {
        if (cube == null) throw new ArgumentNullException(nameof(cube));
        cube.DetectIntersections();
    }

    /// <summary>
    ///     Applies normalization at line intersections.
    /// </summary>
    public void ApplyNormalization(SeismicCubeDataset cube)
    {
        if (cube == null) throw new ArgumentNullException(nameof(cube));
        cube.ApplyNormalization();
    }

    /// <summary>
    ///     Configures grid parameters and builds the regularized volume.
    /// </summary>
    public void BuildVolume(
        SeismicCubeDataset cube,
        int inlineCount,
        int crosslineCount,
        int sampleCount,
        float inlineSpacing,
        float crosslineSpacing,
        float sampleInterval)
    {
        if (cube == null) throw new ArgumentNullException(nameof(cube));

        var grid = cube.GridParameters;
        grid.InlineCount = inlineCount;
        grid.CrosslineCount = crosslineCount;
        grid.SampleCount = sampleCount;
        grid.InlineSpacing = inlineSpacing;
        grid.CrosslineSpacing = crosslineSpacing;
        grid.SampleInterval = sampleInterval;

        cube.BuildRegularizedVolume();
    }

    /// <summary>
    ///     Exports the cube to the compressed .seiscube format.
    /// </summary>
    public Task ExportAsync(
        SeismicCubeDataset cube,
        string outputPath,
        SeismicCubeExportOptions options = null,
        IProgress<(float progress, string message)> progress = null)
    {
        if (cube == null) throw new ArgumentNullException(nameof(cube));
        return SeismicCubeSerializer.ExportAsync(cube, outputPath, options, progress);
    }

    /// <summary>
    ///     Imports a cube from .seiscube format.
    /// </summary>
    public Task<SeismicCubeDataset> ImportAsync(
        string inputPath,
        IProgress<(float progress, string message)> progress = null)
    {
        return SeismicCubeSerializer.ImportAsync(inputPath, progress);
    }

    /// <summary>
    ///     Exports the cube to a Subsurface GIS dataset.
    /// </summary>
    public SubsurfaceGISDataset ExportToSubsurfaceGis(SeismicCubeDataset cube, string name)
    {
        if (cube == null) throw new ArgumentNullException(nameof(cube));
        var exporter = new SeismicCubeGISExporter(cube);
        return exporter.ExportToSubsurfaceGIS(name);
    }

    /// <summary>
    ///     Exports a time slice as a GIS raster layer.
    /// </summary>
    public GISRasterLayer ExportTimeSlice(SeismicCubeDataset cube, float timeMs, string name)
    {
        if (cube == null) throw new ArgumentNullException(nameof(cube));
        var exporter = new SeismicCubeGISExporter(cube);
        return exporter.ExportTimeSliceAsRaster(timeMs, name);
    }

    private static LineGeometry BuildGeometryFromHeaders(SeismicDataset dataset)
    {
        if (dataset.SegyData?.Traces == null || dataset.SegyData.Traces.Count < 2)
            throw new InvalidOperationException("Insufficient trace headers to derive geometry.");

        var traces = dataset.SegyData.Traces;
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
}

