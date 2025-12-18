// GeoscientistToolkit/UI/Borehole/BoreholeLogCorrelationViewer.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;

namespace GeoscientistToolkit.UI.Borehole;

/// <summary>
/// Viewer for correlating lithology logs across multiple boreholes.
/// Displays logs aligned at depth 0, allows clicking to create correlations,
/// and supports auto-correlation, PNG export, and 3D visualization.
/// </summary>
public class BoreholeLogCorrelationViewer : IDisposable
{
    // Layout constants
    private const float DepthScaleWidth = 60f;
    private const float HeaderHeight = 100f;
    private const float ToolbarHeight = 40f;
    private const float MinColumnWidth = 80f;
    private const float MaxColumnWidth = 200f;

    // Borehole data
    private readonly List<BoreholeDataset> _boreholes = new();
    private readonly Dictionary<string, BoreholeDataset> _boreholeMap = new();
    private BoreholeLogCorrelationDataset _correlationData;

    // Display settings
    private float _columnWidth = 120f;
    private float _columnSpacing = 80f;
    private float _depthScale = 3f; // pixels per meter
    private float _zoom = 1f;
    private Vector2 _pan = Vector2.Zero;
    private bool _showCorrelationLines = true;
    private bool _showLithologyNames = true;
    private bool _showDepthScale = true;
    private bool _showGrid = true;
    private bool _alignToZero = true;

    // Selection state
    private LithologyUnit _selectedUnit;
    private string _selectedBoreholeID;
    private LithologyUnit _pendingCorrelationSource;
    private string _pendingCorrelationSourceBorehole;
    private bool _isSelectingCorrelationTarget;

    // UI state
    private bool _isOpen = true;
    private bool _isDragging;
    private Vector2 _lastMousePos;
    private string _statusMessage = "";
    private float _statusMessageTimer;

    // Dialogs
    private bool _showHeaderEditDialog;
    private string _editingBoreholeID;
    private BoreholeHeader _editingHeader = new();
    private bool _showSaveDialog;
    private bool _showView3DDialog;
    private string _saveFilePath = "";

    // Colors
    private readonly Vector4 _backgroundColor = new(0.12f, 0.12f, 0.14f, 1.0f);
    private readonly Vector4 _headerBackgroundColor = new(0.18f, 0.18f, 0.22f, 1.0f);
    private readonly Vector4 _gridColor = new(0.25f, 0.25f, 0.28f, 0.5f);
    private readonly Vector4 _textColor = new(0.9f, 0.9f, 0.9f, 1.0f);
    private readonly Vector4 _mutedTextColor = new(0.6f, 0.6f, 0.6f, 1.0f);
    private readonly Vector4 _selectionColor = new(1.0f, 0.8f, 0.0f, 1.0f);
    private readonly Vector4 _pendingCorrelationColor = new(0.0f, 1.0f, 0.5f, 0.8f);
    private readonly Vector4 _correlationLineColor = new(0.3f, 0.6f, 0.9f, 0.8f);

    // Events
    public event Action OnClose;
    public event Action<BoreholeLogCorrelationDataset> OnView3DRequested;

    public bool IsOpen => _isOpen;

    public BoreholeLogCorrelationViewer(DatasetGroup boreholeGroup)
    {
        InitializeFromGroup(boreholeGroup);
    }

    public BoreholeLogCorrelationViewer(List<BoreholeDataset> boreholes)
    {
        InitializeFromBoreholes(boreholes);
    }

    private void InitializeFromGroup(DatasetGroup group)
    {
        var boreholeList = group.Datasets
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

        // Create correlation dataset
        _correlationData = new BoreholeLogCorrelationDataset(
            $"Correlation_{DateTime.Now:yyyyMMdd_HHmmss}",
            "");

        // Initialize borehole order and headers
        int index = 0;
        foreach (var borehole in _boreholes)
        {
            var id = GetBoreholeID(borehole);
            _correlationData.BoreholeOrder.Add(id);
            _correlationData.Headers[id] = new BoreholeHeader
            {
                BoreholeID = id,
                DisplayName = borehole.WellName ?? borehole.Name,
                Coordinates = borehole.SurfaceCoordinates,
                Elevation = borehole.Elevation,
                TotalDepth = borehole.TotalDepth,
                PositionIndex = index++,
                Field = borehole.Field
            };
        }

        Logger.Log($"[BoreholeLogCorrelationViewer] Initialized with {_boreholes.Count} boreholes");
    }

    private string GetBoreholeID(BoreholeDataset borehole)
    {
        return borehole.FilePath ?? borehole.WellName ?? borehole.GetHashCode().ToString();
    }

