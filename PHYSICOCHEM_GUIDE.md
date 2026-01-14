// PHYSICOCHEM Dataset - User Guide
# PHYSICOCHEM: Multiphysics Reactor Simulation System

**Version:** 1.0
**Date:** 2026-11-14
**Author:** GeoscientistToolkit Development Team

---

## Overview

The **PHYSICOCHEM** dataset system provides a comprehensive framework for building and simulating multiphysics reactor experiments with TOUGH-like capabilities. It features:

-  2D-to-3D geometry interpolation (extrusion, revolution, vertical profile)
-  Multiple reactor types (box, sphere, cylinder, cone, custom shapes)
-  Boolean operations on domains (union, subtract, intersect)
-  Comprehensive boundary conditions (Dirichlet, Neumann, Robin, etc.)
-  Force fields (gravity, vortex, centrifugal, custom)
-  Nucleation and crystal growth
-  Full reactive transport coupling (TOUGHREACT-like)
-  Parameter sweep with sensitivity analysis
-  Optional coupling with geothermal simulations

---

## Quick Start

### Example 1: Simple Box Reactor with Two Reactants

```csharp
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Analysis.PhysicoChem;

// Create dataset
var dataset = new PhysicoChemDataset("TwoBoxReactor",
    "Two boxes stacked vertically with different reactants");

// Create top box with Reactant A
var topBox = new ReactorDomain("TopBox", new ReactorGeometry
{
    Type = GeometryType.Box,
    Center = (0.5, 0.5, 0.75),
    Dimensions = (1.0, 1.0, 0.5)
});

topBox.Material = new MaterialProperties
{
    Porosity = 0.3,
    Permeability = 1e-12
};

topBox.InitialConditions = new InitialConditions
{
    Temperature = 298.15,
    Pressure = 101325.0,
    Concentrations = new Dictionary<string, double>
    {
        {"Ca2+", 0.01}, // mol/L
        {"Cl-", 0.02}
    }
};

// Create bottom box with Reactant B
var bottomBox = new ReactorDomain("BottomBox", new ReactorGeometry
{
    Type = GeometryType.Box,
    Center = (0.5, 0.5, 0.25),
    Dimensions = (1.0, 1.0, 0.5)
});

bottomBox.InitialConditions = new InitialConditions
{
    Temperature = 298.15,
    Pressure = 101325.0,
    Concentrations = new Dictionary<string, double>
    {
        {"HCO3-", 0.01},
        {"Na+", 0.01}
    }
};

// Add domains
dataset.AddDomain(topBox);
dataset.AddDomain(bottomBox);

// Create interactive boundary between boxes (can be disabled/enabled)
var boundary = new BoundaryCondition("Interface",
    BoundaryType.Interactive,
    BoundaryLocation.Custom);

boundary.CustomRegionCenter = (0.5, 0.5, 0.5); // At interface
boundary.CustomRegionRadius = 0.1;
boundary.IsActive = true; // Initially enabled - reactants can mix

dataset.BoundaryConditions.Add(boundary);

// Add gravity
var gravity = new ForceField("Gravity", ForceType.Gravity)
{
    GravityVector = (0, 0, -9.81)
};

dataset.Forces.Add(gravity);

// Configure simulation
dataset.SimulationParams.TotalTime = 3600.0; // 1 hour
dataset.SimulationParams.TimeStep = 1.0; // 1 second
dataset.SimulationParams.EnableReactiveTransport = true;
dataset.SimulationParams.EnableFlow = true;
dataset.SimulationParams.EnableHeatTransfer = true;

// Run simulation
var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();

// Analyze results
var finalState = dataset.CurrentState;
Console.WriteLine($"Final average temperature: {CalculateMean(finalState.Temperature)} K");

// Disable boundary to allow mixing
boundary.IsActive = false;
solver.RunSimulation(); // Run again with mixing enabled
```

---

## Example 2: Cylindrical Reactor with Vortex Flow

