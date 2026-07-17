// GAIA.GeoGenesis/Contaminants/ContaminantModule.cs
//
// High-level facade tying the contaminant workflow together: import a table (CSV/Excel) → build a
// variogram → krige onto a voxel grid → derive a flow field. Used by the PRISM CLI and the
// contaminant-distribution UI; kept free of any PRISM/UI dependency.

namespace GAIA.GeoGenesis.Contaminants;

/// <summary>The full interpolation product for one analyte (and optionally one time step).</summary>
public sealed class ContaminantInterpolation
{
    public string Analyte { get; init; } = string.Empty;
    public double? TimeDays { get; init; }
    public ExperimentalVariogram ExperimentalVariogram { get; init; } = new();
    public VariogramModel Model { get; init; } = new();
    public KrigingResult Grid { get; init; } = new();
    public List<FlowVector> Flow { get; init; } = new();
}

public sealed class ContaminantModule
{
    /// <summary>Read a table and materialise the dataset using the supplied separator/transpose/mapping.</summary>
    public ContaminantDataset Import(string path, ColumnMapping mapping, char? separator = null, bool transpose = false, int sheetIndex = 0)
    {
        var grid = TabularImporter.Read(path, separator, sheetIndex);
        if (transpose) grid = TabularImporter.Transpose(grid);
        return TabularImporter.Import(grid, mapping);
    }

    /// <summary>Experimental variogram + fitted model for one analyte (and optional time step).</summary>
    public (ExperimentalVariogram exp, VariogramModel model) BuildVariogram(
        ContaminantDataset dataset, string analyte, VariogramModelType type = VariogramModelType.Spherical,
        double? timeDays = null, int nLags = 12)
    {
        var pts = dataset.ValuesFor(analyte, timeDays);
        var exp = Variogram.Experimental(pts, nLags);
        var model = Variogram.FitModel(exp, type);
        return (exp, model);
    }

    /// <summary>Full pipeline: variogram → kriging → flow field, for one analyte/time.</summary>
    public ContaminantInterpolation Interpolate(
        ContaminantDataset dataset, string analyte,
        int nx = 48, int ny = 48, int nz = 1,
        VariogramModelType modelType = VariogramModelType.Spherical,
        double? timeDays = null, int maxNeighbors = 16, double flowConductivity = 1.0, int flowStride = 4)
    {
        var pts = dataset.ValuesFor(analyte, timeDays);
        var exp = Variogram.Experimental(pts, Math.Max(4, Math.Min(20, pts.Count)));
        var model = Variogram.FitModel(exp, modelType);
        var grid = OrdinaryKriging.Interpolate(pts, model, nx, ny, nz, maxNeighbors, analyte: analyte, timeDays: timeDays);
        var flow = FlowField.FromScalarGrid(grid, flowConductivity, flowStride);
        return new ContaminantInterpolation
        {
            Analyte = analyte, TimeDays = timeDays,
            ExperimentalVariogram = exp, Model = model, Grid = grid, Flow = flow
        };
    }

    /// <summary>One interpolation per imported time step (for time-series animation), reusing the same grid size.</summary>
    public List<ContaminantInterpolation> InterpolateTimeSeries(
        ContaminantDataset dataset, string analyte,
        int nx = 48, int ny = 48, int nz = 1, VariogramModelType modelType = VariogramModelType.Spherical)
    {
        var times = dataset.TimeSteps;
        if (times.Count == 0) return new List<ContaminantInterpolation> { Interpolate(dataset, analyte, nx, ny, nz, modelType) };
        return times.Select(t => Interpolate(dataset, analyte, nx, ny, nz, modelType, t)).ToList();
    }
}