    public void Draw()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(1200, 800), ImGuiCond.FirstUseEver);

        var flags = ImGuiWindowFlags.MenuBar;
        if (ImGui.Begin("Borehole Log Correlation", ref _isOpen, flags))
        {
            DrawMenuBar();
            DrawToolbar();
            DrawContent();
            DrawStatusBar();
        }
        ImGui.End();

        // Draw dialogs
        DrawHeaderEditDialog();
        DrawSaveDialog();

        if (!_isOpen)
            OnClose?.Invoke();
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                if (ImGui.MenuItem("Save Correlations...", "Ctrl+S"))
                    _showSaveDialog = true;

                if (ImGui.MenuItem("Load Correlations...", "Ctrl+O"))
                    LoadCorrelations();

                ImGui.Separator();

                if (ImGui.MenuItem("Export PNG...", "Ctrl+E"))
                    ExportToPNG();

                ImGui.Separator();

                if (ImGui.MenuItem("Close", "Esc"))
                    _isOpen = false;

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Edit"))
            {
                if (ImGui.MenuItem("Clear All Correlations"))
                {
                    _correlationData.Correlations.Clear();
                    ShowStatus("All correlations cleared");
                }

                if (ImGui.MenuItem("Remove Selected Correlation", null, false, _selectedUnit != null))
                {
                    if (_selectedUnit != null)
                    {
                        _correlationData.RemoveCorrelationsForUnit(_selectedUnit.ID);
                        ShowStatus("Correlations removed for selected unit");
                    }
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("View"))
            {
                ImGui.Checkbox("Show Correlation Lines", ref _showCorrelationLines);
                ImGui.Checkbox("Show Lithology Names", ref _showLithologyNames);
                ImGui.Checkbox("Show Depth Scale", ref _showDepthScale);
                ImGui.Checkbox("Show Grid", ref _showGrid);
                ImGui.Checkbox("Align to Zero", ref _alignToZero);

                ImGui.Separator();

                if (ImGui.MenuItem("Reset Zoom"))
                {
                    _zoom = 1f;
                    _pan = Vector2.Zero;
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Correlate"))
            {
                if (ImGui.MenuItem("Auto-Correlate by Lithology Type"))
                    AutoCorrelateByLithologyType();

                if (ImGui.MenuItem("Auto-Correlate by Depth Similarity"))
                    AutoCorrelateByDepth();

                if (ImGui.MenuItem("Auto-Correlate (Combined)"))
                    AutoCorrelateCombined();

                ImGui.Separator();

                if (ImGui.MenuItem("Build Horizons from Correlations"))
                {
                    _correlationData.BuildHorizonsFromCorrelations(_boreholeMap);
                    ShowStatus($"Built {_correlationData.Horizons.Count} horizons");
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("3D"))
            {
                if (ImGui.MenuItem("View 3D Subsurface Map...", null, false, _correlationData.Correlations.Count > 0))
                {
                    _correlationData.BuildHorizonsFromCorrelations(_boreholeMap);
                    OnView3DRequested?.Invoke(_correlationData);
                }

                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
    }

    private void DrawToolbar()
    {
        var toolbarSize = new Vector2(ImGui.GetContentRegionAvail().X, ToolbarHeight);
        ImGui.BeginChild("Toolbar", toolbarSize, ImGuiChildFlags.None);

        // Zoom controls
        ImGui.Text("Zoom:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("##Zoom", ref _zoom, 0.5f, 5f, "%.1fx");

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        // Column width
        ImGui.Text("Column Width:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.SliderFloat("##ColWidth", ref _columnWidth, MinColumnWidth, MaxColumnWidth, "%.0f");

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        // Depth scale
        ImGui.Text("Depth Scale:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.SliderFloat("##DepthScale", ref _depthScale, 1f, 10f, "%.1f px/m");

        ImGui.SameLine();
        ImGui.Spacing();
        ImGui.SameLine();

        // Correlation mode indicator
        if (_isSelectingCorrelationTarget)
        {
            ImGui.TextColored(new Vector4(0, 1, 0.5f, 1), "Click on a lithology in an adjacent log to correlate");
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _isSelectingCorrelationTarget = false;
                _pendingCorrelationSource = null;
                _pendingCorrelationSourceBorehole = null;
            }
        }
        else
        {
            ImGui.TextDisabled($"Correlations: {_correlationData.Correlations.Count}");
        }

        ImGui.EndChild();
        ImGui.Separator();
    }

    private void DrawContent()
    {
        var contentSize = ImGui.GetContentRegionAvail();
        contentSize.Y -= 25; // Reserve space for status bar

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        if (ImGui.BeginChild("CorrelationContent", contentSize, ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar))
        {
            HandleInput();

            var drawList = ImGui.GetWindowDrawList();
            var windowPos = ImGui.GetCursorScreenPos();
            var scrollX = ImGui.GetScrollX();
            var scrollY = ImGui.GetScrollY();

            // Calculate content dimensions
            var totalWidth = DepthScaleWidth + _boreholes.Count * (_columnWidth + _columnSpacing) * _zoom;
            var maxDepth = _boreholes.Max(b => b.TotalDepth);
            var totalHeight = HeaderHeight + maxDepth * _depthScale * _zoom + 50;

            // Reserve space for scrolling
            ImGui.Dummy(new Vector2(totalWidth, totalHeight));

            // Draw background
            var bgMin = windowPos;
            var bgMax = windowPos + contentSize;
            drawList.AddRectFilled(bgMin, bgMax, ImGui.GetColorU32(_backgroundColor));

            // Draw depth scale
            if (_showDepthScale)
                DrawDepthScale(drawList, windowPos, scrollX, scrollY, maxDepth);

            // Draw each borehole column
            var columnX = windowPos.X + DepthScaleWidth - scrollX;
            for (int i = 0; i < _boreholes.Count; i++)
            {
                var borehole = _boreholes[i];
                var boreholeID = GetBoreholeID(borehole);

                DrawBoreholeColumn(drawList, borehole, boreholeID, i,
                    new Vector2(columnX, windowPos.Y - scrollY), maxDepth);

                columnX += (_columnWidth + _columnSpacing) * _zoom;
            }

            // Draw correlation lines
            if (_showCorrelationLines)
                DrawCorrelationLines(drawList, windowPos, scrollX, scrollY);

            // Draw pending correlation line
            if (_isSelectingCorrelationTarget && _pendingCorrelationSource != null)
                DrawPendingCorrelationLine(drawList, windowPos, scrollX, scrollY);
        }
        ImGui.EndChild();

        ImGui.PopStyleVar();
    }

    private void DrawDepthScale(ImDrawListPtr drawList, Vector2 windowPos, float scrollX, float scrollY, float maxDepth)
    {
        var scaleX = windowPos.X;
        var scaleTop = windowPos.Y + HeaderHeight - scrollY;

        // Background
        drawList.AddRectFilled(
            new Vector2(scaleX, windowPos.Y),
            new Vector2(scaleX + DepthScaleWidth, windowPos.Y + ImGui.GetContentRegionAvail().Y),
            ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.12f, 1f)));

        // Depth labels and grid
        var interval = GetAdaptiveGridInterval(_depthScale * _zoom);
        for (float depth = 0; depth <= maxDepth; depth += interval)
        {
            var y = scaleTop + depth * _depthScale * _zoom;

            // Tick mark
            drawList.AddLine(
                new Vector2(scaleX + DepthScaleWidth - 10, y),
                new Vector2(scaleX + DepthScaleWidth, y),
                ImGui.GetColorU32(_textColor));

            // Label
            var label = $"{depth:0}m";
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(
                new Vector2(scaleX + DepthScaleWidth - textSize.X - 12, y - textSize.Y / 2),
                ImGui.GetColorU32(_textColor), label);

            // Grid line across all columns
            if (_showGrid)
            {
                var gridEnd = windowPos.X + DepthScaleWidth + _boreholes.Count * (_columnWidth + _columnSpacing) * _zoom;
                drawList.AddLine(
                    new Vector2(scaleX + DepthScaleWidth, y),
                    new Vector2(gridEnd - scrollX, y),
                    ImGui.GetColorU32(_gridColor));
            }
        }
    }

    private void DrawBoreholeColumn(ImDrawListPtr drawList, BoreholeDataset borehole, string boreholeID,
        int columnIndex, Vector2 columnPos, float maxDepth)
    {
        var header = _correlationData.Headers.GetValueOrDefault(boreholeID);
        var columnWidth = _columnWidth * _zoom;
        var headerTop = columnPos.Y;
        var lithologyTop = headerTop + HeaderHeight;

        // Draw header
        DrawColumnHeader(drawList, borehole, header, new Vector2(columnPos.X, headerTop), columnWidth);

        // Draw lithology column background
        var columnHeight = maxDepth * _depthScale * _zoom;
        drawList.AddRectFilled(
            new Vector2(columnPos.X, lithologyTop),
            new Vector2(columnPos.X + columnWidth, lithologyTop + columnHeight),
            ImGui.GetColorU32(new Vector4(0.08f, 0.08f, 0.1f, 1f)));

        // Draw each lithology unit
        foreach (var unit in borehole.LithologyUnits)
        {
            var y1 = lithologyTop + unit.DepthFrom * _depthScale * _zoom;
            var y2 = lithologyTop + unit.DepthTo * _depthScale * _zoom;

            if (y2 < columnPos.Y || y1 > columnPos.Y + ImGui.GetContentRegionAvail().Y + HeaderHeight)
                continue; // Skip off-screen units

            var unitRect = new Vector2(columnPos.X, y1);
            var unitSize = new Vector2(columnWidth, y2 - y1);

            // Draw unit
            DrawLithologyUnit(drawList, unit, boreholeID, unitRect, unitSize);

            // Handle click
            var mouse = ImGui.GetMousePos();
            if (mouse.X >= unitRect.X && mouse.X <= unitRect.X + unitSize.X &&
                mouse.Y >= unitRect.Y && mouse.Y <= unitRect.Y + unitSize.Y)
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    HandleLithologyClick(unit, boreholeID);

                // Tooltip
                ImGui.BeginTooltip();
                ImGui.Text(unit.Name);
                ImGui.TextDisabled($"Type: {unit.LithologyType}");
                ImGui.TextDisabled($"Depth: {unit.DepthFrom:F1}m - {unit.DepthTo:F1}m");
                ImGui.TextDisabled($"Thickness: {unit.DepthTo - unit.DepthFrom:F1}m");

                var correlations = _correlationData.GetCorrelationsForUnit(unit.ID);
                if (correlations.Count > 0)
                    ImGui.TextDisabled($"Correlations: {correlations.Count}");

                if (_isSelectingCorrelationTarget)
                    ImGui.TextColored(new Vector4(0, 1, 0.5f, 1), "Click to correlate");
                else
                    ImGui.TextDisabled("Click to select, then click adjacent log to correlate");

                ImGui.EndTooltip();
            }
        }

        // Column border
        drawList.AddRect(
            new Vector2(columnPos.X, lithologyTop),
            new Vector2(columnPos.X + columnWidth, lithologyTop + columnHeight),
            ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.35f, 1f)));
    }

    private void DrawColumnHeader(ImDrawListPtr drawList, BoreholeDataset borehole,
        BoreholeHeader header, Vector2 pos, float width)
    {
        var headerHeight = HeaderHeight;

        // Background
        drawList.AddRectFilled(pos, pos + new Vector2(width, headerHeight),
            ImGui.GetColorU32(_headerBackgroundColor));

        // Border
        drawList.AddRect(pos, pos + new Vector2(width, headerHeight),
            ImGui.GetColorU32(new Vector4(0.35f, 0.35f, 0.4f, 1f)));

        // Well name
        var wellName = header?.DisplayName ?? borehole.WellName ?? borehole.Name;
        var nameSize = ImGui.CalcTextSize(wellName);
        var nameX = pos.X + (width - nameSize.X) / 2;
        drawList.AddText(new Vector2(nameX, pos.Y + 5), ImGui.GetColorU32(_textColor), wellName);

        // Coordinates
        var coords = header?.Coordinates ?? borehole.SurfaceCoordinates;
        var coordText = $"X: {coords.X:F0}  Y: {coords.Y:F0}";
        var coordSize = ImGui.CalcTextSize(coordText);
        drawList.AddText(new Vector2(pos.X + (width - coordSize.X) / 2, pos.Y + 25),
            ImGui.GetColorU32(_mutedTextColor), coordText);

        // Elevation
        var elevation = header?.Elevation ?? borehole.Elevation;
        var elevText = $"Elev: {elevation:F1}m";
        var elevSize = ImGui.CalcTextSize(elevText);
        drawList.AddText(new Vector2(pos.X + (width - elevSize.X) / 2, pos.Y + 42),
            ImGui.GetColorU32(_mutedTextColor), elevText);

        // Total depth
        var depth = header?.TotalDepth ?? borehole.TotalDepth;
        var depthText = $"TD: {depth:F1}m";
        var depthSize = ImGui.CalcTextSize(depthText);
        drawList.AddText(new Vector2(pos.X + (width - depthSize.X) / 2, pos.Y + 59),
            ImGui.GetColorU32(_mutedTextColor), depthText);

        // Custom label if any
        if (!string.IsNullOrEmpty(header?.CustomLabel))
        {
            var labelSize = ImGui.CalcTextSize(header.CustomLabel);
            drawList.AddText(new Vector2(pos.X + (width - labelSize.X) / 2, pos.Y + 76),
                ImGui.GetColorU32(new Vector4(0.5f, 0.8f, 1f, 1f)), header.CustomLabel);
        }

        // Edit button (invisible, but clickable area)
        var mouse = ImGui.GetMousePos();
        if (mouse.X >= pos.X && mouse.X <= pos.X + width &&
            mouse.Y >= pos.Y && mouse.Y <= pos.Y + headerHeight)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _editingBoreholeID = GetBoreholeID(borehole);
                _editingHeader = new BoreholeHeader
                {
                    BoreholeID = _editingBoreholeID,
                    DisplayName = header?.DisplayName ?? borehole.WellName,
                    Coordinates = header?.Coordinates ?? borehole.SurfaceCoordinates,
                    Elevation = header?.Elevation ?? borehole.Elevation,
                    TotalDepth = header?.TotalDepth ?? borehole.TotalDepth,
                    Field = header?.Field ?? borehole.Field,
                    CustomLabel = header?.CustomLabel ?? ""
                };
                _showHeaderEditDialog = true;
            }
        }
    }

    private void DrawLithologyUnit(ImDrawListPtr drawList, LithologyUnit unit, string boreholeID,
        Vector2 pos, Vector2 size)
    {
        var isSelected = _selectedUnit?.ID == unit.ID;
        var isPendingSource = _pendingCorrelationSource?.ID == unit.ID;
        var hasCorrelation = _correlationData.GetCorrelationsForUnit(unit.ID).Count > 0;

        // Unit fill color
        var fillColor = unit.Color;
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(fillColor));

        // Draw lithology pattern
        DrawLithologyPattern(drawList, pos, size, unit.LithologyType, fillColor);

        // Unit border
        var borderColor = isSelected ? _selectionColor :
                         isPendingSource ? _pendingCorrelationColor :
                         hasCorrelation ? _correlationLineColor :
                         new Vector4(0.2f, 0.2f, 0.2f, 1f);
        var borderThickness = isSelected || isPendingSource ? 3f : 1f;
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(borderColor), 0, ImDrawFlags.None, borderThickness);

        // Unit name
        if (_showLithologyNames && size.Y > 20)
        {
            var name = unit.Name;
            var nameSize = ImGui.CalcTextSize(name);
            if (nameSize.X < size.X - 4 && nameSize.Y < size.Y - 4)
            {
                // Shadow for readability
                drawList.AddText(pos + new Vector2((size.X - nameSize.X) / 2 + 1, (size.Y - nameSize.Y) / 2 + 1),
                    ImGui.GetColorU32(new Vector4(0, 0, 0, 0.7f)), name);
                drawList.AddText(pos + new Vector2((size.X - nameSize.X) / 2, (size.Y - nameSize.Y) / 2),
                    ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), name);
            }
        }
    }

    private void DrawLithologyPattern(ImDrawListPtr drawList, Vector2 pos, Vector2 size,
        string lithologyType, Vector4 baseColor)
    {
        var patternColor = ImGui.GetColorU32(new Vector4(
            baseColor.X * 0.6f, baseColor.Y * 0.6f, baseColor.Z * 0.6f, baseColor.W));

        switch (lithologyType?.ToLower())
        {
            case "sandstone":
            case "sand":
                // Dots pattern
                for (float y = 4; y < size.Y; y += 8)
                    for (float x = 4; x < size.X; x += 8)
                        drawList.AddCircleFilled(pos + new Vector2(x, y), 1.5f, patternColor);
                break;

            case "shale":
            case "clay":
                // Horizontal lines
                for (float y = 4; y < size.Y; y += 6)
                    drawList.AddLine(pos + new Vector2(0, y), pos + new Vector2(size.X, y), patternColor, 1f);
                break;

            case "limestone":
                // Brick pattern
                for (float y = 0; y < size.Y; y += 10)
                {
                    var offset = ((int)(y / 10) % 2) * 15;
                    for (float x = -15 + offset; x < size.X; x += 30)
                        drawList.AddRect(pos + new Vector2(x, y), pos + new Vector2(x + 28, y + 8), patternColor);
                }
                break;

            case "dolomite":
                // Diagonal lines
                for (float i = -size.Y; i < size.X + size.Y; i += 8)
                    drawList.AddLine(pos + new Vector2(i, 0), pos + new Vector2(i + size.Y, size.Y), patternColor, 1f);
                break;

            case "siltstone":
                // Dots pattern (smaller)
                for (float y = 3; y < size.Y; y += 5)
                    for (float x = 3; x < size.X; x += 5)
                        drawList.AddCircleFilled(pos + new Vector2(x, y), 1f, patternColor);
                break;

            case "conglomerate":
                // Circles pattern
                var rnd = new Random(42);
                for (int i = 0; i < (int)(size.X * size.Y / 100); i++)
                {
                    var x = (float)rnd.NextDouble() * size.X;
                    var y = (float)rnd.NextDouble() * size.Y;
                    var r = (float)rnd.NextDouble() * 3 + 2;
                    drawList.AddCircle(pos + new Vector2(x, y), r, patternColor);
                }
                break;
        }
    }

    private void DrawCorrelationLines(ImDrawListPtr drawList, Vector2 windowPos, float scrollX, float scrollY)
    {
        var lithologyTop = windowPos.Y + HeaderHeight - scrollY;

        foreach (var correlation in _correlationData.Correlations)
        {
            var sourceIndex = _correlationData.BoreholeOrder.IndexOf(correlation.SourceBoreholeID);
            var targetIndex = _correlationData.BoreholeOrder.IndexOf(correlation.TargetBoreholeID);

            if (sourceIndex < 0 || targetIndex < 0) continue;

            var sourceBorehole = _boreholeMap.GetValueOrDefault(correlation.SourceBoreholeID);
            var targetBorehole = _boreholeMap.GetValueOrDefault(correlation.TargetBoreholeID);

            if (sourceBorehole == null || targetBorehole == null) continue;

            var sourceUnit = sourceBorehole.LithologyUnits.FirstOrDefault(u => u.ID == correlation.SourceLithologyID);
            var targetUnit = targetBorehole.LithologyUnits.FirstOrDefault(u => u.ID == correlation.TargetLithologyID);

            if (sourceUnit == null || targetUnit == null) continue;

            // Calculate positions
            var sourceX = windowPos.X + DepthScaleWidth - scrollX + sourceIndex * (_columnWidth + _columnSpacing) * _zoom;
            var targetX = windowPos.X + DepthScaleWidth - scrollX + targetIndex * (_columnWidth + _columnSpacing) * _zoom;

            var sourceY = lithologyTop + (sourceUnit.DepthFrom + sourceUnit.DepthTo) / 2 * _depthScale * _zoom;
            var targetY = lithologyTop + (targetUnit.DepthFrom + targetUnit.DepthTo) / 2 * _depthScale * _zoom;

            // Adjust X to edge of columns
            if (targetIndex > sourceIndex)
            {
                sourceX += _columnWidth * _zoom;
            }
            else
            {
                targetX += _columnWidth * _zoom;
            }

            // Draw line
            var lineColor = correlation.IsAutoCorrelated
                ? new Vector4(0.6f, 0.6f, 0.3f, 0.7f)
                : correlation.Color;

            // Draw bezier curve for smoother appearance
            var midX = (sourceX + targetX) / 2;
            drawList.AddBezierCubic(
                new Vector2(sourceX, sourceY),
                new Vector2(midX, sourceY),
                new Vector2(midX, targetY),
                new Vector2(targetX, targetY),
                ImGui.GetColorU32(lineColor),
                2f);

            // Draw small circles at endpoints
            drawList.AddCircleFilled(new Vector2(sourceX, sourceY), 4, ImGui.GetColorU32(lineColor));
            drawList.AddCircleFilled(new Vector2(targetX, targetY), 4, ImGui.GetColorU32(lineColor));
        }
    }

    private void DrawPendingCorrelationLine(ImDrawListPtr drawList, Vector2 windowPos, float scrollX, float scrollY)
    {
        if (_pendingCorrelationSource == null || string.IsNullOrEmpty(_pendingCorrelationSourceBorehole))
            return;

        var sourceIndex = _correlationData.BoreholeOrder.IndexOf(_pendingCorrelationSourceBorehole);
        if (sourceIndex < 0) return;

        var lithologyTop = windowPos.Y + HeaderHeight - scrollY;
        var sourceX = windowPos.X + DepthScaleWidth - scrollX + sourceIndex * (_columnWidth + _columnSpacing) * _zoom + _columnWidth * _zoom / 2;
        var sourceY = lithologyTop + (_pendingCorrelationSource.DepthFrom + _pendingCorrelationSource.DepthTo) / 2 * _depthScale * _zoom;

        var mouse = ImGui.GetMousePos();

        // Draw dashed line to mouse
        drawList.AddLine(new Vector2(sourceX, sourceY), mouse,
            ImGui.GetColorU32(_pendingCorrelationColor), 2f);

        // Draw circle at source
        drawList.AddCircleFilled(new Vector2(sourceX, sourceY), 6, ImGui.GetColorU32(_pendingCorrelationColor));
    }

    private void HandleLithologyClick(LithologyUnit unit, string boreholeID)
    {
        if (_isSelectingCorrelationTarget)
        {
            // Try to create correlation
            if (_pendingCorrelationSource != null && _pendingCorrelationSourceBorehole != boreholeID)
            {
                var success = _correlationData.AddCorrelation(
                    _pendingCorrelationSource.ID, _pendingCorrelationSourceBorehole,
                    unit.ID, boreholeID);

                if (success)
                    ShowStatus($"Correlation created between {_pendingCorrelationSource.Name} and {unit.Name}");
                else
                    ShowStatus("Cannot create correlation: constraints not satisfied", true);
            }

            _isSelectingCorrelationTarget = false;
            _pendingCorrelationSource = null;
            _pendingCorrelationSourceBorehole = null;
        }
        else
        {
            // Start correlation selection
            _selectedUnit = unit;
            _selectedBoreholeID = boreholeID;
            _pendingCorrelationSource = unit;
            _pendingCorrelationSourceBorehole = boreholeID;
            _isSelectingCorrelationTarget = true;
        }
    }

    private void HandleInput()
    {
        var io = ImGui.GetIO();
        var mouse = ImGui.GetMousePos();

        // Pan with middle mouse button or right click drag
        if (ImGui.IsMouseDown(ImGuiMouseButton.Middle) || ImGui.IsMouseDown(ImGuiMouseButton.Right))
        {
            if (!_isDragging)
            {
                _isDragging = true;
                _lastMousePos = mouse;
            }

            var delta = mouse - _lastMousePos;
            ImGui.SetScrollX(ImGui.GetScrollX() - delta.X);
            ImGui.SetScrollY(ImGui.GetScrollY() - delta.Y);
            _lastMousePos = mouse;
        }
        else
        {
            _isDragging = false;
        }

        // Zoom with scroll wheel
        if (ImGui.IsWindowHovered() && io.MouseWheel != 0)
        {
            var zoomFactor = io.MouseWheel > 0 ? 1.1f : 0.9f;
            _zoom = Math.Clamp(_zoom * zoomFactor, 0.5f, 5f);
        }

        // Keyboard shortcuts
        if (ImGui.IsWindowFocused())
        {
            if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S))
                _showSaveDialog = true;
            if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.E))
                ExportToPNG();
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                if (_isSelectingCorrelationTarget)
                {
                    _isSelectingCorrelationTarget = false;
                    _pendingCorrelationSource = null;
                    _pendingCorrelationSourceBorehole = null;
                }
                else
                {
                    _selectedUnit = null;
                    _selectedBoreholeID = null;
                }
            }
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
            ImGui.TextDisabled($"Boreholes: {_boreholes.Count} | Correlations: {_correlationData.Correlations.Count} | Horizons: {_correlationData.Horizons.Count}");
        }
    }

    private void ShowStatus(string message, bool isError = false)
    {
        _statusMessage = isError ? $"Error: {message}" : message;
        _statusMessageTimer = 3f;
    }

    private float GetAdaptiveGridInterval(float pixelsPerMeter)
    {
        if (pixelsPerMeter <= 0) return 100f;
        var targetPixels = 60f;
        var interval = targetPixels / pixelsPerMeter;
        var pow10 = Math.Pow(10, Math.Floor(Math.Log10(interval)));
        var normalized = interval / pow10;

        if (normalized < 1.5) return (float)(1 * pow10);
        if (normalized < 3.5) return (float)(2 * pow10);
        if (normalized < 7.5) return (float)(5 * pow10);
        return (float)(10 * pow10);
    }

    #region Auto-Correlation

    private void AutoCorrelateByLithologyType()
    {
        int correlationsAdded = 0;

        for (int i = 0; i < _boreholes.Count - 1; i++)
        {
            var borehole1 = _boreholes[i];
            var borehole2 = _boreholes[i + 1];
            var id1 = GetBoreholeID(borehole1);
            var id2 = GetBoreholeID(borehole2);

            foreach (var unit1 in borehole1.LithologyUnits)
            {
                // Find best matching unit in adjacent borehole by lithology type
                var candidates = borehole2.LithologyUnits
                    .Where(u => u.LithologyType == unit1.LithologyType)
                    .OrderBy(u => Math.Abs((u.DepthFrom + u.DepthTo) / 2 - (unit1.DepthFrom + unit1.DepthTo) / 2))
                    .ToList();

                foreach (var unit2 in candidates)
                {
                    if (_correlationData.AddCorrelation(unit1.ID, id1, unit2.ID, id2, 0.7f, true))
                    {
                        correlationsAdded++;
                        break; // Only one correlation per unit per direction
                    }
                }
            }
        }

        ShowStatus($"Auto-correlation added {correlationsAdded} correlations by lithology type");
    }

    private void AutoCorrelateByDepth()
    {
        int correlationsAdded = 0;
        float depthTolerance = 5f; // meters

        for (int i = 0; i < _boreholes.Count - 1; i++)
        {
            var borehole1 = _boreholes[i];
            var borehole2 = _boreholes[i + 1];
            var id1 = GetBoreholeID(borehole1);
            var id2 = GetBoreholeID(borehole2);

            foreach (var unit1 in borehole1.LithologyUnits)
            {
                var mid1 = (unit1.DepthFrom + unit1.DepthTo) / 2;

                // Find units at similar depth
                var candidates = borehole2.LithologyUnits
                    .Where(u =>
                    {
                        var mid2 = (u.DepthFrom + u.DepthTo) / 2;
                        return Math.Abs(mid1 - mid2) < depthTolerance;
                    })
                    .OrderBy(u => Math.Abs((u.DepthFrom + u.DepthTo) / 2 - mid1))
                    .ToList();

                foreach (var unit2 in candidates)
                {
                    var confidence = 1f - Math.Abs((unit2.DepthFrom + unit2.DepthTo) / 2 - mid1) / depthTolerance;
                    if (_correlationData.AddCorrelation(unit1.ID, id1, unit2.ID, id2, confidence, true))
                    {
                        correlationsAdded++;
                        break;
                    }
                }
            }
        }

        ShowStatus($"Auto-correlation added {correlationsAdded} correlations by depth");
    }

    private void AutoCorrelateCombined()
    {
        int correlationsAdded = 0;
        float depthWeight = 0.4f;
        float typeWeight = 0.4f;
        float thicknessWeight = 0.2f;

        for (int i = 0; i < _boreholes.Count - 1; i++)
        {
            var borehole1 = _boreholes[i];
            var borehole2 = _boreholes[i + 1];
            var id1 = GetBoreholeID(borehole1);
            var id2 = GetBoreholeID(borehole2);

            foreach (var unit1 in borehole1.LithologyUnits)
            {
                var mid1 = (unit1.DepthFrom + unit1.DepthTo) / 2;
                var thickness1 = unit1.DepthTo - unit1.DepthFrom;

                // Score all candidates
                var candidates = borehole2.LithologyUnits
                    .Select(u =>
                    {
                        var mid2 = (u.DepthFrom + u.DepthTo) / 2;
                        var thickness2 = u.DepthTo - u.DepthFrom;

                        // Depth similarity (normalize by max depth)
                        var maxDepth = Math.Max(borehole1.TotalDepth, borehole2.TotalDepth);
                        var depthScore = 1f - Math.Min(1f, Math.Abs(mid1 - mid2) / (maxDepth * 0.2f));

                        // Type similarity
                        var typeScore = u.LithologyType == unit1.LithologyType ? 1f : 0f;

                        // Thickness similarity
                        var maxThickness = Math.Max(thickness1, thickness2);
                        var thicknessScore = maxThickness > 0 ? 1f - Math.Abs(thickness1 - thickness2) / maxThickness : 0f;

                        var totalScore = depthScore * depthWeight + typeScore * typeWeight + thicknessScore * thicknessWeight;
                        return (unit: u, score: totalScore);
                    })
                    .Where(x => x.score > 0.5f)
                    .OrderByDescending(x => x.score)
                    .ToList();

                foreach (var (unit2, score) in candidates)
                {
                    if (_correlationData.AddCorrelation(unit1.ID, id1, unit2.ID, id2, score, true))
                    {
                        correlationsAdded++;
                        break;
                    }
                }
            }
        }

        ShowStatus($"Auto-correlation (combined) added {correlationsAdded} correlations");
    }

    #endregion

    #region Dialogs

    private void DrawHeaderEditDialog()
    {
        if (!_showHeaderEditDialog) return;

        ImGui.OpenPopup("Edit Borehole Header");
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Edit Borehole Header", ref _showHeaderEditDialog, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Display Name:");
            var displayName = _editingHeader.DisplayName ?? "";
            if (ImGui.InputText("##DisplayName", ref displayName, 256))
                _editingHeader.DisplayName = displayName;

            ImGui.Text("Custom Label:");
            var label = _editingHeader.CustomLabel ?? "";
            if (ImGui.InputText("##CustomLabel", ref label, 256))
                _editingHeader.CustomLabel = label;

            ImGui.Text("Coordinates:");
            var coordX = _editingHeader.Coordinates.X;
            var coordY = _editingHeader.Coordinates.Y;
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputFloat("X##CoordX", ref coordX))
                _editingHeader.Coordinates = new Vector2(coordX, coordY);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputFloat("Y##CoordY", ref coordY))
                _editingHeader.Coordinates = new Vector2(coordX, coordY);

            ImGui.Text("Elevation (m):");
            var elevation = _editingHeader.Elevation;
            if (ImGui.InputFloat("##Elevation", ref elevation))
                _editingHeader.Elevation = elevation;

            ImGui.Separator();

            if (ImGui.Button("OK", new Vector2(120, 0)))
            {
                _correlationData.Headers[_editingBoreholeID] = _editingHeader;
                _showHeaderEditDialog = false;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                _showHeaderEditDialog = false;
            }

            ImGui.EndPopup();
        }
    }

    private void DrawSaveDialog()
    {
        if (!_showSaveDialog) return;

        ImGui.OpenPopup("Save Correlations");
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Save Correlations", ref _showSaveDialog, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Save correlation data to file:");

            ImGui.SetNextItemWidth(400);
            ImGui.InputText("##FilePath", ref _saveFilePath, 512);

            ImGui.SameLine();
            if (ImGui.Button("Browse..."))
            {
                // In a real implementation, you would use a file dialog
                _saveFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"correlation_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            }

            ImGui.Separator();

            if (ImGui.Button("Save", new Vector2(120, 0)) && !string.IsNullOrEmpty(_saveFilePath))
            {
                _correlationData.SaveToFile(_saveFilePath);
                ShowStatus($"Saved to {_saveFilePath}");
                _showSaveDialog = false;
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
            {
                _showSaveDialog = false;
            }

            ImGui.EndPopup();
        }
    }

    private void LoadCorrelations()
    {
        // In a real implementation, you would use a file dialog
        ShowStatus("Load correlations: use File > Load from project");
    }

    #endregion

    #region PNG Export

    public void ExportToPNG()
    {
        try
        {
            var exportPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"correlation_{DateTime.Now:yyyyMMdd_HHmmss}.png");

            var exporter = new BoreholeCorrelationImageExporter(
                _boreholes, _boreholeMap, _correlationData, _columnWidth, _columnSpacing, _depthScale);

            exporter.Export(exportPath);
            ShowStatus($"Exported to {exportPath}");
        }
        catch (Exception ex)
        {
            ShowStatus($"Export failed: {ex.Message}", true);
        }
    }

    #endregion

    public void Dispose()
    {
        Logger.Log("[BoreholeLogCorrelationViewer] Disposed");
    }
}