```csharp
// Create cylindrical reactor
var dataset = new PhysicoChemDataset("VortexReactor",
    "Cylindrical reactor with vortex mixing");

var cylinder = new ReactorDomain("Reactor", new ReactorGeometry
{
    Type = GeometryType.Cylinder,
    Center = (0, 0, 0),
    Radius = 0.5, // 0.5 m radius
    Height = 1.0, // 1 m tall
    InnerRadius = 0.0 // Solid cylinder (no hollow core)
});

cylinder.Material = new MaterialProperties
{
    Porosity = 0.4,
    Permeability = 1e-11 // Higher permeability
};

cylinder.InitialConditions = new InitialConditions
{
    Temperature = 350.0, // Heated
    Pressure = 200000.0, // 2 bar
    Concentrations = new Dictionary<string, double>
    {
        {"SiO2", 0.001}, // Silica
        {"Ca2+", 0.01}
    }
};

dataset.AddDomain(cylinder);

// Add vortex force field
var vortex = new ForceField("CentralVortex", ForceType.Vortex)
{
    VortexCenter = (0, 0, 0.5),
    VortexAxis = (0, 0, 1),
    VortexStrength = 10.0, // 10 rad/s
    VortexRadius = 0.4
};

dataset.Forces.Add(vortex);

// Add nucleation site at center
var nucleationSite = new NucleationSite("CenterNucleation",
    (0, 0, 0.5),
    "Calcite");

nucleationSite.NucleationRate = 1e6; // nuclei/s
nucleationSite.CriticalSupersaturation = 1.2;

dataset.NucleationSites.Add(nucleationSite);

// Enable nucleation
dataset.SimulationParams.EnableNucleation = true;

// Run
var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();
```

---

## Example 3: 2D-to-3D Extrusion (Custom Profile)

```csharp
// Define 2D profile (cross-section of reactor)
var profile = new List<(double X, double Y)>
{
    (0, 0),
    (1, 0),
    (1, 0.5),
    (0.5, 1),
    (0, 0.5),
    (0, 0) // Close the loop
};

// Create domain with extrusion
var domain = new ReactorDomain("CustomShape", new ReactorGeometry
{
    Type = GeometryType.Custom2D,
    Profile2D = profile,
    InterpolationMode = Interpolation2D3DMode.Extrusion,
    ExtrusionDepth = 2.0 // Extrude 2 m in Z direction
});

domain.InitialConditions = new InitialConditions
{
    Temperature = 298.15,
    Concentrations = new Dictionary<string, double>
    {
        {"H+", 1e-7},
        {"OH-", 1e-7}
    }
};

var dataset = new PhysicoChemDataset("ExtrudedReactor");
dataset.AddDomain(domain);

// Inlet on one face
var inlet = new BoundaryCondition("Inlet",
    BoundaryType.Inlet,
    BoundaryLocation.XMin);

inlet.Variable = BoundaryVariable.Concentration;
inlet.SpeciesName = "Ca2+";
inlet.Value = 0.05; // mol/L

dataset.BoundaryConditions.Add(inlet);

// Outlet on opposite face
var outlet = new BoundaryCondition("Outlet",
    BoundaryType.Outlet,
    BoundaryLocation.XMax);

dataset.BoundaryConditions.Add(outlet);

var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();
```

---

## Example 4: 2D-to-3D Revolution (Axisymmetric)

```csharp
// Define radial profile (R vs Z)
var radialProfile = new List<(double R, double Z)>
{
    (0.0, 0.0),
    (0.5, 0.0),
    (0.5, 0.5),
    (0.3, 1.0),
    (0.1, 1.5),
    (0.0, 1.5)
};

var domain = new ReactorDomain("ConicalReactor", new ReactorGeometry
{
    Type = GeometryType.Custom2D,
    Profile2D = radialProfile,
    InterpolationMode = Interpolation2D3DMode.Revolution,
    RadialSegments = 36 // 36 segments = 10° each
});

var dataset = new PhysicoChemDataset("AxisymmetricReactor");
dataset.AddDomain(domain);

var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();
```

---

## Example 5: Boolean Operations (Union, Subtract, Intersect)

```csharp
var dataset = new PhysicoChemDataset("BooleanReactor");

// Create two overlapping spheres
var sphere1 = new ReactorDomain("Sphere1", new ReactorGeometry
{
    Type = GeometryType.Sphere,
    Center = (0, 0, 0),
    Radius = 0.5
});

var sphere2 = new ReactorDomain("Sphere2", new ReactorGeometry
{
    Type = GeometryType.Sphere,
    Center = (0.5, 0, 0),
    Radius = 0.5
});

// Union (combine both spheres)
var union = dataset.BooleanOperation(sphere1, sphere2, BooleanOp.Union);
union.Name = "UnionSpheres";

// Subtract (sphere1 minus sphere2 - creates crescent)
var subtract = dataset.BooleanOperation(sphere1, sphere2, BooleanOp.Subtract);
subtract.Name = "Crescent";

// Intersect (only overlapping region)
var intersect = dataset.BooleanOperation(sphere1, sphere2, BooleanOp.Intersect);
intersect.Name = "Lens";

// Add the desired result
dataset.AddDomain(union); // Or subtract, or intersect

var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();
```

---

