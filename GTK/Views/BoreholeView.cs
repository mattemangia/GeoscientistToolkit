// GeoscientistToolkit/GTK/Views/BoreholeView.cs

using Cairo;
using GeoscientistToolkit.Data.Borehole;
using Gtk;
using System;
using System.Linq;
using Gdk;
using GeoscientistToolkit.GtkUI.Dialogs;
using GeoscientistToolkit.GtkUI;
using System.Collections.Generic;
using Newtonsoft.Json;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.GtkUI.Views
{
    public class BoreholeView : Box
    {
        private readonly DrawingArea _drawingArea;
        private readonly ScrolledWindow _scrolledWindow;
        private BoreholeEditorToolbar _toolbar;
        private BoreholeLegend _legend;
        private BoreholeDataset _dataset;
        private double _zoom = 1.0;

        private const int DepthScaleWidth = 60;
        private const int LithologyColumnWidth = 150;
        private const int TrackWidth = 150;
        private const int TrackSpacing = 10;

        private bool _autoScaleDepth = true;
        private double _depthStart;
        private double _depthEnd;
        private bool _showGrid = true;

        private List<LithologyUnit> _selectedLithologyUnits = new();
        private Clipboard _clipboard;

        public BoreholeView(BoreholeDataset dataset) : base(Orientation.Vertical, 0)
        {
            _clipboard = Clipboard.Get(Gdk.Atom.Intern("CLIPBOARD", false));

            _toolbar = new BoreholeEditorToolbar(dataset.TotalDepth);
            PackStart(_toolbar, false, false, 0);

            _scrolledWindow = new ScrolledWindow();
            _drawingArea = new DrawingArea();
            _scrolledWindow.Add(_drawingArea);
            PackStart(_scrolledWindow, true, true, 0);

            _legend = new BoreholeLegend(dataset);

            _drawingArea.Drawn += OnDrawn;
            _drawingArea.AddEvents((int)EventMask.ScrollMask | (int)EventMask.ButtonPressMask);
            _drawingArea.ScrollEvent += OnScroll;
            _drawingArea.ButtonPressEvent += OnButtonPress;

            _scrolledWindow.Vadjustment.ValueChanged += (s, e) => _drawingArea.QueueDraw();
            _scrolledWindow.SizeAllocated += OnSizeAllocated;

            _toolbar.AutoScaleDepthChanged += OnAutoScaleDepthChanged;
            _toolbar.DepthRangeChanged += OnDepthRangeChanged;
            _toolbar.ShowGridChanged += (show) => { _showGrid = show; _drawingArea.QueueDraw(); };
            _toolbar.ShowLegendChanged += (show) => { if (show) _legend.Show(); else _legend.Hide(); };
            _toolbar.ImportLasClicked += () =>
            {
                global::GeoscientistToolkit.GtkUI.BoreholeLasTools.ImportFromLas(this.Toplevel as Gtk.Window, _dataset);
                SetDataset(_dataset);
            };
            _toolbar.ExportLasClicked += step =>
                global::GeoscientistToolkit.GtkUI.BoreholeLasTools.ExportToLas(this.Toplevel as Gtk.Window, _dataset, step);

            SetDataset(dataset);
        }

        public void SetDataset(BoreholeDataset dataset)
        {
            _dataset = dataset;
            _depthEnd = _dataset.TotalDepth;
            _toolbar.UpdateMaxDepth(_dataset.TotalDepth);
            _legend.UpdateDataset(_dataset);
            _drawingArea.QueueDraw();
        }

        private void OnButtonPress(object o, ButtonPressEventArgs args)
        {
            if (args.Event.Button == 3) // Right-click
            {
                var menu = new Menu();
                var copyItem = new MenuItem("Copy");
                copyItem.Activated += (s, e) => HandleCopy();
                menu.Append(copyItem);

                var cutItem = new MenuItem("Cut");
                cutItem.Activated += (s, e) => HandleCut();
                menu.Append(cutItem);

                var pasteItem = new MenuItem("Paste");
                pasteItem.Activated += (s, e) => HandlePaste();
                menu.Append(pasteItem);

                menu.ShowAll();
                menu.Popup();
            }
        }

        private void HandleCopy()
        {
            if (_selectedLithologyUnits.Any())
            {
                var json = JsonConvert.SerializeObject(_selectedLithologyUnits);
                _clipboard.Text = json;
            }
        }

        private void HandleCut()
        {
            HandleCopy();
            _dataset.RemoveLithologyUnits(_selectedLithologyUnits);
            _selectedLithologyUnits.Clear();
            _drawingArea.QueueDraw();
        }

        private void HandlePaste()
        {
            var json = _clipboard.WaitForText();
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var units = JsonConvert.DeserializeObject<List<LithologyUnit>>(json);
                    if (units != null)
                    {
                        _dataset.AddLithologyUnits(units);
                        SetDataset(_dataset);
                    }
                }
                catch (JsonException ex)
                {
                    Logger.LogError($"Failed to deserialize clipboard content: {ex.Message}");
                }
            }
        }


        private void OnAutoScaleDepthChanged(bool autoScale)
        {
            _autoScaleDepth = autoScale;
            if (_autoScaleDepth)
            {
                _depthStart = 0;
                _depthEnd = _dataset.TotalDepth;
                _scrolledWindow.Vadjustment.Value = 0;
            }
            _drawingArea.QueueDraw();
        }

        private void OnDepthRangeChanged(double start, double end)
        {
            _depthStart = start;
            _depthEnd = end;
            _drawingArea.QueueDraw();
        }

        private void OnSizeAllocated(object o, SizeAllocatedArgs args)
        {
            var totalContentHeight = (_depthEnd - _depthStart) * _zoom;
            _scrolledWindow.Vadjustment.Upper = Math.Max(args.Allocation.Height, totalContentHeight);
            _scrolledWindow.Vadjustment.PageSize = args.Allocation.Height;
        }

        private void OnScroll(object o, ScrollEventArgs args)
        {
            var direction = args.Event.Direction;
            var mouseX = args.Event.X;
            var mouseY = args.Event.Y;

            var pixelsPerMeterBefore = _scrolledWindow.AllocatedHeight / (_depthEnd - _depthStart) * _zoom;
            var depthAtMouse = _scrolledWindow.Vadjustment.Value / pixelsPerMeterBefore + mouseY / pixelsPerMeterBefore;

            var zf = 1.2;
            if (direction == ScrollDirection.Up)
                _zoom *= zf;
            else if (direction == ScrollDirection.Down)
                _zoom /= zf;

            _zoom = Math.Clamp(_zoom, 0.1, 20.0);

            var pixelsPerMeterAfter = _scrolledWindow.AllocatedHeight / (_depthEnd - _depthStart) * _zoom;
            var newTotalHeight = (_depthEnd - _depthStart) * _zoom;

            var newVadjustmentValue = depthAtMouse * pixelsPerMeterAfter - mouseY;

            _scrolledWindow.Vadjustment.Upper = Math.Max(_scrolledWindow.AllocatedHeight, newTotalHeight);
            _scrolledWindow.Vadjustment.Value = Math.Clamp(newVadjustmentValue, 0, _scrolledWindow.Vadjustment.Upper - _scrolledWindow.Vadjustment.PageSize);

            _drawingArea.QueueDraw();
        }

        private void OnDrawn(object o, DrawnArgs args)
        {
            var cr = args.Cr;
            var width = _drawingArea.AllocatedWidth;
            var height = _drawingArea.AllocatedHeight;

            var visibleDepthRange = _depthEnd - _depthStart;
            var pixelsPerMeter = height / visibleDepthRange * _zoom;

            var voffsetInMeters = _depthStart;
             if (!_autoScaleDepth)
                voffsetInMeters += _scrolledWindow.Vadjustment.Value / pixelsPerMeter;


            // Background
            cr.SetSourceRGB(0.1, 0.1, 0.1);
            cr.Paint();

            // Draw depth scale
            DrawDepthScale(cr, height, pixelsPerMeter, voffsetInMeters);

            // Draw Lithology
            DrawLithologyColumn(cr, height, pixelsPerMeter, voffsetInMeters);

            // Draw Parameter Tracks
            DrawParameterTracks(cr, height, pixelsPerMeter, voffsetInMeters);
        }

        private void DrawDepthScale(Context cr, int height, double pixelsPerMeter, double voffsetMeters)
        {
            cr.SetSourceRGB(0.85, 0.85, 0.85);
            cr.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
            cr.SetFontSize(12);

            var step = GetAdaptiveGridInterval(pixelsPerMeter);

            for (double depth = 0; depth <= _dataset.TotalDepth; depth += step)
            {
                var y = (depth - voffsetMeters) * pixelsPerMeter;
                if (y < 0 || y > height) continue;

                cr.MoveTo(DepthScaleWidth - 10, y);
                cr.LineTo(DepthScaleWidth, y);
                cr.Stroke();

                var label = $"{depth} m";
                var extents = cr.TextExtents(label);
                cr.MoveTo(DepthScaleWidth - 15 - extents.Width, y + extents.Height / 2);
                cr.ShowText(label);
            }
        }

        private void DrawLithologyColumn(Context cr, int height, double pixelsPerMeter, double voffsetMeters)
        {
            foreach (var unit in _dataset.LithologyUnits)
            {
                var y1 = (unit.DepthFrom - voffsetMeters) * pixelsPerMeter;
                var y2 = (unit.DepthTo - voffsetMeters) * pixelsPerMeter;

                if (y2 < 0 || y1 > height) continue;

                y1 = Math.Max(0, y1);
                y2 = Math.Min(height, y2);

                if (y2 <= y1) continue;

                cr.Rectangle(DepthScaleWidth, y1, LithologyColumnWidth, y2 - y1);
                cr.SetSourceRGBA(unit.Color.X, unit.Color.Y, unit.Color.Z, unit.Color.W);
                cr.Fill();

                DrawLithologyPattern(cr, DepthScaleWidth, y1, LithologyColumnWidth, y2 - y1, unit);

                cr.Rectangle(DepthScaleWidth, y1, LithologyColumnWidth, y2 - y1);
                cr.SetSourceRGB(0.3, 0.3, 0.3);
                cr.Stroke();
            }
        }

        private void DrawParameterTracks(Context cr, int height, double pixelsPerMeter, double voffsetMeters)
        {
            var visibleTracks = _dataset.ParameterTracks.Values.Where(t => t.IsVisible).ToList();
            var x = DepthScaleWidth + LithologyColumnWidth + TrackSpacing;

            foreach (var track in visibleTracks)
            {
                // Background and border
                cr.Rectangle(x, 0, TrackWidth, height);
                cr.SetSourceRGB(0.1, 0.1, 0.1);
                cr.Fill();
                cr.Rectangle(x, 0, TrackWidth, height);
                cr.SetSourceRGB(0.5, 0.5, 0.5);
                cr.Stroke();

                if (track.Points.Count >= 2)
                {
                    cr.SetSourceRGBA(track.Color.X, track.Color.Y, track.Color.Z, track.Color.W);
                    cr.LineWidth = 2;

                    for (var i = 0; i < track.Points.Count - 1; i++)
                    {
                        var p1 = track.Points[i];
                        var p2 = track.Points[i + 1];

                        var y1 = (p1.Depth - voffsetMeters) * pixelsPerMeter;
                        var y2 = (p2.Depth - voffsetMeters) * pixelsPerMeter;

                        if (Math.Max(y1, y2) < 0 || Math.Min(y1, y2) > height) continue;

                        double x1, x2;
                        if (track.IsLogarithmic)
                        {
                            var logMin = Math.Log10(Math.Max(track.MinValue, 0.001f));
                            var logMax = Math.Log10(Math.Max(track.MaxValue, 0.001f));
                            var v1 = Math.Log10(Math.Max(p1.Value, 0.001f));
                            var v2 = Math.Log10(Math.Max(p2.Value, 0.001f));
                            x1 = x + (v1 - logMin) / Math.Max(1e-6f, logMax - logMin) * TrackWidth;
                            x2 = x + (v2 - logMin) / Math.Max(1e-6f, logMax - logMin) * TrackWidth;
                        }
                        else
                        {
                            x1 = x + (p1.Value - track.MinValue) / Math.Max(1e-6f, track.MaxValue - track.MinValue) * TrackWidth;
                            x2 = x + (p2.Value - track.MinValue) / Math.Max(1e-6f, track.MaxValue - track.MinValue) * TrackWidth;
                        }

                        cr.MoveTo(x1, y1);
                        cr.LineTo(x2, y2);
                        cr.Stroke();
                    }
                }
                x += TrackWidth + TrackSpacing;
            }
        }

        private void DrawLithologyPattern(Context cr, double x, double y, double width, double height, LithologyUnit unit)
        {
            var pattern = _dataset.LithologyPatterns.TryGetValue(unit.LithologyType, out var p) ? p : LithologyPattern.Solid;

            cr.Save();
            cr.Rectangle(x, y, width, height);
            cr.Clip();

            cr.SetSourceRGBA(unit.Color.X * 0.7, unit.Color.Y * 0.7, unit.Color.Z * 0.7, unit.Color.W);

            switch (pattern)
            {
                case LithologyPattern.Dots:
                    for (double yy = 0; yy < height; yy += 8)
                    for (double xx = 0; xx < width; xx += 8)
                    {
                        cr.NewSubPath();
                        cr.Arc(x + xx + 4, y + yy + 4, 1.5, 0, 2 * Math.PI);
                        cr.Fill();
                    }
                    break;
                case LithologyPattern.HorizontalLines:
                    for (double yy = 0; yy < height; yy += 6)
                    {
                        cr.MoveTo(x, y + yy);
                        cr.LineTo(x + width, y + yy);
                        cr.Stroke();
                    }
                    break;
                // Add other patterns here...
            }

            cr.Restore();
        }

        private float GetAdaptiveGridInterval(double ppm)
        {
            if (ppm <= 0) return 1000f;
            const float targetPx = 80f;
            var d = targetPx / ppm;
            var p10 = Math.Pow(10, Math.Floor(Math.Log10(d)));
            var n = d / p10;
            if (n < 1.5) return (float)(1 * p10);
            if (n < 3.5) return (float)(2 * p10);
            if (n < 7.5) return (float)(5 * p10);
            return (float)(10 * p10);
        }
    }
}
