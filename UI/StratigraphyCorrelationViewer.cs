using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business.Stratigraphies;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Windows;

/// <summary>
///     Window for viewing and comparing different stratigraphic charts with visual correlations
/// </summary>
public class StratigraphyCorrelationViewer
{
    private readonly List<bool> _columnVisibility = new();
    private readonly ImGuiExportFileDialog _exportDialog = new("StratExportDlg", "Export Stratigraphy");


    // Major orogenic events to mark (in Ma)
    private readonly List<OrogenicEvent> _orogenicEvents = new()
    {
        new OrogenicEvent("Variscan Orogeny", 380, 280, new Vector4(0.8f, 0.3f, 0.3f, 1.0f)),
        new OrogenicEvent("Alpine Orogeny", 65, 2, new Vector4(0.3f, 0.6f, 0.9f, 1.0f)),
        new OrogenicEvent("Apenninic Orogeny", 30, 5, new Vector4(0.6f, 0.4f, 0.8f, 1.0f)),
        new OrogenicEvent("Appalachian Orogeny", 480, 265, new Vector4(0.9f, 0.6f, 0.2f, 1.0f))
    };

    private readonly List<IStratigraphy> _stratigraphies = new();
    private float _ageScale = 1.0f; // pixels per million years
    private float _columnSpacing = 10f; // Space between columns
    private float _columnWidth = 200f;
    private StratigraphicLevel _displayLevel = StratigraphicLevel.Epoch;
    private bool _exportDialogInitialized;
    private bool _isVisible;
    private double _lastExportMs;
    private Vector2 _scrollPosition = Vector2.Zero;
    private float _scrollY;
    private string _selectedStratigraphyCode = "";
    private int _selectedUnitIndex = -1;
    private bool _showAgeScale = true;
    private bool _showCorrelationLines = true;
    private bool _useLogView;
    private float _zoomLevel = 1.0f; // Overall zoom for entire view

    public StratigraphyCorrelationViewer()
    {
        // Initialize all available stratigraphies
        _stratigraphies.Add(new InternationalStratigraphy());
        _stratigraphies.Add(new GermanStratigraphy());
        _stratigraphies.Add(new FrenchStratigraphy());
        _stratigraphies.Add(new ItalianStratigraphy());
        _stratigraphies.Add(new USStratigraphy());
        _stratigraphies.Add(new UkStratigraphy());
        _stratigraphies.Add(new SpanishStratigraphy());
        _stratigraphies.Add(new MammalAgesStratigraphy());

        // All visible by default
        foreach (var _ in _stratigraphies) _columnVisibility.Add(true);

        _exportDialog = new ImGuiExportFileDialog("StratCorrelationExport", "Export Stratigraphy as PNG");
        _exportDialog.SetExtensions((".png", "PNG Image"));
    }

    public void Show()
    {
        _isVisible = true;
    }

    public void Draw()
    {
        if (!_isVisible) return;

        ImGui.SetNextWindowSize(new Vector2(1200, 800), ImGuiCond.FirstUseEver);
        // Add the ImGuiWindowFlags.NoMove flag here to restrict movement to the title bar
        if (ImGui.Begin("Stratigraphy Correlation Viewer", ref _isVisible,
                ImGuiWindowFlags.MenuBar))
        {
            DrawMenuBar();
            DrawToolbar();
            ImGui.Separator();
            DrawCorrelationView();
        }

        ImGui.End();

        // Handle export dialog
        if (_exportDialog.Submit()) ExportToPng(_exportDialog.SelectedPath);
    }

    private void DrawMenuBar()
    {
        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("View"))
            {
                if (ImGui.MenuItem("Show Correlation Lines", null, _showCorrelationLines))
                    _showCorrelationLines = !_showCorrelationLines;

                if (ImGui.MenuItem("Show Age Scale", null, _showAgeScale)) _showAgeScale = !_showAgeScale;

                ImGui.Separator();

                if (ImGui.MenuItem("Log View Style", null, _useLogView)) _useLogView = !_useLogView;

                ImGui.Separator();

                for (var i = 0; i < _stratigraphies.Count; i++)
                {
                    var visible = _columnVisibility[i];
                    if (ImGui.MenuItem(_stratigraphies[i].Name, null, visible)) _columnVisibility[i] = !visible;
                }

                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Level"))
            {
                if (ImGui.MenuItem("Eon", null, _displayLevel == StratigraphicLevel.Eon))
                    _displayLevel = StratigraphicLevel.Eon;
                if (ImGui.MenuItem("Era", null, _displayLevel == StratigraphicLevel.Era))
                    _displayLevel = StratigraphicLevel.Era;
                if (ImGui.MenuItem("Period", null, _displayLevel == StratigraphicLevel.Period))
                    _displayLevel = StratigraphicLevel.Period;
                if (ImGui.MenuItem("Epoch", null, _displayLevel == StratigraphicLevel.Epoch))
                    _displayLevel = StratigraphicLevel.Epoch;
                if (ImGui.MenuItem("Age", null, _displayLevel == StratigraphicLevel.Age))
                    _displayLevel = StratigraphicLevel.Age;
                ImGui.EndMenu();
            }

            if (ImGui.BeginMenu("Export"))
            {
                if (ImGui.MenuItem("Export as PNG..."))
                    _exportDialog.Open($"stratigraphy_correlation_{DateTime.Now:yyyyMMdd_HHmmss}");
                if (ImGui.MenuItem("Export Correlation Table...")) ExportCorrelationTable();
                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }
    }


    private void DrawToolbar()
    {
        ImGui.SameLine();
        if (ImGui.Button("Export PNG"))
            _exportDialog.Open($"stratigraphy_correlation_{DateTime.Now:yyyyMMdd_HHmmss}");
        ImGui.SameLine();
        ImGui.Text("Display Level:");
        ImGui.SameLine();

        if (ImGui.RadioButton("Eon", _displayLevel == StratigraphicLevel.Eon)) _displayLevel = StratigraphicLevel.Eon;
        ImGui.SameLine();
        if (ImGui.RadioButton("Era", _displayLevel == StratigraphicLevel.Era)) _displayLevel = StratigraphicLevel.Era;
        ImGui.SameLine();
        if (ImGui.RadioButton("Period", _displayLevel == StratigraphicLevel.Period))
            _displayLevel = StratigraphicLevel.Period;
        ImGui.SameLine();
        if (ImGui.RadioButton("Epoch", _displayLevel == StratigraphicLevel.Epoch))
            _displayLevel = StratigraphicLevel.Epoch;
        ImGui.SameLine();
        if (ImGui.RadioButton("Age", _displayLevel == StratigraphicLevel.Age)) _displayLevel = StratigraphicLevel.Age;

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(20, 0));
        ImGui.SameLine();

