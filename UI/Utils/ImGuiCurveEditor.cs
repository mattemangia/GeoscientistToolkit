// GeoscientistToolkit/UI/ImGuiCurveEditor.cs

using System.Globalization;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.UI.Utils;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

/// <summary>
///     Represents a single point on the curve.
/// </summary>
public struct CurvePoint : IComparable<CurvePoint>
{
    public Vector2 Point;

    public CurvePoint(float x, float y)
    {
        Point = new Vector2(x, y);
    }

    public int CompareTo(CurvePoint other)
    {
        return Point.X.CompareTo(other.Point.X);
    }
}

/// <summary>
///     A reusable ImGui widget for editing a 1D curve.
/// </summary>
public class ImGuiCurveEditor
{
    private readonly ImGuiExportFileDialog _fileDialog;

    // Configuration
    private readonly string _id;
    private readonly Vector2 _rangeMax;
    private readonly Vector2 _rangeMin;
    private readonly string _xLabel;
    private readonly string _yLabel;

    // Interaction
    private int _draggingPointIndex = -1;
    private int _hoveredPointIndex = -1;
    private bool _isCurveValid = true;
    private bool _isOpen;
    private bool _isPanning;
    private string _loadErrorMessage = "";

    // State
    private List<CurvePoint> _points = new();

    // Error Handling
    private bool _showLoadErrorPopup;
    private readonly string _title;
    private string _validationError = "";

    // View
    private Vector2 _viewOffset = Vector2.Zero; // Pan
    private float _viewZoom = 1.0f; // Zoom

    /// <summary>
    ///     Initializes a new instance of the ImGuiCurveEditor.
    /// </summary>
    /// <param name="id">A unique identifier for the editor window.</param>
    /// <param name="title">The title of the editor window.</param>
    /// <param name="xLabel">Label for the X-axis.</param>
    /// <param name="yLabel">Label for the Y-axis.</param>
    /// <param name="initialPoints">An optional list of starting points.</param>
    /// <param name="rangeMin">The minimum bounds of the editable area (e.g., (0,0)).</param>
    /// <param name="rangeMax">The maximum bounds of the editable area (e.g., (1,1)).</param>
    public ImGuiCurveEditor(string id, string title, string xLabel, string yLabel,
        List<CurvePoint> initialPoints = null, Vector2? rangeMin = null, Vector2? rangeMax = null)
    {
        _id = id;
        _title = title;
        _xLabel = xLabel;
        _yLabel = yLabel;

        _rangeMin = rangeMin ?? Vector2.Zero;
        _rangeMax = rangeMax ?? Vector2.One;

        _fileDialog = new ImGuiExportFileDialog($"CurveFileDialog_{id}", "Curve File");
        _fileDialog.SetExtensions((".crv", "Curve File"));

        if (initialPoints != null)
        {
            _points.AddRange(initialPoints);
            _points.Sort();
        }
        else
        {
            // Add default points
            _points.Add(new CurvePoint(_rangeMin.X, _rangeMin.Y));
            _points.Add(new CurvePoint(_rangeMax.X, _rangeMax.Y));
        }

        ResetView();
    }

    /// <summary>
    ///     Custom validation logic for the curve.
    ///     The function should return a tuple: (bool IsValid, string ErrorMessage).
    /// </summary>
    public Func<List<CurvePoint>, (bool IsValid, string ErrorMessage)> Validator { get; set; }

    /// <summary>
    ///     Opens the curve editor dialog.
    /// </summary>
    public void Open()
    {
        _isOpen = true;
        ValidateCurve();
    }

    /// <summary>
    ///     Sets the curve points programmatically.
    /// </summary>
    public void SetCurve(List<CurvePoint> points)
    {
        _points = new List<CurvePoint>(points);
        _points.Sort();
        ValidateCurve();
    }


