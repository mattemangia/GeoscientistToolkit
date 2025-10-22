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
    private List<Vector2> _tempPoints = new();
    private bool _isDrawing = false;
    
    // Measurement state
    private List<Vector2> _measurementPoints = new();
    private float _measuredDistance = 0f;
    private float _measuredAngle = 0f;
    
    // Colors for drawing
    private Vector4 _newFormationColor = new Vector4(0.8f, 0.6f, 0.4f, 0.8f);
    private string _newFormationName = "New Formation";
    private GeologicalFeatureType _newFaultType = GeologicalFeatureType.Fault_Normal;
    private float _newFaultDip = 60f;
    private string _newFaultDipDirection = "East";
    
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
        
        // Selection tools
        ImGui.Text("Selection Tools:");
        if (ImGui.Button("Select Formation"))
            CurrentEditMode = EditMode.SelectFormation;
        ImGui.SameLine();
        if (ImGui.Button("Select Fault"))
            CurrentEditMode = EditMode.SelectFault;
        
        ImGui.Separator();
        
        // Creation tools
        ImGui.Text("Creation Tools:");
        if (ImGui.Button("Add Formation"))
        {
            CurrentEditMode = EditMode.AddFormation;
            _tempPoints.Clear();
        }
        
        if (CurrentEditMode == EditMode.AddFormation)
        {
            ImGui.Indent();
            ImGui.InputText("Name", ref _newFormationName, 256);
            ImGui.ColorEdit4("Color", ref _newFormationColor);
            ImGui.Unindent();
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Add Fault"))
        {
            CurrentEditMode = EditMode.AddFault;
            _tempPoints.Clear();
        }
        
        if (CurrentEditMode == EditMode.AddFault)
        {
            ImGui.Indent();
            
            // Fault type selection
            int currentType = (int)_newFaultType;
            if (ImGui.Combo("Type", ref currentType, 
                "Normal\0Reverse\0Transform\0Thrust\0Detachment\0Undefined\0"))
            {
                _newFaultType = currentType switch
                {
                    0 => GeologicalFeatureType.Fault_Normal,
                    1 => GeologicalFeatureType.Fault_Reverse,
                    2 => GeologicalFeatureType.Fault_Transform,
                    3 => GeologicalFeatureType.Fault_Thrust,
                    4 => GeologicalFeatureType.Fault_Detachment,
                    5 => GeologicalFeatureType.Fault_Undefined,
                    _ => GeologicalFeatureType.Fault_Normal
                };
            }
            
            ImGui.SliderFloat("Dip", ref _newFaultDip, 0f, 90f, "%.1f°");
            ImGui.InputText("Dip Direction", ref _newFaultDipDirection, 64);
            ImGui.Unindent();
        }
        
        ImGui.Separator();
        
        // Edit tools
        ImGui.Text("Edit Tools:");
        if (ImGui.Button("Edit Boundary"))
            CurrentEditMode = EditMode.EditBoundary;
        ImGui.SameLine();
        if (ImGui.Button("Move Vertex"))
            CurrentEditMode = EditMode.MoveVertex;
        ImGui.SameLine();
        if (ImGui.Button("Delete"))
            CurrentEditMode = EditMode.DeleteFeature;
        
        ImGui.Separator();
        
        // Measurement tools
        ImGui.Text("Measurement Tools:");
        if (ImGui.Button("Measure Distance"))
        {
            CurrentEditMode = EditMode.MeasureDistance;
            _measurementPoints.Clear();
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Measure Angle"))
        {
            CurrentEditMode = EditMode.MeasureAngle;
            _measurementPoints.Clear();
        }
        
        if (CurrentEditMode == EditMode.MeasureDistance && _measuredDistance > 0)
        {
            ImGui.Indent();
            ImGui.Text($"Distance: {_measuredDistance:F2} m");
            ImGui.Unindent();
        }
        
        if (CurrentEditMode == EditMode.MeasureAngle && _measuredAngle > 0)
        {
            ImGui.Indent();
            ImGui.Text($"Angle: {_measuredAngle:F2}°");
            ImGui.Unindent();
        }
        
        ImGui.Separator();
        
        // Current mode display
        ImGui.Text($"Mode: {CurrentEditMode}");
        
        // Cancel button
        if (CurrentEditMode != EditMode.None)
        {
            if (ImGui.Button("Cancel (ESC)"))
            {
                CancelCurrentOperation();
            }
        }
    }
    
    /// <summary>
    /// Handle mouse input for the current tool
    /// </summary>
    public void HandleMouseInput(Vector2 worldPos, bool leftClick, bool rightClick)
    {
        switch (CurrentEditMode)
        {
            case EditMode.SelectFormation:
                if (leftClick)
                    SelectFormationAt(worldPos);
                break;
                
            case EditMode.SelectFault:
                if (leftClick)
                    SelectFaultAt(worldPos);
                break;
                
            case EditMode.AddFormation:
                if (leftClick)
                    AddFormationPoint(worldPos);
                if (rightClick)
                    CompleteFormation();
                break;
                
            case EditMode.AddFault:
                if (leftClick)
                    AddFaultPoint(worldPos);
                if (rightClick)
                    CompleteFault();
                break;
                
            case EditMode.MeasureDistance:
                if (leftClick)
                    AddMeasurementPoint(worldPos);
                break;
                
            case EditMode.MeasureAngle:
                if (leftClick)
                    AddAngleMeasurementPoint(worldPos);
                break;
        }
    }
    
    /// <summary>
    /// Handle keyboard input
    /// </summary>
    public void HandleKeyboardInput()
    {
        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            CancelCurrentOperation();
        }
        
        if (ImGui.IsKeyPressed(ImGuiKey.Delete))
        {
            DeleteSelectedFeature();
        }
    }
    
    private void SelectFormationAt(Vector2 worldPos)
    {
        var crossSection = _dataset.ProfileData;
        if (crossSection == null) return;
        
        foreach (var formation in crossSection.Formations)
        {
            if (IsPointInFormation(worldPos, formation))
            {
                _selectedFormation = formation;
                _selectedFault = null;
                Logger.Log($"Selected formation: {formation.Name}");
                return;
            }
        }
        
        _selectedFormation = null;
        Logger.Log("No formation selected");
    }
    
    private void SelectFaultAt(Vector2 worldPos)
    {
        var crossSection = _dataset.ProfileData;
        if (crossSection == null) return;
        
        const float tolerance = 50f; // 50 meters tolerance
        
        foreach (var fault in crossSection.Faults)
        {
            for (int i = 0; i < fault.FaultTrace.Count - 1; i++)
            {
                var distance = DistanceToLineSegment(worldPos, fault.FaultTrace[i], fault.FaultTrace[i + 1]);
                if (distance < tolerance)
                {
                    _selectedFault = fault;
                    _selectedFormation = null;
                    Logger.Log($"Selected fault: {fault.Type}");
                    return;
                }
            }
        }
        
        _selectedFault = null;
        Logger.Log("No fault selected");
    }
    
    private void AddFormationPoint(Vector2 worldPos)
    {
        _tempPoints.Add(worldPos);
        Logger.Log($"Added point {_tempPoints.Count} at ({worldPos.X:F0}, {worldPos.Y:F0})");
    }
    
    private void CompleteFormation()
    {
        if (_tempPoints.Count < 3)
        {
            Logger.LogWarning("Need at least 3 points to create a formation");
            return;
        }
        
        // Create a simple formation with horizontal top and bottom boundaries
        var minX = _tempPoints.Min(p => p.X);
        var maxX = _tempPoints.Max(p => p.X);
        var avgY = _tempPoints.Average(p => p.Y);
        var thickness = 200f; // Default 200m thickness
        
        var formation = new ProjectedFormation
        {
            Name = _newFormationName,
            Color = _newFormationColor,
            TopBoundary = new List<Vector2>
            {
                new Vector2(minX, avgY + thickness / 2),
                new Vector2(maxX, avgY + thickness / 2)
            },
            BottomBoundary = new List<Vector2>
            {
                new Vector2(minX, avgY - thickness / 2),
                new Vector2(maxX, avgY - thickness / 2)
            }
        };
        
        _dataset.ProfileData.Formations.Add(formation);
        Logger.Log($"Created formation: {formation.Name}");
        
        _tempPoints.Clear();
        CurrentEditMode = EditMode.None;
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
            return;
        }
        
        var fault = new ProjectedFault
        {
            Type = _newFaultType,
            FaultTrace = new List<Vector2>(_tempPoints),
            Dip = _newFaultDip,
            DipDirection = _newFaultDipDirection
        };
        
        _dataset.ProfileData.Faults.Add(fault);
        Logger.Log($"Created fault: {fault.Type}");
        
        _tempPoints.Clear();
        CurrentEditMode = EditMode.None;
    }
    
    private void AddMeasurementPoint(Vector2 worldPos)
    {
        _measurementPoints.Add(worldPos);
        
        if (_measurementPoints.Count >= 2)
        {
            // Calculate total distance
            _measuredDistance = 0f;
            for (int i = 0; i < _measurementPoints.Count - 1; i++)
            {
                _measuredDistance += Vector2.Distance(_measurementPoints[i], _measurementPoints[i + 1]);
            }
            
            Logger.Log($"Measured distance: {_measuredDistance:F2} m");
        }
    }
    
    private void AddAngleMeasurementPoint(Vector2 worldPos)
    {
        _measurementPoints.Add(worldPos);
        
        if (_measurementPoints.Count == 3)
        {
            // Calculate angle between three points
            var v1 = _measurementPoints[0] - _measurementPoints[1];
            var v2 = _measurementPoints[2] - _measurementPoints[1];
            
            var dot = Vector2.Dot(Vector2.Normalize(v1), Vector2.Normalize(v2));
            _measuredAngle = MathF.Acos(Math.Clamp(dot, -1f, 1f)) * 180f / MathF.PI;
            
            Logger.Log($"Measured angle: {_measuredAngle:F2}°");
            
            _measurementPoints.Clear();
        }
    }
    
    private void DeleteSelectedFeature()
    {
        if (_selectedFormation != null)
        {
            _dataset.ProfileData.Formations.Remove(_selectedFormation);
            Logger.Log($"Deleted formation: {_selectedFormation.Name}");
            _selectedFormation = null;
        }
        else if (_selectedFault != null)
        {
            _dataset.ProfileData.Faults.Remove(_selectedFault);
            Logger.Log($"Deleted fault: {_selectedFault.Type}");
            _selectedFault = null;
        }
    }
    
    private void CancelCurrentOperation()
    {
        CurrentEditMode = EditMode.None;
        _tempPoints.Clear();
        _measurementPoints.Clear();
        _isDrawing = false;
        Logger.Log("Cancelled current operation");
    }
    
    private bool IsPointInFormation(Vector2 point, ProjectedFormation formation)
    {
        // Simple check: is point between top and bottom boundaries?
        if (formation.TopBoundary.Count < 2 || formation.BottomBoundary.Count < 2)
            return false;
        
        var minX = Math.Min(formation.TopBoundary.Min(p => p.X), formation.BottomBoundary.Min(p => p.X));
        var maxX = Math.Max(formation.TopBoundary.Max(p => p.X), formation.BottomBoundary.Max(p => p.X));
        
        if (point.X < minX || point.X > maxX)
            return false;
        
        // Get Y values at this X position
        var topY = InterpolateY(formation.TopBoundary, point.X);
        var bottomY = InterpolateY(formation.BottomBoundary, point.X);
        
        return point.Y >= Math.Min(topY, bottomY) && point.Y <= Math.Max(topY, bottomY);
    }
    
    private float InterpolateY(List<Vector2> boundary, float x)
    {
        if (boundary.Count == 0) return 0f;
        if (boundary.Count == 1) return boundary[0].Y;
        
        // Find the two points that bracket x
        for (int i = 0; i < boundary.Count - 1; i++)
        {
            if (x >= boundary[i].X && x <= boundary[i + 1].X)
            {
                // Linear interpolation
                var t = (x - boundary[i].X) / (boundary[i + 1].X - boundary[i].X);
                return boundary[i].Y + t * (boundary[i + 1].Y - boundary[i].Y);
            }
        }
        
        // Outside bounds, return closest point
        if (x < boundary[0].X) return boundary[0].Y;
        return boundary[^1].Y;
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
    
    /// <summary>
    /// Render temporary drawing elements (points being added, etc.)
    /// </summary>
    public void RenderOverlay(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize, 
        Func<Vector2, Vector2> worldToScreen)
    {
        if (_tempPoints.Count == 0 && _measurementPoints.Count == 0)
            return;
        
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 1)); // Yellow
        var pointColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 1)); // Red
        
        // Draw temp points for formation/fault creation
        if (_tempPoints.Count > 0)
        {
            // Draw lines
            for (int i = 0; i < _tempPoints.Count - 1; i++)
            {
                var p1 = worldToScreen(_tempPoints[i]);
                var p2 = worldToScreen(_tempPoints[i + 1]);
                drawList.AddLine(p1, p2, color, 2.0f);
            }
            
            // Draw points
            foreach (var point in _tempPoints)
            {
                var screenPoint = worldToScreen(point);
                drawList.AddCircleFilled(screenPoint, 5f, pointColor);
            }
        }
        
        // Draw measurement points
        if (_measurementPoints.Count > 0)
        {
            // Draw lines
            for (int i = 0; i < _measurementPoints.Count - 1; i++)
            {
                var p1 = worldToScreen(_measurementPoints[i]);
                var p2 = worldToScreen(_measurementPoints[i + 1]);
                drawList.AddLine(p1, p2, color, 2.0f);
            }
            
            // Draw points
            foreach (var point in _measurementPoints)
            {
                var screenPoint = worldToScreen(point);
                drawList.AddCircleFilled(screenPoint, 5f, pointColor);
            }
        }
    }
    
    /// <summary>
    /// Get the currently selected formation
    /// </summary>
    public ProjectedFormation GetSelectedFormation() => _selectedFormation;
    
    /// <summary>
    /// Get the currently selected fault
    /// </summary>
    public ProjectedFault GetSelectedFault() => _selectedFault;
    
    /// <summary>
    /// Set the selected formation
    /// </summary>
    public void SetSelectedFormation(ProjectedFormation formation)
    {
        _selectedFormation = formation;
        _selectedFault = null;
    }
    
    /// <summary>
    /// Set the selected fault
    /// </summary>
    public void SetSelectedFault(ProjectedFault fault)
    {
        _selectedFault = fault;
        _selectedFormation = null;
    }
    
    /// <summary>
    /// Clear selection
    /// </summary>
    public void ClearSelection()
    {
        _selectedFormation = null;
        _selectedFault = null;
    }
}