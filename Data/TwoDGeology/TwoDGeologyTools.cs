// GeoscientistToolkit/UI/GIS/TwoDGeologyTools.cs

using System.Numerics;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Util;
using ImGuiNET;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.ProfileGenerator;

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
        MergeFormations,
        EditTopography
    }

    public EditMode CurrentEditMode { get; set; } = EditMode.None;

    // Tool state
    private ProjectedFormation _selectedFormation;
    private ProjectedFault _selectedFault;
    private readonly List<Vector2> _tempPoints = new();
    private bool _isDrawing = false;
    
    // Selection change events
    public event Action<ProjectedFormation> FormationSelected;
    public event Action<ProjectedFault> FaultSelected;
    public event Action SelectionCleared;

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
    
    // Topography editing state
    private int _selectedTopographyPointIndex = -1;
    private bool _isDraggingTopographyPoint = false;
    
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
        
        if (ImGui.Button("Edit Topography (T)", buttonSize))
        {
            CurrentEditMode = EditMode.EditTopography;
            ClearTempPoints();
            _selectedTopographyPointIndex = -1;
        }
        
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Click and drag topography points to modify the surface profile");
        
        if (CurrentEditMode == EditMode.EditTopography)
        {
            ImGui.Indent();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "Click on a topography point to select it");
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "Drag to move the point vertically");
            if (_selectedTopographyPointIndex >= 0)
            {
                ImGui.Text($"Selected Point: {_selectedTopographyPointIndex}");
            }
            ImGui.Unindent();
        }
        
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
        
        ImGui.Text("Geological Presets:");
        if (ImGui.Button("Load Preset...", buttonSize))
        {
            ImGui.OpenPopup("GeologicalPresets");
        }
        
        if (ImGui.BeginPopup("GeologicalPresets"))
        {
            ImGui.Text("Select a geological scenario:");
            ImGui.Separator();
            
            foreach (var scenario in Enum.GetValues<GeologicalLayerPresets.PresetScenario>())
            {
                if (ImGui.MenuItem(GeologicalLayerPresets.GetPresetName(scenario)))
                {
                    ApplyGeologicalPreset(scenario);
                    ImGui.CloseCurrentPopup();
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text(GeologicalLayerPresets.GetPresetDescription(scenario));
                    ImGui.EndTooltip();
                }
            }
            
            ImGui.EndPopup();
        }
        
        ImGui.Separator();
        
        ImGui.Text("Structural Restoration:");
        if (ImGui.Button("Restore Section...", buttonSize))
        {
            ImGui.OpenPopup("StructuralRestoration");
        }
        
        if (ImGui.BeginPopup("StructuralRestoration"))
        {
            ImGui.Text("Structural Restoration & Forward Modeling");
            ImGui.Separator();
            
            ImGui.TextWrapped("Unfold and unfault geological structures to their pre-deformation state, or forward model deformation.");
            ImGui.Separator();
            
            if (ImGui.MenuItem("Restore (Unfold/Unfault)"))
            {
                PerformRestoration(100f);
                ImGui.CloseCurrentPopup();
            }
            
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Restore the section to its undeformed state (100% restoration)");
            
            if (ImGui.MenuItem("Partial Restore (50%)"))
            {
                PerformRestoration(50f);
                ImGui.CloseCurrentPopup();
            }
            
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Partially restore the section (50% of total deformation removed)");
            
            if (ImGui.MenuItem("Forward Model (Re-deform)"))
            {
                PerformForwardModeling(100f);
                ImGui.CloseCurrentPopup();
            }
            
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Apply deformation to an undeformed or partially restored section");
            
            ImGui.Separator();
            
            if (ImGui.MenuItem("Create Flat Reference"))
            {
                CreateFlatReference();
                ImGui.CloseCurrentPopup();
            }
            
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Create a completely undeformed reference state");
            
            ImGui.Separator();
            
            if (ImGui.MenuItem("Clear Restoration Overlay"))
            {
                _dataset.ClearRestorationData();
                Logger.Log("Cleared restoration overlay");
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.EndPopup();
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
        ImGui.TextDisabled("T - Edit topography");
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
            EditMode.EditTopography => _selectedTopographyPointIndex >= 0 ? $"Editing Topography (Point {_selectedTopographyPointIndex})" : "Edit Topography",
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
                
            case EditMode.EditTopography:
                HandleTopographyEditing(worldPos, leftClick, isDragging);
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
            if (ImGui.IsKeyPressed(ImGuiKey.T))
            {
                CurrentEditMode = EditMode.EditTopography;
                ClearTempPoints();
                _selectedTopographyPointIndex = -1;
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
        _selectedTopographyPointIndex = -1;
        _isDraggingTopographyPoint = false;
        
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
        
        // Draw selected topography point when in edit topography mode
        if (CurrentEditMode == EditMode.EditTopography && _selectedTopographyPointIndex >= 0)
        {
            var profile = _dataset.ProfileData?.Profile;
            if (profile != null && _selectedTopographyPointIndex < profile.Points.Count)
            {
                var point = profile.Points[_selectedTopographyPointIndex];
                var pointPos = new Vector2(point.Distance, point.Elevation);
                var screenPos = worldToScreen(pointPos);
                
                // Draw large highlight circle
                drawList.AddCircle(screenPos, 10f, yellow, 16, 3.0f);
                drawList.AddCircleFilled(screenPos, 5f, yellow);
                
                // Draw elevation guide line
                var leftScreen = worldToScreen(new Vector2(0, point.Elevation));
                var rightScreen = worldToScreen(new Vector2(profile.TotalDistance, point.Elevation));
                var guideColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 0.3f));
                drawList.AddLine(leftScreen, rightScreen, guideColor, 1.0f);
                
                // Draw elevation text
                var textPos = screenPos + new Vector2(15, -15);
                drawList.AddText(textPos, white, $"Elev: {point.Elevation:F1}m");
            }
        }
    }
    
    private void HandleTopographyEditing(Vector2 worldPos, bool leftClick, bool isDragging)
    {
        var profile = _dataset.ProfileData?.Profile;
        if (profile == null || profile.Points.Count == 0) return;
        
        // If clicking, find nearest topography point
        if (leftClick && !_isDraggingTopographyPoint)
        {
            _selectedTopographyPointIndex = FindNearestTopographyPoint(worldPos, profile);
            if (_selectedTopographyPointIndex >= 0)
            {
                _isDraggingTopographyPoint = true;
                Logger.Log($"Selected topography point {_selectedTopographyPointIndex}");
            }
        }
        
        // If dragging a selected point, update its elevation
        if (_isDraggingTopographyPoint && _selectedTopographyPointIndex >= 0 && _selectedTopographyPointIndex < profile.Points.Count)
        {
            if (isDragging)
            {
                var point = profile.Points[_selectedTopographyPointIndex];
                // Only update Y (elevation), keep X (distance) the same
                point.Elevation = worldPos.Y;
                point.Position = new Vector2(point.Distance, worldPos.Y);
                profile.Points[_selectedTopographyPointIndex] = point;
                
                // Update profile elevation range
                profile.MinElevation = profile.Points.Min(p => p.Elevation);
                profile.MaxElevation = profile.Points.Max(p => p.Elevation);
                
                _dataset.MarkAsModified();
            }
            else
            {
                // Stop dragging when mouse released
                _isDraggingTopographyPoint = false;
                Logger.Log($"Updated topography point {_selectedTopographyPointIndex} to elevation {profile.Points[_selectedTopographyPointIndex].Elevation:F2}m");
            }
        }
    }
    
    private int FindNearestTopographyPoint(Vector2 worldPos, GeologicalMapping.ProfileGenerator.TopographicProfile profile)
    {
        const float threshold = 100f; // World units
        float minDist = threshold;
        int nearestIndex = -1;
        
        for (int i = 0; i < profile.Points.Count; i++)
        {
            var point = profile.Points[i];
            var pointPos = new Vector2(point.Distance, point.Elevation);
            var dist = Vector2.Distance(worldPos, pointPos);
            if (dist < minDist)
            {
                minDist = dist;
                nearestIndex = i;
            }
        }
        
        return nearestIndex;
    }
    
    public ProjectedFormation GetSelectedFormation() => _selectedFormation;
    
    public ProjectedFault GetSelectedFault() => _selectedFault;
    
    public void SetSelectedFormation(ProjectedFormation formation)
    {
        _selectedFormation = formation;
        _selectedFault = null;
        FormationSelected?.Invoke(formation);
    }
    
    public void SetSelectedFault(ProjectedFault fault)
    {
        _selectedFault = fault;
        _selectedFormation = null;
        FaultSelected?.Invoke(fault);
    }
    
    public void ClearSelection()
    {
        _selectedFormation = null;
        _selectedFault = null;
        SelectionCleared?.Invoke();
    }
    
    #region Geological Presets and Restoration
    
    private void ApplyGeologicalPreset(GeologicalLayerPresets.PresetScenario scenario)
    {
        try
        {
            // Create the preset cross-section
            var presetSection = GeologicalLayerPresets.CreatePreset(
                scenario, 
                _dataset.ProfileData.Profile.TotalDistance,
                _dataset.ProfileData.Profile.MinElevation
            );
            
            // Replace the current section data
            _dataset.ProfileData.Profile = presetSection.Profile;
            _dataset.ProfileData.Formations.Clear();
            foreach (var formation in presetSection.Formations)
            {
                _dataset.ProfileData.Formations.Add(formation);
            }
            
            _dataset.ProfileData.Faults.Clear();
            foreach (var fault in presetSection.Faults)
            {
                _dataset.ProfileData.Faults.Add(fault);
            }
            
            _dataset.MarkAsModified();
            
            Logger.Log($"Applied geological preset: {GeologicalLayerPresets.GetPresetName(scenario)}");
            Logger.Log($"Created {presetSection.Formations.Count} formations and {presetSection.Faults.Count} faults");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to apply geological preset: {ex.Message}");
        }
    }
    
    private void PerformRestoration(float percentage)
    {
        try
        {
            if (_dataset.ProfileData == null)
            {
                Logger.LogError("No cross-section data available for restoration");
                return;
            }
            
            // Create restoration object
            var restoration = new StructuralRestoration(_dataset.ProfileData);
            
            // Perform restoration
            restoration.Restore(percentage);
            
            // Set the restored section as overlay
            _dataset.SetRestorationData(restoration.RestoredSection);
            
            Logger.Log($"Performed structural restoration at {percentage}%");
            Logger.Log("Restored section is displayed as overlay (semi-transparent)");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to perform restoration: {ex.Message}");
        }
    }
    
    private void PerformForwardModeling(float percentage)
    {
        try
        {
            if (_dataset.ProfileData == null)
            {
                Logger.LogError("No cross-section data available for forward modeling");
                return;
            }
            
            // Create restoration object
            var restoration = new StructuralRestoration(_dataset.ProfileData);
            
            // Perform forward modeling (deformation)
            restoration.Deform(percentage);
            
            // Set the deformed section as overlay
            _dataset.SetRestorationData(restoration.RestoredSection);
            
            Logger.Log($"Performed forward modeling at {percentage}%");
            Logger.Log("Deformed section is displayed as overlay (semi-transparent)");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to perform forward modeling: {ex.Message}");
        }
    }
    
    private void CreateFlatReference()
    {
        try
        {
            if (_dataset.ProfileData == null)
            {
                Logger.LogError("No cross-section data available");
                return;
            }
            
            // Create restoration object
            var restoration = new StructuralRestoration(_dataset.ProfileData);
            
            // Create flat reference
            restoration.CreateFlatReference();
            
            // Set the flat reference as overlay
            _dataset.SetRestorationData(restoration.RestoredSection);
            
            Logger.Log("Created flat reference state");
            Logger.Log("Completely undeformed section is displayed as overlay");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to create flat reference: {ex.Message}");
        }
    }
    
    #endregion
}