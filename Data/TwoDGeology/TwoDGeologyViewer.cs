// GeoscientistToolkit/UI/GIS/TwoDGeologyViewer.cs

using System.Numerics;
using GeoscientistToolkit.Data.TwoDGeology;
using GeoscientistToolkit.Business.GIS;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Veldrid;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.CrossSectionGenerator;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.ProfileGenerator;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
/// Viewer for 2D geological cross-sections with editing capabilities
/// </summary>
public class TwoDGeologyViewer : IDisposable
{
    private readonly TwoDGeologyDataset _dataset;
    private CrossSection _crossSection;
    private CrossSection _restorationData; // For displaying restoration results
    
    // Undo/Redo system
    public UndoRedoManager UndoRedo { get; private set; } = new UndoRedoManager();
    
    // View state
    private Vector2 _panOffset = Vector2.Zero;
    private float _zoom = 1.0f;
    private float _verticalExaggeration = 2.0f;
    
    // Selection state
    private ProjectedFormation _selectedFormation;
    private ProjectedFault _selectedFault;
    private int _selectedVertexIndex = -1;
    private bool _isDraggingVertex = false;
    private Vector2 _dragStartPosition;
    
    // Mouse state
    private Vector2 _lastMousePos;
    private bool _isPanning = false;
    
    // Display options
    private bool _showFormations = true;
    private bool _showFaults = true;
    private bool _showTopography = true;
    private bool _showGrid = true;
    private bool _showRestorationOverlay = false;
    
    // Colors
    private readonly Vector4 _backgroundColor = new Vector4(0.1f, 0.1f, 0.12f, 1.0f);
    private readonly Vector4 _gridColor = new Vector4(0.4f, 0.4f, 0.4f, 0.5f);
    private readonly Vector4 _topographyColor = new Vector4(0.3f, 0.5f, 0.2f, 1.0f);
    private readonly Vector4 _faultColor = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
    private readonly Vector4 _selectionColor = new Vector4(1.0f, 1.0f, 0.0f, 1.0f);
    
    public TwoDGeologyViewer(TwoDGeologyDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _dataset.RegisterViewer(this);
        
        // Load the cross section data
        _dataset.Load();
        _crossSection = _dataset.ProfileData;
        
        if (_crossSection != null)
        {
            _verticalExaggeration = _crossSection.VerticalExaggeration;
        }
        
        Logger.Log($"TwoDGeologyViewer initialized for '{dataset.Name}'");
    }
    
    /// <summary>
    /// Set restoration data to display as overlay
    /// </summary>
    public void SetRestorationData(CrossSection restorationData)
    {
        _restorationData = restorationData;
        _showRestorationOverlay = restorationData != null;
    }
    
    /// <summary>
    /// Clear restoration overlay
    /// </summary>
    public void ClearRestorationData()
    {
        _restorationData = null;
        _showRestorationOverlay = false;
    }
    
    /// <summary>
    /// Main render method
    /// </summary>
    public void Render()
    {
        if (_crossSection == null)
        {
            ImGui.Text("No cross-section data loaded.");
            return;
        }
        
        RenderToolbar();
        ImGui.Separator();
        RenderViewport();
        ImGui.Separator();
        RenderPropertiesPanel();
    }
    
