// GeoscientistToolkit/UI/GIS/TwoDGeologyViewer.cs

using System.Numerics;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.GIS;

public class TwoDGeologyViewer : IDatasetViewer
{
    public enum DrawingMode
    {
        None,
        DrawingLine, // For faults
        DrawingPolygon // For formations
    }

    public enum EditMode
    {
        None,
        EditVertices,
        DrawFault,
        DrawFormation,
        SelectAndMove
    }

    private const float SnappingThreshold = 8.0f; // Screen space pixels

    // Drawing state
    private readonly List<Vector2> _currentDrawing = new();
    private readonly TwoDGeologyDataset _dataset;
    private readonly float _faultDip = 60f;
    private readonly float _faultDisplacement = 100f;
    private readonly ImGuiExportFileDialog _quickExportDialog = new("QuickSvgExport", "Quick Export SVG");
    public readonly UndoRedoManager UndoRedo = new();

    // Drawing tool properties
    private GeologicalMapping.GeologicalFeatureType _currentFaultType =
        GeologicalMapping.GeologicalFeatureType.Fault_Normal;

    private DrawingMode _drawingMode = DrawingMode.None;
    private Vector4 _formationColor = new(0.7f, 0.6f, 0.4f, 0.8f);
    private string _formationName = "New Formation";

    private bool _isDraggingVertex;

    // --- Restoration State ---
    private GeologicalMapping.CrossSectionGenerator.CrossSection _restorationData;
    private List<Vector2> _selectedBoundary; // The specific list of points being edited

    // Selection state
    private object _selectedFeature; // Can be ProjectedFormation or ProjectedFault
    private int _selectedVertexIndex = -1;
    private Vector2? _snappedPoint;
    private float _verticalExaggeration = 2.0f;

    public TwoDGeologyViewer(TwoDGeologyDataset dataset)
    {
        _dataset = dataset;
        _dataset.Load();
        _dataset.RegisterViewer(this);
    }

    // --- Editing State ---
    public EditMode CurrentEditMode { get; set; } = EditMode.None;

    public void DrawToolbarControls()
    {
        ImGui.Text("Vertical Exaggeration:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("##VE", ref _verticalExaggeration, 1f, 10f, "%.1fx"))
            if (_dataset.ProfileData != null)
                _dataset.ProfileData.VerticalExaggeration = _verticalExaggeration;

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();
        if (ImGui.Button("📄 Export SVG..."))
        {
            _quickExportDialog.SetExtensions((".svg", "Scalable Vector Graphics"));
            _quickExportDialog.Open($"cross_section_{DateTime.Now:yyyyMMdd_HHmmss}");
        }

        if (_quickExportDialog.Submit())
        {
            var exporter = new SvgExporter();
            var section = _restorationData ?? _dataset.ProfileData;
            exporter.SaveToFile(_quickExportDialog.SelectedPath, section);
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();
        // Edit mode buttons
        if (ImGui.Button(CurrentEditMode == EditMode.None ? "Edit Mode" : "Stop Editing"))
        {
            CurrentEditMode = CurrentEditMode == EditMode.None ? EditMode.EditVertices : EditMode.None;
            _drawingMode = DrawingMode.None;
            _currentDrawing.Clear();
        }

        if (CurrentEditMode != EditMode.None)
        {
            ImGui.SameLine();
            if (ImGui.BeginCombo("##EditMode", CurrentEditMode.ToString()))
            {
                if (ImGui.Selectable("Edit Vertices", CurrentEditMode == EditMode.EditVertices))
                {
                    CurrentEditMode = EditMode.EditVertices;
                    _drawingMode = DrawingMode.None;
                    _currentDrawing.Clear();
                }

                if (ImGui.Selectable("Draw Fault", CurrentEditMode == EditMode.DrawFault))
                {
                    CurrentEditMode = EditMode.DrawFault;
                    _drawingMode = DrawingMode.DrawingLine;
                    _currentDrawing.Clear();
                }

                if (ImGui.Selectable("Draw Formation", CurrentEditMode == EditMode.DrawFormation))
                {
                    CurrentEditMode = EditMode.DrawFormation;
                    _drawingMode = DrawingMode.DrawingPolygon;
                    _currentDrawing.Clear();
                }

                if (ImGui.Selectable("Select & Move", CurrentEditMode == EditMode.SelectAndMove))
                {
                    CurrentEditMode = EditMode.SelectAndMove;
                    _drawingMode = DrawingMode.None;
                    _currentDrawing.Clear();
                }

                ImGui.EndCombo();
            }

            // Drawing tool properties
            if (CurrentEditMode == EditMode.DrawFault)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(120);
                if (ImGui.BeginCombo("##FaultType", _currentFaultType.ToString()))
                {
                    var faultTypes = new[]
                    {
                        GeologicalMapping.GeologicalFeatureType.Fault_Normal,
                        GeologicalMapping.GeologicalFeatureType.Fault_Reverse,
                        GeologicalMapping.GeologicalFeatureType.Fault_Thrust,
                        GeologicalMapping.GeologicalFeatureType.Fault_Transform
                    };

                    foreach (var type in faultTypes)
                        if (ImGui.Selectable(type.ToString(), _currentFaultType == type))
                            _currentFaultType = type;

                    ImGui.EndCombo();
                }
            }
            else if (CurrentEditMode == EditMode.DrawFormation)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(120);
                ImGui.InputText("##FormName", ref _formationName, 64);
                ImGui.SameLine();
                ImGui.ColorEdit4("##FormColor", ref _formationColor, ImGuiColorEditFlags.NoInputs);
            }
        }
    }

