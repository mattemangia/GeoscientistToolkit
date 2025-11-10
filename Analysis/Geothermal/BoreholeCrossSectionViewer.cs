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
        
        // DEFINITIVE FIX: Define a focused view radius around the borehole
        var viewRadius = _options.BoreholeDataset.WellDiameter * 2.0;

        // Equal aspect ratio
        plot.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "X (m)",
            MajorGridlineStyle = _showGrid ? LineStyle.Solid : LineStyle.None,
            MajorGridlineColor = OxyColor.FromArgb(50, 0, 0, 0),
            IsPanEnabled = false,
            IsZoomEnabled = false,
            // DEFINITIVE FIX: Set explicit bounds to zoom in on the borehole
            Minimum = -viewRadius,
            Maximum = viewRadius
        });

        plot.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Y (m)",
            MajorGridlineStyle = _showGrid ? LineStyle.Solid : LineStyle.None,
            MajorGridlineColor = OxyColor.FromArgb(50, 0, 0, 0),
            IsPanEnabled = false,
            IsZoomEnabled = false,
            // DEFINITIVE FIX: Set explicit bounds to zoom in on the borehole
            Minimum = -viewRadius,
            Maximum = viewRadius
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

    /// <summary>
    /// This method displays both fluid and grout temperatures.
    /// It checks if a point is inside a pipe and retrieves the appropriate temperature from
    /// the 1D fluid profile instead of incorrectly sampling the 3D grout field.
    /// </summary>
    private void AddTemperatureContours(PlotModel plot, int zIndex)
    {
        // DEFINITIVE FIX: Use a radius focused on the borehole, not the entire domain
        var radius = _options.BoreholeDataset.WellDiameter * 2.0;
        var series = new HeatMapSeries
        {
            X0 = -radius,
            X1 = radius,
            Y0 = -radius,
            Y1 = radius,
            Interpolate = true
        };
    
        // Get fluid temperatures at the current depth
        var (tempDown, tempUp) = GetFluidTemperatureAtDepth(_selectedDepthMeters);
    
        // Define radii for coaxial heat exchanger
        var outerPipeRadius = _options.PipeOuterDiameter / 2.0;
        var innerPipeRadius = _options.PipeInnerDiameter / 2.0;
    
        // Sample temperature field at this depth in a Cartesian grid
        var resolution = 200; // Increased resolution for clarity
        var data = new double[resolution, resolution];
    
        for (var i = 0; i < resolution; i++)
        {
            for (var j = 0; j < resolution; j++)
            {
                var x = -radius + 2 * radius * i / (resolution - 1);
                var y = -radius + 2 * radius * j / (resolution - 1);
                var r = Math.Sqrt(x * x + y * y);
    
                double tempCelsius;
    
                if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
                {
                    // =========================================================================
                    // DEFINITIVE FIX: Check the flow configuration to assign the correct
                    // fluid temperature to the inner pipe and outer annulus for VISUALIZATION.
                    // =========================================================================
                    double tempInner, tempOuter;
                    if (_options.FlowConfiguration == FlowConfiguration.CounterFlowReversed)
                    {
                        // Reversed flow: UP (hot) is INNER, DOWN (cold) is OUTER
                        tempInner = tempUp;
                        tempOuter = tempDown;
                    }
                    else
                    {
                        // Standard flow: DOWN (cold) is INNER, UP (hot) is OUTER
                        tempInner = tempDown;
                        tempOuter = tempUp;
                    }

                    if (r <= innerPipeRadius)
                    {
                        // Inside the inner pipe
                        tempCelsius = tempInner - 273.15;
                    }
                    else if (r <= outerPipeRadius)
                    {
                        // Inside the outer annulus
                        tempCelsius = tempOuter - 273.15;
                    }
                    else
                    {
                        // In the grout or surrounding rock
                        tempCelsius = InterpolateTemperature(x, y, zIndex) - 273.15;
                    }
                }
                else // U-Tube
                {
                    // Simplified for now, can be expanded for U-tube geometry
                    tempCelsius = InterpolateTemperature(x, y, zIndex) - 273.15;
                }
                
                // DEFINITIVE FIX: Do not set to NaN. Let the interpolation handle temperatures
                // outside the borehole wall to show the thermal gradient in the surrounding rock.
                data[i, j] = tempCelsius;
            }
        }
    
        series.Data = data;
    
        // Create color axis
        var colorAxis = new LinearColorAxis
        {
            Position = AxisPosition.Right,
            Title = "Temperature (°C)",
            Palette = OxyPalette.Interpolate(256, _turboColormap.ToArray()),
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
        if (_options.HeatExchangerType == HeatExchangerType.Coaxial)
        {
            var outerRadius = _options.PipeOuterDiameter / 2.0;
            var innerRadius = _options.PipeInnerDiameter / 2.0;
    
            // Outer pipe wall
            var outerPipe = CreateCircleSeries(0, 0, outerRadius, OxyColors.Black, "Outer Pipe Wall", filled: false, thickness: 1.5);
            plot.Series.Add(outerPipe);
    
            // Inner pipe wall
            var innerPipe = CreateCircleSeries(0, 0, innerRadius, OxyColors.Red, "Inner Pipe Wall", filled: false, thickness: 1.5);
            plot.Series.Add(innerPipe);

            // =========================================================================
            // DEFINITIVE FIX: The text labels must be conditional on the flow configuration
            // to correctly show the flow direction in the visualization.
            // =========================================================================
            string innerFlowText, outerFlowText;
            if (_options.FlowConfiguration == FlowConfiguration.CounterFlowReversed)
            {
                // Hot fluid UP the center, cold fluid DOWN the annulus
                innerFlowText = "Up";
                outerFlowText = "↓ Down";
            }
            else
            {
                // Standard: Cold fluid DOWN the center, hot fluid UP the annulus
                innerFlowText = "Down";
                outerFlowText = "↑ Up";
            }
    
            // Flow direction annotations
            plot.Annotations.Add(new TextAnnotation
            {
                Text = innerFlowText,
                TextPosition = new DataPoint(0, 0),
                TextColor = OxyColors.Black,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle
            });
    
            plot.Annotations.Add(new TextAnnotation
            {
                Text = outerFlowText,
                TextPosition = new DataPoint(0, (innerRadius + outerRadius) / 2.0),
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
            thickness: 2, lineStyle: LineStyle.Dash);
        plot.Series.Add(boundary);
    }

    private LineSeries CreateCircleSeries(double centerX, double centerY, double radius, OxyColor color,
        string title = null, bool filled = true, double thickness = 1.5, LineStyle lineStyle = LineStyle.Solid)
    {
        var series = new LineSeries
        {
            Title = title,
            Color = color,
            StrokeThickness = thickness,
            LineStyle = lineStyle
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
    
    /// <summary>
    /// Helper function to get the interpolated fluid temperature at a specific depth.
    /// </summary>
    private (double tempDown, double tempUp) GetFluidTemperatureAtDepth(float depthMeters)
    {
        if (_results.FluidTemperatureProfile == null || !_results.FluidTemperatureProfile.Any())
        {
            return (273.15, 273.15); // Return freezing if no data
        }

        // Find the two closest points in the profile to interpolate between
        for (int i = 0; i < _results.FluidTemperatureProfile.Count - 1; i++)
        {
            var p1 = _results.FluidTemperatureProfile[i];
            var p2 = _results.FluidTemperatureProfile[i+1];

            if (depthMeters >= p1.depth && depthMeters <= p2.depth)
            {
                // Linear interpolation
                var t = (p2.depth - p1.depth) > 1e-6 ? (depthMeters - p1.depth) / (p2.depth - p1.depth) : 0;
                var tempDown = p1.temperatureDown + t * (p2.temperatureDown - p1.temperatureDown);
                var tempUp = p1.temperatureUp + t * (p2.temperatureUp - p1.temperatureUp);
                return (tempDown, tempUp);
            }
        }

        // DEFINITIVE FIX: If outside the range, return the temperature of the closest endpoint.
        // The compiler error occurred here because .Last() returns a 3-element tuple.
        // The fix is to deconstruct it and return a new 2-element tuple.
        var lastPoint = _results.FluidTemperatureProfile.Last();
        return (lastPoint.temperatureDown, lastPoint.temperatureUp);
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
        Logger.Log($"Sample temperatures at mid-depth (z={nz / 2}):");
        Logger.Log($"  Borehole center (r=0): {_results.FinalTemperatureField[0, 0, nz / 2]:F2}K ({_results.FinalTemperatureField[0, 0, nz / 2] - 273.15:F2}°C)");
        if (nr > 1) Logger.Log($"  Near borehole (r=1): {_results.FinalTemperatureField[1, 0, nz / 2]:F2}K ({_results.FinalTemperatureField[1, 0, nz / 2] - 273.15:F2}°C)");
        if (nr > 5) Logger.Log($"  Mid radius (r=5): {_results.FinalTemperatureField[5, 0, nz / 2]:F2}K ({_results.FinalTemperatureField[5, 0, nz / 2] - 273.15:F2}°C)");
        Logger.Log($"  Outer edge (r={nr-1}): {_results.FinalTemperatureField[nr - 1, 0, nz / 2]:F2}K ({_results.FinalTemperatureField[nr - 1, 0, nz / 2] - 273.15:F2}°C)");
        
        // Check radial temperature variation at mid-depth
        Logger.Log($"\nRadial temperature profile at mid-depth:");
        for (var i = 0; i < Math.Min(10, nr); i++)
        {
            var temp = _results.FinalTemperatureField[i, 0, nz / 2];
            Logger.Log($"  r[{i}] = {_mesh.R[i]:F3}m: T = {temp:F2}K ({temp - 273.15:F2}°C)");
        }
        
        // Check velocity field if available
        if (_results.DarcyVelocityField != null)
        {
            Logger.Log($"\nVelocity field diagnostics:");
            var vMin = float.MaxValue;
            var vMax = float.MinValue;
            var vSum = 0.0;
            var vCount = 0;
            
            for (var i = 0; i < nr; i++)
            for (var j = 0; j < nth; j++)
            for (var k = 0; k < nz; k++)
            {
                var vr = _results.DarcyVelocityField[i, j, k, 0];
                var vth = _results.DarcyVelocityField[i, j, k, 1];
                var vz = _results.DarcyVelocityField[i, j, k, 2];
                var vmag = Math.Sqrt(vr * vr + vth * vth + vz * vz);
                
                if (vmag < vMin) vMin = (float)vmag;
                if (vmag > vMax) vMax = (float)vmag;
                vSum += vmag;
                vCount++;
            }
            
            Logger.Log($"  Velocity magnitude: min={vMin:E3} m/s, max={vMax:E3} m/s, avg={vSum/vCount:E3} m/s");
            
            // Sample velocities at mid-depth
            Logger.Log($"  Sample velocities at mid-depth:");
            for (var i = 0; i < Math.Min(5, nr); i++)
            {
                var vr = _results.DarcyVelocityField[i, 0, nz / 2, 0];
                var vz = _results.DarcyVelocityField[i, 0, nz / 2, 2];
                Logger.Log($"    r[{i}]: vr={vr:E3} m/s, vz={vz:E3} m/s");
            }
        }
        
        // Check heat exchanger temperatures
        if (_results.FluidTemperatureProfile != null && _results.FluidTemperatureProfile.Any())
        {
            Logger.Log($"\nFluid temperature profile:");
            foreach (var (depth, tDown, tUp) in _results.FluidTemperatureProfile.Take(5))
            {
                Logger.Log($"  Depth {depth:F1}m: down={tDown:F2}K ({tDown - 273.15:F2}°C), up={tUp:F2}K ({tUp - 273.15:F2}°C)");
            }
        }
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