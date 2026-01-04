using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.UI.Utils;
using StbImageSharp;
using StbImageWriteSharp;

namespace GeoscientistToolkit.UI.Windows
{
    /// <summary>
    /// ImGui window for managing and visualizing simulation probes.
    /// Supports point, line, and plane probes with time-series graphs and 2D colormaps.
    /// </summary>
    public class ProbeVisualizerWindow
    {
        private bool _isOpen = true;
        private ProbeManager _probeManager = new();

        // Selected probe
        private string? _selectedProbeId;
        private SimulationProbe? SelectedProbe => _selectedProbeId != null ? _probeManager.GetProbe(_selectedProbeId) : null;

        // Drawing mode
        private int _drawingMode = 0; // 0=None, 1=Point, 2=Line, 3=Plane
        private readonly string[] _drawingModes = { "Select", "Point Probe", "Line Probe", "Plane Probe" };

        // Drawing state
        private bool _isDrawing = false;
        private Vector2 _drawStart;
        private Vector2 _drawEnd;

        // View settings
        private int _viewMode = 0; // 0=Probe List, 1=Time Charts, 2=2D Colormap
        private readonly string[] _viewModes = { "Probe List", "Time Charts", "2D Cross-Section" };

        // Chart settings
        private List<string> _chartProbeIds = new();
        private float _chartTimeRange = 100f; // seconds
        private bool _autoScale = true;
        private float _chartYMin = 0;
        private float _chartYMax = 100;

        // Colormap settings
        private int _colormapType = 0;
        private readonly string[] _colormapNames = { "Jet", "Viridis", "Inferno", "Plasma", "Grayscale" };
        private int _fieldSnapshotIndex = -1; // -1 = latest

        // Plane probe creation
        private int _newPlaneOrientation = 0;
        private readonly string[] _orientationNames = { "XY", "XZ", "YZ" };

        // Export dialog
        private ImGuiExportFileDialog? _exportDialog;
        private string? _exportProbeId;
        private bool _exportingImage = false;

        // Mesh bounds for coordinate mapping
        private Vector3 _meshMin = new(-5, -5, -5);
        private Vector3 _meshMax = new(5, 5, 5);

        // Variable selection
        private int _selectedVariableIndex = 0;
        private readonly string[] _variableNames;

        // Colors for multiple probes in chart
        private readonly uint[] _chartColors = {
            0xFF00FF00, // Green
            0xFF0088FF, // Blue
            0xFFFF8800, // Orange
            0xFFFF00FF, // Magenta
            0xFF00FFFF, // Cyan
            0xFFFFFF00, // Yellow
            0xFFFF0000, // Red
            0xFF8800FF  // Purple
        };

        // Heatmap colors
        private readonly Vector4[] _jetColors = {
            new(0, 0, 0.5f, 1),
            new(0, 0, 1, 1),
            new(0, 1, 1, 1),
            new(0, 1, 0, 1),
            new(1, 1, 0, 1),
            new(1, 0.5f, 0, 1),
            new(1, 0, 0, 1),
            new(0.5f, 0, 0, 1)
        };

        public bool IsOpen => _isOpen;
        public ProbeManager ProbeManager => _probeManager;

        public ProbeVisualizerWindow()
        {
            _variableNames = Enum.GetNames(typeof(ProbeVariable));
        }

        public void SetProbeManager(ProbeManager manager)
        {
            _probeManager = manager;
        }

        public void SetMeshBounds(Vector3 min, Vector3 max)
        {
            _meshMin = min;
            _meshMax = max;
        }

        public void Open() => _isOpen = true;
        public void Close() => _isOpen = false;

        public void Draw()
        {
            if (!_isOpen) return;

            ImGui.SetNextWindowSize(new Vector2(900, 700), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Probe Visualizer", ref _isOpen, ImGuiWindowFlags.MenuBar))
            {
                DrawMenuBar();
                DrawToolbar();

                // Main content
                ImGui.Columns(2, "ProbeColumns", true);
                ImGui.SetColumnWidth(0, 250);

                DrawProbeList();
                ImGui.NextColumn();

                DrawMainView();

                ImGui.Columns(1);
            }
            ImGui.End();

            // Handle export dialog
            if (_exportDialog != null && _exportDialog.IsOpen)
            {
                if (_exportDialog.Submit())
                {
                    PerformExport(_exportDialog.SelectedPath);
                    _exportDialog = null;
                }
            }
        }

        private void DrawMenuBar()
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("Export Selected Probe Data...", "", false, SelectedProbe != null))
                    {
                        StartExportCSV();
                    }
                    if (ImGui.MenuItem("Export Chart as PNG...", "", false, _viewMode == 1 && _chartProbeIds.Count > 0))
                    {
                        StartExportPNG();
                    }
                    if (ImGui.MenuItem("Export Colormap as PNG...", "", false, _viewMode == 2 && SelectedProbe is PlaneProbe))
                    {
                        StartExportPNG();
                    }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Clear All Probe Data"))
                    {
                        _probeManager.ClearAllHistory();
                    }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Probes"))
                {
                    if (ImGui.MenuItem("Add Point Probe"))
                    {
                        AddNewProbe(0);
                    }
                    if (ImGui.MenuItem("Add Line Probe"))
                    {
                        AddNewProbe(1);
                    }
                    if (ImGui.MenuItem("Add Plane Probe"))
                    {
                        AddNewProbe(2);
                    }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Delete Selected", "", false, SelectedProbe != null))
                    {
                        DeleteSelectedProbe();
                    }
                    if (ImGui.MenuItem("Delete All Probes"))
                    {
                        _probeManager.ClearAllProbes();
                        _selectedProbeId = null;
                        _chartProbeIds.Clear();
                    }
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

                ImGui.EndMenuBar();
            }
        }

