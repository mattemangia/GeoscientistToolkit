using System;
using System.Numerics;
using ImGuiNET;
using Veldrid;

namespace GeoscientistToolkit.Analysis.Geothermal
{
    /// <summary>
    /// 2D visualization for ORC simulation results with colormap support
    /// Displays power output, efficiency, mass flow, and economic metrics over time or temperature range
    /// </summary>
    public class ORCVisualization : IDisposable
    {
        private GraphicsDevice _graphicsDevice;
        private ORCSimulation.ORCCycleResults[] _results;
        private float[] _temperatureRange;
        private EconomicResults _economics;

        // Visualization settings
        private ORCVisualizationMode _currentMode = ORCVisualizationMode.PowerOutput;
        private ColorMap _currentColorMap = ColorMap.Turbo;
        private bool _showEconomics = false;
        private bool _showSensitivity = false;

        // Plot data
        private float _minValue;
        private float _maxValue;
        private float[] _plotData;

        public ORCVisualization(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
        }

        #region Data Update

        /// <summary>
        /// Update visualization with new ORC results
        /// </summary>
        public void UpdateResults(ORCSimulation.ORCCycleResults[] results, float[] temperatureRange)
        {
            _results = results;
            _temperatureRange = temperatureRange;
            UpdatePlotData();
        }

        /// <summary>
        /// Update economic analysis results
        /// </summary>
        public void UpdateEconomics(EconomicResults economics)
        {
            _economics = economics;
        }

        private void UpdatePlotData()
        {
            if (_results == null || _results.Length == 0) return;

            _plotData = new float[_results.Length];
            _minValue = float.MaxValue;
            _maxValue = float.MinValue;

            for (int i = 0; i < _results.Length; i++)
            {
                float value = _currentMode switch
                {
                    ORCVisualizationMode.PowerOutput => _results[i].NetPower / 1e6f, // MW
                    ORCVisualizationMode.ThermalEfficiency => _results[i].ThermalEfficiency * 100f, // %
                    ORCVisualizationMode.MassFlowRate => _results[i].MassFlowRate,
                    ORCVisualizationMode.TurbineWork => _results[i].TurbineWork / 1e6f, // MW
                    ORCVisualizationMode.SpecificPower => _results[i].SpecificPower / 1000f, // kW/(kg/s)
                    ORCVisualizationMode.HeatInput => _results[i].HeatInput / 1e6f, // MW
                    ORCVisualizationMode.ExergyEfficiency => _results[i].ExergyEfficiency * 100f, // %
                    _ => 0f
                };

                _plotData[i] = value;
                _minValue = MathF.Min(_minValue, value);
                _maxValue = MathF.Max(_maxValue, value);
            }
        }

        #endregion

        #region Rendering

        /// <summary>
        /// Render ORC visualization UI
        /// </summary>
        public void RenderUI()
        {
            if (_results == null || _results.Length == 0)
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No ORC simulation results available");
                return;
            }

            ImGui.Text($"ORC Simulation Results ({_results.Length} data points)");
            ImGui.Separator();

            // Visualization mode selection
            if (ImGui.BeginCombo("Visualization Mode", GetModeLabel(_currentMode)))
            {
                foreach (ORCVisualizationMode mode in Enum.GetValues<ORCVisualizationMode>())
                {
                    if (ImGui.Selectable(GetModeLabel(mode), _currentMode == mode))
                    {
                        _currentMode = mode;
                        UpdatePlotData();
                    }
                }
                ImGui.EndCombo();
            }

