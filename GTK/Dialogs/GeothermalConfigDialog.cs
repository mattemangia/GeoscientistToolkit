using System;
using GeoscientistToolkit.Data.PhysicoChem;
using Gtk;

namespace GeoscientistToolkit.GtkUI.Dialogs;

/// <summary>
/// Geothermal Deep Wells Configuration Dialog
/// Professional interface for configuring geothermal simulations
/// Supports deep oil/gas wells repurposing (km-scale depths)
/// </summary>
public class GeothermalConfigDialog : Dialog
{
    private readonly Entry _nameEntry;
    private readonly ComboBoxText _wellTypeSelector;
    private readonly SpinButton _depthInput;
    private readonly SpinButton _diameterInput;
    private readonly SpinButton _flowRateInput;
    private readonly SpinButton _surfaceTempInput;
    private readonly SpinButton _geothermalGradientInput;
    private readonly SpinButton _thermalConductivityInput;
    private readonly SpinButton _simulationTimeInput;
    private readonly SpinButton _timeStepInput;
    private readonly CheckButton _multiphaseCheckbox;
    private readonly CheckButton _reactiveTransportCheckbox;

    // Deep wells specific
    private readonly SpinButton _numberOfZonesInput;
    private readonly ComboBoxText _heatExchangerTypeSelector;
    private readonly Entry _rockTypeEntry;

    public PhysicoChemDataset? CreatedDataset { get; private set; }

    public GeothermalConfigDialog(Window parent) : base("Geothermal Deep Wells Configuration", parent, DialogFlags.Modal)
    {
        SetDefaultSize(600, 750);
        BorderWidth = 8;

        _nameEntry = new Entry { PlaceholderText = "e.g., DeepWell_Geothermal_1" };
        _wellTypeSelector = new ComboBoxText();
        _depthInput = new SpinButton(100, 10000, 10) { Value = 2000 }; // Up to 10 km
        _diameterInput = new SpinButton(0.05, 1.0, 0.01) { Value = 0.2 }; // 20 cm default
        _flowRateInput = new SpinButton(0.001, 100, 0.1) { Value = 10 };
        _surfaceTempInput = new SpinButton(0, 50, 1) { Value = 15 };
        _geothermalGradientInput = new SpinButton(10, 100, 1) { Value = 30 }; // °C/km
        _thermalConductivityInput = new SpinButton(0.5, 10, 0.1) { Value = 2.5 };
        _simulationTimeInput = new SpinButton(1, 365 * 100, 1) { Value = 365 }; // days
        _timeStepInput = new SpinButton(0.001, 1, 0.01) { Value = 0.1 }; // days
        _multiphaseCheckbox = new CheckButton("Enable multiphase flow (water-steam)") { Active = false };
        _reactiveTransportCheckbox = new CheckButton("Enable reactive transport") { Active = false };
        _numberOfZonesInput = new SpinButton(1, 100, 1) { Value = 10 };
        _heatExchangerTypeSelector = new ComboBoxText();
        _rockTypeEntry = new Entry { PlaceholderText = "e.g., Granite, Sandstone" };

        BuildUI();

        AddButton("Cancel", ResponseType.Cancel);
        AddButton("Create Simulation", ResponseType.Ok);

        Response += OnResponse;

        ShowAll();
    }

    private void BuildUI()
    {
        var contentBox = new VBox(false, 8);

        // Configuration name
        var nameBox = new HBox(false, 6);
        nameBox.PackStart(new Label("Simulation Name:"), false, false, 0);
        nameBox.PackStart(_nameEntry, true, true, 0);
        contentBox.PackStart(nameBox, false, false, 0);

        // Well configuration frame
        var wellFrame = new Frame("Well Configuration");
        var wellGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6, BorderWidth = 6 };

        int row = 0;

        wellGrid.Attach(new Label("Well Type:") { Xalign = 0 }, 0, row, 1, 1);
        _wellTypeSelector.AppendText("Vertical Well");
        _wellTypeSelector.AppendText("Directional Well");
        _wellTypeSelector.AppendText("Horizontal Well");
        _wellTypeSelector.AppendText("Multi-lateral Well");
        _wellTypeSelector.Active = 0;
        wellGrid.Attach(_wellTypeSelector, 1, row++, 1, 1);

        wellGrid.Attach(new Label("Depth (m):") { Xalign = 0 }, 0, row, 1, 1);
        wellGrid.Attach(_depthInput, 1, row++, 1, 1);

        wellGrid.Attach(new Label("Diameter (m):") { Xalign = 0 }, 0, row, 1, 1);
        wellGrid.Attach(_diameterInput, 1, row++, 1, 1);

        wellGrid.Attach(new Label("Heat Exchanger:") { Xalign = 0 }, 0, row, 1, 1);
        _heatExchangerTypeSelector.AppendText("Coaxial (default)");
        _heatExchangerTypeSelector.AppendText("U-Tube");
        _heatExchangerTypeSelector.AppendText("Open Loop");
        _heatExchangerTypeSelector.AppendText("Closed Loop");
        _heatExchangerTypeSelector.Active = 0;
        wellGrid.Attach(_heatExchangerTypeSelector, 1, row++, 1, 1);

