using System;
using Gtk;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class SimulationSetupWizard : Dialog
    {
        private Notebook _notebook;
        private int _currentPage = 0;
        private readonly PhysicoChemDataset? _dataset;

        // Step 1: General
        private Entry _simNameEntry;
        private SpinButton _durationInput;

        // Step 2: Time Stepping
        private SpinButton _dtInput;
        private CheckButton _adaptiveTimeCheck;
        private SpinButton _convergenceToleranceInput;

        // Step 3: Output
        private CheckButton _saveVtkCheck;
        private CheckButton _saveCsvCheck;

        // Step 4: Parameter sweep
        private CheckButton _enableSweepCheck;
        private ComboBoxText _sweepModeCombo;
        private ListStore _sweepStore;
        private TreeView _sweepList;
        private Entry _sweepNameEntry;
        private Entry _sweepTargetEntry;
        private SpinButton _sweepMinSpin;
        private SpinButton _sweepMaxSpin;
        private ComboBoxText _sweepInterpolationCombo;

        public SimulationSetupWizard(Window parent, PhysicoChemDataset? dataset = null) : base("Simulation Setup Wizard", parent, DialogFlags.Modal)
        {
            _dataset = dataset;
            SetDefaultSize(600, 450);
            BorderWidth = 8;

            _notebook = new Notebook { ShowTabs = false, ShowBorder = false };

            // Page 1: General
            _notebook.AppendPage(BuildGeneralPage(), new Label("General"));

            // Page 2: Time
            _notebook.AppendPage(BuildTimePage(), new Label("Time Stepping"));

            // Page 3: Output
            _notebook.AppendPage(BuildOutputPage(), new Label("Output"));

            // Page 4: Parameter Sweep
            _notebook.AppendPage(BuildParameterSweepPage(), new Label("Parameter Sweep"));

            ContentArea.PackStart(_notebook, true, true, 0);

            var buttonBox = new HBox(false, 6);
            var backBtn = new Button("Back");
            backBtn.Clicked += (s, e) => { if (_currentPage > 0) _notebook.CurrentPage = --_currentPage; };

            var nextBtn = new Button("Next");
            nextBtn.Clicked += (s, e) =>
            {
                if (_currentPage < _notebook.NPages - 1)
                    _notebook.CurrentPage = ++_currentPage;
                else
                    Respond(ResponseType.Ok);
            };

            buttonBox.PackEnd(nextBtn, false, false, 0);
            buttonBox.PackEnd(backBtn, false, false, 0);

            ContentArea.PackEnd(buttonBox, false, false, 6);

            ShowAll();
        }

        private Widget BuildGeneralPage()
        {
            var box = new VBox(false, 10) { BorderWidth = 20 };
            box.PackStart(new Label("Step 1: General Settings") { Xalign = 0, Attributes = new Pango.AttrList() }, false, false, 0);

            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
            grid.Attach(new Label("Simulation Name:"), 0, 0, 1, 1);
            _simNameEntry = new Entry("Sim_Run_01");
            grid.Attach(_simNameEntry, 1, 0, 1, 1);

            grid.Attach(new Label("Total Duration (s):"), 0, 1, 1, 1);
            _durationInput = new SpinButton(0, 1e6, 10) { Value = 3600 };
            grid.Attach(_durationInput, 1, 1, 1, 1);

            box.PackStart(grid, false, false, 0);
            return box;
        }

        private Widget BuildTimePage()
        {
            var box = new VBox(false, 10) { BorderWidth = 20 };
            box.PackStart(new Label("Step 2: Time Stepping") { Xalign = 0 }, false, false, 0);

            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
            grid.Attach(new Label("Time Step (dt):"), 0, 0, 1, 1);
            _dtInput = new SpinButton(0.001, 100, 0.1) { Value = 1.0 };
            grid.Attach(_dtInput, 1, 0, 1, 1);

            _adaptiveTimeCheck = new CheckButton("Use Adaptive Time Stepping");
            _adaptiveTimeCheck.Active = true;
            grid.Attach(_adaptiveTimeCheck, 0, 1, 2, 1);

            grid.Attach(new Label("Convergence Tolerance:"), 0, 2, 1, 1);
            _convergenceToleranceInput = new SpinButton(1e-12, 1e-2, 1e-6)
            {
                Value = _dataset?.SimulationParams.ConvergenceTolerance ?? 1e-6,
                Digits = 8
            };
            grid.Attach(_convergenceToleranceInput, 1, 2, 1, 1);

            box.PackStart(grid, false, false, 0);
            return box;
        }

        private Widget BuildOutputPage()
        {
            var box = new VBox(false, 10) { BorderWidth = 20 };
            box.PackStart(new Label("Step 3: Output Configuration") { Xalign = 0 }, false, false, 0);

            _saveVtkCheck = new CheckButton("Save VTK (Paraview)");
            _saveVtkCheck.Active = true;
            box.PackStart(_saveVtkCheck, false, false, 0);

            _saveCsvCheck = new CheckButton("Save CSV Time Series");
            _saveCsvCheck.Active = true;
            box.PackStart(_saveCsvCheck, false, false, 0);

            return box;
        }

        private Widget BuildParameterSweepPage()
        {
            var box = new VBox(false, 10) { BorderWidth = 20 };
            box.PackStart(new Label("Step 4: Parameter Sweep") { Xalign = 0 }, false, false, 0);

            _enableSweepCheck = new CheckButton("Enable Parameter Sweep");
            _enableSweepCheck.Active = _dataset?.SimulationParams.EnableParameterSweep ?? false;
            box.PackStart(_enableSweepCheck, false, false, 0);

            var modeBox = new HBox(false, 6);
            modeBox.PackStart(new Label("Sweep Mode:"), false, false, 0);
            _sweepModeCombo = new ComboBoxText();
            _sweepModeCombo.AppendText(SweepMode.Temporal.ToString());
            _sweepModeCombo.AppendText(SweepMode.Batch.ToString());
            _sweepModeCombo.Active = (int)(_dataset?.ParameterSweepManager.Mode ?? SweepMode.Temporal);
            modeBox.PackStart(_sweepModeCombo, true, true, 0);
            box.PackStart(modeBox, false, false, 0);

            _sweepStore = new ListStore(typeof(bool), typeof(string), typeof(string), typeof(double), typeof(double),
                typeof(string));
            _sweepList = new TreeView(_sweepStore);
            var enabledCell = new CellRendererToggle();
            enabledCell.Toggled += OnSweepEnabledToggled;
            _sweepList.AppendColumn("On", enabledCell, "active", 0);
            _sweepList.AppendColumn("Name", new CellRendererText(), "text", 1);
            _sweepList.AppendColumn("Target", new CellRendererText(), "text", 2);
            _sweepList.AppendColumn("Min", new CellRendererText(), "text", 3);
            _sweepList.AppendColumn("Max", new CellRendererText(), "text", 4);
            _sweepList.AppendColumn("Interp", new CellRendererText(), "text", 5);
            var sweepScroll = new ScrolledWindow { HeightRequest = 140 };
            sweepScroll.Add(_sweepList);
            box.PackStart(sweepScroll, true, true, 0);

            var nameBox = new HBox(false, 6);
            nameBox.PackStart(new Label("Name:"), false, false, 0);
            _sweepNameEntry = new Entry("Temperature");
            nameBox.PackStart(_sweepNameEntry, true, true, 0);
            box.PackStart(nameBox, false, false, 0);

            var targetBox = new HBox(false, 6);
            targetBox.PackStart(new Label("Target Path:"), false, false, 0);
            _sweepTargetEntry = new Entry("SimulationParams.TotalTime");
            targetBox.PackStart(_sweepTargetEntry, true, true, 0);
            box.PackStart(targetBox, false, false, 0);

            var rangeBox = new HBox(false, 6);
            rangeBox.PackStart(new Label("Min:"), false, false, 0);
            _sweepMinSpin = new SpinButton(-1e6, 1e6, 0.1) { Value = 0.0 };
            rangeBox.PackStart(_sweepMinSpin, true, true, 0);
            rangeBox.PackStart(new Label("Max:"), false, false, 0);
            _sweepMaxSpin = new SpinButton(-1e6, 1e6, 0.1) { Value = 1.0 };
            rangeBox.PackStart(_sweepMaxSpin, true, true, 0);
            box.PackStart(rangeBox, false, false, 0);

            var interpBox = new HBox(false, 6);
            interpBox.PackStart(new Label("Interpolation:"), false, false, 0);
            _sweepInterpolationCombo = new ComboBoxText();
            foreach (var interp in Enum.GetNames<InterpolationType>())
                _sweepInterpolationCombo.AppendText(interp);
            _sweepInterpolationCombo.Active = 0;
            interpBox.PackStart(_sweepInterpolationCombo, true, true, 0);
            box.PackStart(interpBox, false, false, 0);

            var buttonBox = new HBox(false, 6);
            var addBtn = new Button("Add Sweep");
            addBtn.Clicked += (_, _) => AddSweep();
            var removeBtn = new Button("Remove Selected");
            removeBtn.Clicked += (_, _) => RemoveSelectedSweep();
            buttonBox.PackStart(addBtn, true, true, 0);
            buttonBox.PackStart(removeBtn, true, true, 0);
            box.PackStart(buttonBox, false, false, 0);

            RefreshSweepList();
            return box;
        }

        public void ApplySettings()
        {
            if (_dataset == null) return;

            _dataset.SimulationParams.TotalTime = _durationInput.Value;
            _dataset.SimulationParams.TimeStep = _dtInput.Value;
            _dataset.SimulationParams.ConvergenceTolerance = _convergenceToleranceInput.Value;
            _dataset.SimulationParams.EnableParameterSweep = _enableSweepCheck.Active;
            _dataset.ParameterSweepManager.Enabled = _enableSweepCheck.Active;
            _dataset.ParameterSweepManager.Mode = (SweepMode)_sweepModeCombo.Active;

            _dataset.ParameterSweepManager.Sweeps.Clear();
            if (_sweepStore.GetIterFirst(out var iter))
            {
                do
                {
                    var enabled = (bool)_sweepStore.GetValue(iter, 0);
                    var name = (string)_sweepStore.GetValue(iter, 1);
                    var target = (string)_sweepStore.GetValue(iter, 2);
                    var min = (double)_sweepStore.GetValue(iter, 3);
                    var max = (double)_sweepStore.GetValue(iter, 4);
                    var interp = (string)_sweepStore.GetValue(iter, 5);
                    Enum.TryParse(interp, out InterpolationType interpolation);

                    _dataset.ParameterSweepManager.Sweeps.Add(new ParameterSweep
                    {
                        Enabled = enabled,
                        ParameterName = name,
                        TargetPath = target,
                        MinValue = min,
                        MaxValue = max,
                        Interpolation = interpolation
                    });
                } while (_sweepStore.IterNext(ref iter));
            }
        }

        private void AddSweep()
        {
            _sweepStore.AppendValues(true, _sweepNameEntry.Text, _sweepTargetEntry.Text, _sweepMinSpin.Value,
                _sweepMaxSpin.Value, _sweepInterpolationCombo.ActiveText ?? InterpolationType.Linear.ToString());
        }

        private void RemoveSelectedSweep()
        {
            if (_sweepList.Selection.GetSelected(out var model, out var iter))
            {
                if (model is ListStore store)
                {
                    store.Remove(ref iter);
                }
            }
        }

        private void RefreshSweepList()
        {
            _sweepStore.Clear();
            if (_dataset?.ParameterSweepManager?.Sweeps == null) return;
            foreach (var sweep in _dataset.ParameterSweepManager.Sweeps)
            {
                _sweepStore.AppendValues(sweep.Enabled, sweep.ParameterName, sweep.TargetPath, sweep.MinValue,
                    sweep.MaxValue, sweep.Interpolation.ToString());
            }
        }

        private void OnSweepEnabledToggled(object o, ToggledArgs args)
        {
            if (!_sweepStore.GetIter(out var iter, new TreePath(args.Path))) return;
            bool enabled = (bool)_sweepStore.GetValue(iter, 0);
            _sweepStore.SetValue(iter, 0, !enabled);
        }
    }
}
