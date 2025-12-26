// GeoscientistToolkit/Data/TwoDGeology/Interactive2DProfileDrawingTool.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Util;
using ImGuiNET;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;

namespace GeoscientistToolkit.Data.TwoDGeology
{
    /// <summary>
    /// Interactive tool for drawing 2D geological profiles by hand.
    /// Supports drawing topography and geological layers with optional snapping.
    /// </summary>
    public class Interactive2DProfileDrawingTool
    {
        #region Fields

        // Profile data
        private CrossSectionGenerator.CrossSection _profile;
        private TwoDGeologyDataset _dataset;

        // Drawing state
        private DrawMode _currentMode = DrawMode.SelectMove;
        private LayerType _currentLayerType = LayerType.Topography;

        // Layer being edited
        private int _selectedLayerIndex = -1;
        private List<Vector2> _currentLayerPoints = new List<Vector2>();
        private string _currentLayerName = "New Layer";
        private string _currentLithologyType = "Sandstone";
        private Vector4 _currentLayerColor = new Vector4(0.8f, 0.7f, 0.5f, 1.0f);

        // Snapping
        private bool _enableSnapping = true;
        private float _snapGridSize = 10.0f;  // meters
        private float _snapPointRadius = 5.0f;  // pixels
        private bool _snapToGrid = true;
        private bool _snapToPoints = true;
        private bool _snapToLayers = true;

        // View/Camera
        private Vector2 _viewOffset = Vector2.Zero;
        private float _viewZoom = 1.0f;
        private bool _isDraggingView = false;
        private Vector2 _lastMousePos = Vector2.Zero;

        // Interaction
        private int _selectedPointIndex = -1;
        private string _selectedPointType = "none";
        private int _selectedLayerIndex = -1;
        private bool _isDraggingPoint = false;
        private Vector2 _dragStartPos = Vector2.Zero;

        // Profile bounds
        private float _profileLength = 1000f;  // meters
        private float _minElevation = -200f;
        private float _maxElevation = 200f;

        // UI state
        private bool _showGrid = true;
        private bool _showHelp = false;
        private string _statusMessage = "Ready";

        // Color palette for quick selection
        private readonly Vector4[] _colorPalette = new[]
        {
            new Vector4(0.8f, 0.7f, 0.5f, 1.0f),  // Sandstone (tan)
            new Vector4(0.6f, 0.6f, 0.6f, 1.0f),  // Limestone (gray)
            new Vector4(0.5f, 0.3f, 0.2f, 1.0f),  // Shale (brown)
            new Vector4(0.3f, 0.5f, 0.3f, 1.0f),  // Basalt (dark green)
            new Vector4(0.9f, 0.8f, 0.7f, 1.0f),  // Clay (light tan)
            new Vector4(0.4f, 0.4f, 0.5f, 1.0f),  // Granite (blue-gray)
            new Vector4(0.7f, 0.5f, 0.3f, 1.0f),  // Conglomerate (orange-brown)
            new Vector4(0.2f, 0.3f, 0.4f, 1.0f),  // Coal (dark blue-gray)
        };

        private readonly string[] _lithologyTypes = new[]
        {
            "Sandstone", "Limestone", "Shale", "Basalt", "Clay",
            "Granite", "Conglomerate", "Coal", "Siltstone", "Dolomite",
            "Mudstone", "Quartzite", "Schist", "Gneiss", "Marble"
        };

        #endregion

        #region Enums

        public enum DrawMode
        {
            SelectMove,      // Select and move points
            DrawTopography,  // Draw topography profile
            DrawLayer,       // Draw geological layer boundary
            Erase           // Erase points
        }

        public enum LayerType
        {
            Topography,
            Formation,
            Fault
        }

        #endregion

        #region Constructor