        ImGui.Text("Column Width:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("##ColWidth", ref _columnWidth, 100f, 400f, "%.0f px");

        ImGui.SameLine();
        ImGui.Text("Spacing:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("##ColSpacing", ref _columnSpacing, 5f, 100f, "%.0f px");

        ImGui.SameLine();
        ImGui.Text("Time Scale:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("##TimeScale", ref _ageScale, 0.1f, 10.0f, "%.1fx");

        ImGui.SameLine();
        ImGui.Text("Overall Zoom:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.SliderFloat("##OverallZoom", ref _zoomLevel, 0.25f, 3.0f, "%.2fx"))
            _zoomLevel = Math.Max(0.25f, Math.Min(3.0f, _zoomLevel));

        ImGui.SameLine();
        if (ImGui.Button("Reset Zoom"))
        {
            _zoomLevel = 1.0f;
            _columnWidth = 200f;
            _columnSpacing = 10f;
            _ageScale = 1.0f;
        }

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(20, 0));
        ImGui.SameLine();

        var visibleCount = _columnVisibility.Count(v => v);
        ImGui.Text($"Displaying {visibleCount}/{_stratigraphies.Count} stratigraphies");
    }


    private void DrawCorrelationView()
    {
        var drawList = ImGui.GetWindowDrawList();
        var availSize = ImGui.GetContentRegionAvail();

        var visibleStratigraphies = new List<(IStratigraphy strat, int index)>();
        for (var i = 0; i < _stratigraphies.Count; i++)
            if (_columnVisibility[i])
                visibleStratigraphies.Add((_stratigraphies[i], i));

        if (visibleStratigraphies.Count == 0)
        {
            ImGui.TextWrapped("No stratigraphies visible. Enable at least one from the View menu.");
            return;
        }

        if (_useLogView)
            DrawLogStyleView(drawList, availSize, visibleStratigraphies);
        else
            DrawColumnStyleView(drawList, availSize, visibleStratigraphies);
    }

    private void DrawLogStyleView(ImDrawListPtr drawList, Vector2 availSize,
        List<(IStratigraphy strat, int index)> visibleStratigraphies)
    {
        var logWidth = 120f * _zoomLevel;
        var logSpacing = 80f * _zoomLevel;
        var zoomedAgeScale = _ageScale * _zoomLevel;

        StratigraphicUnit hoveredUnit = null;
        IStratigraphy hoveredStrat = null;

        ImGui.BeginChild("CorrelationScrollRegion", Vector2.Zero, ImGuiChildFlags.Border,
            ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar);

        var dl = ImGui.GetWindowDrawList();
        var childMin = ImGui.GetWindowPos();
        var childMax = childMin + ImGui.GetWindowSize();
        dl.PushClipRect(childMin, childMax, true);
        dl.ChannelsSplit(3);

        var (scaleW, labelW, leftGutter, padLeft, padBetween) = ComputeLeftGutter();

        var childCursorPos = ImGui.GetCursorScreenPos();
        var headerHeight = 100f * _zoomLevel;
        var xOffset = leftGutter;
        var headerY = childCursorPos.Y;

        // MAIN: headers
        dl.ChannelsSetCurrent(1);
        foreach (var (strat, _) in visibleStratigraphies)
        {
            var headerCenter = new Vector2(childCursorPos.X + xOffset + logWidth * 0.5f, headerY);
            var lines = WrapText(strat.Name, logWidth - 5);
            var ty = headerCenter.Y + 5;
            foreach (var line in lines)
            {
                var ts = ImGui.CalcTextSize(line);
                var tp = new Vector2(headerCenter.X - ts.X * 0.5f, ty);
                dl.AddText(tp, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1)), line);
                ty += ts.Y + 2;
            }

            var lang = $"[{strat.LanguageCode}]";
            var ls = ImGui.CalcTextSize(lang);
            var lp = new Vector2(headerCenter.X - ls.X * 0.5f, ty + 5);
            dl.AddText(lp, ImGui.ColorConvertFloat4ToU32(new Vector4(0.8f, 0.8f, 0.8f, 1)), lang);

            xOffset += logWidth + logSpacing;
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + headerHeight);
        var contentStartPos = ImGui.GetCursorScreenPos();

        // Units & maxAge
        var columnUnits = new List<List<StratigraphicUnit>>();
        var maxAge = 0.0;
        foreach (var (strat, _) in visibleStratigraphies)
        {
            var units = strat.GetUnitsByLevel(_displayLevel).OrderByDescending(u => u.StartAge).ToList();
            columnUnits.Add(units);

            var all = strat.GetAllUnits();
            if (all.Any()) maxAge = Math.Max(maxAge, all.Max(u => u.StartAge));
        }

        if (maxAge <= 0.0) maxAge = 541.0;

        // BACKGROUND: orogeny behind
        dl.ChannelsSetCurrent(0);
        DrawOrogenicEventMarkers(dl, contentStartPos, xOffset, maxAge, zoomedAgeScale,
            padLeft, scaleW, labelW, leftGutter);

        // MAIN: logs
        dl.ChannelsSetCurrent(1);
        xOffset = leftGutter;
        var logCenters = new List<float>();

        for (var col = 0; col < visibleStratigraphies.Count; col++)
        {
            var (strat, _) = visibleStratigraphies[col];
            var units = columnUnits[col];

            var cx = xOffset + logWidth * 0.5f;
            logCenters.Add(cx);

            // vertical line
            var y0 = contentStartPos.Y;
            var y1 = contentStartPos.Y + (float)maxAge * zoomedAgeScale;
            dl.AddLine(new Vector2(contentStartPos.X + cx, y0), new Vector2(contentStartPos.X + cx, y1),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.3f, 1)), 2f);