## Example 6: Parameter Sweep (Runtime Curves)

```csharp
var dataset = new PhysicoChemDataset("ParamSweepReactor");

// Set up base reactor
var box = new ReactorDomain("Reactor", new ReactorGeometry
{
    Type = GeometryType.Box,
    Center = (0.5, 0.5, 0.5),
    Dimensions = (1, 1, 1)
});

box.Material = new MaterialProperties { Porosity = 0.3 };
dataset.AddDomain(box);

// Enable sweep application during simulation
dataset.SimulationParams.EnableParameterSweep = true;
dataset.ParameterSweepManager.Enabled = true;
dataset.ParameterSweepManager.Mode = SweepMode.Temporal;

// Add a sweep curve (normalized 0..1 across time or step)
dataset.ParameterSweepManager.Sweeps.Add(new ParameterSweep
{
    ParameterName = "Porosity",
    TargetPath = "Domains[0].Material.Porosity",
    MinValue = 0.1,
    MaxValue = 0.5
});

// Run simulation with parameter sweeps applied each step
var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();
```

### Batch Sweeps (Sensitivity Analysis)

For offline parameter combinations, configure `ParameterSweepConfig` and call `RunParameterSweep()`:

```csharp
dataset.ParameterSweep = new ParameterSweepConfig
{
    Type = SweepType.LatinHypercube,
    SamplesPerParameter = 10,
    ParallelExecution = true
};

dataset.ParameterSweep.Parameters.Add(new ParameterRange
{
    Name = "Porosity",
    Min = 0.1,
    Max = 0.5,
    Scale = ParameterScaleType.Linear,
    TargetPath = "Domains[0].Material.Porosity"
});

var results = new PhysicoChemSolver(dataset).RunParameterSweep();
File.WriteAllText("sweep_results.csv", results.ExportToCsv());
```

---

## Example 7: Coupling with Geothermal Simulation

```csharp
var dataset = new PhysicoChemDataset("CoupledReactor");

// Enable geothermal coupling
dataset.CoupleWithGeothermal = true;
dataset.GeothermalDatasetPath = "/path/to/geothermal/dataset.json";

// Set up reactor domain
var domain = new ReactorDomain("InjectionZone", new ReactorGeometry
{
    Type = GeometryType.Cylinder,
    Center = (0, 0, -1000), // 1 km depth
    Radius = 10.0,
    Height = 100.0
});

domain.InitialConditions = new InitialConditions
{
    Temperature = 423.15, // 150°C
    Pressure = 10e6, // 10 MPa
    Concentrations = new Dictionary<string, double>
    {
        {"Ca2+", 0.02},
        {"HCO3-", 0.03},
        {"SiO2", 0.001}
    }
};

dataset.AddDomain(domain);

// Add geothermal gradient boundary
var geothermalBC = new BoundaryCondition("GeothermalGradient",
    BoundaryType.FixedValue,
    BoundaryLocation.ZMin);

geothermalBC.Variable = BoundaryVariable.Temperature;
geothermalBC.Value = 473.15; // 200°C at bottom
geothermalBC.IsTimeDependendent = false;

dataset.BoundaryConditions.Add(geothermalBC);

var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();

// Data will be exchanged with geothermal simulation each timestep
```

---

## Boundary Condition Types

| Type | Description | Use Case |
|------|-------------|----------|
| `FixedValue` | Dirichlet (T = constant) | Fixed temperature, concentration |
| `FixedFlux` | Neumann (dT/dn = constant) | Heat flux, mass flux |
| `ZeroFlux` | Insulated/no-flow | Adiabatic walls |
| `Convective` | Robin (heat transfer coefficient) | Convection to ambient |
| `Periodic` | Wrap-around | Repeating geometry |
| `Open` | Free boundary | Far-field |
| `NoSlipWall` | Velocity = 0 | Solid walls |
| `FreeSlipWall` | Tangential slip allowed | Symmetry planes |
| `Inlet` | Specified inflow | Reactant injection |
| `Outlet` | Zero gradient | Drainage |
| `Interactive` | Can be toggled | Domain interface control |

---

## Force Field Types

| Type | Description | Equation |
|------|-------------|----------|
| `Gravity` | Gravitational acceleration | F = ρg |
| `Vortex` | Rotating flow | F = ρω²r (centripetal) |
| `Centrifugal` | Outward force | F = -ρω²r |
| `Custom` | User-defined function | F = f(x, y, z, t) |

---

## 2D-to-3D Interpolation Modes

### 1. Extrusion
- **Input:** 2D polygon in XY plane
- **Output:** Extrude along Z-axis
- **Use:** Straight channels, rectangular reactors

