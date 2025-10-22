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
    public UndoRedoManager UndoRedo { get; } = new();
    
    // Tools system - NEW: Added reference to tools
    public TwoDGeologyTools Tools { get; private set; }
    
    // View state
    private Vector2 _panOffset = Vector2.Zero;
    private float _zoom = 1.0f;
    private float _verticalExaggeration = 2.0f;
    
    // Selection state - now managed by tools
    private ProjectedFormation _selectedFormation;
    private ProjectedFault _selectedFault;
    private int _selectedVertexIndex = -1;
    private bool _isDraggingVertex = false;
    
    // Mouse state
    private bool _isPanning = false;
    private Vector2 _lastMouseWorldPos = Vector2.Zero;
    
    // Display options
    private bool _showFormations = true;
    private bool _showFaults = true;
    private bool _showTopography = true;
    private bool _showGrid = true;
    private bool _showRestorationOverlay = false;
    
    // Colors
    private readonly uint _backgroundColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.12f, 1.0f));
    private readonly uint _gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.4f, 0.5f));
    private readonly uint _topographyColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.5f, 0.2f, 1.0f));
    private readonly uint _faultColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
    private readonly uint _selectionColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
    
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
        
        // Initialize tools - NEW: Create tools instance
        Tools = new TwoDGeologyTools(this, _dataset);
        
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
        if (ImGui.Button("Undo") && UndoRedo.CanUndo) UndoRedo.Undo();
        ImGui.SameLine();
        if (ImGui.Button("Redo") && UndoRedo.CanRedo) UndoRedo.Redo();
        ImGui.SameLine();
        ImGui.TextUnformatted("|");
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
        ImGui.TextUnformatted("|");
        ImGui.SameLine();
        
        ImGui.Text($"Zoom: {_zoom:F2}x");
        
        ImGui.SameLine();
        ImGui.PushItemWidth(100f);
        if (ImGui.SliderFloat("V.E.", ref _verticalExaggeration, 0.5f, 10.0f, "%.1fx"))
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
        
        // Draw background
        drawList.AddRectFilled(screenPos, screenPos + availSize, _backgroundColor);
        
        // FIXED RENDERING ORDER:
        // 1. Back plane - Grid (background)
        if (_showGrid) 
            RenderGrid(drawList, screenPos, availSize);
        
        // 2. Background layer - Topography (drawn first, behind formations)
        if (_showTopography && _crossSection.Profile != null) 
            RenderTopography(drawList, screenPos, availSize);
        
        // 3. Middle layer - Formations (filled polygons)
        if (_showFormations) 
            RenderFormations(drawList, screenPos, availSize, _crossSection.Formations, false);
        
        // 4. Middle layer - Restoration overlay (if active)
        if (_showRestorationOverlay && _restorationData != null) 
            RenderRestorationOverlay(drawList, screenPos, availSize);
        
        // 5. Foreground layer - Faults (lines on top)
        if (_showFaults) 
            RenderFaults(drawList, screenPos, availSize, _crossSection.Faults, false);
        
        // 6. Overlay layer - Selection highlights
        RenderSelection(drawList, screenPos, availSize);
        
        // 7. Top overlay - Tools overlay (temp points, measurements, etc.)
        Tools?.RenderOverlay(drawList, pos => WorldToScreen(pos, screenPos, availSize));
        
        // Create invisible button for input handling BEFORE handling input
        ImGui.InvisibleButton("viewport", availSize);
        
        // Handle mouse input
        HandleMouseInput(screenPos, availSize);
    }
    
    private void HandleMouseInput(Vector2 screenPos, Vector2 availSize)
    {
        var io = ImGui.GetIO();
        
        // Handle input only when the viewport is hovered
        if (!ImGui.IsItemHovered())
        {
            _isPanning = false;
            return;
        }

        var localMousePos = io.MousePos - screenPos;
        var worldMousePos = ScreenToWorld(localMousePos, availSize);
        _lastMouseWorldPos = worldMousePos;
        
        // Handle keyboard input for tools
        Tools?.HandleKeyboardInput();
        
        // Check if we should handle panning or pass to tools
        bool isMiddleOrRightDragging = ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || 
                                       ImGui.IsMouseDragging(ImGuiMouseButton.Right);
        
        // Pan with middle or right mouse button
        if (isMiddleOrRightDragging)
        {
            if (!_isPanning)
            {
                _isPanning = true;
            }
            // Panning sensitivity is increased for a better feel
            _panOffset += io.MouseDelta * 1.5f; 
        }
        else
        {
            _isPanning = false;
            
            // NEW: Pass mouse input to tools when not panning
            if (Tools != null)
            {
                bool leftClick = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                bool rightClick = ImGui.IsMouseClicked(ImGuiMouseButton.Right);
                bool isDragging = ImGui.IsMouseDragging(ImGuiMouseButton.Left);
                
                Tools.HandleMouseInput(worldMousePos, leftClick, rightClick, isDragging);
                
                // Update selection state from tools
                _selectedFormation = Tools.GetSelectedFormation();
                _selectedFault = Tools.GetSelectedFault();
            }
        }
        
        // Zoom with mouse wheel, centered on the cursor
        if (Math.Abs(io.MouseWheel) > 0.001f)
        {
            var oldZoom = _zoom;
            var oldMouseWorld = ScreenToWorld(localMousePos, availSize);

            _zoom *= MathF.Pow(1.1f, io.MouseWheel);
            _zoom = Math.Clamp(_zoom, 0.1f, 100.0f);

            var newMouseWorld = ScreenToWorld(localMousePos, availSize);
            
            // Adjust pan offset to keep the point under the cursor stationary
            _panOffset.X -= (newMouseWorld.X - oldMouseWorld.X) * (availSize.X / _crossSection.Profile.TotalDistance) * _zoom;
            _panOffset.Y += (newMouseWorld.Y - oldMouseWorld.Y) * (availSize.Y / (_crossSection.Profile.MaxElevation - _crossSection.Profile.MinElevation)) * _zoom;
        }
    }
    
    private void RenderGrid(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null) return;
        
        var minWorld = ScreenToWorld(Vector2.Zero, availSize);
        var maxWorld = ScreenToWorld(availSize, availSize);
        
        // Determine a dynamic grid spacing based on zoom level
        float xSpacing = MathF.Pow(10, MathF.Ceiling(MathF.Log10((maxWorld.X - minWorld.X) / 10)));
        float ySpacing = MathF.Pow(10, MathF.Ceiling(MathF.Log10((maxWorld.Y - minWorld.Y) / 10)));

        // Draw vertical grid lines
        for (float x = MathF.Floor(minWorld.X / xSpacing) * xSpacing; x < maxWorld.X; x += xSpacing)
        {
            var p1 = WorldToScreen(new Vector2(x, minWorld.Y), screenPos, availSize);
            var p2 = WorldToScreen(new Vector2(x, maxWorld.Y), screenPos, availSize);
            drawList.AddLine(p1, p2, _gridColor);
        }

        // Draw horizontal grid lines
        for (float y = MathF.Floor(minWorld.Y / ySpacing) * ySpacing; y < maxWorld.Y; y += ySpacing)
        {
            var p1 = WorldToScreen(new Vector2(minWorld.X, y), screenPos, availSize);
            var p2 = WorldToScreen(new Vector2(maxWorld.X, y), screenPos, availSize);
            drawList.AddLine(p1, p2, _gridColor);
        }
    }
    
    private void RenderTopography(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        var profile = _crossSection.Profile;
        if (profile.Points.Count < 2) return;
        
        for (int i = 0; i < profile.Points.Count - 1; i++)
        {
            var p1 = new Vector2(profile.Points[i].Distance, profile.Points[i].Elevation);
            var p2 = new Vector2(profile.Points[i + 1].Distance, profile.Points[i + 1].Elevation);
            
            drawList.AddLine(
                WorldToScreen(p1, screenPos, availSize), 
                WorldToScreen(p2, screenPos, availSize), 
                _topographyColor, 3.0f);
        }
    }
    
    private void RenderFormations(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize, List<ProjectedFormation> formations, bool isOverlay)
    {
        foreach (var formation in formations)
        {
            if (formation.TopBoundary.Count < 2 || formation.BottomBoundary.Count < 2) continue;
            
            var baseColor = formation.Color;
            var color = isOverlay 
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(baseColor.X, baseColor.Y, baseColor.Z, 0.4f))
                : ImGui.ColorConvertFloat4ToU32(baseColor);
            
            // Build polygon: top boundary forward, bottom boundary backward
            var polygon = new List<Vector2>();
            foreach (var point in formation.TopBoundary) 
                polygon.Add(WorldToScreen(point, screenPos, availSize));
            for (int i = formation.BottomBoundary.Count - 1; i >= 0; i--) 
                polygon.Add(WorldToScreen(formation.BottomBoundary[i], screenPos, availSize));
            
            if (polygon.Count >= 3)
            {
                // Draw filled polygon
                var firstPoint = polygon[0];
                drawList.AddConvexPolyFilled(ref firstPoint, polygon.Count, color);
                
                // Draw outline for better visibility
                var outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
                for (int i = 0; i < polygon.Count; i++)
                {
                    var nextIdx = (i + 1) % polygon.Count;
                    drawList.AddLine(polygon[i], polygon[nextIdx], outlineColor, 1.0f);
                }
            }
        }
    }
    
    private void RenderFaults(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize, List<ProjectedFault> faults, bool isOverlay)
    {
        var color = isOverlay
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(1,0,0,0.5f))
            : _faultColor;

        foreach (var fault in faults)
        {
            if (fault.FaultTrace.Count < 2) continue;
            
            for (int i = 0; i < fault.FaultTrace.Count - 1; i++)
            {
                drawList.AddLine(
                    WorldToScreen(fault.FaultTrace[i], screenPos, availSize), 
                    WorldToScreen(fault.FaultTrace[i + 1], screenPos, availSize), 
                    color, 3.0f);
            }
        }
    }
    
    private void RenderRestorationOverlay(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        RenderFormations(drawList, screenPos, availSize, _restorationData.Formations, true);
        RenderFaults(drawList, screenPos, availSize, _restorationData.Faults, true);
    }
    
    private void RenderSelection(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        if (_selectedFormation != null)
        {
            // Highlight boundaries and vertices of the selected formation
            var top = _selectedFormation.TopBoundary;
            var bottom = _selectedFormation.BottomBoundary;

            for (int i = 0; i < top.Count - 1; i++)
                drawList.AddLine(WorldToScreen(top[i], screenPos, availSize), WorldToScreen(top[i+1], screenPos, availSize), _selectionColor, 3.0f);
            foreach(var p in top) 
                drawList.AddCircleFilled(WorldToScreen(p, screenPos, availSize), 5f, _selectionColor);

            for (int i = 0; i < bottom.Count - 1; i++)
                drawList.AddLine(WorldToScreen(bottom[i], screenPos, availSize), WorldToScreen(bottom[i+1], screenPos, availSize), _selectionColor, 3.0f);
            foreach(var p in bottom) 
                drawList.AddCircleFilled(WorldToScreen(p, screenPos, availSize), 5f, _selectionColor);
        }
        
        if (_selectedFault != null)
        {
            // Highlight trace and vertices of the selected fault
            var trace = _selectedFault.FaultTrace;
            for (int i = 0; i < trace.Count - 1; i++)
                drawList.AddLine(WorldToScreen(trace[i], screenPos, availSize), WorldToScreen(trace[i+1], screenPos, availSize), _selectionColor, 3.0f);
            foreach(var p in trace) 
                drawList.AddCircleFilled(WorldToScreen(p, screenPos, availSize), 5f, _selectionColor);
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
                var cmd = new ChangeFormationColorCommand(_selectedFormation, _selectedFormation.Color, color);
                UndoRedo.ExecuteCommand(cmd);
            }
            
            var name = _selectedFormation.Name ?? "";
            if (ImGui.InputText("Name", ref name, 256, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                var cmd = new RenameFormationCommand(_selectedFormation, _selectedFormation.Name, name);
                UndoRedo.ExecuteCommand(cmd);
            }
        }
        else if (_selectedFault != null)
        {
            ImGui.Text($"Fault: {_selectedFault.Type.ToString().Replace("Fault_", "")}");
            ImGui.Text($"Dip: {_selectedFault.Dip:F1}°");
            
            var disp = _selectedFault.Displacement ?? 0f;
            if (ImGui.InputFloat("Displacement", ref disp, 10f, 100f, "%.1f m", ImGuiInputTextFlags.EnterReturnsTrue))
            {
                var cmd = new ModifyFaultPropertiesCommand(_selectedFault, _selectedFault.Dip, _selectedFault.Dip, "", "", _selectedFault.Displacement, disp);
                UndoRedo.ExecuteCommand(cmd);
            }
        }
        else
        {
            ImGui.Text("No selection");
        }
    }
    
    public Vector2 WorldToScreen(Vector2 worldPos, Vector2 screenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null) return screenPos;
        
        var profile = _crossSection.Profile;
        
        // Define world space boundaries
        float worldXMin = 0;
        float worldXMax = profile.TotalDistance;
        float worldYMin = profile.MinElevation;
        float worldYMax = profile.MaxElevation;
        
        // Normalize world coordinates to a 0-1 range
        float normX = (worldPos.X - worldXMin) / (worldXMax - worldXMin);
        float normY = (worldPos.Y - worldYMin) / (worldYMax - worldYMin);
        
        // Apply vertical exaggeration to the normalized Y
        normY *= _verticalExaggeration;
        
        // Scale to the viewport size
        float viewX = normX * availSize.X * _zoom;
        float viewY = normY * availSize.Y * _zoom;
        
        // Apply pan offset
        viewX -= _panOffset.X;
        viewY -= _panOffset.Y;
        
        // Invert Y-axis for screen coordinates (0,0 is top-left)
        return screenPos + new Vector2(viewX, availSize.Y - viewY);
    }
    
    public Vector2 ScreenToWorld(Vector2 localScreenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null || _zoom == 0) return Vector2.Zero;
        
        var profile = _crossSection.Profile;
        
        // Invert Y-axis from screen coordinates
        Vector2 viewPos = new Vector2(localScreenPos.X, availSize.Y - localScreenPos.Y);
        
        // Remove pan offset
        viewPos.X += _panOffset.X;
        viewPos.Y += _panOffset.Y;
        
        // Un-scale from viewport size
        float normX = viewPos.X / (availSize.X * _zoom);
        float normY = viewPos.Y / (availSize.Y * _zoom);
        
        // Remove vertical exaggeration
        normY /= _verticalExaggeration;
        
        // Un-normalize to get world coordinates
        float worldX = normX * (profile.TotalDistance) + 0;
        float worldY = normY * (profile.MaxElevation - profile.MinElevation) + profile.MinElevation;
        
        return new Vector2(worldX, worldY);
    }
    
    public void Dispose()
    {
        // No unmanaged resources to dispose in this class
    }
    
    #region Command Implementations
    
    public class MoveVertexCommand : ICommand
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
            if (_index >= 0 && _index < _boundary.Count) _boundary[_index] = _newPosition;
        }
        
        public void Undo()
        {
            if (_index >= 0 && _index < _boundary.Count) _boundary[_index] = _oldPosition;
        }
        
        public string Description => $"Move Vertex";
    }
    
    public class AddFormationCommand : ICommand
    {
        private readonly CrossSection _crossSection;
        private readonly ProjectedFormation _formation;
        
        public AddFormationCommand(CrossSection crossSection, ProjectedFormation formation)
        {
            _crossSection = crossSection;
            _formation = formation;
        }
        
        public void Execute() => _crossSection.Formations.Add(_formation);
        public void Undo() => _crossSection.Formations.Remove(_formation);
        public string Description => $"Add Formation '{_formation.Name}'";
    }
    
    public class RemoveFormationCommand : ICommand
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
        
        public void Execute() => _crossSection.Formations.Remove(_formation);
        public void Undo()
        {
            if (_index >= 0 && _index <= _crossSection.Formations.Count)
                _crossSection.Formations.Insert(_index, _formation);
        }
        public string Description => $"Remove Formation '{_formation.Name}'";
    }
    
    public class AddFaultCommand : ICommand
    {
        private readonly CrossSection _crossSection;
        private readonly ProjectedFault _fault;
        
        public AddFaultCommand(CrossSection crossSection, ProjectedFault fault)
        {
            _crossSection = crossSection;
            _fault = fault;
        }
        
        public void Execute() => _crossSection.Faults.Add(_fault);
        public void Undo() => _crossSection.Faults.Remove(_fault);
        public string Description => $"Add Fault ({_fault.Type.ToString().Replace("Fault_", "")})";
    }
    
    public class RemoveFaultCommand : ICommand
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
        
        public void Execute() => _crossSection.Faults.Remove(_fault);
        public void Undo()
        {
            if (_index >= 0 && _index <= _crossSection.Faults.Count)
                _crossSection.Faults.Insert(_index, _fault);
        }
        public string Description => $"Remove Fault ({_fault.Type.ToString().Replace("Fault_", "")})";
    }
    
    public class ChangeFormationColorCommand : ICommand
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
        
        public void Execute() => _formation.Color = _newColor;
        public void Undo() => _formation.Color = _oldColor;
        public string Description => $"Change Color of '{_formation.Name}'";
    }
    
    public class RenameFormationCommand : ICommand
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
        
        public void Execute() => _formation.Name = _newName;
        public void Undo() => _formation.Name = _oldName;
        public string Description => $"Rename Formation from '{_oldName}' to '{_newName}'";
    }
    
    public class ModifyFaultPropertiesCommand : ICommand
    {
        private readonly ProjectedFault _fault;
        private readonly float _oldDip, _newDip;
        private readonly string _oldDipDirection, _newDipDirection;
        private readonly float? _oldDisplacement, _newDisplacement;
        
        public ModifyFaultPropertiesCommand(ProjectedFault fault, float oldDip, float newDip, string oldDipDirection, string newDipDirection, float? oldDisplacement, float? newDisplacement)
        {
            _fault = fault;
            _oldDip = oldDip; _newDip = newDip;
            _oldDipDirection = oldDipDirection; _newDipDirection = newDipDirection;
            _oldDisplacement = oldDisplacement; _newDisplacement = newDisplacement;
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
        
        public string Description => $"Modify Fault Properties";
    }
    
    #endregion
}