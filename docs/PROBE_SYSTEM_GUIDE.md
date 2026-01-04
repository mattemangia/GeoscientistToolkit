# Simulation Probe System Guide

This guide explains how to use the probe system for measuring and visualizing simulation variables in GeoscientistToolkit.

## Overview

The probe system provides comprehensive measurement and visualization capabilities for PhysicoChem simulations:

1. **Point Probes** - Track variable evolution at a single location over time
2. **Line Probes** - Track averaged variable along a line segment
3. **Plane Probes** - 2D cross-section visualization with colormaps
4. **Time Charts** - Multi-variable time-series plots
5. **PNG Export** - Export charts and colormaps to image files

---

## 1. Probe Types

### Point Probe
Measures a variable at a single point in space over time. Ideal for monitoring specific locations (e.g., fuel centerline temperature, coolant outlet).

```csharp
var pointProbe = mesh.Probes.AddPointProbe(
    x: 0.5, y: 0.5, z: -2.0,
    name: "Core Center Temperature"
);
pointProbe.Variable = ProbeVariable.Temperature;
```

### Line Probe
Averages a variable along a line segment. Useful for cross-sectional profiles (e.g., radial temperature distribution).

```csharp
var lineProbe = mesh.Probes.AddLineProbe(
    x1: -1.5, y1: 0, z1: 0,
    x2: 1.5, y2: 0, z2: 0,
    name: "Radial Temperature Profile"
);
lineProbe.Variable = ProbeVariable.Temperature;
lineProbe.SamplePoints = 100;
```

### Plane Probe
Creates 2D cross-section views with colormap visualization. Supports XY, XZ, and YZ orientations.

```csharp
var planeProbe = mesh.Probes.AddPlaneProbe(
    cx: 0, cy: 0, cz: -1.5,
    width: 4, height: 4,
    orientation: ProbePlaneOrientation.XY,
    name: "Axial Temperature Distribution"
);
planeProbe.Variable = ProbeVariable.PowerDensity;
planeProbe.ResolutionX = 64;
planeProbe.ResolutionY = 64;
```

---

## 2. Available Variables

The `ProbeVariable` enum provides variables for different simulation modules:

### Thermal Variables
| Variable | Units | Description |
|----------|-------|-------------|
| `Temperature` | °C | Local temperature |
| `Pressure` | MPa | Local pressure |
| `HeatFlux` | W/m² | Heat transfer rate |
| `ThermalConductivity` | W/(m·K) | Material thermal conductivity |
| `HeatCapacity` | J/(kg·K) | Specific heat capacity |
| `Enthalpy` | kJ/kg | Specific enthalpy |

### Flow Variables
| Variable | Units | Description |
|----------|-------|-------------|
| `Velocity` | m/s | Fluid velocity |
| `MassFlowRate` | kg/s | Mass flow rate |
| `Density` | kg/m³ | Fluid density |
| `Viscosity` | Pa·s | Dynamic viscosity |

### Chemical Variables
| Variable | Units | Description |
|----------|-------|-------------|
| `Concentration` | mol/L | Species concentration |
| `pH` | - | Acidity/alkalinity |
| `Salinity` | ppt | Salt content |
| `DissolvedOxygen` | mg/L | Dissolved O₂ |

### Nuclear Variables
| Variable | Units | Description |
|----------|-------|-------------|
| `NeutronFlux` | n/(cm²·s) | Neutron flux density |
| `PowerDensity` | kW/L | Volumetric power |
| `FuelTemperature` | °C | Fuel pellet temperature |
| `CoolantTemperature` | °C | Coolant temperature |
| `CladTemperature` | °C | Cladding temperature |
| `XenonConcentration` | atoms/cm³ | Xe-135 concentration |
| `Reactivity` | pcm | Reactor reactivity |
| `DNBR` | - | Departure from nucleate boiling ratio |

### ORC Variables
| Variable | Units | Description |
|----------|-------|-------------|
| `ORCEfficiency` | % | Cycle efficiency |
| `TurbinePower` | kW | Turbine output power |
| `CondenserDuty` | kW | Condenser heat rejection |

---

## 3. ImGui Probe Visualizer

### Opening the Window
```csharp
var probeWindow = new ProbeVisualizerWindow();
probeWindow.SetProbeManager(mesh.Probes);
probeWindow.SetMeshBounds(meshMin, meshMax);
```

### Features

#### Probe List Panel
- Tree view organized by probe type
- Click to select probe
- Right-click context menu for rename/delete/add to chart
- Data point count display

#### Mesh View Tab
- Interactive 2D view of probe positions
- Click to select existing probes
- Drawing tools for creating new probes:
  - **Point Mode**: Click to place point probe
  - **Line Mode**: Drag to draw line probe
  - **Plane Mode**: Drag rectangle for plane probe

#### Time Charts Tab
- Add multiple probes to single chart
- Auto-scaling or manual Y-axis range
- Adjustable time range
- Legend with probe colors

#### 2D Cross-Section Tab
- Colormap visualization for plane probes
- Colormap selection (Jet, Viridis, Inferno, Grayscale)
- Snapshot navigation through time history
- Colorbar with min/max values

### PNG Export
```csharp
// Menu: File → Export Chart as PNG...
// Or programmatically through the window's export methods
```

---

## 4. GTK Probe Configuration Dialog

### Opening the Dialog
```csharp
var dialog = new ProbeConfigDialog(parentWindow, existingProbeManager);
dialog.Run();
var updatedProbes = dialog.ProbeManagerResult;
dialog.Destroy();
```

### Features