        private void DrawToolbar()
        {
            // View mode
            ImGui.Text("View:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(130);
            ImGui.Combo("##ViewMode", ref _viewMode, _viewModes, _viewModes.Length);

            ImGui.SameLine();
            ImGui.Text("Draw:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(120);
            ImGui.Combo("##DrawMode", ref _drawingMode, _drawingModes, _drawingModes.Length);

            // Quick add buttons
            ImGui.SameLine();
            if (ImGui.Button("+ Point"))
                AddNewProbe(0);
            ImGui.SameLine();
            if (ImGui.Button("+ Line"))
                AddNewProbe(1);
            ImGui.SameLine();
            if (ImGui.Button("+ Plane"))
                AddNewProbe(2);

            ImGui.Separator();
        }

        private void DrawProbeList()
        {
            ImGui.BeginChild("ProbeListPanel", new Vector2(0, 0), ImGuiChildFlags.Border);

            ImGui.Text($"Probes ({_probeManager.Count})");
            ImGui.Separator();

            // Point probes
            if (ImGui.TreeNodeEx("Point Probes", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var probe in _probeManager.PointProbes)
                {
                    DrawProbeItem(probe, "P");
                }
                ImGui.TreePop();
            }

            // Line probes
            if (ImGui.TreeNodeEx("Line Probes", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var probe in _probeManager.LineProbes)
                {
                    DrawProbeItem(probe, "L");
                }
                ImGui.TreePop();
            }

            // Plane probes
            if (ImGui.TreeNodeEx("Plane Probes", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var probe in _probeManager.PlaneProbes)
                {
                    DrawProbeItem(probe, "S");
                }
                ImGui.TreePop();
            }

            ImGui.Separator();

            // Selected probe details
            if (SelectedProbe != null)
            {
                ImGui.Text("Selected Probe:");
                DrawProbeDetails(SelectedProbe);
            }

            ImGui.EndChild();
        }

        private void DrawProbeItem(SimulationProbe probe, string typeIcon)
        {
            bool isSelected = _selectedProbeId == probe.Id;
            bool inChart = _chartProbeIds.Contains(probe.Id);

            // Color indicator
            var color = ImGui.ColorConvertU32ToFloat4(probe.Color);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.Text(typeIcon);
            ImGui.PopStyleColor();
            ImGui.SameLine();

            // Selectable
            if (ImGui.Selectable($"{probe.Name}##{probe.Id}", isSelected))
            {
                _selectedProbeId = probe.Id;
            }

            // Context menu
            if (ImGui.BeginPopupContextItem($"ctx_{probe.Id}"))
            {
                if (ImGui.MenuItem("Rename"))
                {
                    // TODO: Show rename dialog
                }
                if (ImGui.MenuItem(inChart ? "Remove from Chart" : "Add to Chart"))
                {
                    if (inChart)
                        _chartProbeIds.Remove(probe.Id);
                    else
                        _chartProbeIds.Add(probe.Id);
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Delete"))
                {
                    _probeManager.RemoveProbe(probe.Id);
                    if (_selectedProbeId == probe.Id)
                        _selectedProbeId = null;
                    _chartProbeIds.Remove(probe.Id);
                }
                ImGui.EndPopup();
            }

            // Show data count
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 40);
            ImGui.TextDisabled($"{probe.History.Count}");
        }

        private void DrawProbeDetails(SimulationProbe probe)
        {
            ImGui.BeginChild("ProbeDetails", new Vector2(0, 200), ImGuiChildFlags.Border);

            // Name editing
            var name = probe.Name;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##Name", ref name, 64))
            {
                probe.Name = name;
            }

            // Active toggle
            var active = probe.IsActive;
            if (ImGui.Checkbox("Active", ref active))
            {
                probe.IsActive = active;
            }

            // Variable selection
            ImGui.Text("Variable:");
            int varIndex = (int)probe.Variable;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##Variable", ref varIndex, _variableNames, _variableNames.Length))
            {
                probe.Variable = (ProbeVariable)varIndex;
            }

            // Color picker
            var colorVec = ImGui.ColorConvertU32ToFloat4(probe.Color);
            if (ImGui.ColorEdit4("Color", ref colorVec, ImGuiColorEditFlags.NoInputs))
            {
                probe.Color = ImGui.ColorConvertFloat4ToU32(colorVec);
            }

            // Type-specific details
            ImGui.Separator();
            if (probe is PointProbe pp)
            {
                var pos = new Vector3((float)pp.X, (float)pp.Y, (float)pp.Z);
                if (ImGui.DragFloat3("Position", ref pos, 0.1f))
                {
                    pp.X = pos.X; pp.Y = pos.Y; pp.Z = pos.Z;
                }
            }
            else if (probe is LineProbe lp)
            {
                var start = new Vector3((float)lp.StartX, (float)lp.StartY, (float)lp.StartZ);
                var end = new Vector3((float)lp.EndX, (float)lp.EndY, (float)lp.EndZ);
                if (ImGui.DragFloat3("Start", ref start, 0.1f))
                {
                    lp.StartX = start.X; lp.StartY = start.Y; lp.StartZ = start.Z;
                }
                if (ImGui.DragFloat3("End", ref end, 0.1f))
                {
                    lp.EndX = end.X; lp.EndY = end.Y; lp.EndZ = end.Z;
                }
                ImGui.Text($"Length: {lp.Length:F2} m");
            }
            else if (probe is PlaneProbe plp)
            {
                var center = new Vector3((float)plp.CenterX, (float)plp.CenterY, (float)plp.CenterZ);
                if (ImGui.DragFloat3("Center", ref center, 0.1f))
                {
                    plp.CenterX = center.X; plp.CenterY = center.Y; plp.CenterZ = center.Z;
                }

                var size = new Vector2((float)plp.Width, (float)plp.Height);
                if (ImGui.DragFloat2("Size", ref size, 0.1f, 0.1f, 100f))
                {
                    plp.Width = size.X; plp.Height = size.Y;
                }

                int orient = (int)plp.Orientation;
                if (ImGui.Combo("Orientation", ref orient, _orientationNames, _orientationNames.Length))
                {
                    plp.Orientation = (ProbePlaneOrientation)orient;
                }
            }

            // Add to chart button
            ImGui.Separator();
            bool inChart = _chartProbeIds.Contains(probe.Id);
            if (ImGui.Button(inChart ? "Remove from Chart" : "Add to Chart", new Vector2(-1, 0)))
            {
                if (inChart)
                    _chartProbeIds.Remove(probe.Id);
                else
                    _chartProbeIds.Add(probe.Id);
            }

            ImGui.EndChild();
        }

        private void DrawMainView()
        {
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();

            switch (_viewMode)
            {
                case 0:
                    DrawMeshViewWithProbes(canvasPos, canvasSize);
                    break;
                case 1:
                    DrawTimeCharts(canvasPos, canvasSize);
                    break;
                case 2:
                    DrawColormapView(canvasPos, canvasSize);
                    break;
            }
        }

        private void DrawMeshViewWithProbes(Vector2 origin, Vector2 size)
        {
            var drawList = ImGui.GetWindowDrawList();

            // Background
            drawList.AddRectFilled(origin, origin + size, 0xFF1A1A1A);
            drawList.AddRect(origin, origin + size, 0xFF404040);

            // Draw mesh outline (simplified XY view)
            var meshSize = _meshMax - _meshMin;
            float scale = Math.Min(size.X / meshSize.X, size.Y / meshSize.Y) * 0.8f;
            var center = origin + size / 2;

            // Mesh boundary
            var meshMin2D = center + new Vector2(_meshMin.X, -_meshMin.Y) * scale;
            var meshMax2D = center + new Vector2(_meshMax.X, -_meshMax.Y) * scale;
            drawList.AddRect(
                new Vector2(Math.Min(meshMin2D.X, meshMax2D.X), Math.Min(meshMin2D.Y, meshMax2D.Y)),
                new Vector2(Math.Max(meshMin2D.X, meshMax2D.X), Math.Max(meshMin2D.Y, meshMax2D.Y)),
                0xFF808080, 0, ImDrawFlags.None, 2);

            // Draw probes
            foreach (var probe in _probeManager.AllProbes)
            {
                bool isSelected = probe.Id == _selectedProbeId;
                uint color = probe.IsActive ? probe.Color : 0x80808080;

                if (probe is PointProbe pp)
                {
                    var pos = center + new Vector2((float)pp.X, -(float)pp.Y) * scale;
                    float radius = isSelected ? 8 : 5;
                    drawList.AddCircleFilled(pos, radius, color);
                    if (isSelected)
                        drawList.AddCircle(pos, radius + 2, 0xFFFFFFFF, 12, 2);
                }
                else if (probe is LineProbe lp)
                {
                    var start = center + new Vector2((float)lp.StartX, -(float)lp.StartY) * scale;
                    var end = center + new Vector2((float)lp.EndX, -(float)lp.EndY) * scale;
                    float thickness = isSelected ? 4 : 2;
                    drawList.AddLine(start, end, color, thickness);
                    drawList.AddCircleFilled(start, 4, color);
                    drawList.AddCircleFilled(end, 4, color);
                }
                else if (probe is PlaneProbe plp)
                {
                    // Draw plane as rectangle
                    var planeCenter = center + new Vector2((float)plp.CenterX, -(float)plp.CenterY) * scale;
                    var halfSize = new Vector2((float)plp.Width, (float)plp.Height) * scale / 2;
                    var min = planeCenter - halfSize;
                    var max = planeCenter + halfSize;

                    uint fillColor = (color & 0x00FFFFFF) | 0x40000000;
                    drawList.AddRectFilled(min, max, fillColor);
                    drawList.AddRect(min, max, color, 0, ImDrawFlags.None, isSelected ? 3 : 1);
                }
            }

            // Handle drawing interaction
            ImGui.SetCursorScreenPos(origin);
            ImGui.InvisibleButton("MeshCanvas", size);

            if (ImGui.IsItemHovered())
            {
                var mousePos = ImGui.GetMousePos();
                var worldPos = (mousePos - center) / scale;
                worldPos.Y = -worldPos.Y;

                // Show coordinates
                drawList.AddText(origin + new Vector2(5, 5), 0xFFFFFFFF,
                    $"({worldPos.X:F2}, {worldPos.Y:F2})");

                if (_drawingMode > 0)
                {
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        _isDrawing = true;
                        _drawStart = mousePos;
                    }

                    if (_isDrawing)
                    {
                        _drawEnd = mousePos;

                        // Preview
                        if (_drawingMode == 1) // Point
                        {
                            drawList.AddCircle(mousePos, 6, 0xFFFFFF00, 12, 2);
                        }
                        else if (_drawingMode == 2) // Line
                        {
                            drawList.AddLine(_drawStart, _drawEnd, 0xFFFFFF00, 2);
                        }
                        else if (_drawingMode == 3) // Plane
                        {
                            drawList.AddRect(_drawStart, _drawEnd, 0xFFFFFF00, 0, ImDrawFlags.None, 2);
                        }
                    }

                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && _isDrawing)
                    {
                        _isDrawing = false;

                        // Create probe from drawing
                        var startWorld = (_drawStart - center) / scale;
                        startWorld.Y = -startWorld.Y;
                        var endWorld = (_drawEnd - center) / scale;
                        endWorld.Y = -endWorld.Y;

                        if (_drawingMode == 1)
                        {
                            var probe = _probeManager.AddPointProbe(endWorld.X, endWorld.Y, 0,
                                $"Point {_probeManager.PointProbes.Count + 1}");
                            _selectedProbeId = probe.Id;
                        }
                        else if (_drawingMode == 2)
                        {
                            var probe = _probeManager.AddLineProbe(
                                startWorld.X, startWorld.Y, 0,
                                endWorld.X, endWorld.Y, 0,
                                $"Line {_probeManager.LineProbes.Count + 1}");
                            _selectedProbeId = probe.Id;
                        }
                        else if (_drawingMode == 3)
                        {
                            var c = (startWorld + endWorld) / 2;
                            var s = Vector2.Abs(endWorld - startWorld);
                            var probe = _probeManager.AddPlaneProbe(
                                c.X, c.Y, 0, s.X, s.Y,
                                ProbePlaneOrientation.XY,
                                $"Plane {_probeManager.PlaneProbes.Count + 1}");
                            _selectedProbeId = probe.Id;
                        }

                        _drawingMode = 0; // Reset to select mode
                    }
                }
                else
                {
                    // Click to select probe
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    {
                        SelectProbeAtPosition(mousePos, center, scale);
                    }
                }
            }

            // Legend
            drawList.AddText(origin + new Vector2(5, size.Y - 20), 0xFFCCCCCC,
                _drawingMode > 0 ? $"Drawing: {_drawingModes[_drawingMode]}" : "Click to select probe");
        }

