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
        DrawFormation,
        DrawFault,
        EditBoundary,
        MoveVertex,
        DeleteFeature,
        MeasureDistance,
        MeasureAngle,
        SplitFormation,
        MergeFormations
    }

    public EditMode CurrentEditMode { get; set; } = EditMode.None;

    // Tool state
    private ProjectedFormation _selectedFormation;
    private ProjectedFault _selectedFault;
    private readonly List<Vector2> _tempPoints = new();
    private bool _isDrawing = false;

    // Measurement state
    private readonly List<Vector2> _measurementPoints = new();
    private float _measuredDistance;
    private float _measuredAngle;

    // Properties for new features
    private Vector4 _newFormationColor = new(0.8f, 0.6f, 0.4f, 0.8f);
    private string _newFormationName = "New Formation";
    private float _newFormationThickness = 300f;
    private bool _drawFormationAsPolygon = false;
    private GeologicalFeatureType _newFaultType = GeologicalFeatureType.Fault_Normal;
    private float _newFaultDip = 60f;
    private string _newFaultDipDirection = "East";
    private float _newFaultDisplacement = 100f;

    // Snapping state
    private readonly float _snapRadius = 50.0f; // World units
    private Vector2? _snapPoint;
    private bool _enableSnapping = true;
    
    // Mouse hover state
    private Vector2 _lastMouseWorldPos;
    
    public TwoDGeologyTools(TwoDGeologyViewer viewer, TwoDGeologyDataset dataset)
    {
        _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
    }

    /// <summary>
    /// Check if any tool is currently active
    /// </summary>
    public bool IsActive()
    {
        return CurrentEditMode != EditMode.None || _isDrawing || _tempPoints.Count > 0;
    }

    /// <summary>
    /// Render the tools panel
    /// </summary>
    public void RenderToolsPanel()
    {
        ImGui.Text("2D Geology Tools");
        ImGui.Separator();

        var buttonSize = new Vector2(-1, 0);

        // Tool mode buttons
        ImGui.Text("Selection Tools:");
        if (ImGui.Button("Select / Move (Q)", buttonSize)) 
        {
            CurrentEditMode = EditMode.SelectFormation;
            ClearTempPoints();
        }
        
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to select formations or faults\nDrag vertices to edit shapes");

        ImGui.Separator();

        ImGui.Text("Drawing Tools:");
        
        // Draw Formation button
        if (ImGui.Button("Draw Formation (W)", buttonSize))
        {
            CurrentEditMode = EditMode.DrawFormation;
            ClearTempPoints();
            _isDrawing = false;
        }
        
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to place points\nRight-click to finish\nESC to cancel");

        if (CurrentEditMode == EditMode.DrawFormation)
        {
            ImGui.Indent();
            ImGui.InputText("Name", ref _newFormationName, 256);
            ImGui.ColorEdit4("Color", ref _newFormationColor);
            ImGui.SliderFloat("Thickness (m)", ref _newFormationThickness, 50f, 2000f, "%.0f");
            ImGui.Checkbox("Draw as Polygon", ref _drawFormationAsPolygon);
            
            if (_tempPoints.Count > 0)
            {
                ImGui.Text($"Points: {_tempPoints.Count}");
                if (ImGui.Button("Complete (Right-Click)")) CompleteFormation();
                ImGui.SameLine();
                if (ImGui.Button("Cancel (ESC)")) CancelCurrentOperation();
            }
            else
            {
                ImGui.TextDisabled("Click in the viewport to start drawing");
            }
            ImGui.Unindent();
        }

        // Draw Fault button
        if (ImGui.Button("Draw Fault (E)", buttonSize))
        {
            CurrentEditMode = EditMode.DrawFault;
            ClearTempPoints();
            _isDrawing = false;
        }
        
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click to place fault trace points\nRight-click to finish");

        if (CurrentEditMode == EditMode.DrawFault)
        {
            ImGui.Indent();
            if (ImGui.BeginCombo("Type", _newFaultType.ToString().Replace("Fault_", "")))
            {
                foreach (var type in Enum.GetValues<GeologicalFeatureType>().Where(t => t.ToString().Contains("Fault")))
                {
                    if (ImGui.Selectable(type.ToString().Replace("Fault_", ""), _newFaultType == type))
                    {
                        _newFaultType = type;
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SliderFloat("Dip", ref _newFaultDip, 0f, 90f, "%.1f°");
            ImGui.InputText("Dip Direction", ref _newFaultDipDirection, 64);
            ImGui.InputFloat("Displacement (m)", ref _newFaultDisplacement);
            
            if (_tempPoints.Count > 0)
            {
                ImGui.Text($"Points: {_tempPoints.Count}");
                if (ImGui.Button("Complete (Right-Click)")) CompleteFault();
                ImGui.SameLine();
                if (ImGui.Button("Cancel (ESC)")) CancelCurrentOperation();
            }
            else
            {
                ImGui.TextDisabled("Click in the viewport to start drawing");
            }
            ImGui.Unindent();
        }

        ImGui.Separator();

        ImGui.Text("Modification Tools:");
        
        if (ImGui.Button("Split Formation", buttonSize))
        {
            CurrentEditMode = EditMode.SplitFormation;
            ClearTempPoints();
        }
        
        if (ImGui.Button("Merge Formations", buttonSize))
        {
            CurrentEditMode = EditMode.MergeFormations;
        }

        ImGui.Separator();

        ImGui.Text("Measurement Tools:");
        if (ImGui.Button("Measure Distance (M)", buttonSize))
        {
            CurrentEditMode = EditMode.MeasureDistance;
            _measurementPoints.Clear();
            _measuredDistance = 0f;
        }

        if (CurrentEditMode == EditMode.MeasureDistance && _measurementPoints.Count > 0)
        {
            ImGui.Indent();
            ImGui.Text($"Distance: {_measuredDistance:F2} m");
            if (_measurementPoints.Count >= 2)
            {
                var dx = _measurementPoints.Last().X - _measurementPoints.First().X;
                var dy = _measurementPoints.Last().Y - _measurementPoints.First().Y;
                ImGui.Text($"Horizontal: {Math.Abs(dx):F2} m");
                ImGui.Text($"Vertical: {Math.Abs(dy):F2} m");
            }
            ImGui.TextDisabled("Right-click to finish");
            ImGui.Unindent();
        }
        
        ImGui.Separator();
        
        ImGui.Text("Options:");
        ImGui.Checkbox("Enable Snapping", ref _enableSnapping);
        if (_enableSnapping)
        {
            ImGui.SameLine();
            ImGui.Text($"(Radius: {_snapRadius:F0}m)");
        }
        
        ImGui.Separator();
        
        ImGui.Text($"Mode: {GetModeDisplayName()}");
        
        if (_lastMouseWorldPos != Vector2.Zero)
        {
            ImGui.Text($"Cursor: ({_lastMouseWorldPos.X:F0}, {_lastMouseWorldPos.Y:F0})");
        }
        
        if (CurrentEditMode != EditMode.None && _tempPoints.Count == 0)
        {
            ImGui.Separator();
            if (ImGui.Button("Cancel Mode (ESC)", buttonSize)) CancelCurrentOperation();
        }
        
        // Keyboard shortcuts help
        ImGui.Separator();
        ImGui.Text("Shortcuts:");
        ImGui.TextDisabled("Q - Select mode");
        ImGui.TextDisabled("W - Draw formation");
        ImGui.TextDisabled("E - Draw fault");
        ImGui.TextDisabled("M - Measure");
        ImGui.TextDisabled("DEL - Delete selected");
        ImGui.TextDisabled("ESC - Cancel operation");
    }

    private string GetModeDisplayName()
    {
        return CurrentEditMode switch
        {
            EditMode.None => "None",
            EditMode.SelectFormation => "Select",
            EditMode.DrawFormation => _tempPoints.Count > 0 ? $"Drawing Formation ({_tempPoints.Count} points)" : "Draw Formation",
            EditMode.DrawFault => _tempPoints.Count > 0 ? $"Drawing Fault ({_tempPoints.Count} points)" : "Draw Fault",
            EditMode.MeasureDistance => "Measuring",
            EditMode.SplitFormation => "Split Formation",
            EditMode.MergeFormations => "Merge Formations",
            _ => CurrentEditMode.ToString()
        };
    }

    /// <summary>
    /// Handle mouse input for the current tool
    /// </summary>
    public void HandleMouseInput(Vector2 worldPos, bool leftClick, bool rightClick, bool isDragging)
    {
        _lastMouseWorldPos = worldPos;
        
        // Update snap point if snapping is enabled
        _snapPoint = _enableSnapping ? FindSnapPoint(worldPos, _snapRadius) : null;
        var effectiveWorldPos = _snapPoint ?? worldPos;

        switch (CurrentEditMode)
        {
            case EditMode.SelectFormation:
                if (leftClick) SelectFormationAt(worldPos);
                break;
                
            case EditMode.SelectFault:
                if (leftClick) SelectFaultAt(worldPos);
                break;
                
            case EditMode.DrawFormation:
                if (leftClick) 
                {
                    AddFormationPoint(effectiveWorldPos);
                    _isDrawing = true;
                }
                if (rightClick && _tempPoints.Count > 0) CompleteFormation();
                break;
                
            case EditMode.DrawFault:
                if (leftClick)
                {
                    AddFaultPoint(effectiveWorldPos);
                    _isDrawing = true;
                }
                if (rightClick && _tempPoints.Count > 0) CompleteFault();
                break;
                
            case EditMode.MeasureDistance:
                if (leftClick) AddMeasurementPoint(effectiveWorldPos);
                if (rightClick) _measurementPoints.Clear();
                break;
                
            case EditMode.SplitFormation:
                if (leftClick && _selectedFormation != null)
                {
                    _tempPoints.Add(effectiveWorldPos);
                    if (_tempPoints.Count >= 2) SplitFormation();
                }
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
        
        // Keyboard shortcuts for tool modes
        if (!ImGui.GetIO().WantTextInput)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Q))
            {
                CurrentEditMode = EditMode.SelectFormation;
                ClearTempPoints();
            }
            if (ImGui.IsKeyPressed(ImGuiKey.W))
            {
                CurrentEditMode = EditMode.DrawFormation;
                ClearTempPoints();
            }
            if (ImGui.IsKeyPressed(ImGuiKey.E))
            {
                CurrentEditMode = EditMode.DrawFault;
                ClearTempPoints();
            }
            if (ImGui.IsKeyPressed(ImGuiKey.M))
            {
                CurrentEditMode = EditMode.MeasureDistance;
                _measurementPoints.Clear();
            }
        }
    }

    private void SelectFormationAt(Vector2 worldPos)
    {
        var crossSection = _dataset.ProfileData;
        if (crossSection == null) return;
        
        // Check formations from top to bottom (reverse order)
        for (int i = crossSection.Formations.Count - 1; i >= 0; i--)
        {
            var formation = crossSection.Formations[i];
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
            Logger.LogWarning("Need at least 2 points to create a formation");
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

        if (_drawFormationAsPolygon)
        {
            // Use the drawn points as a closed polygon
            // Bottom boundary is the same as top, creating a filled polygon
            formation.BottomBoundary = new List<Vector2>(formation.TopBoundary);
            formation.BottomBoundary.Reverse();
            
            // Offset bottom slightly to create thickness
            for (int i = 0; i < formation.BottomBoundary.Count; i++)
            {
                formation.BottomBoundary[i] = new Vector2(
                    formation.BottomBoundary[i].X,
                    formation.BottomBoundary[i].Y - _newFormationThickness);
            }
        }
        else
        {
            // Create bottom boundary by offsetting top boundary vertically
            foreach (var topPoint in formation.TopBoundary)
            {
                formation.BottomBoundary.Add(new Vector2(topPoint.X, topPoint.Y - _newFormationThickness));
            }
        }

        var cmd = new TwoDGeologyViewer.AddFormationCommand(_dataset.ProfileData, formation);
        _viewer.UndoRedo.ExecuteCommand(cmd);

        Logger.Log($"Created formation: {formation.Name}");
        _tempPoints.Clear();
        _isDrawing = false;
        
        // Auto-select the new formation
        SetSelectedFormation(formation);
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
            DipDirection = _newFaultDipDirection,
            Displacement = _newFaultDisplacement
        };

        var cmd = new TwoDGeologyViewer.AddFaultCommand(_dataset.ProfileData, fault);
        _viewer.UndoRedo.ExecuteCommand(cmd);

        Logger.Log($"Created fault: {fault.Type}");
        _tempPoints.Clear();
        _isDrawing = false;
        
        // Auto-select the new fault
        SetSelectedFault(fault);
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
    
    private void SplitFormation()
    {
        if (_selectedFormation == null || _tempPoints.Count < 2) return;
        
        // Create a splitting line from the temp points
        var splitLine = _tempPoints.ToList();
        
        // Use GeologyGeometryUtils to split the formation
        var result = GeologyGeometryUtils.SplitFormation(_selectedFormation, splitLine);
        
        if (result.HasValue)
        {
            var (upperFormation, lowerFormation) = result.Value;
            
            // Create composite command to handle the split
            var compositeCmd = new CompositeCommand("Split Formation");
            
            // Remove original formation
            compositeCmd.AddCommand(new DelegateCommand(
                execute: () => _dataset.ProfileData.Formations.Remove(_selectedFormation),
                undo: () => _dataset.ProfileData.Formations.Add(_selectedFormation),
                description: $"Remove {_selectedFormation.Name}"
            ));
            
            // Add two new formations
            compositeCmd.AddCommand(new AddItemCommand<ProjectedFormation>(
                _dataset.ProfileData.Formations,
                upperFormation,
                upperFormation.Name
            ));
            
            compositeCmd.AddCommand(new AddItemCommand<ProjectedFormation>(
                _dataset.ProfileData.Formations,
                lowerFormation,
                lowerFormation.Name
            ));
            
            // Execute the composite command
            _viewer.UndoRedo.ExecuteCommand(compositeCmd);
            
            Logger.Log($"Split formation '{_selectedFormation.Name}' into two formations");
            _dataset.MarkAsModified();
            _selectedFormation = null;
        }
        else
        {
            Logger.LogWarning("Could not split formation - split line may not intersect formation boundaries");
        }
        
        _tempPoints.Clear();
        CurrentEditMode = EditMode.None;
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
        _tempPoints.Clear();
        _measurementPoints.Clear();
        _isDrawing = false;
        
        if (CurrentEditMode != EditMode.SelectFormation)
        {
            CurrentEditMode = EditMode.None;
        }
        
        Logger.Log("Cancelled current operation");
    }
    
    private void ClearTempPoints()
    {
        _tempPoints.Clear();
        _measurementPoints.Clear();
        _isDrawing = false;
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

        // Check formation vertices
        foreach (var formation in crossSection.Formations)
        {
            findInBoundary(formation.TopBoundary);
            findInBoundary(formation.BottomBoundary);
        }

        // Check fault vertices
        foreach (var fault in crossSection.Faults)
        {
            findInBoundary(fault.FaultTrace);
        }

        // Check topography points
        if (crossSection.Profile != null)
        {
            findInBoundary(crossSection.Profile.Points.Select(p => new Vector2(p.Distance, p.Elevation)));
        }

        return closestPoint;
    }
    
    private bool IsPointInFormation(Vector2 point, ProjectedFormation formation)
    {
        if (formation.TopBoundary.Count < 2 || formation.BottomBoundary.Count < 2)
            return false;
        
        // Check if point X is within formation bounds
        var minX = Math.Min(formation.TopBoundary.Min(p => p.X), formation.BottomBoundary.Min(p => p.X));
        var maxX = Math.Max(formation.TopBoundary.Max(p => p.X), formation.BottomBoundary.Max(p => p.X));
        
        if (point.X < minX || point.X > maxX)
            return false;
        
        // Interpolate Y values at the point's X position
        var topY = InterpolateY(formation.TopBoundary, point.X);
        var bottomY = InterpolateY(formation.BottomBoundary, point.X);
        
        // Check if point Y is between top and bottom boundaries
        return point.Y >= Math.Min(topY, bottomY) && point.Y <= Math.Max(topY, bottomY);
    }
    
    private float InterpolateY(List<Vector2> boundary, float x)
    {
        if (boundary.Count == 0) return 0f;
        if (boundary.Count == 1) return boundary[0].Y;

        // Sort boundary points by X coordinate
        var sortedBoundary = boundary.OrderBy(p => p.X).ToList();
        
        // If x is outside bounds, return edge values
        if (x <= sortedBoundary[0].X) return sortedBoundary[0].Y;
        if (x >= sortedBoundary[^1].X) return sortedBoundary[^1].Y;

        // Find the two points that x falls between
        for (int i = 0; i < sortedBoundary.Count - 1; i++)
        {
            if (x >= sortedBoundary[i].X && x <= sortedBoundary[i + 1].X)
            {
                var p1 = sortedBoundary[i];
                var p2 = sortedBoundary[i + 1];
                
                // Avoid division by zero
                if (Math.Abs(p2.X - p1.X) < 1e-6) return p1.Y;
                
                // Linear interpolation
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
    
    /// <summary>
    /// Render overlay graphics for the current tool
    /// </summary>
    public void RenderOverlay(ImDrawListPtr drawList, Func<Vector2, Vector2> worldToScreen)
    {
        var yellow = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 1));
        var red = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 1));
        var green = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 1, 0, 1));
        var cyan = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 1, 1, 0.8f));
        var white = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1));

        // Draw temp points for formation/fault creation
        if (_tempPoints.Count > 0)
        {
            // Draw lines between points
            for (var i = 0; i < _tempPoints.Count - 1; i++)
            {
                drawList.AddLine(worldToScreen(_tempPoints[i]), worldToScreen(_tempPoints[i + 1]), yellow, 2.0f);
            }
            
            // Draw preview line to cursor if drawing
            if (_isDrawing && _lastMouseWorldPos != Vector2.Zero && _tempPoints.Count > 0)
            {
                var lastPoint = _tempPoints[^1];
                var cursorPos = _snapPoint ?? _lastMouseWorldPos;
                drawList.AddLine(worldToScreen(lastPoint), worldToScreen(cursorPos), 
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 0.5f)), 2.0f);
            }
            
            // Draw points
            foreach (var p in _tempPoints)
            {
                drawList.AddCircleFilled(worldToScreen(p), 5f, green);
                drawList.AddCircle(worldToScreen(p), 5f, white, 12, 1.0f);
            }
            
            // Draw thickness preview for formations
            if (CurrentEditMode == EditMode.DrawFormation && _tempPoints.Count >= 2 && !_drawFormationAsPolygon)
            {
                for (var i = 0; i < _tempPoints.Count - 1; i++)
                {
                    var bottomPoint1 = new Vector2(_tempPoints[i].X, _tempPoints[i].Y - _newFormationThickness);
                    var bottomPoint2 = new Vector2(_tempPoints[i + 1].X, _tempPoints[i + 1].Y - _newFormationThickness);
                    drawList.AddLine(worldToScreen(bottomPoint1), worldToScreen(bottomPoint2), 
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 0.3f)), 1.0f);
                }
            }
        }

        // Draw measurement points and lines
        if (_measurementPoints.Count > 0)
        {
            for (var i = 0; i < _measurementPoints.Count - 1; i++)
            {
                drawList.AddLine(worldToScreen(_measurementPoints[i]), 
                    worldToScreen(_measurementPoints[i + 1]), yellow, 2.0f);
                
                // Draw distance text at midpoint
                var midPoint = (_measurementPoints[i] + _measurementPoints[i + 1]) / 2f;
                var screenMid = worldToScreen(midPoint);
                var segmentDist = Vector2.Distance(_measurementPoints[i], _measurementPoints[i + 1]);
                drawList.AddText(screenMid, white, $"{segmentDist:F1}m");
            }
            
            foreach (var p in _measurementPoints)
            {
                drawList.AddCircleFilled(worldToScreen(p), 5f, red);
                drawList.AddCircle(worldToScreen(p), 5f, white, 12, 1.0f);
            }
            
            // Draw total distance if multiple segments
            if (_measurementPoints.Count > 2)
            {
                var totalPos = worldToScreen(_measurementPoints[^1]) + new Vector2(10, -10);
                drawList.AddText(totalPos, yellow, $"Total: {_measuredDistance:F1}m");
            }
        }

        // Draw snap indicator
        if (_snapPoint.HasValue && _enableSnapping &&
            (CurrentEditMode == EditMode.DrawFormation || 
             CurrentEditMode == EditMode.DrawFault ||
             CurrentEditMode == EditMode.MeasureDistance))
        {
            var snapScreen = worldToScreen(_snapPoint.Value);
            drawList.AddCircle(snapScreen, 8f, cyan, 16, 2.0f);
            drawList.AddCircleFilled(snapScreen, 3f, cyan);
        }
        
        // Draw cursor crosshair when in drawing mode
        if (_isDrawing && _lastMouseWorldPos != Vector2.Zero)
        {
            var cursorScreen = worldToScreen(_snapPoint ?? _lastMouseWorldPos);
            var crosshairColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 0.5f));
            var size = 10f;
            drawList.AddLine(cursorScreen - new Vector2(size, 0), cursorScreen + new Vector2(size, 0), crosshairColor);
            drawList.AddLine(cursorScreen - new Vector2(0, size), cursorScreen + new Vector2(0, size), crosshairColor);
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