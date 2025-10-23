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
    private CrossSection _restorationData; // For displaying restoration results
    
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
    public int SelectedVertexIndex { get => _selectedVertexIndex; set => _selectedVertexIndex = value; }
    private bool _isDraggingVertex = false;
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
    private bool _showRestorationOverlay = false;
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
    private int _contextualSegmentIndex = -1;
    private Vector2 _contextualPoint;
    
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
            // Initialize view to fit the profile
            FitViewToProfile();
        }
        
        // Initialize tools
        Tools = new TwoDGeologyTools(this, _dataset);
        CustomTopographyDrawer = new CustomTopographyDrawTool();
        
        Logger.Log($"TwoDGeologyViewer initialized for '{dataset.Name}'");
    }
    
    private CrossSection CreateDefaultProfile()
    {
        var profile = new CrossSection
        {
            Profile = new TopographicProfile
            {
                Name = "Default Profile",
                TotalDistance = 10000f, // 10km
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
                Elevation = 0, // Sea level
                Features = new List<GeologicalFeature>()
            });
        }
        
        return profile;
    }
    
    private void FitViewToProfile()
    {
        if (_crossSection?.Profile == null) return;
        
        // Reset pan and set appropriate zoom to fit the profile
        _panOffset = Vector2.Zero;
        _zoom = 0.8f; // Start at 80% to leave some margin
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
            if (ImGui.Button("Create Default Profile"))
            {
                _crossSection = CreateDefaultProfile();
                _dataset.ProfileData = _crossSection;
                FitViewToProfile();
            }
            return;
        }
        
        RenderToolbar();
        ImGui.Separator();
        
        // Split view between viewport and layers panel
        var availSize = ImGui.GetContentRegionAvail();
        var layersPanelWidth = _showLayersPanel ? 250f : 0f;
        
        // Main viewport
        ImGui.BeginChild("Viewport", new Vector2(availSize.X - layersPanelWidth - 5, availSize.Y * 0.75f), ImGuiChildFlags.Border);
        RenderViewport();
        ImGui.EndChild();
        
        // Layers panel (right side)
        if (_showLayersPanel)
        {
            ImGui.SameLine();
            ImGui.BeginChild("LayersPanel", new Vector2(layersPanelWidth, availSize.Y * 0.75f), ImGuiChildFlags.Border);
            RenderLayersPanel();
            ImGui.EndChild();
        }
        
        // Properties panel (bottom)
        ImGui.BeginChild("Properties", new Vector2(-1, -1), ImGuiChildFlags.Border);
        RenderPropertiesPanel();
        ImGui.EndChild();
    }
    
    private void RenderToolbar()
    {
        if (ImGui.Button("Undo") && UndoRedo.CanUndo) UndoRedo.Undo();
        ImGui.SameLine();
        if (ImGui.Button("Redo") && UndoRedo.CanRedo) UndoRedo.Redo();
        ImGui.SameLine();
        ImGui.TextUnformatted("|");
        ImGui.SameLine();
        
        if (ImGui.Button("Fit View")) FitViewToProfile();
        ImGui.SameLine();
        if (ImGui.Button("Reset Zoom")) _zoom = 1.0f;
        ImGui.SameLine();

        if (ImGui.Button("Export SVG"))
        {
            var exporter = new SvgExporter();
            var outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GeoscientistToolkitExports");
            Directory.CreateDirectory(outputFolder);
            var filePath = Path.Combine(outputFolder, $"{_dataset.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.svg");
            exporter.SaveToFile(filePath, _crossSection);
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("|");
        ImGui.SameLine();
        
        ImGui.Checkbox("Grid", ref _showGrid);
        ImGui.SameLine();
        ImGui.Checkbox("Topography", ref _showTopography);
        ImGui.SameLine();
        ImGui.Checkbox("Formations", ref _showFormations);
        ImGui.SameLine();
        ImGui.Checkbox("Faults", ref _showFaults);
        ImGui.SameLine();
        ImGui.Checkbox("Layers Panel", ref _showLayersPanel);
        
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
    
    private void RenderLayersPanel()
    {
        ImGui.Text("Layers");
        ImGui.Separator();
        
        // Add new formation button
        if (ImGui.Button("Add Formation", new Vector2(-1, 0)))
        {
            var newFormation = new ProjectedFormation
            {
                Name = $"Formation {_crossSection.Formations.Count + 1}",
                Color = GetNextFormationColor(),
                TopBoundary = new List<Vector2>(),
                BottomBoundary = new List<Vector2>()
            };
            
            // Create default boundaries
            var profile = _crossSection.Profile;
            if (profile != null)
            {
                var baseElevation = -500f * _crossSection.Formations.Count;
                for (int i = 0; i < 5; i++)
                {
                    var x = i * profile.TotalDistance / 4f;
                    newFormation.TopBoundary.Add(new Vector2(x, baseElevation));
                    newFormation.BottomBoundary.Add(new Vector2(x, baseElevation - 300f));
                }
            }
            
            var cmd = new AddFormationCommand(_crossSection, newFormation);
            UndoRedo.ExecuteCommand(cmd);
        }
        
        if (ImGui.Button("Add Fault", new Vector2(-1, 0)))
        {
            var newFault = new ProjectedFault
            {
                Type = GeologicalFeatureType.Fault_Normal,
                FaultTrace = new List<Vector2>(),
                Dip = 60f,
                DipDirection = "East"
            };
            
            // Create default fault trace
            var profile = _crossSection.Profile;
            if (profile != null)
            {
                var x = profile.TotalDistance / 2f;
                newFault.FaultTrace.Add(new Vector2(x, profile.MaxElevation));
                newFault.FaultTrace.Add(new Vector2(x + 500f, profile.MinElevation));
            }
            
            var cmd = new AddFaultCommand(_crossSection, newFault);
            UndoRedo.ExecuteCommand(cmd);
        }
        
        ImGui.Separator();
        ImGui.Text("Formations:");
        
        // List formations
        for (int i = _crossSection.Formations.Count - 1; i >= 0; i--)
        {
            var formation = _crossSection.Formations[i];
            var isSelected = (_selectedLayerIndex == i && _selectedFormation == formation);
            
            ImGui.PushID($"formation_{i}");
            
            // Visibility checkbox
            var visible = formation.GetIsVisible();
            if (ImGui.Checkbox("##vis", ref visible))
            {
                formation.SetIsVisible(visible);
            }
            ImGui.SameLine();
            
            // Color indicator
            ImGui.ColorButton("##color", formation.Color, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker, new Vector2(20, 20));
            ImGui.SameLine();
            
            // Formation name (selectable)
            if (ImGui.Selectable(formation.Name, isSelected))
            {
                _selectedLayerIndex = i;
                _selectedFormation = formation;
                _selectedFault = null;
                _selectedFaultIndex = -1;
                Tools?.SetSelectedFormation(formation);
            }
            
            // Right-click context menu
            if (ImGui.BeginPopupContextItem($"formation_context_{i}"))
            {
                if (ImGui.MenuItem("Rename"))
                {
                    _renamingLayer = true;
                    _renameBuffer = formation.Name;
                    _selectedLayerIndex = i;
                    _selectedFormation = formation;
                }
                
                if (ImGui.MenuItem("Change Color"))
                {
                    ImGui.OpenPopup($"color_picker_{i}");
                }
                
                if (ImGui.MenuItem("Move Up") && i < _crossSection.Formations.Count - 1)
                {
                    (_crossSection.Formations[i], _crossSection.Formations[i + 1]) = 
                        (_crossSection.Formations[i + 1], _crossSection.Formations[i]);
                }
                
                if (ImGui.MenuItem("Move Down") && i > 0)
                {
                    (_crossSection.Formations[i], _crossSection.Formations[i - 1]) = 
                        (_crossSection.Formations[i - 1], _crossSection.Formations[i]);
                }
                
                ImGui.Separator();
                
                if (ImGui.MenuItem("Delete"))
                {
                    var cmd = new RemoveFormationCommand(_crossSection, formation);
                    UndoRedo.ExecuteCommand(cmd);
                }
                
                ImGui.EndPopup();
            }
            
            // Color picker popup
            if (ImGui.BeginPopup($"color_picker_{i}"))
            {
                var color = formation.Color;
                if (ImGui.ColorPicker4("Formation Color", ref color))
                {
                    formation.Color = color;
                }
                ImGui.EndPopup();
            }
            
            ImGui.PopID();
        }
        
        ImGui.Separator();
        ImGui.Text("Faults:");
        
        // List faults
        for (int i = 0; i < _crossSection.Faults.Count; i++)
        {
            var fault = _crossSection.Faults[i];
            var isSelected = (_selectedFaultIndex == i && _selectedFault == fault);
            
            ImGui.PushID($"fault_{i}");
            
            var faultName = $"{fault.Type.ToString().Replace("Fault_", "")} Fault {i + 1}";
            if (ImGui.Selectable(faultName, isSelected))
            {
                _selectedFaultIndex = i;
                _selectedFault = fault;
                _selectedFormation = null;
                _selectedLayerIndex = -1;
                Tools?.SetSelectedFault(fault);
            }
            
            // Right-click context menu
            if (ImGui.BeginPopupContextItem($"fault_context_{i}"))
            {
                if (ImGui.MenuItem("Delete"))
                {
                    var cmd = new RemoveFaultCommand(_crossSection, fault);
                    UndoRedo.ExecuteCommand(cmd);
                }
                ImGui.EndPopup();
            }
            
            ImGui.PopID();
        }
        
        // Rename dialog
        if (_renamingLayer && _selectedFormation != null)
        {
            ImGui.OpenPopup("Rename Layer");
            if (ImGui.BeginPopupModal("Rename Layer"))
            {
                ImGui.InputText("New Name", ref _renameBuffer, 256);
                
                if (ImGui.Button("OK"))
                {
                    var cmd = new RenameFormationCommand(_selectedFormation, _selectedFormation.Name, _renameBuffer);
                    UndoRedo.ExecuteCommand(cmd);
                    _renamingLayer = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    _renamingLayer = false;
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.EndPopup();
            }
        }
    }
    
    private Vector4 GetNextFormationColor()
    {
        // Predefined color palette for geological formations
        var colors = new[]
        {
            new Vector4(0.8f, 0.6f, 0.4f, 0.8f), // Sandstone
            new Vector4(0.6f, 0.6f, 0.7f, 0.8f), // Shale
            new Vector4(0.9f, 0.9f, 0.7f, 0.8f), // Limestone
            new Vector4(0.7f, 0.5f, 0.5f, 0.8f), // Mudstone
            new Vector4(0.5f, 0.7f, 0.5f, 0.8f), // Siltstone
            new Vector4(0.9f, 0.7f, 0.5f, 0.8f), // Dolomite
            new Vector4(0.4f, 0.4f, 0.6f, 0.8f), // Basalt
            new Vector4(0.8f, 0.8f, 0.8f, 0.8f), // Granite
        };
        
        return colors[_crossSection.Formations.Count % colors.Length];
    }
    
    private void RenderViewport()
    {
        var availSize = ImGui.GetContentRegionAvail();
        if (availSize.X <= 0 || availSize.Y <= 0) return;
        
        var drawList = ImGui.GetWindowDrawList();
        var screenPos = ImGui.GetCursorScreenPos();
        
        // Create clipping rectangle for the viewport
        drawList.PushClipRect(screenPos, screenPos + availSize);
        
        // Draw background
        drawList.AddRectFilled(screenPos, screenPos + availSize, _backgroundColor);
        
        // FIXED RENDERING ORDER:
        // 1. Grid (background)
        if (_showGrid) 
            RenderGrid(drawList, screenPos, availSize);
        
        // 2. Formations (filled polygons)
        if (_showFormations) 
            RenderFormations(drawList, screenPos, availSize, _crossSection.Formations, false);
        
        // 3. Topography (line on top of formations)
        if (_showTopography && _crossSection.Profile != null) 
            RenderTopography(drawList, screenPos, availSize);
        
        // 4. Restoration overlay (if active)
        if (_showRestorationOverlay && _restorationData != null) 
            RenderRestorationOverlay(drawList, screenPos, availSize);
        
        // 5. Faults (lines on top)
        if (_showFaults) 
            RenderFaults(drawList, screenPos, availSize, _crossSection.Faults, false);
        
        // 6. Selection highlights
        RenderSelection(drawList, screenPos, availSize);
        
        // 7. Tools overlay (temp points, measurements, etc.)
        Tools?.RenderOverlay(drawList, pos => WorldToScreen(pos, screenPos, availSize));
        CustomTopographyDrawer?.RenderDrawPreview(drawList, pos => WorldToScreen(pos, screenPos, availSize));
        
        // 8. Axes and labels
        RenderAxes(drawList, screenPos, availSize);
        
        drawList.PopClipRect();
        
        // Create invisible button for input handling
        ImGui.SetCursorScreenPos(screenPos);
        ImGui.InvisibleButton("viewport", availSize);
        
        // Handle mouse input and context menus
        HandleMouseInput(screenPos, availSize);
        RenderFaultContextMenus();
    }
    
    private void RenderGrid(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null) return;
        
        var profile = _crossSection.Profile;
        
        // Determine grid spacing based on zoom level
        float[] possibleSpacings = { 100, 250, 500, 1000, 2500, 5000, 10000 };
        var targetPixelSpacing = 50f; // Target spacing in pixels
        var worldSpacingPerPixel = profile.TotalDistance / (availSize.X * _zoom);
        var idealSpacing = targetPixelSpacing * worldSpacingPerPixel;
        
        float gridSpacingX = possibleSpacings[0];
        foreach (var spacing in possibleSpacings)
        {
            if (spacing >= idealSpacing)
            {
                gridSpacingX = spacing;
                break;
            }
        }
        
        // Vertical grid lines
        for (float x = 0; x <= profile.TotalDistance; x += gridSpacingX)
        {
            var worldPos = new Vector2(x, profile.MinElevation);
            var screenPosBottom = WorldToScreen(worldPos, screenPos, availSize);
            worldPos.Y = profile.MaxElevation;
            var screenPosTop = WorldToScreen(worldPos, screenPos, availSize);
            
            drawList.AddLine(screenPosBottom, screenPosTop, _gridColor, 1.0f);
        }
        
        // Horizontal grid lines
        var elevRange = profile.MaxElevation - profile.MinElevation;
        var gridSpacingY = GetNiceSpacing(elevRange / 10f);
        
        for (float y = profile.MinElevation; y <= profile.MaxElevation; y += gridSpacingY)
        {
            var worldPos = new Vector2(0, y);
            var screenPosLeft = WorldToScreen(worldPos, screenPos, availSize);
            worldPos.X = profile.TotalDistance;
            var screenPosRight = WorldToScreen(worldPos, screenPos, availSize);
            
            drawList.AddLine(screenPosLeft, screenPosRight, _gridColor, 1.0f);
        }
    }
    
    private float GetNiceSpacing(float roughSpacing)
    {
        float[] niceNumbers = { 1, 2, 5 };
        var magnitude = MathF.Pow(10, MathF.Floor(MathF.Log10(roughSpacing)));
        var normalized = roughSpacing / magnitude;
        
        foreach (var nice in niceNumbers)
        {
            if (normalized <= nice)
                return nice * magnitude;
        }
        
        return 10 * magnitude;
    }
    
    private void RenderTopography(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        var profile = _crossSection.Profile;
        if (profile?.Points == null || profile.Points.Count < 2) return;
        
        // Draw topography as a thick line
        for (int i = 0; i < profile.Points.Count - 1; i++)
        {
            var worldPos1 = new Vector2(profile.Points[i].Distance, profile.Points[i].Elevation);
            var worldPos2 = new Vector2(profile.Points[i + 1].Distance, profile.Points[i + 1].Elevation);
            
            var screenPos1 = WorldToScreen(worldPos1, screenPos, availSize);
            var screenPos2 = WorldToScreen(worldPos2, screenPos, availSize);
            
            drawList.AddLine(screenPos1, screenPos2, _topographyColor, 3.0f);
        }
        
        // Draw points
        foreach (var point in profile.Points)
        {
            var worldPos = new Vector2(point.Distance, point.Elevation);
            var screenPosPoint = WorldToScreen(worldPos, screenPos, availSize);
            drawList.AddCircleFilled(screenPosPoint, 3f, _topographyColor);
        }
    }
    
    private void RenderFormations(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize, 
        List<ProjectedFormation> formations, bool isOverlay)
    {
        if (formations == null || formations.Count == 0) return;

        foreach (var formation in formations)
        {
            if (!formation.GetIsVisible() || formation.TopBoundary.Count < 2 || formation.BottomBoundary.Count < 2) continue;

            var color = ImGui.ColorConvertFloat4ToU32(formation.Color);
            if (isOverlay) color = (color & 0x00FFFFFF) | 0x80000000; // Make semi-transparent
            
            var outlineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, isOverlay ? 0.3f : 0.5f));

            // Render formation as a strip of quads to handle non-convex shapes robustly
            for (int i = 0; i < formation.TopBoundary.Count - 1; i++)
            {
                if (i + 1 >= formation.BottomBoundary.Count) break;

                var p1_top = WorldToScreen(formation.TopBoundary[i], screenPos, availSize);
                var p2_top = WorldToScreen(formation.TopBoundary[i + 1], screenPos, availSize);
                var p1_bot = WorldToScreen(formation.BottomBoundary[i], screenPos, availSize);
                var p2_bot = WorldToScreen(formation.BottomBoundary[i + 1], screenPos, availSize);

                drawList.AddQuadFilled(p1_top, p2_top, p2_bot, p1_bot, color);
            }
            
            // Draw the outline separately for a clean look
            for (int i = 0; i < formation.TopBoundary.Count - 1; i++)
            {
                drawList.AddLine(
                    WorldToScreen(formation.TopBoundary[i], screenPos, availSize),
                    WorldToScreen(formation.TopBoundary[i + 1], screenPos, availSize),
                    outlineColor, 1.0f);
            }
            for (int i = 0; i < formation.BottomBoundary.Count - 1; i++)
            {
                drawList.AddLine(
                    WorldToScreen(formation.BottomBoundary[i], screenPos, availSize),
                    WorldToScreen(formation.BottomBoundary[i + 1], screenPos, availSize),
                    outlineColor, 1.0f);
            }
        }
    }
    
    private List<Vector2> InterpolateCatmullRom(List<Vector2> points, int pointsPerSegment)
    {
        if (points.Count < 2) return points;

        var result = new List<Vector2>();
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = i > 0 ? points[i - 1] : points[i];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i < points.Count - 2 ? points[i + 2] : p2;

            for (var j = 0; j < pointsPerSegment; j++)
            {
                var t = j / (float)pointsPerSegment;
                var interpolatedPoint = InterpolationUtils.CatmullRom(p0, p1, p2, p3, t);
                result.Add(interpolatedPoint);
            }
        }
        result.Add(points.Last());
        return result;
    }
    
    private void RenderFaults(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize,
        List<ProjectedFault> faults, bool isOverlay)
    {
        if (faults == null) return;
        
        foreach (var fault in faults)
        {
            if (fault.FaultTrace.Count < 2) continue;
            
            var color = isOverlay ? 
                ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.5f, 0.0f, 0.5f)) : 
                _faultColor;

            if (fault.FaultTrace.Count > 2)
            {
                // Draw as a curve for 3+ points
                var splinePoints = InterpolateCatmullRom(fault.FaultTrace, 10);
                for (var j = 0; j < splinePoints.Count - 1; j++)
                {
                    var screenPos1 = WorldToScreen(splinePoints[j], screenPos, availSize);
                    var screenPos2 = WorldToScreen(splinePoints[j + 1], screenPos, availSize);
                    drawList.AddLine(screenPos1, screenPos2, color, 3.0f);
                }
            }
            else
            {
                // Draw as a straight line for 2 points
                var screenPos1 = WorldToScreen(fault.FaultTrace[0], screenPos, availSize);
                var screenPos2 = WorldToScreen(fault.FaultTrace[1], screenPos, availSize);
                drawList.AddLine(screenPos1, screenPos2, color, 3.0f);
            }
            
            // Draw fault type indicator
            if (fault.FaultTrace.Count >= 2)
            {
                var midPoint = fault.FaultTrace[fault.FaultTrace.Count / 2];
                var screenMid = WorldToScreen(midPoint, screenPos, availSize);
                
                // Draw appropriate symbol based on fault type
                switch (fault.Type)
                {
                    case GeologicalFeatureType.Fault_Normal:
                        // Draw downward arrows
                        drawList.AddLine(screenMid, screenMid + new Vector2(-10, 10), color, 2.0f);
                        drawList.AddLine(screenMid, screenMid + new Vector2(10, 10), color, 2.0f);
                        break;
                    case GeologicalFeatureType.Fault_Reverse:
                        // Draw upward arrows
                        drawList.AddLine(screenMid, screenMid + new Vector2(-10, -10), color, 2.0f);
                        drawList.AddLine(screenMid, screenMid + new Vector2(10, -10), color, 2.0f);
                        break;
                    case GeologicalFeatureType.Fault_Strike_Slip:
                        // Draw horizontal arrows
                        drawList.AddLine(screenMid + new Vector2(-15, 0), screenMid + new Vector2(-5, 0), color, 2.0f);
                        drawList.AddLine(screenMid + new Vector2(5, 0), screenMid + new Vector2(15, 0), color, 2.0f);
                        break;
                }
            }
        }
    }
    
    private void RenderRestorationOverlay(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        if (_restorationData == null) return;
        
        // Render restored formations with transparency
        RenderFormations(drawList, screenPos, availSize, _restorationData.Formations, true);
        RenderFaults(drawList, screenPos, availSize, _restorationData.Faults, true);
    }
    
    private void RenderSelection(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        // Highlight selected formation
        if (_selectedFormation != null)
        {
            // Draw thicker outlines for selected formation
            if (_selectedFormation.TopBoundary.Count >= 2)
            {
                for (int i = 0; i < _selectedFormation.TopBoundary.Count - 1; i++)
                {
                    drawList.AddLine(
                        WorldToScreen(_selectedFormation.TopBoundary[i], screenPos, availSize),
                        WorldToScreen(_selectedFormation.TopBoundary[i + 1], screenPos, availSize),
                        _selectionColor, 3.0f);
                }
            }
            
            if (_selectedFormation.BottomBoundary.Count >= 2)
            {
                for (int i = 0; i < _selectedFormation.BottomBoundary.Count - 1; i++)
                {
                    drawList.AddLine(
                        WorldToScreen(_selectedFormation.BottomBoundary[i], screenPos, availSize),
                        WorldToScreen(_selectedFormation.BottomBoundary[i + 1], screenPos, availSize),
                        _selectionColor, 3.0f);
                }
            }
            
            // Draw vertex handles
            foreach (var vertex in _selectedFormation.TopBoundary)
            {
                var screenVertex = WorldToScreen(vertex, screenPos, availSize);
                drawList.AddCircle(screenVertex, 6f, _selectionColor, 12, 2.0f);
            }
            
            foreach (var vertex in _selectedFormation.BottomBoundary)
            {
                var screenVertex = WorldToScreen(vertex, screenPos, availSize);
                drawList.AddCircle(screenVertex, 6f, _selectionColor, 12, 2.0f);
            }
        }
        
        // Highlight selected fault
        if (_selectedFault != null && _selectedFault.FaultTrace.Count >= 2)
        {
            var color = _selectionColor;
            var thickness = 5.0f;
            
            if (_selectedFault.FaultTrace.Count > 2)
            {
                // Draw as a curve for 3+ points
                var splinePoints = InterpolateCatmullRom(_selectedFault.FaultTrace, 10);
                for (var j = 0; j < splinePoints.Count - 1; j++)
                {
                    var screenPos1 = WorldToScreen(splinePoints[j], screenPos, availSize);
                    var screenPos2 = WorldToScreen(splinePoints[j + 1], screenPos, availSize);
                    drawList.AddLine(screenPos1, screenPos2, color, thickness);
                }
            }
            else
            {
                // Draw as a straight line for 2 points
                var screenPos1 = WorldToScreen(_selectedFault.FaultTrace[0], screenPos, availSize);
                var screenPos2 = WorldToScreen(_selectedFault.FaultTrace[1], screenPos, availSize);
                drawList.AddLine(screenPos1, screenPos2, color, thickness);
            }
            
            // Draw vertex handles
            foreach (var vertex in _selectedFault.FaultTrace)
            {
                var screenVertex = WorldToScreen(vertex, screenPos, availSize);
                drawList.AddCircle(screenVertex, 6f, _selectionColor, 12, 2.0f);
            }
        }
    }
    
    private void RenderAxes(ImDrawListPtr drawList, Vector2 screenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null) return;
        
        var profile = _crossSection.Profile;
        var textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.8f, 1.0f));
        
        // Draw distance labels
        var distanceStep = GetNiceSpacing(profile.TotalDistance / 5f);
        for (float x = 0; x <= profile.TotalDistance; x += distanceStep)
        {
            var worldPos = new Vector2(x, profile.MinElevation);
            var screenPosLabel = WorldToScreen(worldPos, screenPos, availSize);
            screenPosLabel.Y = screenPos.Y + availSize.Y - 20;
            
            var label = $"{x:F0}m";
            drawList.AddText(screenPosLabel - new Vector2(ImGui.CalcTextSize(label).X / 2f, 0), 
                textColor, label);
        }
        
        // Draw elevation labels
        var elevStep = GetNiceSpacing((profile.MaxElevation - profile.MinElevation) / 5f);
        for (float y = profile.MinElevation; y <= profile.MaxElevation; y += elevStep)
        {
            var worldPos = new Vector2(0, y);
            var screenPosLabel = WorldToScreen(worldPos, screenPos, availSize);
            screenPosLabel.X = screenPos.X + 5;
            
            var label = $"{y:F0}m";
            drawList.AddText(screenPosLabel, textColor, label);
        }
    }
    
    private void RenderFaultContextMenus()
    {
        if (ImGui.BeginPopup("FaultVertexContextMenu"))
        {
            if (ImGui.MenuItem("Remove Vertex") && _selectedFault != null && _selectedVertexIndex != -1)
            {
                // Can't remove if it leaves less than 2 points
                if (_selectedFault.FaultTrace.Count > 2)
                {
                    var cmd = new RemoveItemAtCommand<Vector2>(_selectedFault.FaultTrace, _selectedVertexIndex, "Vertex");
                    UndoRedo.ExecuteCommand(cmd);
                    _selectedVertexIndex = -1; // Deselect vertex
                }
                else
                {
                    Logger.LogWarning("Cannot remove vertex, fault needs at least 2 points.");
                }
            }
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopup("FaultSegmentContextMenu"))
        {
            if (ImGui.MenuItem("Add Vertex Here") && _selectedFault != null && _contextualSegmentIndex != -1)
            {
                var cmd = new InsertItemCommand<Vector2>(_selectedFault.FaultTrace, _contextualSegmentIndex + 1, _contextualPoint, "Vertex");
                UndoRedo.ExecuteCommand(cmd);
            }
            ImGui.EndPopup();
        }
    }
    
    private void HandleMouseInput(Vector2 screenPos, Vector2 availSize)
    {
        var io = ImGui.GetIO();
        
        // Check if viewport is hovered
        if (!ImGui.IsItemHovered())
        {
            _isPanning = false;
            _isDraggingVertex = false;
            return;
        }
        
        var localMousePos = io.MousePos - screenPos;
        var worldMousePos = ScreenToWorld(localMousePos, availSize);
        
        // Handle zooming with mouse wheel
        if (io.MouseWheel != 0)
        {
            var zoomDelta = io.MouseWheel * 0.1f;
            var oldZoom = _zoom;
            _zoom = Math.Clamp(_zoom + zoomDelta, 0.1f, 10.0f);
            
            // Zoom towards mouse position
            if (_zoom != oldZoom)
            {
                var zoomRatio = _zoom / oldZoom;
                var mouseOffset = localMousePos - availSize / 2f;
                _panOffset = _panOffset * zoomRatio - mouseOffset * (zoomRatio - 1f);
            }
        }
        
        // Handle panning with middle mouse or right mouse
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || 
            (ImGui.IsMouseDragging(ImGuiMouseButton.Right) && !Tools.IsActive() && !CustomTopographyDrawer.IsDrawing))
        {
            _panOffset += io.MouseDelta;
            _isPanning = true;
        }
        else
        {
            _isPanning = false;
        }
        
        // Handle left mouse button for tools or selection
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _lastMousePos = localMousePos;
            _dragStartPos = worldMousePos;
            
            // Check if clicking on a vertex
            if (_selectedFormation != null)
            {
                _selectedVertexIndex = FindNearestVertex(worldMousePos, _selectedFormation);
                if (_selectedVertexIndex >= 0)
                {
                    _isDraggingVertex = true;
                }
            }
            else if (_selectedFault != null)
            {
                _selectedVertexIndex = FindNearestFaultVertex(worldMousePos, _selectedFault);
                if (_selectedVertexIndex >= 0)
                {
                    _isDraggingVertex = true;
                }
            }
        }
        
        // Handle vertex dragging
        if (_isDraggingVertex && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            if (_selectedFormation != null && _selectedVertexIndex >= 0)
            {
                // Determine if it's a top or bottom boundary vertex
                if (_selectedVertexIndex < _selectedFormation.TopBoundary.Count)
                {
                    _selectedFormation.TopBoundary[_selectedVertexIndex] = worldMousePos;
                }
                else
                {
                    var bottomIndex = _selectedVertexIndex - _selectedFormation.TopBoundary.Count;
                    if (bottomIndex < _selectedFormation.BottomBoundary.Count)
                    {
                        _selectedFormation.BottomBoundary[bottomIndex] = worldMousePos;
                    }
                }
            }
            else if (_selectedFault != null && _selectedVertexIndex >= 0 && 
                     _selectedVertexIndex < _selectedFault.FaultTrace.Count)
            {
                _selectedFault.FaultTrace[_selectedVertexIndex] = worldMousePos;
            }
        }
        
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _isDraggingVertex = false;
            // Don't reset _selectedVertexIndex here, we might want to operate on it (e.g., delete)
        }
        
        // Pass input to tools if not handling other operations
        if (!_isPanning && !_isDraggingVertex)
        {
            var leftClick = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            var rightClick = ImGui.IsMouseClicked(ImGuiMouseButton.Right);
            var isDragging = ImGui.IsMouseDragging(ImGuiMouseButton.Left);
            var isMouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);

            // Handle context menus for fault editing before passing to general tools
            if (rightClick && _selectedFault != null)
            {
                var nearestVertex = FindNearestFaultVertex(worldMousePos, _selectedFault);
                if (nearestVertex != -1)
                {
                    _selectedVertexIndex = nearestVertex;
                    ImGui.OpenPopup("FaultVertexContextMenu");
                }
                else
                {
                    var (segmentIndex, closestPoint) = FindNearestFaultSegment(worldMousePos, _selectedFault);
                    if (segmentIndex != -1)
                    {
                        _contextualSegmentIndex = segmentIndex;
                        _contextualPoint = closestPoint;
                        ImGui.OpenPopup("FaultSegmentContextMenu");
                    }
                }
            }
            
            if (CustomTopographyDrawer.IsDrawing)
            {
                CustomTopographyDrawer.HandleDrawingInput(worldMousePos, isMouseDown, leftClick);
            }
            else
            {
                 Tools?.HandleMouseInput(worldMousePos, leftClick, rightClick, isDragging);
            }
        }
        
        // Handle keyboard shortcuts
        Tools?.HandleKeyboardInput();
        
        // Store mouse position for next frame
        _lastMouseWorldPos = worldMousePos;
    }
    
    private int FindNearestVertex(Vector2 worldPos, ProjectedFormation formation)
    {
        const float threshold = 100f; // World units
        float minDist = threshold;
        int nearestIndex = -1;
        
        // Check top boundary vertices
        for (int i = 0; i < formation.TopBoundary.Count; i++)
        {
            var dist = Vector2.Distance(worldPos, formation.TopBoundary[i]);
            if (dist < minDist)
            {
                minDist = dist;
                nearestIndex = i;
            }
        }
        
        // Check bottom boundary vertices
        for (int i = 0; i < formation.BottomBoundary.Count; i++)
        {
            var dist = Vector2.Distance(worldPos, formation.BottomBoundary[i]);
            if (dist < minDist)
            {
                minDist = dist;
                nearestIndex = formation.TopBoundary.Count + i;
            }
        }
        
        return nearestIndex;
    }
    
    private int FindNearestFaultVertex(Vector2 worldPos, ProjectedFault fault)
    {
        // Increase threshold based on zoom for easier selection
        var threshold = 50f / _zoom; 
        float minDistSq = threshold * threshold;
        int nearestIndex = -1;
        
        for (int i = 0; i < fault.FaultTrace.Count; i++)
        {
            var distSq = Vector2.DistanceSquared(worldPos, fault.FaultTrace[i]);
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                nearestIndex = i;
            }
        }
        
        return nearestIndex;
    }
    
    private (int segmentIndex, Vector2 closestPoint) FindNearestFaultSegment(Vector2 worldPos, ProjectedFault fault)
    {
        var threshold = 50f / _zoom;
        float minDistance = threshold;
        int nearestSegmentIndex = -1;
        Vector2 closestPointOnSegment = Vector2.Zero;

        for (int i = 0; i < fault.FaultTrace.Count - 1; i++)
        {
            var p1 = fault.FaultTrace[i];
            var p2 = fault.FaultTrace[i + 1];

            var dist = ProfileGenerator.DistanceToLineSegment(worldPos, p1, p2);

            if (dist < minDistance)
            {
                minDistance = dist;
                nearestSegmentIndex = i;

                // Find the actual closest point for insertion
                var ap = worldPos - p1;
                var ab = p2 - p1;
                var t = Math.Clamp(Vector2.Dot(ap, ab) / ab.LengthSquared(), 0f, 1f);
                closestPointOnSegment = p1 + ab * t;
            }
        }
        
        return (nearestSegmentIndex, closestPointOnSegment);
    }
    
    private void RenderPropertiesPanel()
    {
        if (_selectedFormation != null)
        {
            ImGui.Text($"Selected Formation: {_selectedFormation.Name}");
            ImGui.Separator();
            
            // Name
            var name = _selectedFormation.Name;
            if (ImGui.InputText("Name", ref name, 256))
            {
                _selectedFormation.Name = name;
            }
            
            // Color
            var color = _selectedFormation.Color;
            if (ImGui.ColorEdit4("Color", ref color))
            {
                _selectedFormation.Color = color;
            }
            
            // Fold style
            var hasFold = _selectedFormation.FoldStyle.HasValue;
            if (ImGui.Checkbox("Has Fold", ref hasFold))
            {
                _selectedFormation.FoldStyle = hasFold ? FoldStyle.Anticline : null;
            }
            
            if (_selectedFormation.FoldStyle.HasValue)
            {
                var foldStyle = _selectedFormation.FoldStyle.Value;
                if (ImGui.BeginCombo("Fold Type", foldStyle.ToString()))
                {
                    foreach (var style in Enum.GetValues<FoldStyle>())
                    {
                        if (ImGui.Selectable(style.ToString(), foldStyle == style))
                        {
                            _selectedFormation.FoldStyle = style;
                        }
                    }
                    ImGui.EndCombo();
                }
            }
            
            ImGui.Separator();
            ImGui.Text($"Top Boundary Points: {_selectedFormation.TopBoundary.Count}");
            ImGui.Text($"Bottom Boundary Points: {_selectedFormation.BottomBoundary.Count}");
        }
        else if (_selectedFault != null)
        {
            ImGui.Text($"Selected Fault");
            ImGui.Separator();
            
            // Fault type
            var faultType = _selectedFault.Type;
            if (ImGui.BeginCombo("Type", faultType.ToString().Replace("Fault_", "")))
            {
                foreach (var type in Enum.GetValues<GeologicalFeatureType>().Where(t => t.ToString().Contains("Fault")))
                {
                    if (ImGui.Selectable(type.ToString().Replace("Fault_", ""), faultType == type))
                    {
                        _selectedFault.Type = type;
                    }
                }
                ImGui.EndCombo();
            }
            
            // Dip
            var dip = _selectedFault.Dip;
            if (ImGui.SliderFloat("Dip", ref dip, 0f, 90f, "%.1f°"))
            {
                _selectedFault.Dip = dip;
            }
            
            // Dip direction
            var dipDir = _selectedFault.DipDirection ?? "";
            if (ImGui.InputText("Dip Direction", ref dipDir, 256))
            {
                _selectedFault.DipDirection = dipDir;
            }
            
            // Displacement
            var hasDisplacement = _selectedFault.Displacement.HasValue;
            if (ImGui.Checkbox("Has Displacement", ref hasDisplacement))
            {
                _selectedFault.Displacement = hasDisplacement ? 100f : null;
            }
            
            if (_selectedFault.Displacement.HasValue)
            {
                var displacement = _selectedFault.Displacement.Value;
                if (ImGui.InputFloat("Displacement (m)", ref displacement))
                {
                    _selectedFault.Displacement = displacement;
                }
            }
            
            ImGui.Separator();
            ImGui.Text($"Trace Points: {_selectedFault.FaultTrace.Count}");
        }
        else
        {
            ImGui.TextDisabled("No feature selected");
            ImGui.Separator();
            ImGui.TextWrapped("Click on a formation or fault to select it, or use the Layers Panel to manage features.");
        }
    }
    
    public Vector2 WorldToScreen(Vector2 worldPos, Vector2 screenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null || _zoom == 0) return screenPos;
        
        var profile = _crossSection.Profile;
        
        // Normalize world coordinates to [0, 1]
        float worldXMin = 0;
        float worldXMax = profile.TotalDistance;
        float worldYMin = profile.MinElevation;
        float worldYMax = profile.MaxElevation;
        
        float normX = (worldXMax - worldXMin) == 0 ? 0 : (worldPos.X - worldXMin) / (worldXMax - worldXMin);
        float normY = (worldYMax - worldYMin) == 0 ? 0 : (worldPos.Y - worldYMin) / (worldYMax - worldYMin);
        
        // Calculate the center of the viewport in normalized coordinates
        float centerX = 0.5f;
        float centerY = 0.5f;

        // Scale coordinates from the center
        normX = centerX + (normX - centerX) * _zoom;
        normY = centerY + (normY - centerY) * _zoom;
        
        // Apply vertical exaggeration
        normY *= _verticalExaggeration;
        
        // Scale to viewport size
        float viewX = normX * availSize.X;
        float viewY = normY * availSize.Y;
        
        // Apply pan offset (pan is applied after zoom)
        viewX += _panOffset.X;
        viewY += _panOffset.Y;

        // Invert Y-axis for screen coordinates and add viewport position
        return screenPos + new Vector2(viewX, availSize.Y - viewY);
    }
    
    public Vector2 ScreenToWorld(Vector2 localScreenPos, Vector2 availSize)
    {
        if (_crossSection.Profile == null || _zoom == 0) return Vector2.Zero;
        
        var profile = _crossSection.Profile;
        
        // Invert Y-axis from screen coordinates
        Vector2 viewPos = new Vector2(localScreenPos.X, availSize.Y - localScreenPos.Y);

        // Remove pan offset
        viewPos.X -= _panOffset.X;
        viewPos.Y -= _panOffset.Y;
        
        // Un-scale from viewport size
        float normX = viewPos.X / availSize.X;
        float normY = viewPos.Y / availSize.Y;

        // Remove vertical exaggeration
        if (_verticalExaggeration != 0)
        {
            normY /= _verticalExaggeration;
        }

        // Calculate center of viewport in normalized coords
        float centerX = 0.5f;
        float centerY = 0.5f;

        // Un-zoom from the center
        normX = centerX + (normX - centerX) / _zoom;
        normY = centerY + (normY - centerY) / _zoom;
        
        // Un-normalize to get world coordinates
        float worldX = normX * profile.TotalDistance;
        float worldY = normY * (profile.MaxElevation - profile.MinElevation) + profile.MinElevation;
        
        return new Vector2(worldX, worldY);
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