/// <summary>
/// Exports borehole correlations to PNG using StbImageSharp
/// </summary>
public class BoreholeCorrelationImageExporter
{
    // Simple 5x7 bitmap font
    private static readonly Dictionary<char, byte[]> SimpleFont = new()
    {
        { 'A', new byte[] { 0x7C, 0x12, 0x11, 0x12, 0x7C } }, { 'B', new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x36 } },
        { 'C', new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x22 } }, { 'D', new byte[] { 0x7F, 0x41, 0x41, 0x22, 0x1C } },
        { 'E', new byte[] { 0x7F, 0x49, 0x49, 0x41, 0x41 } }, { 'F', new byte[] { 0x7F, 0x09, 0x09, 0x01, 0x01 } },
        { 'G', new byte[] { 0x3E, 0x41, 0x49, 0x49, 0x7A } }, { 'H', new byte[] { 0x7F, 0x08, 0x08, 0x08, 0x7F } },
        { 'I', new byte[] { 0x00, 0x41, 0x7F, 0x41, 0x00 } }, { 'J', new byte[] { 0x20, 0x40, 0x41, 0x3F, 0x01 } },
        { 'K', new byte[] { 0x7F, 0x08, 0x14, 0x22, 0x41 } }, { 'L', new byte[] { 0x7F, 0x40, 0x40, 0x40, 0x40 } },
        { 'M', new byte[] { 0x7F, 0x02, 0x0C, 0x02, 0x7F } }, { 'N', new byte[] { 0x7F, 0x04, 0x08, 0x10, 0x7F } },
        { 'O', new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x3E } }, { 'P', new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x06 } },
        { 'Q', new byte[] { 0x3E, 0x41, 0x51, 0x21, 0x5E } }, { 'R', new byte[] { 0x7F, 0x09, 0x19, 0x29, 0x46 } },
        { 'S', new byte[] { 0x46, 0x49, 0x49, 0x49, 0x31 } }, { 'T', new byte[] { 0x01, 0x01, 0x7F, 0x01, 0x01 } },
        { 'U', new byte[] { 0x3F, 0x40, 0x40, 0x40, 0x3F } }, { 'V', new byte[] { 0x1F, 0x20, 0x40, 0x20, 0x1F } },
        { 'W', new byte[] { 0x3F, 0x40, 0x38, 0x40, 0x3F } }, { 'X', new byte[] { 0x63, 0x14, 0x08, 0x14, 0x63 } },
        { 'Y', new byte[] { 0x07, 0x08, 0x70, 0x08, 0x07 } }, { 'Z', new byte[] { 0x61, 0x51, 0x49, 0x45, 0x43 } },
        { '0', new byte[] { 0x3E, 0x51, 0x49, 0x45, 0x3E } }, { '1', new byte[] { 0x00, 0x42, 0x7F, 0x40, 0x00 } },
        { '2', new byte[] { 0x42, 0x61, 0x51, 0x49, 0x46 } }, { '3', new byte[] { 0x21, 0x41, 0x45, 0x4B, 0x31 } },
        { '4', new byte[] { 0x18, 0x14, 0x12, 0x7F, 0x10 } }, { '5', new byte[] { 0x27, 0x45, 0x45, 0x45, 0x39 } },
        { '6', new byte[] { 0x3C, 0x4A, 0x49, 0x49, 0x30 } }, { '7', new byte[] { 0x01, 0x71, 0x09, 0x05, 0x03 } },
        { '8', new byte[] { 0x36, 0x49, 0x49, 0x49, 0x36 } }, { '9', new byte[] { 0x06, 0x49, 0x49, 0x29, 0x1E } },
        { ' ', new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 } }, { '.', new byte[] { 0x00, 0x60, 0x60, 0x00, 0x00 } },
        { '-', new byte[] { 0x08, 0x08, 0x08, 0x08, 0x08 } }, { ':', new byte[] { 0x00, 0x36, 0x36, 0x00, 0x00 } },
        { ',', new byte[] { 0x00, 0x50, 0x30, 0x00, 0x00 } }, { '(', new byte[] { 0x00, 0x1C, 0x22, 0x41, 0x00 } },
        { ')', new byte[] { 0x00, 0x41, 0x22, 0x1C, 0x00 } }
    };

    private readonly List<BoreholeDataset> _boreholes;
    private readonly Dictionary<string, BoreholeDataset> _boreholeMap;
    private readonly BoreholeLogCorrelationDataset _correlationData;
    private readonly float _columnWidth;
    private readonly float _columnSpacing;
    private readonly float _depthScale;

    private byte[] _pixelBuffer;
    private int _width;
    private int _height;

    public BoreholeCorrelationImageExporter(
        List<BoreholeDataset> boreholes,
        Dictionary<string, BoreholeDataset> boreholeMap,
        BoreholeLogCorrelationDataset correlationData,
        float columnWidth,
        float columnSpacing,
        float depthScale)
    {
        _boreholes = boreholes;
        _boreholeMap = boreholeMap;
        _correlationData = correlationData;
        _columnWidth = columnWidth;
        _columnSpacing = columnSpacing;
        _depthScale = depthScale;
    }

    public void Export(string filePath)
    {
        // Calculate dimensions
        const float depthScaleWidth = 60f;
        const float headerHeight = 100f;
        const float margin = 20f;

        var maxDepth = _boreholes.Max(b => b.TotalDepth);
        _width = (int)(margin * 2 + depthScaleWidth + _boreholes.Count * (_columnWidth + _columnSpacing));
        _height = (int)(margin * 2 + headerHeight + maxDepth * _depthScale);

        _pixelBuffer = new byte[_width * _height * 4];

        // Clear to white
        Clear(255, 255, 255, 255);

        // Draw depth scale
        DrawDepthScale(depthScaleWidth, headerHeight, margin, maxDepth);

        // Draw each borehole
        var columnX = margin + depthScaleWidth;
        foreach (var borehole in _boreholes)
        {
            var boreholeID = borehole.FilePath ?? borehole.WellName ?? borehole.GetHashCode().ToString();
            var header = _correlationData.Headers.GetValueOrDefault(boreholeID);

            DrawBoreholeColumn(borehole, header, columnX, margin + headerHeight, margin);
            columnX += _columnWidth + _columnSpacing;
        }

        // Draw correlation lines
        DrawCorrelationLines(depthScaleWidth, headerHeight, margin);

        // Save to file
        using var stream = new MemoryStream();
        var writer = new ImageWriter();
        writer.WritePng(_pixelBuffer, _width, _height, ColorComponents.RedGreenBlueAlpha, stream);
        File.WriteAllBytes(filePath, stream.ToArray());

        Logger.Log($"[BoreholeCorrelationImageExporter] Exported to {filePath}");
    }

    private void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        if (x < 0 || x >= _width || y < 0 || y >= _height) return;
        var index = (y * _width + x) * 4;
        _pixelBuffer[index] = r;
        _pixelBuffer[index + 1] = g;
        _pixelBuffer[index + 2] = b;
        _pixelBuffer[index + 3] = a;
    }

    private void Clear(byte r, byte g, byte b, byte a)
    {
        for (var i = 0; i < _pixelBuffer.Length; i += 4)
        {
            _pixelBuffer[i] = r;
            _pixelBuffer[i + 1] = g;
            _pixelBuffer[i + 2] = b;
            _pixelBuffer[i + 3] = a;
        }
    }

    private void FillRect(int x, int y, int w, int h, byte r, byte g, byte b, byte a)
    {
        for (var j = 0; j < h; j++)
            for (var i = 0; i < w; i++)
                SetPixel(x + i, y + j, r, g, b, a);
    }

    private void DrawRect(int x, int y, int w, int h, byte r, byte g, byte b, byte a, int thickness = 1)
    {
        FillRect(x, y, w, thickness, r, g, b, a);
        FillRect(x, y + h - thickness, w, thickness, r, g, b, a);
        FillRect(x, y, thickness, h, r, g, b, a);
        FillRect(x + w - thickness, y, thickness, h, r, g, b, a);
    }

    private void DrawLine(int x1, int y1, int x2, int y2, byte r, byte g, byte b, byte a)
    {
        int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
        int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
        int err = dx + dy, e2;

        while (true)
        {
            SetPixel(x1, y1, r, g, b, a);
            if (x1 == x2 && y1 == y2) break;
            e2 = 2 * err;
            if (e2 >= dy) { err += dy; x1 += sx; }
            if (e2 <= dx) { err += dx; y1 += sy; }
        }
    }

    private void DrawText(string text, int x, int y, byte r, byte g, byte b)
    {
        var currentX = x;
        foreach (var c in text.ToUpper())
        {
            if (SimpleFont.TryGetValue(c, out var charData))
                for (var i = 0; i < 5; i++)
                    for (var j = 0; j < 7; j++)
                        if ((charData[i] & (1 << j)) != 0)
                            SetPixel(currentX + i, y + j, r, g, b, 255);
            currentX += 6;
        }
    }

    private void DrawDepthScale(float scaleWidth, float headerHeight, float margin, float maxDepth)
    {
        var x = (int)margin;
        var y = (int)(margin + headerHeight);

        FillRect(x, y, (int)scaleWidth, (int)(maxDepth * _depthScale), 240, 240, 240, 255);

        var interval = GetGridInterval();
        for (float depth = 0; depth <= maxDepth; depth += interval)
        {
            var depthY = y + (int)(depth * _depthScale);
            DrawLine(x + (int)scaleWidth - 10, depthY, x + (int)scaleWidth, depthY, 50, 50, 50, 255);
            DrawText($"{depth:0}M", x + 5, depthY - 3, 50, 50, 50);
        }
    }

    private void DrawBoreholeColumn(BoreholeDataset borehole, BoreholeHeader header, float x, float y, float margin)
    {
        var colX = (int)x;
        var colY = (int)y;
        var colW = (int)_columnWidth;

        // Draw header
        var headerY = (int)margin;
        FillRect(colX, headerY, colW, 100, 60, 60, 80, 255);
        DrawRect(colX, headerY, colW, 100, 100, 100, 120, 255);

        var wellName = header?.DisplayName ?? borehole.WellName ?? borehole.Name;
        DrawText(wellName.Length > 15 ? wellName.Substring(0, 15) : wellName,
            colX + 5, headerY + 10, 255, 255, 255);

        var coords = header?.Coordinates ?? borehole.SurfaceCoordinates;
        DrawText($"X:{coords.X:0}", colX + 5, headerY + 30, 180, 180, 180);
        DrawText($"Y:{coords.Y:0}", colX + 5, headerY + 45, 180, 180, 180);

        var elev = header?.Elevation ?? borehole.Elevation;
        DrawText($"EL:{elev:0}M", colX + 5, headerY + 60, 180, 180, 180);

        var td = header?.TotalDepth ?? borehole.TotalDepth;
        DrawText($"TD:{td:0}M", colX + 5, headerY + 75, 180, 180, 180);

        // Draw lithology units
        foreach (var unit in borehole.LithologyUnits)
        {
            var unitY = colY + (int)(unit.DepthFrom * _depthScale);
            var unitH = (int)((unit.DepthTo - unit.DepthFrom) * _depthScale);

            var color = unit.Color;
            FillRect(colX, unitY, colW, unitH,
                (byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), 255);
            DrawRect(colX, unitY, colW, unitH, 100, 100, 100, 255);

            if (unitH > 15)
            {
                var name = unit.Name.Length > 12 ? unit.Name.Substring(0, 12) : unit.Name;
                DrawText(name, colX + 5, unitY + unitH / 2 - 3, 0, 0, 0);
            }
        }
    }

    private void DrawCorrelationLines(float depthScaleWidth, float headerHeight, float margin)
    {
        var lithologyTop = margin + headerHeight;

        foreach (var correlation in _correlationData.Correlations)
        {
            var sourceIndex = _correlationData.BoreholeOrder.IndexOf(correlation.SourceBoreholeID);
            var targetIndex = _correlationData.BoreholeOrder.IndexOf(correlation.TargetBoreholeID);

            if (sourceIndex < 0 || targetIndex < 0) continue;

            var sourceBorehole = _boreholeMap.GetValueOrDefault(correlation.SourceBoreholeID);
            var targetBorehole = _boreholeMap.GetValueOrDefault(correlation.TargetBoreholeID);

            if (sourceBorehole == null || targetBorehole == null) continue;

            var sourceUnit = sourceBorehole.LithologyUnits.FirstOrDefault(u => u.ID == correlation.SourceLithologyID);
            var targetUnit = targetBorehole.LithologyUnits.FirstOrDefault(u => u.ID == correlation.TargetLithologyID);

            if (sourceUnit == null || targetUnit == null) continue;

            var sourceX = margin + depthScaleWidth + sourceIndex * (_columnWidth + _columnSpacing);
            var targetX = margin + depthScaleWidth + targetIndex * (_columnWidth + _columnSpacing);

            var sourceY = lithologyTop + (sourceUnit.DepthFrom + sourceUnit.DepthTo) / 2 * _depthScale;
            var targetY = lithologyTop + (targetUnit.DepthFrom + targetUnit.DepthTo) / 2 * _depthScale;

            if (targetIndex > sourceIndex)
                sourceX += _columnWidth;
            else
                targetX += _columnWidth;

            var color = correlation.Color;
            DrawLine((int)sourceX, (int)sourceY, (int)targetX, (int)targetY,
                (byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), 255);
        }
    }

    private float GetGridInterval()
    {
        var maxDepth = _boreholes.Max(b => b.TotalDepth);
        if (maxDepth <= 50) return 5;
        if (maxDepth <= 100) return 10;
        if (maxDepth <= 500) return 50;
        return 100;
    }
}
