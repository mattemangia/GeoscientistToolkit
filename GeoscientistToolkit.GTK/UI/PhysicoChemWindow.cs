using System;
using Gtk;
using Cairo;
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Data.Mesh3D;
using System.Numerics;

namespace GeoscientistToolkit.GTK.UI
{
    public class PhysicoChemWindow : Window
    {
        private PhysicoChemDataset _dataset;
        private DrawingArea _visualizationArea;
        private int _currentSlice = 0;
        private double _rotationX = 0.5;
        private double _rotationY = 0.5;
        private double _scale = 20.0;

        public PhysicoChemWindow(PhysicoChemDataset dataset) : base($"PhysicoChem Simulation: {dataset.Name}")
        {
            _dataset = dataset;
            SetDefaultSize(1000, 700);

            var vbox = new Box(Orientation.Vertical, 0);
            Add(vbox);

            // Controls
            var controlsBox = new Box(Orientation.Horizontal, 5);
            vbox.PackStart(controlsBox, false, false, 5);

            var runButton = new Button("Run Simulation");
            runButton.Clicked += OnRunSimulation;
            controlsBox.PackStart(runButton, false, false, 0);

            var rotateButton = new Button("Rotate View");
            rotateButton.Clicked += (s, e) => { _rotationY += 0.1; _visualizationArea.QueueDraw(); };
            controlsBox.PackStart(rotateButton, false, false, 0);

            var zoomButton = new Button("Zoom In");
            zoomButton.Clicked += (s, e) => { _scale *= 1.2; _visualizationArea.QueueDraw(); };
            controlsBox.PackStart(zoomButton, false, false, 0);

            // Visualization
            _visualizationArea = new DrawingArea();
            _visualizationArea.Drawn += OnDrawn;

            // Enable mouse interaction for rotation
            _visualizationArea.AddEvents((int)Gdk.EventMask.ButtonPressMask | (int)Gdk.EventMask.PointerMotionMask);
            _visualizationArea.MotionNotifyEvent += OnMotionNotify;

            vbox.PackStart(_visualizationArea, true, true, 0);

            ShowAll();
        }

        private double _lastMouseX, _lastMouseY;

        private void OnMotionNotify(object o, MotionNotifyEventArgs args)
        {
            if (args.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                double dx = args.Event.X - _lastMouseX;
                double dy = args.Event.Y - _lastMouseY;
                _rotationY += dx * 0.01;
                _rotationX += dy * 0.01;
                _visualizationArea.QueueDraw();
            }
            _lastMouseX = args.Event.X;
            _lastMouseY = args.Event.Y;
        }

        private void OnRunSimulation(object sender, EventArgs e)
        {
            // Stub for running simulation
            var md = new MessageDialog(this, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok,
                "Simulation started (Stub).");
            md.Run();
            md.Destroy();
        }

        private void OnDrawn(object sender, DrawnArgs args)
        {
            var cr = args.Cr;
            int width = _visualizationArea.AllocatedWidth;
            int height = _visualizationArea.AllocatedHeight;

            // Background
            cr.SetSourceRGB(0.1, 0.1, 0.15);
            cr.Rectangle(0, 0, width, height);
            cr.Fill();

            if (_dataset.CurrentState == null)
            {
                cr.SetSourceRGB(1, 1, 1);
                cr.MoveTo(width / 2 - 50, height / 2);
                cr.ShowText("No simulation state available.");
                return;
            }

            // Draw 3D Mesh (Software Projection)
            var temp = _dataset.CurrentState.Temperature;
            int nx = temp.GetLength(0);
            int ny = temp.GetLength(1);
            int nz = temp.GetLength(2);

            double cx = width / 2.0;
            double cy = height / 2.0;

            // Iterate cells back-to-front (painter's algorithm approx)
            // For simplicity, just draw points or small cubes

            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                // Center coordinates
                double wx = x - nx / 2.0;
                double wy = y - ny / 2.0;
                double wz = z - nz / 2.0;

                // Rotate Y
                double tx = wx * Math.Cos(_rotationY) - wz * Math.Sin(_rotationY);
                double tz = wx * Math.Sin(_rotationY) + wz * Math.Cos(_rotationY);
                wx = tx; wz = tz;

                // Rotate X
                double ty = wy * Math.Cos(_rotationX) - wz * Math.Sin(_rotationX);
                tz = wy * Math.Sin(_rotationX) + wz * Math.Cos(_rotationX);
                wy = ty; wz = tz;

                // Project
                double persp = 1000.0 / (1000.0 + wz * _scale); // Perspective
                double sx = cx + wx * _scale * persp;
                double sy = cy + wy * _scale * persp;
                double size = _scale * 0.8 * persp;

                // Color based on temperature
                float val = temp[x, y, z];
                float t = Math.Clamp((val - 273.15f) / 100.0f, 0, 1);
                cr.SetSourceRGBA(t, 0, 1 - t, 0.8); // Semi-transparent

                // Draw cube face (front)
                if (size > 1)
                {
                    cr.Rectangle(sx - size/2, sy - size/2, size, size);
                    cr.Fill();
                }
            }

            // Info Overlay
            cr.SetSourceRGB(1, 1, 1);
            cr.MoveTo(10, 20);
            cr.ShowText($"Time: {_dataset.CurrentState.CurrentTime:F2}s");
            cr.MoveTo(10, 40);
            cr.ShowText($"Avg Temp: {_dataset.CurrentState.AverageTemperature:F2} K");
        }
    }
}
