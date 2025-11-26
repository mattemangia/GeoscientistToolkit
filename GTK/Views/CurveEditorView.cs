// GeoscientistToolkit/GTK/Views/CurveEditorView.cs

using Cairo;
using Gtk;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace GeoscientistToolkit.GtkUI.Views
{
    /// <summary>
    /// Represents a single point on the curve.
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


    public class CurveEditorView : Dialog
    {
        private readonly DrawingArea _drawingArea;
        private List<CurvePoint> _points = new();

        private readonly Vector2 _rangeMin;
        private readonly Vector2 _rangeMax;

        private int _draggingPointIndex = -1;
        private int _hoveredPointIndex = -1;

        // View
        private Vector2 _viewOffset = Vector2.Zero; // Pan
        private float _viewZoom = 1.0f; // Zoom

        public Func<List<CurvePoint>, (bool IsValid, string ErrorMessage)> Validator { get; set; }


        public CurveEditorView(Window parent, string title, string xLabel, string yLabel, List<CurvePoint> initialPoints, Vector2? rangeMin = null, Vector2? rangeMax = null) : base(title, parent, DialogFlags.Modal | DialogFlags.DestroyWithParent)
        {
            _rangeMin = rangeMin ?? Vector2.Zero;
            _rangeMax = rangeMax ?? Vector2.One;

            if (initialPoints != null && initialPoints.Any())
            {
                _points.AddRange(initialPoints);
                _points.Sort();
            }
            else
            {
                _points.Add(new CurvePoint(_rangeMin.X, (_rangeMin.Y + _rangeMax.Y) / 2));
                _points.Add(new CurvePoint(_rangeMax.X, (_rangeMin.Y + _rangeMax.Y) / 2));
            }

            _drawingArea = new DrawingArea();
            _drawingArea.Drawn += OnDrawn;
            _drawingArea.AddEvents((int)(Gdk.EventMask.ButtonPressMask | Gdk.EventMask.ButtonReleaseMask | Gdk.EventMask.PointerMotionMask | Gdk.EventMask.ScrollMask));
            _drawingArea.ButtonPressEvent += OnButtonPress;
            _drawingArea.ButtonReleaseEvent += OnButtonRelease;
            _drawingArea.MotionNotifyEvent += OnMotionNotify;
            _drawingArea.ScrollEvent += OnScroll;

            var mainGrid = new Grid { RowSpacing = 6 };
            mainGrid.Attach(BuildToolbar(), 0, 0, 1, 1);
            mainGrid.Attach(new Separator(Orientation.Horizontal), 0, 1, 1, 1);

            var canvasGrid = new Grid { BorderWidth = 6, RowSpacing = 3 };
            canvasGrid.Attach(new Label(yLabel) { Xalign = 0.5f }, 0, 0, 1, 1);
            canvasGrid.Attach(_drawingArea, 0, 1, 1, 1);
            canvasGrid.Attach(new Label(xLabel) { Xalign = 0.5f }, 0, 2, 1, 1);

            mainGrid.Attach(canvasGrid, 0, 2, 1, 1);

            ContentArea.Add(mainGrid);

            AddButton("OK", ResponseType.Ok);
            AddButton("Cancel", ResponseType.Cancel);

            SetDefaultSize(700, 550);
            ResetView();
            ShowAll();
        }

        private Toolbar BuildToolbar()
        {
            var toolbar = new Toolbar { IconSize = IconSize.SmallToolbar };

            var resetButton = new ToolButton(Stock.Refresh) { TooltipText = "Reset View" };
            resetButton.Clicked += (_, _) => { ResetView(); _drawingArea.QueueDraw(); };
            toolbar.Insert(resetButton, -1);

            var fitButton = new ToolButton(Stock.ZoomFit) { TooltipText = "Fit View" };
            fitButton.Clicked += (_, _) => { FitView(); _drawingArea.QueueDraw(); };
            toolbar.Insert(fitButton, -1);

            toolbar.Insert(new SeparatorToolItem(), -1);

            var loadButton = new ToolButton(Stock.Open) { TooltipText = "Load..." };
            loadButton.Clicked += OnLoadClicked;
            toolbar.Insert(loadButton, -1);

            var saveButton = new ToolButton(Stock.SaveAs) { TooltipText = "Save As..." };
            saveButton.Clicked += OnSaveClicked;
            toolbar.Insert(saveButton, -1);

            return toolbar;
        }

        private void OnDrawn(object o, DrawnArgs args)
        {
            var cr = args.Cr;
            int width = _drawingArea.AllocatedWidth;
            int height = _drawingArea.AllocatedHeight;

            cr.SetSourceRGB(0.12, 0.13, 0.15);
            cr.Rectangle(0, 0, width, height);
            cr.Fill();

            DrawGrid(cr, width, height);
            DrawCurve(cr, width, height);
        }

        private void DrawGrid(Context cr, int width, int height)
        {
            cr.SetSourceRGBA(0.3, 0.3, 0.3, 0.5);
            cr.LineWidth = 1;

            int gridLines = 10;
            for (int i = 1; i < gridLines; i++)
            {
                float x = _rangeMin.X + (_rangeMax.X - _rangeMin.X) * i / gridLines;
                var p1 = CurveToScreen(new Vector2(x, _rangeMin.Y), width, height);
                var p2 = CurveToScreen(new Vector2(x, _rangeMax.Y), width, height);
                cr.MoveTo(p1.X, 0);
                cr.LineTo(p2.X, height);
                cr.Stroke();
            }

            for (int i = 1; i < gridLines; i++)
            {
                float y = _rangeMin.Y + (_rangeMax.Y - _rangeMin.Y) * i / gridLines;
                var p1 = CurveToScreen(new Vector2(_rangeMin.X, y), width, height);
                var p2 = CurveToScreen(new Vector2(_rangeMax.X, y), width, height);
                cr.MoveTo(0, p1.Y);
                cr.LineTo(width, p2.Y);
                cr.Stroke();
            }
        }

        private void DrawCurve(Context cr, int width, int height)
        {
            if (_points.Count < 2) return;

            cr.SetSourceRGB(0.3, 0.7, 1.0);
            cr.LineWidth = 2.5;

            var p1 = CurveToScreen(_points[0].Point, width, height);
            cr.MoveTo(p1.X, p1.Y);

            for (int i = 1; i < _points.Count; i++)
            {
                var p2 = CurveToScreen(_points[i].Point, width, height);
                cr.LineTo(p2.X, p2.Y);
            }
            cr.Stroke();

            for (int i = 0; i < _points.Count; i++)
            {
                var p = CurveToScreen(_points[i].Point, width, height);
                if (i == _hoveredPointIndex || i == _draggingPointIndex)
                {
                    cr.SetSourceRGB(1.0, 1.0, 0.2);
                }
                else
                {
                    cr.SetSourceRGB(0.3, 0.7, 1.0);
                }
                cr.Arc(p.X, p.Y, 6, 0, 2 * Math.PI);
                cr.FillPreserve();
                cr.SetSourceRGB(0.9, 0.92, 0.95);
                cr.LineWidth = 1.5;
                cr.Stroke();
            }
        }

        private void OnButtonPress(object o, ButtonPressEventArgs args)
        {
            var mousePos = new Vector2((float)args.Event.X, (float)args.Event.Y);
            UpdateHover(mousePos);

            if (args.Event.Button == 1 && _hoveredPointIndex != -1) // Left click
            {
                _draggingPointIndex = _hoveredPointIndex;
            }
            else if (args.Event.Button == 1 && _hoveredPointIndex == -1)
            {
                var curvePos = ScreenToCurve(mousePos, _drawingArea.AllocatedWidth, _drawingArea.AllocatedHeight);
                AddPoint(curvePos);
                _drawingArea.QueueDraw();
            }
            else if (args.Event.Button == 3 && _hoveredPointIndex != -1) // Right click
            {
                if (_points.Count > 2)
                {
                    _points.RemoveAt(_hoveredPointIndex);
                    _hoveredPointIndex = -1;
                    _drawingArea.QueueDraw();
                }
            }
        }

        private void OnButtonRelease(object o, ButtonReleaseEventArgs args)
        {
            if (args.Event.Button == 1)
            {
                _draggingPointIndex = -1;
                _drawingArea.QueueDraw();
            }
        }

        private void OnMotionNotify(object o, MotionNotifyEventArgs args)
        {
            var mousePos = new Vector2((float)args.Event.X, (float)args.Event.Y);

            if (_draggingPointIndex != -1)
            {
                var newPos = ScreenToCurve(mousePos, _drawingArea.AllocatedWidth, _drawingArea.AllocatedHeight);
                newPos.X = Math.Clamp(newPos.X, _rangeMin.X, _rangeMax.X);
                newPos.Y = Math.Clamp(newPos.Y, _rangeMin.Y, _rangeMax.Y);
                _points[_draggingPointIndex] = new CurvePoint(newPos.X, newPos.Y);
                _points.Sort();
                _drawingArea.QueueDraw();
            }
            else
            {
                UpdateHover(mousePos);
            }
        }

        private void OnScroll(object o, ScrollEventArgs args)
        {
            float zoomFactor = (args.Event.Direction == Gdk.ScrollDirection.Up) ? 1.1f : 1 / 1.1f;
            var mousePosInCurve = ScreenToCurve(new Vector2((float)args.Event.X, (float)args.Event.Y), _drawingArea.AllocatedWidth, _drawingArea.AllocatedHeight);

            _viewZoom *= zoomFactor;
            _viewOffset = mousePosInCurve - (mousePosInCurve - _viewOffset) / zoomFactor;
            _drawingArea.QueueDraw();
        }

        private void UpdateHover(Vector2 mousePos)
        {
            int oldHovered = _hoveredPointIndex;
            _hoveredPointIndex = -1;
            for (var i = 0; i < _points.Count; i++)
            {
                var pointScreenPos = CurveToScreen(_points[i].Point, _drawingArea.AllocatedWidth, _drawingArea.AllocatedHeight);
                if (Vector2.Distance(mousePos, pointScreenPos) < 8.0f)
                {
                    _hoveredPointIndex = i;
                    break;
                }
            }
            if(oldHovered != _hoveredPointIndex)
                _drawingArea.QueueDraw();
        }

        private Vector2 CurveToScreen(Vector2 p, int width, int height)
        {
            var worldSize = (_rangeMax - _rangeMin) / _viewZoom;
            var normalized = (p - _viewOffset) / worldSize;
            return new Vector2(normalized.X * width, (1.0f - normalized.Y) * height);
        }

        private Vector2 ScreenToCurve(Vector2 p, int width, int height)
        {
            var worldSize = (_rangeMax - _rangeMin) / _viewZoom;
            var normalized = new Vector2(p.X / width, 1.0f - (p.Y / height));
            return _viewOffset + normalized * worldSize;
        }

        private void AddPoint(Vector2 curvePos)
        {
            _points.Add(new CurvePoint(curvePos.X, curvePos.Y));
            _points.Sort();
        }

        public float[] GetCurveData(int resolution)
        {
            if (_points.Count < 2) return null;
            var data = new float[resolution];
            var xRange = _points.Last().Point.X - _points.First().Point.X;
            var xStart = _points.First().Point.X;

            for (var i = 0; i < resolution; i++)
            {
                var x = xStart + i / (float)(resolution - 1) * xRange;
                data[i] = Evaluate(x);
            }
            return data;
        }

        public float Evaluate(float x)
        {
            if (_points.Count == 0) return _rangeMin.Y;
            if (_points.Count == 1 || x <= _points[0].Point.X) return _points[0].Point.Y;
            if (x >= _points[^1].Point.X) return _points[^1].Point.Y;

            var i = 0;
            while (i < _points.Count - 2 && x > _points[i + 1].Point.X) i++;

            var p1 = _points[i];
            var p2 = _points[i + 1];

            if (Math.Abs(p2.Point.X - p1.Point.X) < 1e-6) return p1.Point.Y;
            var t = (x - p1.Point.X) / (p2.Point.X - p1.Point.X);
            return p1.Point.Y + t * (p2.Point.Y - p1.Point.Y);
        }

        public List<CurvePoint> GetPoints() => new List<CurvePoint>(_points);

        private void OnLoadClicked(object sender, EventArgs e)
        {
            using var dialog = new FileChooserDialog("Load Curve", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "Open", ResponseType.Accept);
            var filter = new FileFilter { Name = "Curve Files (*.crv)" };
            filter.AddPattern("*.crv");
            dialog.AddFilter(filter);

            if (dialog.Run() == (int)ResponseType.Accept)
            {
                LoadFromFile(dialog.Filename);
            }
            dialog.Destroy();
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            using var dialog = new FileChooserDialog("Save Curve", this, FileChooserAction.Save, "Cancel", ResponseType.Cancel, "Save", ResponseType.Accept);
            dialog.CurrentName = "curve.crv";
            var filter = new FileFilter { Name = "Curve Files (*.crv)" };
            filter.AddPattern("*.crv");
            dialog.AddFilter(filter);

            if (dialog.Run() == (int)ResponseType.Accept)
            {
                SaveToFile(dialog.Filename);
            }
            dialog.Destroy();
        }

        private void SaveToFile(string path)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# GTK CurveEditor Data");
                foreach (var p in _points)
                    sb.AppendLine($"{p.Point.X.ToString(CultureInfo.InvariantCulture)},{p.Point.Y.ToString(CultureInfo.InvariantCulture)}");
                File.WriteAllText(path, sb.ToString());
            }
            catch (Exception ex)
            {
                ShowError($"Failed to save curve: {ex.Message}");
            }
        }

        private void LoadFromFile(string path)
        {
            try
            {
                var lines = File.ReadAllLines(path);
                var newPoints = new List<CurvePoint>();
                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#')))
                {
                    var parts = line.Split(',');
                    if (parts.Length == 2 &&
                        float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var x) &&
                        float.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var y))
                        newPoints.Add(new CurvePoint(x, y));
                    else
                        throw new FormatException($"Invalid data format on line: \"{line}\"");
                }

                if (newPoints.Count > 0)
                {
                    _points = newPoints;
                    _points.Sort();
                    FitView();
                    _drawingArea.QueueDraw();
                }
            }
            catch (Exception ex)
            {
                 ShowError($"Failed to load curve: {ex.Message}");
            }
        }

        private void ShowError(string message)
        {
            using var md = new MessageDialog(this, DialogFlags.Modal, MessageType.Error, ButtonsType.Ok, message);
            md.Run();
            md.Destroy();
        }

        public void ResetView()
        {
            _viewOffset = _rangeMin;
            var rangeSize = _rangeMax - _rangeMin;
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

            var viewSize = _rangeMax - _rangeMin;
            var zoomX = viewSize.X / size.X;
            var zoomY = viewSize.Y / size.Y;

            _viewZoom = Math.Min(zoomX, zoomY) * 0.9f;
            _viewOffset = min - size * 0.05f;
        }
    }
}
