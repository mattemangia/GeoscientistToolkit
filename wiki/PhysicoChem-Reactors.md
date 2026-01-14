# PhysicoChem Reactors

Guide for multiphysics reactor simulation in Geoscientist's Toolkit.

---

## Overview

The PhysicoChem module provides:
- Multi-reactor type support (box, sphere, cylinder, cone)
- 2D-to-3D geometry generation
- Boundary condition configuration
- Force field simulation
- Nucleation and crystal growth
- Reactive transport coupling
- Parameter sweeps and sensitivity analysis

---

## PhysicoChem Dataset Anatomy

PhysicoChem simulations are driven by the **PhysicoChem dataset** (stored as a Group dataset internally). The dataset combines geometry, materials, boundary conditions, and simulation settings.

### Core Dataset Components

- **Mesh**: A grid or Voronoi mesh with cells and connections.
- **Domains**: Named reactor regions with geometry, materials, and initial conditions.
- **Materials**: Porosity, permeability, density, thermal properties, and mineral composition.
- **Boundary Conditions**: Fixed value/flux/convective constraints (temperature, pressure, species).
- **Forces**: Gravity, vortices, or custom forces applied per cell.
- **Nucleation Sites**: Explicit nucleation points for crystal growth.
- **Simulation Parameters**: Time step, convergence tolerance, heat/flow/reactive transport toggles.
- **Tracking & Parameter Sweep**: Simulation trackers and sweep configuration.

### Simulation Parameters (Highlights)

| Parameter | Purpose |
|-----------|---------|
| TotalTime / TimeStep | Time-based simulation control |
| ConvergenceTolerance | Solver convergence criterion |
| EnableReactiveTransport | Species transport & reactions |
| EnableHeatTransfer | Heat equation solving |
| EnableFlow | Fluid flow coupling |
| EnableNucleation | Nucleation and growth physics |
| UseGPU | GPU acceleration (when available) |
| SolverType | Sequential vs fully-coupled solvers |

---

## Multiphase Flow Integration

PhysicoChem can enable multiphase flow (water/steam/NCG) with EOS and relative permeability models. Multiphase parameters live under `SimulationParameters` and are typically set by GeoScript commands:

- `ENABLE_MULTIPHASE` for EOS selection
- `SET_MULTIPHASE_PARAMS` for residual saturations and van Genuchten parameters
- `SET_KR_MODEL` for relative permeability models
- `SET_PC_MODEL` for capillary pressure models
- `ADD_GAS_PHASE` for gas saturation and dissolved gas

See [Multiphase Flow](Multiphase-Flow.md) for full details.

---

## Getting Started

### Quick Start

1. Go to **Tools → Analysis → PhysicoChem**
2. Click **New Reactor** or use default exothermic reactor
3. Configure reactor geometry and parameters
4. Add reactants and define reactions
5. Run simulation
6. Visualize results (temperature, pressure, species)

### GUI Panels (ImGui + GTK)

- **ImGui Tools Panel**
  - Domain/cell management (material assignment, active/visible toggles)
  - Mesh editing & deformation
  - Boundary conditions and force fields
  - Nucleation sites, simulation parameters (including convergence tolerance), and sweeps
  - Export (CSV/JSON/VTK/TOUGH2)
- **GTK Toolkit**
  - Reactor Builder (grid + mesh import)
  - Cell Operations (select, activate, hide/show)
  - Material/force/boundary configuration dialogs
  - Simulation Setup Wizard (time stepping and convergence tolerance)

> Visibility toggles affect what you see; simulation activation uses the *Active* flag.

### Default Test Reactor

The toolkit includes a default exothermic reactor for testing:
1. Launch GTK
2. View default reactor in **PhysicoChem** panel
3. Click **Visualize 3D Mesh**
4. Inspect cells and configure reactions
5. Run simulation to see results

---

## Reactor Types

### Box Reactor

Rectangular cuboid geometry.

