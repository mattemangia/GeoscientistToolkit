using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Analysis.PhysicoChem;

namespace GeoscientistToolkit.UI.Windows
{
    /// <summary>
    /// ImGui-based 2D/3D visualizer for nuclear reactor simulation.
    /// Displays neutron flux, power distribution, temperatures, and reactor state.
    /// </summary>
    public class NuclearReactorVisualizer
    {
        private bool _isOpen = true;
        private NuclearReactorParameters? _params;
        private NuclearReactorSolver? _solver;
        private NuclearReactorState _state = new();
        private List<NuclearReactorState> _history = new();

        // View settings
        private int _viewMode = 0; // 0=2D Axial, 1=2D Radial, 2=3D, 3=Charts
        private readonly string[] _viewModes = { "2D Axial Slice", "2D Radial Slice", "3D Core View", "Time Charts" };

        private int _fieldVariable = 0;
        private readonly string[] _fieldVariables = { "Neutron Flux", "Power Density", "Fuel Temperature",
                                                       "Coolant Temperature", "Xenon Distribution" };

        private int _slicePosition = 50; // 0-100%
        private float _zoom = 1.0f;
        private Vector2 _pan = Vector2.Zero;
        private float _rotation = 0;

        // Color maps
        private readonly Vector4[] _heatmapColors = {
            new(0, 0, 0.5f, 1),      // Dark blue
            new(0, 0, 1, 1),          // Blue
            new(0, 1, 1, 1),          // Cyan
            new(0, 1, 0, 1),          // Green
            new(1, 1, 0, 1),          // Yellow
            new(1, 0.5f, 0, 1),       // Orange
            new(1, 0, 0, 1),          // Red
            new(1, 1, 1, 1)           // White (max)
        };

        // Simulation control
        private bool _isRunning = false;
        private double _simTime = 0;
        private double _simTimeStep = 0.01;
        private double _simEndTime = 100;

        // Transient parameters
        private double _rodInsertionRate = 0;
        private double _externalReactivity = 0;

        public bool IsOpen => _isOpen;

        public void SetReactorData(NuclearReactorParameters parameters, NuclearReactorSolver solver)
        {
            _params = parameters;
            _solver = solver;
            _state = solver.GetState();
        }

        public void Draw()
        {
            if (!_isOpen) return;

            ImGui.SetNextWindowSize(new Vector2(1000, 750), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Nuclear Reactor Visualization", ref _isOpen, ImGuiWindowFlags.MenuBar))
            {
                DrawMenuBar();
                DrawToolbar();

                // Main content area with panels
                ImGui.Columns(3, "ReactorVisColumns", true);
                ImGui.SetColumnWidth(0, 180);
                ImGui.SetColumnWidth(1, 550);

                DrawControlPanel();
                ImGui.NextColumn();

                DrawVisualization();
                ImGui.NextColumn();

                DrawInstrumentPanel();

                ImGui.Columns(1);
            }
            ImGui.End();
        }

        private void DrawMenuBar()
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("Export State...")) { }
                    if (ImGui.MenuItem("Export History...")) { }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("View"))
                {
                    for (int i = 0; i < _viewModes.Length; i++)
                    {
                        if (ImGui.MenuItem(_viewModes[i], "", _viewMode == i))
                            _viewMode = i;
                    }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Simulation"))
                {
                    if (ImGui.MenuItem("Start", "Space", false, !_isRunning)) _isRunning = true;
                    if (ImGui.MenuItem("Pause", "Space", false, _isRunning)) _isRunning = false;
                    if (ImGui.MenuItem("Reset")) ResetSimulation();
                    ImGui.Separator();
                    if (ImGui.MenuItem("SCRAM", "S")) PerformScram();
                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }
        }

