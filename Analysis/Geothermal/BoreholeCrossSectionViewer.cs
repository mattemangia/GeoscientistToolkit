// GeoscientistToolkit/UI/Visualization/BoreholeCrossSectionViewer.cs

using System.Numerics;
using GeoscientistToolkit.Analysis.Geothermal;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;
using ImGuiNET;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;

namespace GeoscientistToolkit.UI.Visualization;

/// <summary>
///     2D visualization of borehole cross-section showing heat exchanger geometry,
///     temperature distribution, velocity fields, and fluid circulation.
/// </summary>
public class BoreholeCrossSectionViewer
{
    public enum ViewMode
    {
        Temperature,
        Velocity,
        FluidTemperature,
        FluidVelocity,
        Combined
    }

    private readonly List<OxyColor> _turboColormap;
    private ViewMode _currentView = ViewMode.Temperature;
    private float _depthPosition = 0.5f; // 0-1 normalized depth
    private GeothermalMesh _mesh;
    private GeothermalSimulationOptions _options;
    private GeothermalSimulationResults _results;
    private float _selectedDepthMeters;
    private bool _showFlowArrows = true;
    private bool _showGrid = true;
    private bool _showLegend = true;
    private int _debugCallCount = 0;

    public BoreholeCrossSectionViewer()
    {
        _turboColormap = GenerateTurboColormap();
    }

    public void LoadResults(GeothermalSimulationResults results, GeothermalMesh mesh,
        GeothermalSimulationOptions options)
    {
        _results = results;
        _mesh = mesh;
        _options = options;
        _selectedDepthMeters = (float)(_options.BoreholeDataset.TotalDepth * 0.5);
    }

