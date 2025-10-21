// GeoscientistToolkit/UI/GIS/GeologicalMappingViewer.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.Stratigraphies;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Util;
using ImGuiNET;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping;

namespace GeoscientistToolkit.UI.GIS;

/// <summary>
///     Extended GIS viewer with geological mapping capabilities and profile tools
/// </summary>
public class GeologicalMappingViewer : GISViewer
{
    // --- NEW: Snapping and Vertex Editing ---
    private const float SnappingThresholdScreen = 10.0f; // pixels
    private readonly List<Vector2> _profileLine = new();
    private readonly List<ProfileGenerator.TopographicProfile> _savedProfiles = new();

    // --- NEW: Stratigraphy and Coloring ---
    private readonly StratigraphyManager _stratigraphyManager = StratigraphyManager.Instance;
    private string _ageSearchFilter = "";
    private CrossSectionGenerator.CrossSection _currentCrossSection;
    private GeologicalFeatureType _currentGeologicalType = GeologicalFeatureType.Formation;
    private ProfileGenerator.TopographicProfile _currentProfile;
    private string _description = "";
    private string _dipDirection = "N";
    private float _dipValue = 30f;
    private float _displacement;
    private GeologicalFeature _featureToEdit;
    private FormationColorMode _formationColorMode = FormationColorMode.Lithology;

    private string _formationName = "Formation A";

    // Geological mapping state
    private GeologicalEditMode _geologicalMode = GeologicalEditMode.None;
    private bool _isCovered;
    private bool _isDraggingVertex;
    private bool _isInferred;
    private string _lithologyCode = "Sandstone";
    private string _movementSense = "Normal";

    // Profile tool state
    private ProfileToolMode _profileMode = ProfileToolMode.None;
    private string _selectedAgeCode = "";

    // --- NEW: Borehole selection ---
    private int _selectedBoreholeIndex = -1;
    private GeologicalFeature _selectedGeologicalFeature;
    private int _selectedSegmentIndex = -1;
    private int _selectedStratigraphyIndex;
    private int _selectedVertexIndex = -1;

    // Cross-section state
    private bool _showCrossSectionWindow;

    // Geological property editor
    private bool _showGeologicalEditor;
    private bool _showGeologicalSymbols = true;
    private bool _showProfileWindow;
    private Vector2? _snappedWorldPos;
    private float _strikeValue;
    private float _symbolScale = 1.0f;
    private float _thickness = 100f;


    public GeologicalMappingViewer(GISDataset dataset) : base(dataset)
    {
        InitializeGeologicalLayer(dataset);
        InitializeStratigraphy();
    }

    public GeologicalMappingViewer(List<GISDataset> datasets) : base(datasets)
    {
        if (datasets.Count > 0)
            InitializeGeologicalLayer(datasets[0]);
        InitializeStratigraphy();
    }

    private GISLayer GeologicalLayer
    {
        get
        {
            var geolLayer = _dataset.Layers.FirstOrDefault(l => l.Name == "Geological Features");
            if (geolLayer == null)
            {
                // This should be created by InitializeGeologicalLayer, but as a fallback:
                geolLayer = new GISLayer
                    { Name = "Geological Features", Type = LayerType.Vector, IsVisible = true, IsEditable = true };
                _dataset.Layers.Add(geolLayer);
            }

            return geolLayer;
        }
    }

    private void InitializeStratigraphy()
    {
        _selectedStratigraphyIndex =
            _stratigraphyManager.AvailableStratigraphies.FindIndex(s => s == _stratigraphyManager.CurrentStratigraphy);
        if (_selectedStratigraphyIndex < 0) _selectedStratigraphyIndex = 0;
    }

    private void InitializeGeologicalLayer(GISDataset dataset)
    {
        // Check if geological layer already exists
        var geolLayer = dataset.Layers.FirstOrDefault(l => l.Name == "Geological Features");
        if (geolLayer == null)
        {
            geolLayer = new GISLayer
            {
                Name = "Geological Features",
                Type = LayerType.Vector,
                IsVisible = true,
                IsEditable = true,
                Color = new Vector4(0.8f, 0.5f, 0.2f, 1.0f)
            };
            dataset.Layers.Add(geolLayer);
            dataset.AddTag(GISTag.GeologicalMap);
        }
    }

    public override void DrawToolbarControls()
    {
        base.DrawToolbarControls();

        ImGui.Separator();
        ImGui.SameLine();

        // --- MODIFIED: Added Edit button and dynamic text ---
        var modeButtonText = _geologicalMode == GeologicalEditMode.None ? "Geological Mode" : "Stop Action";
        if (ImGui.Button(modeButtonText))
        {
            _geologicalMode = _geologicalMode == GeologicalEditMode.None
                ? GeologicalEditMode.DrawFormation
                : GeologicalEditMode.None;
            _featureToEdit = null; // Exit editing when stopping
        }

        if (ImGui.Button("Edit Vertices"))
        {
            _geologicalMode = GeologicalEditMode.EditVertices;
            _currentDrawing.Clear();
            _featureToEdit = null;
        }

        if (_geologicalMode != GeologicalEditMode.None && _geologicalMode != GeologicalEditMode.EditVertices)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);
            if (ImGui.BeginCombo("##GeolType", _currentGeologicalType.ToString()))
            {
                foreach (var type in Enum.GetValues<GeologicalFeatureType>())
                    if (ImGui.Selectable(type.ToString(), type == _currentGeologicalType))
                    {
                        _currentGeologicalType = type;
                        UpdateGeologicalEditMode();
                    }

                ImGui.EndCombo();
            }