            for (var i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var yStart = contentStartPos.Y + (float)u.EndAge * zoomedAgeScale;
                var yEnd = contentStartPos.Y + (float)u.StartAge * zoomedAgeScale;
                var h = Math.Max(1f, yEnd - yStart);

                var r0 = new Vector2(contentStartPos.X + xOffset, yStart);
                var r1 = new Vector2(contentStartPos.X + xOffset + logWidth, yEnd);

                var c = Color.FromArgb(u.Color.ToArgb());
                var fill = ImGui.ColorConvertFloat4ToU32(new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f));

                dl.AddRectFilled(r0, r1, fill);
                dl.AddLine(new Vector2(r0.X, yStart), new Vector2(r1.X, yStart),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)), 1.5f);
                dl.AddLine(new Vector2(r0.X, yEnd), new Vector2(r1.X, yEnd),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)), 1.5f);
                dl.AddLine(new Vector2(r0.X, yStart), new Vector2(r0.X, yEnd),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.5f)), 1.0f);
                dl.AddLine(new Vector2(r1.X, yStart), new Vector2(r1.X, yEnd),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.5f)), 1.0f);

                var mouse = ImGui.GetMousePos();
                var hovered = mouse.X >= r0.X && mouse.X <= r1.X && mouse.Y >= r0.Y && mouse.Y <= r1.Y;

                if (hovered)
                {
                    hoveredUnit = u;
                    hoveredStrat = strat;
                }

                if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _selectedUnitIndex = i;
                    _selectedStratigraphyCode = strat.LanguageCode;
                }

                if (h > 15f * _zoomLevel)
                {
                    var txt = u.Code;
                    var ts = ImGui.CalcTextSize(txt);
                    var tp = new Vector2(r1.X + 5f, yStart + (h - ts.Y) * 0.5f);
                    dl.AddText(tp, ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.95f, 0.95f, 1)), txt);
                }

                // overlay outlines
                dl.ChannelsSetCurrent(2);
                if (hovered)
                    dl.AddRect(r0, r1, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 1)), 0, 0, 3f);
                if (_selectedUnitIndex == i && _selectedStratigraphyCode == strat.LanguageCode)
                    dl.AddRect(r0, r1, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0.5f, 0, 1)), 0, 0, 3f);
                dl.ChannelsSetCurrent(1);
            }

            xOffset += logWidth + logSpacing;
        }

        // OVERLAY: correlations + scale
        dl.ChannelsSetCurrent(2);
        if (_showCorrelationLines && columnUnits.Count > 1)
            DrawLogCorrelationLines(dl, contentStartPos, logCenters, logWidth, columnUnits, maxAge, zoomedAgeScale);

        if (_showAgeScale)
            DrawAgeScale(dl, contentStartPos, maxAge, zoomedAgeScale, padLeft, scaleW);

        var totalWidth = xOffset + 50f;
        var totalHeight = (float)maxAge * zoomedAgeScale + 100f;
        ImGui.Dummy(new Vector2(totalWidth, totalHeight));

        dl.ChannelsMerge();
        dl.PopClipRect();

        if (hoveredUnit != null)
        {
            ImGui.BeginTooltip();
            ImGui.Text($"Unit: {hoveredUnit.Name} ({hoveredUnit.Code})");
            ImGui.Text($"Age: {hoveredUnit.StartAge:F2} - {hoveredUnit.EndAge:F2} Ma");
            ImGui.Text($"Stratigraphy: {hoveredStrat.Name}");
            if (!string.IsNullOrEmpty(hoveredUnit.InternationalCorrelationCode))
            {
                ImGui.Separator();
                ImGui.Text($"Correlation: {hoveredUnit.InternationalCorrelationCode}");
            }

            ImGui.EndTooltip();
        }

        ImGui.EndChild();
    }

    private void DrawColumnStyleView(ImDrawListPtr drawList, Vector2 availSize,
        List<(IStratigraphy strat, int index)> visibleStratigraphies)
    {
        var zoomedColumnWidth = _columnWidth * _zoomLevel;
        var zoomedAgeScale = _ageScale * _zoomLevel;

        StratigraphicUnit hoveredUnit = null;
        IStratigraphy hoveredStrat = null;

        // ---- Scrollable region with clipping ----
        ImGui.BeginChild("CorrelationScrollRegion", Vector2.Zero, ImGuiChildFlags.Border,
            ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar);

        var dl = ImGui.GetWindowDrawList();
        var childMin = ImGui.GetWindowPos();
        var childMax = childMin + ImGui.GetWindowSize();
        dl.PushClipRect(childMin, childMax, true);
        dl.ChannelsSplit(3); // 0=background (zones/lines), 1=main (blocks/headers), 2=overlay (labels, outlines)

        // ---- Layout positions ----
        var (scaleW, labelW, leftGutter, padLeft, padBetween) = ComputeLeftGutter();

        var childCursorPos = ImGui.GetCursorScreenPos();
        var xOffset = leftGutter; // start columns AFTER the gutter
        var headerHeight = 80f * _zoomLevel;
        var headerY = childCursorPos.Y;

        // ---------- MAIN (headers) ----------
        dl.ChannelsSetCurrent(1);

        foreach (var (strat, _) in visibleStratigraphies)
        {
            var headerPos = new Vector2(childCursorPos.X + xOffset, headerY);
            var headerSize = new Vector2(zoomedColumnWidth, headerHeight);

            dl.AddRectFilled(headerPos, headerPos + headerSize,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.2f, 0.2f, 0.25f, 1)));
            dl.AddRect(headerPos, headerPos + headerSize,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.4f, 0.5f, 1)), 0, 0, 2);

            var textLines = WrapText(strat.Name, zoomedColumnWidth - 10);
            var ty = headerPos.Y + 10;
            foreach (var line in textLines)
            {
                var ts = ImGui.CalcTextSize(line);
                var tp = new Vector2(headerPos.X + (zoomedColumnWidth - ts.X) * 0.5f, ty);
                dl.AddText(tp, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), line);
                ty += ts.Y + 2;
            }

            var lang = strat.LanguageCode;
            var ls = ImGui.CalcTextSize(lang);
            var lp = new Vector2(headerPos.X + (zoomedColumnWidth - ls.X) * 0.5f,
                headerPos.Y + headerHeight - ls.Y - 5);
            dl.AddText(lp, ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 1)), lang);

            xOffset += zoomedColumnWidth + _columnSpacing * _zoomLevel;
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + headerHeight + 10f);
        var contentStartPos = ImGui.GetCursorScreenPos();

        // Collect units & max age
        var columnUnits = new List<List<StratigraphicUnit>>();
        var maxAge = 0.0;
        foreach (var (strat, _) in visibleStratigraphies)
        {
            var units = strat.GetUnitsByLevel(_displayLevel).OrderByDescending(u => u.StartAge).ToList();
            columnUnits.Add(units);

            var allUnits = strat.GetAllUnits();
            if (allUnits.Any()) maxAge = Math.Max(maxAge, allUnits.Max(u => u.StartAge));
        }

        if (maxAge <= 0.0) maxAge = 541.0;

        // ---------- BACKGROUND: Orogeny zones/lines (behind blocks) ----------
        dl.ChannelsSetCurrent(0);
        DrawOrogenicEventMarkers(dl, contentStartPos, xOffset, maxAge, zoomedAgeScale,
            padLeft, scaleW, labelW, leftGutter);

        // ---------- MAIN: unit blocks ----------
        dl.ChannelsSetCurrent(1);
        xOffset = leftGutter;
        var columnPositions = new List<float>();

        for (var col = 0; col < visibleStratigraphies.Count; col++)
        {
            var (strat, _) = visibleStratigraphies[col];
            var units = columnUnits[col];
            columnPositions.Add(xOffset);

            for (var i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var y0 = contentStartPos.Y + (float)u.EndAge * zoomedAgeScale;
                var y1 = contentStartPos.Y + (float)u.StartAge * zoomedAgeScale;
                var h = Math.Max(1f, y1 - y0);

                var r0 = new Vector2(contentStartPos.X + xOffset, y0);
                var r1 = new Vector2(contentStartPos.X + xOffset + zoomedColumnWidth, y1);

                var c = Color.FromArgb(u.Color.ToArgb());
                var fill = ImGui.ColorConvertFloat4ToU32(new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f));
                dl.AddRectFilled(r0, r1, fill);
                dl.AddRect(r0, r1, ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1)));

                // Hover/select handling (overlay channel for outlines)
                var mouse = ImGui.GetMousePos();
                var hovered = mouse.X >= r0.X && mouse.X <= r1.X && mouse.Y >= r0.Y && mouse.Y <= r1.Y;

                if (hovered)
                {
                    hoveredUnit = u;
                    hoveredStrat = strat;
                }

                if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _selectedUnitIndex = i;
                    _selectedStratigraphyCode = strat.LanguageCode;
                }

                if (h > 25f * _zoomLevel)
                {
                    var txt = u.Name;
                    var ts = ImGui.CalcTextSize(txt);
                    if (ts.X > zoomedColumnWidth - 10f)
                    {
                        txt = u.Code;
                        ts = ImGui.CalcTextSize(txt);
                    }

                    if (ts.X < zoomedColumnWidth - 10f)
                    {
                        var tp = r0 + new Vector2((zoomedColumnWidth - ts.X) * 0.5f, (h - ts.Y) * 0.5f);
                        dl.AddText(tp + new Vector2(1, 1), ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.7f)),
                            txt);
                        dl.AddText(tp, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), txt);
                    }
                }

                // Overlay outline (hover/selection)
                dl.ChannelsSetCurrent(2);
                if (hovered)
                    dl.AddRect(r0, r1, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 0, 1)), 0, 0, 3f);
                if (_selectedUnitIndex == i && _selectedStratigraphyCode == strat.LanguageCode)
                    dl.AddRect(r0, r1, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0.5f, 0, 1)), 0, 0, 3f);
                dl.ChannelsSetCurrent(1);
            }

            xOffset += zoomedColumnWidth + _columnSpacing * _zoomLevel;
        }

        // ---------- OVERLAY: correlation lines + labels + age scale ----------
        dl.ChannelsSetCurrent(2);

        if (_showCorrelationLines && columnUnits.Count > 1)
            DrawCorrelationLines(dl, contentStartPos, columnPositions, columnUnits, maxAge, zoomedColumnWidth,
                zoomedAgeScale);

        // Age scale in gutter
        if (_showAgeScale)
            DrawAgeScale(dl, contentStartPos, maxAge, zoomedAgeScale, padLeft, scaleW);

        // Finish: ensure content area is scrollable enough
        var totalWidth = xOffset + 50f;
        var totalHeight = (float)maxAge * zoomedAgeScale + 100f;
        ImGui.Dummy(new Vector2(totalWidth, totalHeight));

        // Merge & pop
        dl.ChannelsMerge();
        dl.PopClipRect();

        if (hoveredUnit != null)
        {
            ImGui.BeginTooltip();
            ImGui.Text($"Unit: {hoveredUnit.Name} ({hoveredUnit.Code})");
            ImGui.Text($"Age: {hoveredUnit.StartAge:F2} - {hoveredUnit.EndAge:F2} Ma");
            ImGui.Text($"Stratigraphy: {hoveredStrat.Name}");
            if (!string.IsNullOrEmpty(hoveredUnit.InternationalCorrelationCode))
            {
                ImGui.Separator();
                ImGui.Text($"Correlation: {hoveredUnit.InternationalCorrelationCode}");
            }

            ImGui.EndTooltip();
        }

        ImGui.EndChild();
    }

    private void DrawOrogenicEventMarkers(ImDrawListPtr drawList, Vector2 contentStartPos,
        float totalWidth, double maxAge, float zoomedAgeScale,
        float padLeft, float scaleWidth, float labelLaneWidth, float leftGutter,
        int backgroundChannel = 0, int overlayChannel = 2)
    {
        // Columns drawing area (to the right of the gutter)
        var areaXMin = contentStartPos.X + leftGutter;
        var areaXMax = contentStartPos.X + totalWidth;

        // Label lane position (between scale and columns)
        var labelX = contentStartPos.X + padLeft + scaleWidth + 8f * _zoomLevel; // padBetween

        // --- Background (zones + base lines) ---
        drawList.ChannelsSetCurrent(backgroundChannel);

        foreach (var ev in _orogenicEvents)
        {
            if (ev.EndAge > maxAge) continue;

            var yTop = contentStartPos.Y + (float)ev.EndAge * zoomedAgeScale;
            var yBottom = contentStartPos.Y + (float)ev.StartAge * zoomedAgeScale;

            // Semi-transparent zone across the columns area only (not under labels or scale)
            if (yBottom - yTop > 3f)
            {
                var zoneColor = new Vector4(ev.Color.X, ev.Color.Y, ev.Color.Z, 0.10f);
                drawList.AddRectFilled(new Vector2(areaXMin, yTop), new Vector2(areaXMax, yBottom),
                    ImGui.ColorConvertFloat4ToU32(zoneColor));
            }

            // A base line at the "bottom" (you can add one on top if desired)
            drawList.AddLine(new Vector2(areaXMin, yBottom), new Vector2(areaXMax, yBottom),
                ImGui.ColorConvertFloat4ToU32(new Vector4(ev.Color.X, ev.Color.Y, ev.Color.Z, 0.65f)),
                2.0f * _zoomLevel);
        }

        // --- Overlay (labels in the gutter, never over blocks) ---
        drawList.ChannelsSetCurrent(overlayChannel);

        foreach (var ev in _orogenicEvents)
        {
            if (string.IsNullOrWhiteSpace(ev.Name)) continue;
            if (ev.EndAge > maxAge) continue;

            var yBottom = contentStartPos.Y + (float)ev.StartAge * zoomedAgeScale;

            // Stack words upward inside the label lane, just above the bottom line
            var words = ev.Name.Split(' ');
            var lineH = ImGui.GetTextLineHeightWithSpacing();
            var totalLabelH = words.Length * lineH;
            var y = yBottom - totalLabelH - 4f * _zoomLevel; // little gap above line

            foreach (var w in words)
            {
                var ws = ImGui.CalcTextSize(w);
                var pos = new Vector2(labelX + Math.Max(0f, (labelLaneWidth - ws.X) * 0.5f), y);
                drawList.AddText(pos,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(ev.Color.X, ev.Color.Y, ev.Color.Z, 1f)), w);
                y += lineH;
            }
        }
    }

    private List<string> WrapText(string text, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        var words = text.Split(' ');
        var currentLine = "";

        foreach (var word in words)
        {
            var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            if (ImGui.CalcTextSize(testLine).X > maxWidth && !string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }

        if (!string.IsNullOrEmpty(currentLine)) lines.Add(currentLine);

        return lines;
    }

    private void DrawLogCorrelationLines(ImDrawListPtr drawList, Vector2 contentStartPos,
        List<float> logCenterPositions, float logWidth, List<List<StratigraphicUnit>> columnUnits, double maxAge,
        float zoomedAgeScale)
    {
        // Connect adjacent columns only
        for (var c1 = 0; c1 < columnUnits.Count - 1; c1++)
        {
            var c2 = c1 + 1;
            var left = columnUnits[c1];
            var right = columnUnits[c2];

            foreach (var u1 in left.Where(u => !string.IsNullOrWhiteSpace(u.InternationalCorrelationCode)))
            {
                StratigraphicUnit? best = null;
                double bestOv = -1;

                // Find best matching unit in the right column
                foreach (var u2 in right.Where(u => !string.IsNullOrWhiteSpace(u.InternationalCorrelationCode)))
                {
                    var codes1 = u1.InternationalCorrelationCode.Split(',').Select(s => s.Trim());
                    var codes2 = u2.InternationalCorrelationCode.Split(',').Select(s => s.Trim());
                    if (!codes1.Any(c => codes2.Contains(c))) continue;

                    var (hasOv, yOv, oOv) = OverlapBounds(u1, u2);
                    var ov = hasOv ? oOv - yOv : 0.0;
                    if (ov > bestOv)
                    {
                        bestOv = ov;
                        best = u2;
                    }
                }

                if (best == null) continue;

                // Calculate actual positions
                var x1 = contentStartPos.X + logCenterPositions[c1] + logWidth * 0.5f;
                var x2 = contentStartPos.X + logCenterPositions[c2] - logWidth * 0.5f;

                // Get the actual Y positions of the unit boundaries
                var u1Top = contentStartPos.Y + (float)u1.EndAge * zoomedAgeScale;
                var u1Bottom = contentStartPos.Y + (float)u1.StartAge * zoomedAgeScale;
                var u2Top = contentStartPos.Y + (float)best.EndAge * zoomedAgeScale;
                var u2Bottom = contentStartPos.Y + (float)best.StartAge * zoomedAgeScale;

                // Draw lines connecting the tops and bottoms
                drawList.AddLine(new Vector2(x1, u1Top), new Vector2(x2, u2Top),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.6f, 0.8f, 0.85f)), 2.0f * _zoomLevel);

                drawList.AddLine(new Vector2(x1, u1Bottom), new Vector2(x2, u2Bottom),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.4f, 0.6f, 0.8f, 0.65f)), 2.0f * _zoomLevel);
            }
        }
    }

    private void DrawCorrelationLines(ImDrawListPtr drawList, Vector2 contentStartPos, List<float> columnPositions,
        List<List<StratigraphicUnit>> columnUnits, double maxAge, float zoomedColumnWidth, float zoomedAgeScale)
    {
        // Only connect adjacent columns
        for (var c1 = 0; c1 < columnUnits.Count - 1; c1++)
        {
            var c2 = c1 + 1;
            var left = columnUnits[c1];
            var right = columnUnits[c2];

            foreach (var u1 in left.Where(u => !string.IsNullOrWhiteSpace(u.InternationalCorrelationCode)))
            {
                StratigraphicUnit? best = null;
                double bestOv = -1;

                // Find best matching unit in the right column
                foreach (var u2 in right.Where(u => !string.IsNullOrWhiteSpace(u.InternationalCorrelationCode)))
                {
                    var codes1 = u1.InternationalCorrelationCode.Split(',').Select(s => s.Trim());
                    var codes2 = u2.InternationalCorrelationCode.Split(',').Select(s => s.Trim());
                    if (!codes1.Any(c => codes2.Contains(c))) continue;

                    var (hasOv, yOv, oOv) = OverlapBounds(u1, u2);
                    var ov = hasOv ? oOv - yOv : 0.0;
                    if (ov > bestOv)
                    {
                        bestOv = ov;
                        best = u2;
                    }
                }

                if (best == null) continue;

                // X positions at column edges
                var x1 = contentStartPos.X + columnPositions[c1] + zoomedColumnWidth;
                var x2 = contentStartPos.X + columnPositions[c2];

                // Get the actual Y positions of the unit boundaries
                var u1Top = contentStartPos.Y + (float)u1.EndAge * zoomedAgeScale;
                var u1Bottom = contentStartPos.Y + (float)u1.StartAge * zoomedAgeScale;
                var u2Top = contentStartPos.Y + (float)best.EndAge * zoomedAgeScale;
                var u2Bottom = contentStartPos.Y + (float)best.StartAge * zoomedAgeScale;

                // Draw lines connecting the actual unit boundaries
                // Top to top
                DrawDashedLine(drawList, new Vector2(x1, u1Top), new Vector2(x2, u2Top),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.9f, 0.8f)),
                    2.0f * _zoomLevel, 10f * _zoomLevel);

                // Bottom to bottom
                DrawDashedLine(drawList, new Vector2(x1, u1Bottom), new Vector2(x2, u2Bottom),
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.9f, 0.6f)),
                    2.0f * _zoomLevel, 10f * _zoomLevel);
            }
        }
    }

    private StratigraphyExportSettings BuildCurrentExportSettings()
    {
        // Collect visible columns (strat + original index)
        var visibleStratigraphies = new List<(IStratigraphy strat, int index)>();
        for (var i = 0; i < _stratigraphies.Count; i++)
            if (_columnVisibility[i])
                visibleStratigraphies.Add((_stratigraphies[i], i));

        // Per-column units for the current display level, ordered older→younger top-down drawing (StartAge desc)
        var columnUnits = new List<List<StratigraphicUnit>>();
        var maxAge = 0.0;

        foreach (var (strat, _) in visibleStratigraphies)
        {
            var units = strat.GetUnitsByLevel(_displayLevel)
                .OrderByDescending(u => u.StartAge)
                .ToList();
            columnUnits.Add(units);

            // Use the oldest boundary among all units to size the canvas
            if (units.Any())
                maxAge = Math.Max(maxAge, units.Max(u => u.StartAge));
        }

        if (maxAge <= 0.0) maxAge = 541.0; // safe fallback (Phanerozoic cap)

        return new StratigraphyExportSettings
        {
            VisibleStratigraphies = visibleStratigraphies,
            ColumnUnits = columnUnits,
            OrogenicEvents = _orogenicEvents,
            MaxAge = maxAge,
            ZoomLevel = _zoomLevel,
            ColumnWidth = _columnWidth,
            AgeScale = _ageScale,
            ShowCorrelationLines = _showCorrelationLines,
            ShowAgeScale = _showAgeScale,
            DisplayLevel = _displayLevel
        };
    }

    private void OpenExportDialog()
    {
        // Suggest a filename like "Stratigraphy_[level].png" in the user's Documents folder
        var suggested = $"Stratigraphy_{_displayLevel}.png";
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _exportDialog.Open(Path.GetFileNameWithoutExtension(suggested), docs);
    }

    private void DrawExportUI()
    {
        // 1) Export button (put it where you want in your toolbar)
        if (ImGui.Button("Export"))
            OpenExportDialog();

        // Optional keyboard shortcut: Ctrl+E
        if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.E, false))
            OpenExportDialog();

        // 2) Show/save dialog
        if (_exportDialog.IsOpen)
        {
            // lazy-init: set extension filters only once
            if (!_exportDialogInitialized)
            {
                _exportDialog.SetExtensions(
                    new ImGuiExportFileDialog.ExtensionOption(".png", "PNG image"));
                _exportDialogInitialized = true;
            }

            if (_exportDialog.Submit())
            {
                var targetPath = _exportDialog.SelectedPath;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                    var settings = BuildCurrentExportSettings(); // packs current viewer state
                    var exporter = new StratigraphyImageExporter(settings);
                    var t0 = Stopwatch.GetTimestamp();
                    exporter.Export(targetPath);
                    var t1 = Stopwatch.GetTimestamp();
                    _lastExportMs = 1000.0 * (t1 - t0) / Stopwatch.Frequency;

                    Logger.Log(
                        $"[StratigraphyCorrelationViewer] Exported PNG to: {targetPath} in {_lastExportMs:F1} ms");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[StratigraphyCorrelationViewer] Export failed: {ex.Message}");
                }
            }
        }
    }

    private void DrawDashedLine(ImDrawListPtr drawList, Vector2 p1, Vector2 p2, uint color, float thickness,
        float dashLength)
    {
        var dir = p2 - p1;
        var length = dir.Length();
        if (length < 0.001f) return;
        dir /= length;

        for (float traveled = 0; traveled < length; traveled += dashLength * 2)
        {
            var start = p1 + dir * traveled;
            var end = p1 + dir * Math.Min(traveled + dashLength, length);
            drawList.AddLine(start, end, color, thickness);
        }
    }

    private void DrawAgeScale(ImDrawListPtr drawList, Vector2 contentStartPos, double maxAge,
        float zoomedAgeScale, float padLeft, float scaleWidth)
    {
        var scaleX = contentStartPos.X + padLeft;
        var scaleY0 = contentStartPos.Y;
        var scaleY1 = contentStartPos.Y + (float)maxAge * zoomedAgeScale;

        // Background for the scale
        drawList.AddRectFilled(
            new Vector2(scaleX, scaleY0),
            new Vector2(scaleX + scaleWidth, scaleY1),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.15f, 0.15f, 0.85f)));

        // Major ticks & labels
        var majorStep = CalculateAgeStep(maxAge);
        for (double age = 0; age <= maxAge; age += majorStep)
        {
            var y = contentStartPos.Y + (float)age * zoomedAgeScale;
            var tick0 = new Vector2(scaleX + scaleWidth - 10f * _zoomLevel, y);
            var tick1 = new Vector2(scaleX + scaleWidth, y);
            drawList.AddLine(tick0, tick1, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), 2.0f);

            var t = $"{age:F0}";
            var ts = ImGui.CalcTextSize(t);
            var tp = new Vector2(scaleX + scaleWidth - ts.X - 14f * _zoomLevel, y - ts.Y * 0.5f);
            drawList.AddText(tp, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)), t);
        }

        // "Ma" title
        var ma = "Ma";
        var mas = ImGui.CalcTextSize(ma);
        drawList.AddText(
            new Vector2(scaleX + (scaleWidth - mas.X) * 0.5f, contentStartPos.Y - 22f * _zoomLevel),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1)),
            ma);
    }

    private (float scaleWidth, float labelLaneWidth, float totalLeftGutter, float padLeft, float padBetween)
        ComputeLeftGutter()
    {
        var padLeft = 5f * _zoomLevel; // left padding inside child
        var padBetween = 8f * _zoomLevel; // between scale and labels / labels and columns
        var scaleWidth = 50f * _zoomLevel; // age scale box (you can keep your slider to change if needed)

        // widest single word among all orogeny names (you render one word per line)
        var maxWordWidth = 0f;
        foreach (var ev in _orogenicEvents)
        foreach (var w in ev.Name.Split(' '))
            maxWordWidth = Math.Max(maxWordWidth, ImGui.CalcTextSize(w).X);
        var labelLaneWidth = Math.Max(60f * _zoomLevel, maxWordWidth); // never smaller than 60px at zoom 1

        var total = padLeft + scaleWidth + padBetween + labelLaneWidth + padBetween; // last pad separates from columns
        return (scaleWidth, labelLaneWidth, total, padLeft, padBetween);
    }

    private float GetLeftGutterWidth()
    {
        // 5 px left pad + 50 px scale + 15 px label pad = 70 px at zoom 1
        return 70f * _zoomLevel;
    }

    private double CalculateAgeStep(double maxAge)
    {
        if (maxAge <= 10) return 1;
        if (maxAge <= 50) return 5;
        if (maxAge <= 100) return 10;
        if (maxAge <= 500) return 50;
        if (maxAge <= 1000) return 100;
        return 500;
    }