    /// <summary>
    ///     Submits the UI for the editor. This should be called every frame.
    /// </summary>
    /// <param name="resultCurve">The final interpolated curve data if OK was clicked.</param>
    /// <param name="resolution">The number of samples in the output array.</param>
    /// <returns>True if the OK button was clicked and the curve is valid, otherwise false.</returns>
    public bool Submit(out float[] resultCurve, int resolution = 256)
    {
        resultCurve = null;
        if (!_isOpen) return false;

        var okClicked = false;

        ImGui.SetNextWindowSize(new Vector2(600, 450), ImGuiCond.FirstUseEver);

        // Change title bar color if the curve is invalid
        if (!_isCurveValid)
        {
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.8f, 0.1f, 0.1f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.6f, 0.1f, 0.1f, 1.0f));
        }

        if (ImGui.Begin(_title + "###" + _id, ref _isOpen))
        {
            if (!_isCurveValid) ImGui.PopStyleColor(2);

            DrawToolbar();
            ImGui.Separator();

            if (!_isCurveValid) ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error: {_validationError}");

            // Main canvas for the curve editor
            ImGui.Text(_yLabel);
            var canvasMin = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail() - new Vector2(0, ImGui.GetFrameHeightWithSpacing() * 2 + 20);
            if (canvasSize.X < 50 || canvasSize.Y < 50)
                canvasSize = new Vector2(Math.Max(50, canvasSize.X), Math.Max(50, canvasSize.Y));

            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(canvasMin, canvasMin + canvasSize, ImGui.GetColorU32(ImGuiCol.FrameBg));
            drawList.AddRect(canvasMin, canvasMin + canvasSize, ImGui.GetColorU32(ImGuiCol.Border));

            ImGui.Dummy(canvasSize); // Reserve space
            ImGui.Text(_xLabel);

            // FIX: Pass Vector2 for min and size instead of a non-existent ImRect
            HandleInteractions(canvasMin, canvasSize);

            // Clip rendering to the canvas
            drawList.PushClipRect(canvasMin, canvasMin + canvasSize, true);
            DrawGrid(canvasMin, canvasSize);
            DrawCurve(canvasMin, canvasSize);
            drawList.PopClipRect();

            // Bottom controls
            ImGui.Separator();

            if (ImGui.Button("OK", new Vector2(80, 0)))
            {
                ValidateCurve();
                if (_isCurveValid)
                {
                    resultCurve = GetCurveData(resolution);
                    okClicked = true;
                    _isOpen = false;
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(80, 0))) _isOpen = false;
        }
        else
        {
            if (!_isCurveValid) ImGui.PopStyleColor(2);
        }

        ImGui.End();

        // Handle file dialog submissions
        if (_fileDialog.Submit())
        {
            if (_fileDialog.SelectedPath.EndsWith(".crv"))
                LoadFromFile(_fileDialog.SelectedPath);
            else
                SaveToFile(_fileDialog.SelectedPath);
        }

        // Draw any popups that need to be shown
        DrawLoadErrorPopup();

        return okClicked;
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Reset View")) ResetView();
        ImGui.SameLine();
        if (ImGui.Button("Fit View")) FitView();
        ImGui.SameLine();
        if (ImGui.Button("Load...")) _fileDialog.Open(startingPath: Directory.GetCurrentDirectory());
        ImGui.SameLine();
        if (ImGui.Button("Save As...")) _fileDialog.Open("my_curve.crv", Directory.GetCurrentDirectory());
    }

    // FIX: Method signature changed to use Vector2
    private void HandleInteractions(Vector2 viewMin, Vector2 viewSize)
    {
        var io = ImGui.GetIO();

        // Use ImGui.IsMouseHoveringRect to check if the mouse is inside our canvas
        var isHoveringCanvas = ImGui.IsMouseHoveringRect(viewMin, viewMin + viewSize);

        var mousePosInView = io.MousePos - viewMin;
        var mousePosInCurve = ScreenToCurve(mousePosInView, viewMin, viewSize);

        _hoveredPointIndex = -1;
        if (isHoveringCanvas)
            for (var i = 0; i < _points.Count; i++)
            {
                var pointScreenPos = CurveToScreen(_points[i].Point, viewMin, viewSize);
                if (Vector2.Distance(io.MousePos, pointScreenPos) < 6.0f)
                {
                    _hoveredPointIndex = i;
                    break;
                }
            }

        // Dragging points
        if (_draggingPointIndex != -1)
        {
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _draggingPointIndex = -1;
                _points.Sort();
                ValidateCurve();
            }
            else
            {
                var newPos = _points[_draggingPointIndex].Point;
                newPos.X = Math.Clamp(mousePosInCurve.X, _rangeMin.X, _rangeMax.X);
                newPos.Y = Math.Clamp(mousePosInCurve.Y, _rangeMin.Y, _rangeMax.Y);
                _points[_draggingPointIndex] = new CurvePoint(newPos.X, newPos.Y);
            }
        }

        if (isHoveringCanvas)
        {
            // Start dragging
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _hoveredPointIndex != -1)
            {
                _draggingPointIndex = _hoveredPointIndex;
            }
            // Add a point on the curve
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && _hoveredPointIndex == -1)
            {
                AddPoint(mousePosInCurve);
                ValidateCurve();
            }
            // Remove a point
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && _hoveredPointIndex != -1)
            {
                if (_points.Count > 2)
                {
                    _points.RemoveAt(_hoveredPointIndex);
                    ValidateCurve();
                }
            }

            // Zooming
            if (io.MouseWheel != 0)
            {
                var zoomFactor = MathF.Pow(1.1f, io.MouseWheel);
                _viewZoom *= zoomFactor;
                _viewOffset = mousePosInCurve - (mousePosInCurve - _viewOffset) / zoomFactor;
            }

            // Panning
            if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            {
                _isPanning = true;
                var delta = io.MouseDelta / viewSize * (_rangeMax - _rangeMin) / _viewZoom;
                _viewOffset -= new Vector2(delta.X, -delta.Y); // Y is inverted in screen space
            }
            else
            {
                _isPanning = false;
            }
        }
    }

    // FIX: Method signature changed to use Vector2
    private void DrawGrid(Vector2 viewMin, Vector2 viewSize)
    {
        var drawList = ImGui.GetWindowDrawList();
        var step = 0.1f * (_rangeMax - _rangeMin); // Example step

        for (var x = _rangeMin.X; x <= _rangeMax.X; x += step.X)
        {
            var p1 = CurveToScreen(new Vector2(x, _rangeMin.Y), viewMin, viewSize);
            var p2 = CurveToScreen(new Vector2(x, _rangeMax.Y), viewMin, viewSize);
            drawList.AddLine(p1, p2, ImGui.GetColorU32(ImGuiCol.TextDisabled, 0.5f));
        }

        for (var y = _rangeMin.Y; y <= _rangeMax.Y; y += step.Y)
        {
            var p1 = CurveToScreen(new Vector2(_rangeMin.X, y), viewMin, viewSize);
            var p2 = CurveToScreen(new Vector2(_rangeMax.X, y), viewMin, viewSize);
            drawList.AddLine(p1, p2, ImGui.GetColorU32(ImGuiCol.TextDisabled, 0.5f));
        }
    }

    // FIX: Method signature changed to use Vector2
    private void DrawCurve(Vector2 viewMin, Vector2 viewSize)
    {
        var drawList = ImGui.GetWindowDrawList();

        // Draw curve lines
        for (var i = 0; i < _points.Count - 1; i++)
        {
            var p1 = CurveToScreen(_points[i].Point, viewMin, viewSize);
            var p2 = CurveToScreen(_points[i + 1].Point, viewMin, viewSize);
            drawList.AddLine(p1, p2, ImGui.GetColorU32(ImGuiCol.PlotLines), 2.0f);
        }

        // Draw points
        for (var i = 0; i < _points.Count; i++)
        {
            var pos = CurveToScreen(_points[i].Point, viewMin, viewSize);
            var color = ImGui.GetColorU32(i == _hoveredPointIndex ? ImGuiCol.PlotLinesHovered : ImGuiCol.PlotLines);
            drawList.AddCircleFilled(pos, 6.0f, color);
            drawList.AddCircle(pos, 6.0f, ImGui.GetColorU32(ImGuiCol.Text));

            if (i == _hoveredPointIndex) ImGui.SetTooltip($"({_points[i].Point.X:F2}, {_points[i].Point.Y:F2})");
        }
    }

    /// <summary>
    ///     Draws the modal popup for file load errors.
    /// </summary>
    private void DrawLoadErrorPopup()
    {
        if (!_showLoadErrorPopup) return;

        var popupId = $"Load Error##{_id}";
        ImGui.OpenPopup(popupId);

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal(popupId, ref _showLoadErrorPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Error Loading File");
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextWrapped(_loadErrorMessage);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Center the button
            float buttonWidth = 120;
            ImGui.SetCursorPosX((ImGui.GetWindowWidth() - buttonWidth) * 0.5f);
            if (ImGui.Button("OK", new Vector2(buttonWidth, 0)))
            {
                _showLoadErrorPopup = false;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    /// <summary>
    ///     Converts a point from curve coordinates to screen coordinates.
    /// </summary>
    // FIX: Method signature changed to use Vector2
    private Vector2 CurveToScreen(Vector2 p, Vector2 viewMin, Vector2 viewSize)
    {
        var normalized = (p - _viewOffset) * _viewZoom / (_rangeMax - _rangeMin);
        var screenPos = new Vector2(normalized.X, 1.0f - normalized.Y) * viewSize + viewMin;
        return screenPos;
    }

    /// <summary>
    ///     Converts a point from screen coordinates to curve coordinates.
    /// </summary>
    // FIX: Method signature changed to use Vector2
    private Vector2 ScreenToCurve(Vector2 p, Vector2 viewMin, Vector2 viewSize)
    {
        var normalized = p / viewSize;
        var curvePos = new Vector2(normalized.X, 1.0f - normalized.Y) * (_rangeMax - _rangeMin) / _viewZoom +
                       _viewOffset;
        return curvePos;
    }

    private void AddPoint(Vector2 curvePos)
    {
        curvePos.X = Math.Clamp(curvePos.X, _rangeMin.X, _rangeMax.X);
        curvePos.Y = Math.Clamp(curvePos.Y, _rangeMin.Y, _rangeMax.Y);

        _points.Add(new CurvePoint(curvePos.X, curvePos.Y));
        _points.Sort();
    }

    private void ValidateCurve()
    {
        if (Validator != null)
        {
            var (isValid, errorMessage) = Validator(_points);
            _isCurveValid = isValid;
            _validationError = errorMessage;
        }
        else
        {
            _isCurveValid = true;
            _validationError = "";
        }
    }

    /// <summary>
    ///     Gets the interpolated curve data as an array.
    /// </summary>
    public float[] GetCurveData(int resolution)
    {
        if (_points.Count < 2) return null;

        var data = new float[resolution];
        var xRange = _rangeMax.X - _rangeMin.X;

        for (var i = 0; i < resolution; i++)
        {
            var x = _rangeMin.X + i / (float)(resolution - 1) * xRange;
            data[i] = Evaluate(x);
        }

        return data;
    }

    /// <summary>
    ///     Evaluates the curve at a specific X value using linear interpolation.
    /// </summary>
    public float Evaluate(float x)
    {
        if (_points.Count == 0) return _rangeMin.Y;
        if (_points.Count == 1 || x <= _points[0].Point.X) return _points[0].Point.Y;
        if (x >= _points[^1].Point.X) return _points[^1].Point.Y;

        var i = 0;
        while (i < _points.Count - 1 && x > _points[i + 1].Point.X) i++;

        var p1 = _points[i];
        var p2 = _points[i + 1];

        if (Math.Abs(p1.Point.X - p2.Point.X) < 1e-6) return p1.Point.Y;

        var t = (x - p1.Point.X) / (p2.Point.X - p1.Point.X);
        return p1.Point.Y + t * (p2.Point.Y - p1.Point.Y);
    }

    /// <summary>
    ///     Gets the raw control points of the curve.
    /// </summary>
    public List<CurvePoint> GetPoints()
    {
        return new List<CurvePoint>(_points);
    }

    /// <summary>
    ///     Draws the curve editor window (convenience method that calls Submit).
    /// </summary>
    public void Draw()
    {
        Submit(out _, resolution: 256);
    }

    public void ResetView()
    {
        _viewOffset = _rangeMin;
        _viewZoom = 1.0f;
    }

    public void FitView()
    {
        if (_points.Count < 2)
        {
            ResetView();
            return;
        }

        var min = _points[0].Point;
        var max = _points[0].Point;
        foreach (var p in _points)
        {
            min = Vector2.Min(min, p.Point);
            max = Vector2.Max(max, p.Point);
        }

        var size = max - min;
        if (size.X < 1e-6f || size.Y < 1e-6f)
        {
            ResetView();
            return;
        }

        var zoomX = (_rangeMax.X - _rangeMin.X) / size.X;
        var zoomY = (_rangeMax.Y - _rangeMin.Y) / size.Y;

        _viewZoom = Math.Min(zoomX, zoomY) * 0.9f;
        _viewOffset = min - (max - min) * 0.05f;
    }

    private void SaveToFile(string path)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ImGuiCurveEditor Data");
            foreach (var p in _points)
                sb.AppendLine(
                    $"{p.Point.X.ToString(CultureInfo.InvariantCulture)},{p.Point.Y.ToString(CultureInfo.InvariantCulture)}");
            File.WriteAllText(path, sb.ToString());
        }
        catch (Exception ex)
        {
            _loadErrorMessage = $"Failed to save curve to '{Path.GetFileName(path)}'.\n\nReason: {ex.Message}";
            _showLoadErrorPopup = true;
        }
    }

    private void LoadFromFile(string path)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            var newPoints = new List<CurvePoint>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

                var parts = line.Split(',');
                if (parts.Length == 2 &&
                    float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var y))
                    newPoints.Add(new CurvePoint(x, y));
                else
                    // Throw a more specific error if a line is malformed
                    throw new FormatException($"Invalid data format on line: \"{line}\"");
            }

            if (newPoints.Count > 0)
            {
                _points = newPoints;
                _points.Sort();
                ValidateCurve();
                FitView();
            }
            else
            {
                throw new InvalidDataException("The file contains no valid curve points.");
            }
        }
        catch (Exception ex)
        {
            _loadErrorMessage = $"Failed to load curve from '{Path.GetFileName(path)}'.\n\nReason: {ex.Message}";
            _showLoadErrorPopup = true;
        }
    }
}