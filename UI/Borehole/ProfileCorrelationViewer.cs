// GeoscientistToolkit/UI/Borehole/ProfileCorrelationViewer.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Borehole;

/// <summary>
/// Multi-profile correlation viewer for managing correlation profiles and creating
/// cross-profile correlations. Provides both 2D profile view and map view.
/// </summary>
public class ProfileCorrelationViewer : IDisposable
{
    // Layout constants
    private const float DepthScaleWidth = 50f;
    private const float HeaderHeight = 90f;
    private const float ToolbarHeight = 35f;
    private const float MapPanelWidth = 300f;
    private const float ProfileListWidth = 200f;

    // Data
    private readonly List<BoreholeDataset> _boreholes = new();
    private readonly Dictionary<string, BoreholeDataset> _boreholeMap = new();
    private MultiProfileCorrelationDataset _correlationData;

    // Display settings
    private float _columnWidth = 100f;
    private float _columnSpacing = 60f;
    private float _depthScale = 3f;
    private float _zoom = 1f;

    // Selection state
    private CorrelationProfile _selectedProfile;
    private LithologyUnit _selectedUnit;
    private string _selectedBoreholeID;
    private LithologyUnit _pendingCorrelationSource;
    private string _pendingCorrelationSourceBorehole;
    private string _pendingCorrelationSourceProfile;
    private bool _isSelectingCorrelationTarget;
    private CorrelationProfile _targetProfile; // For cross-profile correlations

    // Profile creation
    private bool _isCreatingProfile;
    private string _newProfileName = "Profile 1";
    private List<string> _selectedBoreholeIDs = new();

    // UI state
    private bool _isOpen = true;
    private bool _isDragging;
    private Vector2 _lastMousePos;
    private string _statusMessage = "";
    private float _statusMessageTimer;

    // Map view
    private float _mapZoom = 1f;
    private Vector2 _mapPan = Vector2.Zero;
    private BoundingBox _mapBounds;
    private bool _showMapPanel = true;
    private bool _showProfilePanel = true;

    // View modes
    private enum ViewMode { SingleProfile, AllProfiles, MapOnly }
    private ViewMode _viewMode = ViewMode.SingleProfile;

    // Dialogs
    private bool _showProfileEditDialog;
    private bool _showExportDialog;
    private string _exportPath = "";

    // Colors
    private readonly Vector4 _backgroundColor = new(0.12f, 0.12f, 0.14f, 1.0f);
    private readonly Vector4 _headerBackgroundColor = new(0.18f, 0.18f, 0.22f, 1.0f);
    private readonly Vector4 _gridColor = new(0.25f, 0.25f, 0.28f, 0.5f);
    private readonly Vector4 _textColor = new(0.9f, 0.9f, 0.9f, 1.0f);
    private readonly Vector4 _mutedTextColor = new(0.6f, 0.6f, 0.6f, 1.0f);
    private readonly Vector4 _selectionColor = new(1.0f, 0.8f, 0.0f, 1.0f);
    private readonly Vector4 _pendingCorrelationColor = new(0.0f, 1.0f, 0.5f, 0.8f);
    private readonly Vector4 _crossCorrelationColor = new(0.9f, 0.5f, 0.2f, 0.9f);

    // Events
    public event Action OnClose;
    public event Action<MultiProfileCorrelationDataset> OnView3DRequested;

    public bool IsOpen => _isOpen;
    public MultiProfileCorrelationDataset CorrelationData => _correlationData;

    public ProfileCorrelationViewer(List<BoreholeDataset> boreholes)
    {
        InitializeFromBoreholes(boreholes);
    }

    public ProfileCorrelationViewer(DatasetGroup boreholeGroup)
    {
        var boreholeList = boreholeGroup.Datasets
            .OfType<BoreholeDataset>()
            .OrderBy(b => b.SurfaceCoordinates.X)
            .ThenBy(b => b.SurfaceCoordinates.Y)
            .ToList();

        InitializeFromBoreholes(boreholeList);
    }

    private void InitializeFromBoreholes(List<BoreholeDataset> boreholes)
    {
        _boreholes.Clear();
        _boreholeMap.Clear();

        foreach (var borehole in boreholes)
        {
            var id = borehole.FilePath ?? borehole.WellName ?? Guid.NewGuid().ToString();
            _boreholes.Add(borehole);
            _boreholeMap[id] = borehole;
        }

        _correlationData = new MultiProfileCorrelationDataset(
            $"MultiProfile_{DateTime.Now:yyyyMMdd_HHmmss}", "");

        // Initialize headers for all boreholes
        foreach (var borehole in _boreholes)
        {
            var id = GetBoreholeID(borehole);
            _correlationData.Headers[id] = new BoreholeHeader
            {
                BoreholeID = id,
                DisplayName = borehole.WellName ?? borehole.Name,
                Coordinates = borehole.SurfaceCoordinates,
                Elevation = borehole.Elevation,
                TotalDepth = borehole.TotalDepth,
                Field = borehole.Field
            };
        }

        CalculateMapBounds();
        Logger.Log($"[ProfileCorrelationViewer] Initialized with {_boreholes.Count} boreholes");
    }

    private string GetBoreholeID(BoreholeDataset borehole)
    {
        return borehole.FilePath ?? borehole.WellName ?? borehole.GetHashCode().ToString();
    }

    private void CalculateMapBounds()
    {
        if (_boreholes.Count == 0)
        {
            _mapBounds = new BoundingBox { Min = Vector2.Zero, Max = new Vector2(100, 100) };
            return;
        }

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach (var bh in _boreholes)
        {
            minX = Math.Min(minX, bh.SurfaceCoordinates.X);
            maxX = Math.Max(maxX, bh.SurfaceCoordinates.X);
            minY = Math.Min(minY, bh.SurfaceCoordinates.Y);
            maxY = Math.Max(maxY, bh.SurfaceCoordinates.Y);
        }

        float buffer = Math.Max(maxX - minX, maxY - minY) * 0.15f;
        _mapBounds = new BoundingBox
        {
            Min = new Vector2(minX - buffer, minY - buffer),
            Max = new Vector2(maxX + buffer, maxY + buffer)
        };
    }

    public void Draw()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(1400, 900), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Multi-Profile Correlation", ref _isOpen, ImGuiWindowFlags.MenuBar))
        {
            DrawMenuBar();
            DrawMainContent();
        }
        ImGui.End();

