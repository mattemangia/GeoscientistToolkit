using System;
using Gtk;
using Cairo;
using GeoscientistToolkit.Data.Borehole;

namespace GeoscientistToolkit.GTK.UI
{
    public class BoreholeWindow : Window
    {
        private BoreholeDataset _dataset;
        private DrawingArea _drawingArea;
        private float _depthScale = 10.0f; // pixels per meter

        public BoreholeWindow(BoreholeDataset dataset) : base($"Borehole Viewer: {dataset.Name}")
        {
            _dataset = dataset;
            SetDefaultSize(900, 700);

            var vbox = new Box(Orientation.Vertical, 0);
            Add(vbox);

            // Toolbar
            var toolbar = new Toolbar();
            vbox.PackStart(toolbar, false, false, 0);

            var addUnitBtn = new ToolButton(Stock.Add);
            addUnitBtn.Label = "Add Unit";
            addUnitBtn.IsImportant = true;
            addUnitBtn.Clicked += OnAddUnit;
            toolbar.Insert(addUnitBtn, -1);

            toolbar.Insert(new SeparatorToolItem(), -1);

            var zoomInBtn = new ToolButton(Stock.ZoomIn);
            zoomInBtn.Clicked += (s, e) => { _depthScale *= 1.2f; _drawingArea.QueueDraw(); UpdateDrawingAreaSize(); };
            toolbar.Insert(zoomInBtn, -1);

            var zoomOutBtn = new ToolButton(Stock.ZoomOut);
            zoomOutBtn.Clicked += (s, e) => { _depthScale /= 1.2f; _drawingArea.QueueDraw(); UpdateDrawingAreaSize(); };
            toolbar.Insert(zoomOutBtn, -1);

            // Scrolled Window for Drawing Area
            var scrolledWindow = new ScrolledWindow();
            vbox.PackStart(scrolledWindow, true, true, 0);

            _drawingArea = new DrawingArea();
            _drawingArea.Drawn += OnDrawn;

            UpdateDrawingAreaSize();

            scrolledWindow.Add(_drawingArea);

            ShowAll();
        }

        private void OnAddUnit(object sender, EventArgs e)
        {
            var dialog = new Dialog("Add Lithology Unit", this, DialogFlags.Modal);
            dialog.AddButton("Cancel", ResponseType.Cancel);
            dialog.AddButton("Add", ResponseType.Ok);

            var content = dialog.ContentArea;
            var grid = new Grid { RowSpacing = 5, ColumnSpacing = 5, Margin = 10 };
            content.Add(grid);

            var nameEntry = new Entry { Text = "New Unit" };
            var typeEntry = new Entry { Text = "Sandstone" };
            var fromEntry = new Entry { Text = "0" };
            var toEntry = new Entry { Text = "10" };

            grid.Attach(new Label("Name:"), 0, 0, 1, 1);
            grid.Attach(nameEntry, 1, 0, 1, 1);
            grid.Attach(new Label("Type:"), 0, 1, 1, 1);
            grid.Attach(typeEntry, 1, 1, 1, 1);
            grid.Attach(new Label("Depth From:"), 0, 2, 1, 1);
            grid.Attach(fromEntry, 1, 2, 1, 1);
            grid.Attach(new Label("Depth To:"), 0, 3, 1, 1);
            grid.Attach(toEntry, 1, 3, 1, 1);

            dialog.ShowAll();
            var response = (ResponseType)dialog.Run();

            if (response == ResponseType.Ok)
            {
                if (float.TryParse(fromEntry.Text, out float d1) && float.TryParse(toEntry.Text, out float d2))
                {
                    var unit = new LithologyUnit
                    {
                        Name = nameEntry.Text,
                        LithologyType = typeEntry.Text,
                        DepthFrom = d1,
                        DepthTo = d2
                    };
                    _dataset.AddLithologyUnit(unit);

                    // Update total depth if needed
                    if (d2 > _dataset.TotalDepth) _dataset.TotalDepth = d2;

                    UpdateDrawingAreaSize();
                    _drawingArea.QueueDraw();
                }
            }
            dialog.Destroy();
        }