        public Interactive2DProfileDrawingTool(TwoDGeologyDataset dataset)
        {
            _dataset = dataset;
            _profile = dataset.ProfileData ?? CreateDefaultProfile();

            // Initialize view to show full profile
            ResetView();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Draw the tool UI and canvas.
        /// </summary>
        public void Draw()
        {
            // Main window
            if (ImGui.Begin("2D Profile Drawing Tool", ImGuiWindowFlags.MenuBar))
            {
                DrawMenuBar();

                // Split: left toolbar, right canvas
                var contentRegion = ImGui.GetContentRegionAvail();

                // Left toolbar (200px wide)
                if (ImGui.BeginChild("Toolbar", new Vector2(200, contentRegion.Y), ImGuiChildFlags.Border))
                {
                    DrawToolbar();
                }
                ImGui.EndChild();

                ImGui.SameLine();

                // Right canvas
                if (ImGui.BeginChild("Canvas", new Vector2(contentRegion.X - 210, contentRegion.Y), ImGuiChildFlags.Border))
                {
                    DrawCanvas();
                }
                ImGui.EndChild();
            }
            ImGui.End();

            // Help window
            if (_showHelp)
            {
                DrawHelpWindow();
            }
        }

        /// <summary>
        /// Apply changes to the dataset.
        /// </summary>
        public void ApplyChanges()
        {
            _dataset.ProfileData = _profile;
            _dataset.MarkAsModified();
            _statusMessage = "Changes applied to dataset";
            Logger.Log("[Interactive2DProfileDrawingTool] Changes applied");
        }

        #endregion

        #region UI Drawing

        private void DrawMenuBar()
        {
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    if (ImGui.MenuItem("New Profile"))
                    {
                        CreateNewProfile();
                    }
                    if (ImGui.MenuItem("Apply Changes", "Ctrl+S"))
                    {
                        ApplyChanges();
                    }
                    ImGui.Separator();
                    if (ImGui.MenuItem("Reset View", "Home"))
                    {
                        ResetView();
                    }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("View"))
                {
                    ImGui.MenuItem("Show Grid", null, ref _showGrid);
                    ImGui.Separator();
                    if (ImGui.MenuItem("Zoom In", "+"))
                    {
                        _viewZoom = Math.Min(_viewZoom * 1.2f, 10f);
                    }
                    if (ImGui.MenuItem("Zoom Out", "-"))
                    {
                        _viewZoom = Math.Max(_viewZoom / 1.2f, 0.1f);
                    }
                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu("Help"))
                {
                    ImGui.MenuItem("Show Help", "F1", ref _showHelp);
                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }
        }

        private void DrawToolbar()
        {
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Drawing Tools");
            ImGui.Separator();

            // Mode selection
            if (ImGui.RadioButton("Select/Move", _currentMode == DrawMode.SelectMove))
                _currentMode = DrawMode.SelectMove;
            if (ImGui.RadioButton("Draw Topography", _currentMode == DrawMode.DrawTopography))
            {
                _currentMode = DrawMode.DrawTopography;
                _currentLayerType = LayerType.Topography;
            }
            if (ImGui.RadioButton("Draw Layer", _currentMode == DrawMode.DrawLayer))
            {
                _currentMode = DrawMode.DrawLayer;
                _currentLayerType = LayerType.Formation;
            }
            if (ImGui.RadioButton("Erase", _currentMode == DrawMode.Erase))
                _currentMode = DrawMode.Erase;

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Snapping settings
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Snapping");
            ImGui.Separator();

            ImGui.Checkbox("Enable Snapping", ref _enableSnapping);

            if (_enableSnapping)
            {
                ImGui.Indent();
                ImGui.Checkbox("Snap to Grid", ref _snapToGrid);
                ImGui.Checkbox("Snap to Points", ref _snapToPoints);
                ImGui.Checkbox("Snap to Layers", ref _snapToLayers);

                ImGui.DragFloat("Grid Size (m)", ref _snapGridSize, 1f, 1f, 100f);
                ImGui.DragFloat("Point Radius", ref _snapPointRadius, 0.5f, 1f, 20f);
                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Layer properties (when drawing layers)
            if (_currentMode == DrawMode.DrawLayer)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Layer Properties");
                ImGui.Separator();

                ImGui.InputText("Name", ref _currentLayerName, 100);

                // Lithology type combo
                int currentLithIndex = Array.IndexOf(_lithologyTypes, _currentLithologyType);
                if (currentLithIndex < 0) currentLithIndex = 0;

                if (ImGui.Combo("Lithology", ref currentLithIndex, _lithologyTypes, _lithologyTypes.Length))
                {
                    _currentLithologyType = _lithologyTypes[currentLithIndex];
                }

                ImGui.ColorEdit4("Color", ref _currentLayerColor);

                // Color palette
                ImGui.Text("Quick Colors:");
                for (int i = 0; i < _colorPalette.Length; i++)
                {
                    if (i % 4 != 0) ImGui.SameLine();

                    ImGui.PushID(i);
                    if (ImGui.ColorButton($"##palette{i}", _colorPalette[i], ImGuiColorEditFlags.None, new Vector2(30, 30)))
                    {
                        _currentLayerColor = _colorPalette[i];
                    }
                    ImGui.PopID();
                }

                ImGui.Spacing();

                if (ImGui.Button("Finish Layer", new Vector2(-1, 0)) && _currentLayerPoints.Count >= 2)
                {
                    FinishCurrentLayer();
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Profile settings
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Profile Settings");
            ImGui.Separator();

            ImGui.DragFloat("Length (m)", ref _profileLength, 10f, 100f, 10000f);
            ImGui.DragFloat("Min Elev (m)", ref _minElevation, 5f, -1000f, _maxElevation - 10f);
            ImGui.DragFloat("Max Elev (m)", ref _maxElevation, 5f, _minElevation + 10f, 1000f);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Status
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.5f, 1.0f), "Status");
            ImGui.Separator();
            ImGui.TextWrapped(_statusMessage);
        }