### 2. Revolution
- **Input:** 2D profile in RZ plane (radius vs height)
- **Output:** Rotate 360° around Z-axis
- **Use:** Cylindrical, conical, spherical reactors

### 3. Vertical Profile
- **Input:** 2D vertical slice
- **Output:** Interpolate horizontally
- **Use:** Geological layers, stratified systems

---

## Parameter Sweep Types

| Type | Description | Best For |
|------|-------------|----------|
| `FullFactorial` | All combinations | Small parameter spaces (≤3 params) |
| `LatinHypercube` | Space-filling design | Medium spaces (3-10 params) |
| `RandomSampling` | Random points | Quick exploration |
| `SobolSequence` | Quasi-random low-discrepancy | High-dimensional (>10 params) |
| `OneAtATime` | Vary one parameter at a time | Sensitivity screening |

---

## Performance Tips

1. **Grid Resolution:**
   - Start with coarse grids (20x20x20) for testing
   - Refine to 50x50x50 or higher for production

2. **Time Step:**
   - Use CFL condition: dt < dx / v_max
   - Typical: 0.1-10 seconds depending on flow velocity

3. **GPU Acceleration:**
   ```csharp
   dataset.SimulationParams.UseGPU = true;
   ```

4. **Parallel Parameter Sweep:**
   ```csharp
   dataset.ParameterSweep.ParallelExecution = true;
   dataset.ParameterSweep.MaxParallelRuns = 8;
   ```

---

## Advanced Features

### Nucleation Control 

```csharp
var site = new NucleationSite("Site1", position, "Calcite");
site.NucleationRate = 1e6; // nuclei/s
site.CriticalSupersaturation = 1.5;
site.ActivationEnergy = 50000; // J/mol
site.InitialRadius = 1e-6; // 1 micron
```

### Time-Dependent Boundaries

```csharp
var bc = new BoundaryCondition("PulsedInlet",
    BoundaryType.FixedValue,
    BoundaryLocation.XMin);

bc.IsTimeDependendent = true;
bc.TimeExpression = "300 + 50*sin(t/100)"; // Oscillating temperature
```

### Custom Force Fields

```csharp
var customForce = new ForceField("Custom", ForceType.Custom);
customForce.CustomForce = (x, y, z, t) =>
{
    double fx = -0.1 * x; // Spring force
    double fy = -0.1 * y;
    double fz = 0;
    return (fx, fy, fz);
};
```

---

## Troubleshooting

### Common Issues

1. **Simulation diverges:**
   - Reduce time step
   - Check CFL condition
   - Ensure reasonable material properties

2. **Slow performance:**
   - Enable GPU acceleration
   - Reduce grid resolution
   - Use coarser time steps

3. **Boundary conditions not applied:**
   - Check `IsActive = true`
   - Verify boundary location
   - Check variable type matches

4. **Boolean operations give unexpected results:**
   - Visualize individual domains first
   - Check geometry overlap
   - Verify operation type (union vs subtract)

---

## File Structure

```
Data/PhysicoChem/
├── PhysicoChemDataset.cs       - Main dataset class
├── ReactorDomain.cs            - Domain definition
├── ReactorGeometry.cs          - Geometry (in ReactorDomain.cs)
├── BoundaryCondition.cs        - Boundary conditions
├── ForceField.cs               - Force fields (in BoundaryCondition.cs)
├── NucleationSite.cs           - Nucleation (in BoundaryCondition.cs)
├── ParameterSweep.cs           - Parameter sweep config
└── ReactorMeshGenerator.cs     - Mesh generation

Analysis/PhysicoChem/
├── PhysicoChemSolver.cs        - Main multiphysics solver
└── SubSolvers.cs               - Heat, flow, nucleation solvers
```

---

## References

### Scientific Basis

- **Reactive Transport:** Xu et al. (2011) TOUGHREACT - Same approach as TOUGH simulators
- **Nucleation Theory:** Classical Nucleation Theory (CNT) - Nielsen & Söhnel (1971)
- **Heat Transfer:** Finite volume method - Patankar (1980)
- **Darcy Flow:** Multiphase extension - Bear (1972)

### Related Tools

- TOUGH2/TOUGH3 (Lawrence Berkeley National Lab)
- PHREEQC (USGS)
- COMSOL Multiphysics
- PetraSim
- OpenFOAM

---

## Support

For questions, issues, or feature requests, please contact the GeoscientistToolkit development team.

**Version History:**
- v1.0 (2026-11-14): Initial release with full multiphysics capabilities

---

**© 2026 The Geoscientist's Toolkit Project**
