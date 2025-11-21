using System;
using Gtk;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class SimulationSetupWizard : Dialog
    {
        private Notebook _notebook;
        private int _currentPage = 0;

        // Step 1: General
        private Entry _simNameEntry;
        private SpinButton _durationInput;

        // Step 2: Time Stepping
        private SpinButton _dtInput;
        private CheckButton _adaptiveTimeCheck;

        // Step 3: Output
        private CheckButton _saveVtkCheck;
        private CheckButton _saveCsvCheck;

        public SimulationSetupWizard(Window parent) : base("Simulation Setup Wizard", parent, DialogFlags.Modal)
        {
            SetDefaultSize(600, 450);
            BorderWidth = 8;

            _notebook = new Notebook { ShowTabs = false, ShowBorder = false };

            // Page 1: General
            _notebook.AppendPage(BuildGeneralPage(), new Label("General"));

            // Page 2: Time
            _notebook.AppendPage(BuildTimePage(), new Label("Time Stepping"));

            // Page 3: Output
            _notebook.AppendPage(BuildOutputPage(), new Label("Output"));

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
    }
}