        private void DrawToolbar()
        {
            // View mode selector
            ImGui.Text("View:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            ImGui.Combo("##ViewMode", ref _viewMode, _viewModes, _viewModes.Length);

            ImGui.SameLine();
            ImGui.Text("Field:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            ImGui.Combo("##FieldVar", ref _fieldVariable, _fieldVariables, _fieldVariables.Length);

            ImGui.SameLine();
            if (_viewMode < 2)
            {
                ImGui.Text("Slice:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                ImGui.SliderInt("##Slice", ref _slicePosition, 0, 100, "%d%%");
            }

            ImGui.SameLine(ImGui.GetWindowWidth() - 250);

            // Simulation controls
            if (_isRunning)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1));
                if (ImGui.Button("PAUSE")) _isRunning = false;
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.7f, 0.2f, 1));
                if (ImGui.Button("START")) _isRunning = true;
                ImGui.PopStyleColor();
            }

            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.1f, 0.1f, 1));
            if (ImGui.Button("SCRAM")) PerformScram();
            ImGui.PopStyleColor();

            ImGui.Separator();
        }

        private void DrawControlPanel()
        {
            ImGui.Text("Reactor Control");
            ImGui.Separator();

            ImGui.BeginChild("ControlPanel", new Vector2(0, 0), ImGuiChildFlags.Border);

            // Power control
            if (ImGui.CollapsingHeader("Power", ImGuiTreeNodeFlags.DefaultOpen))
            {
                float power = (float)(_state.RelativePower * 100);
                ImGui.Text($"Power: {power:F1}%");
                ImGui.ProgressBar(power / 100f, new Vector2(-1, 0), $"{power:F1}%");

                ImGui.Text($"Thermal: {_state.ThermalPowerMW:F0} MW");
            }

            // Control rods
            if (ImGui.CollapsingHeader("Control Rods", ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (_params != null)
                {
                    foreach (var bank in _params.ControlRodBanks)
                    {
                        float insertion = (float)bank.InsertionFraction * 100;
                        ImGui.Text($"{bank.Name}:");
                        ImGui.SameLine(100);
                        ImGui.SetNextItemWidth(70);
                        if (ImGui.SliderFloat($"##{bank.Name}", ref insertion, 0, 100, "%.0f%%"))
                        {
                            bank.InsertionFraction = insertion / 100.0;
                        }
                    }
                }

                ImGui.Separator();
                float rate = (float)_rodInsertionRate;
                ImGui.Text("Rod Speed:");
                if (ImGui.SliderFloat("##RodRate", ref rate, -10, 10, "%.1f %%/s"))
                {
                    _rodInsertionRate = rate;
                }
            }

            // Boron control
            if (ImGui.CollapsingHeader("Chemical Shim"))
            {
                if (_params != null)
                {
                    float boron = (float)_params.BoronConcentrationPPM;
                    if (ImGui.SliderFloat("Boron (ppm)", ref boron, 0, 2000))
                    {
                        _params.BoronConcentrationPPM = boron;
                    }
                    ImGui.Text($"Worth: {-10 * boron:F0} pcm");
                }
            }

            // External reactivity
            if (ImGui.CollapsingHeader("Perturbation"))
            {
                float rho = (float)(_externalReactivity * 100);
                ImGui.SliderFloat("Reactivity", ref rho, -100, 100, "%.1f pcm");
                _externalReactivity = rho / 100.0;
            }

            // Simulation settings
            if (ImGui.CollapsingHeader("Simulation"))
            {
                float dt = (float)(_simTimeStep * 1000);
                ImGui.SliderFloat("dt (ms)", ref dt, 1, 100);
                _simTimeStep = dt / 1000.0;

                float endTime = (float)_simEndTime;
                ImGui.SliderFloat("End (s)", ref endTime, 10, 1000);
                _simEndTime = endTime;
            }

            ImGui.EndChild();
        }

        private void DrawVisualization()
        {
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            canvasSize.Y -= 10;

            var drawList = ImGui.GetWindowDrawList();

            // Background
            drawList.AddRectFilled(canvasPos, canvasPos + canvasSize,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.12f, 1)));

            switch (_viewMode)
            {
                case 0: Draw2DAxialSlice(drawList, canvasPos, canvasSize); break;
                case 1: Draw2DRadialSlice(drawList, canvasPos, canvasSize); break;
                case 2: Draw3DCore(drawList, canvasPos, canvasSize); break;
                case 3: DrawTimeCharts(drawList, canvasPos, canvasSize); break;
            }

            // Handle interaction
            ImGui.InvisibleButton("canvas", canvasSize);
            if (ImGui.IsItemHovered())
            {
                // Zoom
                float wheel = ImGui.GetIO().MouseWheel;
                if (wheel != 0)
                {
                    _zoom = Math.Clamp(_zoom + wheel * 0.1f, 0.5f, 3.0f);
                }

                // Pan
                if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
                {
                    _pan += ImGui.GetIO().MouseDelta;
                }

                // Rotate (3D mode)
                if (_viewMode == 2 && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    _rotation += ImGui.GetIO().MouseDelta.X * 0.01f;
                }
            }

            // Draw colorbar
            DrawColorbar(drawList, canvasPos + new Vector2(canvasSize.X - 40, 20), new Vector2(20, canvasSize.Y - 80));
        }

        private void Draw2DAxialSlice(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            // Draw axial (vertical) cross-section of reactor core
            if (_params == null) return;

            float coreHeight = (float)_params.CoreHeight;
            float coreDiameter = (float)_params.CoreDiameter;
            float sliceY = coreDiameter * _slicePosition / 100f - coreDiameter / 2;

            var center = origin + size / 2 + _pan;
            float scale = Math.Min(size.X / coreDiameter, size.Y / coreHeight) * 0.8f * _zoom;

            // Draw core outline
            var coreMin = center - new Vector2(coreDiameter / 2, coreHeight / 2) * scale;
            var coreMax = center + new Vector2(coreDiameter / 2, coreHeight / 2) * scale;
            drawList.AddRect(coreMin, coreMax, 0xFFFFFFFF, 0, ImDrawFlags.None, 2);

            // Draw fuel assemblies
            foreach (var assembly in _params.FuelAssemblies)
            {
                float x = (float)assembly.PositionX;
                if (Math.Abs(x - sliceY) > _params.AssemblyPitch / 2) continue;

                for (int k = 0; k < 10; k++)
                {
                    float z = coreHeight * k / 9f - coreHeight / 2;
                    var pos = center + new Vector2((float)assembly.PositionY, -z) * scale;

                    // Get field value
                    float value = GetFieldValue(assembly.Id, k);
                    var color = GetHeatmapColor(value);

                    float rodRadius = 3 * _zoom;
                    drawList.AddCircleFilled(pos, rodRadius, ImGui.ColorConvertFloat4ToU32(color));
                }
            }

            // Draw control rods
            foreach (var bank in _params.ControlRodBanks)
            {
                if (bank.InsertionFraction > 0)
                {
                    float insertedHeight = coreHeight * (float)bank.InsertionFraction;
                    var rodTop = center + new Vector2(0, -coreHeight / 2) * scale;
                    var rodBot = rodTop + new Vector2(0, insertedHeight * scale);

                    drawList.AddLine(rodTop, rodBot, 0xFF0000FF, 4);
                }
            }

            // Labels
            drawList.AddText(origin + new Vector2(10, 10), 0xFFFFFFFF, $"Axial Slice at Y={sliceY:F2}m");
            drawList.AddText(origin + new Vector2(10, 30), 0xFFCCCCCC, $"Showing: {_fieldVariables[_fieldVariable]}");
        }

        private void Draw2DRadialSlice(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            // Draw radial (horizontal) cross-section
            if (_params == null) return;

            float coreDiameter = (float)_params.CoreDiameter;
            int axialLevel = (int)(_params.AxialNodes * _slicePosition / 100f);

            var center = origin + size / 2 + _pan;
            float scale = Math.Min(size.X, size.Y) / coreDiameter * 0.8f * _zoom;

            // Draw core circle
            drawList.AddCircle(center, coreDiameter / 2 * scale, 0xFFFFFFFF, 64, 2);

            // Draw fuel assemblies as colored squares
            float assemblySize = (float)_params.AssemblyPitch * scale * 0.9f;

            foreach (var assembly in _params.FuelAssemblies)
            {
                var pos = center + new Vector2((float)assembly.PositionX, (float)assembly.PositionY) * scale;

                float value = GetFieldValue(assembly.Id, axialLevel);
                var color = GetHeatmapColor(value);

                var min = pos - new Vector2(assemblySize / 2);
                var max = pos + new Vector2(assemblySize / 2);

                drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(color));
                drawList.AddRect(min, max, 0xFF404040);
            }

            // Labels
            float z = (float)(_params.CoreHeight * _slicePosition / 100f);
            drawList.AddText(origin + new Vector2(10, 10), 0xFFFFFFFF, $"Radial Slice at Z={z:F2}m");
            drawList.AddText(origin + new Vector2(10, 30), 0xFFCCCCCC, $"Assemblies: {_params.FuelAssemblies.Count}");
        }

        private void Draw3DCore(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            // Simplified 3D isometric view
            if (_params == null) return;

            var center = origin + size / 2 + _pan;
            float scale = Math.Min(size.X, size.Y) * 0.3f * _zoom;

            // Isometric projection angles
            float cosR = (float)Math.Cos(_rotation);
            float sinR = (float)Math.Sin(_rotation);
            float isoAngle = 0.5f; // Isometric tilt

            // Draw assemblies as 3D boxes
            foreach (var assembly in _params.FuelAssemblies)
            {
                float x = (float)assembly.PositionX;
                float y = (float)assembly.PositionY;

                // Project to 2D with rotation
                float px = (x * cosR - y * sinR) * scale;
                float py = (x * sinR + y * cosR) * scale * isoAngle;

                // Draw vertical line (fuel rod)
                float height = (float)_params.CoreHeight * scale * 0.3f;
                var bottom = center + new Vector2(px, py);
                var top = bottom - new Vector2(0, height);

                float value = GetFieldValue(assembly.Id, 5);
                var color = GetHeatmapColor(value);

                drawList.AddLine(bottom, top, ImGui.ColorConvertFloat4ToU32(color), 2);
                drawList.AddCircleFilled(top, 3, ImGui.ColorConvertFloat4ToU32(color));
            }

            // Draw control rod banks (inserted)
            foreach (var bank in _params.ControlRodBanks)
            {
                if (bank.InsertionFraction > 0.1)
                {
                    float rodLen = (float)(_params.CoreHeight * bank.InsertionFraction) * scale * 0.3f;
                    var pos = center - new Vector2(0, 0);
                    drawList.AddLine(pos, pos - new Vector2(0, rodLen), 0xFF0000FF, 3);
                }
            }

            // Labels
            drawList.AddText(origin + new Vector2(10, 10), 0xFFFFFFFF, "3D Core View (drag to rotate)");
        }

        private void DrawTimeCharts(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            // Draw time history charts
            float chartHeight = size.Y / 4 - 10;
            float chartWidth = size.X - 20;

            var chartNames = new[] { "Power (%)", "Reactivity (pcm)", "Fuel Temp (°C)", "Xenon (rel)" };
            var chartColors = new uint[] { 0xFF00FF00, 0xFFFF8800, 0xFF0088FF, 0xFFFF00FF };

            for (int c = 0; c < 4; c++)
            {
                var chartOrigin = origin + new Vector2(10, 10 + c * (chartHeight + 10));
                var chartSize = new Vector2(chartWidth, chartHeight);

                // Chart background
                drawList.AddRectFilled(chartOrigin, chartOrigin + chartSize, 0xFF1A1A1A);
                drawList.AddRect(chartOrigin, chartOrigin + chartSize, 0xFF404040);

                // Chart label
                drawList.AddText(chartOrigin + new Vector2(5, 2), 0xFFFFFFFF, chartNames[c]);

                // Draw data
                if (_history.Count > 1)
                {
                    float maxTime = (float)_history[^1].Time;
                    if (maxTime > 0)
                    {
                        for (int i = 1; i < _history.Count; i++)
                        {
                            float x1 = chartOrigin.X + chartWidth * (float)(_history[i - 1].Time / maxTime);
                            float x2 = chartOrigin.X + chartWidth * (float)(_history[i].Time / maxTime);

                            float v1 = GetChartValue(_history[i - 1], c);
                            float v2 = GetChartValue(_history[i], c);

                            float y1 = chartOrigin.Y + chartHeight - chartHeight * 0.8f * v1 - 10;
                            float y2 = chartOrigin.Y + chartHeight - chartHeight * 0.8f * v2 - 10;

                            drawList.AddLine(new Vector2(x1, y1), new Vector2(x2, y2), chartColors[c], 2);
                        }
                    }
                }
            }
        }

        private void DrawColorbar(ImDrawListPtr drawList, Vector2 origin, Vector2 size)
        {
            // Draw vertical colorbar
            for (int i = 0; i < size.Y; i++)
            {
                float t = 1 - i / size.Y;
                var color = GetHeatmapColor(t);
                drawList.AddLine(
                    origin + new Vector2(0, i),
                    origin + new Vector2(size.X, i),
                    ImGui.ColorConvertFloat4ToU32(color));
            }

            // Border
            drawList.AddRect(origin, origin + size, 0xFFFFFFFF);

            // Labels
            drawList.AddText(origin + new Vector2(-30, -15), 0xFFFFFFFF, "Max");
            drawList.AddText(origin + new Vector2(-30, size.Y), 0xFFFFFFFF, "Min");
        }

        private void DrawInstrumentPanel()
        {
            ImGui.Text("Instruments");
            ImGui.Separator();

            ImGui.BeginChild("InstrumentPanel", new Vector2(0, 0), ImGuiChildFlags.Border);

            // Criticality
            if (ImGui.CollapsingHeader("Criticality", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text($"keff: {_state.Keff:F5}");
                DrawIndicator("keff", (float)_state.Keff, 0.95f, 1.05f, 0.99f, 1.01f);

                ImGui.Text($"Reactivity: {_state.ReactivityPcm:F1} pcm");
                DrawIndicator("rho", (float)(_state.ReactivityPcm / 1000 + 0.5f), 0, 1, 0.4f, 0.6f);

                ImGui.Text($"Period: {FormatPeriod(_state.PeriodSeconds)}");
            }

            // Thermal
            if (ImGui.CollapsingHeader("Thermal", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text($"Peak Fuel: {_state.PeakFuelTemp:F0} °C");
                DrawIndicator("Tfuel", (float)(_state.PeakFuelTemp / 2865), 0, 1, 0, 0.7f);

                ImGui.Text($"Peak Clad: {_state.PeakCladTemp:F0} °C");
                DrawIndicator("Tclad", (float)(_state.PeakCladTemp / 1200), 0, 1, 0, 0.8f);

                ImGui.Text($"Min DNBR: {_state.MinDNBR:F2}");
                DrawIndicator("DNBR", (float)(1 - 1 / _state.MinDNBR), 0, 1, 0.3f, 1);
            }

            // Poisons
            if (ImGui.CollapsingHeader("Fission Products"))
            {
                ImGui.Text($"I-135: {_state.IodineConcentration:E2}");
                ImGui.Text($"Xe-135: {_state.XenonConcentration:E2}");
                ImGui.Text($"Sm-149: {_state.SamariumConcentration:E2}");
            }

            // Time
            ImGui.Separator();
            ImGui.Text($"Time: {_state.Time:F2} s");
            ImGui.Text($"History: {_history.Count} points");

            // Safety status
            ImGui.Separator();
            if (_params?.Safety.IsScramActive == true)
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "*** SCRAM ACTIVE ***");
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), _params.Safety.ScramReason);
            }
            else
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Normal Operation");
            }

            ImGui.EndChild();
        }

        private void DrawIndicator(string id, float value, float min, float max, float safeMin, float safeMax)
        {
            value = Math.Clamp(value, min, max);
            float normalized = (value - min) / (max - min);

            Vector4 color;
            if (normalized >= safeMin && normalized <= safeMax)
                color = new Vector4(0, 1, 0, 1); // Green
            else if (normalized < safeMin - 0.1f || normalized > safeMax + 0.1f)
                color = new Vector4(1, 0, 0, 1); // Red
            else
                color = new Vector4(1, 1, 0, 1); // Yellow

            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
            ImGui.ProgressBar(normalized, new Vector2(-1, 15), "");
            ImGui.PopStyleColor();
        }

        private float GetFieldValue(int assemblyId, int axialLevel)
        {
            // Get normalized field value (0-1) for visualization
            if (_state.PowerDensity == null) return 0.5f;

            // Simplified: use assembly average
            return 0.3f + 0.4f * (float)Math.Sin(Math.PI * axialLevel / 10.0);
        }

        private float GetChartValue(NuclearReactorState state, int chartIndex)
        {
            return chartIndex switch
            {
                0 => Math.Clamp((float)state.RelativePower, 0, 1.2f) / 1.2f,
                1 => Math.Clamp((float)(state.ReactivityPcm + 500) / 1000, 0, 1),
                2 => Math.Clamp((float)(state.PeakFuelTemp / 2000), 0, 1),
                3 => Math.Clamp((float)(state.XenonConcentration * 1e15), 0, 1),
                _ => 0
            };
        }

        private Vector4 GetHeatmapColor(float value)
        {
            value = Math.Clamp(value, 0, 1);
            int n = _heatmapColors.Length - 1;
            float t = value * n;
            int i = Math.Min((int)t, n - 1);
            float f = t - i;

            return Vector4.Lerp(_heatmapColors[i], _heatmapColors[i + 1], f);
        }

        private string FormatPeriod(double period)
        {
            if (double.IsInfinity(period) || double.IsNaN(period))
                return "∞";
            if (Math.Abs(period) > 1000)
                return $"{period / 60:F1} min";
            if (Math.Abs(period) > 0.1)
                return $"{period:F1} s";
            return $"{period * 1000:F1} ms";
        }

        private void PerformScram()
        {
            if (_params != null)
            {
                foreach (var bank in _params.ControlRodBanks)
                {
                    bank.InsertionFraction = 1.0;
                }
                _params.Safety.IsScramActive = true;
                _params.Safety.ScramReason = "Manual SCRAM";
            }
            _solver?.PerformSCRAM();
        }

        private void ResetSimulation()
        {
            _simTime = 0;
            _history.Clear();
            _state = new NuclearReactorState { RelativePower = 1.0 };
            if (_params != null)
            {
                _params.Safety.IsScramActive = false;
                foreach (var bank in _params.ControlRodBanks)
                {
                    bank.InsertionFraction = 0;
                }
            }
        }

        /// <summary>
        /// Update simulation (call each frame when running)
        /// </summary>
        public void Update(float deltaTime)
        {
            if (!_isRunning || _solver == null) return;

            // Update control rod positions based on rate
            if (_params != null && Math.Abs(_rodInsertionRate) > 0.01)
            {
                foreach (var bank in _params.ControlRodBanks)
                {
                    if (bank.RodType == ControlRodType.Control)
                    {
                        bank.InsertionFraction = Math.Clamp(
                            bank.InsertionFraction + _rodInsertionRate * deltaTime / 100,
                            0, 1);
                    }
                }
            }

            // Run solver step
            _solver.SolvePointKinetics(_simTimeStep, _externalReactivity / 100);
            _solver.SolveXenonDynamics(_simTimeStep);
            _solver.UpdateThermalHydraulics();

            _state = _solver.GetState();
            _simTime = _state.Time;

            // Store history (every 10 steps)
            if (_history.Count == 0 || _state.Time - _history[^1].Time > 0.1)
            {
                _history.Add(_state.Clone());
                if (_history.Count > 10000) _history.RemoveAt(0);
            }
        }
    }
}