**Parameters:**
| Parameter | Description | Units |
|-----------|-------------|-------|
| Width | X dimension | m |
| Height | Y dimension | m |
| Depth | Z dimension | m |
| Resolution | Mesh cells per dimension | - |

### Sphere Reactor

Spherical geometry.

**Parameters:**
| Parameter | Description | Units |
|-----------|-------------|-------|
| Radius | Sphere radius | m |
| Center | Center point (x, y, z) | m |
| Resolution | Angular resolution | - |

### Cylinder Reactor

Cylindrical geometry.

**Parameters:**
| Parameter | Description | Units |
|-----------|-------------|-------|
| Radius | Cylinder radius | m |
| Height | Cylinder height | m |
| Axis | Orientation (X, Y, Z) | - |
| Resolution | Radial and axial cells | - |

### Cone Reactor

Conical geometry.

**Parameters:**
| Parameter | Description | Units |
|-----------|-------------|-------|
| Base Radius | Bottom radius | m |
| Top Radius | Top radius (0 for cone) | m |
| Height | Cone height | m |
| Resolution | Mesh resolution | - |

### Custom Geometry

Import custom geometry or use 2D-to-3D generation:
- **Extrusion**: Extend 2D shape along axis
- **Revolution**: Rotate 2D shape around axis
- **Import**: Load mesh from OBJ/STL

---

## Creating a Reactor

### Example 1: Simple Box Reactor

```csharp
// API Example
var reactor = new PhysicoChemDataset("SimpleBox");
reactor.GeometryType = ReactorGeometry.Box;
reactor.Width = 1.0;   // 1 meter
reactor.Height = 1.0;
reactor.Depth = 1.0;
reactor.Resolution = 50;

// Add reactants
reactor.AddSpecies("A", initialConc: 1.0);
reactor.AddSpecies("B", initialConc: 1.0);

// Define reaction: A + B -> C (exothermic)
reactor.AddReaction("A + B -> C",
    rateConstant: 0.1,
    activationEnergy: 50000,
    reactionHeat: -100000);

// Run
reactor.Simulate(duration: 100, dt: 0.01);
```

### Example 2: Calcite Precipitation

```csharp
var reactor = new PhysicoChemDataset("CalcitePrecip");
reactor.GeometryType = ReactorGeometry.Cylinder;
reactor.Radius = 0.1;
reactor.Height = 0.5;

// Add species
reactor.AddSpecies("Ca2+", 0.01);      // mol/L
reactor.AddSpecies("CO3^2-", 0.01);    // mol/L

// Add mineral
reactor.AddMineral("Calcite",
    saturationThreshold: 1.0,
    precipitationRate: 1e-6);

// Run with nucleation
reactor.EnableNucleation = true;
reactor.Simulate(duration: 3600, dt: 1.0);
```

### Example 3: Reactive Transport in Pore Network

```csharp
// Load pore network
var pnm = ProjectManager.GetDataset<PNMDataset>("CorePNM");

// Create coupled reactor
var reactor = new PhysicoChemDataset("PNM_Reactive");
reactor.SetGeometryFromPNM(pnm);

// Configure species and reactions
reactor.AddSpecies("H+", 1e-7);
reactor.AddSpecies("Ca2+", 0.0);
reactor.AddMineral("Calcite", initialAmount: 0.1);

// Add dissolution reaction
reactor.AddDissolutionReaction("Calcite",
    rateConstant: 1e-5,
    surfaceArea: 1000);  // m²/m³

// Run with flow
reactor.FlowRate = 1e-6;  // m³/s
reactor.Simulate(duration: 86400, dt: 10);
```

---

## Boundary Conditions

### Available Types

| Type | Description |
|------|-------------|
| Fixed | Constant value |
| Flux | Specified flux |
| Convective | Heat/mass transfer |
| Periodic | Wrap-around |
| No-flux | Insulated |

### Configuration

Apply to reactor boundaries:
- Top, Bottom
- Left, Right
- Front, Back