        wellGrid.Attach(new Label("Flow Rate (L/s):") { Xalign = 0 }, 0, row, 1, 1);
        wellGrid.Attach(_flowRateInput, 1, row++, 1, 1);

        wellFrame.Add(wellGrid);
        contentBox.PackStart(wellFrame, false, false, 0);

        // Thermal configuration frame
        var thermalFrame = new Frame("Thermal Configuration");
        var thermalGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6, BorderWidth = 6 };

        row = 0;

        thermalGrid.Attach(new Label("Surface Temperature (°C):") { Xalign = 0 }, 0, row, 1, 1);
        thermalGrid.Attach(_surfaceTempInput, 1, row++, 1, 1);

        thermalGrid.Attach(new Label("Geothermal Gradient (°C/km):") { Xalign = 0 }, 0, row, 1, 1);
        thermalGrid.Attach(_geothermalGradientInput, 1, row++, 1, 1);

        thermalGrid.Attach(new Label("Rock Thermal Conductivity (W/m·K):") { Xalign = 0 }, 0, row, 1, 1);
        thermalGrid.Attach(_thermalConductivityInput, 1, row++, 1, 1);

        thermalGrid.Attach(new Label("Rock Type:") { Xalign = 0 }, 0, row, 1, 1);
        thermalGrid.Attach(_rockTypeEntry, 1, row++, 1, 1);

        thermalFrame.Add(thermalGrid);
        contentBox.PackStart(thermalFrame, false, false, 0);

        // Mesh configuration frame
        var meshFrame = new Frame("Mesh Configuration");
        var meshGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6, BorderWidth = 6 };

        meshGrid.Attach(new Label("Number of Vertical Zones:") { Xalign = 0 }, 0, 0, 1, 1);
        meshGrid.Attach(_numberOfZonesInput, 1, 0, 1, 1);

        var infoLabel = new Label("(Higher values = better resolution, slower simulation)")
        {
            Xalign = 0,
            Wrap = true
        };
        meshGrid.Attach(infoLabel, 0, 1, 2, 1);

        meshFrame.Add(meshGrid);
        contentBox.PackStart(meshFrame, false, false, 0);

        // Simulation options frame
        var simFrame = new Frame("Simulation Parameters");
        var simGrid = new Grid { ColumnSpacing = 8, RowSpacing = 6, BorderWidth = 6 };

        row = 0;

        simGrid.Attach(new Label("Simulation Time (days):") { Xalign = 0 }, 0, row, 1, 1);
        simGrid.Attach(_simulationTimeInput, 1, row++, 1, 1);

        simGrid.Attach(new Label("Time Step (days):") { Xalign = 0 }, 0, row, 1, 1);
        simGrid.Attach(_timeStepInput, 1, row++, 1, 1);

        simGrid.Attach(_multiphaseCheckbox, 0, row++, 2, 1);
        simGrid.Attach(_reactiveTransportCheckbox, 0, row++, 2, 1);

        simFrame.Add(simGrid);
        contentBox.PackStart(simFrame, false, false, 0);

        // Info panel
        var infoFrame = new Frame("Configuration Summary");
        var infoText = new Label
        {
            Xalign = 0,
            Wrap = true,
            Markup = "<b>This configuration will create a deep geothermal well suitable for:\n</b>" +
                     "• Repurposing deep oil/gas wells for geothermal energy\n" +
                     "• Enhanced Geothermal Systems (EGS)\n" +
                     "• Deep aquifer thermal energy storage\n" +
                     "• Supercritical geothermal resources (>374°C, >3.7 km)\n\n" +
                     "<b>Simulation will include:</b>\n" +
                     "• Heat transfer (conduction + convection)\n" +
                     "• Groundwater flow in surrounding rock\n" +
                     "• Temperature-dependent fluid properties\n" +
                     "• Optional: Multiphase flow, reactive transport"
        };
        infoFrame.Add(infoText);
        contentBox.PackStart(infoFrame, false, false, 0);

        VBox.PackStart(contentBox, true, true, 0);
    }

    private void OnResponse(object? sender, ResponseArgs args)
    {
        if (args.ResponseId == ResponseType.Ok)
        {
            CreatedDataset = CreateGeothermalDataset();
        }
    }

    private PhysicoChemDataset CreateGeothermalDataset()
    {
        var name = _nameEntry.Text;
        if (string.IsNullOrWhiteSpace(name))
            name = $"GeothermalWell_{DateTime.Now:yyyyMMdd_HHmmss}";

        var dataset = new PhysicoChemDataset(name, "Deep geothermal well simulation");

        // Calculate temperature at bottom hole
        double depth = _depthInput.Value;
        double surfaceTemp = _surfaceTempInput.Value;
        double gradient = _geothermalGradientInput.Value / 1000.0; // Convert to °C/m
        double bottomHoleTemp = surfaceTemp + (gradient * depth);

        // Create rock material
        string rockType = string.IsNullOrWhiteSpace(_rockTypeEntry.Text) ? "Granite" : _rockTypeEntry.Text;
        var rockMaterial = new MaterialProperties
        {
            MaterialID = $"{rockType}_Rock",
            Density = 2650.0,
            ThermalConductivity = _thermalConductivityInput.Value,
            SpecificHeat = 840.0,
            Porosity = 0.05, // Low porosity for deep rock
            Permeability = 1e-18, // Very low permeability
            Color = new System.Numerics.Vector4(0.6f, 0.5f, 0.4f, 1.0f)
        };
        dataset.Materials.Add(rockMaterial);

        // Create fluid material
        var fluidMaterial = new MaterialProperties
        {
            MaterialID = "GeothermalFluid",
            Density = 1000.0,
            ThermalConductivity = 0.6,
            SpecificHeat = 4186.0,
            Porosity = 1.0,
            Color = new System.Numerics.Vector4(0.3f, 0.6f, 0.9f, 1.0f)
        };
        dataset.Materials.Add(fluidMaterial);

        // Create mesh with vertical zones
        int nZones = (int)_numberOfZonesInput.Value;
        double zoneHeight = depth / nZones;
        double wellRadius = _diameterInput.Value / 2.0;
        double outerRadius = wellRadius * 20; // 20x well radius for surrounding rock

        for (int i = 0; i < nZones; i++)
        {
            double zCenter = (i + 0.5) * zoneHeight;
            double zoneTemp = surfaceTemp + (gradient * zCenter);

            // Wellbore cell
            var wellCell = new Cell
            {
                ID = $"Well_Zone_{i}",
                MaterialID = "GeothermalFluid",
                Center = (0, 0, -zCenter), // Negative Z goes down
                Volume = Math.PI * wellRadius * wellRadius * zoneHeight,
                IsActive = true,
                InitialConditions = new InitialConditions
                {
                    Temperature = zoneTemp + 273.15, // Convert to Kelvin
                    Pressure = 101325.0 + (1000 * 9.81 * zCenter), // Hydrostatic
                    LiquidSaturation = 1.0,
                    Concentrations = new System.Collections.Generic.Dictionary<string, double>()
                }
            };
            dataset.Mesh.Cells[wellCell.ID] = wellCell;

            // Surrounding rock cell
            var rockCell = new Cell
            {
                ID = $"Rock_Zone_{i}",
                MaterialID = $"{rockType}_Rock",
                Center = (outerRadius, 0, -zCenter),
                Volume = Math.PI * (outerRadius * outerRadius - wellRadius * wellRadius) * zoneHeight,
                IsActive = true,
                InitialConditions = new InitialConditions
                {
                    Temperature = zoneTemp + 273.15,
                    Pressure = 101325.0 + (1000 * 9.81 * zCenter),
                    LiquidSaturation = 0.05, // Low saturation in rock
                    Concentrations = new System.Collections.Generic.Dictionary<string, double>()
                }
            };
            dataset.Mesh.Cells[rockCell.ID] = rockCell;

            // Connect wellbore to rock (heat transfer)
            dataset.Mesh.Connections.Add((wellCell.ID, rockCell.ID));

            // Connect to adjacent wellbore cells
            if (i > 0)
            {
                dataset.Mesh.Connections.Add((wellCell.ID, $"Well_Zone_{i - 1}"));
            }
        }

        // Add inlet boundary condition (injection at surface)
        dataset.BoundaryConditions.Add(new BoundaryCondition
        {
            Name = "Surface_Injection",
            Type = BoundaryType.Inlet,
            Location = BoundaryLocation.ZMax,
            Variable = BoundaryVariable.MassFlux,
            FluxValue = _flowRateInput.Value / 1000.0, // L/s to kg/s
            InletTemperature = surfaceTemp + 273.15,
            InletFlowRate = _flowRateInput.Value / 1000.0,
            IsActive = true
        });

        // Add outlet boundary condition (production at bottom)
        dataset.BoundaryConditions.Add(new BoundaryCondition
        {
            Name = "Bottom_Production",
            Type = BoundaryType.Outlet,
            Location = BoundaryLocation.ZMin,
            Variable = BoundaryVariable.Pressure,
            Value = 101325.0 + (1000 * 9.81 * depth),
            IsActive = true
        });

        // Add gravity
        dataset.Forces.Add(new ForceField
        {
            Name = "Gravity",
            Type = ForceType.Gravity,
            GravityVector = (0, 0, -9.81),
            IsActive = true
        });

        // Set simulation parameters
        dataset.SimulationParams.TotalTime = _simulationTimeInput.Value * 86400; // days to seconds
        dataset.SimulationParams.TimeStep = _timeStepInput.Value * 86400;
        dataset.SimulationParams.EnableFlow = true;
        dataset.SimulationParams.EnableHeatTransfer = true;
        dataset.SimulationParams.EnableChemistry = _reactiveTransportCheckbox.Active;
        dataset.SimulationParams.EnableMultiphase = _multiphaseCheckbox.Active;

        return dataset;
    }
}