            // Colormap selection
            if (ImGui.BeginCombo("Color Map", _currentColorMap.ToString()))
            {
                foreach (ColorMap map in Enum.GetValues<ColorMap>())
                {
                    if (ImGui.Selectable(map.ToString(), _currentColorMap == map))
                    {
                        _currentColorMap = map;
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.Separator();

            // Display current statistics
            DisplayStatistics();

            ImGui.Separator();

            // Plot area
            RenderPlot();

            ImGui.Separator();

            // Economic results
            ImGui.Checkbox("Show Economic Analysis", ref _showEconomics);
            if (_showEconomics && _economics != null)
            {
                RenderEconomics();
            }

            // Sensitivity analysis
            ImGui.Checkbox("Show Sensitivity Analysis", ref _showSensitivity);
            if (_showSensitivity && _economics?.SensitivityAnalysis != null)
            {
                RenderSensitivity();
            }
        }

        private void DisplayStatistics()
        {
            if (_plotData == null || _plotData.Length == 0) return;

            float avg = 0f;
            for (int i = 0; i < _plotData.Length; i++)
                avg += _plotData[i];
            avg /= _plotData.Length;

            string units = GetModeUnits(_currentMode);
            ImGui.Text($"Min: {_minValue:F2} {units}");
            ImGui.SameLine(200);
            ImGui.Text($"Max: {_maxValue:F2} {units}");
            ImGui.SameLine(400);
            ImGui.Text($"Avg: {avg:F2} {units}");

            // Additional ORC-specific statistics
            if (_results != null && _results.Length > 0)
            {
                int maxPowerIdx = 0;
                float maxPower = 0;
                for (int i = 0; i < _results.Length; i++)
                {
                    if (_results[i].NetPower > maxPower)
                    {
                        maxPower = _results[i].NetPower;
                        maxPowerIdx = i;
                    }
                }

                ImGui.Separator();
                ImGui.TextColored(new Vector4(0.2f, 1f, 0.2f, 1), "Optimal Operating Point:");
                if (_temperatureRange != null && maxPowerIdx < _temperatureRange.Length)
                {
                    ImGui.Text($"  Geo Temp: {_temperatureRange[maxPowerIdx] - 273.15f:F1} °C");
                }
                ImGui.Text($"  Net Power: {_results[maxPowerIdx].NetPower / 1e6f:F2} MW");
                ImGui.Text($"  Efficiency: {_results[maxPowerIdx].ThermalEfficiency * 100f:F2} %");
                ImGui.Text($"  Mass Flow: {_results[maxPowerIdx].MassFlowRate:F2} kg/s");
            }
        }

        private void RenderPlot()
        {
            if (_plotData == null || _plotData.Length == 0) return;

            Vector2 plotSize = new Vector2(ImGui.GetContentRegionAvail().X - 20, 300);

            // Create colored plot with gradient
            ImGui.BeginChild("ORCPlot", plotSize, ImGuiChildFlags.Border);

            var drawList = ImGui.GetWindowDrawList();
            Vector2 plotMin = ImGui.GetCursorScreenPos();
            Vector2 plotMax = plotMin + plotSize;

            // Background
            drawList.AddRectFilled(plotMin, plotMax, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1)));

            // Draw grid
            int numGridLines = 5;
            for (int i = 0; i <= numGridLines; i++)
            {
                float y = plotMin.Y + (plotSize.Y * i / numGridLines);
                drawList.AddLine(
                    new Vector2(plotMin.X, y),
                    new Vector2(plotMax.X, y),
                    ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 1)),
                    1.0f
                );

                // Grid labels
                float value = _maxValue - (_maxValue - _minValue) * i / numGridLines;
                string units = GetModeUnits(_currentMode);
                ImGui.SetCursorScreenPos(new Vector2(plotMax.X + 5, y - 8));
                ImGui.Text($"{value:F1}{units}");
            }