```csharp
reactor.SetBoundaryCondition(
    boundary: "Top",
    type: BoundaryType.Fixed,
    temperature: 25.0,  // °C
    concentration: new Dictionary<string, double> {
        {"A", 1.0}, {"B", 1.0}
    }
);
```

---

## Force Fields

### Available Forces

| Force | Description | Parameters |
|-------|-------------|------------|
| Gravity | Gravitational acceleration | g vector |
| Vortex | Swirling flow | center, strength |
| Centrifugal | Outward force | axis, angular velocity |
| Custom | User-defined | function |

### Configuration

```csharp
// Add gravity
reactor.AddForceField(
    type: ForceFieldType.Gravity,
    acceleration: new Vector3(0, -9.81, 0));

// Add vortex (stirring)
reactor.AddForceField(
    type: ForceFieldType.Vortex,
    center: new Vector3(0.5, 0.5, 0.5),
    strength: 1.0,
    axis: new Vector3(0, 1, 0));

// Add centrifuge
reactor.AddForceField(
    type: ForceFieldType.Centrifugal,
    axis: new Vector3(0, 1, 0),
    angularVelocity: 100.0);  // rad/s
```

### Custom Gravitational Acceleration

The PhysicoChem module supports custom gravity through the `GravityProperties` class with full planetary body presets and spatial variations:

**Planetary Body Presets:**
| Body | Surface Gravity (m/s²) |
|------|----------------------|
| Earth | 9.81 |
| Moon | 1.62 |
| Mars | 3.72 |
| Venus | 8.87 |
| Jupiter | 24.79 |
| Saturn | 10.44 |
| Mercury | 3.70 |
| Microgravity | ~0 |

**Configuration:**
```csharp
// Simple custom gravity vector
var gravityForce = new ForceField("Gravity", ForceType.Gravity)
{
    GravityVector = (0, 0, -1.62)  // Moon gravity
};

// Using planetary presets with advanced features
var gravityForce = new ForceField("Gravity", ForceType.Gravity)
{
    Gravity = new GravityProperties
    {
        UsePreset = true,
        PlanetaryBody = PlanetaryBody.Mars,  // Auto-sets 3.72 m/s²
        EnableAltitudeVariation = true,      // g varies with height
        EnableLatitudeVariation = false,     // For Earth latitude effects
        PlanetRadius = 3.3895e6              // Mars radius for altitude
    }
};
reactor.Forces.Add(gravityForce);
```

**GeoScript:**
```geoscript
# Set custom gravity (vector components)
PHYSICOCHEM_ADD_FORCE name=MoonGravity type=gravity gravity=0,0,-1.62

# Use planetary preset
PHYSICOCHEM_ADD_FORCE name=MarsGravity type=gravity gravity_preset=mars
```

---

## Nucleation and Crystal Growth

You can define **explicit nucleation sites** in both the ImGui tools panel and the GTK toolkit:

- **Point sites**: enter XYZ coordinates, mineral type, and nucleation rate.
- **Geometry-based sites**: select a reactor object (box/cylinder) and generate multiple sites inside it.
- **Material assignment**: optionally bind a site to a material ID so nucleation only occurs in matching cells.

### Homogeneous Nucleation

Spontaneous nucleation in bulk solution.

**Parameters:**
| Parameter | Description | Units |
|-----------|-------------|-------|
| Saturation Threshold | SI for nucleation | - |
| Nucleation Rate | Rate constant | 1/(m³·s) |
| Critical Radius | Minimum nucleus size | nm |

### Heterogeneous Nucleation

Nucleation on surfaces.

**Parameters:**
| Parameter | Description | Units |
|-----------|-------------|-------|
| Surface Sites | Nucleation site density | 1/m² |
| Contact Angle | Wetting angle | degrees |

### Crystal Growth

Growth of existing crystals.

**Parameters:**
| Parameter | Description | Units |
|-----------|-------------|-------|
| Growth Rate | Surface normal rate | m/s |
| Habit | Crystal shape | - |
| Size Distribution | Initial PSD | - |

