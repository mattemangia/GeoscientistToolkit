using System;
using System.Data;
using System.Globalization;
using Gtk;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Business.Thermodynamics;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.GtkUI.Dialogs;

public class ThermodynamicSweepDialog : Dialog
{
    private readonly Entry _compositionEntry;
    private readonly Entry _datasetNameEntry;
    private readonly SpinButton _minTempSpin;
    private readonly SpinButton _maxTempSpin;
    private readonly SpinButton _minPressureSpin;
    private readonly SpinButton _maxPressureSpin;
    private readonly SpinButton _gridPointsSpin;

    public TableDataset? CreatedDataset { get; private set; }

    public ThermodynamicSweepDialog(Window parent) : base("Thermodynamic Parameter Sweep", parent, DialogFlags.Modal)
    {
        SetDefaultSize(520, 360);
        BorderWidth = 8;

        var content = new VBox(false, 8) { BorderWidth = 10 };
        content.PackStart(new Label("Configure a temperature/pressure sweep for equilibrium calculations.")
        {
            Xalign = 0
        }, false, false, 0);

        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 6 };
        grid.Attach(new Label("Composition (moles):") { Xalign = 0 }, 0, 0, 1, 1);
        _compositionEntry = new Entry("'H₂O'=55.5, 'CO₂'=1.0");
        grid.Attach(_compositionEntry, 1, 0, 1, 1);

        grid.Attach(new Label("Result Dataset Name:") { Xalign = 0 }, 0, 1, 1, 1);
        _datasetNameEntry = new Entry("ThermoSweep");
        grid.Attach(_datasetNameEntry, 1, 1, 1, 1);

        grid.Attach(new Label("Min Temperature (K):") { Xalign = 0 }, 0, 2, 1, 1);
        _minTempSpin = new SpinButton(1, 5000, 1) { Value = 273.15 };
        grid.Attach(_minTempSpin, 1, 2, 1, 1);

        grid.Attach(new Label("Max Temperature (K):") { Xalign = 0 }, 0, 3, 1, 1);
        _maxTempSpin = new SpinButton(1, 5000, 1) { Value = 473.15 };
        grid.Attach(_maxTempSpin, 1, 3, 1, 1);

        grid.Attach(new Label("Min Pressure (bar):") { Xalign = 0 }, 0, 4, 1, 1);
        _minPressureSpin = new SpinButton(0.1, 10000, 1) { Value = 1 };
        grid.Attach(_minPressureSpin, 1, 4, 1, 1);

        grid.Attach(new Label("Max Pressure (bar):") { Xalign = 0 }, 0, 5, 1, 1);
        _maxPressureSpin = new SpinButton(0.1, 10000, 1) { Value = 1000 };
        grid.Attach(_maxPressureSpin, 1, 5, 1, 1);

        grid.Attach(new Label("Grid Points:") { Xalign = 0 }, 0, 6, 1, 1);
        _gridPointsSpin = new SpinButton(5, 100, 1) { Value = 25 };
        grid.Attach(_gridPointsSpin, 1, 6, 1, 1);

        content.PackStart(grid, false, false, 0);
        ContentArea.PackStart(content, true, true, 0);

        AddButton("Cancel", ResponseType.Cancel);
        AddButton("Run Sweep", ResponseType.Ok);

        ShowAll();
    }

    public void RunSweep()
    {
        var composition = ParseComposition(_compositionEntry.Text);
        var generator = new PhaseDiagramGenerator();
        var ptData = generator.GeneratePTDiagram(composition, (float)_minTempSpin.Value, (float)_maxTempSpin.Value,
            (float)_minPressureSpin.Value, (float)_maxPressureSpin.Value, (int)_gridPointsSpin.Value);

        var datasetName = string.IsNullOrWhiteSpace(_datasetNameEntry.Text) ? "ThermoSweep" : _datasetNameEntry.Text.Trim();
        var table = new DataTable(datasetName);
        table.Columns.Add("Temperature_K", typeof(double));
        table.Columns.Add("Pressure_bar", typeof(double));
        table.Columns.Add("DominantPhase", typeof(string));
        foreach (var point in ptData.Points)
        {
            table.Rows.Add(point.Temperature_K, point.Pressure_bar, point.DominantPhase);
        }

        CreatedDataset = new TableDataset(datasetName, table);
        Logger.Log($"Created thermodynamic sweep dataset: {CreatedDataset.Name}");
    }

    private static System.Collections.Generic.Dictionary<string, double> ParseComposition(string compStr)
    {
        var composition = new System.Collections.Generic.Dictionary<string, double>();
        var parts = compStr.Split(',');
        foreach (var part in parts)
        {
            var pair = part.Split('=');
            if (pair.Length == 2)
            {
                var name = pair[0].Trim().Trim('\'');
                var moles = double.Parse(pair[1].Trim(), CultureInfo.InvariantCulture);
                composition[name] = moles;
            }
        }

        return composition;
    }
}