            if (_currentGeologicalType == GeologicalFeatureType.Borehole)
            {
                ImGui.SameLine();
                var boreholeDatasets = ProjectManager.Instance.LoadedDatasets.OfType<BoreholeDataset>().ToList();
                var boreholeNames = boreholeDatasets.Select(b => b.Name).ToArray();

                ImGui.SetNextItemWidth(150);
                if (boreholeNames.Length > 0)
                {
                    if (_selectedBoreholeIndex >= boreholeNames.Length || _selectedBoreholeIndex < 0)
                        _selectedBoreholeIndex = 0;
                    ImGui.Combo("Borehole##BoreholeSelect", ref _selectedBoreholeIndex, boreholeNames,
                        boreholeNames.Length);
                }
                else
                {
                    ImGui.TextDisabled("No boreholes loaded");
                }
            }
        }

        if (_geologicalMode != GeologicalEditMode.None)
        {
            ImGui.SameLine();
            if (ImGui.Button("Properties"))
                _showGeologicalEditor = !_showGeologicalEditor;
        }


        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // Profile tool controls
        if (ImGui.Button(_profileMode == ProfileToolMode.None ? "Profile Tool" : "Cancel Profile"))
        {
            if (_profileMode == ProfileToolMode.None)
            {
                _profileMode = ProfileToolMode.DrawingLine;
                _profileLine.Clear();
            }
            else
            {
                _profileMode = ProfileToolMode.None;
                _profileLine.Clear();
            }
        }

        if (_savedProfiles.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button($"Profiles ({_savedProfiles.Count})"))
                _showProfileWindow = !_showProfileWindow;
        }

        ImGui.SameLine();
        ImGui.Checkbox("Symbols", ref _showGeologicalSymbols);

        if (_showGeologicalSymbols)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.SliderFloat("##SymbolScale", ref _symbolScale, 0.5f, 3.0f, "Scale: %.1f");
        }

        ImGui.SameLine();
        ImGui.Separator();
        ImGui.SameLine();

        // --- NEW: Formation color mode ---
        ImGui.Text("Color Formations by:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Lithology", _formationColorMode == FormationColorMode.Lithology))
            _formationColorMode = FormationColorMode.Lithology;
        ImGui.SameLine();
        if (ImGui.RadioButton("Age", _formationColorMode == FormationColorMode.Age))
            _formationColorMode = FormationColorMode.Age;
    }

    public override void DrawContent(ref float zoom, ref Vector2 pan)
    {
        base.DrawContent(ref zoom, ref pan);

        // Draw additional geological features on top
        var drawList = ImGui.GetWindowDrawList();
        var canvas_pos = ImGui.GetCursorScreenPos();
        var canvas_size = ImGui.GetContentRegionAvail();
        var statusBarHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y * 2;
        canvas_size.Y -= statusBarHeight;

        drawList.PushClipRect(canvas_pos, canvas_pos + canvas_size, true);

        // Draw geological features with symbols
        if (_showGeologicalSymbols) DrawGeologicalSymbols(drawList, canvas_pos, canvas_size, zoom, pan);

        // --- NEW: Draw vertex handles if a feature is being edited ---
        if (_geologicalMode == GeologicalEditMode.EditVertices && _featureToEdit != null)
            DrawVertexHandles(drawList, canvas_pos, canvas_size, zoom, pan);

        // Draw profile line if in profile mode
        if (_profileMode == ProfileToolMode.DrawingLine && _profileLine.Count > 0)
            DrawProfileLine(drawList, canvas_pos, canvas_size, zoom, pan);

        // --- NEW: Draw snapping indicator ---
        if (_snappedWorldPos.HasValue)
        {
            var screenPos = WorldToScreen(_snappedWorldPos.Value, canvas_pos, canvas_size, zoom, pan);
            drawList.AddCircle(screenPos, SnappingThresholdScreen * 0.8f, ImGui.GetColorU32(new Vector4(1, 0, 1, 1)), 0,
                2f);
        }

        drawList.PopClipRect();

        // Handle input for geological and profile tools
        var io = ImGui.GetIO();
        var is_hovered = ImGui.IsItemHovered();
        var worldPos = ScreenToWorld(io.MousePos - canvas_pos, canvas_pos, canvas_size, zoom, pan);

        HandleSnappingAndEditing(worldPos, is_hovered, zoom, pan, canvas_pos, canvas_size);

        if (is_hovered && io.MouseClicked[0])
        {
            var clickPos = _snappedWorldPos ?? worldPos;

            if (_profileMode == ProfileToolMode.DrawingLine)
                HandleProfileClick(clickPos);
            else if (_geologicalMode != GeologicalEditMode.None) HandleGeologicalClick(clickPos);
        }

        // Draw windows
        if (_showGeologicalEditor)
            DrawGeologicalPropertiesWindow();

        if (_showProfileWindow)
            DrawProfileWindow();

        if (_showCrossSectionWindow && _currentCrossSection != null)
            DrawCrossSectionWindow();
    }

    private void DrawGeologicalSymbols(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize,
        float zoom, Vector2 pan)
    {
        var symbolColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
        var scale = 20f * _symbolScale * Math.Min(zoom / 10f, 2f); // Scale with zoom

        foreach (var feature in GeologicalLayer.Features.OfType<GeologicalFeature>())
        {
            // --- NEW: Highlight feature being edited ---
            var featureIsBeingEdited = _featureToEdit == feature;

            // Draw different symbols based on feature type
            switch (feature.GeologicalType)
            {
                case GeologicalFeatureType.StrikeDip:
                    if (feature.Coordinates.Count > 0)
                    {
                        var screenPos = WorldToScreen(feature.Coordinates[0], canvasPos, canvasSize, zoom, pan);
                        var symbols = GeologicalSymbols.GenerateStrikeDipSymbol(
                            screenPos, feature.Strike ?? 0, feature.Dip ?? 0, scale);

                        foreach (var symbol in symbols)
                            if (symbol.Length >= 2)
                                for (var i = 0; i < symbol.Length - 1; i++)
                                    drawList.AddLine(symbol[i], symbol[i + 1], symbolColor, 2f);

                        // Add dip value text
                        if (feature.Dip.HasValue)
                        {
                            var text = $"{feature.Dip:0}°";
                            drawList.AddText(screenPos + new Vector2(scale * 0.4f, -scale * 0.3f),
                                symbolColor, text);
                        }
                    }

                    break;

                case GeologicalFeatureType.Fault_Normal:
                case GeologicalFeatureType.Fault_Reverse:
                case GeologicalFeatureType.Fault_Transform:
                case GeologicalFeatureType.Fault_Thrust:
                    if (feature.Coordinates.Count >= 2)
                    {
                        var screenCoords = feature.Coordinates
                            .Select(c => WorldToScreen(c, canvasPos, canvasSize, zoom, pan))
                            .ToArray();

                        var faultSymbols = GeologicalSymbols.GenerateFaultSymbol(
                            screenCoords, feature.GeologicalType, scale, feature.MovementSense);

                        foreach (var symbol in faultSymbols)
                            if (symbol.Length >= 2)
                            {
                                var isClosed = symbol[0] == symbol[^1];
                                if (isClosed && symbol.Length > 2)
                                    // Draw filled polygon for certain symbols
                                    drawList.AddConvexPolyFilled(ref symbol[0], symbol.Length - 1,
                                        ImGui.GetColorU32(new Vector4(0.8f, 0.2f, 0.2f, 0.5f)));
                                else
                                    for (var i = 0; i < symbol.Length - 1; i++)
                                        drawList.AddLine(symbol[i], symbol[i + 1],
                                            ImGui.GetColorU32(new Vector4(0.8f, 0.2f, 0.2f, 1f)),
                                            feature.IsInferred ? 1f : featureIsBeingEdited ? 3f : 2f);
                            }
                    }

                    break;

                case GeologicalFeatureType.Bedding:
                    if (feature.Coordinates.Count > 0)
                    {
                        var screenPos = WorldToScreen(feature.Coordinates[0], canvasPos, canvasSize, zoom, pan);
                        var symbols = GeologicalSymbols.GenerateBeddingSymbol(
                            screenPos, feature.Strike ?? 0, feature.Dip ?? 0, false, scale);

                        foreach (var symbol in symbols)
                            if (symbol.Length >= 2)
                                for (var i = 0; i < symbol.Length - 1; i++)
                                    drawList.AddLine(symbol[i], symbol[i + 1], symbolColor, 1.5f);
                    }

                    break;

                case GeologicalFeatureType.Anticline:
                case GeologicalFeatureType.Syncline:
                    if (feature.Coordinates.Count >= 2)
                    {
                        var screenCoords = feature.Coordinates
                            .Select(c => WorldToScreen(c, canvasPos, canvasSize, zoom, pan))
                            .ToArray();

                        var foldSymbols = GeologicalSymbols.GenerateFoldSymbol(
                            screenCoords, feature.GeologicalType, scale, feature.Plunge);

                        foreach (var symbol in foldSymbols)
                            if (symbol.Length >= 2)
                                for (var i = 0; i < symbol.Length - 1; i++)
                                    drawList.AddLine(symbol[i], symbol[i + 1],
                                        ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.8f, 1f)),
                                        featureIsBeingEdited ? 3f : 2f);
                    }

                    break;

                case GeologicalFeatureType.Formation:
                    // Draw formation polygons with lithology colors
                    if (feature.Coordinates.Count >= 3)
                    {
                        var screenCoords = feature.Coordinates
                            .Select(c => WorldToScreen(c, canvasPos, canvasSize, zoom, pan))
                            .ToArray();

                        Vector4 color;
                        if (_formationColorMode == FormationColorMode.Age && !string.IsNullOrEmpty(feature.AgeCode))
                        {
                            var unit = _stratigraphyManager.GetUnitByCode(feature.AgeCode);
                            if (unit != null)
                                // Convert System.Drawing.Color to Vector4
                                color = new Vector4(unit.Color.R / 255f, unit.Color.G / 255f, unit.Color.B / 255f,
                                    1.0f);
                            else
                                // Fallback color if age code is invalid
                                color = new Vector4(0.5f, 0.5f, 0.5f, 0.4f);
                        }
                        else // Default to lithology color
                        {
                            color = LithologyPatterns.StandardColors
                                .GetValueOrDefault(feature.LithologyCode ?? "Sandstone",
                                    new Vector4(0.7f, 0.7f, 0.7f, 0.4f));
                        }

                        var fillColor = ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.4f));
                        var borderColor = ImGui.GetColorU32(new Vector4(color.X * 0.7f, color.Y * 0.7f,
                            color.Z * 0.7f, 1f));

                        drawList.AddConvexPolyFilled(ref screenCoords[0], screenCoords.Length, fillColor);
                        drawList.AddPolyline(ref screenCoords[0], screenCoords.Length, borderColor,
                            ImDrawFlags.Closed, feature.IsInferred ? 1f : featureIsBeingEdited ? 3f : 2f);

                        // Add formation label
                        if (!string.IsNullOrEmpty(feature.FormationName) && zoom > 5f)
                        {
                            var center = screenCoords.Aggregate(Vector2.Zero, (a, b) => a + b) / screenCoords.Length;
                            drawList.AddText(center, ImGui.GetColorU32(ImGuiCol.Text), feature.FormationName);
                        }
                    }

                    break;

                case GeologicalFeatureType.Borehole:
                    if (feature.Coordinates.Count > 0)
                    {
                        var screenPos = WorldToScreen(feature.Coordinates[0], canvasPos, canvasSize, zoom, pan);
                        var boreholeColor = ImGui.GetColorU32(new Vector4(0.1f, 0.8f, 0.8f, 1.0f));
                        var symbols = GeologicalSymbols.GenerateBoreholeSymbol(screenPos, scale);

                        foreach (var symbol in symbols)
                            if (symbol.Length >= 2)
                                drawList.AddPolyline(ref symbol[0], symbol.Length, boreholeColor, ImDrawFlags.None, 2f);

                        // Add borehole name text
                        if (!string.IsNullOrEmpty(feature.BoreholeName))
                            drawList.AddText(screenPos + new Vector2(scale * 0.5f, -scale * 0.3f),
                                boreholeColor, feature.BoreholeName);
                    }

                    break;
            }
        }
    }

    private void DrawProfileLine(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize,
        float zoom, Vector2 pan)
    {
        var lineColor = ImGui.GetColorU32(new Vector4(1f, 0f, 1f, 1f));

        if (_profileLine.Count >= 1)
        {
            var screenPoints = _profileLine.Select(p => WorldToScreen(p, canvasPos, canvasSize, zoom, pan)).ToList();

            // Draw the line
            for (var i = 0; i < screenPoints.Count - 1; i++)
                drawList.AddLine(screenPoints[i], screenPoints[i + 1], lineColor, 3f);

            // Draw points
            foreach (var point in screenPoints) drawList.AddCircleFilled(point, 5f, lineColor);

            // Draw distance text
            if (_profileLine.Count == 2)
            {
                var distance = Vector2.Distance(_profileLine[0], _profileLine[1]);
                var midPoint = (screenPoints[0] + screenPoints[1]) * 0.5f;
                drawList.AddText(midPoint + new Vector2(10, -20), lineColor,
                    $"Distance: {distance:F1} units");
            }
        }

        // Draw preview line to mouse position
        if (_profileLine.Count == 1)
        {
            var io = ImGui.GetIO();
            var mouseScreen = io.MousePos;
            var lastScreen = WorldToScreen(_profileLine[0], canvasPos, canvasSize, zoom, pan);

            // Dashed line to mouse
            DrawDashedLine(drawList, lastScreen, mouseScreen, lineColor, 2f);
        }
    }

    private void HandleProfileClick(Vector2 worldPos)
    {
        _profileLine.Add(worldPos);

        if (_profileLine.Count == 2)
        {
            // Generate profile
            GenerateProfile();
            _profileMode = ProfileToolMode.None;
        }
    }

    private void HandleGeologicalClick(Vector2 worldPos)
    {
        if (_geologicalMode == GeologicalEditMode.EditVertices)
        {
            if (_featureToEdit == null)
            {
                // Select a feature to edit
                var selected = FindClosestFeature(worldPos, SnappingThresholdScreen / _currentZoom);
                if (selected != null)
                {
                    _featureToEdit = selected as GeologicalFeature;
                    Logger.Log($"Selected feature '{_featureToEdit?.GeologicalType}' for editing.");
                }
            }

            return;
        }

        // Create appropriate geological feature based on current type
        var feature = new GeologicalFeature
        {
            GeologicalType = _currentGeologicalType,
            FormationName = _formationName,
            LithologyCode = _lithologyCode,
            AgeCode = _selectedAgeCode,
            Strike = _strikeValue,
            Dip = _dipValue,
            DipDirection = _dipDirection,
            Thickness = _thickness,
            Displacement = _displacement,
            MovementSense = _movementSense,
            IsInferred = _isInferred,
            IsCovered = _isCovered,
            Description = _description
        };

        if (_currentGeologicalType == GeologicalFeatureType.Borehole)
        {
            var boreholeDatasets = ProjectManager.Instance.LoadedDatasets.OfType<BoreholeDataset>().ToList();
            if (boreholeDatasets.Count > 0 && _selectedBoreholeIndex >= 0 &&
                _selectedBoreholeIndex < boreholeDatasets.Count)
            {
                feature.BoreholeName = boreholeDatasets[_selectedBoreholeIndex].Name;
            }
            else
            {
                Logger.LogWarning("Cannot place borehole: No borehole dataset selected or loaded.");
                return;
            }
        }

        // Determine geometry type based on geological feature type
        if (IsPointFeature(_currentGeologicalType))
        {
            feature.Type = FeatureType.Point;
            feature.Coordinates.Add(worldPos);
            feature.UpdateProperties();
            AddToGeologicalLayer(feature);
        }
        else if (IsLineFeature(_currentGeologicalType))
        {
            // Start drawing line
            if (_currentDrawing.Count == 0)
            {
                _currentDrawing.Add(worldPos);
            }
            else
            {
                _currentDrawing.Add(worldPos);
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    feature.Type = FeatureType.Line;
                    feature.Coordinates = new List<Vector2>(_currentDrawing);
                    feature.UpdateProperties();
                    AddToGeologicalLayer(feature);
                    _currentDrawing.Clear();
                }
            }
        }
        else if (IsPolygonFeature(_currentGeologicalType))
        {
            // Start drawing polygon
            _currentDrawing.Add(worldPos);
            if (_currentDrawing.Count >= 3 && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                feature.Type = FeatureType.Polygon;
                feature.Coordinates = new List<Vector2>(_currentDrawing);
                feature.UpdateProperties();
                AddToGeologicalLayer(feature);
                _currentDrawing.Clear();
            }
        }
    }

    private void AddToGeologicalLayer(GeologicalFeature feature)
    {
        GeologicalLayer.Features.Add(feature);
        _dataset.UpdateBounds();
        Logger.Log($"Added {feature.GeologicalType} to geological layer");
    }

    private void GenerateProfile()
    {
        if (_profileLine.Count != 2)
            return;

        // Check if we have a DEM loaded
        GISRasterLayer demLayer = null;
        var demDataset =
            _datasets.FirstOrDefault(ds => ds.HasTag(GISTag.DEM) && ds.Layers.Any(l => l is GISRasterLayer));
        if (demDataset != null) demLayer = demDataset.Layers.OfType<GISRasterLayer>().First();

        if (demLayer == null)
        {
            Logger.LogWarning("No DEM layer found. Cannot generate topographic profile.");
            _profileLine.Clear();
            return;
        }

        var demData = demLayer.GetPixelData();
        var demBounds = demLayer.Bounds;
        var featuresForProfile = GeologicalLayer.Features.OfType<GeologicalFeature>().ToList();


        _currentProfile = ProfileGenerator.GenerateProfile(
            demData, demBounds, _profileLine[0], _profileLine[1], 100, featuresForProfile);

        _savedProfiles.Add(_currentProfile);
        _showProfileWindow = true;
        _profileLine.Clear();

        Logger.Log($"Generated profile '{_currentProfile.Name}' with {_currentProfile.Points.Count} points.");
    }

    private void GenerateCrossSection()
    {
        if (_currentProfile == null) return;
        var allGeologicalFeatures = GeologicalLayer.Features.OfType<GeologicalFeature>();

        var formations = allGeologicalFeatures.Where(f => f.GeologicalType == GeologicalFeatureType.Formation).ToList();
        var faults = allGeologicalFeatures.Where(f => CrossSectionGenerator.IsFaultType(f.GeologicalType)).ToList();

        _currentCrossSection = CrossSectionGenerator.GenerateCrossSection(_currentProfile, formations, faults);
        _showCrossSectionWindow = true;

        Logger.Log("Generated geological cross-section.");
    }

    private void DrawProfileWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(800, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Topographic Profile", ref _showProfileWindow))
        {
            if (_currentProfile == null)
            {
                ImGui.Text("No profile generated.");
                ImGui.End();
                return;
            }

            ImGui.Text(_currentProfile.Name);
            ImGui.SameLine();

            var ve = _currentProfile.VerticalExaggeration;
            if (ImGui.SliderFloat("VE", ref ve, 1f, 10f, "%.1fx")) _currentProfile.VerticalExaggeration = ve;

            ImGui.SameLine();
            if (ImGui.Button("Generate Cross-Section")) GenerateCrossSection();

            ImGui.Separator();

            var drawList = ImGui.GetWindowDrawList();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            drawList.AddRectFilled(canvasPos, canvasPos + canvasSize, ImGui.GetColorU32(ImGuiCol.FrameBg));

            var margin = new Vector2(50, 40);
            var plotSize = canvasSize - margin * 2;
            var plotOrigin = canvasPos + new Vector2(margin.X, canvasSize.Y - margin.Y);

            // Data range
            var distRange = _currentProfile.TotalDistance;
            var elevRange = _currentProfile.MaxElevation - _currentProfile.MinElevation;
            if (elevRange < 1f) elevRange = 1f;

            // Draw profile line
            var profilePoints = _currentProfile.Points.Select(p =>
            {
                var x = p.Distance / distRange * plotSize.X;
                var y = (p.Elevation - _currentProfile.MinElevation) / elevRange * plotSize.Y *
                        _currentProfile.VerticalExaggeration;
                return plotOrigin + new Vector2(x, -y);
            }).ToArray();

            if (profilePoints.Length > 1)
                drawList.AddPolyline(ref profilePoints[0], profilePoints.Length,
                    ImGui.GetColorU32(new Vector4(0.3f, 0.8f, 0.4f, 1f)), ImDrawFlags.None, 2f);

            // Draw axes
            drawList.AddLine(plotOrigin, plotOrigin + new Vector2(plotSize.X, 0), ImGui.GetColorU32(ImGuiCol.Text), 1f);
            drawList.AddLine(plotOrigin, plotOrigin + new Vector2(0, -plotSize.Y), ImGui.GetColorU32(ImGuiCol.Text),
                1f);
            drawList.AddText(plotOrigin + new Vector2(plotSize.X / 2, 10), ImGui.GetColorU32(ImGuiCol.Text),
                "Distance");
            drawList.AddText(plotOrigin + new Vector2(-margin.X, -plotSize.Y / 2), ImGui.GetColorU32(ImGuiCol.Text),
                "Elevation");

            ImGui.End();
        }
    }

    private void DrawCrossSectionWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(800, 500), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Geological Cross-Section", ref _showCrossSectionWindow))
        {
            if (_currentCrossSection == null || _currentCrossSection.Profile == null)
            {
                ImGui.Text("No cross-section data.");
                ImGui.End();
                return;
            }

            var ve = _currentCrossSection.VerticalExaggeration;
            if (ImGui.SliderFloat("VE", ref ve, 1f, 10f, "%.1fx")) _currentCrossSection.VerticalExaggeration = ve;
            ImGui.Separator();

            var drawList = ImGui.GetWindowDrawList();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            drawList.AddRectFilled(canvasPos, canvasPos + canvasSize, ImGui.GetColorU32(ImGuiCol.FrameBg));

            var margin = new Vector2(50, 40);
            var plotSize = canvasSize - margin * 2;
            var plotOrigin = canvasPos + new Vector2(margin.X, canvasSize.Y - margin.Y);

            var profile = _currentCrossSection.Profile;
            var distRange = profile.TotalDistance;
            var elevRange = profile.MaxElevation - profile.MinElevation;
            if (elevRange < 1f) elevRange = 1f;

            // Draw formations
            foreach (var formation in _currentCrossSection.Formations)
            {
                if (formation.TopBoundary.Count < 2) continue;
                var polyPoints = new List<Vector2>();
                polyPoints.AddRange(formation.TopBoundary);
                polyPoints.AddRange(formation.BottomBoundary.AsEnumerable().Reverse());

                var screenPoly = polyPoints.Select(p =>
                {
                    var x = p.X / distRange * plotSize.X;
                    var y = (p.Y - profile.MinElevation) / elevRange * plotSize.Y *
                            _currentCrossSection.VerticalExaggeration;
                    return plotOrigin + new Vector2(x, -y);
                }).ToArray();

                if (screenPoly.Length > 2)
                    drawList.AddConvexPolyFilled(ref screenPoly[0], screenPoly.Length,
                        ImGui.ColorConvertFloat4ToU32(formation.Color));
            }

            // Draw faults
            foreach (var fault in _currentCrossSection.Faults)
            {
                var faultPoints = fault.FaultTrace.Select(p =>
                {
                    var x = p.X / distRange * plotSize.X;
                    var y = (p.Y - profile.MinElevation) / elevRange * plotSize.Y *
                            _currentCrossSection.VerticalExaggeration;
                    return plotOrigin + new Vector2(x, -y);
                }).ToArray();
                if (faultPoints.Length > 1)
                    drawList.AddPolyline(ref faultPoints[0], faultPoints.Length,
                        ImGui.GetColorU32(new Vector4(1, 0, 0, 1)), ImDrawFlags.None, 2f);
            }

            // Draw profile on top
            var profilePoints = profile.Points.Select(p =>
            {
                var x = p.Distance / distRange * plotSize.X;
                var y = (p.Elevation - profile.MinElevation) / elevRange * plotSize.Y *
                        _currentCrossSection.VerticalExaggeration;
                return plotOrigin + new Vector2(x, -y);
            }).ToArray();
            if (profilePoints.Length > 1)
                drawList.AddPolyline(ref profilePoints[0], profilePoints.Length,
                    ImGui.GetColorU32(new Vector4(0, 0, 0, 1f)), ImDrawFlags.None, 2.5f);

            ImGui.End();
        }
    }

    private void DrawGeologicalPropertiesWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(350, 550), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Geological Properties", ref _showGeologicalEditor))
        {
            ImGui.InputText("Formation", ref _formationName, 128);
            ImGui.InputText("Lithology", ref _lithologyCode, 64);

            ImGui.Separator();
            ImGui.Text("Stratigraphy");

            // --- NEW: Stratigraphy and Age selection ---
            var stratNames = _stratigraphyManager.AvailableStratigraphies.Select(s => s.Name).ToArray();
            if (ImGui.Combo("System", ref _selectedStratigraphyIndex, stratNames, stratNames.Length))
            {
                _stratigraphyManager.CurrentStratigraphy =
                    _stratigraphyManager.AvailableStratigraphies[_selectedStratigraphyIndex];
                _selectedAgeCode = ""; // Reset selection when changing system
            }

            var currentUnitName = "Select Unit...";
            if (!string.IsNullOrEmpty(_selectedAgeCode))
            {
                var unit = _stratigraphyManager.GetUnitByCode(_selectedAgeCode);
                if (unit != null) currentUnitName = unit.Name;
            }

            ImGui.Text("Geological Age:");
            if (ImGui.BeginCombo("##AgeSelector", currentUnitName))
            {
                ImGui.InputTextWithHint("##search", "Search...", ref _ageSearchFilter, 100);
                ImGui.Separator();

                if (_stratigraphyManager.CurrentStratigraphy != null)
                {
                    var allUnits = _stratigraphyManager.CurrentStratigraphy.GetAllUnits()
                        .Where(u => string.IsNullOrEmpty(_ageSearchFilter) ||
                                    u.Name.Contains(_ageSearchFilter, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(u => u.StartAge);

                    foreach (var unit in allUnits)
                        if (ImGui.Selectable($"{unit.Name} ({unit.Level})", unit.Code == _selectedAgeCode))
                            _selectedAgeCode = unit.Code;
                }

                ImGui.EndCombo();
            }

            ImGui.Separator();
            ImGui.SliderFloat("Strike", ref _strikeValue, 0f, 360f, "%.0f°");
            ImGui.SliderFloat("Dip", ref _dipValue, 0f, 90f, "%.0f°");
            ImGui.InputText("Dip Direction", ref _dipDirection, 8);
            ImGui.Separator();
            ImGui.InputFloat("Thickness (m)", ref _thickness);
            ImGui.InputFloat("Displacement (m)", ref _displacement);
            ImGui.InputText("Movement Sense", ref _movementSense, 64);
            ImGui.Separator();
            ImGui.Checkbox("Inferred", ref _isInferred);
            ImGui.SameLine();
            ImGui.Checkbox("Covered", ref _isCovered);
            ImGui.Separator();
            ImGui.InputTextMultiline("Description", ref _description, 1024, new Vector2(-1, 80));
            ImGui.End();
        }
    }

    private void UpdateGeologicalEditMode()
    {
        _featureToEdit = null;
        _currentDrawing.Clear();

        if (IsPointFeature(_currentGeologicalType))
            _geologicalMode = GeologicalEditMode.DrawPoint;
        else if (IsLineFeature(_currentGeologicalType))
            _geologicalMode = GeologicalEditMode.DrawLine;
        else if (IsPolygonFeature(_currentGeologicalType))
            _geologicalMode = GeologicalEditMode.DrawFormation;
        else
            _geologicalMode = GeologicalEditMode.None;
    }

    private bool IsPointFeature(GeologicalFeatureType type)
    {
        return type switch
        {
            GeologicalFeatureType.StrikeDip or GeologicalFeatureType.Bedding or GeologicalFeatureType.Sample
                or GeologicalFeatureType.Outcrop or GeologicalFeatureType.Borehole => true,
            _ => false
        };
    }

    private bool IsLineFeature(GeologicalFeatureType type)
    {
        return type switch
        {
            GeologicalFeatureType.Fault_Normal or GeologicalFeatureType.Fault_Reverse
                or GeologicalFeatureType.Fault_Transform or
                GeologicalFeatureType.Fault_Thrust or GeologicalFeatureType.Fault_Detachment
                or GeologicalFeatureType.Fault_Undefined or
                GeologicalFeatureType.Anticline or GeologicalFeatureType.Syncline or GeologicalFeatureType.Dike
                or GeologicalFeatureType.Vein => true,
            _ => false
        };
    }

    private bool IsPolygonFeature(GeologicalFeatureType type)
    {
        return type switch
        {
            GeologicalFeatureType.Formation or GeologicalFeatureType.Intrusion
                or GeologicalFeatureType.Unconformity => true,
            _ => false
        };
    }

    // --- NEW METHODS for Snapping and Vertex Editing ---

    private void HandleSnappingAndEditing(Vector2 worldPos, bool isHovered, float zoom, Vector2 pan, Vector2 canvasPos,
        Vector2 canvasSize)
    {
        _snappedWorldPos = null;
        if (!isHovered)
        {
            _isDraggingVertex = false;
            return;
        }

        var snappingThresholdWorld = SnappingThresholdScreen / zoom;

        // --- Vertex Dragging Logic ---
        if (_isDraggingVertex && _featureToEdit != null)
        {
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _isDraggingVertex = false;
                _selectedVertexIndex = -1;
                _selectedSegmentIndex = -1;
            }
            else
            {
                var targetPos = worldPos;
                // Snap while dragging
                foreach (var feature in GeologicalLayer.Features.Where(f => f != _featureToEdit))
                foreach (var vertex in feature.Coordinates)
                    if (Vector2.Distance(worldPos, vertex) < snappingThresholdWorld)
                    {
                        targetPos = vertex;
                        break;
                    }

                if (_selectedVertexIndex != -1) _featureToEdit.Coordinates[_selectedVertexIndex] = targetPos;
            }
        }

        // --- Hover and Snap Logic ---
        if (!_isDraggingVertex)
        {
            // First, check for snapping to any feature vertex
            foreach (var feature in GeologicalLayer.Features)
            foreach (var vertex in feature.Coordinates)
                if (Vector2.Distance(worldPos, vertex) < snappingThresholdWorld)
                {
                    _snappedWorldPos = vertex;
                    goto SnappingFound; // Exit loops once a snap is found
                }

            SnappingFound:

            if (_geologicalMode == GeologicalEditMode.EditVertices && _featureToEdit != null)
                // Check for interaction with the vertices and segments of the feature being edited
                // This overrides general snapping if we are close to a handle
                CheckVertexHandlesInteraction(worldPos, snappingThresholdWorld);
        }
    }

    private void CheckVertexHandlesInteraction(Vector2 worldPos, float threshold)
    {
        // Check for dragging main vertices
        for (var i = 0; i < _featureToEdit.Coordinates.Count; i++)
            if (Vector2.Distance(worldPos, _featureToEdit.Coordinates[i]) < threshold)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _isDraggingVertex = true;
                    _selectedVertexIndex = i;
                    return;
                }

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                {
                    // Delete vertex, but ensure geometry remains valid
                    var minVertices = _featureToEdit.Type == FeatureType.Polygon ? 3 : 2;
                    if (_featureToEdit.Coordinates.Count > minVertices)
                    {
                        _featureToEdit.Coordinates.RemoveAt(i);
                        Logger.Log($"Removed vertex {i} from feature.");
                    }

                    return;
                }
            }

        // Check for dragging segment mid-points to add a vertex
        for (var i = 0; i < _featureToEdit.Coordinates.Count; i++)
        {
            var p1 = _featureToEdit.Coordinates[i];
            var p2 = _featureToEdit.Coordinates[(i + 1) % _featureToEdit.Coordinates.Count];
            var midPoint = p1 + (p2 - p1) * 0.5f;

            if (Vector2.Distance(worldPos, midPoint) < threshold)
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _isDraggingVertex = true;
                    _selectedSegmentIndex = i;
                    // Insert new vertex at the midpoint and start dragging it
                    _featureToEdit.Coordinates.Insert(i + 1, midPoint);
                    _selectedVertexIndex = i + 1;
                    return;
                }
            }
        }
    }

    private void DrawVertexHandles(ImDrawListPtr drawList, Vector2 canvasPos, Vector2 canvasSize, float zoom,
        Vector2 pan)
    {
        var vertexColor = ImGui.GetColorU32(new Vector4(1, 0, 1, 1));
        var midPointColor = ImGui.GetColorU32(new Vector4(1, 0, 1, 0.5f));
        var handleRadius = 6f;

        // Draw main vertices
        foreach (var vertex in _featureToEdit.Coordinates)
        {
            var screenPos = WorldToScreen(vertex, canvasPos, canvasSize, zoom, pan);
            drawList.AddCircleFilled(screenPos, handleRadius, vertexColor);
        }

        // Draw mid-point handles for adding new vertices
        var count = _featureToEdit.Coordinates.Count;
        if (count < 2) return;

        for (var i = 0; i < count; i++)
        {
            // For polygons, the last segment connects back to the first vertex
            if (_featureToEdit.Type != FeatureType.Polygon && i == count - 1) break;

            var p1 = _featureToEdit.Coordinates[i];
            var p2 = _featureToEdit.Coordinates[(i + 1) % count];
            var midPoint = p1 + (p2 - p1) * 0.5f;
            var screenPos = WorldToScreen(midPoint, canvasPos, canvasSize, zoom, pan);
            drawList.AddRectFilled(screenPos - new Vector2(handleRadius / 2), screenPos + new Vector2(handleRadius / 2),
                midPointColor);
        }
    }

    private GISFeature FindClosestFeature(Vector2 worldPos, float threshold)
    {
        GISFeature closestFeature = null;
        var minDistance = float.MaxValue;

        foreach (var feature in GeologicalLayer.Features)
        {
            if (feature.Type != FeatureType.Line && feature.Type != FeatureType.Polygon) continue;

            for (var i = 0; i < feature.Coordinates.Count; i++)
            {
                var p1 = feature.Coordinates[i];
                var p2 = feature.Coordinates[(i + 1) % feature.Coordinates.Count];
                if (feature.Type == FeatureType.Line && i == feature.Coordinates.Count - 1) continue;

                var dist = ProfileGenerator.DistanceToLineSegment(worldPos, p1, p2);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestFeature = feature;
                }
            }
        }

        if (minDistance < threshold) return closestFeature;

        return null;
    }

    private enum FormationColorMode
    {
        Lithology,
        Age
    }

    private enum GeologicalEditMode
    {
        None,
        DrawPoint,
        DrawLine,
        DrawFormation,
        EditVertices
    }

    private enum ProfileToolMode
    {
        None,
        DrawingLine,
        ViewingProfile
    }
}