    public void DrawContent(ref float zoom, ref Vector2 pan)
    {
        // Use restoration data if available, otherwise use original
        var crossSection = _restorationData ?? _dataset.ProfileData;

        if (crossSection == null || crossSection.Profile == null)
        {
            ImGui.Text("No cross-section data loaded.");
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var canvasPos = ImGui.GetCursorScreenPos();
        var canvasSize = ImGui.GetContentRegionAvail();
        drawList.AddRectFilled(canvasPos, canvasPos + canvasSize, ImGui.GetColorU32(ImGuiCol.FrameBg));

        var margin = new Vector2(50, 40);
        var plotSize = canvasSize - margin * 2;
        var plotOrigin = canvasPos + new Vector2(margin.X, canvasSize.Y - margin.Y);

        var profile = crossSection.Profile;
        var distRange = profile.TotalDistance;
        var elevRange = profile.MaxElevation - profile.MinElevation;
        if (elevRange < 1f) elevRange = 1f;

        var ve = crossSection.VerticalExaggeration;

        // --- Draw Cross Section Content ---
        DrawFormations(drawList, crossSection, distRange, elevRange, plotOrigin, plotSize, ve);
        DrawFaults(drawList, crossSection, distRange, elevRange, plotOrigin, plotSize, ve);
        DrawTopography(drawList, profile, distRange, elevRange, plotOrigin, plotSize, ve);

        // --- Draw current drawing ---
        if (_currentDrawing.Count > 0) DrawCurrentDrawing(drawList, plotOrigin, plotSize, distRange, elevRange, ve);

        // --- Handle Input ---
        var io = ImGui.GetIO();
        var isHovered = ImGui.IsWindowHovered();

        if (isHovered)
        {
            var mouseScreenPos = io.MousePos;
            var mouseWorldPos = ScreenToWorld(mouseScreenPos, plotOrigin, plotSize, distRange, elevRange, ve);

            // Handle snapping
            _snappedPoint = FindSnapPoint(mouseWorldPos, crossSection, plotOrigin, plotSize, distRange, elevRange, ve);
            var effectiveMousePos = _snappedPoint ?? mouseWorldPos;

            // Draw snap indicator
            if (_snappedPoint.HasValue)
            {
                var snapScreen = WorldToScreen(_snappedPoint.Value, plotOrigin, plotSize, distRange, elevRange, ve);
                drawList.AddCircle(snapScreen, 8f, ImGui.GetColorU32(new Vector4(1, 0, 1, 1)), 0, 2f);
            }

            // Handle different edit modes
            if (CurrentEditMode == EditMode.EditVertices)
            {
                HandleEditingInput(canvasPos, canvasSize, plotOrigin, plotSize, distRange, elevRange, ve);
                DrawVertexHandles(drawList, plotOrigin, plotSize, distRange, elevRange, ve);
            }
            else if (CurrentEditMode == EditMode.DrawFault || CurrentEditMode == EditMode.DrawFormation)
            {
                HandleDrawingInput(effectiveMousePos, io);
            }
            else if (CurrentEditMode == EditMode.SelectAndMove)
            {
                HandleSelectAndMove(effectiveMousePos, io, crossSection);
            }
        }
    }

    public void Dispose()
    {
        /* Nothing to dispose */
    }

    #region Snapping

    private Vector2? FindSnapPoint(Vector2 worldPos, GeologicalMapping.CrossSectionGenerator.CrossSection section,
        Vector2 origin, Vector2 size, float distRange, float elevRange, float ve)
    {
        var snapThresholdWorld = SnappingThreshold / (size.X / distRange);
        var minDist = snapThresholdWorld;
        Vector2? snapPoint = null;

        // Snap to formation vertices
        foreach (var formation in section.Formations)
        foreach (var point in formation.TopBoundary.Concat(formation.BottomBoundary))
        {
            var dist = Vector2.Distance(worldPos, point);
            if (dist < minDist)
            {
                minDist = dist;
                snapPoint = point;
            }
        }

        // Snap to fault vertices
        foreach (var fault in section.Faults)
        foreach (var point in fault.FaultTrace)
        {
            var dist = Vector2.Distance(worldPos, point);
            if (dist < minDist)
            {
                minDist = dist;
                snapPoint = point;
            }
        }

        // Snap to topography
        foreach (var point in section.Profile.Points)
        {
            var topoPoint = new Vector2(point.Distance, point.Elevation);
            var dist = Vector2.Distance(worldPos, topoPoint);
            if (dist < minDist)
            {
                minDist = dist;
                snapPoint = topoPoint;
            }
        }

        return snapPoint;
    }

    #endregion

    #region Vertex Editing

    private void DrawVertexHandles(ImDrawListPtr drawList, Vector2 origin, Vector2 size, float distRange,
        float elevRange, float ve)
    {
        if (_selectedBoundary == null) return;
        var io = ImGui.GetIO();
        var mouseScreenPos = io.MousePos;

        // Main vertices
        for (var i = 0; i < _selectedBoundary.Count; i++)
        {
            var screenPos = WorldToScreen(_selectedBoundary[i], origin, size, distRange, elevRange, ve);
            drawList.AddRectFilled(screenPos - new Vector2(4, 4), screenPos + new Vector2(4, 4), 0xFF00FFFF);
            if (Vector2.Distance(screenPos, mouseScreenPos) < SnappingThreshold)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (io.MouseClicked[0])
                {
                    _isDraggingVertex = true;
                    _selectedVertexIndex = i;
                    var cmd = new MoveVertexCommand(_selectedBoundary, i, _selectedBoundary[i]);
                    UndoRedo.Execute(cmd);
                }
            }
        }

        // Mid-points to add new vertices
        for (var i = 0; i < _selectedBoundary.Count - 1; i++)
        {
            var midPoint = (_selectedBoundary[i] + _selectedBoundary[i + 1]) / 2;
            var screenPos = WorldToScreen(midPoint, origin, size, distRange, elevRange, ve);
            drawList.AddCircleFilled(screenPos, 5, 0x8000FFFF);
            if (Vector2.Distance(screenPos, mouseScreenPos) < SnappingThreshold)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (io.MouseClicked[0])
                {
                    _selectedBoundary.Insert(i + 1, midPoint);
                    _isDraggingVertex = true;
                    _selectedVertexIndex = i + 1;
                }
            }
        }
    }

    #endregion

    #region Drawing Methods

    private void DrawFormations(ImDrawListPtr drawList, GeologicalMapping.CrossSectionGenerator.CrossSection section,
        float distRange, float elevRange, Vector2 origin, Vector2 size, float ve)
    {
        foreach (var formation in section.Formations)
        {
            if (formation.TopBoundary.Count < 2) continue;
            var polyPoints = new List<Vector2>(formation.TopBoundary);
            polyPoints.AddRange(formation.BottomBoundary.AsEnumerable().Reverse());

            var screenPoly = polyPoints.Select(p => WorldToScreen(p, origin, size, distRange, elevRange, ve)).ToArray();

            var color = ImGui.ColorConvertFloat4ToU32(formation.Color);
            if (screenPoly.Length > 2)
                drawList.AddConvexPolyFilled(ref screenPoly[0], screenPoly.Length, color);

            // Highlight selected feature
            if (_selectedFeature == formation)
                drawList.AddPolyline(ref screenPoly[0], screenPoly.Length, 0xFF00FFFF, ImDrawFlags.Closed, 3f);
        }
    }

    private void DrawFaults(ImDrawListPtr drawList, GeologicalMapping.CrossSectionGenerator.CrossSection section,
        float distRange, float elevRange, Vector2 origin, Vector2 size, float ve)
    {
        foreach (var fault in section.Faults)
        {
            var faultPoints = fault.FaultTrace.Select(p => WorldToScreen(p, origin, size, distRange, elevRange, ve))
                .ToArray();
            var color = ImGui.GetColorU32(new Vector4(1, 0, 0, 1));
            var thickness = _selectedFeature == fault ? 3f : 2f;
            if (faultPoints.Length > 1)
                drawList.AddPolyline(ref faultPoints[0], faultPoints.Length, color, ImDrawFlags.None, thickness);
        }
    }

    private void DrawTopography(ImDrawListPtr drawList, GeologicalMapping.ProfileGenerator.TopographicProfile profile,
        float distRange, float elevRange, Vector2 origin, Vector2 size, float ve)
    {
        var profilePoints = profile.Points.Select(p =>
            WorldToScreen(new Vector2(p.Distance, p.Elevation), origin, size, distRange, elevRange, ve)).ToArray();
        if (profilePoints.Length > 1)
            drawList.AddPolyline(ref profilePoints[0], profilePoints.Length,
                ImGui.GetColorU32(new Vector4(0, 0, 0, 1f)), ImDrawFlags.None, 2.5f);
    }

    private void DrawCurrentDrawing(ImDrawListPtr drawList, Vector2 origin, Vector2 size,
        float distRange, float elevRange, float ve)
    {
        if (_currentDrawing.Count < 1) return;

        var color = _drawingMode == DrawingMode.DrawingLine
            ? ImGui.GetColorU32(new Vector4(1, 0, 0, 1))
            : ImGui.GetColorU32(new Vector4(0, 0.7f, 1, 1));

        var screenPoints = _currentDrawing.Select(p => WorldToScreen(p, origin, size, distRange, elevRange, ve))
            .ToList();

        // Draw lines between points
        for (var i = 0; i < screenPoints.Count - 1; i++)
            drawList.AddLine(screenPoints[i], screenPoints[i + 1], color, 2f);

        // Draw points
        foreach (var point in screenPoints) drawList.AddCircleFilled(point, 4f, color);

        // Draw preview line to mouse
        if (_snappedPoint.HasValue)
        {
            var previewPoint = WorldToScreen(_snappedPoint.Value, origin, size, distRange, elevRange, ve);
            drawList.AddLine(screenPoints[^1], previewPoint, color, 1f);
        }
    }

    #endregion

    #region Input Handling

    private void HandleDrawingInput(Vector2 worldPos, ImGuiIOPtr io)
    {
        if (io.MouseClicked[0])
        {
            _currentDrawing.Add(worldPos);

            // Finish drawing on double-click
            if (io.MouseDoubleClicked[0] && _currentDrawing.Count >= 2) FinishDrawing();
        }

        // Cancel with right-click
        if (io.MouseClicked[1]) _currentDrawing.Clear();

        // Finish with Enter key
        if (ImGui.IsKeyPressed(ImGuiKey.Enter) && _currentDrawing.Count >= 2) FinishDrawing();
    }

    private void FinishDrawing()
    {
        if (CurrentEditMode == EditMode.DrawFault && _currentDrawing.Count >= 2)
            CreateFault();
        else if (CurrentEditMode == EditMode.DrawFormation && _currentDrawing.Count >= 3) CreateFormation();

        _currentDrawing.Clear();
    }

    private void CreateFault()
    {
        var newFault = new GeologicalMapping.CrossSectionGenerator.ProjectedFault
        {
            Type = _currentFaultType,
            Dip = _faultDip,
            Displacement = _faultDisplacement,
            FaultTrace = new List<Vector2>(_currentDrawing)
        };

        var crossSection = _restorationData ?? _dataset.ProfileData;
        crossSection.Faults.Add(newFault);

        var cmd = new AddFeatureCommand<GeologicalMapping.CrossSectionGenerator.ProjectedFault>(
            crossSection.Faults, newFault);
        UndoRedo.Execute(cmd);

        Logger.Log($"Created new {_currentFaultType} fault with {_currentDrawing.Count} points");
    }

    private void CreateFormation()
    {
        // Split drawing into top and bottom boundaries
        // Assume first half is top, second half is bottom (reversed)
        var midPoint = _currentDrawing.Count / 2;

        var topBoundary = _currentDrawing.Take(midPoint + 1).ToList();
        var bottomBoundary = _currentDrawing.Skip(midPoint).Reverse().ToList();

        var newFormation = new GeologicalMapping.CrossSectionGenerator.ProjectedFormation
        {
            Name = _formationName,
            Color = _formationColor,
            TopBoundary = topBoundary,
            BottomBoundary = bottomBoundary
        };

        var crossSection = _restorationData ?? _dataset.ProfileData;
        crossSection.Formations.Add(newFormation);

        var cmd = new AddFeatureCommand<GeologicalMapping.CrossSectionGenerator.ProjectedFormation>(
            crossSection.Formations, newFormation);
        UndoRedo.Execute(cmd);

        Logger.Log($"Created new formation '{_formationName}' with {_currentDrawing.Count} points");
    }

    private void HandleSelectAndMove(Vector2 worldPos, ImGuiIOPtr io,
        GeologicalMapping.CrossSectionGenerator.CrossSection crossSection)
    {
        if (io.MouseClicked[0])
        {
            _selectedFeature = FindFeatureAtPoint(worldPos, crossSection);
            _selectedBoundary = null;
            _selectedVertexIndex = -1;
        }
    }

    private void HandleEditingInput(Vector2 canvasPos, Vector2 canvasSize, Vector2 origin, Vector2 size,
        float distRange, float elevRange, float ve)
    {
        var io = ImGui.GetIO();
        if (!ImGui.IsWindowHovered())
        {
            _isDraggingVertex = false;
            return;
        }

        var mouseWorldPos = ScreenToWorld(io.MousePos, origin, size, distRange, elevRange, ve);

        if (_isDraggingVertex && _selectedBoundary != null)
        {
            if (io.MouseReleased[0])
            {
                _isDraggingVertex = false;
                _selectedVertexIndex = -1;
            }
            else
            {
                _selectedBoundary[_selectedVertexIndex] = _snappedPoint ?? mouseWorldPos;
            }
        }
        else if (io.MouseClicked[0])
        {
            FindFeatureToEdit(mouseWorldPos, SnappingThreshold / (size.X / distRange));
        }
    }

    #endregion

    #region Feature Selection

    private object FindFeatureAtPoint(Vector2 worldPos, GeologicalMapping.CrossSectionGenerator.CrossSection section)
    {
        var threshold = 50f; // World space units

        // Check faults first (they're on top)
        foreach (var fault in section.Faults)
            for (var i = 0; i < fault.FaultTrace.Count - 1; i++)
            {
                var dist = GeologicalMapping.ProfileGenerator.DistanceToLineSegment(
                    worldPos, fault.FaultTrace[i], fault.FaultTrace[i + 1]);
                if (dist < threshold)
                    return fault;
            }

        // Check formations
        foreach (var formation in section.Formations)
            if (IsPointInFormation(worldPos, formation))
                return formation;

        return null;
    }

    private bool IsPointInFormation(Vector2 point, GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation)
    {
        // Simple point-in-polygon test
        var polygon = new List<Vector2>(formation.TopBoundary);
        polygon.AddRange(formation.BottomBoundary.AsEnumerable().Reverse());

        return GeologicalMapping.ProfileGenerator.IsPointInPolygon(point, polygon);
    }

    private void FindFeatureToEdit(Vector2 worldPos, float worldThreshold)
    {
        _selectedFeature = null;
        _selectedBoundary = null;
        var minDistance = float.MaxValue;

        var section = _restorationData ?? _dataset.ProfileData;

        // Check faults
        foreach (var fault in section.Faults)
            for (var i = 0; i < fault.FaultTrace.Count - 1; i++)
            {
                var dist = GeologicalMapping.ProfileGenerator.DistanceToLineSegment(worldPos, fault.FaultTrace[i],
                    fault.FaultTrace[i + 1]);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    _selectedFeature = fault;
                    _selectedBoundary = fault.FaultTrace;
                }
            }

        // Check formations
        foreach (var formation in section.Formations)
        {
            for (var i = 0; i < formation.TopBoundary.Count - 1; i++)
            {
                var dist = GeologicalMapping.ProfileGenerator.DistanceToLineSegment(worldPos, formation.TopBoundary[i],
                    formation.TopBoundary[i + 1]);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    _selectedFeature = formation;
                    _selectedBoundary = formation.TopBoundary;
                }
            }

            for (var i = 0; i < formation.BottomBoundary.Count - 1; i++)
            {
                var dist = GeologicalMapping.ProfileGenerator.DistanceToLineSegment(worldPos,
                    formation.BottomBoundary[i], formation.BottomBoundary[i + 1]);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    _selectedFeature = formation;
                    _selectedBoundary = formation.BottomBoundary;
                }
            }
        }

        if (minDistance > worldThreshold)
        {
            _selectedFeature = null;
            _selectedBoundary = null;
        }
    }

    #endregion

    #region Restoration Data Management

    public void SetRestorationData(GeologicalMapping.CrossSectionGenerator.CrossSection data)
    {
        _restorationData = data;
    }

    public void ClearRestorationData()
    {
        _restorationData = null;
    }

    #endregion

    #region Coordinate Transformation

    private Vector2 WorldToScreen(Vector2 worldPos, Vector2 origin, Vector2 size, float distRange, float elevRange,
        float ve)
    {
        var x = worldPos.X / distRange * size.X;
        var y = (worldPos.Y - _dataset.ProfileData.Profile.MinElevation) / elevRange * size.Y * ve;
        return origin + new Vector2(x, -y);
    }

    private Vector2 ScreenToWorld(Vector2 screenPos, Vector2 origin, Vector2 size, float distRange, float elevRange,
        float ve)
    {
        var relativePos = screenPos - origin;
        var x = relativePos.X / size.X * distRange;
        var y = relativePos.Y / -size.Y / ve * elevRange + _dataset.ProfileData.Profile.MinElevation;
        return new Vector2(x, y);
    }

    #endregion

    #region Command Classes

    private class MoveVertexCommand : ICommand
    {
        private readonly List<Vector2> _boundary;
        private readonly int _index;
        private readonly Vector2 _originalPosition;
        private Vector2 _newPosition;

        public MoveVertexCommand(List<Vector2> boundary, int index, Vector2 originalPosition)
        {
            _boundary = boundary;
            _index = index;
            _originalPosition = originalPosition;
        }

        public void Execute()
        {
            _boundary[_index] = _newPosition;
        }

        public void Undo()
        {
            _boundary[_index] = _originalPosition;
        }

        public void UpdateNewPosition(Vector2 pos)
        {
            _newPosition = pos;
        }
    }

    private class AddFeatureCommand<T> : ICommand
    {
        private readonly T _feature;
        private readonly List<T> _list;

        public AddFeatureCommand(List<T> list, T feature)
        {
            _list = list;
            _feature = feature;
        }

        public void Execute()
        {
            if (!_list.Contains(_feature))
                _list.Add(_feature);
        }

        public void Undo()
        {
            _list.Remove(_feature);
        }
    }

    #endregion
}