        private void DrawCanvas()
        {
            var drawList = ImGui.GetWindowDrawList();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();

            // Canvas background
            drawList.AddRectFilled(canvasPos, canvasPos + canvasSize, ImGui.GetColorU32(new Vector4(0.15f, 0.15f, 0.15f, 1.0f)));

            // Draw grid
            if (_showGrid)
            {
                DrawGrid(drawList, canvasPos, canvasSize);
            }

            // Draw coordinate axes
            DrawAxes(drawList, canvasPos, canvasSize);

            // Draw existing layers
            DrawExistingLayers(drawList, canvasPos, canvasSize);

            // Draw current layer being drawn
            if (_currentMode == DrawMode.DrawLayer || _currentMode == DrawMode.DrawTopography)
            {
                DrawCurrentLayer(drawList, canvasPos, canvasSize);
            }

            // Handle mouse interaction
            HandleCanvasInteraction(canvasPos, canvasSize);

            // Draw cursor crosshair
            DrawCursorCrosshair(drawList, canvasPos, canvasSize);

            // Invisible button for canvas interaction
            ImGui.SetCursorScreenPos(canvasPos);
            ImGui.InvisibleButton("canvas", canvasSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        }

        private void DrawGrid(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
        {
            uint gridColor = ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

            // Vertical grid lines
            float gridSpacing = _snapGridSize * _viewZoom;
            if (gridSpacing < 10) gridSpacing *= 10;  // Avoid too many lines when zoomed out

            for (float x = 0; x <= _profileLength; x += _snapGridSize)
            {
                var screenX = WorldToScreen(new Vector2(x, 0), canvasPos, canvasSize).X;
                if (screenX >= canvasPos.X && screenX <= canvasPos.X + canvasSize.X)
                {
                    drawList.AddLine(
                        new Vector2(screenX, canvasPos.Y),
                        new Vector2(screenX, canvasPos.Y + canvasSize.Y),
                        gridColor);
                }
            }

            // Horizontal grid lines
            for (float y = _minElevation; y <= _maxElevation; y += _snapGridSize)
            {
                var screenY = WorldToScreen(new Vector2(0, y), canvasPos, canvasSize).Y;
                if (screenY >= canvasPos.Y && screenY <= canvasPos.Y + canvasSize.Y)
                {
                    drawList.AddLine(
                        new Vector2(canvasPos.X, screenY),
                        new Vector2(canvasPos.X + canvasSize.X, screenY),
                        gridColor);
                }
            }
        }

        private void DrawAxes(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
        {
            uint axisColor = ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
            uint labelColor = ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 1.0f));

            // X axis (distance)
            var xAxisY = WorldToScreen(new Vector2(0, 0), canvasPos, canvasSize).Y;
            if (xAxisY >= canvasPos.Y && xAxisY <= canvasPos.Y + canvasSize.Y)
            {
                drawList.AddLine(
                    new Vector2(canvasPos.X, xAxisY),
                    new Vector2(canvasPos.X + canvasSize.X, xAxisY),
                    axisColor, 2f);
            }

            // Y axis (elevation)
            var yAxisX = WorldToScreen(new Vector2(0, 0), canvasPos, canvasSize).X;
            if (yAxisX >= canvasPos.X && yAxisX <= canvasPos.X + canvasSize.X)
            {
                drawList.AddLine(
                    new Vector2(yAxisX, canvasPos.Y),
                    new Vector2(yAxisX, canvasPos.Y + canvasSize.Y),
                    axisColor, 2f);
            }

            // Labels
            drawList.AddText(canvasPos + new Vector2(10, canvasSize.Y - 25), labelColor, $"Distance (m)");
            drawList.AddText(canvasPos + new Vector2(10, 10), labelColor, $"Elevation (m)");
        }

        private void DrawExistingLayers(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
        {
            if (_profile?.Profile?.Points == null) return;

            // Draw topography
            uint topoColor = ImGui.GetColorU32(new Vector4(0.4f, 0.7f, 0.3f, 1.0f));
            var topoPoints = _profile.Profile.Points.Select(p => WorldToScreen(p.Position, canvasPos, canvasSize)).ToList();

            if (topoPoints.Count >= 2)
            {
                for (int i = 0; i < topoPoints.Count - 1; i++)
                {
                    drawList.AddLine(topoPoints[i], topoPoints[i + 1], topoColor, 2f);
                }

                // Draw points
                foreach (var pt in topoPoints)
                {
                    drawList.AddCircleFilled(pt, 4f, topoColor);
                }
            }

            // Draw formations
            if (_profile.Formations != null)
            {
                foreach (var formation in _profile.Formations)
                {
                    uint formColor = ImGui.GetColorU32(formation.Color);

                    // Draw top boundary
                    if (formation.TopBoundary != null && formation.TopBoundary.Count >= 2)
                    {
                        var boundaryPoints = formation.TopBoundary.Select(p => WorldToScreen(p, canvasPos, canvasSize)).ToList();

                        for (int i = 0; i < boundaryPoints.Count - 1; i++)
                        {
                            drawList.AddLine(boundaryPoints[i], boundaryPoints[i + 1], formColor, 2f);
                        }

                        foreach (var pt in boundaryPoints)
                        {
                            drawList.AddCircleFilled(pt, 3f, formColor);
                        }

                        // Draw label
                        if (boundaryPoints.Count > 0)
                        {
                            var labelPos = boundaryPoints[boundaryPoints.Count / 2];
                            drawList.AddText(labelPos + new Vector2(5, -20), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), formation.Name ?? "Layer");
                        }
                    }
                }
            }
        }

        private void DrawCurrentLayer(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
        {
            if (_currentLayerPoints.Count == 0) return;

            uint color = ImGui.GetColorU32(_currentLayerColor);
            var screenPoints = _currentLayerPoints.Select(p => WorldToScreen(p, canvasPos, canvasSize)).ToList();

            // Draw lines
            for (int i = 0; i < screenPoints.Count - 1; i++)
            {
                drawList.AddLine(screenPoints[i], screenPoints[i + 1], color, 3f);
            }

            // Draw points
            foreach (var pt in screenPoints)
            {
                drawList.AddCircleFilled(pt, 5f, color);
                drawList.AddCircle(pt, 5f, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), 0, 2f);
            }

            // Draw line from last point to mouse
            if (screenPoints.Count > 0 && ImGui.IsWindowHovered())
            {
                var mousePos = ImGui.GetMousePos();
                drawList.AddLine(screenPoints[screenPoints.Count - 1], mousePos, color, 1f);
            }
        }

        private void DrawCursorCrosshair(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize)
        {
            if (!ImGui.IsWindowHovered()) return;

            var mousePos = ImGui.GetMousePos();
            uint crosshairColor = ImGui.GetColorU32(new Vector4(1.0f, 1.0f, 0.0f, 0.5f));

            // Vertical line
            drawList.AddLine(
                new Vector2(mousePos.X, canvasPos.Y),
                new Vector2(mousePos.X, canvasPos.Y + canvasSize.Y),
                crosshairColor, 1f);

            // Horizontal line
            drawList.AddLine(
                new Vector2(canvasPos.X, mousePos.Y),
                new Vector2(canvasPos.X + canvasSize.X, mousePos.Y),
                crosshairColor, 1f);

            // Show coordinates
            var worldPos = ScreenToWorld(mousePos, canvasPos, canvasSize);
            var coordText = $"({worldPos.X:F1}m, {worldPos.Y:F1}m)";
            drawList.AddText(mousePos + new Vector2(15, 15), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), coordText);
        }

