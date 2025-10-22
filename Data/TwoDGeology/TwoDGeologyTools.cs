// GeoscientistToolkit/UI/GIS/TwoDGeologyTools.cs

using System.Numerics;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Util;
using ImGuiNET;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
/// Tools for interacting with 2D geological cross-sections
/// </summary>
public class TwoDGeologyTools
{
    private readonly TwoDGeologyViewer _viewer;
    private readonly TwoDGeologyDataset _dataset;

    // Edit modes
    public enum EditMode
    {
        None,
        SelectFormation,
        SelectFault,
        AddFormation,
        AddFault,
        EditBoundary,
        MoveVertex,
        DeleteFeature,
        MeasureDistance,
        MeasureAngle
    }

    public EditMode CurrentEditMode { get; set; } = EditMode.None;

    // Tool state
    private ProjectedFormation _selectedFormation;
    private ProjectedFault _selectedFault;
    private readonly List<Vector2> _tempPoints = new();

    // Measurement state
    private readonly List<Vector2> _measurementPoints = new();
    private float _measuredDistance;
    private float _measuredAngle;

    // Properties for new features
    private Vector4 _newFormationColor = new(0.8f, 0.6f, 0.4f, 0.8f);
    private string _newFormationName = "New Formation";
    private float _newFormationThickness = 200f;
    private GeologicalFeatureType _newFaultType = GeologicalFeatureType.Fault_Normal;
    private float _newFaultDip = 60f;
    private string _newFaultDipDirection = "East";

    // Snapping state
    private readonly float _snapRadius = 50.0f; // World units
    private Vector2? _snapPoint;

    public TwoDGeologyTools(TwoDGeologyViewer viewer, TwoDGeologyDataset dataset)
    {
        _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
    }

    /// <summary>
    /// Render the tools panel
    /// </summary>
    public void RenderToolsPanel()
    {
        ImGui.Text("2D Geology Tools");
        ImGui.Separator();

        var buttonSize = new Vector2(-1, 0);

        ImGui.Text("Selection & Edit Tools:");
        if (ImGui.Button("Select / Move (Q)", buttonSize)) CurrentEditMode = EditMode.SelectFormation;

        ImGui.Separator();

        ImGui.Text("Creation Tools:");
        if (ImGui.Button("Add Formation (W)", buttonSize))
        {
            CurrentEditMode = EditMode.AddFormation;
            _tempPoints.Clear();
        }

        if (CurrentEditMode == EditMode.AddFormation)
        {
            ImGui.Indent();
            ImGui.InputText("Name", ref _newFormationName, 256);
            ImGui.ColorEdit4("Color", ref _newFormationColor);
            ImGui.SliderFloat("Thickness (m)", ref _newFormationThickness, 50f, 2000f, "%.0f");
            ImGui.TextDisabled("Click to add points, right-click to finish.");
            ImGui.Unindent();
        }

        if (ImGui.Button("Add Fault (E)", buttonSize))
        {
            CurrentEditMode = EditMode.AddFault;
            _tempPoints.Clear();
        }

        if (CurrentEditMode == EditMode.AddFault)
        {
            ImGui.Indent();
            if (ImGui.BeginCombo("Type", _newFaultType.ToString().Replace("Fault_", "")))
            {
                foreach (var type in Enum.GetValues<GeologicalFeatureType>().Where(t => t.ToString().Contains("Fault")))
                {
                    if (ImGui.Selectable(type.ToString().Replace("Fault_", ""), _newFaultType == type)) _newFaultType = type;
                }
                ImGui.EndCombo();
            }
            ImGui.SliderFloat("Dip", ref _newFaultDip, 0f, 90f, "%.1f°");
            ImGui.TextDisabled("Click to add points, right-click to finish.");
            ImGui.Unindent();
        }

        ImGui.Separator();

        ImGui.Text("Measurement Tools:");
        if (ImGui.Button("Measure Distance", buttonSize))
        {
            CurrentEditMode = EditMode.MeasureDistance;
            _measurementPoints.Clear();
            _measuredDistance = 0f;
        }

        if (CurrentEditMode == EditMode.MeasureDistance && _measurementPoints.Count > 0)
        {
            ImGui.Indent();
            ImGui.Text($"Distance: {_measuredDistance:F2} m");
            ImGui.TextDisabled("Right-click to finish.");
            ImGui.Unindent();
        }
        
        ImGui.Separator();
        
        ImGui.Text($"Mode: {CurrentEditMode}");
        
        if (CurrentEditMode != EditMode.None)
        {
            if (ImGui.Button("Cancel (ESC)", buttonSize)) CancelCurrentOperation();
        }
    }