        private void DrawTimeCharts(Vector2 origin, Vector2 size)
        {
            var drawList = ImGui.GetWindowDrawList();

            // Chart controls at top
            ImGui.SetCursorScreenPos(origin);
            ImGui.BeginChild("ChartControls", new Vector2(size.X, 50), ImGuiChildFlags.None);

            ImGui.Text("Time Range:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.SliderFloat("##TimeRange", ref _chartTimeRange, 10, 1000, "%.0f s");

            ImGui.SameLine();
            ImGui.Checkbox("Auto Scale", ref _autoScale);

            if (!_autoScale)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                ImGui.DragFloat("Min", ref _chartYMin, 1);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80);
                ImGui.DragFloat("Max", ref _chartYMax, 1);
            }

            ImGui.EndChild();

            // Chart area
            var chartOrigin = origin + new Vector2(0, 55);
            var chartSize = new Vector2(size.X, size.Y - 60);

            // Background
            drawList.AddRectFilled(chartOrigin, chartOrigin + chartSize, 0xFF1A1A1A);
            drawList.AddRect(chartOrigin, chartOrigin + chartSize, 0xFF404040);

            // Margins
            float marginLeft = 60;
            float marginRight = 20;
            float marginTop = 20;
            float marginBottom = 40;

            var plotOrigin = chartOrigin + new Vector2(marginLeft, marginTop);
            var plotSize = chartSize - new Vector2(marginLeft + marginRight, marginTop + marginBottom);

