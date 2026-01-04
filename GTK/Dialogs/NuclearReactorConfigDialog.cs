using System;
using Gtk;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.GTK.Dialogs
{
    /// <summary>
    /// GTK dialog for configuring nuclear reactor simulation parameters.
    /// Supports PWR, CANDU/PHWR, BWR, and research reactor configurations.
    /// </summary>
    public class NuclearReactorConfigDialog : Dialog
    {
        private readonly NuclearReactorParameters _config;

        // Reactor Type
        private ComboBoxText _reactorTypeCombo = null!;

        // Core Geometry
        private SpinButton _thermalPowerSpin = null!;
        private SpinButton _electricalPowerSpin = null!;
        private SpinButton _coreHeightSpin = null!;
        private SpinButton _coreDiameterSpin = null!;
        private SpinButton _numAssembliesSpin = null!;
        private SpinButton _assemblyPitchSpin = null!;

        // Moderator
        private ComboBoxText _moderatorTypeCombo = null!;
        private SpinButton _moderatorDensitySpin = null!;
        private SpinButton _moderatorTempSpin = null!;
        private SpinButton _d2oPuritySpin = null!;
        private Label _moderationRatioLabel = null!;

        // Coolant
        private ComboBoxText _coolantTypeCombo = null!;
        private SpinButton _coolantInletTempSpin = null!;
        private SpinButton _coolantOutletTempSpin = null!;
        private SpinButton _coolantPressureSpin = null!;
        private SpinButton _coolantFlowRateSpin = null!;
        private Label _heatRemovalLabel = null!;

        // Fuel
        private SpinButton _enrichmentSpin = null!;
        private SpinButton _rodsPerAssemblySpin = null!;
        private SpinButton _pelletDiameterSpin = null!;
        private SpinButton _cladDiameterSpin = null!;
        private ComboBoxText _cladMaterialCombo = null!;
        private Label _fuelMassLabel = null!;

        // Control
        private SpinButton _boronConcSpin = null!;
        private SpinButton _numControlBanksSpin = null!;
        private SpinButton _controlRodWorthSpin = null!;

        // Safety
        private SpinButton _scramPowerSpin = null!;
        private SpinButton _scramPeriodSpin = null!;
        private SpinButton _scramTempSpin = null!;
        private CheckButton _hasEccsCheck = null!;

        // Display
        private Label _summaryLabel = null!;

        public NuclearReactorParameters? CreatedConfiguration { get; private set; }

        public NuclearReactorConfigDialog(Window parent) : base(
            "Nuclear Reactor Configuration",
            parent,
            DialogFlags.Modal | DialogFlags.DestroyWithParent,
            "Cancel", ResponseType.Cancel,
            "Create Reactor", ResponseType.Ok)
        {
            _config = new NuclearReactorParameters();
            SetDefaultSize(800, 700);
            BuildUI();
            LoadDefaultPWR();
            UpdateSummary();
        }

        private void BuildUI()
        {
            var notebook = new Notebook();
            ContentArea.PackStart(notebook, true, true, 0);

            // Tab 1: Reactor Type & Core
            notebook.AppendPage(CreateCoreTab(), new Label("Core Design"));

            // Tab 2: Moderator & Coolant
            notebook.AppendPage(CreateModeratorCoolantTab(), new Label("Moderator/Coolant"));

            // Tab 3: Fuel Assemblies
            notebook.AppendPage(CreateFuelTab(), new Label("Fuel"));

            // Tab 4: Control Systems
            notebook.AppendPage(CreateControlTab(), new Label("Control"));

            // Tab 5: Safety & Summary
            notebook.AppendPage(CreateSafetyTab(), new Label("Safety"));

            ContentArea.ShowAll();
        }

        private Widget CreateCoreTab()
        {
            var vbox = new VBox(false, 10) { BorderWidth = 15 };

            // Reactor Type Frame
            var typeFrame = new Frame("Reactor Type");
            var typeBox = new VBox(false, 5) { BorderWidth = 10 };

            _reactorTypeCombo = new ComboBoxText();
            _reactorTypeCombo.AppendText("PWR - Pressurized Water Reactor");
            _reactorTypeCombo.AppendText("PHWR - CANDU (Heavy Water)");
            _reactorTypeCombo.AppendText("BWR - Boiling Water Reactor");
            _reactorTypeCombo.AppendText("HTGR - High Temperature Gas");
            _reactorTypeCombo.AppendText("Research Reactor");
            _reactorTypeCombo.Active = 0;
            _reactorTypeCombo.Changed += OnReactorTypeChanged;
            typeBox.PackStart(CreateLabeledWidget("Reactor Type:", _reactorTypeCombo), false, false, 0);

            // Preset buttons
            var presetBox = new HBox(true, 5);
            var pwrButton = new Button("Load PWR Preset");
            pwrButton.Clicked += (s, e) => LoadDefaultPWR();
            var canduButton = new Button("Load CANDU Preset");
            canduButton.Clicked += (s, e) => LoadDefaultCANDU();
            presetBox.PackStart(pwrButton, true, true, 0);
            presetBox.PackStart(canduButton, true, true, 0);
            typeBox.PackStart(presetBox, false, false, 5);

            typeFrame.Add(typeBox);
            vbox.PackStart(typeFrame, false, false, 0);

            // Power Frame
            var powerFrame = new Frame("Power Ratings");
            var powerGrid = new Grid { RowSpacing = 8, ColumnSpacing = 10, MarginStart = 10, MarginEnd = 10, MarginTop = 10, MarginBottom = 10 };

            _thermalPowerSpin = new SpinButton(100, 5000, 10) { Value = 3411 };
            _thermalPowerSpin.ValueChanged += (s, e) => UpdateSummary();
            powerGrid.Attach(new Label("Thermal Power (MWth):") { Halign = Align.End }, 0, 0, 1, 1);
            powerGrid.Attach(_thermalPowerSpin, 1, 0, 1, 1);

            _electricalPowerSpin = new SpinButton(30, 2000, 10) { Value = 1150 };
            _electricalPowerSpin.ValueChanged += (s, e) => UpdateSummary();
            powerGrid.Attach(new Label("Electrical Power (MWe):") { Halign = Align.End }, 0, 1, 1, 1);
            powerGrid.Attach(_electricalPowerSpin, 1, 1, 1, 1);

            var effLabel = new Label("Efficiency: 33.7%") { Halign = Align.Start };
            _thermalPowerSpin.ValueChanged += (s, e) =>
                effLabel.Text = $"Efficiency: {_electricalPowerSpin.Value / _thermalPowerSpin.Value * 100:F1}%";
            _electricalPowerSpin.ValueChanged += (s, e) =>
                effLabel.Text = $"Efficiency: {_electricalPowerSpin.Value / _thermalPowerSpin.Value * 100:F1}%";
            powerGrid.Attach(effLabel, 2, 0, 1, 2);

            powerFrame.Add(powerGrid);
            vbox.PackStart(powerFrame, false, false, 0);

            // Core Geometry Frame
            var geoFrame = new Frame("Core Geometry");
            var geoGrid = new Grid { RowSpacing = 8, ColumnSpacing = 10, MarginStart = 10, MarginEnd = 10, MarginTop = 10, MarginBottom = 10 };

            _coreHeightSpin = new SpinButton(1, 10, 0.1) { Value = 3.66, Digits = 2 };
            geoGrid.Attach(new Label("Active Core Height (m):") { Halign = Align.End }, 0, 0, 1, 1);
            geoGrid.Attach(_coreHeightSpin, 1, 0, 1, 1);

            _coreDiameterSpin = new SpinButton(1, 10, 0.1) { Value = 3.37, Digits = 2 };
            geoGrid.Attach(new Label("Core Diameter (m):") { Halign = Align.End }, 0, 1, 1, 1);
            geoGrid.Attach(_coreDiameterSpin, 1, 1, 1, 1);

            _numAssembliesSpin = new SpinButton(1, 500, 1) { Value = 193 };
            geoGrid.Attach(new Label("Number of Assemblies:") { Halign = Align.End }, 0, 2, 1, 1);
            geoGrid.Attach(_numAssembliesSpin, 1, 2, 1, 1);

            _assemblyPitchSpin = new SpinButton(0.1, 0.5, 0.001) { Value = 0.214, Digits = 3 };
            geoGrid.Attach(new Label("Assembly Pitch (m):") { Halign = Align.End }, 0, 3, 1, 1);
            geoGrid.Attach(_assemblyPitchSpin, 1, 3, 1, 1);

            geoFrame.Add(geoGrid);
            vbox.PackStart(geoFrame, false, false, 0);

            return vbox;
        }

        private Widget CreateModeratorCoolantTab()
        {
            var vbox = new VBox(false, 10) { BorderWidth = 15 };

            // Moderator Frame
            var modFrame = new Frame("Neutron Moderator");
            var modGrid = new Grid { RowSpacing = 8, ColumnSpacing = 10, MarginStart = 10, MarginEnd = 10, MarginTop = 10, MarginBottom = 10 };

            _moderatorTypeCombo = new ComboBoxText();
            _moderatorTypeCombo.AppendText("Light Water (H2O)");
            _moderatorTypeCombo.AppendText("Heavy Water (D2O)");
            _moderatorTypeCombo.AppendText("Graphite");
            _moderatorTypeCombo.AppendText("Beryllium");
            _moderatorTypeCombo.Active = 0;
            _moderatorTypeCombo.Changed += OnModeratorTypeChanged;
            modGrid.Attach(new Label("Moderator Type:") { Halign = Align.End }, 0, 0, 1, 1);
            modGrid.Attach(_moderatorTypeCombo, 1, 0, 1, 1);

            _moderatorDensitySpin = new SpinButton(100, 2000, 10) { Value = 700 };
            modGrid.Attach(new Label("Density (kg/m³):") { Halign = Align.End }, 0, 1, 1, 1);
            modGrid.Attach(_moderatorDensitySpin, 1, 1, 1, 1);

            _moderatorTempSpin = new SpinButton(20, 400, 5) { Value = 300 };
            modGrid.Attach(new Label("Temperature (°C):") { Halign = Align.End }, 0, 2, 1, 1);
            modGrid.Attach(_moderatorTempSpin, 1, 2, 1, 1);

            _d2oPuritySpin = new SpinButton(90, 99.99, 0.01) { Value = 99.75, Digits = 2 };
            modGrid.Attach(new Label("D2O Purity (%):") { Halign = Align.End }, 0, 3, 1, 1);
            modGrid.Attach(_d2oPuritySpin, 1, 3, 1, 1);

            _moderationRatioLabel = new Label("Moderation Ratio: 74") { Halign = Align.Start };
            modGrid.Attach(_moderationRatioLabel, 2, 0, 1, 4);

            // Info about moderators
            var modInfo = new Label(
                "<b>Moderator Properties:</b>\n" +
                "• Light Water: Good moderator, absorbs neutrons → requires enriched fuel\n" +
                "• Heavy Water: Excellent moderator, low absorption → can use natural uranium\n" +
                "• Graphite: Solid moderator for gas-cooled reactors")
            { UseMarkup = true, Wrap = true, Halign = Align.Start };
            modGrid.Attach(modInfo, 0, 4, 3, 1);

            modFrame.Add(modGrid);
            vbox.PackStart(modFrame, false, false, 0);

            // Coolant Frame
            var coolFrame = new Frame("Primary Coolant");
            var coolGrid = new Grid { RowSpacing = 8, ColumnSpacing = 10, MarginStart = 10, MarginEnd = 10, MarginTop = 10, MarginBottom = 10 };

            _coolantTypeCombo = new ComboBoxText();
            _coolantTypeCombo.AppendText("Light Water");
            _coolantTypeCombo.AppendText("Heavy Water");
            _coolantTypeCombo.AppendText("Helium");
            _coolantTypeCombo.AppendText("CO2");
            _coolantTypeCombo.AppendText("Liquid Sodium");
            _coolantTypeCombo.AppendText("Lead-Bismuth");
            _coolantTypeCombo.Active = 0;
            coolGrid.Attach(new Label("Coolant Type:") { Halign = Align.End }, 0, 0, 1, 1);
            coolGrid.Attach(_coolantTypeCombo, 1, 0, 1, 1);

            _coolantInletTempSpin = new SpinButton(100, 400, 5) { Value = 292 };
            coolGrid.Attach(new Label("Inlet Temperature (°C):") { Halign = Align.End }, 0, 1, 1, 1);
            coolGrid.Attach(_coolantInletTempSpin, 1, 1, 1, 1);

            _coolantOutletTempSpin = new SpinButton(150, 500, 5) { Value = 326 };
            coolGrid.Attach(new Label("Outlet Temperature (°C):") { Halign = Align.End }, 0, 2, 1, 1);
            coolGrid.Attach(_coolantOutletTempSpin, 1, 2, 1, 1);

            _coolantPressureSpin = new SpinButton(0.1, 20, 0.5) { Value = 15.5, Digits = 1 };
            coolGrid.Attach(new Label("Pressure (MPa):") { Halign = Align.End }, 0, 3, 1, 1);
            coolGrid.Attach(_coolantPressureSpin, 1, 3, 1, 1);

            _coolantFlowRateSpin = new SpinButton(100, 30000, 100) { Value = 17400 };
            coolGrid.Attach(new Label("Mass Flow Rate (kg/s):") { Halign = Align.End }, 0, 4, 1, 1);
            coolGrid.Attach(_coolantFlowRateSpin, 1, 4, 1, 1);

            _heatRemovalLabel = new Label("Heat Removal: 3411 MW") { Halign = Align.Start };
            coolGrid.Attach(_heatRemovalLabel, 2, 1, 1, 4);

            _coolantFlowRateSpin.ValueChanged += (s, e) => UpdateHeatRemoval();
            _coolantInletTempSpin.ValueChanged += (s, e) => UpdateHeatRemoval();
            _coolantOutletTempSpin.ValueChanged += (s, e) => UpdateHeatRemoval();

            coolFrame.Add(coolGrid);
            vbox.PackStart(coolFrame, false, false, 0);

            return vbox;
        }

        private Widget CreateFuelTab()
        {
            var vbox = new VBox(false, 10) { BorderWidth = 15 };

            // Fuel Enrichment Frame
            var enrichFrame = new Frame("Fuel Composition");
            var enrichGrid = new Grid { RowSpacing = 8, ColumnSpacing = 10, MarginStart = 10, MarginEnd = 10, MarginTop = 10, MarginBottom = 10 };

            _enrichmentSpin = new SpinButton(0.7, 20, 0.1) { Value = 3.5, Digits = 2 };
            _enrichmentSpin.ValueChanged += (s, e) => UpdateSummary();
            enrichGrid.Attach(new Label("U-235 Enrichment (%):") { Halign = Align.End }, 0, 0, 1, 1);
            enrichGrid.Attach(_enrichmentSpin, 1, 0, 1, 1);

            var enrichInfo = new Label("• Natural uranium: 0.71%\n• LEU (PWR): 3-5%\n• HEU (research): >20%") { Halign = Align.Start };
            enrichGrid.Attach(enrichInfo, 2, 0, 1, 1);

            enrichFrame.Add(enrichGrid);
            vbox.PackStart(enrichFrame, false, false, 0);

            // Fuel Rod Geometry Frame
            var rodFrame = new Frame("Fuel Rod Geometry");
            var rodGrid = new Grid { RowSpacing = 8, ColumnSpacing = 10, MarginStart = 10, MarginEnd = 10, MarginTop = 10, MarginBottom = 10 };

            _rodsPerAssemblySpin = new SpinButton(1, 500, 1) { Value = 264 };
            rodGrid.Attach(new Label("Rods per Assembly:") { Halign = Align.End }, 0, 0, 1, 1);
            rodGrid.Attach(_rodsPerAssemblySpin, 1, 0, 1, 1);

            _pelletDiameterSpin = new SpinButton(5, 15, 0.1) { Value = 8.2, Digits = 1 };
            rodGrid.Attach(new Label("Pellet Diameter (mm):") { Halign = Align.End }, 0, 1, 1, 1);
            rodGrid.Attach(_pelletDiameterSpin, 1, 1, 1, 1);

            _cladDiameterSpin = new SpinButton(6, 18, 0.1) { Value = 9.5, Digits = 1 };
            rodGrid.Attach(new Label("Clad Outer Diameter (mm):") { Halign = Align.End }, 0, 2, 1, 1);
            rodGrid.Attach(_cladDiameterSpin, 1, 2, 1, 1);

            _cladMaterialCombo = new ComboBoxText();
            _cladMaterialCombo.AppendText("Zircaloy-4");
            _cladMaterialCombo.AppendText("Zircaloy-2");
            _cladMaterialCombo.AppendText("M5 Alloy");
            _cladMaterialCombo.AppendText("ZIRLO");
            _cladMaterialCombo.AppendText("Stainless Steel");
            _cladMaterialCombo.Active = 0;
            rodGrid.Attach(new Label("Cladding Material:") { Halign = Align.End }, 0, 3, 1, 1);
            rodGrid.Attach(_cladMaterialCombo, 1, 3, 1, 1);

            _fuelMassLabel = new Label("Total UO2 Mass: ~100 tonnes") { Halign = Align.Start };
            rodGrid.Attach(_fuelMassLabel, 2, 0, 1, 4);

            _rodsPerAssemblySpin.ValueChanged += (s, e) => UpdateFuelMass();
            _pelletDiameterSpin.ValueChanged += (s, e) => UpdateFuelMass();
            _numAssembliesSpin.ValueChanged += (s, e) => UpdateFuelMass();

            rodFrame.Add(rodGrid);
            vbox.PackStart(rodFrame, false, false, 0);

            // Fuel Material Properties
            var matFrame = new Frame("Fuel Material (UO2)");
            var matBox = new VBox(false, 5) { BorderWidth = 10 };
            matBox.PackStart(new Label("• Theoretical Density: 10.97 g/cm³") { Halign = Align.Start }, false, false, 0);
            matBox.PackStart(new Label("• Typical Density: 95% TD (10.42 g/cm³)") { Halign = Align.Start }, false, false, 0);
            matBox.PackStart(new Label("• Melting Point: 2865°C") { Halign = Align.Start }, false, false, 0);
            matBox.PackStart(new Label("• Thermal Conductivity: 2-4 W/m·K") { Halign = Align.Start }, false, false, 0);
            matFrame.Add(matBox);
            vbox.PackStart(matFrame, false, false, 0);

            return vbox;
        }

        private Widget CreateControlTab()
        {
            var vbox = new VBox(false, 10) { BorderWidth = 15 };

            // Control Rods Frame
            var rodFrame = new Frame("Control Rod System");
            var rodGrid = new Grid { RowSpacing = 8, ColumnSpacing = 10, MarginStart = 10, MarginEnd = 10, MarginTop = 10, MarginBottom = 10 };

            _numControlBanksSpin = new SpinButton(1, 10, 1) { Value = 4 };
            rodGrid.Attach(new Label("Number of Control Banks:") { Halign = Align.End }, 0, 0, 1, 1);
            rodGrid.Attach(_numControlBanksSpin, 1, 0, 1, 1);

            _controlRodWorthSpin = new SpinButton(100, 2000, 50) { Value = 500 };
            rodGrid.Attach(new Label("Average Rod Worth (pcm):") { Halign = Align.End }, 0, 1, 1, 1);
            rodGrid.Attach(_controlRodWorthSpin, 1, 1, 1, 1);

            var absorberCombo = new ComboBoxText();
            absorberCombo.AppendText("Ag-In-Cd (Silver-Indium-Cadmium)");
            absorberCombo.AppendText("B4C (Boron Carbide)");
            absorberCombo.AppendText("Hafnium");
            absorberCombo.Active = 0;
            rodGrid.Attach(new Label("Absorber Material:") { Halign = Align.End }, 0, 2, 1, 1);
            rodGrid.Attach(absorberCombo, 1, 2, 1, 1);

            var rodInfo = new Label(
                "<b>Control Rod Banks:</b>\n" +
                "• Bank A-D: Normal power control\n" +
                "• Shutdown Banks: Emergency insertion\n" +
                "• Part-length Rods: Axial power shaping")
            { UseMarkup = true, Wrap = true, Halign = Align.Start };
            rodGrid.Attach(rodInfo, 0, 3, 2, 1);

            rodFrame.Add(rodGrid);
            vbox.PackStart(rodFrame, false, false, 0);

            // Chemical Shim Frame
            var boronFrame = new Frame("Chemical Shim (Soluble Boron)");
            var boronGrid = new Grid { RowSpacing = 8, ColumnSpacing = 10, MarginStart = 10, MarginEnd = 10, MarginTop = 10, MarginBottom = 10 };

            _boronConcSpin = new SpinButton(0, 2500, 50) { Value = 1000 };
            boronGrid.Attach(new Label("Boron Concentration (ppm):") { Halign = Align.End }, 0, 0, 1, 1);
            boronGrid.Attach(_boronConcSpin, 1, 0, 1, 1);

            var boronWorthLabel = new Label("Reactivity Worth: ~-10,000 pcm") { Halign = Align.Start };
            _boronConcSpin.ValueChanged += (s, e) =>
                boronWorthLabel.Text = $"Reactivity Worth: ~{-10 * _boronConcSpin.Value:F0} pcm";
            boronGrid.Attach(boronWorthLabel, 2, 0, 1, 1);

            var boronInfo = new Label(
                "• BOL (Beginning of Life): ~1500 ppm\n" +
                "• EOL (End of Life): ~10 ppm\n" +
                "• Boron worth: ~-10 pcm/ppm\n" +
                "• Note: CANDU reactors don't use soluble boron")
            { Halign = Align.Start };
            boronGrid.Attach(boronInfo, 0, 1, 3, 1);

            boronFrame.Add(boronGrid);
            vbox.PackStart(boronFrame, false, false, 0);

            return vbox;
        }

        private Widget CreateSafetyTab()
        {
            var vbox = new VBox(false, 10) { BorderWidth = 15 };

            // SCRAM Setpoints Frame
            var scramFrame = new Frame("SCRAM (Emergency Shutdown) Setpoints");
            var scramGrid = new Grid { RowSpacing = 8, ColumnSpacing = 10, MarginStart = 10, MarginEnd = 10, MarginTop = 10, MarginBottom = 10 };

            _scramPowerSpin = new SpinButton(100, 130, 1) { Value = 118 };
            scramGrid.Attach(new Label("High Power Trip (%):") { Halign = Align.End }, 0, 0, 1, 1);
            scramGrid.Attach(_scramPowerSpin, 1, 0, 1, 1);

            _scramPeriodSpin = new SpinButton(1, 30, 1) { Value = 10 };
            scramGrid.Attach(new Label("Short Period Trip (s):") { Halign = Align.End }, 0, 1, 1, 1);
            scramGrid.Attach(_scramPeriodSpin, 1, 1, 1, 1);

            _scramTempSpin = new SpinButton(300, 400, 5) { Value = 343 };
            scramGrid.Attach(new Label("High Temp Trip (°C):") { Halign = Align.End }, 0, 2, 1, 1);
            scramGrid.Attach(_scramTempSpin, 1, 2, 1, 1);

            scramFrame.Add(scramGrid);
            vbox.PackStart(scramFrame, false, false, 0);

            // ECCS Frame
            var eccsFrame = new Frame("Emergency Core Cooling System");
            var eccsBox = new VBox(false, 5) { BorderWidth = 10 };

            _hasEccsCheck = new CheckButton("Enable ECCS") { Active = true };
            eccsBox.PackStart(_hasEccsCheck, false, false, 0);
            eccsBox.PackStart(new Label("• High Pressure Injection") { Halign = Align.Start }, false, false, 0);
            eccsBox.PackStart(new Label("• Accumulators (passive)") { Halign = Align.Start }, false, false, 0);
            eccsBox.PackStart(new Label("• Low Pressure Injection") { Halign = Align.Start }, false, false, 0);
            eccsBox.PackStart(new Label("• Residual Heat Removal") { Halign = Align.Start }, false, false, 0);

            eccsFrame.Add(eccsBox);
            vbox.PackStart(eccsFrame, false, false, 0);

            // Summary Frame
            var summaryFrame = new Frame("Configuration Summary");
            _summaryLabel = new Label { UseMarkup = true, Halign = Align.Start, Valign = Align.Start };
            _summaryLabel.SetPadding(10, 10);
            summaryFrame.Add(_summaryLabel);
            vbox.PackStart(summaryFrame, true, true, 0);

            return vbox;
        }

        private Widget CreateLabeledWidget(string labelText, Widget widget)
        {
            var hbox = new HBox(false, 10);
            hbox.PackStart(new Label(labelText) { WidthRequest = 150, Halign = Align.End }, false, false, 0);
            hbox.PackStart(widget, true, true, 0);
            return hbox;
        }

        private void OnReactorTypeChanged(object? sender, EventArgs e)
        {
            switch (_reactorTypeCombo.Active)
            {
                case 0: LoadDefaultPWR(); break;
                case 1: LoadDefaultCANDU(); break;
                case 2: LoadDefaultBWR(); break;
            }
        }

        private void OnModeratorTypeChanged(object? sender, EventArgs e)
        {
            bool isHeavyWater = _moderatorTypeCombo.Active == 1;
            _d2oPuritySpin.Sensitive = isHeavyWater;

            // Update moderation ratio display
            var modParams = new ModeratorParameters
            {
                Type = (ModeratorType)_moderatorTypeCombo.Active
            };
            _moderationRatioLabel.Text = $"Moderation Ratio: {modParams.ModerationRatio:F0}\n" +
                                         $"Collisions to thermalize: {modParams.CollisionsToThermalize}";
        }

        private void UpdateHeatRemoval()
        {
            double flow = _coolantFlowRateSpin.Value;
            double dT = _coolantOutletTempSpin.Value - _coolantInletTempSpin.Value;
            double cp = 5500; // J/kg·K for water
            double Q = flow * cp * dT / 1e6; // MW
            _heatRemovalLabel.Text = $"Heat Removal: {Q:F0} MW";
        }

        private void UpdateFuelMass()
        {
            int numAssemblies = (int)_numAssembliesSpin.Value;
            int rodsPerAssembly = (int)_rodsPerAssemblySpin.Value;
            double pelletDia = _pelletDiameterSpin.Value / 1000; // m
            double activeLength = _coreHeightSpin.Value;
            double volume = Math.PI * Math.Pow(pelletDia / 2, 2) * activeLength * numAssemblies * rodsPerAssembly;
            double mass = volume * 10420; // 95% TD UO2
            _fuelMassLabel.Text = $"Total UO2 Mass: {mass / 1000:F1} tonnes\n" +
                                  $"Total U: {mass * 0.88 / 1000:F1} tonnes";
        }

        private void LoadDefaultPWR()
        {
            _thermalPowerSpin.Value = 3411;
            _electricalPowerSpin.Value = 1150;
            _coreHeightSpin.Value = 3.66;
            _coreDiameterSpin.Value = 3.37;
            _numAssembliesSpin.Value = 193;
            _assemblyPitchSpin.Value = 0.214;

            _moderatorTypeCombo.Active = 0; // Light water
            _moderatorDensitySpin.Value = 700;
            _moderatorTempSpin.Value = 300;

            _coolantTypeCombo.Active = 0; // Light water
            _coolantInletTempSpin.Value = 292;
            _coolantOutletTempSpin.Value = 326;
            _coolantPressureSpin.Value = 15.5;
            _coolantFlowRateSpin.Value = 17400;

            _enrichmentSpin.Value = 3.5;
            _rodsPerAssemblySpin.Value = 264;
            _boronConcSpin.Value = 1000;

            UpdateSummary();
        }

        private void LoadDefaultCANDU()
        {
            _thermalPowerSpin.Value = 2064;
            _electricalPowerSpin.Value = 700;
            _coreHeightSpin.Value = 5.94;
            _coreDiameterSpin.Value = 7.6;
            _numAssembliesSpin.Value = 380;
            _assemblyPitchSpin.Value = 0.286;

            _moderatorTypeCombo.Active = 1; // Heavy water
            _moderatorDensitySpin.Value = 1085;
            _moderatorTempSpin.Value = 70;
            _d2oPuritySpin.Value = 99.75;

            _coolantTypeCombo.Active = 1; // Heavy water
            _coolantInletTempSpin.Value = 266;
            _coolantOutletTempSpin.Value = 310;
            _coolantPressureSpin.Value = 10.0;
            _coolantFlowRateSpin.Value = 7600;

            _enrichmentSpin.Value = 0.71; // Natural uranium!
            _rodsPerAssemblySpin.Value = 37;
            _boronConcSpin.Value = 0; // No soluble boron

            UpdateSummary();
        }

        private void LoadDefaultBWR()
        {
            _thermalPowerSpin.Value = 3293;
            _electricalPowerSpin.Value = 1100;
            _coreHeightSpin.Value = 3.71;
            _coreDiameterSpin.Value = 4.75;
            _numAssembliesSpin.Value = 764;
            _assemblyPitchSpin.Value = 0.155;

            _moderatorTypeCombo.Active = 0;
            _coolantTypeCombo.Active = 0;
            _coolantPressureSpin.Value = 7.2; // Lower pressure, boiling
            _enrichmentSpin.Value = 3.2;

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            string reactorType = _reactorTypeCombo.ActiveText ?? "PWR";
            double efficiency = _electricalPowerSpin.Value / _thermalPowerSpin.Value * 100;
            string moderator = _moderatorTypeCombo.ActiveText ?? "Light Water";

            _summaryLabel.Markup =
                $"<b>Reactor Configuration Summary</b>\n\n" +
                $"Type: {reactorType}\n" +
                $"Thermal Power: {_thermalPowerSpin.Value:F0} MWth\n" +
                $"Electrical Power: {_electricalPowerSpin.Value:F0} MWe\n" +
                $"Efficiency: {efficiency:F1}%\n\n" +
                $"Moderator: {moderator}\n" +
                $"Enrichment: {_enrichmentSpin.Value:F2}% U-235\n" +
                $"Assemblies: {_numAssembliesSpin.Value:F0}\n" +
                $"Coolant Pressure: {_coolantPressureSpin.Value:F1} MPa\n\n" +
                $"<i>Ready for simulation</i>";
        }

        protected override void OnResponse(ResponseType response_id)
        {
            if (response_id == ResponseType.Ok)
            {
                BuildConfiguration();
            }
            base.OnResponse(response_id);
        }

        private void BuildConfiguration()
        {
            _config.ReactorType = (NuclearReactorType)_reactorTypeCombo.Active;
            _config.ThermalPowerMW = _thermalPowerSpin.Value;
            _config.ElectricalPowerMW = _electricalPowerSpin.Value;
            _config.CoreHeight = _coreHeightSpin.Value;
            _config.CoreDiameter = _coreDiameterSpin.Value;
            _config.NumberOfAssemblies = (int)_numAssembliesSpin.Value;
            _config.AssemblyPitch = _assemblyPitchSpin.Value;

            _config.Moderator = new ModeratorParameters
            {
                Type = (ModeratorType)_moderatorTypeCombo.Active,
                Density = _moderatorDensitySpin.Value,
                Temperature = _moderatorTempSpin.Value,
                D2OPurity = _d2oPuritySpin.Value
            };

            _config.Coolant = new NuclearCoolantParameters
            {
                Type = (NuclearCoolantType)_coolantTypeCombo.Active,
                InletTemperature = _coolantInletTempSpin.Value,
                OutletTemperature = _coolantOutletTempSpin.Value,
                Pressure = _coolantPressureSpin.Value,
                MassFlowRate = _coolantFlowRateSpin.Value
            };

            _config.BoronConcentrationPPM = _boronConcSpin.Value;

            _config.Safety = new NuclearSafetyParameters
            {
                ScramPowerPercent = _scramPowerSpin.Value,
                ScramPeriodSeconds = _scramPeriodSpin.Value,
                ScramTempCelsius = _scramTempSpin.Value,
                HasECCS = _hasEccsCheck.Active
            };

            // Initialize fuel assemblies based on configuration
            if (_config.ReactorType == NuclearReactorType.PHWR)
                _config.InitializeCANDU();
            else
                _config.InitializePWR();

            CreatedConfiguration = _config;
        }
    }
}
