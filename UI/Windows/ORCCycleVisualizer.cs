using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Windows
{
    /// <summary>
    /// Visualizes ORC thermodynamic cycles using P-h and T-s diagrams.
    /// Shows state points, cycle path, and saturation curves.
    /// </summary>
    public class ORCCycleVisualizer
    {
        private bool _isOpen = true;
        private int _selectedDiagram = 0; // 0 = P-h, 1 = T-s
        private readonly string[] _diagramTypes = { "P-h Diagram", "T-s Diagram" };

        // Cycle state points (4-point simple cycle)
        private ORCStatePoint[] _statePoints = new ORCStatePoint[4];
        private bool _showSaturationCurve = true;
        private bool _showIsobars = true;
        private bool _showIsotherms = false;
        private bool _animateCycle = false;
        private float _animationPhase = 0;

        // Working fluid properties (simplified for isobutane)
        private readonly string[] _fluids = { "Isobutane (R600a)", "R134a", "R245fa", "n-Pentane", "Toluene" };
        private int _selectedFluid = 0;

        // Diagram bounds
        private float _minH = 200, _maxH = 600;   // kJ/kg
        private float _minS = 1.0f, _maxS = 2.5f; // kJ/(kg·K)
        private float _minP = 0.5f, _maxP = 30f;  // bar
        private float _minT = 0, _maxT = 150;     // °C

        public bool IsOpen => _isOpen;

        public ORCCycleVisualizer()
        {
            InitializeDefaultCycle();
        }

        private void InitializeDefaultCycle()
        {
            // Default isobutane ORC cycle
            _statePoints[0] = new ORCStatePoint
            {
                Name = "1: Pump Inlet",
                Temperature = 30,
                Pressure = 2.0f,
                Enthalpy = 250,
                Entropy = 1.15f,
                Quality = 0 // Subcooled liquid
            };

            _statePoints[1] = new ORCStatePoint
            {
                Name = "2: Pump Outlet",
                Temperature = 32,
                Pressure = 15.0f,
                Enthalpy = 255,
                Entropy = 1.16f,
                Quality = 0
            };

            _statePoints[2] = new ORCStatePoint
            {
                Name = "3: Turbine Inlet",
                Temperature = 100,
                Pressure = 15.0f,
                Enthalpy = 520,
                Entropy = 2.05f,
                Quality = 1 // Superheated vapor
            };

            _statePoints[3] = new ORCStatePoint
            {
                Name = "4: Turbine Outlet",
                Temperature = 40,
                Pressure = 2.0f,
                Enthalpy = 470,
                Entropy = 2.10f,
                Quality = 0.95f // Slightly wet
            };
        }

        public void SetCycleFromParameters(double evapPressure, double condPressure, double turbineEff, double pumpEff)
        {
            // Update state points based on input parameters
            // Simplified thermodynamic calculation
            _statePoints[0].Pressure = (float)condPressure;
            _statePoints[1].Pressure = (float)evapPressure;
            _statePoints[2].Pressure = (float)evapPressure;
            _statePoints[3].Pressure = (float)condPressure;

            // Recalculate enthalpies (simplified)
            double pumpWork = (_statePoints[1].Pressure - _statePoints[0].Pressure) * 100 / (1000 * pumpEff);
            _statePoints[1].Enthalpy = _statePoints[0].Enthalpy + (float)pumpWork;

            double turbineWork = (_statePoints[2].Enthalpy - _statePoints[3].Enthalpy) * turbineEff;
            _statePoints[3].Enthalpy = _statePoints[2].Enthalpy - (float)turbineWork;
        }

        public void Draw()
        {
            if (!_isOpen) return;

            ImGui.SetNextWindowSize(new Vector2(700, 550), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("ORC Cycle Diagram", ref _isOpen))
            {
                DrawToolbar();
                ImGui.Separator();

                // Two columns: diagram on left, data on right
                ImGui.Columns(2, "CycleVisCols", true);
                ImGui.SetColumnWidth(0, 450);

                DrawDiagram();

                ImGui.NextColumn();

                DrawStatePointPanel();
                DrawCycleMetrics();

                ImGui.Columns(1);
            }
            ImGui.End();
        }

        private void DrawToolbar()
        {
            // Diagram selector
            ImGui.Text("Diagram:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            ImGui.Combo("##DiagramType", ref _selectedDiagram, _diagramTypes, _diagramTypes.Length);

            ImGui.SameLine();

            // Fluid selector
            ImGui.Text("Fluid:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("##Fluid", ref _selectedFluid, _fluids, _fluids.Length))
            {
                UpdateFluidProperties();
            }

            ImGui.SameLine();
            ImGui.Checkbox("Saturation Curve", ref _showSaturationCurve);
            ImGui.SameLine();
            ImGui.Checkbox("Animate", ref _animateCycle);
        }

        private void DrawDiagram()
        {
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = new Vector2(430, 350);

            var drawList = ImGui.GetWindowDrawList();

            // Background
            drawList.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF1A1A1A);
            drawList.AddRect(canvasPos, canvasPos + canvasSize, 0xFF404040);

            // Margins
            var margin = new Vector2(50, 40);
            var plotArea = canvasSize - margin * 2;
            var plotOrigin = canvasPos + new Vector2(margin.X, canvasSize.Y - margin.Y);

            // Draw grid
            DrawGrid(drawList, plotOrigin, plotArea);

            // Draw saturation curve
            if (_showSaturationCurve)
            {
                DrawSaturationCurve(drawList, plotOrigin, plotArea);
            }

            // Draw isobars/isotherms
            if (_showIsobars && _selectedDiagram == 0) DrawIsobars(drawList, plotOrigin, plotArea);
            if (_showIsotherms && _selectedDiagram == 1) DrawIsotherms(drawList, plotOrigin, plotArea);

            // Draw cycle
            DrawCyclePath(drawList, plotOrigin, plotArea);

            // Draw state points
            DrawStatePoints(drawList, plotOrigin, plotArea);

            // Draw axes labels
            DrawAxesLabels(drawList, canvasPos, canvasSize, margin);

            // Handle animation
            if (_animateCycle)
            {
                _animationPhase += 0.02f;
                if (_animationPhase > 4) _animationPhase = 0;
            }

            ImGui.InvisibleButton("DiagramCanvas", canvasSize);
        }

        private void DrawGrid(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            uint gridColor = 0xFF303030;
            int gridLines = 5;

            for (int i = 0; i <= gridLines; i++)
            {
                float t = (float)i / gridLines;

                // Horizontal lines
                var y = origin.Y - t * size.Y;
                drawList.AddLine(new Vector2(origin.X, y), new Vector2(origin.X + size.X, y), gridColor);

                // Vertical lines
                var x = origin.X + t * size.X;
                drawList.AddLine(new Vector2(x, origin.Y), new Vector2(x, origin.Y - size.Y), gridColor);
            }
        }

        private void DrawSaturationCurve(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            // Simplified saturation dome for isobutane
            var points = new List<Vector2>();
            uint color = 0xFF00AAFF; // Cyan

            if (_selectedDiagram == 0) // P-h
            {
                // Liquid line
                for (float p = _minP; p <= _maxP; p += 0.5f)
                {
                    float h = 200 + p * 5; // Simplified
                    points.Add(TransformToPlot(h, p, origin, size, true));
                }

                // Add critical point
                points.Add(TransformToPlot(400, 36, origin, size, true));

                // Vapor line (reverse)
                for (float p = _maxP; p >= _minP; p -= 0.5f)
                {
                    float h = 500 - p * 2; // Simplified
                    points.Add(TransformToPlot(h, p, origin, size, true));
                }
            }
            else // T-s
            {
                // Liquid line
                for (float t = _minT; t <= 135; t += 5)
                {
                    float s = 1.0f + t * 0.005f;
                    points.Add(TransformToPlot(s, t, origin, size, false));
                }

                // Vapor line
                for (float t = 135; t >= _minT; t -= 5)
                {
                    float s = 2.3f - (135 - t) * 0.003f;
                    points.Add(TransformToPlot(s, t, origin, size, false));
                }
            }

            if (points.Count > 2)
            {
                for (int i = 0; i < points.Count - 1; i++)
                {
                    drawList.AddLine(points[i], points[i + 1], color, 2.0f);
                }
            }
        }

        private void DrawIsobars(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            uint color = 0x80808080;
            float[] pressures = { 2, 5, 10, 15, 20 };

            foreach (var p in pressures)
            {
                var p1 = TransformToPlot(_minH, p, origin, size, true);
                var p2 = TransformToPlot(_maxH, p, origin, size, true);
                drawList.AddLine(p1, p2, color, 1.0f);
            }
        }

        private void DrawIsotherms(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            uint color = 0x80808080;
            float[] temps = { 20, 40, 60, 80, 100, 120 };

            foreach (var t in temps)
            {
                var p1 = TransformToPlot(_minS, t, origin, size, false);
                var p2 = TransformToPlot(_maxS, t, origin, size, false);
                drawList.AddLine(p1, p2, color, 1.0f);
            }
        }

        private void DrawCyclePath(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            uint[] colors = { 0xFF00FF00, 0xFFFF8800, 0xFFFF0000, 0xFF0088FF }; // Green, Orange, Red, Blue

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                Vector2 p1, p2;

                if (_selectedDiagram == 0) // P-h
                {
                    p1 = TransformToPlot(_statePoints[i].Enthalpy, _statePoints[i].Pressure, origin, size, true);
                    p2 = TransformToPlot(_statePoints[next].Enthalpy, _statePoints[next].Pressure, origin, size, true);
                }
                else // T-s
                {
                    p1 = TransformToPlot(_statePoints[i].Entropy, _statePoints[i].Temperature, origin, size, false);
                    p2 = TransformToPlot(_statePoints[next].Entropy, _statePoints[next].Temperature, origin, size, false);
                }

                drawList.AddLine(p1, p2, colors[i], 3.0f);

                // Draw arrow
                var dir = Vector2.Normalize(p2 - p1);
                var mid = (p1 + p2) / 2;
                var perp = new Vector2(-dir.Y, dir.X);
                drawList.AddTriangleFilled(mid + dir * 8, mid - dir * 4 + perp * 5, mid - dir * 4 - perp * 5, colors[i]);
            }
        }

        private void DrawStatePoints(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 pos;
                if (_selectedDiagram == 0)
                    pos = TransformToPlot(_statePoints[i].Enthalpy, _statePoints[i].Pressure, origin, size, true);
                else
                    pos = TransformToPlot(_statePoints[i].Entropy, _statePoints[i].Temperature, origin, size, false);

                // Animate selected point
                float radius = 8;
                if (_animateCycle && (int)_animationPhase == i)
                    radius = 12;

                drawList.AddCircleFilled(pos, radius, 0xFFFFFFFF);
                drawList.AddCircle(pos, radius, 0xFF000000, 0, 2);

                // Label
                drawList.AddText(pos + new Vector2(10, -10), 0xFFFFFFFF, $"{i + 1}");
            }
        }

        private void DrawAxesLabels(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, Vector2 margin)
        {
            // X-axis label
            string xLabel = _selectedDiagram == 0 ? "Enthalpy h [kJ/kg]" : "Entropy s [kJ/(kg·K)]";
            drawList.AddText(canvasPos + new Vector2(canvasSize.X / 2 - 60, canvasSize.Y - 20), 0xFFFFFFFF, xLabel);

            // Y-axis label (rotated text not supported, using vertical placement)
            string yLabel = _selectedDiagram == 0 ? "P [bar]" : "T [°C]";
            drawList.AddText(canvasPos + new Vector2(5, canvasSize.Y / 2), 0xFFFFFFFF, yLabel);

            // Axis values
            float xMin = _selectedDiagram == 0 ? _minH : _minS;
            float xMax = _selectedDiagram == 0 ? _maxH : _maxS;
            float yMin = _selectedDiagram == 0 ? _minP : _minT;
            float yMax = _selectedDiagram == 0 ? _maxP : _maxT;

            // X values
            for (int i = 0; i <= 4; i++)
            {
                float val = xMin + (xMax - xMin) * i / 4;
                float x = margin.X + (canvasSize.X - 2 * margin.X) * i / 4;
                drawList.AddText(canvasPos + new Vector2(x - 15, canvasSize.Y - 35), 0xFFAAAAAA, $"{val:F0}");
            }

            // Y values
            for (int i = 0; i <= 4; i++)
            {
                float val = yMin + (yMax - yMin) * i / 4;
                float y = canvasSize.Y - margin.Y - (canvasSize.Y - 2 * margin.Y) * i / 4;
                drawList.AddText(canvasPos + new Vector2(5, y - 8), 0xFFAAAAAA, $"{val:F1}");
            }
        }

        private Vector2 TransformToPlot(float x, float y, Vector2 origin, Vector2 size, bool isPH)
        {
            float xMin, xMax, yMin, yMax;
            if (isPH)
            {
                xMin = _minH; xMax = _maxH;
                yMin = _minP; yMax = _maxP;
            }
            else
            {
                xMin = _minS; xMax = _maxS;
                yMin = _minT; yMax = _maxT;
            }

            float px = origin.X + (x - xMin) / (xMax - xMin) * size.X;
            float py = origin.Y - (y - yMin) / (yMax - yMin) * size.Y;
            return new Vector2(px, py);
        }

        private void DrawStatePointPanel()
        {
            ImGui.Text("State Points");
            ImGui.Separator();

            ImGui.BeginChild("StatePoints", new Vector2(0, 180), ImGuiChildFlags.Border);

            for (int i = 0; i < 4; i++)
            {
                if (ImGui.TreeNode($"Point {i + 1}: {GetProcessName(i)}"))
                {
                    ImGui.Text($"T = {_statePoints[i].Temperature:F1} °C");
                    ImGui.Text($"P = {_statePoints[i].Pressure:F2} bar");
                    ImGui.Text($"h = {_statePoints[i].Enthalpy:F1} kJ/kg");
                    ImGui.Text($"s = {_statePoints[i].Entropy:F3} kJ/(kg·K)");
                    if (_statePoints[i].Quality < 1)
                        ImGui.Text($"x = {_statePoints[i].Quality:F2}");
                    ImGui.TreePop();
                }
            }

            ImGui.EndChild();
        }

        private void DrawCycleMetrics()
        {
            ImGui.Text("Cycle Performance");
            ImGui.Separator();

            // Calculate cycle metrics
            float turbineWork = _statePoints[2].Enthalpy - _statePoints[3].Enthalpy;
            float pumpWork = _statePoints[1].Enthalpy - _statePoints[0].Enthalpy;
            float heatInput = _statePoints[2].Enthalpy - _statePoints[1].Enthalpy;
            float heatRejection = _statePoints[3].Enthalpy - _statePoints[0].Enthalpy;
            float netWork = turbineWork - pumpWork;
            float efficiency = netWork / heatInput * 100;

            ImGui.BeginChild("Metrics", new Vector2(0, 0), ImGuiChildFlags.Border);

            ImGui.TextColored(new Vector4(0.5f, 1, 0.5f, 1), $"Turbine Work: {turbineWork:F1} kJ/kg");
            ImGui.TextColored(new Vector4(1, 0.5f, 0.5f, 1), $"Pump Work: {pumpWork:F1} kJ/kg");
            ImGui.Separator();
            ImGui.TextColored(new Vector4(1, 0.8f, 0.3f, 1), $"Heat Input: {heatInput:F1} kJ/kg");
            ImGui.TextColored(new Vector4(0.3f, 0.8f, 1, 1), $"Heat Rejection: {heatRejection:F1} kJ/kg");
            ImGui.Separator();
            ImGui.TextColored(new Vector4(1, 1, 1, 1), $"Net Work: {netWork:F1} kJ/kg");
            ImGui.TextColored(new Vector4(1, 1, 0, 1), $"Thermal Efficiency: {efficiency:F1}%");

            // Back-work ratio
            float bwr = pumpWork / turbineWork * 100;
            ImGui.Text($"Back-Work Ratio: {bwr:F1}%");

            ImGui.EndChild();
        }

        private string GetProcessName(int index)
        {
            return index switch
            {
                0 => "Pump Inlet (Sat. Liquid)",
                1 => "Pump Outlet",
                2 => "Turbine Inlet (Superheat)",
                3 => "Turbine Outlet",
                _ => "Unknown"
            };
        }

        private void UpdateFluidProperties()
        {
            // Update saturation curve bounds based on selected fluid
            switch (_selectedFluid)
            {
                case 0: // Isobutane
                    _minH = 200; _maxH = 600;
                    _minS = 1.0f; _maxS = 2.5f;
                    break;
                case 1: // R134a
                    _minH = 150; _maxH = 450;
                    _minS = 0.8f; _maxS = 2.0f;
                    break;
                case 2: // R245fa
                    _minH = 200; _maxH = 500;
                    _minS = 1.0f; _maxS = 2.2f;
                    break;
                case 3: // n-Pentane
                    _minH = 0; _maxH = 600;
                    _minS = 0.5f; _maxS = 2.5f;
                    break;
                case 4: // Toluene
                    _minH = 0; _maxH = 700;
                    _minS = 0.0f; _maxS = 2.5f;
                    break;
            }
        }
    }

    public class ORCStatePoint
    {
        public string Name { get; set; } = "";
        public float Temperature { get; set; } // °C
        public float Pressure { get; set; }    // bar
        public float Enthalpy { get; set; }    // kJ/kg
        public float Entropy { get; set; }     // kJ/(kg·K)
        public float Quality { get; set; }     // 0-1 (vapor fraction)
    }
}