#### Probe List
- List view with type, name, and data count
- Add buttons for Point, Line, and Plane probes
- Delete selected probe

#### Probe Details Panel
- Name editing
- Active toggle
- Variable selection dropdown
- Color picker
- Type-specific position controls:
  - Point: X, Y, Z coordinates
  - Line: Start and end points
  - Plane: Center, width, height, orientation

#### Mesh View Tab
- Cairo-based 2D visualization
- Click to select probes
- Shift+click to add point probe at location

#### Time Charts Tab
- Add probes to chart via dropdown
- Adjustable time range slider
- Cairo-rendered time-series plot

#### 2D Cross-Section Tab
- Colormap selection
- Cairo-rendered colormap visualization

### PNG Export
- Export Chart: Menu → Export Chart as PNG
- Export Colormap: Menu → Export Colormap as PNG
- Uses Cairo ImageSurface for rendering

---

## 5. Recording Data During Simulation

### Point Probe Data Recording
```csharp
// During simulation loop
double time = simulationTime;
double value = GetTemperatureAt(probe.X, probe.Y, probe.Z);
probe.RecordValue(time, value);
```

### Line Probe Data Recording
```csharp
// Sample along line and average
var samples = new List<double>();
foreach (var pos in lineProbe.GetSamplePositions())
{
    samples.Add(GetTemperatureAt(pos.X, pos.Y, pos.Z));
}
double avg = samples.Average();
double min = samples.Min();
double max = samples.Max();
double stdDev = CalculateStdDev(samples);

lineProbe.RecordValue(time, avg, min, max, stdDev);
```

### Plane Probe Data Recording
```csharp
// Sample 2D field
var field = new double[planeProbe.ResolutionX, planeProbe.ResolutionY];
foreach (var (x, y, z, i, j) in planeProbe.GetSamplePositions())
{
    field[i, j] = GetTemperatureAt(x, y, z);
}
planeProbe.RecordField(time, field);
```

---

## 6. Data Export

### CSV Export
```csharp
string csv = mesh.Probes.ExportToCSV(probe);
File.WriteAllText("probe_data.csv", csv);
```

CSV format:
```
# Probe: Core Center Temperature
# Type: Point at (0.50, 0.50, -2.00)
# Variable: Temperature (°C)

Time,Value,Min,Max,StdDev
0.00,300.00,,,
0.10,301.25,,,
0.20,302.50,,,
...
```

### PNG Export (ImGui)
Uses StbImageWriteSharp for cross-platform PNG generation:
```csharp
// Through menu: File → Export Chart as PNG...
// Or programmatically using the window's export functionality
```

### PNG Export (GTK)
Uses Cairo ImageSurface:
```csharp
// Through button: Export Chart as PNG / Export Colormap as PNG
// Renders to Cairo surface and saves with WriteToPng()
```

---

## 7. Integration with Nuclear Reactor Simulation

The probe system integrates seamlessly with the nuclear reactor module:

```csharp
// Create reactor and mesh
var nuclearParams = new NuclearReactorParameters();
nuclearParams.InitializePWR();

var mesh = new PhysicoChemMesh();
mesh.EmbedNuclearReactor(nuclearParams, center: (0, 0, -5));

// Add probes for nuclear monitoring
var fuelTempProbe = mesh.Probes.AddPointProbe(0, 0, -5, "Peak Fuel Temperature");
fuelTempProbe.Variable = ProbeVariable.FuelTemperature;

var powerProfile = mesh.Probes.AddLineProbe(
    0, 0, -7, 0, 0, -3,
    "Axial Power Profile"
);
powerProfile.Variable = ProbeVariable.PowerDensity;

var coreSection = mesh.Probes.AddPlaneProbe(
    0, 0, -5, 4, 4,
    ProbePlaneOrientation.XY,
    "Core XY Cross-Section"
);
coreSection.Variable = ProbeVariable.NeutronFlux;

// Run simulation and record data
var solver = new NuclearReactorSolver(nuclearParams);
for (double t = 0; t < 100; t += 0.1)
{
    solver.SolvePointKinetics(0.1);
    var state = solver.GetState();

    fuelTempProbe.RecordValue(t, state.PeakFuelTemp);
    // Record other probe data...
}
```

---

## 8. Integration with ORC Simulation

The probe system also works with ORC geothermal simulations:

```csharp
// Add probes for ORC monitoring
var evapTempProbe = mesh.Probes.AddPointProbe(
    evaporator.Center.X, evaporator.Center.Y, evaporator.Center.Z,
    "Evaporator Temperature"
);
evapTempProbe.Variable = ProbeVariable.Temperature;

var turbineProbe = mesh.Probes.AddPointProbe(
    turbine.Center.X, turbine.Center.Y, turbine.Center.Z,
    "Turbine Power Output"
);
turbineProbe.Variable = ProbeVariable.TurbinePower;

var efficiencyProbe = mesh.Probes.AddPointProbe(0, 0, 0, "Cycle Efficiency");
efficiencyProbe.Variable = ProbeVariable.ORCEfficiency;
```

---

## 9. Best Practices

1. **Name Probes Descriptively**: Use names that clearly indicate what is being measured
2. **Choose Appropriate Resolution**: For plane probes, balance resolution with memory usage
3. **Limit History**: Set `MaxHistoryPoints` to prevent memory issues in long simulations
4. **Export Regularly**: Export data during long simulations to avoid data loss
5. **Use Line Probes for Profiles**: Average values along lines for cleaner trends
6. **Use Plane Probes for Spatial Distribution**: Visualize 2D patterns in the domain
