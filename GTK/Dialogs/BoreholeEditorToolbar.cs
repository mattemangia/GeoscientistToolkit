// GeoscientistToolkit/GTK/Dialogs/BoreholeEditorToolbar.cs

using Gtk;
using System;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class BoreholeEditorToolbar : Box
    {
        public event System.Action<bool>? AutoScaleDepthChanged;
        public event System.Action<double, double>? DepthRangeChanged;
        public event System.Action<bool>? ShowGridChanged;
        public event System.Action<bool>? ShowLegendChanged;
        public event System.Action? ImportLasClicked;
        public event System.Action<double>? ExportLasClicked;

        private readonly CheckButton _autoScaleCheck;
        private readonly SpinButton _startDepthSpin;
        private readonly SpinButton _endDepthSpin;
        private readonly CheckButton _gridCheck;
        private readonly CheckButton _legendCheck;
        private readonly Button _importButton;
        private readonly Button _exportButton;
        private readonly SpinButton _exportStepSpin;

        public BoreholeEditorToolbar(double maxDepth) : base(Orientation.Horizontal, 5)
        {
            // Depth Range Controls
            Add(new Label("Depth Range:"));
            _autoScaleCheck = new CheckButton("Auto") { Active = true };
            _autoScaleCheck.Toggled += (s, e) =>
            {
                var active = _autoScaleCheck.Active;
                _startDepthSpin.Sensitive = !active;
                _endDepthSpin.Sensitive = !active;
                AutoScaleDepthChanged?.Invoke(active);
            };
            Add(_autoScaleCheck);

            _startDepthSpin = new SpinButton(0, maxDepth, 0.1) { Sensitive = false, WidthChars = 7 };
            _startDepthSpin.ValueChanged += OnDepthRangeChanged;
            Add(_startDepthSpin);

            Add(new Label("to"));

            _endDepthSpin = new SpinButton(0, maxDepth, 0.1) { Sensitive = false, WidthChars = 7 };
            _endDepthSpin.Value = maxDepth;
            _endDepthSpin.ValueChanged += OnDepthRangeChanged;
            Add(_endDepthSpin);

            Add(new Separator(Orientation.Vertical));

            // Toggles
            _gridCheck = new CheckButton("Grid") { Active = true };
            _gridCheck.Toggled += (s, e) => ShowGridChanged?.Invoke(_gridCheck.Active);
            Add(_gridCheck);

            _legendCheck = new CheckButton("Legend") { Active = false };
            _legendCheck.Toggled += (s, e) => ShowLegendChanged?.Invoke(_legendCheck.Active);
            Add(_legendCheck);

            Add(new Separator(Orientation.Vertical));

            // Import/Export
            _importButton = new Button("Import LAS");
            _importButton.Clicked += (s, e) => ImportLasClicked?.Invoke();
            Add(_importButton);

            _exportButton = new Button("Export LAS");
            _exportButton.Clicked += (s, e) => ExportLasClicked?.Invoke(_exportStepSpin.Value);
            Add(_exportButton);

            _exportStepSpin = new SpinButton(0.01, 10, 0.01) { Value = 0.1, WidthChars = 5 };
            Add(_exportStepSpin);
        }

        private void OnDepthRangeChanged(object sender, EventArgs e)
        {
            DepthRangeChanged?.Invoke(_startDepthSpin.Value, _endDepthSpin.Value);
        }

        public void SetDepthRange(double start, double end)
        {
            _startDepthSpin.Value = start;
            _endDepthSpin.Value = end;
        }

        public void UpdateMaxDepth(double maxDepth)
        {
            _startDepthSpin.SetRange(0, maxDepth);
            _endDepthSpin.SetRange(0, maxDepth);
            _endDepthSpin.Value = maxDepth;
        }
    }
}