            // Grid
            drawList.AddRectFilled(plotOrigin, plotOrigin + plotSize, 0xFF222222);

            // Draw grid lines
            for (int i = 0; i <= 10; i++)
            {
                float x = plotOrigin.X + plotSize.X * i / 10;
                float y = plotOrigin.Y + plotSize.Y * i / 10;
                drawList.AddLine(new Vector2(x, plotOrigin.Y), new Vector2(x, plotOrigin.Y + plotSize.Y), 0xFF333333);
                drawList.AddLine(new Vector2(plotOrigin.X, y), new Vector2(plotOrigin.X + plotSize.X, y), 0xFF333333);
            }

            // Determine Y range
            float yMin = _chartYMin, yMax = _chartYMax;
            if (_autoScale && _chartProbeIds.Count > 0)
            {
                yMin = float.MaxValue;
                yMax = float.MinValue;
                foreach (var id in _chartProbeIds)
                {
                    var probe = _probeManager.GetProbe(id);
                    if (probe?.History.Count > 0)
                    {
                        foreach (var pt in probe.History)
                        {
                            yMin = Math.Min(yMin, (float)pt.Value);
                            yMax = Math.Max(yMax, (float)pt.Value);
                        }
                    }
                }
                if (yMin >= yMax) { yMin = 0; yMax = 100; }
                float margin = (yMax - yMin) * 0.1f;
                yMin -= margin;
                yMax += margin;
            }