        private void DrawHelpWindow()
        {
            if (ImGui.Begin("Help - 2D Profile Drawing", ref _showHelp))
            {
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Mouse Controls:");
                ImGui.BulletText("Left Click: Add point / Select point");
                ImGui.BulletText("Left Drag: Move point (in Select mode)");
                ImGui.BulletText("Right Click: Remove last point");
                ImGui.BulletText("Middle Drag: Pan view");
                ImGui.BulletText("Scroll Wheel: Zoom");

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Keyboard Shortcuts:");
                ImGui.BulletText("Ctrl+S: Apply changes");
                ImGui.BulletText("Home: Reset view");
                ImGui.BulletText("Delete: Delete selected point");
                ImGui.BulletText("Escape: Cancel current drawing");
                ImGui.BulletText("+/-: Zoom in/out");

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Drawing Workflow:");
                ImGui.BulletText("1. Select 'Draw Topography' to draw surface");
                ImGui.BulletText("2. Click to add points along topography");
                ImGui.BulletText("3. Select 'Draw Layer' for geological layers");
                ImGui.BulletText("4. Set layer name, lithology, and color");
                ImGui.BulletText("5. Draw layer boundary points");
                ImGui.BulletText("6. Click 'Finish Layer' when done");
                ImGui.BulletText("7. Use 'Select/Move' to edit points");
                ImGui.BulletText("8. Apply changes to save to dataset");

                ImGui.Spacing();
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), "Snapping:");
                ImGui.BulletText("Enable snapping for precise alignment");
                ImGui.BulletText("Snap to Grid: Align to regular grid");
                ImGui.BulletText("Snap to Points: Snap to existing points");
                ImGui.BulletText("Snap to Layers: Snap to layer boundaries");
            }
            ImGui.End();
        }

        #endregion

        #region Interaction Handling

        private void HandleCanvasInteraction(Vector2 canvasPos, Vector2 canvasSize)
        {
            if (!ImGui.IsItemHovered()) return;

            var io = ImGui.GetIO();
            var mousePos = ImGui.GetMousePos();
            var worldPos = ScreenToWorld(mousePos, canvasPos, canvasSize);

            // Apply snapping if enabled
            if (_enableSnapping)
            {
                worldPos = ApplySnapping(worldPos, mousePos, canvasPos, canvasSize);
            }

            // Mouse wheel zoom
            if (io.MouseWheel != 0)
            {
                var zoomFactor = 1.0f + io.MouseWheel * 0.1f;
                _viewZoom = Math.Clamp(_viewZoom * zoomFactor, 0.1f, 10f);
            }

            // Middle mouse button - pan view
            if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                if (!_isDraggingView)
                {
                    _isDraggingView = true;
                    _lastMousePos = mousePos;
                }
                else
                {
                    var delta = mousePos - _lastMousePos;
                    _viewOffset += delta / _viewZoom;
                    _lastMousePos = mousePos;
                }
            }
            else
            {
                _isDraggingView = false;
            }

            // Left mouse button - mode-specific interaction
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                HandleLeftClick(worldPos, mousePos, canvasPos, canvasSize);
            }

            // Left mouse drag
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) && _currentMode == DrawMode.SelectMove)
            {
                HandleDrag(worldPos);
            }

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _isDraggingPoint = false;
                _selectedPointIndex = -1;
                _selectedPointType = "none";
                _selectedLayerIndex = -1;
            }

            // Right click - context action
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                HandleRightClick();
            }

            // Keyboard shortcuts
            HandleKeyboard();
        }

        private void HandleLeftClick(Vector2 worldPos, Vector2 mousePos, Vector2 canvasPos, Vector2 canvasSize)
        {
            switch (_currentMode)
            {
                case DrawMode.DrawTopography:
                case DrawMode.DrawLayer:
                    // Add point to current layer
                    _currentLayerPoints.Add(worldPos);
                    _statusMessage = $"Added point {_currentLayerPoints.Count} at ({worldPos.X:F1}, {worldPos.Y:F1})";
                    break;

                case DrawMode.SelectMove:
                    // Try to select a point
                    _selectedPointIndex = FindNearestPoint(mousePos, canvasPos, canvasSize, out _selectedPointType, out _selectedLayerIndex);
                    if (_selectedPointIndex >= 0)
                    {
                        _isDraggingPoint = true;
                        _dragStartPos = worldPos;
                        _statusMessage = $"Selected {_selectedPointType} point {_selectedPointIndex}";
                    }
                    break;

                case DrawMode.Erase:
                    // Erase nearest point
                    var eraseIndex = FindNearestPoint(mousePos, canvasPos, canvasSize, out var eraseType, out var eraseLayer);
                    if (eraseIndex >= 0)
                    {
                        ErasePoint(eraseIndex, eraseType, eraseLayer);
                    }
                    break;
            }
        }

        private void HandleDrag(Vector2 worldPos)
        {
            if (!_isDraggingPoint || _selectedPointIndex < 0) return;

            // Update the actual point in the profile data using stored selection info
            if (_selectedPointType == "topography" && _profile?.Profile?.Points != null &&
                _selectedPointIndex < _profile.Profile.Points.Count)
            {
                var point = _profile.Profile.Points[_selectedPointIndex];
                point.Position = worldPos;
                point.Distance = worldPos.X;
                point.Elevation = worldPos.Y;
                _profile.Profile.Points[_selectedPointIndex] = point;
                _statusMessage = $"Moving topography point to ({worldPos.X:F1}, {worldPos.Y:F1})";
            }
            else if (_selectedPointType == "formation" && _profile?.Formations != null &&
                     _selectedLayerIndex >= 0 && _selectedLayerIndex < _profile.Formations.Count)
            {
                var formation = _profile.Formations[_selectedLayerIndex];
                if (formation.TopBoundary != null && _selectedPointIndex < formation.TopBoundary.Count)
                {
                    formation.TopBoundary[_selectedPointIndex] = worldPos;
                    _statusMessage = $"Moving formation point to ({worldPos.X:F1}, {worldPos.Y:F1})";
                }
            }
        }

        private void HandleRightClick()
        {
            if (_currentLayerPoints.Count > 0)
            {
                _currentLayerPoints.RemoveAt(_currentLayerPoints.Count - 1);
                _statusMessage = "Removed last point";
            }
        }

        private void HandleKeyboard()
        {
            var io = ImGui.GetIO();

            // Ctrl+S - Apply changes
            if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S))
            {
                ApplyChanges();
            }

            // Home - Reset view
            if (ImGui.IsKeyPressed(ImGuiKey.Home))
            {
                ResetView();
            }

            // Escape - Cancel current drawing
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _currentLayerPoints.Clear();
                _statusMessage = "Cancelled drawing";
            }

            // +/- - Zoom
            if (ImGui.IsKeyPressed(ImGuiKey.Equal) || ImGui.IsKeyPressed(ImGuiKey.KeypadAdd))
            {
                _viewZoom = Math.Min(_viewZoom * 1.2f, 10f);
            }
            if (ImGui.IsKeyPressed(ImGuiKey.Minus) || ImGui.IsKeyPressed(ImGuiKey.KeypadSubtract))
            {
                _viewZoom = Math.Max(_viewZoom / 1.2f, 0.1f);
            }
        }

        #endregion

        #region Helper Methods

        private Vector2 WorldToScreen(Vector2 worldPos, Vector2 canvasPos, Vector2 canvasSize)
        {
            // Normalize world position to [0, 1]
            float normX = worldPos.X / _profileLength;
            float normY = (worldPos.Y - _minElevation) / (_maxElevation - _minElevation);

            // Apply zoom and offset
            float screenX = canvasPos.X + (normX * canvasSize.X + _viewOffset.X) * _viewZoom;
            float screenY = canvasPos.Y + canvasSize.Y - (normY * canvasSize.Y + _viewOffset.Y) * _viewZoom;

            return new Vector2(screenX, screenY);
        }

        private Vector2 ScreenToWorld(Vector2 screenPos, Vector2 canvasPos, Vector2 canvasSize)
        {
            // Reverse the transform
            float normX = ((screenPos.X - canvasPos.X) / _viewZoom - _viewOffset.X) / canvasSize.X;
            float normY = (canvasSize.Y - (screenPos.Y - canvasPos.Y) / _viewZoom + _viewOffset.Y) / canvasSize.Y;

            float worldX = normX * _profileLength;
            float worldY = _minElevation + normY * (_maxElevation - _minElevation);

            return new Vector2(worldX, worldY);
        }

        private Vector2 ApplySnapping(Vector2 worldPos, Vector2 screenPos, Vector2 canvasPos, Vector2 canvasSize)
        {
            Vector2 snappedPos = worldPos;
            float minSnapDist = float.MaxValue;

            // Snap to grid
            if (_snapToGrid)
            {
                var gridSnap = new Vector2(
                    MathF.Round(worldPos.X / _snapGridSize) * _snapGridSize,
                    MathF.Round(worldPos.Y / _snapGridSize) * _snapGridSize
                );

                var gridScreenPos = WorldToScreen(gridSnap, canvasPos, canvasSize);
                float dist = Vector2.Distance(screenPos, gridScreenPos);

                if (dist < _snapPointRadius && dist < minSnapDist)
                {
                    snappedPos = gridSnap;
                    minSnapDist = dist;
                }
            }

            // Snap to existing points
            if (_snapToPoints && _profile?.Profile?.Points != null)
            {
                foreach (var point in _profile.Profile.Points)
                {
                    var pointScreenPos = WorldToScreen(point.Position, canvasPos, canvasSize);
                    float dist = Vector2.Distance(screenPos, pointScreenPos);

                    if (dist < _snapPointRadius && dist < minSnapDist)
                    {
                        snappedPos = point.Position;
                        minSnapDist = dist;
                    }
                }
            }

            // Snap to layer boundaries
            if (_snapToLayers && _profile?.Formations != null)
            {
                foreach (var formation in _profile.Formations)
                {
                    if (formation.TopBoundary == null) continue;

                    foreach (var point in formation.TopBoundary)
                    {
                        var pointScreenPos = WorldToScreen(point, canvasPos, canvasSize);
                        float dist = Vector2.Distance(screenPos, pointScreenPos);

                        if (dist < _snapPointRadius && dist < minSnapDist)
                        {
                            snappedPos = point;
                            minSnapDist = dist;
                        }
                    }
                }
            }

            return snappedPos;
        }

        private int FindNearestPoint(Vector2 screenPos, Vector2 canvasPos, Vector2 canvasSize,
            out string pointType, out int layerIndex)
        {
            pointType = "none";
            layerIndex = -1;
            int nearestIndex = -1;
            float minDist = _snapPointRadius;

            // Check topography points
            if (_profile?.Profile?.Points != null)
            {
                for (int i = 0; i < _profile.Profile.Points.Count; i++)
                {
                    var pointScreenPos = WorldToScreen(_profile.Profile.Points[i].Position, canvasPos, canvasSize);
                    float dist = Vector2.Distance(screenPos, pointScreenPos);

                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestIndex = i;
                        pointType = "topography";
                    }
                }
            }

            // Check formation boundaries
            if (_profile?.Formations != null)
            {
                for (int formIdx = 0; formIdx < _profile.Formations.Count; formIdx++)
                {
                    var formation = _profile.Formations[formIdx];
                    if (formation.TopBoundary == null) continue;

                    for (int i = 0; i < formation.TopBoundary.Count; i++)
                    {
                        var pointScreenPos = WorldToScreen(formation.TopBoundary[i], canvasPos, canvasSize);
                        float dist = Vector2.Distance(screenPos, pointScreenPos);

                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearestIndex = i;
                            pointType = "formation";
                            layerIndex = formIdx;
                        }
                    }
                }
            }

            return nearestIndex;
        }

        private void ErasePoint(int index, string pointType, int layerIndex)
        {
            bool erased = false;

            if (pointType == "topography" && _profile?.Profile?.Points != null)
            {
                if (index >= 0 && index < _profile.Profile.Points.Count)
                {
                    _profile.Profile.Points.RemoveAt(index);
                    erased = true;
                    _statusMessage = $"Erased topography point {index}";
                    Logger.Log($"[Interactive2DProfileDrawingTool] Erased topography point {index}");
                }
            }
            else if (pointType == "formation" && _profile?.Formations != null)
            {
                if (layerIndex >= 0 && layerIndex < _profile.Formations.Count)
                {
                    var formation = _profile.Formations[layerIndex];
                    if (formation.TopBoundary != null && index >= 0 && index < formation.TopBoundary.Count)
                    {
                        formation.TopBoundary.RemoveAt(index);
                        erased = true;
                        _statusMessage = $"Erased formation point {index} from '{formation.Name}'";
                        Logger.Log($"[Interactive2DProfileDrawingTool] Erased formation point {index} from {formation.Name}");

                        // Remove formation if it has less than 2 points
                        if (formation.TopBoundary.Count < 2)
                        {
                            _profile.Formations.RemoveAt(layerIndex);
                            _statusMessage += " (formation removed - insufficient points)";
                            Logger.Log($"[Interactive2DProfileDrawingTool] Removed formation {formation.Name} due to insufficient points");
                        }
                    }
                }
            }

            if (!erased)
            {
                _statusMessage = "Failed to erase point - invalid index or type";
            }
        }

        private void FinishCurrentLayer()
        {
            if (_currentLayerPoints.Count < 2)
            {
                _statusMessage = "Need at least 2 points to create a layer";
                return;
            }

            // Create new formation
            var formation = new CrossSectionGenerator.ProjectedFormation
            {
                Name = _currentLayerName,
                LithologyType = _currentLithologyType,
                Color = _currentLayerColor,
                TopBoundary = new List<Vector2>(_currentLayerPoints),
                BottomBoundary = new List<Vector2>() // Will be filled later
            };

            if (_profile.Formations == null)
                _profile.Formations = new List<CrossSectionGenerator.ProjectedFormation>();

            _profile.Formations.Add(formation);

            _statusMessage = $"Created layer '{_currentLayerName}' with {_currentLayerPoints.Count} points";
            Logger.Log($"[Interactive2DProfileDrawingTool] Created formation: {_currentLayerName}");

            // Clear current layer
            _currentLayerPoints.Clear();
            _currentLayerName = "New Layer";
        }

        private void ResetView()
        {
            _viewOffset = Vector2.Zero;
            _viewZoom = 1.0f;
            _statusMessage = "View reset";
        }

        private void CreateNewProfile()
        {
            _profile = CreateDefaultProfile();
            _currentLayerPoints.Clear();
            _selectedPointIndex = -1;
            _statusMessage = "Created new profile";
        }

        private CrossSectionGenerator.CrossSection CreateDefaultProfile()
        {
            var profile = new CrossSectionGenerator.CrossSection
            {
                Profile = new ProfileGenerator.TopographicProfile
                {
                    Name = "New Profile",
                    TotalDistance = _profileLength,
                    MinElevation = _minElevation,
                    MaxElevation = _maxElevation,
                    StartPoint = new Vector2(0, 0),
                    EndPoint = new Vector2(_profileLength, 0),
                    CreatedAt = DateTime.Now,
                    VerticalExaggeration = 1.0f,
                    Points = new List<ProfileGenerator.ProfilePoint>()
                },
                VerticalExaggeration = 1.0f,
                Formations = new List<CrossSectionGenerator.ProjectedFormation>(),
                Faults = new List<CrossSectionGenerator.ProjectedFault>()
            };

            return profile;
        }

        #endregion
    }
}
