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
using GeoscientistToolkit.UI.Utils;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
/// Viewer for 2D geological cross-sections with editing capabilities
/// </summary>
public class TwoDGeologyViewer : IDisposable
{
    private readonly TwoDGeologyDataset _dataset;
    private CrossSection _crossSection;
    
    // Restoration System
    private CrossSection _restorationData;
    private StructuralRestoration _restorationProcessor;
    private float _restorationPercentage = 0f;
    private bool _showRestorationOverlay = false;
    private bool _restorationInitialized = false;
    private readonly List<(float percentage, CrossSection snapshot)> _restorationHistory = new();
    private int _currentRestorationStep = -1;
    
    // Undo/Redo system
    public UndoRedoManager UndoRedo { get; } = new();
    
    // Tools system
    public TwoDGeologyTools Tools { get; private set; }
    public CustomTopographyDrawTool CustomTopographyDrawer { get; private set; }

    // View state
    private Vector2 _panOffset = Vector2.Zero;
    private float _zoom = 1.0f;
    private float _verticalExaggeration = 2.0f;
    
    // Selection state
    private ProjectedFormation _selectedFormation;
    private ProjectedFault _selectedFault;
    private int _selectedVertexIndex = -1;
    private int _selectedFaultVertexIndex = -1;
    public int SelectedVertexIndex { get => _selectedVertexIndex; set => _selectedVertexIndex = value; }
    private bool _isDraggingVertex = false;
    private bool _isDraggingFaultVertex = false;
    private Vector2 _dragStartPos;
    
    // Mouse state
    private bool _isPanning = false;
    private Vector2 _lastMousePos = Vector2.Zero;
    private Vector2 _lastMouseWorldPos = Vector2.Zero;
    private bool _wasMouseDown = false;
    
    // Display options
    private bool _showFormations = true;
    private bool _showFaults = true;
    private bool _showTopography = true;
    private bool _showGrid = true;
    private bool _showLayersPanel = true;
    