            // Draw probes
            int colorIdx = 0;
            foreach (var id in _chartProbeIds)
            {
                var probe = _probeManager.GetProbe(id);
                if (probe?.History.Count > 1)
                {
                    uint color = probe.Color;
                    var history = probe.History;
                    double maxTime = history[^1].Time;
                    double minTime = Math.Max(0, maxTime - _chartTimeRange);

                    Vector2? lastPt = null;
                    foreach (var pt in history)
                    {
                        if (pt.Time < minTime) continue;

                        float x = plotOrigin.X + (float)((pt.Time - minTime) / _chartTimeRange) * plotSize.X;
                        float y = plotOrigin.Y + plotSize.Y - (float)((pt.Value - yMin) / (yMax - yMin)) * plotSize.Y;
                        y = Math.Clamp(y, plotOrigin.Y, plotOrigin.Y + plotSize.Y);

                        var currentPt = new Vector2(x, y);
                        if (lastPt.HasValue)
                        {
                            drawList.AddLine(lastPt.Value, currentPt, color, 2);
                        }
                        lastPt = currentPt;
                    }

                    // Legend entry
                    float legendY = chartOrigin.Y + 5 + colorIdx * 18;
                    drawList.AddRectFilled(
                        new Vector2(chartOrigin.X + chartSize.X - 150, legendY),
                        new Vector2(chartOrigin.X + chartSize.X - 140, legendY + 12),
                        color);
                    drawList.AddText(new Vector2(chartOrigin.X + chartSize.X - 135, legendY), 0xFFFFFFFF, probe.Name);

                    colorIdx++;
                }
            }

            // Axes labels
            drawList.AddText(new Vector2(chartOrigin.X + chartSize.X / 2 - 20, chartOrigin.Y + chartSize.Y - 18), 0xFFFFFFFF, "Time (s)");

            // Y axis labels
            for (int i = 0; i <= 5; i++)
            {
                float val = yMin + (yMax - yMin) * (5 - i) / 5;
                float y = plotOrigin.Y + plotSize.Y * i / 5;
                drawList.AddText(new Vector2(chartOrigin.X + 5, y - 6), 0xFFCCCCCC, $"{val:G4}");
            }