            // Draw plot line with colormap
            if (_plotData.Length > 1)
            {
                for (int i = 0; i < _plotData.Length - 1; i++)
                {
                    float x1 = plotMin.X + (plotSize.X * i / (_plotData.Length - 1));
                    float x2 = plotMin.X + (plotSize.X * (i + 1) / (_plotData.Length - 1));

                    float t1 = (_plotData[i] - _minValue) / (_maxValue - _minValue);
                    float t2 = (_plotData[i + 1] - _minValue) / (_maxValue - _minValue);
                    t1 = Math.Clamp(t1, 0f, 1f);
                    t2 = Math.Clamp(t2, 0f, 1f);

                    float y1 = plotMax.Y - (plotSize.Y * t1);
                    float y2 = plotMax.Y - (plotSize.Y * t2);

                    Vector4 color1 = GetColormapColor(t1);
                    Vector4 color2 = GetColormapColor(t2);

                    drawList.AddLine(
                        new Vector2(x1, y1),
                        new Vector2(x2, y2),
                        ImGui.GetColorU32(color1),
                        3.0f
                    );

                    // Add filled area under curve
                    drawList.AddQuadFilled(
                        new Vector2(x1, plotMax.Y),
                        new Vector2(x2, plotMax.Y),
                        new Vector2(x2, y2),
                        new Vector2(x1, y1),
                        ImGui.GetColorU32(new Vector4(color1.X, color1.Y, color1.Z, 0.3f))
                    );
                }
            }

            // X-axis labels (temperature)
            if (_temperatureRange != null && _temperatureRange.Length == _plotData.Length)
            {
                int numLabels = Math.Min(5, _temperatureRange.Length);
                for (int i = 0; i < numLabels; i++)
                {
                    int idx = i * (_temperatureRange.Length - 1) / (numLabels - 1);
                    float x = plotMin.X + (plotSize.X * idx / (_temperatureRange.Length - 1));
                    float tempC = _temperatureRange[idx] - 273.15f;

                    ImGui.SetCursorScreenPos(new Vector2(x - 20, plotMax.Y + 5));
                    ImGui.Text($"{tempC:F0}°C");
                }
            }

            ImGui.EndChild();