    // Colors
    private readonly uint _backgroundColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.12f, 1.0f));
    private readonly uint _gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 0.3f));
    private readonly uint _topographyColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.5f, 0.2f, 1.0f));
    private readonly uint _faultColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
    private readonly uint _selectionColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
    
    // Layer panel state
    private int _selectedLayerIndex = -1;
    private int _selectedFaultIndex = -1;
    private bool _renamingLayer = false;
    private string _renameBuffer = "";

    // Context menu state
    private bool _showContextMenu = false;
    private Vector2 _contextMenuPos = Vector2.Zero;
    private Vector2 _contextMenuWorldPos = Vector2.Zero;
    private int _contextualSegmentIndex = -1;
    private Vector2 _contextualPoint;
    
    // Tool to move entire formations
    private bool _movingEntireFormation = false;
    private Vector2 _formationMoveStartPos = Vector2.Zero;
    
    public TwoDGeologyViewer(TwoDGeologyDataset dataset)
    {
        _dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        _dataset.RegisterViewer(this);
        
        // Load the cross section data
        _dataset.Load();
        _crossSection = _dataset.ProfileData;
        
        // Ensure we have a valid profile
        if (_crossSection == null)
        {
            _crossSection = CreateDefaultProfile();
            _dataset.ProfileData = _crossSection;
        }
        
        if (_crossSection != null)
        {
            _verticalExaggeration = _crossSection.VerticalExaggeration;
            FitViewToProfile();
        }
        
        // Initialize tools
        Tools = new TwoDGeologyTools(this, _dataset);
        CustomTopographyDrawer = new CustomTopographyDrawTool();
        
        // Subscribe to tool selection events to keep viewer selection in sync
        Tools.FormationSelected += (formation) => {
            _selectedFormation = formation;
            _selectedFault = null;
            _selectedLayerIndex = _crossSection.Formations.IndexOf(formation);
            _selectedFaultIndex = -1;
        };
        
        Tools.FaultSelected += (fault) => {
            _selectedFault = fault;
            _selectedFormation = null;
            _selectedFaultIndex = _crossSection.Faults.IndexOf(fault);
            _selectedLayerIndex = -1;
        };
        
        Tools.SelectionCleared += () => {
            _selectedFormation = null;
            _selectedFault = null;
            _selectedLayerIndex = -1;
            _selectedFaultIndex = -1;
        };
        
        Logger.Log($"TwoDGeologyViewer initialized for '{dataset.Name}'");
    }
    
    private CrossSection CreateDefaultProfile()
    {
        var profile = new CrossSection
        {
            Profile = new TopographicProfile
            {
                Name = "Default Profile",
                TotalDistance = 10000f,
                MinElevation = -2000f,
                MaxElevation = 1000f,
                StartPoint = new Vector2(0, 0),
                EndPoint = new Vector2(10000f, 0),
                CreatedAt = DateTime.Now,
                VerticalExaggeration = 2.0f,
                Points = new List<ProfilePoint>()
            },
            VerticalExaggeration = 2.0f,
            Formations = new List<ProjectedFormation>(),
            Faults = new List<ProjectedFault>()
        };
        
        // Generate default flat topography at sea level
        var numPoints = 50;
        for (var i = 0; i <= numPoints; i++)
        {
            var distance = i / (float)numPoints * profile.Profile.TotalDistance;
            profile.Profile.Points.Add(new ProfilePoint
            {
                Position = new Vector2(distance, 0),
                Distance = distance,
                Elevation = 0,
                Features = new List<GeologicalFeature>()
            });
        }
        
        // NO DEFAULT BEDROCK - keep it clean
        
        return profile;
    }
    
    private void FitViewToProfile()
    {
        if (_crossSection?.Profile == null) return;
        _panOffset = Vector2.Zero;
        _zoom = 0.8f;
    }
    
    private void InitializeRestoration()
    {
        if (_crossSection == null) return;
        
        _restorationProcessor = new StructuralRestoration(_crossSection);
        _restorationHistory.Clear();
        _currentRestorationStep = -1;
        _restorationPercentage = 0f;
        _restorationInitialized = true;
        
        _restorationHistory.Add((0f, DeepCopySection(_crossSection)));
        _currentRestorationStep = 0;
        
        Logger.Log("Initialized structural restoration processor");
    }
    
    public void SetRestorationData(CrossSection restorationData)
    {
        _restorationData = restorationData;
        _showRestorationOverlay = restorationData != null;
    }
    
    public void ClearRestorationData()
    {
        _restorationData = null;
        _showRestorationOverlay = false;
        _restorationPercentage = 0f;
        _restorationHistory.Clear();
        _currentRestorationStep = -1;
        _restorationInitialized = false;
    }
    
    public void Render()
    {
        if (_crossSection == null)
        {
            ImGui.Text("No cross-section data loaded");
            return;
        }
        
        var windowSize = ImGui.GetContentRegionAvail();
        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        
        if (ImGui.BeginChild("ViewerContent", windowSize, ImGuiChildFlags.None, flags))
        {
            RenderToolbar();
            RenderViewportAndPanel();
            RenderOverlapFixPopup(); // Render overlap fix popup
        }
        ImGui.EndChild();
    }
    
    private void RenderToolbar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 4));
        
        // File operations
        if (ImGui.Button("Save"))
        {
            _dataset.Save();
            Logger.Log("Saved 2D Geology dataset");
        }
        
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Save changes to file");
        
        ImGui.SameLine();
        
        if (ImGui.Button("Export SVG"))
        {
            ExportToSVG();
        }
        
        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();
        
        // Undo/Redo
        ImGui.BeginDisabled(!UndoRedo.CanUndo);
        if (ImGui.Button($"Undo ({UndoRedo.UndoCount})"))
        {
            UndoRedo.Undo();
            _dataset.MarkAsModified();
        }
        ImGui.EndDisabled();
        
        if (ImGui.IsItemHovered())
        {
            if (UndoRedo.CanUndo)
                ImGui.SetTooltip($"Undo: {UndoRedo.GetUndoDescription()}");
            else
                ImGui.SetTooltip("Nothing to undo");
        }
        
        ImGui.SameLine();
        
        ImGui.BeginDisabled(!UndoRedo.CanRedo);
        if (ImGui.Button($"Redo ({UndoRedo.RedoCount})"))
        {
            UndoRedo.Redo();
            _dataset.MarkAsModified();
        }
        ImGui.EndDisabled();
        
        if (ImGui.IsItemHovered())
        {
            if (UndoRedo.CanRedo)
                ImGui.SetTooltip($"Redo: {UndoRedo.GetRedoDescription()}");
            else
                ImGui.SetTooltip("Nothing to redo");
        }
        
        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();
        
        // View controls (first line)
        if (ImGui.Button("Fit View"))
        {
            FitViewToProfile();
        }
        
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.DragFloat("##VExag", ref _verticalExaggeration, 0.1f, 0.1f, 10.0f, "VE: %.1f"))
        {
            _crossSection.VerticalExaggeration = _verticalExaggeration;
            _dataset.MarkAsModified();
        }
        
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Vertical Exaggeration");
        
        ImGui.SameLine();
        ImGui.Checkbox("Grid", ref _showGrid);
        ImGui.SameLine();
        ImGui.Checkbox("Topo", ref _showTopography);
        ImGui.SameLine();
        ImGui.Checkbox("Layers", ref _showLayersPanel);
        
        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();
        
        // Overlap checking
        if (ImGui.Button("Check Overlaps"))
        {
            CheckAndReportOverlaps();
        }
        
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Check for geological formation overlaps");
        
        // SECOND LINE for restoration controls to prevent truncation
        if (ImGui.Button("Restore"))
        {
            if (!_restorationInitialized)
            {
                InitializeRestoration();
            }
        }
        
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("##RestPct", ref _restorationPercentage, 0f, 100f, "%.0f%%"))
        {
            if (_restorationInitialized && _restorationProcessor != null)
            {
                _restorationProcessor.Restore(_restorationPercentage);
                SetRestorationData(_restorationProcessor.RestoredSection);
            }
        }
        
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Restoration percentage");
        
        ImGui.SameLine();
        if (ImGui.Button("Clear Overlay"))
        {
            ClearRestorationData();
        }
        
        ImGui.PopStyleVar();
        ImGui.Separator();
    }
    
    private void RenderViewportAndPanel()
    {
        var availSize = ImGui.GetContentRegionAvail();
        
        if (_showLayersPanel)
        {
            // Split view: viewport on left, layers panel on right
            var viewportWidth = availSize.X * 0.7f;
            var panelWidth = availSize.X * 0.3f;
            
            if (ImGui.BeginChild("Viewport", new Vector2(viewportWidth, availSize.Y), ImGuiChildFlags.Border))
            {
                RenderViewport();
            }
            ImGui.EndChild();
            
            ImGui.SameLine();
            
            if (ImGui.BeginChild("LayersPanel", new Vector2(panelWidth, availSize.Y), ImGuiChildFlags.Border))
            {
                RenderLayersPanel();
            }
            ImGui.EndChild();
        }
        else
        {
            // Full viewport
            if (ImGui.BeginChild("Viewport", availSize, ImGuiChildFlags.Border))
            {
                RenderViewport();
            }
            ImGui.EndChild();
        }
    }
    
    private void RenderViewport()
    {
        var availSize = ImGui.GetContentRegionAvail();
        var screenPos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        
        // Background
        drawList.AddRectFilled(screenPos, screenPos + availSize, _backgroundColor);
        
        // Handle input
        HandleViewportInput(screenPos, availSize);
        
        // Draw grid
        if (_showGrid)
        {
            DrawGrid(drawList, screenPos, availSize);
        }
        
        // Draw formations
        if (_showFormations)
        {
            DrawFormations(drawList, screenPos, availSize);
        }
        
        // Draw faults
        if (_showFaults)
        {
            DrawFaults(drawList, screenPos, availSize);
        }
        
        // Draw topography
        if (_showTopography)
        {
            DrawTopography(drawList, screenPos, availSize);
        }
        
        // Draw restoration overlay
        if (_showRestorationOverlay && _restorationData != null)
        {
            DrawRestorationOverlay(drawList, screenPos, availSize);
        }
        
        // Draw tool overlays
        Tools.RenderOverlay(drawList, (worldPos) => WorldToScreen(worldPos, screenPos, availSize));
        
        // Draw custom topography preview
        if (CustomTopographyDrawer.IsDrawing)
        {
            CustomTopographyDrawer.RenderDrawPreview(drawList, (worldPos) => WorldToScreen(worldPos, screenPos, availSize));
        }
        
        // Context menu
        if (_showContextMenu)
        {
            RenderContextMenu();
        }
        
        ImGui.SetCursorScreenPos(screenPos + availSize);
    }
    
    private void HandleViewportInput(Vector2 screenPos, Vector2 availSize)
    {
        var io = ImGui.GetIO();
        var mousePos = io.MousePos - screenPos;
        bool isHovered = mousePos.X >= 0 && mousePos.X < availSize.X && 
                        mousePos.Y >= 0 && mousePos.Y < availSize.Y;
        
        if (!isHovered) return;
        
        var worldPos = ScreenToWorld(mousePos, availSize);
        _lastMouseWorldPos = worldPos;
        
        bool leftClick = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        bool leftDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        bool leftReleased = ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        bool rightClick = ImGui.IsMouseClicked(ImGuiMouseButton.Right);
        bool middleDown = ImGui.IsMouseDown(ImGuiMouseButton.Middle);
        
        // Zooming with mouse wheel
        if (io.MouseWheel != 0)
        {
            var zoomFactor = io.MouseWheel > 0 ? 1.1f : 0.9f;
            _zoom = Math.Clamp(_zoom * zoomFactor, 0.1f, 10f);
        }
        
        // Panning with middle mouse or space+left mouse
        // CRITICAL: Don't pan if we're dragging a vertex or formation!
        bool isDragging = _isDraggingVertex || _isDraggingFaultVertex || _movingEntireFormation;
        
        if (!isDragging && (middleDown || (leftDown && io.KeyShift)))
        {
            if (!_isPanning)
            {
                _isPanning = true;
                _lastMousePos = mousePos;
            }
            else
            {
                var delta = mousePos - _lastMousePos;
                _panOffset += delta;
                _lastMousePos = mousePos;
            }
        }
        else
        {
            _isPanning = false;
        }
        
        // Right-click context menu
        if (rightClick)
        {
            _contextMenuPos = io.MousePos;
            _contextMenuWorldPos = worldPos;
            
            // CRITICAL FIX: Select the formation/fault under the cursor before opening context menu
            HandleSelection(worldPos);
            
            // Find which element was clicked
            _contextualSegmentIndex = FindNearestBoundarySegment(worldPos);
            
            // Open the popup
            ImGui.OpenPopup("ViewerContextMenu");
        }
        
        // Tool interaction
        if (!_isPanning)
        {
            // Keyboard input
            Tools.HandleKeyboardInput();
            
            if (CustomTopographyDrawer.IsDrawing)
            {
                CustomTopographyDrawer.HandleDrawingInput(worldPos, leftDown, leftClick);
            }
            else
            {
                // Mouse input for tools
                Tools.HandleMouseInput(worldPos, leftClick, rightClick, leftDown && !leftClick);
                
                // Selection and dragging
                if (leftClick && Tools.CurrentEditMode == TwoDGeologyTools.EditMode.SelectFormation)
                {
                    HandleSelection(worldPos);
                }
                
                if (leftDown && _selectedFormation != null && _selectedVertexIndex >= 0)
                {
                    HandleVertexDragging(worldPos, leftDown, leftReleased);
                }
                
                if (leftDown && _selectedFault != null && _selectedFaultVertexIndex >= 0)
                {
                    HandleFaultVertexDragging(worldPos, leftDown, leftReleased);
                }
                
                // Move entire formation
                if (_movingEntireFormation && _selectedFormation != null)
                {
                    HandleFormationMove(worldPos, leftDown, leftReleased);
                }
            }
        }
        
        _wasMouseDown = leftDown;
    }
    
    private void HandleSelection(Vector2 worldPos)
    {
        _selectedVertexIndex = -1;
        _selectedFaultVertexIndex = -1;
        
        // Try to select a fault first (they are usually on top)
        foreach (var fault in _crossSection.Faults)
        {
            for (int i = 0; i < fault.FaultTrace.Count; i++)
            {
                if (Vector2.Distance(worldPos, fault.FaultTrace[i]) < 100f)
                {
                    _selectedFault = fault;
                    _selectedFormation = null;
                    _selectedFaultVertexIndex = i;
                    _selectedFaultIndex = _crossSection.Faults.IndexOf(fault);
                    _selectedLayerIndex = -1;
                    Tools.SetSelectedFault(fault);
                    return;
                }
            }
        }
        
        // Try to select a formation boundary vertex
        foreach (var formation in _crossSection.Formations)
        {
            for (int i = 0; i < formation.TopBoundary.Count; i++)
            {
                if (Vector2.Distance(worldPos, formation.TopBoundary[i]) < 100f)
                {
                    _selectedFormation = formation;
                    _selectedFault = null;
                    _selectedVertexIndex = i;
                    _selectedLayerIndex = _crossSection.Formations.IndexOf(formation);
                    _selectedFaultIndex = -1;
                    Tools.SetSelectedFormation(formation);
                    return;
                }
            }
            
            for (int i = 0; i < formation.BottomBoundary.Count; i++)
            {
                if (Vector2.Distance(worldPos, formation.BottomBoundary[i]) < 100f)
                {
                    _selectedFormation = formation;
                    _selectedFault = null;
                    _selectedVertexIndex = i + 10000; // Offset to indicate bottom boundary
                    _selectedLayerIndex = _crossSection.Formations.IndexOf(formation);
                    _selectedFaultIndex = -1;
                    Tools.SetSelectedFormation(formation);
                    return;
                }
            }
        }
        
        // No vertex selected, check if clicking inside a formation
        for (int i = _crossSection.Formations.Count - 1; i >= 0; i--)
        {
            var formation = _crossSection.Formations[i];
            if (IsPointInFormation(worldPos, formation))
            {
                _selectedFormation = formation;
                _selectedFault = null;
                _selectedVertexIndex = -1;
                _selectedLayerIndex = i;
                _selectedFaultIndex = -1;
                Tools.SetSelectedFormation(formation);
                return;
            }
        }
        
        // Nothing selected
        _selectedFormation = null;
        _selectedFault = null;
        _selectedLayerIndex = -1;
        _selectedFaultIndex = -1;
        Tools.ClearSelection();
    }
    
    private bool IsPointInFormation(Vector2 point, ProjectedFormation formation)
    {
        // Simple check: point must be between top and bottom boundaries
        if (formation.TopBoundary.Count < 2 || formation.BottomBoundary.Count < 2)
            return false;
        
        // Find X position
        float minX = formation.TopBoundary.Min(p => p.X);
        float maxX = formation.TopBoundary.Max(p => p.X);
        
        if (point.X < minX || point.X > maxX)
            return false;
        
        // Interpolate top and bottom elevations at this X
        float topY = InterpolateY(formation.TopBoundary, point.X);
        float bottomY = InterpolateY(formation.BottomBoundary, point.X);
        
        return point.Y >= bottomY && point.Y <= topY;
    }
    
    private float InterpolateY(List<Vector2> boundary, float x)
    {
        for (int i = 0; i < boundary.Count - 1; i++)
        {
            if (x >= boundary[i].X && x <= boundary[i + 1].X)
            {
                float t = (x - boundary[i].X) / (boundary[i + 1].X - boundary[i].X);
                return boundary[i].Y + t * (boundary[i + 1].Y - boundary[i].Y);
            }
        }
        return boundary[0].Y;
    }
    
    private void HandleVertexDragging(Vector2 worldPos, bool leftDown, bool leftReleased)
    {
        if (!_isDraggingVertex)
        {
            _isDraggingVertex = true;
            _dragStartPos = GetVertexPosition(_selectedFormation, _selectedVertexIndex);
        }
        
        if (leftDown)
        {
            SetVertexPosition(_selectedFormation, _selectedVertexIndex, worldPos);
            _dataset.MarkAsModified();
        }
        
        if (leftReleased)
        {
            var finalPos = GetVertexPosition(_selectedFormation, _selectedVertexIndex);
            UndoRedo.ExecuteCommand(new MoveVertexCommand(
                GetBoundary(_selectedFormation, _selectedVertexIndex),
                GetBoundaryIndex(_selectedVertexIndex),
                _dragStartPos,
                finalPos
            ));
            _isDraggingVertex = false;
        }
    }
    
    private void HandleFaultVertexDragging(Vector2 worldPos, bool leftDown, bool leftReleased)
    {
        if (!_isDraggingFaultVertex)
        {
            _isDraggingFaultVertex = true;
            _dragStartPos = _selectedFault.FaultTrace[_selectedFaultVertexIndex];
        }
        
        if (leftDown)
        {
            _selectedFault.FaultTrace[_selectedFaultVertexIndex] = worldPos;
            _dataset.MarkAsModified();
        }
        
        if (leftReleased)
        {
            var finalPos = _selectedFault.FaultTrace[_selectedFaultVertexIndex];
            UndoRedo.ExecuteCommand(new MoveVertexCommand(
                _selectedFault.FaultTrace,
                _selectedFaultVertexIndex,
                _dragStartPos,
                finalPos
            ));
            _isDraggingFaultVertex = false;
        }
    }
    
    private void HandleFormationMove(Vector2 worldPos, bool leftDown, bool leftReleased)
    {
        if (leftDown)
        {
            var delta = worldPos - _formationMoveStartPos;
            
            // Move all vertices
            for (int i = 0; i < _selectedFormation.TopBoundary.Count; i++)
            {
                _selectedFormation.TopBoundary[i] += delta;
            }
            for (int i = 0; i < _selectedFormation.BottomBoundary.Count; i++)
            {
                _selectedFormation.BottomBoundary[i] += delta;
            }
            
            _formationMoveStartPos = worldPos;
            _dataset.MarkAsModified();
        }
        
        if (leftReleased)
        {
            _movingEntireFormation = false;
        }
    }
    
    private Vector2 GetVertexPosition(ProjectedFormation formation, int index)
    {
        if (index >= 10000)
        {
            index -= 10000;
            return formation.BottomBoundary[index];
        }
        return formation.TopBoundary[index];
    }
    
    private void SetVertexPosition(ProjectedFormation formation, int index, Vector2 pos)
    {
        if (index >= 10000)
        {
            index -= 10000;
            formation.BottomBoundary[index] = pos;
        }
        else
        {
            formation.TopBoundary[index] = pos;
        }
    }
    
    private List<Vector2> GetBoundary(ProjectedFormation formation, int index)
    {
        return index >= 10000 ? formation.BottomBoundary : formation.TopBoundary;
    }
    
    private int GetBoundaryIndex(int index)
    {
        return index >= 10000 ? index - 10000 : index;
    }
    
    private void RenderContextMenu()
    {
        if (ImGui.BeginPopup("ViewerContextMenu"))
        {
            if (_selectedFormation != null)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), $"Formation: {_selectedFormation.Name}");
                ImGui.Separator();
                
                if (ImGui.MenuItem("Rename Formation"))
                {
                    _renamingLayer = true;
                    _renameBuffer = _selectedFormation.Name ?? "";
                }
                
                ImGui.Separator();
                
                if (ImGui.MenuItem("Add Vertex"))
                {
                    AddVertexToFormation(_selectedFormation, _contextualSegmentIndex, _contextMenuWorldPos);
                    _dataset.MarkAsModified();
                }
                
                if (ImGui.MenuItem("Move Formation"))
                {
                    _movingEntireFormation = true;
                    _formationMoveStartPos = _contextMenuWorldPos;
                }
                
                ImGui.Separator();
                
                if (ImGui.MenuItem("Delete Formation", "Del"))
                {
                    DeleteFormation(_selectedFormation);
                }
            }
            else if (_selectedFault != null)
            {
                ImGui.TextColored(new Vector4(1f, 0.7f, 0.7f, 1f), $"Fault: {_selectedFault.Type.ToString().Replace("Fault_", "")}");
                ImGui.Separator();
                
                if (ImGui.MenuItem("Add Vertex"))
                {
                    AddVertexToFault(_selectedFault, _contextMenuWorldPos);
                    _dataset.MarkAsModified();
                }
                
                ImGui.Separator();
                
                if (ImGui.MenuItem("Delete Fault", "Del"))
                {
                    DeleteFault(_selectedFault);
                }
            }
            else
            {
                ImGui.TextDisabled("No selection");
                ImGui.Separator();
                ImGui.TextDisabled("Right-click on a formation or fault");
                ImGui.TextDisabled("to see available options");
            }
            
            ImGui.EndPopup();
        }
        
        // Rename dialog
        if (_renamingLayer && _selectedFormation != null)
        {
            ImGui.OpenPopup("Rename Formation");
            _renamingLayer = false;
        }
        
        if (ImGui.BeginPopupModal("Rename Formation", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Enter new name:");
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("##rename", ref _renameBuffer, 100, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                if (!string.IsNullOrWhiteSpace(_renameBuffer))
                {
                    _selectedFormation.Name = _renameBuffer;
                    _dataset.MarkAsModified();
                    Logger.Log($"Renamed formation to '{_renameBuffer}'");
                }
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.Separator();
            
            if (ImGui.Button("OK", new Vector2(145, 0)))
            {
                if (!string.IsNullOrWhiteSpace(_renameBuffer))
                {
                    _selectedFormation.Name = _renameBuffer;
                    _dataset.MarkAsModified();
                    Logger.Log($"Renamed formation to '{_renameBuffer}'");
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(145, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.EndPopup();
        }
    }
    
    private void AddVertexToFormation(ProjectedFormation formation, int segmentIndex, Vector2 worldPos)
    {
        if (segmentIndex >= 0 && segmentIndex < formation.TopBoundary.Count - 1)
        {
            formation.TopBoundary.Insert(segmentIndex + 1, worldPos);
            formation.BottomBoundary.Insert(segmentIndex + 1, new Vector2(worldPos.X, worldPos.Y - 100f));
            _dataset.MarkAsModified();
            Logger.Log("Added vertex to formation");
        }
    }
    
    private void AddVertexToFault(ProjectedFault fault, Vector2 worldPos)
    {
        // Find closest segment
        int bestSegment = 0;
        float minDist = float.MaxValue;
        
        for (int i = 0; i < fault.FaultTrace.Count - 1; i++)
        {
            float dist = GeologyGeometryUtils.DistanceToLineSegment(worldPos, fault.FaultTrace[i], fault.FaultTrace[i + 1]);
            if (dist < minDist)
            {
                minDist = dist;
                bestSegment = i;
            }
        }
        
        fault.FaultTrace.Insert(bestSegment + 1, worldPos);
        _dataset.MarkAsModified();
        Logger.Log("Added vertex to fault");
    }
    
    private int FindNearestBoundarySegment(Vector2 worldPos)
    {
        if (_selectedFormation == null) return -1;
        
        int bestSegment = -1;
        float minDist = float.MaxValue;
        
        for (int i = 0; i < _selectedFormation.TopBoundary.Count - 1; i++)
        {
            float dist = GeologyGeometryUtils.DistanceToLineSegment(worldPos, 
                _selectedFormation.TopBoundary[i], 
                _selectedFormation.TopBoundary[i + 1]);
            if (dist < minDist)
            {
                minDist = dist;
                bestSegment = i;
            }
        }
        
        return minDist < 200f ? bestSegment : -1;
    }
    
    private void DeleteFormation(ProjectedFormation formation)
    {
        UndoRedo.ExecuteCommand(new RemoveFormationCommand(_crossSection, formation));
        _dataset.MarkAsModified();
        _selectedFormation = null;
        _selectedLayerIndex = -1;
    }
    
    private void DeleteFault(ProjectedFault fault)
    {
        UndoRedo.ExecuteCommand(new RemoveFaultCommand(_crossSection, fault));
        _dataset.MarkAsModified();
        _selectedFault = null;
        _selectedFaultIndex = -1;
    }
    
    private void DrawGrid(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        var profile = _crossSection.Profile;
        if (profile == null) return;
        
        // Vertical grid lines every 1000m
        for (float x = 0; x <= profile.TotalDistance; x += 1000f)
        {
            var top = WorldToScreen(new Vector2(x, profile.MaxElevation), screenPos, availSize);
            var bottom = WorldToScreen(new Vector2(x, profile.MinElevation), screenPos, availSize);
            drawList.AddLine(top, bottom, _gridColor);
        }
        
        // Horizontal grid lines every 500m
        for (float y = profile.MinElevation; y <= profile.MaxElevation; y += 500f)
        {
            var left = WorldToScreen(new Vector2(0, y), screenPos, availSize);
            var right = WorldToScreen(new Vector2(profile.TotalDistance, y), screenPos, availSize);
            drawList.AddLine(left, right, _gridColor);
        }
    }
    
    private void DrawFormations(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        foreach (var formation in _crossSection.Formations)
        {
            if (!formation.GetIsVisible()) continue;
            
            var color = ImGui.ColorConvertFloat4ToU32(formation.Color);
            var isSelected = formation == _selectedFormation;
            
            // Draw filled polygon
            if (formation.TopBoundary.Count > 1 && formation.BottomBoundary.Count > 1)
            {
                var vertices = new List<Vector2>();
                foreach (var point in formation.TopBoundary)
                {
                    vertices.Add(WorldToScreen(point, screenPos, availSize));
                }
                for (int i = formation.BottomBoundary.Count - 1; i >= 0; i--)
                {
                    vertices.Add(WorldToScreen(formation.BottomBoundary[i], screenPos, availSize));
                }
                
                var vertexArray = vertices.ToArray();
                drawList.AddConvexPolyFilled(ref vertexArray[0], vertices.Count, color);
            }
            
            // Draw boundaries
            var boundaryColor = isSelected ? _selectionColor : ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.2f, 1f));
            DrawBoundary(drawList, formation.TopBoundary, screenPos, availSize, boundaryColor, isSelected);
            DrawBoundary(drawList, formation.BottomBoundary, screenPos, availSize, boundaryColor, isSelected);
        }
    }
    
    private void DrawBoundary(ImDrawListPtr drawList, List<Vector2> boundary, Vector2 screenPos, Vector2 availSize, uint color, bool showVertices)
    {
        for (int i = 0; i < boundary.Count - 1; i++)
        {
            var p1 = WorldToScreen(boundary[i], screenPos, availSize);
            var p2 = WorldToScreen(boundary[i + 1], screenPos, availSize);
            drawList.AddLine(p1, p2, color, 2f);
        }
        
        if (showVertices)
        {
            foreach (var point in boundary)
            {
                var screenPoint = WorldToScreen(point, screenPos, availSize);
                drawList.AddCircleFilled(screenPoint, 5f, _selectionColor);
            }
        }
    }
    
    private void DrawFaults(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        foreach (var fault in _crossSection.Faults)
        {
            var isSelected = fault == _selectedFault;
            var color = isSelected ? _selectionColor : _faultColor;
            
            // Draw fault trace (now finite with start and end points)
            for (int i = 0; i < fault.FaultTrace.Count - 1; i++)
            {
                var p1 = WorldToScreen(fault.FaultTrace[i], screenPos, availSize);
                var p2 = WorldToScreen(fault.FaultTrace[i + 1], screenPos, availSize);
                drawList.AddLine(p1, p2, color, isSelected ? 3f : 2f);
            }
            
            // Draw vertices if selected
            if (isSelected)
            {
                foreach (var point in fault.FaultTrace)
                {
                    var screenPoint = WorldToScreen(point, screenPos, availSize);
                    drawList.AddCircleFilled(screenPoint, 6f, _selectionColor);
                }
            }
            
            // Draw fault type indicator at midpoint
            if (fault.FaultTrace.Count >= 2)
            {
                int midIdx = fault.FaultTrace.Count / 2;
                var midPoint = fault.FaultTrace[midIdx];
                var screenMid = WorldToScreen(midPoint, screenPos, availSize);
                
                string symbol = fault.Type switch
                {
                    GeologicalFeatureType.Fault_Normal => "N",
                    GeologicalFeatureType.Fault_Reverse => "R",
                    GeologicalFeatureType.Fault_Thrust => "T",
                    GeologicalFeatureType.Fault_Transform => "SS",
                    _ => "F"
                };
                
                drawList.AddText(screenMid + new Vector2(10, -10), ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), symbol);
            }
        }
    }
    
    private void DrawTopography(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        var profile = _crossSection.Profile;
        if (profile?.Points == null || profile.Points.Count < 2) return;
        
        var points = new List<Vector2>();
        foreach (var point in profile.Points)
        {
            points.Add(WorldToScreen(new Vector2(point.Distance, point.Elevation), screenPos, availSize));
        }
        
        for (int i = 0; i < points.Count - 1; i++)
        {
            drawList.AddLine(points[i], points[i + 1], _topographyColor, 2f);
        }
    }
    
    private void DrawRestorationOverlay(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        if (_restorationData == null) return;
        
        var overlayColor = new Vector4(0.5f, 0.8f, 1f, 0.3f);
        
        foreach (var formation in _restorationData.Formations)
        {
            var color = ImGui.ColorConvertFloat4ToU32(overlayColor);
            
            if (formation.TopBoundary.Count > 1 && formation.BottomBoundary.Count > 1)
            {
                var vertices = new List<Vector2>();
                foreach (var point in formation.TopBoundary)
                {
                    vertices.Add(WorldToScreen(point, screenPos, availSize));
                }
                for (int i = formation.BottomBoundary.Count - 1; i >= 0; i--)
                {
                    vertices.Add(WorldToScreen(formation.BottomBoundary[i], screenPos, availSize));
                }
                
                var vertexArray = vertices.ToArray();
                drawList.AddConvexPolyFilled(ref vertexArray[0], vertices.Count, color);
            }
        }
    }
    
    private void RenderLayersPanel()
    {
        ImGui.Text("Layers");
        ImGui.Separator();
        
        // Formations
        ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), "Formations:");
        for (int i = 0; i < _crossSection.Formations.Count; i++)
        {
            var formation = _crossSection.Formations[i];
            bool isVisible = formation.GetIsVisible();
            bool isSelected = i == _selectedLayerIndex;
            
            if (isSelected)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));
            
            ImGui.PushID(i);
            
            if (ImGui.Checkbox("##vis", ref isVisible))
            {
                formation.SetIsVisible(isVisible);
            }
            
            ImGui.SameLine();
            ImGui.ColorButton("##color", formation.Color, ImGuiColorEditFlags.NoTooltip, new Vector2(20, 20));
            
            ImGui.SameLine();
            if (ImGui.Selectable(formation.Name, isSelected))
            {
                _selectedFormation = formation;
                _selectedFault = null;
                _selectedLayerIndex = i;
                _selectedFaultIndex = -1;
                Tools.SetSelectedFormation(formation);
            }
            
            ImGui.PopID();
            
            if (isSelected)
                ImGui.PopStyleColor();
        }
        
        ImGui.Separator();
        
        // Faults
        ImGui.TextColored(new Vector4(1f, 0.7f, 0.7f, 1f), "Faults:");
        for (int i = 0; i < _crossSection.Faults.Count; i++)
        {
            var fault = _crossSection.Faults[i];
            bool isSelected = i == _selectedFaultIndex;
            
            if (isSelected)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 0f, 1f));
            
            ImGui.PushID(1000 + i);
            
            string faultName = $"{fault.Type.ToString().Replace("Fault_", "")} ({fault.Dip:F0}°)";
            if (ImGui.Selectable(faultName, isSelected))
            {
                _selectedFault = fault;
                _selectedFormation = null;
                _selectedFaultIndex = i;
                _selectedLayerIndex = -1;
                Tools.SetSelectedFault(fault);
            }
            
            ImGui.PopID();
            
            if (isSelected)
                ImGui.PopStyleColor();
        }
    }
    
    private void ExportToSVG()
    {
        try
        {
            var svgPath = _dataset.FilePath.Replace(".2dgeol", ".svg");
            var exporter = new SvgExporter();
            var svgContent = exporter.ExportToSvg(_crossSection, includeLabels: true, includeGrid: true, includeLegend: true);
            File.WriteAllText(svgPath, svgContent);
            Logger.Log($"Exported to SVG: {svgPath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to export SVG: {ex.Message}");
        }
    }
    
    private CrossSection DeepCopySection(CrossSection section)
    {
        // Simple deep copy for restoration
        var copy = new CrossSection
        {
            Profile = section.Profile,
            VerticalExaggeration = section.VerticalExaggeration,
            Formations = new List<ProjectedFormation>(),
            Faults = new List<ProjectedFault>()
        };
        
        foreach (var f in section.Formations)
        {
            copy.Formations.Add(new ProjectedFormation
            {
                Name = f.Name,
                Color = f.Color,
                TopBoundary = new List<Vector2>(f.TopBoundary),
                BottomBoundary = new List<Vector2>(f.BottomBoundary),
                FoldStyle = f.FoldStyle
            });
        }
        
        foreach (var f in section.Faults)
        {
            copy.Faults.Add(new ProjectedFault
            {
                Type = f.Type,
                FaultTrace = new List<Vector2>(f.FaultTrace),
                Dip = f.Dip,
                DipDirection = f.DipDirection,
                Displacement = f.Displacement
            });
        }
        
        return copy;
    }
    
    public Vector2 WorldToScreen(Vector2 worldPos, Vector2 screenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null || _zoom == 0) return screenPos;
        
        var profile = _crossSection.Profile;
        
        float worldXMin = 0;
        float worldXMax = profile.TotalDistance;
        float worldYMin = profile.MinElevation;
        float worldYMax = profile.MaxElevation;
        
        float normX = (worldXMax - worldXMin) == 0 ? 0 : (worldPos.X - worldXMin) / (worldXMax - worldXMin);
        float normY = (worldYMax - worldYMin) == 0 ? 0 : (worldPos.Y - worldYMin) / (worldYMax - worldYMin);
        
        float centerX = 0.5f;
        float centerY = 0.5f;

        normX = centerX + (normX - centerX) * _zoom;
        normY = centerY + (normY - centerY) * _zoom;
        
        normY *= _verticalExaggeration;
        
        float viewX = normX * availSize.X;
        float viewY = normY * availSize.Y;
        
        viewX += _panOffset.X;
        viewY += _panOffset.Y;

        return screenPos + new Vector2(viewX, availSize.Y - viewY);
    }
    
    public Vector2 ScreenToWorld(Vector2 localScreenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null || _zoom == 0) return Vector2.Zero;
        
        var profile = _crossSection.Profile;
        
        Vector2 viewPos = new Vector2(localScreenPos.X, availSize.Y - localScreenPos.Y);

        viewPos.X -= _panOffset.X;
        viewPos.Y -= _panOffset.Y;
        
        float normX = viewPos.X / availSize.X;
        float normY = viewPos.Y / availSize.Y;

        if (_verticalExaggeration != 0)
        {
            normY /= _verticalExaggeration;
        }

        float centerX = 0.5f;
        float centerY = 0.5f;

        normX = centerX + (normX - centerX) / _zoom;
        normY = centerY + (normY - centerY) / _zoom;
        
        float worldX = normX * profile.TotalDistance;
        float worldY = normY * (profile.MaxElevation - profile.MinElevation) + profile.MinElevation;
        
        return new Vector2(worldX, worldY);
    }
    
    private void CheckAndReportOverlaps()
    {
        var overlaps = GeologicalConstraints.FindAllOverlaps(_crossSection.Formations);
        
        if (overlaps.Count == 0)
        {
            Logger.Log("✓ No formation overlaps detected");
            return;
        }
        
        Logger.LogWarning($"⚠ Found {overlaps.Count} formation overlap(s):");
        foreach (var (f1, f2) in overlaps)
        {
            Logger.LogWarning($"  - '{f1.Name}' overlaps with '{f2.Name}'");
        }
        
        // Show fix option
        ImGui.OpenPopup("OverlapDetected");
    }
    
    private void RenderOverlapFixPopup()
    {
        if (ImGui.BeginPopupModal("OverlapDetected", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Geological formations overlap detected!");
            ImGui.TextColored(new Vector4(1f, 0.7f, 0.3f, 1f), 
                "CRITICAL: Formations must never overlap in geological cross-sections.");
            ImGui.Separator();
            
            var overlaps = GeologicalConstraints.FindAllOverlaps(_crossSection.Formations);
            ImGui.Text($"Found {overlaps.Count} overlap(s):");
            
            foreach (var (f1, f2) in overlaps)
            {
                ImGui.BulletText($"'{f1.Name}' ↔ '{f2.Name}'");
            }
            
            ImGui.Separator();
            
            if (ImGui.Button("Auto-Fix (Arrange Formations)", new Vector2(220, 30)))
            {
                GeologicalConstraints.AutoArrangeFormations(_crossSection.Formations);
                _dataset.MarkAsModified();
                Logger.Log("Auto-fixed formation overlaps");
                ImGui.CloseCurrentPopup();
            }
            
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Automatically adjust formations to eliminate overlaps\nMaintains relative positions");
            
            ImGui.SameLine();
            
            if (ImGui.Button("Close", new Vector2(100, 30)))
            {
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.EndPopup();
        }
    }
    
    public void Dispose()
    {
        // No unmanaged resources to dispose
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
    
    #endregion
}

// Extension to support visibility in formations
public static class FormationExtensions
{
    private static readonly Dictionary<ProjectedFormation, bool> _visibilityMap = new();
    
    public static bool GetIsVisible(this ProjectedFormation formation)
    {
        return _visibilityMap.TryGetValue(formation, out var visible) ? visible : true;
    }
    
    public static void SetIsVisible(this ProjectedFormation formation, bool value)
    {
        _visibilityMap[formation] = value;
    }
}