        DrawDialogs();

        if (!_isOpen)
            OnClose?.Invoke();
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Save Project...", "Ctrl+S"))
                    _showExportDialog = true;
                if (ImGui.MenuItem("Load Project...", "Ctrl+O"))
                    LoadProject();
                ImGui.Separator();
                if (ImGui.MenuItem("Export to GIS..."))
                    ExportToGIS();
                ImGui.Separator();
                if (ImGui.MenuItem("Close"))
                    _isOpen = false;
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Profiles"))
            {
                if (ImGui.MenuItem("Create New Profile...", "Ctrl+N"))
                    StartProfileCreation();
                if (ImGui.MenuItem("Delete Selected Profile", null, false, _selectedProfile != null))
                    DeleteSelectedProfile();
                ImGui.Separator();
                if (ImGui.MenuItem("Auto-Create Parallel Profiles"))
                    AutoCreateParallelProfiles();
                if (ImGui.MenuItem("Auto-Create Grid Profiles"))
                    AutoCreateGridProfiles();
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Correlate"))
            {
                if (ImGui.MenuItem("Auto-Correlate Within Profiles"))
                    AutoCorrelateWithinProfiles();
                if (ImGui.MenuItem("Auto-Correlate Across Profiles"))
                    AutoCorrelateAcrossProfiles();
                if (ImGui.MenuItem("Auto-Correlate All"))
                    _correlationData.AutoCorrelate(_boreholeMap);
                ImGui.Separator();
                if (ImGui.MenuItem("Build Horizons from Correlations"))
                {
                    _correlationData.BuildHorizons(_boreholeMap);
                    ShowStatus($"Built {_correlationData.Horizons.Count} horizons");
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Clear All Correlations"))
                {
                    _correlationData.IntraProfileCorrelations.Clear();
                    _correlationData.CrossProfileCorrelations.Clear();
                    ShowStatus("All correlations cleared");
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Single Profile", null, _viewMode == ViewMode.SingleProfile))
                    _viewMode = ViewMode.SingleProfile;
                if (ImGui.MenuItem("All Profiles", null, _viewMode == ViewMode.AllProfiles))
                    _viewMode = ViewMode.AllProfiles;
                if (ImGui.MenuItem("Map Only", null, _viewMode == ViewMode.MapOnly))
                    _viewMode = ViewMode.MapOnly;
                ImGui.Separator();
                ImGui.Checkbox("Show Map Panel", ref _showMapPanel);
                ImGui.Checkbox("Show Profile List", ref _showProfilePanel);
                ImGui.Separator();
                if (ImGui.MenuItem("Reset View"))
                {
                    _zoom = 1f;
                    _mapZoom = 1f;
                    _mapPan = Vector2.Zero;
                }
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("3D"))
            {
                if (ImGui.MenuItem("View 3D...", null, false, _correlationData.Profiles.Count > 0))
                {
                    _correlationData.BuildHorizons(_boreholeMap);
                    OnView3DRequested?.Invoke(_correlationData);
                }
                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
    }

    private void DrawMainContent()
    {
        var contentSize = ImGui.GetContentRegionAvail();

        // Left panel: Profile list
        if (_showProfilePanel)
        {
            ImGui.BeginChild("ProfileListPanel", new Vector2(ProfileListWidth, contentSize.Y), ImGuiChildFlags.Border);
            DrawProfileListPanel();
            ImGui.EndChild();
            ImGui.SameLine();
        }

        // Calculate remaining width
        float centerWidth = contentSize.X - (_showProfilePanel ? ProfileListWidth + 8 : 0) -
                           (_showMapPanel ? MapPanelWidth + 8 : 0);

        // Center: Profile view
        ImGui.BeginChild("ProfileViewPanel", new Vector2(centerWidth, contentSize.Y), ImGuiChildFlags.Border);
        DrawToolbar();
        DrawProfileView();
        DrawStatusBar();
        ImGui.EndChild();

        // Right panel: Map view
        if (_showMapPanel)
        {
            ImGui.SameLine();
            ImGui.BeginChild("MapPanel", new Vector2(MapPanelWidth, contentSize.Y), ImGuiChildFlags.Border);
            DrawMapPanel();
            ImGui.EndChild();
        }
    }

    private void DrawProfileListPanel()
    {
        ImGui.Text("Profiles");
        ImGui.Separator();

        // Add profile button
        if (ImGui.Button("+ New Profile", new Vector2(-1, 0)))
            StartProfileCreation();

        ImGui.Spacing();

        // Profile list
        foreach (var profile in _correlationData.Profiles)
        {
            var isSelected = _selectedProfile?.ID == profile.ID;
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
            if (isSelected) flags |= ImGuiTreeNodeFlags.Selected;

            ImGui.PushStyleColor(ImGuiCol.Text,
                new Vector4(profile.Color.X, profile.Color.Y, profile.Color.Z, 1));

            bool expanded = ImGui.TreeNodeEx(profile.ID, flags, profile.Name);

            ImGui.PopStyleColor();

            if (ImGui.IsItemClicked())
            {
                _selectedProfile = profile;
                _viewMode = ViewMode.SingleProfile;
            }

            // Context menu
            if (ImGui.BeginPopupContextItem())
            {
                if (ImGui.MenuItem("Edit..."))
                    _showProfileEditDialog = true;
                if (ImGui.MenuItem("Delete"))
                {
                    _correlationData.RemoveProfile(profile.ID);
                    if (_selectedProfile?.ID == profile.ID)
                        _selectedProfile = _correlationData.Profiles.FirstOrDefault();
                }
                ImGui.EndPopup();
            }

            if (expanded)
            {
                ImGui.Indent();
                ImGui.TextDisabled($"Boreholes: {profile.BoreholeOrder.Count}");
                ImGui.TextDisabled($"Azimuth: {profile.Azimuth:F1}");

                foreach (var bhID in profile.BoreholeOrder)
                {
                    if (_correlationData.Headers.TryGetValue(bhID, out var header))
                    {
                        ImGui.BulletText(header.DisplayName);
                    }
                }
                ImGui.Unindent();
                ImGui.TreePop();
            }
        }

        ImGui.Separator();

        // Summary
        ImGui.TextDisabled($"Intra-correlations: {_correlationData.IntraProfileCorrelations.Count}");
        ImGui.TextDisabled($"Cross-correlations: {_correlationData.CrossProfileCorrelations.Count}");
        ImGui.TextDisabled($"Horizons: {_correlationData.Horizons.Count}");
        ImGui.TextDisabled($"Intersections: {_correlationData.Intersections.Count}");

        // Profile creation mode
        if (_isCreatingProfile)
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0, 1, 0.5f, 1), "Creating Profile");

            ImGui.InputText("Name", ref _newProfileName, 128);
            ImGui.TextDisabled($"Selected: {_selectedBoreholeIDs.Count} boreholes");

            if (ImGui.Button("Create", new Vector2(60, 0)) && _selectedBoreholeIDs.Count >= 2)
            {
                CreateProfileFromSelection();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(60, 0)))
            {
                _isCreatingProfile = false;
                _selectedBoreholeIDs.Clear();
            }
        }
    }

    private void DrawToolbar()
    {
        ImGui.BeginChild("Toolbar", new Vector2(0, ToolbarHeight), ImGuiChildFlags.None);

        // View mode tabs
        if (ImGui.BeginTabBar("ViewModeTabs"))
        {
            if (ImGui.BeginTabItem("Profile View"))
            {
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 400);

        // Zoom
        ImGui.Text("Zoom:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.SliderFloat("##Zoom", ref _zoom, 0.5f, 4f, "%.1fx");

        ImGui.SameLine();

        // Column width
        ImGui.Text("Width:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(60);
        ImGui.SliderFloat("##ColWidth", ref _columnWidth, 60f, 150f, "%.0f");

        ImGui.SameLine();

        // Depth scale
        ImGui.Text("Scale:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(60);
        ImGui.SliderFloat("##DepthScale", ref _depthScale, 1f, 8f, "%.1f");

        // Correlation mode indicator
        if (_isSelectingCorrelationTarget)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0, 1, 0.5f, 1), "Click target lithology");
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel"))
            {
                CancelCorrelationSelection();
            }
        }

        ImGui.EndChild();
        ImGui.Separator();
    }

    private void DrawProfileView()
    {
        var contentSize = ImGui.GetContentRegionAvail();
        contentSize.Y -= 25; // Status bar

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        if (ImGui.BeginChild("ProfileContent", contentSize, ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar))
        {
            HandleProfileViewInput();

            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetCursorScreenPos();
            var scrollX = ImGui.GetScrollX();
            var scrollY = ImGui.GetScrollY();

            if (_selectedProfile != null && _viewMode == ViewMode.SingleProfile)
            {
                DrawSingleProfile(drawList, windowPos, scrollX, scrollY);
            }
            else if (_viewMode == ViewMode.AllProfiles && _correlationData.Profiles.Count > 0)
            {
                DrawAllProfiles(drawList, windowPos, scrollX, scrollY);
            }
            else
            {
                // No profile selected - show instructions
                var center = windowPos + contentSize / 2;
                drawList.AddText(center - new Vector2(100, 20), ImGui.GetColorU32(_mutedTextColor),
                    "No profile selected");
                drawList.AddText(center - new Vector2(120, 0), ImGui.GetColorU32(_mutedTextColor),
                    "Create a profile using the");
                drawList.AddText(center - new Vector2(100, -20), ImGui.GetColorU32(_mutedTextColor),
                    "Profiles menu or map view");
            }
        }
        ImGui.EndChild();

        ImGui.PopStyleVar();
    }

    private void DrawSingleProfile(ImDrawListPtr drawList, Vector2 windowPos, float scrollX, float scrollY)
    {
        var profile = _selectedProfile;
        if (profile == null || profile.BoreholeOrder.Count == 0) return;

        // Calculate dimensions
        float maxDepth = 0;
        foreach (var bhID in profile.BoreholeOrder)
        {
            if (_boreholeMap.TryGetValue(bhID, out var bh))
                maxDepth = Math.Max(maxDepth, bh.TotalDepth);
        }

        var totalWidth = DepthScaleWidth + profile.BoreholeOrder.Count * (_columnWidth + _columnSpacing) * _zoom + 100;
        var totalHeight = HeaderHeight + maxDepth * _depthScale * _zoom + 50;

        ImGui.Dummy(new Vector2(totalWidth, totalHeight));

        // Background
        drawList.AddRectFilled(windowPos, windowPos + ImGui.GetContentRegionAvail(),
            ImGui.GetColorU32(_backgroundColor));

        // Depth scale
        DrawDepthScale(drawList, windowPos, scrollX, scrollY, maxDepth);

        // Draw boreholes
        var columnX = windowPos.X + DepthScaleWidth - scrollX;
        for (int i = 0; i < profile.BoreholeOrder.Count; i++)
        {
            var bhID = profile.BoreholeOrder[i];
            if (!_boreholeMap.TryGetValue(bhID, out var borehole)) continue;

            DrawBoreholeColumn(drawList, borehole, bhID, profile, i,
                new Vector2(columnX, windowPos.Y - scrollY), maxDepth);

            columnX += (_columnWidth + _columnSpacing) * _zoom;
        }

        // Draw intra-profile correlations
        DrawIntraProfileCorrelations(drawList, profile, windowPos, scrollX, scrollY);

        // Draw pending correlation
        if (_isSelectingCorrelationTarget && _pendingCorrelationSourceProfile == profile.ID)
        {
            DrawPendingCorrelationLine(drawList, profile, windowPos, scrollX, scrollY);
        }
    }

    private void DrawAllProfiles(ImDrawListPtr drawList, Vector2 windowPos, float scrollX, float scrollY)
    {
        float yOffset = 0;
        float maxDepth = _boreholes.Max(b => b.TotalDepth);

        foreach (var profile in _correlationData.Profiles)
        {
            if (!profile.IsVisible) continue;

            // Profile header
            var headerY = windowPos.Y + yOffset - scrollY;
            var profileColor = new Vector4(profile.Color.X, profile.Color.Y, profile.Color.Z, 1);

            drawList.AddRectFilled(
                new Vector2(windowPos.X, headerY),
                new Vector2(windowPos.X + 200, headerY + 25),
                ImGui.GetColorU32(profileColor * 0.3f));

            drawList.AddText(new Vector2(windowPos.X + 10, headerY + 5),
                ImGui.GetColorU32(profileColor), profile.Name);

            yOffset += 30;

            // Boreholes
            var columnX = windowPos.X + DepthScaleWidth - scrollX;
            foreach (var bhID in profile.BoreholeOrder)
            {
                if (!_boreholeMap.TryGetValue(bhID, out var borehole)) continue;

                DrawBoreholeColumn(drawList, borehole, bhID, profile, 0,
                    new Vector2(columnX, windowPos.Y + yOffset - scrollY), maxDepth);

                columnX += (_columnWidth + _columnSpacing) * _zoom * 0.7f;
            }

            yOffset += HeaderHeight + maxDepth * _depthScale * _zoom * 0.5f + 20;
        }

        ImGui.Dummy(new Vector2(1000, yOffset));
    }

    private void DrawDepthScale(ImDrawListPtr drawList, Vector2 windowPos, float scrollX, float scrollY, float maxDepth)
    {
        var scaleX = windowPos.X;
        var scaleTop = windowPos.Y + HeaderHeight - scrollY;

        drawList.AddRectFilled(
            new Vector2(scaleX, windowPos.Y),
            new Vector2(scaleX + DepthScaleWidth, windowPos.Y + ImGui.GetContentRegionAvail().Y),
            ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1f)));

        var interval = GetAdaptiveGridInterval(_depthScale * _zoom);
        for (float depth = 0; depth <= maxDepth; depth += interval)
        {
            var y = scaleTop + depth * _depthScale * _zoom;

            drawList.AddLine(
                new Vector2(scaleX + DepthScaleWidth - 8, y),
                new Vector2(scaleX + DepthScaleWidth, y),
                ImGui.GetColorU32(_textColor));

            var label = $"{depth:0}";
            drawList.AddText(new Vector2(scaleX + 5, y - 7), ImGui.GetColorU32(_textColor), label);

            // Grid line
            var gridEnd = windowPos.X + 2000;
            drawList.AddLine(
                new Vector2(scaleX + DepthScaleWidth, y),
                new Vector2(gridEnd - scrollX, y),
                ImGui.GetColorU32(_gridColor));
        }
    }

    private void DrawBoreholeColumn(ImDrawListPtr drawList, BoreholeDataset borehole, string boreholeID,
        CorrelationProfile profile, int columnIndex, Vector2 columnPos, float maxDepth)
    {
        var header = _correlationData.Headers.GetValueOrDefault(boreholeID);
        var columnWidth = _columnWidth * _zoom;
        var headerTop = columnPos.Y;
        var lithologyTop = headerTop + HeaderHeight;

        // Header
        DrawColumnHeader(drawList, borehole, header, profile, new Vector2(columnPos.X, headerTop), columnWidth);

        // Lithology background
        var columnHeight = maxDepth * _depthScale * _zoom;
        drawList.AddRectFilled(
            new Vector2(columnPos.X, lithologyTop),
            new Vector2(columnPos.X + columnWidth, lithologyTop + columnHeight),
            ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.1f, 1f)));

        // Lithology units
        foreach (var unit in borehole.LithologyUnits)
        {
            var y1 = lithologyTop + unit.DepthFrom * _depthScale * _zoom;
            var y2 = lithologyTop + unit.DepthTo * _depthScale * _zoom;

            var unitRect = new Vector2(columnPos.X, y1);
            var unitSize = new Vector2(columnWidth, y2 - y1);

            DrawLithologyUnit(drawList, unit, boreholeID, profile, unitRect, unitSize);

            // Handle click
            var mouse = ImGui.GetMousePos();
            if (mouse.X >= unitRect.X && mouse.X <= unitRect.X + unitSize.X &&
                mouse.Y >= unitRect.Y && mouse.Y <= unitRect.Y + unitSize.Y)
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    HandleLithologyClick(unit, boreholeID, profile);

                // Tooltip
                DrawLithologyTooltip(unit, boreholeID, profile);
            }
        }

        // Column border
        drawList.AddRect(
            new Vector2(columnPos.X, lithologyTop),
            new Vector2(columnPos.X + columnWidth, lithologyTop + columnHeight),
            ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.35f, 1f)));
    }

    private void DrawColumnHeader(ImDrawListPtr drawList, BoreholeDataset borehole,
        BoreholeHeader header, CorrelationProfile profile, Vector2 pos, float width)
    {
        // Background with profile color tint
        var bgColor = _headerBackgroundColor;
        if (profile != null)
        {
            bgColor = new Vector4(
                _headerBackgroundColor.X + profile.Color.X * 0.1f,
                _headerBackgroundColor.Y + profile.Color.Y * 0.1f,
                _headerBackgroundColor.Z + profile.Color.Z * 0.1f,
                1f);
        }

        drawList.AddRectFilled(pos, pos + new Vector2(width, HeaderHeight), ImGui.GetColorU32(bgColor));
        drawList.AddRect(pos, pos + new Vector2(width, HeaderHeight),
            ImGui.GetColorU32(new Vector4(0.35f, 0.35f, 0.4f, 1f)));

        // Well name
        var wellName = header?.DisplayName ?? borehole.WellName ?? borehole.Name;
        if (wellName.Length > 12) wellName = wellName.Substring(0, 11) + "..";
        var nameSize = ImGui.CalcTextSize(wellName);
        drawList.AddText(new Vector2(pos.X + (width - nameSize.X) / 2, pos.Y + 5),
            ImGui.GetColorU32(_textColor), wellName);

        // Coordinates
        var coords = header?.Coordinates ?? borehole.SurfaceCoordinates;
        var coordText = $"{coords.X:F0}, {coords.Y:F0}";
        var coordSize = ImGui.CalcTextSize(coordText);
        if (coordSize.X < width - 4)
        {
            drawList.AddText(new Vector2(pos.X + (width - coordSize.X) / 2, pos.Y + 22),
                ImGui.GetColorU32(_mutedTextColor), coordText);
        }

        // Elevation
        var elevation = header?.Elevation ?? borehole.Elevation;
        var elevText = $"El: {elevation:F0}m";
        var elevSize = ImGui.CalcTextSize(elevText);
        drawList.AddText(new Vector2(pos.X + (width - elevSize.X) / 2, pos.Y + 38),
            ImGui.GetColorU32(_mutedTextColor), elevText);

        // Total depth
        var depth = header?.TotalDepth ?? borehole.TotalDepth;
        var depthText = $"TD: {depth:F0}m";
        var depthSize = ImGui.CalcTextSize(depthText);
        drawList.AddText(new Vector2(pos.X + (width - depthSize.X) / 2, pos.Y + 54),
            ImGui.GetColorU32(_mutedTextColor), depthText);
    }

    private void DrawLithologyUnit(ImDrawListPtr drawList, LithologyUnit unit, string boreholeID,
        CorrelationProfile profile, Vector2 pos, Vector2 size)
    {
        var isSelected = _selectedUnit?.ID == unit.ID;
        var isPendingSource = _pendingCorrelationSource?.ID == unit.ID;

        // Check if has correlations
        bool hasIntraCorr = _correlationData.IntraProfileCorrelations.Any(c =>
            c.SourceLithologyID == unit.ID || c.TargetLithologyID == unit.ID);
        bool hasCrossCorr = _correlationData.CrossProfileCorrelations.Any(c =>
            c.SourceLithologyID == unit.ID || c.TargetLithologyID == unit.ID);

        // Fill
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(unit.Color));

        // Simple pattern overlay
        DrawLithologyPattern(drawList, pos, size, unit.LithologyType, unit.Color);

        // Border
        var borderColor = isSelected ? _selectionColor :
                         isPendingSource ? _pendingCorrelationColor :
                         hasCrossCorr ? _crossCorrelationColor :
                         hasIntraCorr ? new Vector4(0.3f, 0.6f, 0.9f, 0.8f) :
                         new Vector4(0.2f, 0.2f, 0.2f, 1f);
        var borderThickness = isSelected || isPendingSource ? 3f : 1f;
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(borderColor), 0, ImDrawFlags.None, borderThickness);

        // Name (if space allows)
        if (size.Y > 16 && size.X > 30)
        {
            var name = unit.Name.Length > 10 ? unit.Name.Substring(0, 9) + ".." : unit.Name;
            var nameSize = ImGui.CalcTextSize(name);
            if (nameSize.X < size.X - 4)
            {
                drawList.AddText(pos + new Vector2(2, (size.Y - nameSize.Y) / 2),
                    ImGui.GetColorU32(new Vector4(1, 1, 1, 0.9f)), name);
            }
        }
    }

    private void DrawLithologyPattern(ImDrawListPtr drawList, Vector2 pos, Vector2 size, string lithologyType, Vector4 baseColor)
    {
        var patternColor = ImGui.GetColorU32(new Vector4(
            baseColor.X * 0.6f, baseColor.Y * 0.6f, baseColor.Z * 0.6f, 0.5f));

        switch (lithologyType?.ToLower())
        {
            case "sandstone":
            case "sand":
                for (float y = 3; y < size.Y; y += 6)
                    for (float x = 3; x < size.X; x += 6)
                        drawList.AddCircleFilled(pos + new Vector2(x, y), 1f, patternColor);
                break;

            case "shale":
            case "clay":
                for (float y = 3; y < size.Y; y += 5)
                    drawList.AddLine(pos + new Vector2(0, y), pos + new Vector2(size.X, y), patternColor);
                break;

            case "limestone":
                for (float y = 0; y < size.Y; y += 8)
                {
                    var offset = ((int)(y / 8) % 2) * 12;
                    for (float x = -12 + offset; x < size.X; x += 24)
                        drawList.AddRect(pos + new Vector2(x, y), pos + new Vector2(x + 22, y + 6), patternColor);
                }
                break;
        }
    }

    private void DrawLithologyTooltip(LithologyUnit unit, string boreholeID, CorrelationProfile profile)
    {
        ImGui.BeginTooltip();
        ImGui.Text(unit.Name);
        ImGui.TextDisabled($"Type: {unit.LithologyType}");
        ImGui.TextDisabled($"Depth: {unit.DepthFrom:F1}m - {unit.DepthTo:F1}m");
        ImGui.TextDisabled($"Thickness: {unit.DepthTo - unit.DepthFrom:F1}m");

        var intraCount = _correlationData.IntraProfileCorrelations.Count(c =>
            c.SourceLithologyID == unit.ID || c.TargetLithologyID == unit.ID);
        var crossCount = _correlationData.CrossProfileCorrelations.Count(c =>
            c.SourceLithologyID == unit.ID || c.TargetLithologyID == unit.ID);

        if (intraCount > 0)
            ImGui.TextDisabled($"Intra-profile correlations: {intraCount}");
        if (crossCount > 0)
            ImGui.TextColored(_crossCorrelationColor, $"Cross-profile correlations: {crossCount}");

        if (_isSelectingCorrelationTarget)
        {
            if (_pendingCorrelationSourceProfile == profile?.ID)
                ImGui.TextColored(new Vector4(0, 1, 0.5f, 1), "Click to correlate within profile");
            else
                ImGui.TextColored(_crossCorrelationColor, "Click to create cross-profile correlation");
        }
        else
        {
            ImGui.TextDisabled("Click to start correlation");
        }

        ImGui.EndTooltip();
    }

    private void DrawIntraProfileCorrelations(ImDrawListPtr drawList, CorrelationProfile profile,
        Vector2 windowPos, float scrollX, float scrollY)
    {
        var lithologyTop = windowPos.Y + HeaderHeight - scrollY;

        var profileCorrelations = _correlationData.IntraProfileCorrelations.Where(c =>
            profile.BoreholeOrder.Contains(c.SourceBoreholeID) &&
            profile.BoreholeOrder.Contains(c.TargetBoreholeID)).ToList();

        foreach (var correlation in profileCorrelations)
        {
            var sourceIndex = profile.BoreholeOrder.IndexOf(correlation.SourceBoreholeID);
            var targetIndex = profile.BoreholeOrder.IndexOf(correlation.TargetBoreholeID);

            if (sourceIndex < 0 || targetIndex < 0) continue;

            if (!_boreholeMap.TryGetValue(correlation.SourceBoreholeID, out var sourceBh) ||
                !_boreholeMap.TryGetValue(correlation.TargetBoreholeID, out var targetBh))
                continue;

            var sourceUnit = sourceBh.LithologyUnits.FirstOrDefault(u => u.ID == correlation.SourceLithologyID);
            var targetUnit = targetBh.LithologyUnits.FirstOrDefault(u => u.ID == correlation.TargetLithologyID);

            if (sourceUnit == null || targetUnit == null) continue;

            var sourceX = windowPos.X + DepthScaleWidth - scrollX + sourceIndex * (_columnWidth + _columnSpacing) * _zoom;
            var targetX = windowPos.X + DepthScaleWidth - scrollX + targetIndex * (_columnWidth + _columnSpacing) * _zoom;

            var sourceY = lithologyTop + (sourceUnit.DepthFrom + sourceUnit.DepthTo) / 2 * _depthScale * _zoom;
            var targetY = lithologyTop + (targetUnit.DepthFrom + targetUnit.DepthTo) / 2 * _depthScale * _zoom;

            if (targetIndex > sourceIndex)
                sourceX += _columnWidth * _zoom;
            else
                targetX += _columnWidth * _zoom;

            var lineColor = correlation.IsAutoCorrelated
                ? new Vector4(0.6f, 0.6f, 0.3f, 0.7f)
                : correlation.Color;

            // Bezier curve
            var midX = (sourceX + targetX) / 2;
            drawList.AddBezierCubic(
                new Vector2(sourceX, sourceY),
                new Vector2(midX, sourceY),
                new Vector2(midX, targetY),
                new Vector2(targetX, targetY),
                ImGui.GetColorU32(lineColor), 2f);

            // Endpoints
            drawList.AddCircleFilled(new Vector2(sourceX, sourceY), 4, ImGui.GetColorU32(lineColor));
            drawList.AddCircleFilled(new Vector2(targetX, targetY), 4, ImGui.GetColorU32(lineColor));
        }
    }

    private void DrawPendingCorrelationLine(ImDrawListPtr drawList, CorrelationProfile profile,
        Vector2 windowPos, float scrollX, float scrollY)
    {
        if (_pendingCorrelationSource == null) return;

        var sourceIndex = profile.BoreholeOrder.IndexOf(_pendingCorrelationSourceBorehole);
        if (sourceIndex < 0) return;

        var lithologyTop = windowPos.Y + HeaderHeight - scrollY;
        var sourceX = windowPos.X + DepthScaleWidth - scrollX +
                     sourceIndex * (_columnWidth + _columnSpacing) * _zoom + _columnWidth * _zoom / 2;
        var sourceY = lithologyTop + (_pendingCorrelationSource.DepthFrom + _pendingCorrelationSource.DepthTo) / 2 * _depthScale * _zoom;

        var mouse = ImGui.GetMousePos();

        // Determine if cross-profile
        var color = _targetProfile != null && _targetProfile.ID != profile.ID
            ? _crossCorrelationColor
            : _pendingCorrelationColor;

        drawList.AddLine(new Vector2(sourceX, sourceY), mouse, ImGui.GetColorU32(color), 2f);
        drawList.AddCircleFilled(new Vector2(sourceX, sourceY), 6, ImGui.GetColorU32(color));
    }

    private void DrawMapPanel()
    {
        ImGui.Text("Map View");
        ImGui.Separator();

        var mapSize = ImGui.GetContentRegionAvail();
        mapSize.Y -= 30; // Reserve space for controls

        if (mapSize.X < 10 || mapSize.Y < 10) return;

        var drawList = ImGui.GetWindowDrawList();
        var mapPos = ImGui.GetCursorScreenPos();

        // Background
        drawList.AddRectFilled(mapPos, mapPos + mapSize, ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.1f, 1f)));
        drawList.AddRect(mapPos, mapPos + mapSize, ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.35f, 1f)));

        // Calculate transform
        var mapCenter = _mapBounds.Center;
        var mapScale = Math.Min(mapSize.X / _mapBounds.Width, mapSize.Y / _mapBounds.Height) * 0.9f * _mapZoom;

        Func<Vector2, Vector2> worldToScreen = (world) =>
        {
            var centered = world - mapCenter + _mapPan;
            return mapPos + mapSize / 2 + new Vector2(centered.X * mapScale, -centered.Y * mapScale);
        };

        // Draw profile lines
        foreach (var profile in _correlationData.Profiles)
        {
            if (!profile.IsVisible) continue;

            var p1 = worldToScreen(profile.StartPoint);
            var p2 = worldToScreen(profile.EndPoint);

            var isSelected = _selectedProfile?.ID == profile.ID;
            var lineColor = isSelected ? _selectionColor : profile.Color;
            var lineWidth = isSelected ? 3f : 2f;

            drawList.AddLine(p1, p2, ImGui.GetColorU32(lineColor), lineWidth);

            // Profile name
            var mid = (p1 + p2) / 2;
            drawList.AddText(mid + new Vector2(5, -10), ImGui.GetColorU32(lineColor), profile.Name);
        }

        // Draw intersections
        foreach (var intersection in _correlationData.Intersections)
        {
            var screenPos = worldToScreen(intersection.IntersectionPoint);
            drawList.AddCircleFilled(screenPos, 5, ImGui.GetColorU32(new Vector4(1, 0.8f, 0.2f, 1)));
            drawList.AddCircle(screenPos, 5, ImGui.GetColorU32(new Vector4(1, 0.6f, 0, 1)), 0, 2);
        }

        // Draw boreholes
        foreach (var bh in _boreholes)
        {
            var screenPos = worldToScreen(bh.SurfaceCoordinates);
            var bhID = GetBoreholeID(bh);

            // Check if in any profile
            bool inProfile = _correlationData.Profiles.Any(p => p.BoreholeOrder.Contains(bhID));
            bool isSelectedForProfile = _selectedBoreholeIDs.Contains(bhID);

            var color = isSelectedForProfile ? new Vector4(0, 1, 0.5f, 1) :
                       inProfile ? new Vector4(0.3f, 0.7f, 1f, 1f) :
                       new Vector4(0.5f, 0.5f, 0.5f, 1f);

            drawList.AddCircleFilled(screenPos, 6, ImGui.GetColorU32(color));
            drawList.AddCircle(screenPos, 6, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.5f)));

            // Borehole name
            if (_mapZoom > 0.8f)
            {
                var name = bh.WellName ?? bh.Name;
                if (name.Length > 8) name = name.Substring(0, 7) + "..";
                drawList.AddText(screenPos + new Vector2(8, -7), ImGui.GetColorU32(_textColor), name);
            }

            // Handle click
            var mouse = ImGui.GetMousePos();
            var dist = Vector2.Distance(mouse, screenPos);
            if (dist < 8 && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (_isCreatingProfile)
                {
                    if (_selectedBoreholeIDs.Contains(bhID))
                        _selectedBoreholeIDs.Remove(bhID);
                    else
                        _selectedBoreholeIDs.Add(bhID);
                }
            }
        }

        ImGui.Dummy(mapSize);

        // Handle map pan
        if (ImGui.IsItemHovered())
        {
            var io = ImGui.GetIO();
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right))
            {
                _mapPan += io.MouseDelta / mapScale * new Vector2(1, -1);
            }
            if (io.MouseWheel != 0)
            {
                _mapZoom *= io.MouseWheel > 0 ? 1.1f : 0.9f;
                _mapZoom = Math.Clamp(_mapZoom, 0.2f, 5f);
            }
        }

        // Controls
        ImGui.Text($"Zoom: {_mapZoom:F1}x");
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset"))
        {
            _mapZoom = 1f;
            _mapPan = Vector2.Zero;
        }
    }

    private void DrawStatusBar()
    {
        ImGui.Separator();

        if (_statusMessageTimer > 0)
        {
            _statusMessageTimer -= ImGui.GetIO().DeltaTime;
            ImGui.TextColored(_statusMessage.StartsWith("Error") ? new Vector4(1, 0.3f, 0.3f, 1) : _textColor,
                _statusMessage);
        }
        else
        {
            var profileInfo = _selectedProfile != null
                ? $"Profile: {_selectedProfile.Name} ({_selectedProfile.BoreholeOrder.Count} boreholes)"
                : "No profile selected";
            ImGui.TextDisabled(profileInfo);
        }
    }

    private void DrawDialogs()
    {
        if (_showExportDialog)
        {
            ImGui.OpenPopup("Save Project");
            if (ImGui.BeginPopupModal("Save Project", ref _showExportDialog, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.Text("Save correlation project to file:");
                ImGui.SetNextItemWidth(400);
                ImGui.InputText("##Path", ref _exportPath, 512);

                if (ImGui.Button("Save", new Vector2(100, 0)) && !string.IsNullOrEmpty(_exportPath))
                {
                    _correlationData.SaveToFile(_exportPath);
                    ShowStatus($"Saved to {_exportPath}");
                    _showExportDialog = false;
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(100, 0)))
                    _showExportDialog = false;

                ImGui.EndPopup();
            }
        }
    }

    #region Input Handling

    private void HandleProfileViewInput()
    {
        var io = ImGui.GetIO();

        if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
        {
            if (!_isDragging)
            {
                _isDragging = true;
                _lastMousePos = io.MousePos;
            }

            var delta = io.MousePos - _lastMousePos;
            ImGui.SetScrollX(ImGui.GetScrollX() - delta.X);
            ImGui.SetScrollY(ImGui.GetScrollY() - delta.Y);
            _lastMousePos = io.MousePos;
        }
        else
        {
            _isDragging = false;
        }

        if (ImGui.IsWindowHovered() && io.MouseWheel != 0)
        {
            _zoom *= io.MouseWheel > 0 ? 1.1f : 0.9f;
            _zoom = Math.Clamp(_zoom, 0.5f, 4f);
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            if (_isSelectingCorrelationTarget)
                CancelCorrelationSelection();
            else if (_isCreatingProfile)
            {
                _isCreatingProfile = false;
                _selectedBoreholeIDs.Clear();
            }
        }
    }

    private void HandleLithologyClick(LithologyUnit unit, string boreholeID, CorrelationProfile profile)
    {
        if (_isSelectingCorrelationTarget)
        {
            // Create correlation
            if (_pendingCorrelationSource != null && _pendingCorrelationSourceBorehole != boreholeID)
            {
                bool success;
                if (_pendingCorrelationSourceProfile == profile?.ID)
                {
                    // Intra-profile correlation
                    success = _correlationData.AddIntraProfileCorrelation(
                        profile.ID,
                        _pendingCorrelationSource.ID, _pendingCorrelationSourceBorehole,
                        unit.ID, boreholeID);
                }
                else
                {
                    // Cross-profile correlation
                    success = _correlationData.AddCrossProfileCorrelation(
                        _pendingCorrelationSourceProfile, _pendingCorrelationSource.ID, _pendingCorrelationSourceBorehole,
                        profile?.ID, unit.ID, boreholeID);
                }

                if (success)
                    ShowStatus($"Correlation created between {_pendingCorrelationSource.Name} and {unit.Name}");
                else
                    ShowStatus("Cannot create correlation", true);
            }

            CancelCorrelationSelection();
        }
        else
        {
            // Start correlation
            _selectedUnit = unit;
            _selectedBoreholeID = boreholeID;
            _pendingCorrelationSource = unit;
            _pendingCorrelationSourceBorehole = boreholeID;
            _pendingCorrelationSourceProfile = profile?.ID;
            _isSelectingCorrelationTarget = true;
            _targetProfile = null;
        }
    }

    private void CancelCorrelationSelection()
    {
        _isSelectingCorrelationTarget = false;
        _pendingCorrelationSource = null;
        _pendingCorrelationSourceBorehole = null;
        _pendingCorrelationSourceProfile = null;
        _targetProfile = null;
    }

    #endregion

    #region Profile Creation

    private void StartProfileCreation()
    {
        _isCreatingProfile = true;
        _selectedBoreholeIDs.Clear();
        _newProfileName = $"Profile {_correlationData.Profiles.Count + 1}";
        ShowStatus("Click boreholes on map to add to profile");
    }

    private void CreateProfileFromSelection()
    {
        if (_selectedBoreholeIDs.Count < 2)
        {
            ShowStatus("Select at least 2 boreholes", true);
            return;
        }

        var profile = _correlationData.CreateProfile(_newProfileName, _selectedBoreholeIDs, _boreholeMap);
        _selectedProfile = profile;
        _isCreatingProfile = false;
        _selectedBoreholeIDs.Clear();

        ShowStatus($"Created profile '{profile.Name}' with {profile.BoreholeOrder.Count} boreholes");
    }

    private void DeleteSelectedProfile()
    {
        if (_selectedProfile == null) return;

        _correlationData.RemoveProfile(_selectedProfile.ID);
        _selectedProfile = _correlationData.Profiles.FirstOrDefault();
        ShowStatus("Profile deleted");
    }

    private void AutoCreateParallelProfiles()
    {
        if (_boreholes.Count < 4)
        {
            ShowStatus("Need at least 4 boreholes for parallel profiles", true);
            return;
        }

        // Group boreholes by Y coordinate (approximate rows)
        var grouped = _boreholes
            .GroupBy(b => Math.Round(b.SurfaceCoordinates.Y / 50) * 50)
            .Where(g => g.Count() >= 2)
            .OrderBy(g => g.Key)
            .ToList();

        int profileCount = 0;
        foreach (var group in grouped)
        {
            var boreholeIDs = group
                .OrderBy(b => b.SurfaceCoordinates.X)
                .Select(b => GetBoreholeID(b))
                .ToList();

            if (boreholeIDs.Count >= 2)
            {
                _correlationData.CreateProfile($"Row {profileCount + 1}", boreholeIDs, _boreholeMap);
                profileCount++;
            }
        }

        if (profileCount > 0)
        {
            _selectedProfile = _correlationData.Profiles.FirstOrDefault();
            ShowStatus($"Created {profileCount} parallel profiles");
        }
        else
        {
            ShowStatus("Could not create parallel profiles", true);
        }
    }

    private void AutoCreateGridProfiles()
    {
        if (_boreholes.Count < 4)
        {
            ShowStatus("Need at least 4 boreholes for grid profiles", true);
            return;
        }

        // Create E-W profiles (group by Y)
        var ewGrouped = _boreholes
            .GroupBy(b => Math.Round(b.SurfaceCoordinates.Y / 100) * 100)
            .Where(g => g.Count() >= 2)
            .OrderBy(g => g.Key);

        int ewCount = 0;
        foreach (var group in ewGrouped)
        {
            var boreholeIDs = group.OrderBy(b => b.SurfaceCoordinates.X).Select(b => GetBoreholeID(b)).ToList();
            if (boreholeIDs.Count >= 2)
            {
                _correlationData.CreateProfile($"EW-{ewCount + 1}", boreholeIDs, _boreholeMap);
                ewCount++;
            }
        }

        // Create N-S profiles (group by X)
        var nsGrouped = _boreholes
            .GroupBy(b => Math.Round(b.SurfaceCoordinates.X / 100) * 100)
            .Where(g => g.Count() >= 2)
            .OrderBy(g => g.Key);

        int nsCount = 0;
        foreach (var group in nsGrouped)
        {
            var boreholeIDs = group.OrderBy(b => b.SurfaceCoordinates.Y).Select(b => GetBoreholeID(b)).ToList();
            if (boreholeIDs.Count >= 2)
            {
                _correlationData.CreateProfile($"NS-{nsCount + 1}", boreholeIDs, _boreholeMap);
                nsCount++;
            }
        }

        _selectedProfile = _correlationData.Profiles.FirstOrDefault();
        ShowStatus($"Created {ewCount} E-W and {nsCount} N-S profiles");
    }

    #endregion

    #region Correlation Actions

    private void AutoCorrelateWithinProfiles()
    {
        int count = 0;
        foreach (var profile in _correlationData.Profiles)
        {
            for (int i = 0; i < profile.BoreholeOrder.Count - 1; i++)
            {
                var bhID1 = profile.BoreholeOrder[i];
                var bhID2 = profile.BoreholeOrder[i + 1];

                if (!_boreholeMap.TryGetValue(bhID1, out var bh1) ||
                    !_boreholeMap.TryGetValue(bhID2, out var bh2))
                    continue;

                foreach (var unit1 in bh1.LithologyUnits)
                {
                    var matches = bh2.LithologyUnits
                        .Where(u => u.LithologyType == unit1.LithologyType)
                        .OrderBy(u => Math.Abs((u.DepthFrom + u.DepthTo) / 2 - (unit1.DepthFrom + unit1.DepthTo) / 2))
                        .ToList();

                    foreach (var unit2 in matches)
                    {
                        if (_correlationData.AddIntraProfileCorrelation(profile.ID, unit1.ID, bhID1, unit2.ID, bhID2, 0.8f, true))
                        {
                            count++;
                            break;
                        }
                    }
                }
            }
        }

        ShowStatus($"Added {count} intra-profile correlations");
    }

    private void AutoCorrelateAcrossProfiles()
    {
        int count = _correlationData.AutoCorrelate(_boreholeMap) -
                   _correlationData.IntraProfileCorrelations.Count(c => c.IsAutoCorrelated);
        ShowStatus($"Added {Math.Max(0, count)} cross-profile correlations");
    }

    #endregion

    #region File Operations

    private void LoadProject()
    {
        ShowStatus("Load project: use File menu from main application");
    }

    private void ExportToGIS()
    {
        try
        {
            _correlationData.BuildHorizons(_boreholeMap);

            if (_correlationData.Horizons.Count == 0)
            {
                ShowStatus("No horizons to export. Build horizons first.", true);
                return;
            }

            var gisDataset = new GISDataset($"Horizons_{_correlationData.Name}", "");

            foreach (var horizon in _correlationData.Horizons)
            {
                var layer = new GISVectorLayer(horizon.Name);

                foreach (var cp in horizon.ControlPoints)
                {
                    var feature = new GISFeature
                    {
                        Type = GISFeatureType.Point
                    };
                    feature.Points.Add(new Vector2(cp.Position.X, cp.Position.Y));
                    feature.Properties["elevation"] = cp.Position.Z.ToString("F2");
                    feature.Properties["horizon"] = horizon.Name;
                    layer.Features.Add(feature);
                }

                gisDataset.Layers.Add(layer);
            }

            ProjectManager.Instance.AddDataset(gisDataset);
            ShowStatus($"Exported {_correlationData.Horizons.Count} horizons to GIS");
        }
        catch (Exception ex)
        {
            ShowStatus($"Export failed: {ex.Message}", true);
        }
    }

    #endregion

    private void ShowStatus(string message, bool isError = false)
    {
        _statusMessage = isError ? $"Error: {message}" : message;
        _statusMessageTimer = 4f;
    }

    private float GetAdaptiveGridInterval(float pixelsPerMeter)
    {
        if (pixelsPerMeter <= 0) return 100f;
        var targetPixels = 50f;
        var interval = targetPixels / pixelsPerMeter;
        var pow10 = Math.Pow(10, Math.Floor(Math.Log10(interval)));
        var normalized = interval / pow10;

        if (normalized < 1.5) return (float)(1 * pow10);
        if (normalized < 3.5) return (float)(2 * pow10);
        if (normalized < 7.5) return (float)(5 * pow10);
        return (float)(10 * pow10);
    }

    public void Dispose()
    {
        Logger.Log("[ProfileCorrelationViewer] Disposed");
    }
}