            // Colorbar legend
            RenderColorBar(new Vector2(plotSize.X, 30));
        }

        private void RenderColorBar(Vector2 size)
        {
            ImGui.Text("Color Scale:");
            ImGui.SameLine();

            Vector2 barPos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();

            // Draw gradient bar
            int segments = 100;
            for (int i = 0; i < segments; i++)
            {
                float t1 = (float)i / segments;
                float t2 = (float)(i + 1) / segments;

                Vector4 color1 = GetColormapColor(t1);
                Vector4 color2 = GetColormapColor(t2);

                Vector2 p1 = new Vector2(barPos.X + size.X * t1, barPos.Y);
                Vector2 p2 = new Vector2(barPos.X + size.X * t2, barPos.Y + size.Y);

                drawList.AddRectFilledMultiColor(
                    p1, p2,
                    ImGui.GetColorU32(color1),
                    ImGui.GetColorU32(color2),
                    ImGui.GetColorU32(color2),
                    ImGui.GetColorU32(color1)
                );
            }

            // Border
            drawList.AddRect(barPos, barPos + size, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), 0, 0, 2.0f);

            ImGui.Dummy(size);
        }

        private void RenderEconomics()
        {
            if (_economics == null) return;

            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.2f, 1f, 1f, 1), "Economic Analysis Results");
            ImGui.Separator();

            ImGui.Columns(2, "economics", true);

            // Capital costs
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1), "Capital Costs:");
            ImGui.Text($"Drilling: ${_economics.DrillingCostsMUSD:F2} M");
            ImGui.Text($"Power Plant: ${_economics.PowerPlantCostsMUSD:F2} M");
            ImGui.Text($"Infrastructure: ${_economics.InfrastructureCostsMUSD:F2} M");
            ImGui.TextColored(new Vector4(1f, 1f, 0.2f, 1), $"Total CAPEX: ${_economics.TotalCapitalCostMUSD:F2} M");

            ImGui.NextColumn();

            // Financial metrics
            ImGui.TextColored(new Vector4(0.2f, 1f, 0.2f, 1), "Financial Metrics:");
            ImGui.Text($"NPV: ${_economics.NPV_MUSD:F2} M");
            ImGui.Text($"IRR: {_economics.IRR:F2} %");
            ImGui.Text($"Payback: {_economics.PaybackPeriodYears:F1} years");
            ImGui.Text($"LCOE: ${_economics.LCOE_USDperMWh:F2} /MWh");
            ImGui.Text($"ROI: {_economics.ROI:F1} %");

            ImGui.Columns(1);
            ImGui.Separator();

            // Annual metrics
            ImGui.Text($"Average Power: {_economics.AverageNetPowerMW:F2} MW");
            ImGui.Text($"Annual Energy: {_economics.AnnualEnergyProductionMWh:F0} MWh");
            ImGui.Text($"Annual Revenue: ${_economics.AnnualRevenueMUSD:F2} M");
            ImGui.Text($"Annual OPEX: ${_economics.AnnualOperatingCostMUSD:F2} M");

            // Project viability indicator
            ImGui.Separator();
            if (_economics.NPV_MUSD > 0 && _economics.IRR > 8.0f)
            {
                ImGui.TextColored(new Vector4(0.2f, 1f, 0.2f, 1), "Project appears economically viable");
            }
            else if (_economics.NPV_MUSD > 0)
            {
                ImGui.TextColored(new Vector4(1f, 1f, 0.2f, 1), "Marginal project viability");
            }
            else
            {
                ImGui.TextColored(new Vector4(1f, 0.2f, 0.2f, 1), "Project not economically viable");
            }
        }

        private void RenderSensitivity()
        {
            if (_economics?.SensitivityAnalysis == null) return;

            ImGui.Separator();
            ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1), "Sensitivity Analysis");
            ImGui.Separator();

            var sensitivity = _economics.SensitivityAnalysis;

            // Electricity price sensitivity
            if (sensitivity.ElectricityPriceVariation != null && sensitivity.ElectricityPriceVariation.Count > 0)
            {
                ImGui.Text("Electricity Price Sensitivity:");
                RenderSensitivityPlot(sensitivity.ElectricityPriceVariation, "Price Variation (%)", "NPV (M$)");
            }

            ImGui.Separator();

            // Capital cost sensitivity
            if (sensitivity.CapitalCostVariation != null && sensitivity.CapitalCostVariation.Count > 0)
            {
                ImGui.Text("Capital Cost Sensitivity:");
                RenderSensitivityPlot(sensitivity.CapitalCostVariation, "Cost Variation (%)", "NPV (M$)");
            }

            ImGui.Separator();

            // Discount rate sensitivity
            if (sensitivity.DiscountRateVariation != null && sensitivity.DiscountRateVariation.Count > 0)
            {
                ImGui.Text("Discount Rate Sensitivity:");
                RenderSensitivityPlot(sensitivity.DiscountRateVariation, "Discount Rate (%)", "NPV (M$)");
            }
        }

        private void RenderSensitivityPlot(System.Collections.Generic.List<(float param, float npv)> data, string xLabel, string yLabel)
        {
            Vector2 plotSize = new Vector2(400, 150);

            ImGui.BeginChild($"SensPlot_{xLabel}", plotSize, ImGuiChildFlags.Border);

            var drawList = ImGui.GetWindowDrawList();
            Vector2 plotMin = ImGui.GetCursorScreenPos();
            Vector2 plotMax = plotMin + plotSize;

            // Background
            drawList.AddRectFilled(plotMin, plotMax, ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 1)));

            // Find data range
            float minParam = float.MaxValue, maxParam = float.MinValue;
            float minNPV = float.MaxValue, maxNPV = float.MinValue;

            foreach (var (param, npv) in data)
            {
                minParam = MathF.Min(minParam, param);
                maxParam = MathF.Max(maxParam, param);
                minNPV = MathF.Min(minNPV, npv);
                maxNPV = MathF.Max(maxNPV, npv);
            }

            // Draw zero line (NPV = 0)
            if (minNPV < 0 && maxNPV > 0)
            {
                float y0 = plotMax.Y - (plotSize.Y * (0 - minNPV) / (maxNPV - minNPV));
                drawList.AddLine(
                    new Vector2(plotMin.X, y0),
                    new Vector2(plotMax.X, y0),
                    ImGui.GetColorU32(new Vector4(1f, 0f, 0f, 0.7f)),
                    2.0f
                );
            }

            // Plot data points
            for (int i = 0; i < data.Count - 1; i++)
            {
                var (param1, npv1) = data[i];
                var (param2, npv2) = data[i + 1];

                float x1 = plotMin.X + (plotSize.X * (param1 - minParam) / (maxParam - minParam));
                float x2 = plotMin.X + (plotSize.X * (param2 - minParam) / (maxParam - minParam));

                float y1 = plotMax.Y - (plotSize.Y * (npv1 - minNPV) / (maxNPV - minNPV));
                float y2 = plotMax.Y - (plotSize.Y * (npv2 - minNPV) / (maxNPV - minNPV));

                Vector4 color = npv1 > 0 ? new Vector4(0.2f, 1f, 0.2f, 1) : new Vector4(1f, 0.2f, 0.2f, 1);

                drawList.AddLine(new Vector2(x1, y1), new Vector2(x2, y2), ImGui.GetColorU32(color), 2.5f);
                drawList.AddCircleFilled(new Vector2(x1, y1), 4.0f, ImGui.GetColorU32(color));
            }

            ImGui.EndChild();

            ImGui.Text($"{xLabel} vs {yLabel}");
        }

        #endregion

        #region Colormap Functions

        private Vector4 GetColormapColor(float t)
        {
            t = Math.Clamp(t, 0f, 1f);

            return _currentColorMap switch
            {
                ColorMap.Turbo => GetTurboColor(t),
                ColorMap.Viridis => GetViridisColor(t),
                ColorMap.Plasma => GetPlasmaColor(t),
                ColorMap.Inferno => GetInfernoColor(t),
                ColorMap.Magma => GetMagmaColor(t),
                ColorMap.Jet => GetJetColor(t),
                ColorMap.Rainbow => GetRainbowColor(t),
                ColorMap.Thermal => GetThermalColor(t),
                ColorMap.BlueRed => GetBlueRedColor(t),
                _ => new Vector4(t, t, t, 1)
            };
        }

        private Vector4 GetTurboColor(float t)
        {
            // Google Turbo colormap (simplified)
            if (t < 0.25f)
            {
                float s = t / 0.25f;
                return new Vector4(0.19f + s * 0.3f, 0.07f + s * 0.5f, 0.48f + s * 0.4f, 1);
            }
            else if (t < 0.5f)
            {
                float s = (t - 0.25f) / 0.25f;
                return new Vector4(0.49f + s * 0.4f, 0.57f + s * 0.3f, 0.88f - s * 0.3f, 1);
            }
            else if (t < 0.75f)
            {
                float s = (t - 0.5f) / 0.25f;
                return new Vector4(0.89f + s * 0.1f, 0.87f - s * 0.2f, 0.58f - s * 0.3f, 1);
            }
            else
            {
                float s = (t - 0.75f) / 0.25f;
                return new Vector4(0.99f, 0.67f - s * 0.4f, 0.28f - s * 0.2f, 1);
            }
        }

        private Vector4 GetViridisColor(float t)
        {
            // Viridis colormap approximation
            float r = 0.28f * (1 - t) + 0.99f * t;
            float g = 0.00f * (1 - t) + 0.91f * t;
            float b = 0.56f * (1 - t) + 0.14f * t;
            return new Vector4(r, g, b, 1);
        }

        private Vector4 GetPlasmaColor(float t)
        {
            float r = 0.05f + 0.95f * t;
            float g = 0.03f + 0.77f * MathF.Sin(t * MathF.PI);
            float b = 0.53f * (1 - t);
            return new Vector4(r, g, b, 1);
        }

        private Vector4 GetInfernoColor(float t)
        {
            float r = t;
            float g = t * t;
            float b = MathF.Sqrt(t) * 0.5f;
            return new Vector4(r, g, b, 1);
        }

        private Vector4 GetMagmaColor(float t)
        {
            float r = t;
            float g = t * t * 0.8f;
            float b = MathF.Sqrt(t) * 0.7f;
            return new Vector4(r, g, b, 1);
        }

        private Vector4 GetJetColor(float t)
        {
            float r = Math.Clamp(1.5f - MathF.Abs(4 * t - 3), 0, 1);
            float g = Math.Clamp(1.5f - MathF.Abs(4 * t - 2), 0, 1);
            float b = Math.Clamp(1.5f - MathF.Abs(4 * t - 1), 0, 1);
            return new Vector4(r, g, b, 1);
        }

        private Vector4 GetRainbowColor(float t)
        {
            float r = MathF.Sin(t * MathF.PI * 2);
            float g = MathF.Sin((t + 0.333f) * MathF.PI * 2);
            float b = MathF.Sin((t + 0.666f) * MathF.PI * 2);
            return new Vector4(r * 0.5f + 0.5f, g * 0.5f + 0.5f, b * 0.5f + 0.5f, 1);
        }

        private Vector4 GetThermalColor(float t)
        {
            // Black -> Red -> Yellow -> White
            if (t < 0.33f)
            {
                float s = t / 0.33f;
                return new Vector4(s, 0, 0, 1);
            }
            else if (t < 0.66f)
            {
                float s = (t - 0.33f) / 0.33f;
                return new Vector4(1, s, 0, 1);
            }
            else
            {
                float s = (t - 0.66f) / 0.34f;
                return new Vector4(1, 1, s, 1);
            }
        }

        private Vector4 GetBlueRedColor(float t)
        {
            // Blue -> White -> Red
            if (t < 0.5f)
            {
                float s = t * 2;
                return new Vector4(s, s, 1, 1);
            }
            else
            {
                float s = (t - 0.5f) * 2;
                return new Vector4(1, 1 - s, 1 - s, 1);
            }
        }

        #endregion

        #region Helper Methods

        private string GetModeLabel(ORCVisualizationMode mode)
        {
            return mode switch
            {
                ORCVisualizationMode.PowerOutput => "Net Power Output",
                ORCVisualizationMode.ThermalEfficiency => "Thermal Efficiency",
                ORCVisualizationMode.MassFlowRate => "Working Fluid Mass Flow",
                ORCVisualizationMode.TurbineWork => "Turbine Work Output",
                ORCVisualizationMode.SpecificPower => "Specific Power",
                ORCVisualizationMode.HeatInput => "Heat Input",
                ORCVisualizationMode.ExergyEfficiency => "Exergy Efficiency",
                _ => "Unknown"
            };
        }

        private string GetModeUnits(ORCVisualizationMode mode)
        {
            return mode switch
            {
                ORCVisualizationMode.PowerOutput => " MW",
                ORCVisualizationMode.ThermalEfficiency => " %",
                ORCVisualizationMode.MassFlowRate => " kg/s",
                ORCVisualizationMode.TurbineWork => " MW",
                ORCVisualizationMode.SpecificPower => " kW/(kg/s)",
                ORCVisualizationMode.HeatInput => " MW",
                ORCVisualizationMode.ExergyEfficiency => " %",
                _ => ""
            };
        }

        #endregion

        public void Dispose()
        {
            // No GPU resources to dispose in this simplified version
        }
    }

    #region Enums

    public enum ORCVisualizationMode
    {
        PowerOutput,
        ThermalEfficiency,
        MassFlowRate,
        TurbineWork,
        SpecificPower,
        HeatInput,
        ExergyEfficiency
    }

    // ColorMap enum (should match GeothermalVisualization3D.cs)
    public enum ColorMap
    {
        Turbo,
        Viridis,
        Plasma,
        Inferno,
        Magma,
        Jet,
        Rainbow,
        Thermal,
        BlueRed
    }

    #endregion
}