    public void RenderControls()
    {
        if (_results == null || _mesh == null)
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "Load simulation results to view borehole cross-section");
            return;
        }

        ImGui.Text("Borehole Cross-Section Viewer");
        ImGui.Separator();

        // View mode selector
        if (ImGui.BeginCombo("View Mode", _currentView.ToString()))
        {
            foreach (var mode in Enum.GetValues<ViewMode>())
                if (ImGui.Selectable(mode.ToString(), _currentView == mode))
                    _currentView = mode;
            ImGui.EndCombo();
        }

        // Depth slider
        var depthMeters = _selectedDepthMeters;
        if (ImGui.SliderFloat("Depth (m)", ref depthMeters, 0f, (float)_options.BoreholeDataset.TotalDepth))
        {
            _selectedDepthMeters = depthMeters;
            _depthPosition = depthMeters / (float)_options.BoreholeDataset.TotalDepth;
        }

        ImGui.Text($"At depth: {_selectedDepthMeters:F1} m ({_depthPosition * 100:F0}%)");

        ImGui.Separator();
        ImGui.Checkbox("Show Flow Arrows", ref _showFlowArrows);
        ImGui.Checkbox("Show Grid", ref _showGrid);
        ImGui.Checkbox("Show Legend", ref _showLegend);

        ImGui.Separator();
        if (ImGui.Button("Export Cross-Section Plot", new Vector2(-1, 30)))
            ExportPlot();

        if (ImGui.Button("Diagnose Temperature Field", new Vector2(-1, 30)))
            DiagnoseTemperatureField();

        ImGui.Separator();
        ImGui.TextWrapped("Note: SVG plots cannot be displayed directly in ImGui. Use Export button to view in browser.");
    }

    /// <summary>
    ///     Creates a PlotModel for the cross-section at the selected depth.
    /// </summary>
    public PlotModel CreateCrossSectionPlot()
    {
        if (_results == null || _mesh == null) return new PlotModel { Title = "No data loaded" };

        var plot = new PlotModel
        {
            Title = $"Borehole Cross-Section at {_selectedDepthMeters:F1} m depth",
            PlotAreaBorderThickness = new OxyThickness(1),
            Background = OxyColors.White
        };

        // Equal aspect ratio
        plot.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "X (m)",
            MajorGridlineStyle = _showGrid ? LineStyle.Solid : LineStyle.None,
            MajorGridlineColor = OxyColor.FromArgb(50, 0, 0, 0)
        });

        plot.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Y (m)",
            MajorGridlineStyle = _showGrid ? LineStyle.Solid : LineStyle.None,
            MajorGridlineColor = OxyColor.FromArgb(50, 0, 0, 0)
        });

        // Find the vertical index closest to selected depth
        var zIndex = FindVerticalIndex(_selectedDepthMeters);

        switch (_currentView)
        {
            case ViewMode.Temperature:
                AddTemperatureContours(plot, zIndex);
                break;
            case ViewMode.Velocity:
                AddVelocityContours(plot, zIndex);
                break;
            case ViewMode.FluidTemperature:
                AddFluidTemperatureProfile(plot);
                break;
            case ViewMode.FluidVelocity:
                AddFluidVelocityProfile(plot);
                break;
            case ViewMode.Combined:
                AddTemperatureContours(plot, zIndex);
                if (_showFlowArrows)
                    AddVelocityVectors(plot, zIndex);
                break;
        }

        // Add heat exchanger geometry
        AddHeatExchangerGeometry(plot);

        // Add borehole boundary
        AddBoreholeBoundary(plot);

        return plot;
    }

    private void AddTemperatureContours(PlotModel plot, int zIndex)
    {
        var series = new HeatMapSeries
        {
            X0 = -_options.BoreholeDataset.WellDiameter / 2,
            X1 = _options.BoreholeDataset.WellDiameter / 2,
            Y0 = -_options.BoreholeDataset.WellDiameter / 2,
            Y1 = _options.BoreholeDataset.WellDiameter / 2,
            Interpolate = true
        };

        // Sample temperature field at this depth in Cartesian grid
        var resolution = 100;
        var data = new double[resolution, resolution];
        var radius = _options.BoreholeDataset.WellDiameter / 2;

        for (var i = 0; i < resolution; i++)
        for (var j = 0; j < resolution; j++)
        {
            var x = -radius + 2 * radius * i / (resolution - 1);
            var y = -radius + 2 * radius * j / (resolution - 1);
            var r = Math.Sqrt(x * x + y * y);

            if (r > radius)
            {
                data[i, j] = double.NaN; // Outside borehole
            }
            else
            {
                // Interpolate temperature from cylindrical mesh
                var temp = InterpolateTemperature(x, y, zIndex);
                data[i, j] = temp - 273.15; // Convert to Celsius
            }
        }

        series.Data = data;

        // Create color axis
        var colorAxis = new LinearColorAxis
        {
            Position = AxisPosition.Right,
            Title = "Temperature (°C)",
            Palette = OxyPalette.Interpolate(100,
                OxyColors.Blue, OxyColors.Cyan, OxyColors.Green,
                OxyColors.Yellow, OxyColors.Orange, OxyColors.Red),
            HighColor = OxyColors.Red,
            LowColor = OxyColors.Blue
        };

        plot.Axes.Add(colorAxis);
        plot.Series.Add(series);
    }

    private void AddVelocityContours(PlotModel plot, int zIndex)
    {
        if (_results.DarcyVelocityField == null) return;

        var series = new HeatMapSeries
        {
            X0 = -_options.BoreholeDataset.WellDiameter / 2,
            X1 = _options.BoreholeDataset.WellDiameter / 2,
            Y0 = -_options.BoreholeDataset.WellDiameter / 2,
            Y1 = _options.BoreholeDataset.WellDiameter / 2,
            Interpolate = true
        };

        var resolution = 100;
        var data = new double[resolution, resolution];
        var radius = _options.BoreholeDataset.WellDiameter / 2;

        for (var i = 0; i < resolution; i++)
        for (var j = 0; j < resolution; j++)
        {
            var x = -radius + 2 * radius * i / (resolution - 1);
            var y = -radius + 2 * radius * j / (resolution - 1);
            var r = Math.Sqrt(x * x + y * y);

            if (r > radius)
            {
                data[i, j] = double.NaN;
            }
            else
            {
                var vel = InterpolateVelocityMagnitude(x, y, zIndex);
                data[i, j] = vel * 1000; // Convert to mm/s for better visualization
            }
        }

        series.Data = data;

        var colorAxis = new LinearColorAxis
        {
            Position = AxisPosition.Right,
            Title = "Velocity (mm/s)",
            Palette = OxyPalette.Interpolate(100,
                OxyColors.DarkBlue, OxyColors.Blue, OxyColors.Cyan,
                OxyColors.Yellow, OxyColors.Red),
            HighColor = OxyColors.Red,
            LowColor = OxyColors.DarkBlue
        };

        plot.Axes.Add(colorAxis);
        plot.Series.Add(series);
    }

    private void AddVelocityVectors(PlotModel plot, int zIndex)
    {
        if (_results.DarcyVelocityField == null) return;

        var vectorSeries = new ScatterSeries
        {
            MarkerType = MarkerType.None
        };

        // Sample velocity vectors on a grid
        var samples = 10;
        var radius = _options.BoreholeDataset.WellDiameter / 2;

        for (var i = 0; i < samples; i++)
        for (var j = 0; j < samples; j++)
        {
            var x = -radius + 2 * radius * i / (samples - 1);
            var y = -radius + 2 * radius * j / (samples - 1);
            var r = Math.Sqrt(x * x + y * y);

            if (r < radius * 0.9) // Stay inside borehole
            {
                var vel = InterpolateVelocity(x, y, zIndex);
                if (vel.Length() > 1e-8)
                {
                    // Add arrow annotation
                    var arrow = new ArrowAnnotation
                    {
                        StartPoint = new DataPoint(x, y),
                        EndPoint = new DataPoint(x + vel.X * 1000, y + vel.Y * 1000), // Scale for visibility
                        Color = OxyColors.Black,
                        StrokeThickness = 1.5,
                        HeadLength = 3,
                        HeadWidth = 2
                    };
                    plot.Annotations.Add(arrow);
                }
            }
        }
    }

    private void AddFluidTemperatureProfile(PlotModel plot)
    {
        if (_results.FluidTemperatureProfile == null || !_results.FluidTemperatureProfile.Any())
        {
            plot.Title = "No fluid temperature profile available";
            return;
        }

        // Create line series for down and up pipes
        var downSeries = new LineSeries
        {
            Title = "Downflow Pipe",
            Color = OxyColors.Blue,
            StrokeThickness = 2
        };

        var upSeries = new LineSeries
        {
            Title = "Upflow Pipe",
            Color = OxyColors.Red,
            StrokeThickness = 2
        };

        foreach (var point in _results.FluidTemperatureProfile)
        {
            downSeries.Points.Add(new DataPoint(point.temperatureDown - 273.15, point.depth));
            upSeries.Points.Add(new DataPoint(point.temperatureUp - 273.15, point.depth));
        }

        plot.Series.Add(downSeries);
        plot.Series.Add(upSeries);

        // Update axes
        plot.Axes.Clear();
        plot.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Temperature (°C)"
        });

        plot.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Depth (m)",
            StartPosition = 1,
            EndPosition = 0 // Invert so depth increases downward
        });

        plot.Title = "Fluid Temperature Profile";
        plot.Legends.Add(new Legend
        {
            LegendPosition = LegendPosition.TopRight
        });
    }

    private void AddFluidVelocityProfile(PlotModel plot)
    {
        // Placeholder - calculate fluid velocity based on flow rate and pipe dimensions
        plot.Title = "Fluid Velocity Profile - Not yet implemented";
    }

    private void AddHeatExchangerGeometry(PlotModel plot)
    {
        var pipeRadius = _options.PipeSpacing / 8; // Approximate pipe radius

        if (_options.HeatExchangerType == HeatExchangerType.UTube)
        {
            // U-tube: two pipes separated by spacing
            var spacing = _options.PipeSpacing / 2;

            // Downflow pipe (left)
            var downPipe = CreateCircleSeries(-spacing, 0, pipeRadius, OxyColors.Blue, "Down pipe");
            plot.Series.Add(downPipe);

            // Upflow pipe (right)
            var upPipe = CreateCircleSeries(spacing, 0, pipeRadius, OxyColors.Red, "Up pipe");
            plot.Series.Add(upPipe);
        }
        else if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
        {
            // Coaxial: inner pipe and outer annulus
            var outerRadius = _options.PipeSpacing / 2;
            var innerRadius = outerRadius * 0.5; // Inner pipe is half the diameter

            // Outer pipe
            var outerPipe = CreateCircleSeries(0, 0, outerRadius, OxyColors.Blue, "Outer annulus", filled: false);
            plot.Series.Add(outerPipe);

            // Inner pipe
            var innerPipe = CreateCircleSeries(0, 0, innerRadius, OxyColors.Red, "Inner pipe");
            plot.Series.Add(innerPipe);

            // Flow direction annotations
            plot.Annotations.Add(new TextAnnotation
            {
                Text = "↓ Down",
                TextPosition = new DataPoint(0, innerRadius / 2),
                TextColor = OxyColors.Red,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            });

            plot.Annotations.Add(new TextAnnotation
            {
                Text = "↑ Up",
                TextPosition = new DataPoint(0, -outerRadius * 0.7),
                TextColor = OxyColors.Blue,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            });
        }
    }

    private void AddBoreholeBoundary(PlotModel plot)
    {
        var radius = _options.BoreholeDataset.WellDiameter / 2;
        var boundary = CreateCircleSeries(0, 0, radius, OxyColors.Black, "Borehole wall", filled: false,
            thickness: 2);
        plot.Series.Add(boundary);
    }

    private LineSeries CreateCircleSeries(double centerX, double centerY, double radius, OxyColor color,
        string title = null, bool filled = true, double thickness = 1.5)
    {
        var series = new LineSeries
        {
            Title = title,
            Color = color,
            StrokeThickness = thickness,
            LineStyle = filled ? LineStyle.Solid : LineStyle.Dash
        };

        var points = 100;
        for (var i = 0; i <= points; i++)
        {
            var angle = 2 * Math.PI * i / points;
            var x = centerX + radius * Math.Cos(angle);
            var y = centerY + radius * Math.Sin(angle);
            series.Points.Add(new DataPoint(x, y));
        }

        return series;
    }

    private int FindVerticalIndex(float depthMeters)
    {
        for (var k = 0; k < _mesh.VerticalPoints - 1; k++)
            if (Math.Abs(_mesh.Z[k]) >= depthMeters)
                return k;

        return _mesh.VerticalPoints / 2; // Default to middle
    }

    private double InterpolateTemperature(double x, double y, int zIndex)
    {
        if (_results.FinalTemperatureField == null)
        {
            Logger.LogWarning("FinalTemperatureField is null in InterpolateTemperature");
            return 293.15; // 20°C default
        }

        // Validate zIndex bounds
        if (zIndex < 0 || zIndex >= _mesh.VerticalPoints)
        {
            Logger.LogWarning($"Invalid zIndex: {zIndex}, clamping to valid range [0, {_mesh.VerticalPoints - 1}]");
            zIndex = Math.Clamp(zIndex, 0, _mesh.VerticalPoints - 1);
        }

        var r = Math.Sqrt(x * x + y * y);
        var theta = Math.Atan2(y, x);
        if (theta < 0) theta += 2 * Math.PI;

        // Find radial index with proper interpolation
        var ir = 0;
        var found = false;
        for (var i = 0; i < _mesh.RadialPoints - 1; i++)
            if (r >= _mesh.R[i] && r < _mesh.R[i + 1])
            {
                ir = i;
                found = true;
                break;
            }

        // If not found, clamp to nearest valid index
        if (!found)
        {
            if (r < _mesh.R[0])
                ir = 0; // Inside innermost radius
            else
                ir = _mesh.RadialPoints - 2; // Outside, use outermost cell
        }

        // Calculate angular index with proper wrapping
        var ith = (int)(theta / (2 * Math.PI) * _mesh.AngularPoints) % _mesh.AngularPoints;
        if (ith < 0) ith += _mesh.AngularPoints;

        // Bounds check before accessing array
        ir = Math.Clamp(ir, 0, _mesh.RadialPoints - 1);
        ith = Math.Clamp(ith, 0, _mesh.AngularPoints - 1);

        // Get temperature and check if it's valid
        var temp = _results.FinalTemperatureField[ir, ith, zIndex];

        // Debug logging for first few calls
        if (_debugCallCount < 5)
        {
            Logger.Log(
                $"[InterpolateTemperature #{_debugCallCount}] x={x:F4}, y={y:F4}, r={r:F4}, theta={theta:F4}, ir={ir}, ith={ith}, z={zIndex}, T={temp:F2}K ({temp - 273.15:F2}°C)");
            _debugCallCount++;
        }

        return temp;
    }

    private double InterpolateVelocityMagnitude(double x, double y, int zIndex)
    {
        if (_results.DarcyVelocityField == null) return 0;

        var vel = InterpolateVelocity(x, y, zIndex);
        return vel.Length();
    }

    private Vector3 InterpolateVelocity(double x, double y, int zIndex)
    {
        if (_results.DarcyVelocityField == null) return Vector3.Zero;

        var r = Math.Sqrt(x * x + y * y);
        var theta = Math.Atan2(y, x);
        if (theta < 0) theta += 2 * Math.PI;

        var ir = 0;
        for (var i = 0; i < _mesh.RadialPoints - 1; i++)
            if (r >= _mesh.R[i] && r <= _mesh.R[i + 1])
            {
                ir = i;
                break;
            }

        var ith = (int)(theta / (2 * Math.PI) * _mesh.AngularPoints) % _mesh.AngularPoints;

        var vr = _results.DarcyVelocityField[ir, ith, zIndex, 0];
        var vth = _results.DarcyVelocityField[ir, ith, zIndex, 1];
        var vz = _results.DarcyVelocityField[ir, ith, zIndex, 2];

        // Convert to Cartesian
        var vx = vr * Math.Cos(theta) - vth * Math.Sin(theta);
        var vy = vr * Math.Sin(theta) + vth * Math.Cos(theta);

        return new Vector3((float)vx, (float)vy, (float)vz);
    }

    private List<OxyColor> GenerateTurboColormap()
    {
        // Turbo colormap - perceptually uniform
        var colors = new List<OxyColor>();
        var turboData = new[]
        {
            (0.18995, 0.07176, 0.23217),
            (0.25107, 0.25237, 0.63374),
            (0.27628, 0.42118, 0.89123),
            (0.25862, 0.57469, 0.96866),
            (0.21382, 0.72049, 0.92710),
            (0.22149, 0.85094, 0.77584),
            (0.41420, 0.93559, 0.55959),
            (0.67296, 0.95574, 0.36093),
            (0.90196, 0.88451, 0.18431),
            (0.98826, 0.71553, 0.14579),
            (0.96988, 0.49291, 0.14075),
            (0.85783, 0.27333, 0.14162),
            (0.64667, 0.08609, 0.13024)
        };

        foreach (var (r, g, b) in turboData)
            colors.Add(OxyColor.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));

        return colors;
    }


    /// <summary>
    ///     Diagnoses the temperature field to check for issues.
    /// </summary>
    public void DiagnoseTemperatureField()
    {
        if (_results?.FinalTemperatureField == null)
        {
            Logger.LogError("DiagnoseTemperatureField: FinalTemperatureField is NULL");
            return;
        }

        var dims = _results.FinalTemperatureField.GetLength(0) + "x" +
                   _results.FinalTemperatureField.GetLength(1) + "x" +
                   _results.FinalTemperatureField.GetLength(2);
        Logger.Log($"DiagnoseTemperatureField: Dimensions = {dims}");

        // Sample temperature values
        var nr = _results.FinalTemperatureField.GetLength(0);
        var nth = _results.FinalTemperatureField.GetLength(1);
        var nz = _results.FinalTemperatureField.GetLength(2);

        var minT = float.MaxValue;
        var maxT = float.MinValue;
        var sumT = 0.0;
        var count = 0;
        var zeroCount = 0;

        for (var i = 0; i < nr; i++)
        for (var j = 0; j < nth; j++)
        for (var k = 0; k < nz; k++)
        {
            var temp = _results.FinalTemperatureField[i, j, k];
            if (temp < minT) minT = temp;
            if (temp > maxT) maxT = temp;
            sumT += temp;
            count++;
            if (Math.Abs(temp) < 0.001) zeroCount++;
        }

        var avgT = sumT / count;

        Logger.Log($"Temperature field statistics:");
        Logger.Log($"  Min: {minT:F2}K ({minT - 273.15:F2}°C)");
        Logger.Log($"  Max: {maxT:F2}K ({maxT - 273.15:F2}°C)");
        Logger.Log($"  Avg: {avgT:F2}K ({avgT - 273.15:F2}°C)");
        Logger.Log($"  Zero values: {zeroCount}/{count} ({100.0 * zeroCount / count:F1}%)");

        // Sample some specific locations
        Logger.Log($"Sample temperatures:");
        Logger.Log($"  Center (0,0,mid): {_results.FinalTemperatureField[0, 0, nz / 2]:F2}K");
        Logger.Log($"  Edge (max,0,mid): {_results.FinalTemperatureField[nr - 1, 0, nz / 2]:F2}K");
        Logger.Log($"  Mid radius (mid,0,mid): {_results.FinalTemperatureField[nr / 2, 0, nz / 2]:F2}K");
    }

    private void ExportPlot()
    {
        var plot = CreateCrossSectionPlot();
        var path = Path.Combine(Path.GetTempPath(), $"borehole_cross_section_{_selectedDepthMeters:F1}m.svg");

        try
        {
            // Use SVG export which is built into OxyPlot
            var exporter = new OxyPlot.SvgExporter { Width = 1200, Height = 1000 };
            using var stream = File.Create(path);
            exporter.Export(plot, stream);
            Logger.Log($"Exported cross-section plot to: {path}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export plot: {ex.Message}");
        }
    }
}