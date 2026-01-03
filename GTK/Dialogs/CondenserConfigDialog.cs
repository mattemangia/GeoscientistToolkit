using System;
using System.Collections.Generic;
using Gtk;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    /// <summary>
    /// Configuration dialog for ORC condenser setup.
    /// Allows detailed configuration of condenser type, cooling parameters, and thermal properties.
    /// </summary>
    public class CondenserConfigDialog : Dialog
    {
        public CondenserConfiguration? CreatedConfiguration { get; private set; }

        // Basic settings
        private readonly Entry _nameEntry;
        private readonly ComboBoxText _typeSelector;

        // Thermal parameters
        private readonly SpinButton _condenserTempSpin;
        private readonly SpinButton _pressureSpin;
        private readonly SpinButton _effectivenessSpin;
        private readonly SpinButton _uaValueSpin;

        // Cooling water parameters
        private readonly SpinButton _coolingFlowRateSpin;
        private readonly SpinButton _coolingInletTempSpin;
        private readonly SpinButton _coolingOutletTempSpin;

        // Pressure drop
        private readonly SpinButton _pressureDropSpin;

        // Geometry
        private readonly SpinButton _tubeCountSpin;
        private readonly SpinButton _tubeLengthSpin;
        private readonly SpinButton _tubeDiameterSpin;

        // Material
        private readonly ComboBoxText _materialSelector;
        private readonly SpinButton _foulingFactorSpin;

        public CondenserConfigDialog(Window parent)
            : base("Configure ORC Condenser", parent, DialogFlags.Modal)
        {
            SetDefaultSize(500, 600);
            BorderWidth = 10;

            var notebook = new Notebook();

            // ==================== TAB 1: Basic Settings ====================
            var basicGrid = new Grid { ColumnSpacing = 10, RowSpacing = 8, BorderWidth = 10 };

            int row = 0;

            // Name
            basicGrid.Attach(new Label("Name:") { Halign = Align.End }, 0, row, 1, 1);
            _nameEntry = new Entry("ORC Condenser 1");
            basicGrid.Attach(_nameEntry, 1, row++, 2, 1);

            // Type
            basicGrid.Attach(new Label("Condenser Type:") { Halign = Align.End }, 0, row, 1, 1);
            _typeSelector = new ComboBoxText();
            _typeSelector.AppendText("Water-Cooled (Shell & Tube)");
            _typeSelector.AppendText("Air-Cooled (Fin-Fan)");
            _typeSelector.AppendText("Evaporative (Cooling Tower)");
            _typeSelector.AppendText("Hybrid (Water + Air)");
            _typeSelector.Active = 0;
            _typeSelector.Changed += OnTypeChanged;
            basicGrid.Attach(_typeSelector, 1, row++, 2, 1);

            // Separator
            basicGrid.Attach(new Separator(Orientation.Horizontal), 0, row++, 3, 1);
            basicGrid.Attach(new Label("<b>Thermal Parameters</b>") { UseMarkup = true }, 0, row++, 3, 1);

            // Condenser Temperature
            basicGrid.Attach(new Label("Condenser Temp (°C):") { Halign = Align.End }, 0, row, 1, 1);
            _condenserTempSpin = new SpinButton(10, 80, 1) { Value = 30 };
            basicGrid.Attach(_condenserTempSpin, 1, row, 1, 1);
            basicGrid.Attach(new Label("Typical: 25-40°C"), 2, row++, 1, 1);

            // Condenser Pressure
            basicGrid.Attach(new Label("Condenser Pressure (bar):") { Halign = Align.End }, 0, row, 1, 1);
            _pressureSpin = new SpinButton(0.5, 20, 0.1) { Value = 2.0, Digits = 2 };
            basicGrid.Attach(_pressureSpin, 1, row, 1, 1);
            basicGrid.Attach(new Label("Depends on fluid"), 2, row++, 1, 1);

            // Effectiveness
            basicGrid.Attach(new Label("Effectiveness (%):") { Halign = Align.End }, 0, row, 1, 1);
            _effectivenessSpin = new SpinButton(50, 99, 1) { Value = 85 };
            basicGrid.Attach(_effectivenessSpin, 1, row, 1, 1);
            basicGrid.Attach(new Label("ε = Q_actual / Q_max"), 2, row++, 1, 1);

            // UA Value
            basicGrid.Attach(new Label("UA Value (kW/K):") { Halign = Align.End }, 0, row, 1, 1);
            _uaValueSpin = new SpinButton(1, 1000, 1) { Value = 50 };
            basicGrid.Attach(_uaValueSpin, 1, row, 1, 1);
            basicGrid.Attach(new Label("Overall HT coeff × Area"), 2, row++, 1, 1);

            notebook.AppendPage(basicGrid, new Label("Basic"));

            // ==================== TAB 2: Cooling System ====================
            var coolingGrid = new Grid { ColumnSpacing = 10, RowSpacing = 8, BorderWidth = 10 };
            row = 0;

            coolingGrid.Attach(new Label("<b>Cooling Water Circuit</b>") { UseMarkup = true }, 0, row++, 3, 1);

            // Cooling Flow Rate
            coolingGrid.Attach(new Label("Flow Rate (kg/s):") { Halign = Align.End }, 0, row, 1, 1);
            _coolingFlowRateSpin = new SpinButton(0.1, 100, 0.1) { Value = 5.0, Digits = 1 };
            coolingGrid.Attach(_coolingFlowRateSpin, 1, row++, 1, 1);

            // Cooling Inlet Temperature
            coolingGrid.Attach(new Label("Inlet Temp (°C):") { Halign = Align.End }, 0, row, 1, 1);
            _coolingInletTempSpin = new SpinButton(5, 40, 1) { Value = 15 };
            coolingGrid.Attach(_coolingInletTempSpin, 1, row, 1, 1);
            coolingGrid.Attach(new Label("From cooling tower/river"), 2, row++, 1, 1);

            // Cooling Outlet Temperature
            coolingGrid.Attach(new Label("Outlet Temp (°C):") { Halign = Align.End }, 0, row, 1, 1);
            _coolingOutletTempSpin = new SpinButton(10, 50, 1) { Value = 25 };
            coolingGrid.Attach(_coolingOutletTempSpin, 1, row, 1, 1);
            coolingGrid.Attach(new Label("ΔT typically 5-15°C"), 2, row++, 1, 1);

            // Pressure Drop
            coolingGrid.Attach(new Separator(Orientation.Horizontal), 0, row++, 3, 1);
            coolingGrid.Attach(new Label("Pressure Drop (kPa):") { Halign = Align.End }, 0, row, 1, 1);
            _pressureDropSpin = new SpinButton(1, 100, 1) { Value = 20 };
            coolingGrid.Attach(_pressureDropSpin, 1, row, 1, 1);
            coolingGrid.Attach(new Label("Shell-side + Tube-side"), 2, row++, 1, 1);

            // Calculated heat rejection
            coolingGrid.Attach(new Separator(Orientation.Horizontal), 0, row++, 3, 1);
            var calcButton = new Button("Calculate Heat Rejection");
            calcButton.Clicked += (s, e) => CalculateHeatRejection();
            coolingGrid.Attach(calcButton, 0, row++, 3, 1);

            notebook.AppendPage(coolingGrid, new Label("Cooling"));

            // ==================== TAB 3: Geometry ====================
            var geomGrid = new Grid { ColumnSpacing = 10, RowSpacing = 8, BorderWidth = 10 };
            row = 0;

            geomGrid.Attach(new Label("<b>Shell & Tube Geometry</b>") { UseMarkup = true }, 0, row++, 3, 1);

            // Tube Count
            geomGrid.Attach(new Label("Number of Tubes:") { Halign = Align.End }, 0, row, 1, 1);
            _tubeCountSpin = new SpinButton(10, 1000, 10) { Value = 100 };
            geomGrid.Attach(_tubeCountSpin, 1, row++, 1, 1);

            // Tube Length
            geomGrid.Attach(new Label("Tube Length (m):") { Halign = Align.End }, 0, row, 1, 1);
            _tubeLengthSpin = new SpinButton(0.5, 10, 0.1) { Value = 3.0, Digits = 1 };
            geomGrid.Attach(_tubeLengthSpin, 1, row++, 1, 1);

            // Tube Diameter
            geomGrid.Attach(new Label("Tube OD (mm):") { Halign = Align.End }, 0, row, 1, 1);
            _tubeDiameterSpin = new SpinButton(10, 50, 1) { Value = 19 };
            geomGrid.Attach(_tubeDiameterSpin, 1, row, 1, 1);
            geomGrid.Attach(new Label("Standard: 19.05mm (3/4\")"), 2, row++, 1, 1);

            // Material
            geomGrid.Attach(new Separator(Orientation.Horizontal), 0, row++, 3, 1);
            geomGrid.Attach(new Label("Tube Material:") { Halign = Align.End }, 0, row, 1, 1);
            _materialSelector = new ComboBoxText();
            _materialSelector.AppendText("Copper");
            _materialSelector.AppendText("Stainless Steel 304");
            _materialSelector.AppendText("Stainless Steel 316");
            _materialSelector.AppendText("Titanium");
            _materialSelector.AppendText("Carbon Steel");
            _materialSelector.Active = 0;
            geomGrid.Attach(_materialSelector, 1, row++, 2, 1);

            // Fouling Factor
            geomGrid.Attach(new Label("Fouling Factor (m²K/kW):") { Halign = Align.End }, 0, row, 1, 1);
            _foulingFactorSpin = new SpinButton(0, 0.5, 0.01) { Value = 0.05, Digits = 3 };
            geomGrid.Attach(_foulingFactorSpin, 1, row, 1, 1);
            geomGrid.Attach(new Label("Clean: 0, Fouled: 0.1-0.5"), 2, row++, 1, 1);

            notebook.AppendPage(geomGrid, new Label("Geometry"));

            ContentArea.PackStart(notebook, true, true, 0);

            AddButton("Cancel", ResponseType.Cancel);
            AddButton("Create", ResponseType.Ok);

            ShowAll();
        }

        private void OnTypeChanged(object? sender, EventArgs e)
        {
            // Update UI based on condenser type
            bool isWaterCooled = _typeSelector.Active == 0 || _typeSelector.Active == 3;
            _coolingFlowRateSpin.Sensitive = isWaterCooled;
            _coolingInletTempSpin.Sensitive = isWaterCooled;
            _coolingOutletTempSpin.Sensitive = isWaterCooled;
        }

        private void CalculateHeatRejection()
        {
            double flowRate = _coolingFlowRateSpin.Value; // kg/s
            double dT = _coolingOutletTempSpin.Value - _coolingInletTempSpin.Value; // °C
            double Cp = 4186; // J/(kg·K) for water
            double Q = flowRate * Cp * dT / 1000; // kW

            var dialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok,
                $"Heat Rejection Capacity:\n\n" +
                $"Q = ṁ × Cp × ΔT\n" +
                $"Q = {flowRate:F1} × 4.186 × {dT:F0}\n" +
                $"Q = {Q:F1} kW\n\n" +
                $"This is the maximum heat the condenser can reject\n" +
                $"with the specified cooling water parameters.");
            dialog.Run();
            dialog.Destroy();
        }

        protected override void OnResponse(ResponseType response_id)
        {
            if (response_id == ResponseType.Ok)
            {
                CreatedConfiguration = new CondenserConfiguration
                {
                    Name = _nameEntry.Text,
                    Type = (CondenserType)_typeSelector.Active,

                    // Thermal
                    CondenserTemperature = _condenserTempSpin.Value,
                    CondenserPressure = _pressureSpin.Value,
                    Effectiveness = _effectivenessSpin.Value / 100.0,
                    UAValue = _uaValueSpin.Value,

                    // Cooling
                    CoolingFlowRate = _coolingFlowRateSpin.Value,
                    CoolingInletTemp = _coolingInletTempSpin.Value,
                    CoolingOutletTemp = _coolingOutletTempSpin.Value,
                    PressureDrop = _pressureDropSpin.Value,

                    // Geometry
                    TubeCount = (int)_tubeCountSpin.Value,
                    TubeLength = _tubeLengthSpin.Value,
                    TubeDiameter = _tubeDiameterSpin.Value / 1000.0, // Convert mm to m
                    Material = _materialSelector.ActiveText ?? "Copper",
                    FoulingFactor = _foulingFactorSpin.Value
                };
            }
            base.OnResponse(response_id);
        }
    }

    /// <summary>
    /// Condenser type enumeration
    /// </summary>
    public enum CondenserType
    {
        WaterCooledShellTube,
        AirCooledFinFan,
        EvaporativeCoolingTower,
        Hybrid
    }

    /// <summary>
    /// Complete condenser configuration for ORC system
    /// </summary>
    public class CondenserConfiguration
    {
        public string Name { get; set; } = "Condenser";
        public CondenserType Type { get; set; } = CondenserType.WaterCooledShellTube;

        // Thermal parameters
        public double CondenserTemperature { get; set; } = 30.0; // °C
        public double CondenserPressure { get; set; } = 2.0; // bar
        public double Effectiveness { get; set; } = 0.85; // 0-1
        public double UAValue { get; set; } = 50.0; // kW/K

        // Cooling circuit
        public double CoolingFlowRate { get; set; } = 5.0; // kg/s
        public double CoolingInletTemp { get; set; } = 15.0; // °C
        public double CoolingOutletTemp { get; set; } = 25.0; // °C
        public double PressureDrop { get; set; } = 20.0; // kPa

        // Geometry
        public int TubeCount { get; set; } = 100;
        public double TubeLength { get; set; } = 3.0; // m
        public double TubeDiameter { get; set; } = 0.019; // m
        public string Material { get; set; } = "Copper";
        public double FoulingFactor { get; set; } = 0.05; // m²K/kW

        /// <summary>
        /// Calculate heat rejection capacity in kW
        /// </summary>
        public double CalculateHeatRejection()
        {
            double Cp = 4186; // J/(kg·K)
            double dT = CoolingOutletTemp - CoolingInletTemp;
            return CoolingFlowRate * Cp * dT / 1000.0; // kW
        }

        /// <summary>
        /// Calculate total heat transfer area in m²
        /// </summary>
        public double CalculateHeatTransferArea()
        {
            return TubeCount * Math.PI * TubeDiameter * TubeLength;
        }
    }
}