    private void RenderToolbar()
    {
        if (ImGui.Button("Undo") && UndoRedo.CanUndo)
        {
            UndoRedo.Undo();
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Redo") && UndoRedo.CanRedo)
        {
            UndoRedo.Redo();
        }
        
        ImGui.SameLine();
        ImGui.Separator();
        
        ImGui.SameLine();
        ImGui.Checkbox("Formations", ref _showFormations);
        
        ImGui.SameLine();
        ImGui.Checkbox("Faults", ref _showFaults);
        
        ImGui.SameLine();
        ImGui.Checkbox("Topography", ref _showTopography);
        
        ImGui.SameLine();
        ImGui.Checkbox("Grid", ref _showGrid);
        
        if (_restorationData != null)
        {
            ImGui.SameLine();
            ImGui.Checkbox("Show Restoration", ref _showRestorationOverlay);
        }
        
        ImGui.SameLine();
        ImGui.Text($"Zoom: {_zoom:F2}x");
        
        ImGui.SameLine();
        ImGui.PushItemWidth(120f); // Set a fixed width for the slider
        if (ImGui.SliderFloat("V.E.", ref _verticalExaggeration, 0.5f, 10.0f))
        {
            _crossSection.VerticalExaggeration = _verticalExaggeration;
        }
        ImGui.PopItemWidth();
    }
    
    private void RenderViewport()
    {
        var availSize = ImGui.GetContentRegionAvail();
        var drawList = ImGui.GetWindowDrawList();
        var screenPos = ImGui.GetCursorScreenPos();
        
        // Background
        drawList.AddRectFilled(screenPos, screenPos + availSize, ImGui.ColorConvertFloat4ToU32(_backgroundColor));
        
        // Handle mouse input
        HandleMouseInput(screenPos, availSize);
        
        // Render grid
        if (_showGrid)
        {
            RenderGrid(drawList, screenPos, availSize);
        }
        
        // Render topography
        if (_showTopography && _crossSection.Profile != null)
        {
            RenderTopography(drawList, screenPos, availSize);
        }
        
        // Render formations
        if (_showFormations)
        {
            RenderFormations(drawList, screenPos, availSize);
        }
        
        // Render faults
        if (_showFaults)
        {
            RenderFaults(drawList, screenPos, availSize);
        }
        
        // Render restoration overlay
        if (_showRestorationOverlay && _restorationData != null)
        {
            RenderRestorationOverlay(drawList, screenPos, availSize);
        }
        
        // Render selection
        RenderSelection(drawList, screenPos, availSize);
        
        // Dummy to capture input
        ImGui.InvisibleButton("viewport", availSize);
    }
    
    private void HandleMouseInput(Vector2 screenPos, Vector2 availSize)
    {
        var io = ImGui.GetIO();
        var mousePos = io.MousePos;
        var localMousePos = mousePos - screenPos;
        
        // Pan with middle mouse or right mouse
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right))
        {
            if (!_isPanning)
            {
                _isPanning = true;
                _lastMousePos = localMousePos;
            }
            else
            {
                var delta = localMousePos - _lastMousePos;
                _panOffset += delta / _zoom;
                _lastMousePos = localMousePos;
            }
        }
        else
        {
            _isPanning = false;
        }
        
        // Zoom with mouse wheel
        if (ImGui.IsWindowHovered() && Math.Abs(io.MouseWheel) > 0.001f)
        {
            var oldZoom = _zoom;
            _zoom *= MathF.Pow(1.1f, io.MouseWheel);
            _zoom = Math.Clamp(_zoom, 0.1f, 100.0f);
            
            // Zoom towards mouse position
            var worldPos = ScreenToWorld(localMousePos, screenPos, availSize);
            var newScreenPos = WorldToScreen(worldPos, screenPos, availSize);
            var screenDelta = localMousePos - newScreenPos;
            _panOffset += screenDelta / _zoom;
        }
        