        private void UpdateDrawingAreaSize()
        {
            int height = (int)(_dataset.TotalDepth * _depthScale) + 100;
            _drawingArea.SetSizeRequest(600, height);
        }

        private void OnDrawn(object sender, DrawnArgs args)
        {
            var cr = args.Cr;
            int width = _drawingArea.AllocatedWidth;
            int height = _drawingArea.AllocatedHeight;

            // Background
            cr.SetSourceRGB(0.95, 0.95, 0.95);
            cr.Rectangle(0, 0, width, height);
            cr.Fill();

            // Draw Lithology Column
            double columnX = 60;
            double columnWidth = 150;

            // Draw depth scale
            cr.SetSourceRGB(0.2, 0.2, 0.2);
            cr.LineWidth = 1;
            for (float d = 0; d <= _dataset.TotalDepth; d += 5) // Every 5m
            {
                double y = d * _depthScale;
                cr.MoveTo(0, y);
                cr.LineTo(50, y);
                cr.Stroke();

                cr.MoveTo(5, y + 12);
                cr.ShowText($"{d}m");
            }

            // Draw units
            foreach (var unit in _dataset.LithologyUnits)
            {
                double y1 = unit.DepthFrom * _depthScale;
                double y2 = unit.DepthTo * _depthScale;
                double h = Math.Max(1, y2 - y1);

                // Color
                cr.SetSourceRGBA(unit.Color.X, unit.Color.Y, unit.Color.Z, unit.Color.W);
                cr.Rectangle(columnX, y1, columnWidth, h);
                cr.Fill();

                // Border
                cr.SetSourceRGB(0, 0, 0);
                cr.Rectangle(columnX, y1, columnWidth, h);
                cr.Stroke();

                // Text
                if (h > 15)
                {
                    cr.SetSourceRGB(0, 0, 0);
                    cr.MoveTo(columnX + 5, y1 + 15);
                    cr.ShowText(unit.Name);
                    if (h > 30)
                    {
                         cr.MoveTo(columnX + 5, y1 + 30);
                         cr.ShowText(unit.LithologyType);
                    }
                }
            }

            // Draw Parameter Tracks
            double trackX = columnX + columnWidth + 20;
            foreach (var track in _dataset.ParameterTracks.Values)
            {
                if (!track.IsVisible) continue;

                DrawTrack(cr, track, trackX, 120);
                trackX += 140;
            }
        }

        private void DrawTrack(Context cr, ParameterTrack track, double x, double w)
        {
            // Header
            cr.SetSourceRGB(0, 0, 0);
            cr.MoveTo(x, 20);
            cr.ShowText(track.Name);
            cr.MoveTo(x, 35);
            cr.SetFontSize(10);
            cr.ShowText($"[{track.Unit}]");
            cr.SetFontSize(12);

            // Frame
            cr.Rectangle(x, 0, w, _dataset.TotalDepth * _depthScale);
            cr.Stroke();

            // Grid lines
            cr.SetSourceRGBA(0.8, 0.8, 0.8, 1);
            for (float d = 0; d <= _dataset.TotalDepth; d += 10)
            {
                double y = d * _depthScale;
                cr.MoveTo(x, y);
                cr.LineTo(x + w, y);
                cr.Stroke();
            }

            if (track.Points.Count < 2) return;

            // Plot
            cr.SetSourceRGB(track.Color.X, track.Color.Y, track.Color.Z);
            cr.LineWidth = 2;

            bool first = true;
            foreach (var p in track.Points)
            {
                double y = p.Depth * _depthScale;
                // Log scale handling could be added here
                double valNorm = (p.Value - track.MinValue) / (track.MaxValue - track.MinValue);
                valNorm = Math.Clamp(valNorm, 0, 1);
                double px = x + valNorm * w;

                if (first)
                {
                    cr.MoveTo(px, y);
                    first = false;
                }
                else
                {
                    cr.LineTo(px, y);
                }
            }
            cr.Stroke();
        }
    }
}