---

## Parameter Sweeps

### Overview

Systematic exploration of parameter space.

### Configuration

Use the **Simulation Parameters → Parameter Sweep** panel (ImGui) or **Simulation Setup Wizard → Parameter Sweep**
(GTK) to add sweep curves that are applied each time step or simulation step.

```csharp
dataset.SimulationParams.EnableParameterSweep = true;
dataset.ParameterSweepManager.Enabled = true;
dataset.ParameterSweepManager.Mode = SweepMode.Temporal;

dataset.ParameterSweepManager.Sweeps.Add(new ParameterSweep
{
    ParameterName = "Temperature",
    TargetPath = "Domains[0].InitialConditions.Temperature",
    MinValue = 273,
    MaxValue = 373
});

new PhysicoChemSolver(dataset).RunSimulation();
```

**GeoScript:**
```geoscript
PHYSICOCHEM_SWEEP name=Temperature target=Domains[0].InitialConditions.Temperature min=273 max=373 mode=Temporal
RUN_SIMULATION
```

### Output

- Sweep curve values applied per step/time
- Updated simulation history and tracked metrics

---

## Visualization

### 3D Mesh View

- Cell coloring by property (T, P, concentration)
- Isosurfaces at threshold values
- Streamlines for flow visualization
- Animation of time evolution

### 2D Slice View

- Horizontal/vertical cuts
- Contour plots
- Vector fields

### Time Series

- Concentration vs time
- Temperature vs time
- Reaction rate vs time
- Mass balance verification

---

## Output File Formats

### CSV Export

Tabular data for each cell or time step.

### JSON Export

Complete simulation configuration and results.

```json
{
  "reactor": {
    "type": "Box",
    "dimensions": [1.0, 1.0, 1.0],
    "resolution": 50
  },
  "species": ["A", "B", "C"],
  "results": {
    "time": [0, 0.1, 0.2, ...],
    "concentrations": {...}
  }
}
```

---

## Troubleshooting

### Simulation Diverges

**Solutions:**
- Reduce time step
- Check reaction rates are physical
- Verify boundary conditions
- Enable adaptive time stepping

### No Reaction Occurs

**Check:**
- Reactants are present
- Reaction is defined correctly
- Activation energy is reasonable
- Temperature is sufficient

### Slow Simulation

**Solutions:**
- Reduce mesh resolution
- Increase time step (carefully)
- Enable GPU acceleration
- Use implicit solver

### Missing Output Files

**Check:**
- Output directory exists
- File permissions
- Sufficient disk space
- Output format configuration

---

## Common Parameters Reference

### Temperatures
| Condition | Temperature |
|-----------|-------------|
| Room temperature | 25°C (298 K) |
| Reservoir | 80-150°C |
| Geothermal | 150-350°C |
| Magmatic | >500°C |

### Pressures
| Condition | Pressure |
|-----------|----------|
| Surface | 1 bar |
| Shallow subsurface | 1-10 bar |
| Deep reservoir | 100-500 bar |
| Mantle | >10,000 bar |

### Concentrations
| Species | Typical Range |
|---------|---------------|
| Major ions | 0.001-1 mol/L |
| Trace elements | 1e-9-1e-6 mol/L |
| Dissolved gases | 1e-5-1e-2 mol/L |

### Porosity
| Rock Type | Porosity |
|-----------|----------|
| Sandstone | 0.05-0.30 |
| Limestone | 0.01-0.20 |
| Granite | 0.001-0.02 |
| Shale | 0.01-0.10 |

---

## References & Next Steps

- [Thermodynamics and Geochemistry](Thermodynamics-and-Geochemistry.md) - Chemical systems
- [Pore Network Modeling](Pore-Network-Modeling.md) - PNM coupling
- [Geothermal Simulation](Geothermal-Simulation.md) - Heat transfer
- [User Guide](User-Guide.md) - Application documentation
- [Home](Home.md) - Wiki home page