        // Vertex dragging
        if (_isDraggingVertex && _selectedFormation != null && _selectedVertexIndex >= 0)
        {
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var worldPos = ScreenToWorld(localMousePos, screenPos, availSize);
                
                // Update vertex position (simplified - you'd want to determine which boundary)
                // This is just a placeholder - implement proper vertex selection logic
            }
            else
            {
                _isDraggingVertex = false;
                _selectedVertexIndex = -1;
            }
        }
    }
    
    private void RenderGrid(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null) return;
        
        var gridColor = ImGui.ColorConvertFloat4ToU32(_gridColor);
        var totalDistance = _crossSection.Profile.TotalDistance;
        var minElev = _crossSection.Profile.MinElevation;
        var maxElev = _crossSection.Profile.MaxElevation;
        
        // Vertical lines (every 1000m)
        for (float x = 0; x <= totalDistance; x += 1000f)
        {
            var worldPos1 = new Vector2(x, minElev);
            var worldPos2 = new Vector2(x, maxElev);
            var screenPos1 = WorldToScreen(worldPos1, screenPos, availSize);
            var screenPos2 = WorldToScreen(worldPos2, screenPos, availSize);
            
            drawList.AddLine(screenPos1, screenPos2, gridColor, 1.0f);
        }
        
        // Horizontal lines (every 500m elevation)
        var elevStep = 500f;
        for (float y = MathF.Floor(minElev / elevStep) * elevStep; y <= maxElev; y += elevStep)
        {
            var worldPos1 = new Vector2(0, y);
            var worldPos2 = new Vector2(totalDistance, y);
            var screenPos1 = WorldToScreen(worldPos1, screenPos, availSize);
            var screenPos2 = WorldToScreen(worldPos2, screenPos, availSize);
            
            drawList.AddLine(screenPos1, screenPos2, gridColor, 1.0f);
        }
    }
    
    private void RenderTopography(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        var profile = _crossSection.Profile;
        if (profile.Points.Count < 2) return;
        
        var color = ImGui.ColorConvertFloat4ToU32(_topographyColor);
        
        for (int i = 0; i < profile.Points.Count - 1; i++)
        {
            var p1 = new Vector2(profile.Points[i].Distance, profile.Points[i].Elevation);
            var p2 = new Vector2(profile.Points[i + 1].Distance, profile.Points[i + 1].Elevation);
            
            var sp1 = WorldToScreen(p1, screenPos, availSize);
            var sp2 = WorldToScreen(p2, screenPos, availSize);
            
            drawList.AddLine(sp1, sp2, color, 2.0f);
        }
    }
    
    private void RenderFormations(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        foreach (var formation in _crossSection.Formations)
        {
            RenderFormation(drawList, screenPos, availSize, formation, false);
        }
    }
    
    private void RenderFormation(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize, 
        ProjectedFormation formation, bool isRestoration)
    {
        if (formation.TopBoundary.Count < 2 || formation.BottomBoundary.Count < 2)
            return;
        
        var color = ImGui.ColorConvertFloat4ToU32(isRestoration 
            ? new Vector4(formation.Color.X, formation.Color.Y, formation.Color.Z, 0.5f)
            : formation.Color);
        
        // Create polygon from boundaries
        var polygon = new List<Vector2>();
        
        // Add top boundary
        foreach (var point in formation.TopBoundary)
        {
            polygon.Add(WorldToScreen(point, screenPos, availSize));
        }
        
        // Add bottom boundary in reverse
        for (int i = formation.BottomBoundary.Count - 1; i >= 0; i--)
        {
            polygon.Add(WorldToScreen(formation.BottomBoundary[i], screenPos, availSize));
        }
        
        // Draw filled polygon
        if (polygon.Count >= 3)
        {
            var vector2 = polygon[0];
            drawList.AddConvexPolyFilled(ref vector2, polygon.Count, color);
        }
        
        // Draw boundaries
        var boundaryColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1));
        for (int i = 0; i < formation.TopBoundary.Count - 1; i++)
        {
            var p1 = WorldToScreen(formation.TopBoundary[i], screenPos, availSize);
            var p2 = WorldToScreen(formation.TopBoundary[i + 1], screenPos, availSize);
            drawList.AddLine(p1, p2, boundaryColor, 1.0f);
        }
        
        for (int i = 0; i < formation.BottomBoundary.Count - 1; i++)
        {
            var p1 = WorldToScreen(formation.BottomBoundary[i], screenPos, availSize);
            var p2 = WorldToScreen(formation.BottomBoundary[i + 1], screenPos, availSize);
            drawList.AddLine(p1, p2, boundaryColor, 1.0f);
        }
    }
    
    private void RenderFaults(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        foreach (var fault in _crossSection.Faults)
        {
            RenderFault(drawList, screenPos, availSize, fault, false);
        }
    }
    
    private void RenderFault(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize, 
        ProjectedFault fault, bool isRestoration)
    {
        if (fault.FaultTrace.Count < 2) return;
        
        var color = ImGui.ColorConvertFloat4ToU32(isRestoration 
            ? new Vector4(_faultColor.X, _faultColor.Y, _faultColor.Z, 0.5f)
            : _faultColor);
        
        for (int i = 0; i < fault.FaultTrace.Count - 1; i++)
        {
            var p1 = WorldToScreen(fault.FaultTrace[i], screenPos, availSize);
            var p2 = WorldToScreen(fault.FaultTrace[i + 1], screenPos, availSize);
            drawList.AddLine(p1, p2, color, 2.0f);
        }
    }
    
    private void RenderRestorationOverlay(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        // Render restoration formations
        foreach (var formation in _restorationData.Formations)
        {
            RenderFormation(drawList, screenPos, availSize, formation, true);
        }
        
        // Render restoration faults
        foreach (var fault in _restorationData.Faults)
        {
            RenderFault(drawList, screenPos, availSize, fault, true);
        }
    }
    
    private void RenderSelection(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        var selectionColor = ImGui.ColorConvertFloat4ToU32(_selectionColor);
        
        if (_selectedFormation != null)
        {
            // Highlight selected formation boundaries
            for (int i = 0; i < _selectedFormation.TopBoundary.Count - 1; i++)
            {
                var p1 = WorldToScreen(_selectedFormation.TopBoundary[i], screenPos, availSize);
                var p2 = WorldToScreen(_selectedFormation.TopBoundary[i + 1], screenPos, availSize);
                drawList.AddLine(p1, p2, selectionColor, 3.0f);
            }
        }
        
        if (_selectedFault != null)
        {
            // Highlight selected fault
            for (int i = 0; i < _selectedFault.FaultTrace.Count - 1; i++)
            {
                var p1 = WorldToScreen(_selectedFault.FaultTrace[i], screenPos, availSize);
                var p2 = WorldToScreen(_selectedFault.FaultTrace[i + 1], screenPos, availSize);
                drawList.AddLine(p1, p2, selectionColor, 3.0f);
            }
        }
    }
    
    private void RenderPropertiesPanel()
    {
        ImGui.Text("Properties");
        
        if (_selectedFormation != null)
        {
            ImGui.Text($"Formation: {_selectedFormation.Name}");
            
            var color = _selectedFormation.Color;
            if (ImGui.ColorEdit4("Color", ref color))
            {
                var oldColor = _selectedFormation.Color;
                var cmd = new ChangeFormationColorCommand(_selectedFormation, oldColor, color);
                UndoRedo.ExecuteCommand(cmd);
            }
            
            var name = _selectedFormation.Name ?? "";
            if (ImGui.InputText("Name", ref name, 256))
            {
                var oldName = _selectedFormation.Name;
                var cmd = new RenameFormationCommand(_selectedFormation, oldName, name);
                UndoRedo.ExecuteCommand(cmd);
            }
            
            if (_selectedFormation.FoldStyle.HasValue)
            {
                var currentStyle = _selectedFormation.FoldStyle.Value.ToString();
                ImGui.Text($"Fold Style: {currentStyle}");
            }
        }
        else if (_selectedFault != null)
        {
            ImGui.Text($"Fault: {_selectedFault.Type}");
            ImGui.Text($"Dip: {_selectedFault.Dip:F1}°");
            ImGui.Text($"Dip Direction: {_selectedFault.DipDirection}");
            
            if (_selectedFault.Displacement.HasValue)
            {
                ImGui.Text($"Displacement: {_selectedFault.Displacement.Value:F1} m");
            }
        }
        else
        {
            ImGui.Text("No selection");
        }
    }
    
    private Vector2 WorldToScreen(Vector2 worldPos, Vector2 screenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null) return screenPos;
        
        var totalDistance = _crossSection.Profile.TotalDistance;
        var minElev = _crossSection.Profile.MinElevation;
        var maxElev = _crossSection.Profile.MaxElevation;
        var elevRange = maxElev - minElev;
        
        // Normalize to 0-1
        var normX = worldPos.X / totalDistance;
        var normY = (worldPos.Y - minElev) / elevRange;
        
        // Apply vertical exaggeration
        normY *= _verticalExaggeration;
        
        // Apply zoom and pan
        var viewX = (normX + _panOffset.X / totalDistance) * _zoom;
        var viewY = (normY + _panOffset.Y / elevRange) * _zoom;
        
        // Convert to screen space (flip Y)
        var screenX = viewX * availSize.X;
        var screenY = availSize.Y - (viewY * availSize.Y);
        
        return screenPos + new Vector2(screenX, screenY);
    }
    
    private Vector2 ScreenToWorld(Vector2 localScreenPos, Vector2 screenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null) return Vector2.Zero;
        
        var totalDistance = _crossSection.Profile.TotalDistance;
        var minElev = _crossSection.Profile.MinElevation;
        var maxElev = _crossSection.Profile.MaxElevation;
        var elevRange = maxElev - minElev;
        
        // Normalized screen position (0-1)
        var normX = localScreenPos.X / availSize.X;
        var normY = 1.0f - (localScreenPos.Y / availSize.Y); // Flip Y
        
        // Remove zoom and pan
        var viewX = normX / _zoom - _panOffset.X / totalDistance;
        var viewY = normY / _zoom - _panOffset.Y / elevRange;
        
        // Remove vertical exaggeration
        viewY /= _verticalExaggeration;
        
        // Convert to world coordinates
        var worldX = viewX * totalDistance;
        var worldY = viewY * elevRange + minElev;
        
        return new Vector2(worldX, worldY);
    }
    
    public void Dispose()
    {
        // Cleanup if needed
    }
    
    #region Command Implementations
    
    /// <summary>
    /// Command for moving a vertex in a boundary
    /// </summary>
    private class MoveVertexCommand : ICommand
    {
        private readonly List<Vector2> _boundary;
        private readonly int _index;
        private readonly Vector2 _oldPosition;
        private readonly Vector2 _newPosition;
        
        public MoveVertexCommand(List<Vector2> boundary, int index, Vector2 oldPosition, Vector2 newPosition)
        {
            _boundary = boundary;
            _index = index;
            _oldPosition = oldPosition;
            _newPosition = newPosition;
        }
        
        public void Execute()
        {
            if (_index >= 0 && _index < _boundary.Count)
                _boundary[_index] = _newPosition;
        }
        
        public void Undo()
        {
            if (_index >= 0 && _index < _boundary.Count)
                _boundary[_index] = _oldPosition;
        }
        
        public string Description => $"Move Vertex";
    }
    
    /// <summary>
    /// Command for adding a feature (formation or fault)
    /// </summary>
    private class AddFeatureCommand : ICommand
    {
        private readonly CrossSection _crossSection;
        private readonly object _feature;
        private readonly bool _isFormation;
        
        public AddFeatureCommand(CrossSection crossSection, ProjectedFormation formation)
        {
            _crossSection = crossSection;
            _feature = formation;
            _isFormation = true;
        }
        
        public AddFeatureCommand(CrossSection crossSection, ProjectedFault fault)
        {
            _crossSection = crossSection;
            _feature = fault;
            _isFormation = false;
        }
        
        public void Execute()
        {
            if (_isFormation)
                _crossSection.Formations.Add((ProjectedFormation)_feature);
            else
                _crossSection.Faults.Add((ProjectedFault)_feature);
        }
        
        public void Undo()
        {
            if (_isFormation)
                _crossSection.Formations.Remove((ProjectedFormation)_feature);
            else
                _crossSection.Faults.Remove((ProjectedFault)_feature);
        }
        
        public string Description => _isFormation 
            ? $"Add Formation '{((ProjectedFormation)_feature).Name}'"
            : $"Add Fault ({((ProjectedFault)_feature).Type})";
    }
    
    /// <summary>
    /// Command for adding a formation to the cross section
    /// </summary>
    private class AddFormationCommand : ICommand
    {
        private readonly CrossSection _crossSection;
        private readonly ProjectedFormation _formation;
        
        public AddFormationCommand(CrossSection crossSection, ProjectedFormation formation)
        {
            _crossSection = crossSection;
            _formation = formation;
        }
        
        public void Execute()
        {
            _crossSection.Formations.Add(_formation);
        }
        
        public void Undo()
        {
            _crossSection.Formations.Remove(_formation);
        }
        
        public string Description => $"Add Formation '{_formation.Name}'";
    }
    
    /// <summary>
    /// Command for removing a formation from the cross section
    /// </summary>
    private class RemoveFormationCommand : ICommand
    {
        private readonly CrossSection _crossSection;
        private readonly ProjectedFormation _formation;
        private readonly int _index;
        
        public RemoveFormationCommand(CrossSection crossSection, ProjectedFormation formation)
        {
            _crossSection = crossSection;
            _formation = formation;
            _index = crossSection.Formations.IndexOf(formation);
        }
        
        public void Execute()
        {
            _crossSection.Formations.Remove(_formation);
        }
        
        public void Undo()
        {
            if (_index >= 0 && _index <= _crossSection.Formations.Count)
                _crossSection.Formations.Insert(_index, _formation);
            else
                _crossSection.Formations.Add(_formation);
        }
        
        public string Description => $"Remove Formation '{_formation.Name}'";
    }
    
    /// <summary>
    /// Command for adding a fault to the cross section
    /// </summary>
    private class AddFaultCommand : ICommand
    {
        private readonly CrossSection _crossSection;
        private readonly ProjectedFault _fault;
        
        public AddFaultCommand(CrossSection crossSection, ProjectedFault fault)
        {
            _crossSection = crossSection;
            _fault = fault;
        }
        
        public void Execute()
        {
            _crossSection.Faults.Add(_fault);
        }
        
        public void Undo()
        {
            _crossSection.Faults.Remove(_fault);
        }
        
        public string Description => $"Add Fault ({_fault.Type})";
    }
    
    /// <summary>
    /// Command for removing a fault from the cross section
    /// </summary>
    private class RemoveFaultCommand : ICommand
    {
        private readonly CrossSection _crossSection;
        private readonly ProjectedFault _fault;
        private readonly int _index;
        
        public RemoveFaultCommand(CrossSection crossSection, ProjectedFault fault)
        {
            _crossSection = crossSection;
            _fault = fault;
            _index = crossSection.Faults.IndexOf(fault);
        }
        
        public void Execute()
        {
            _crossSection.Faults.Remove(_fault);
        }
        
        public void Undo()
        {
            if (_index >= 0 && _index <= _crossSection.Faults.Count)
                _crossSection.Faults.Insert(_index, _fault);
            else
                _crossSection.Faults.Add(_fault);
        }
        
        public string Description => $"Remove Fault ({_fault.Type})";
    }
    
    /// <summary>
    /// Command for modifying formation boundaries
    /// </summary>
    private class ModifyFormationBoundaryCommand : ICommand
    {
        private readonly ProjectedFormation _formation;
        private readonly List<Vector2> _oldTopBoundary;
        private readonly List<Vector2> _oldBottomBoundary;
        private readonly List<Vector2> _newTopBoundary;
        private readonly List<Vector2> _newBottomBoundary;
        
        public ModifyFormationBoundaryCommand(
            ProjectedFormation formation,
            List<Vector2> oldTopBoundary,
            List<Vector2> oldBottomBoundary,
            List<Vector2> newTopBoundary,
            List<Vector2> newBottomBoundary)
        {
            _formation = formation;
            _oldTopBoundary = new List<Vector2>(oldTopBoundary);
            _oldBottomBoundary = new List<Vector2>(oldBottomBoundary);
            _newTopBoundary = new List<Vector2>(newTopBoundary);
            _newBottomBoundary = new List<Vector2>(newBottomBoundary);
        }
        
        public void Execute()
        {
            _formation.TopBoundary = new List<Vector2>(_newTopBoundary);
            _formation.BottomBoundary = new List<Vector2>(_newBottomBoundary);
        }
        
        public void Undo()
        {
            _formation.TopBoundary = new List<Vector2>(_oldTopBoundary);
            _formation.BottomBoundary = new List<Vector2>(_oldBottomBoundary);
        }
        
        public string Description => $"Modify Boundary of '{_formation.Name}'";
    }
    
    /// <summary>
    /// Command for modifying fault trace
    /// </summary>
    private class ModifyFaultTraceCommand : ICommand
    {
        private readonly ProjectedFault _fault;
        private readonly List<Vector2> _oldTrace;
        private readonly List<Vector2> _newTrace;
        
        public ModifyFaultTraceCommand(
            ProjectedFault fault,
            List<Vector2> oldTrace,
            List<Vector2> newTrace)
        {
            _fault = fault;
            _oldTrace = new List<Vector2>(oldTrace);
            _newTrace = new List<Vector2>(newTrace);
        }
        
        public void Execute()
        {
            _fault.FaultTrace = new List<Vector2>(_newTrace);
        }
        
        public void Undo()
        {
            _fault.FaultTrace = new List<Vector2>(_oldTrace);
        }
        
        public string Description => $"Modify Fault Trace ({_fault.Type})";
    }
    
    /// <summary>
    /// Command for changing formation color
    /// </summary>
    private class ChangeFormationColorCommand : ICommand
    {
        private readonly ProjectedFormation _formation;
        private readonly Vector4 _oldColor;
        private readonly Vector4 _newColor;
        
        public ChangeFormationColorCommand(ProjectedFormation formation, Vector4 oldColor, Vector4 newColor)
        {
            _formation = formation;
            _oldColor = oldColor;
            _newColor = newColor;
        }
        
        public void Execute()
        {
            _formation.Color = _newColor;
        }
        
        public void Undo()
        {
            _formation.Color = _oldColor;
        }
        
        public string Description => $"Change Color of '{_formation.Name}'";
    }
    
    /// <summary>
    /// Command for changing formation name
    /// </summary>
    private class RenameFormationCommand : ICommand
    {
        private readonly ProjectedFormation _formation;
        private readonly string _oldName;
        private readonly string _newName;
        
        public RenameFormationCommand(ProjectedFormation formation, string oldName, string newName)
        {
            _formation = formation;
            _oldName = oldName;
            _newName = newName;
        }
        
        public void Execute()
        {
            _formation.Name = _newName;
        }
        
        public void Undo()
        {
            _formation.Name = _oldName;
        }
        
        public string Description => $"Rename Formation from '{_oldName}' to '{_newName}'";
    }
    
    /// <summary>
    /// Command for changing fault properties
    /// </summary>
    private class ModifyFaultPropertiesCommand : ICommand
    {
        private readonly ProjectedFault _fault;
        private readonly float _oldDip;
        private readonly float _newDip;
        private readonly string _oldDipDirection;
        private readonly string _newDipDirection;
        private readonly float? _oldDisplacement;
        private readonly float? _newDisplacement;
        
        public ModifyFaultPropertiesCommand(
            ProjectedFault fault,
            float oldDip, float newDip,
            string oldDipDirection, string newDipDirection,
            float? oldDisplacement, float? newDisplacement)
        {
            _fault = fault;
            _oldDip = oldDip;
            _newDip = newDip;
            _oldDipDirection = oldDipDirection;
            _newDipDirection = newDipDirection;
            _oldDisplacement = oldDisplacement;
            _newDisplacement = newDisplacement;
        }
        
        public void Execute()
        {
            _fault.Dip = _newDip;
            _fault.DipDirection = _newDipDirection;
            _fault.Displacement = _newDisplacement;
        }
        
        public void Undo()
        {
            _fault.Dip = _oldDip;
            _fault.DipDirection = _oldDipDirection;
            _fault.Displacement = _oldDisplacement;
        }
        
        public string Description => $"Modify Fault Properties ({_fault.Type})";
    }
    
    /// <summary>
    /// Command for setting fold style
    /// </summary>
    private class SetFoldStyleCommand : ICommand
    {
        private readonly ProjectedFormation _formation;
        private readonly FoldStyle? _oldStyle;
        private readonly FoldStyle? _newStyle;
        
        public SetFoldStyleCommand(ProjectedFormation formation, FoldStyle? oldStyle, FoldStyle? newStyle)
        {
            _formation = formation;
            _oldStyle = oldStyle;
            _newStyle = newStyle;
        }
        
        public void Execute()
        {
            _formation.FoldStyle = _newStyle;
        }
        
        public void Undo()
        {
            _formation.FoldStyle = _oldStyle;
        }
        
        public string Description => $"Set Fold Style to {(_newStyle.HasValue ? _newStyle.Value.ToString() : "None")}";
    }
    
    #endregion
}