            // X axis labels
            for (int i = 0; i <= 5; i++)
            {
                float t = _chartTimeRange * i / 5;
                float x = plotOrigin.X + plotSize.X * i / 5;
                drawList.AddText(new Vector2(x - 15, plotOrigin.Y + plotSize.Y + 5), 0xFFCCCCCC, $"{t:F0}");
            }

            // Info text
            if (_chartProbeIds.Count == 0)
            {
                var textPos = plotOrigin + plotSize / 2 - new Vector2(100, 10);
                drawList.AddText(textPos, 0xFF808080, "Add probes to chart from the list");
            }
        }

        private void DrawColormapView(Vector2 origin, Vector2 size)
        {
            var drawList = ImGui.GetWindowDrawList();

            // Background
            drawList.AddRectFilled(origin, origin + size, 0xFF1A1A1A);
            drawList.AddRect(origin, origin + size, 0xFF404040);

            // Controls
            ImGui.SetCursorScreenPos(origin);
            ImGui.BeginChild("ColormapControls", new Vector2(size.X, 35), ImGuiChildFlags.None);

            ImGui.Text("Colormap:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.Combo("##Colormap", ref _colormapType, _colormapNames, _colormapNames.Length);

            ImGui.SameLine();
            ImGui.Text("Snapshot:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.InputInt("##Snapshot", ref _fieldSnapshotIndex);

            ImGui.EndChild();

            // Get plane probe
            PlaneProbe? planeProbe = SelectedProbe as PlaneProbe;
            if (planeProbe == null)
            {
                // Try to find first plane probe
                planeProbe = _probeManager.PlaneProbes.FirstOrDefault();
            }

            if (planeProbe == null || planeProbe.FieldHistory.Count == 0)
            {
                var textPos = origin + size / 2 - new Vector2(80, 10);
                drawList.AddText(textPos, 0xFF808080, "No plane probe data available");
                return;
            }

            // Get field data
            int snapshotIdx = _fieldSnapshotIndex < 0 || _fieldSnapshotIndex >= planeProbe.FieldHistory.Count
                ? planeProbe.FieldHistory.Count - 1
                : _fieldSnapshotIndex;
            var fieldData = planeProbe.FieldHistory[snapshotIdx];

            // Draw colormap
            var mapOrigin = origin + new Vector2(40, 50);
            var mapSize = new Vector2(size.X - 100, size.Y - 80);

            float cellWidth = mapSize.X / fieldData.ResolutionX;
            float cellHeight = mapSize.Y / fieldData.ResolutionY;

            for (int i = 0; i < fieldData.ResolutionX; i++)
            {
                for (int j = 0; j < fieldData.ResolutionY; j++)
                {
                    double val = fieldData.Values[i, j];
                    float normalized = (float)((val - fieldData.MinValue) / (fieldData.MaxValue - fieldData.MinValue + 1e-10));
                    var color = GetHeatmapColor(normalized);

                    var cellPos = mapOrigin + new Vector2(i * cellWidth, (fieldData.ResolutionY - 1 - j) * cellHeight);
                    drawList.AddRectFilled(cellPos, cellPos + new Vector2(cellWidth, cellHeight),
                        ImGui.ColorConvertFloat4ToU32(color));
                }
            }

            // Draw colorbar
            var barOrigin = mapOrigin + new Vector2(mapSize.X + 10, 0);
            var barSize = new Vector2(20, mapSize.Y);

            for (int i = 0; i < barSize.Y; i++)
            {
                float t = 1 - i / barSize.Y;
                var color = GetHeatmapColor(t);
                drawList.AddLine(
                    barOrigin + new Vector2(0, i),
                    barOrigin + new Vector2(barSize.X, i),
                    ImGui.ColorConvertFloat4ToU32(color));
            }
            drawList.AddRect(barOrigin, barOrigin + barSize, 0xFFFFFFFF);

            // Colorbar labels
            drawList.AddText(barOrigin + new Vector2(25, -5), 0xFFFFFFFF, $"{fieldData.MaxValue:G4}");
            drawList.AddText(barOrigin + new Vector2(25, barSize.Y - 10), 0xFFFFFFFF, $"{fieldData.MinValue:G4}");

            // Info
            drawList.AddText(origin + new Vector2(10, size.Y - 25), 0xFFCCCCCC,
                $"Probe: {planeProbe.Name} | Time: {fieldData.Time:F2}s | {planeProbe.Orientation} plane");
        }

        private Vector4 GetHeatmapColor(float t)
        {
            t = Math.Clamp(t, 0, 1);
            int n = _jetColors.Length - 1;
            float idx = t * n;
            int i = Math.Min((int)idx, n - 1);
            float f = idx - i;
            return Vector4.Lerp(_jetColors[i], _jetColors[i + 1], f);
        }

        private void SelectProbeAtPosition(Vector2 mousePos, Vector2 center, float scale)
        {
            float minDist = float.MaxValue;
            string? closestId = null;

            foreach (var probe in _probeManager.AllProbes)
            {
                float dist = float.MaxValue;

                if (probe is PointProbe pp)
                {
                    var pos = center + new Vector2((float)pp.X, -(float)pp.Y) * scale;
                    dist = Vector2.Distance(mousePos, pos);
                }
                else if (probe is LineProbe lp)
                {
                    var start = center + new Vector2((float)lp.StartX, -(float)lp.StartY) * scale;
                    var end = center + new Vector2((float)lp.EndX, -(float)lp.EndY) * scale;
                    dist = DistanceToLine(mousePos, start, end);
                }
                else if (probe is PlaneProbe plp)
                {
                    var planeCenter = center + new Vector2((float)plp.CenterX, -(float)plp.CenterY) * scale;
                    var halfSize = new Vector2((float)plp.Width, (float)plp.Height) * scale / 2;
                    var min = planeCenter - halfSize;
                    var max = planeCenter + halfSize;

                    if (mousePos.X >= min.X && mousePos.X <= max.X &&
                        mousePos.Y >= min.Y && mousePos.Y <= max.Y)
                    {
                        dist = 0;
                    }
                }

                if (dist < minDist && dist < 20)
                {
                    minDist = dist;
                    closestId = probe.Id;
                }
            }

            _selectedProbeId = closestId;
        }

        private float DistanceToLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            var line = lineEnd - lineStart;
            float len = line.Length();
            if (len < 0.001f) return Vector2.Distance(point, lineStart);

            float t = Math.Clamp(Vector2.Dot(point - lineStart, line) / (len * len), 0, 1);
            var projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        private void AddNewProbe(int type)
        {
            var center = (_meshMin + _meshMax) / 2;

            if (type == 0)
            {
                var probe = _probeManager.AddPointProbe(center.X, center.Y, center.Z,
                    $"Point {_probeManager.PointProbes.Count + 1}");
                probe.Color = _chartColors[_probeManager.PointProbes.Count % _chartColors.Length];
                _selectedProbeId = probe.Id;
            }
            else if (type == 1)
            {
                var probe = _probeManager.AddLineProbe(
                    _meshMin.X, center.Y, center.Z,
                    _meshMax.X, center.Y, center.Z,
                    $"Line {_probeManager.LineProbes.Count + 1}");
                probe.Color = _chartColors[(_probeManager.LineProbes.Count + 2) % _chartColors.Length];
                _selectedProbeId = probe.Id;
            }
            else if (type == 2)
            {
                var size = (_meshMax - _meshMin) * 0.5f;
                var probe = _probeManager.AddPlaneProbe(
                    center.X, center.Y, center.Z,
                    size.X, size.Y,
                    ProbePlaneOrientation.XY,
                    $"Plane {_probeManager.PlaneProbes.Count + 1}");
                probe.Color = _chartColors[(_probeManager.PlaneProbes.Count + 4) % _chartColors.Length];
                _selectedProbeId = probe.Id;
            }
        }

        private void DeleteSelectedProbe()
        {
            if (_selectedProbeId != null)
            {
                _probeManager.RemoveProbe(_selectedProbeId);
                _chartProbeIds.Remove(_selectedProbeId);
                _selectedProbeId = null;
            }
        }

        private void StartExportCSV()
        {
            if (SelectedProbe == null) return;

            _exportDialog = new ImGuiExportFileDialog("ProbeExport", "Export Probe Data");
            _exportDialog.SetExtensions((".csv", "CSV File"));
            _exportDialog.Open($"{SelectedProbe.Name}_data");
            _exportProbeId = SelectedProbe.Id;
            _exportingImage = false;
        }

        private void StartExportPNG()
        {
            _exportDialog = new ImGuiExportFileDialog("ChartExport", "Export Image");
            _exportDialog.SetExtensions((".png", "PNG Image"));
            _exportDialog.Open($"probe_chart");
            _exportingImage = true;
        }

        private void PerformExport(string path)
        {
            try
            {
                if (_exportingImage)
                {
                    // Export chart/colormap as PNG
                    ExportChartAsPNG(path);
                }
                else if (_exportProbeId != null)
                {
                    // Export probe data as CSV
                    var probe = _probeManager.GetProbe(_exportProbeId);
                    if (probe != null)
                    {
                        var csv = _probeManager.ExportToCSV(probe);
                        System.IO.File.WriteAllText(path, csv);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Export error: {ex.Message}");
            }
        }

        private void ExportChartAsPNG(string path)
        {
            // Create image buffer
            int width = 800;
            int height = 600;
            byte[] pixels = new byte[width * height * 4];

            // Fill with background color
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 26;     // R
                pixels[i + 1] = 26; // G
                pixels[i + 2] = 26; // B
                pixels[i + 3] = 255; // A
            }

            // Draw chart data to pixels
            if (_viewMode == 1 && _chartProbeIds.Count > 0)
            {
                DrawChartToBuffer(pixels, width, height);
            }
            else if (_viewMode == 2 && SelectedProbe is PlaneProbe planeProbe && planeProbe.FieldHistory.Count > 0)
            {
                DrawColormapToBuffer(pixels, width, height, planeProbe);
            }

            // Write PNG using StbImageWrite
            using var stream = System.IO.File.OpenWrite(path);
            var writer = new ImageWriter();
            writer.WritePng(pixels, width, height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        }

        private void DrawChartToBuffer(byte[] pixels, int width, int height)
        {
            // Simplified chart rendering to pixel buffer
            // Determine Y range
            float yMin = float.MaxValue, yMax = float.MinValue;
            double maxTime = 0;

            foreach (var id in _chartProbeIds)
            {
                var probe = _probeManager.GetProbe(id);
                if (probe?.History.Count > 0)
                {
                    foreach (var pt in probe.History)
                    {
                        yMin = Math.Min(yMin, (float)pt.Value);
                        yMax = Math.Max(yMax, (float)pt.Value);
                        maxTime = Math.Max(maxTime, pt.Time);
                    }
                }
            }

            if (yMin >= yMax) { yMin = 0; yMax = 100; }
            double minTime = Math.Max(0, maxTime - _chartTimeRange);

            int margin = 50;
            int plotWidth = width - margin * 2;
            int plotHeight = height - margin * 2;

            // Draw data
            int colorIdx = 0;
            foreach (var id in _chartProbeIds)
            {
                var probe = _probeManager.GetProbe(id);
                if (probe?.History.Count > 1)
                {
                    var colorVec = ImGui.ColorConvertU32ToFloat4(probe.Color);
                    byte r = (byte)(colorVec.X * 255);
                    byte g = (byte)(colorVec.Y * 255);
                    byte b = (byte)(colorVec.Z * 255);

                    int? lastX = null, lastY = null;
                    foreach (var pt in probe.History)
                    {
                        if (pt.Time < minTime) continue;

                        int x = margin + (int)((pt.Time - minTime) / _chartTimeRange * plotWidth);
                        int y = margin + plotHeight - (int)((pt.Value - yMin) / (yMax - yMin) * plotHeight);
                        y = Math.Clamp(y, margin, margin + plotHeight);

                        if (lastX.HasValue)
                        {
                            DrawLineToBuffer(pixels, width, height, lastX.Value, lastY!.Value, x, y, r, g, b);
                        }
                        lastX = x;
                        lastY = y;
                    }
                    colorIdx++;
                }
            }
        }

        private void DrawColormapToBuffer(byte[] pixels, int width, int height, PlaneProbe probe)
        {
            if (probe.FieldHistory.Count == 0) return;

            var fieldData = probe.FieldHistory[^1];
            int margin = 40;

            float cellWidth = (float)(width - margin * 2) / fieldData.ResolutionX;
            float cellHeight = (float)(height - margin * 2) / fieldData.ResolutionY;

            for (int i = 0; i < fieldData.ResolutionX; i++)
            {
                for (int j = 0; j < fieldData.ResolutionY; j++)
                {
                    double val = fieldData.Values[i, j];
                    float normalized = (float)((val - fieldData.MinValue) / (fieldData.MaxValue - fieldData.MinValue + 1e-10));
                    var color = GetHeatmapColor(normalized);

                    int x0 = margin + (int)(i * cellWidth);
                    int y0 = margin + (int)((fieldData.ResolutionY - 1 - j) * cellHeight);
                    int x1 = margin + (int)((i + 1) * cellWidth);
                    int y1 = margin + (int)((fieldData.ResolutionY - j) * cellHeight);

                    for (int py = y0; py < y1 && py < height; py++)
                    {
                        for (int px = x0; px < x1 && px < width; px++)
                        {
                            int idx = (py * width + px) * 4;
                            if (idx >= 0 && idx < pixels.Length - 3)
                            {
                                pixels[idx] = (byte)(color.X * 255);
                                pixels[idx + 1] = (byte)(color.Y * 255);
                                pixels[idx + 2] = (byte)(color.Z * 255);
                                pixels[idx + 3] = 255;
                            }
                        }
                    }
                }
            }
        }

        private void DrawLineToBuffer(byte[] pixels, int width, int height, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            // Bresenham's line algorithm
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                {
                    int idx = (y0 * width + x0) * 4;
                    pixels[idx] = r;
                    pixels[idx + 1] = g;
                    pixels[idx + 2] = b;
                    pixels[idx + 3] = 255;
                }

                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }
    }
}