// Helpers
    private static bool CodesIntersect(string codes1, string codes2, out HashSet<string> common)
    {
        common = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(codes1) || string.IsNullOrWhiteSpace(codes2)) return false;

        var set1 = new HashSet<string>(
            codes1.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        foreach (var c in codes2.Split(',').Select(c => c.Trim()).Where(c => c.Length > 0))
            if (set1.Contains(c))
                common.Add(c);

        return common.Count > 0;
    }

    private static double RangeOverlapMa(StratigraphicUnit a, StratigraphicUnit b)
    {
        // Ages are Ma; StartAge > EndAge typically (older to younger). Normalize to [younger, older].
        var aMin = Math.Min(a.StartAge, a.EndAge);
        var aMax = Math.Max(a.StartAge, a.EndAge);
        var bMin = Math.Min(b.StartAge, b.EndAge);
        var bMax = Math.Max(b.StartAge, b.EndAge);

        var overlap = Math.Max(0.0, Math.Min(aMax, bMax) - Math.Max(aMin, bMin));
        return overlap; // in Ma
    }

    private static double MidAge(StratigraphicUnit u)
    {
        return 0.5 * (u.StartAge + u.EndAge);
    }

    private void ExportCorrelationTable()
    {
        try
        {
            // 1) Gather visible stratigraphies in the same order you draw/export
            var visibleStratigraphies = new List<(IStratigraphy strat, int index)>();
            for (var i = 0; i < _stratigraphies.Count; i++)
                if (_columnVisibility[i])
                    visibleStratigraphies.Add((_stratigraphies[i], i));

            if (visibleStratigraphies.Count < 2)
            {
                Logger.LogWarning(
                    "[StratigraphyCorrelationViewer] Need at least two visible stratigraphies to export a correlation table.");
                return;
            }

            // Build per-column units at the current display level (top-down by StartAge)
            var columnUnits = new List<List<StratigraphicUnit>>();
            foreach (var (strat, _) in visibleStratigraphies)
                columnUnits.Add(strat.GetUnitsByLevel(_displayLevel).OrderByDescending(u => u.StartAge).ToList());

            // 2) Correlate adjacent columns by shared InternationalCorrelationCode tokens
            //    For each unit pair (u1, u2) that shares at least one code, emit a CSV row.
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",",
                "LeftStratigraphy", "LeftUnitCode", "LeftUnitName", "LeftStartAgeMa", "LeftEndAgeMa",
                "RightStratigraphy", "RightUnitCode", "RightUnitName", "RightStartAgeMa", "RightEndAgeMa",
                "CommonCodes", "Relation", "YoungerBoundaryMa", "OlderBoundaryMa", "BoundaryPair",
                "BoundaryAgeDiffMa"));

            for (var c1 = 0; c1 < columnUnits.Count - 1; c1++)
            {
                var c2 = c1 + 1;
                var leftName = visibleStratigraphies[c1].strat.Name;
                var rightName = visibleStratigraphies[c2].strat.Name;

                var left = columnUnits[c1];
                var right = columnUnits[c2];

                foreach (var u1 in left.Where(u => !string.IsNullOrWhiteSpace(u.InternationalCorrelationCode)))
                {
                    var codes1 = new HashSet<string>(
                        u1.InternationalCorrelationCode.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var u2 in right.Where(u => !string.IsNullOrWhiteSpace(u.InternationalCorrelationCode)))
                    {
                        var codes2 = new HashSet<string>(
                            u2.InternationalCorrelationCode.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
                            StringComparer.OrdinalIgnoreCase);

                        var common = codes1.Intersect(codes2, StringComparer.OrdinalIgnoreCase).ToArray();
                        if (common.Length == 0) continue;

                        // Boundary logic
                        // Overlap on age axis (Ma): younger = max(EndAge), older = min(StartAge)
                        var youngerOverlap = Math.Max(u1.EndAge, u2.EndAge);
                        var olderOverlap = Math.Min(u1.StartAge, u2.StartAge);
                        var hasOverlap = youngerOverlap <= olderOverlap;
                        string relation, boundaryPair;
                        double youngerOut = double.NaN, olderOut = double.NaN, diffOut = double.NaN;

                        if (hasOverlap)
                        {
                            relation = "Overlap";
                            boundaryPair = "";
                            youngerOut = youngerOverlap;
                            olderOut = olderOverlap;
                            diffOut = olderOut - youngerOut; // overlap thickness (Ma)
                        }
                        else
                        {
                            // No overlap → connect the closest boundary pair (top↔top or base↔base)
                            var dTop = Math.Abs(u1.EndAge - u2.EndAge);
                            var dBase = Math.Abs(u1.StartAge - u2.StartAge);
                            if (dTop <= dBase)
                            {
                                relation = "NoOverlap";
                                boundaryPair = "Top-Top";
                                youngerOut =
                                    u1.EndAge; // report the two anchors as YoungerBoundary/OlderBoundary for convenience
                                olderOut = u2.EndAge;
                                diffOut = dTop;
                            }
                            else
                            {
                                relation = "NoOverlap";
                                boundaryPair = "Base-Base";
                                youngerOut = u1.StartAge;
                                olderOut = u2.StartAge;
                                diffOut = dBase;
                            }
                        }

                        // CSV row
                        sb.AppendLine(string.Join(",",
                            Csv(leftName),
                            Csv(u1.Code), Csv(u1.Name), u1.StartAge.ToString("0.###"), u1.EndAge.ToString("0.###"),
                            Csv(rightName),
                            Csv(u2.Code), Csv(u2.Name), u2.StartAge.ToString("0.###"), u2.EndAge.ToString("0.###"),
                            Csv(string.Join("|", common)),
                            Csv(relation),
                            double.IsNaN(youngerOut) ? "" : youngerOut.ToString("0.###"),
                            double.IsNaN(olderOut) ? "" : olderOut.ToString("0.###"),
                            Csv(hasOverlap ? "" : boundaryPair),
                            double.IsNaN(diffOut) ? "" : diffOut.ToString("0.###")
                        ));
                    }
                }
            }

            // 3) Save to Documents with timestamp
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var filePath = Path.Combine(docs, $"stratigraphy_correlations_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            Logger.Log($"[StratigraphyCorrelationViewer] Correlation table exported: {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError(
                $"[StratigraphyCorrelationViewer] Failed to export correlation table: {ex.Message}\n{ex.StackTrace}");
        }

        // --- local helper ---
        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // escape quotes and wrap if needed
            var needsQuotes = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
            var escaped = s.Replace("\"", "\"\"");
            return needsQuotes ? $"\"{escaped}\"" : escaped;
        }
    }

    private static (bool hasOverlap, double youngerOverlap, double olderOverlap) OverlapBounds(StratigraphicUnit a,
        StratigraphicUnit b)
    {
        // Ages are Ma: StartAge = older, EndAge = younger
        var olderOverlap = Math.Min(a.StartAge, b.StartAge);
        var youngerOverlap = Math.Max(a.EndAge, b.EndAge);
        return (youngerOverlap <= olderOverlap, youngerOverlap, olderOverlap);
    }

    private static (double aAnchor, double bAnchor) ClosestBoundaryPair(StratigraphicUnit a, StratigraphicUnit b)
    {
        // Candidate pairs: top↔top (EndAge), base↔base (StartAge)
        var dTop = Math.Abs(a.EndAge - b.EndAge);
        var dBase = Math.Abs(a.StartAge - b.StartAge);
        return dTop <= dBase ? (a.EndAge, b.EndAge) : (a.StartAge, b.StartAge);
    }

    private void ExportToPng(string filePath)
    {
        try
        {
            Logger.Log($"[StratigraphyCorrelationViewer] Starting PNG export to: {filePath}");

            var visibleStratigraphies = new List<(IStratigraphy strat, int index)>();
            for (var i = 0; i < _stratigraphies.Count; i++)
                if (_columnVisibility[i])
                    visibleStratigraphies.Add((_stratigraphies[i], i));

            if (visibleStratigraphies.Count == 0)
            {
                Logger.LogWarning("No stratigraphies visible for export.");
                return;
            }

            var columnUnits = new List<List<StratigraphicUnit>>();
            var maxAge = 0.0;
            foreach (var (strat, _) in visibleStratigraphies)
            {
                var units = strat.GetUnitsByLevel(_displayLevel).OrderByDescending(u => u.StartAge).ToList();
                columnUnits.Add(units);
                if (units.Any()) maxAge = Math.Max(maxAge, units.Max(u => u.StartAge));
            }

            var settings = new StratigraphyExportSettings
            {
                VisibleStratigraphies = visibleStratigraphies,
                ColumnUnits = columnUnits,
                OrogenicEvents = _orogenicEvents,
                MaxAge = maxAge,
                ZoomLevel = _zoomLevel,
                ColumnWidth = _columnWidth,
                AgeScale = _ageScale,
                ShowCorrelationLines = _showCorrelationLines,
                ShowAgeScale = _showAgeScale,
                DisplayLevel = _displayLevel
            };

            var exporter = new StratigraphyImageExporter(settings);
            exporter.Export(filePath);

            Logger.Log($"[StratigraphyCorrelationViewer] PNG export task completed for: {filePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[StratigraphyCorrelationViewer] Failed to export PNG: {ex.Message}\n{ex.StackTrace}");
        }
    }

    public class OrogenicEvent
    {
        public OrogenicEvent(string name, double startAge, double endAge, Vector4 color)
        {
            Name = name;
            StartAge = startAge;
            EndAge = endAge;
            Color = color;
        }

        public string Name { get; }
        public double StartAge { get; }
        public double EndAge { get; }
        public Vector4 Color { get; }
    }
}