    /// <summary>
    /// Handle mouse input for the current tool
    /// </summary>
    public void HandleMouseInput(Vector2 worldPos, bool leftClick, bool rightClick, bool isDragging)
    {
        _snapPoint = FindSnapPoint(worldPos, _snapRadius);
        var effectiveWorldPos = _snapPoint ?? worldPos;

        switch (CurrentEditMode)
        {
            case EditMode.SelectFormation:
                if (leftClick) SelectFormationAt(worldPos);
                break;
            case EditMode.SelectFault:
                if (leftClick) SelectFaultAt(worldPos);
                break;
            case EditMode.AddFormation:
                if (leftClick) AddFormationPoint(effectiveWorldPos);
                if (rightClick) CompleteFormation();
                break;
            case EditMode.AddFault:
                if (leftClick) AddFaultPoint(effectiveWorldPos);
                if (rightClick) CompleteFault();
                break;
            case EditMode.MeasureDistance:
                if (leftClick) AddMeasurementPoint(worldPos);
                if (rightClick) _measurementPoints.Clear();
                break;
        }
    }

    /// <summary>
    /// Handle keyboard input
    /// </summary>
    public void HandleKeyboardInput()
    {
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) CancelCurrentOperation();
        if (ImGui.IsKeyPressed(ImGuiKey.Delete)) DeleteSelectedFeature();
    }

    private void SelectFormationAt(Vector2 worldPos)
    {
        var crossSection = _dataset.ProfileData;
        if (crossSection == null) return;
        
        foreach (var formation in crossSection.Formations.AsEnumerable().Reverse()) // Check topmost first
        {
            if (IsPointInFormation(worldPos, formation))
            {
                SetSelectedFormation(formation);
                Logger.Log($"Selected formation: {formation.Name}");
                return;
            }
        }
        
        ClearSelection();
        Logger.Log("No formation selected");
    }

    private void SelectFaultAt(Vector2 worldPos)
    {
        var crossSection = _dataset.ProfileData;
        if (crossSection == null) return;
        
        const float tolerance = 50f; // World units tolerance
        
        foreach (var fault in crossSection.Faults)
        {
            for (int i = 0; i < fault.FaultTrace.Count - 1; i++)
            {
                var distance = DistanceToLineSegment(worldPos, fault.FaultTrace[i], fault.FaultTrace[i + 1]);
                if (distance < tolerance)
                {
                    SetSelectedFault(fault);
                    Logger.Log($"Selected fault: {fault.Type}");
                    return;
                }
            }
        }
        
        ClearSelection();
        Logger.Log("No fault selected");
    }
    
    private void AddFormationPoint(Vector2 worldPos)
    {
        _tempPoints.Add(worldPos);
        Logger.Log($"Added formation point {_tempPoints.Count} at ({worldPos.X:F0}, {worldPos.Y:F0})");
    }

    private void CompleteFormation()
    {
        if (_tempPoints.Count < 2)
        {
            Logger.LogWarning("Need at least 2 points to create a formation boundary.");
            _tempPoints.Clear();
            return;
        }

        var formation = new ProjectedFormation
        {
            Name = _newFormationName,
            Color = _newFormationColor,
            TopBoundary = new List<Vector2>(_tempPoints),
            BottomBoundary = new List<Vector2>()
        };

        // Create bottom boundary by offsetting top boundary vertically
        foreach (var topPoint in formation.TopBoundary)
            formation.BottomBoundary.Add(new Vector2(topPoint.X, topPoint.Y - _newFormationThickness));

        var cmd = new TwoDGeologyViewer.AddFormationCommand(_dataset.ProfileData, formation);
        _viewer.UndoRedo.ExecuteCommand(cmd);

        Logger.Log($"Created formation: {formation.Name}");
        _tempPoints.Clear();
    }

    private void AddFaultPoint(Vector2 worldPos)
    {
        _tempPoints.Add(worldPos);
        Logger.Log($"Added fault point {_tempPoints.Count} at ({worldPos.X:F0}, {worldPos.Y:F0})");
    }
    
    private void CompleteFault()
    {
        if (_tempPoints.Count < 2)
        {
            Logger.LogWarning("Need at least 2 points to create a fault");
            _tempPoints.Clear();
            return;
        }

        var fault = new ProjectedFault
        {
            Type = _newFaultType,
            FaultTrace = new List<Vector2>(_tempPoints),
            Dip = _newFaultDip,
            DipDirection = _newFaultDipDirection
        };

        var cmd = new TwoDGeologyViewer.AddFaultCommand(_dataset.ProfileData, fault);
        _viewer.UndoRedo.ExecuteCommand(cmd);

        Logger.Log($"Created fault: {fault.Type}");
        _tempPoints.Clear();
    }
    
    private void AddMeasurementPoint(Vector2 worldPos)
    {
        _measurementPoints.Add(worldPos);
        if (_measurementPoints.Count >= 2)
        {
            _measuredDistance = 0f;
            for (var i = 0; i < _measurementPoints.Count - 1; i++)
                _measuredDistance += Vector2.Distance(_measurementPoints[i], _measurementPoints[i + 1]);
            Logger.Log($"Measured distance: {_measuredDistance:F2} m");
        }
    }
    
    private void DeleteSelectedFeature()
    {
        if (_selectedFormation != null)
        {
            var cmd = new TwoDGeologyViewer.RemoveFormationCommand(_dataset.ProfileData, _selectedFormation);
            _viewer.UndoRedo.ExecuteCommand(cmd);
            Logger.Log($"Deleted formation: {_selectedFormation.Name}");
            _selectedFormation = null;
        }
        else if (_selectedFault != null)
        {
            var cmd = new TwoDGeologyViewer.RemoveFaultCommand(_dataset.ProfileData, _selectedFault);
            _viewer.UndoRedo.ExecuteCommand(cmd);
            Logger.Log($"Deleted fault: {_selectedFault.Type}");
            _selectedFault = null;
        }
    }
    
    private void CancelCurrentOperation()
    {
        CurrentEditMode = EditMode.None;
        _tempPoints.Clear();
        _measurementPoints.Clear();
        Logger.Log("Cancelled current operation");
    }

    private Vector2? FindSnapPoint(Vector2 worldPos, float radius)
    {
        var crossSection = _dataset.ProfileData;
        if (crossSection == null) return null;

        var radiusSq = radius * radius;
        Vector2? closestPoint = null;
        var minDistanceSq = float.MaxValue;

        Action<IEnumerable<Vector2>> findInBoundary = boundary =>
        {
            foreach (var vertex in boundary)
            {
                var distSq = Vector2.DistanceSquared(worldPos, vertex);
                if (distSq < radiusSq && distSq < minDistanceSq)
                {
                    minDistanceSq = distSq;
                    closestPoint = vertex;
                }
            }
        };

        foreach (var formation in crossSection.Formations)
        {
            findInBoundary(formation.TopBoundary);
            findInBoundary(formation.BottomBoundary);
        }

        foreach (var fault in crossSection.Faults) findInBoundary(fault.FaultTrace);

        if (crossSection.Profile != null)
            findInBoundary(crossSection.Profile.Points.Select(p => new Vector2(p.Distance, p.Elevation)));

        return closestPoint;
    }
    
    private bool IsPointInFormation(Vector2 point, ProjectedFormation formation)
    {
        if (formation.TopBoundary.Count < 2 || formation.BottomBoundary.Count < 2)
            return false;
        
        var minX = Math.Min(formation.TopBoundary.Min(p => p.X), formation.BottomBoundary.Min(p => p.X));
        var maxX = Math.Max(formation.TopBoundary.Max(p => p.X), formation.BottomBoundary.Max(p => p.X));
        
        if (point.X < minX || point.X > maxX)
            return false;
        
        var topY = InterpolateY(formation.TopBoundary, point.X);
        var bottomY = InterpolateY(formation.BottomBoundary, point.X);
        
        return point.Y >= Math.Min(topY, bottomY) && point.Y <= Math.Max(topY, bottomY);
    }
    
    private float InterpolateY(List<Vector2> boundary, float x)
    {
        if (boundary.Count == 0) return 0f;
        if (boundary.Count == 1) return boundary[0].Y;

        var sortedBoundary = boundary.OrderBy(p => p.X).ToList();
        
        if (x <= sortedBoundary[0].X) return sortedBoundary[0].Y;
        if (x >= sortedBoundary[^1].X) return sortedBoundary[^1].Y;

        for (int i = 0; i < sortedBoundary.Count - 1; i++)
        {
            if (x >= sortedBoundary[i].X && x <= sortedBoundary[i + 1].X)
            {
                var p1 = sortedBoundary[i];
                var p2 = sortedBoundary[i + 1];
                if (Math.Abs(p2.X - p1.X) < 1e-6) return p1.Y;
                
                var t = (x - p1.X) / (p2.X - p1.X);
                return p1.Y + t * (p2.Y - p1.Y);
            }
        }
        
        return sortedBoundary[^1].Y;
    }

    private float DistanceToLineSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var ap = point - a;
        var abLengthSq = ab.LengthSquared();
        
        if (abLengthSq == 0)
            return Vector2.Distance(point, a);
        
        var t = Math.Clamp(Vector2.Dot(ap, ab) / abLengthSq, 0, 1);
        var projection = a + ab * t;
        return Vector2.Distance(point, projection);
    }
    
    public void RenderOverlay(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        var yellow = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 1));
        var red = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 1));

        // Draw temp points for formation/fault creation
        if (_tempPoints.Count > 0)
        {
            for (var i = 0; i < _tempPoints.Count - 1; i++)
                drawList.AddLine(worldToScreen(_tempPoints[i]), worldToScreen(_tempPoints[i + 1]), yellow, 2.0f);
            foreach (var p in _tempPoints) drawList.AddCircleFilled(worldToScreen(p), 5f, red);
        }

        // Draw measurement points
        if (_measurementPoints.Count > 0)
        {
            for (var i = 0; i < _measurementPoints.Count - 1; i++)
                drawList.AddLine(worldToScreen(_measurementPoints[i]), worldToScreen(_measurementPoints[i + 1]), yellow, 2.0f);
            foreach (var p in _measurementPoints) drawList.AddCircleFilled(worldToScreen(p), 5f, red);
        }

        // Draw snap indicator
        if (_snapPoint.HasValue &&
            (CurrentEditMode == EditMode.AddFormation || CurrentEditMode == EditMode.AddFault))
        {
            var cyan = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 1, 1, 0.8f));
            drawList.AddCircle(worldToScreen(_snapPoint.Value), 8f, cyan, 12, 2.0f);
        }
    }
    
    public ProjectedFormation GetSelectedFormation() => _selectedFormation;
    
    public ProjectedFault GetSelectedFault() => _selectedFault;
    
    public void SetSelectedFormation(ProjectedFormation formation)
    {
        _selectedFormation = formation;
        _selectedFault = null;
    }
    
    public void SetSelectedFault(ProjectedFault fault)
    {
        _selectedFault = fault;
        _selectedFormation = null;
    }
    
    public void ClearSelection()
    {
        _selectedFormation = null;
        _selectedFault = null;
    }
}