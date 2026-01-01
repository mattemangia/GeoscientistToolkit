# Quick Guide: Starting a Sample Reaction

**GeoscientistToolkit - Quick Start Guide for Reactive Simulations**

---

## Table of Contents

1. [Introduction](#introduction)
2. [Quick Start with GTK Interface](#quick-start-with-gtk-interface)
3. [Example 1: Simple Reaction in a Box Reactor](#example-1-simple-reaction-in-a-box-reactor)
4. [Example 2: Calcite Precipitation](#example-2-calcite-precipitation)
5. [Example 3: Reactive Transport in PNM](#example-3-reactive-transport-in-pnm)
6. [Visualizing Results](#visualizing-results)
7. [Troubleshooting](#troubleshooting)

---

## Introduction

GeoscientistToolkit offers two main systems for reactive simulations:

- **PhysicoChem**: Multiphysics reactors with custom geometries (box, cylinders, spheres, Voronoi meshes)
- **PNM Reactive Transport**: Reactive transport through pore networks from CT images

This guide shows you how to quickly start a sample simulation.

---

## Quick Start with GTK Interface

### The Simplest Way: Test the Default Reactor

On startup, GeoscientistToolkit GTK automatically creates a **default exothermic reactor** ready to be tested!

#### Step 1: Launch the GTK Application

```bash
cd GeoscientistToolkit
./bin/GeoscientistToolkit-gtk
# or on Windows: bin\GeoscientistToolkit-gtk.exe
```

#### Step 2: View the Default Reactor

When the application opens, you'll see:

1. **Left panel**: Dataset list
   - There should be `Default Exothermic Reactor` already loaded
2. **Center panel**: 3D viewport
3. **Right panel**: Visualization options and controls

**Click** on the `Default Exothermic Reactor` dataset to select it.

#### Step 3: Visualize the 3D Mesh

The default reactor is a cube with a **3x3x3** cell grid (27 total cells):

- **Dimensions**: Width 6.0m, Height 6.0m, Depth 6.0m
- **Initial reactants**:
  - `ReactantA`: 5.0 mol/L
  - `ReactantB`: 3.0 mol/L
  - `Product`: 0.0 mol/L (forms during reaction)
  - `Catalyst`: 0.01 mol/L
- **Initial temperature**: 298.15 K (25°C)
- **Exothermic reaction**: ReactantA + ReactantB → Product + Heat

In the **3D Viewport**:
- You should see **27 boxes** arranged in a 3D grid
- Rotate the view with **right mouse + drag**
- Zoom with **mouse wheel**
- Pan with **middle mouse + drag**

#### Step 4: Inspect Cells

1. **Click on a cell** (box) in the 3D viewport
2. In the right panel you'll see:
   ```
   Selected Cell: Cell_13
   Temperature: 298.15 K
   Pressure: 101325.0 Pa
   Volume: 81.67 m³

   Concentrations:
     ReactantA: 5.00 mol/L
     ReactantB: 3.00 mol/L
     Product: 0.00 mol/L
     Catalyst: 0.01 mol/L
   ```

#### Step 5: Select a Plane of Cells

In the right panel, **"Plane Selection"** section:

1. Click on **"Select XY plane"** → Selects all cells on the same horizontal plane
2. Click on **"Select XZ plane"** → Selects cells on vertical plane along X
3. Click on **"Select YZ plane"** → Selects cells on vertical plane along Y

Selected cells are highlighted in **bright cyan**.

#### Step 6: Configure a Reaction

To configure chemical species and reactions:

1. **Upper menu** → `Tools` → `Configure Species...`
2. A dialog opens where you can:
   - Add new chemical species
   - Modify initial concentrations
   - Configure chemical reactions

Or use **GeoScript** (see below).

---

### Creating a New Reactor from Scratch

#### Step 1: Create a New PhysicoChem Dataset

In the **upper toolbar**:
- Click the **"Add PhysicoChem"** button (icon with physico-chemical symbol)

Or:
- **Menu** → `File` → `New project`

A new empty dataset called `PhysicoChem_HHmmss` will be created.

#### Step 2: Create a Domain (Reactor)

1. **Toolbar** → Click on **"Create Domain"** (mesh icon)

   **Or:** `Tools` → `Create Domain...`

2. The **Domain Creator Dialog** opens with the following options:

   **a) Domain Name**
   ```
   Domain Name: [MainReactor]
   ```

   **b) Geometry Type** (select from dropdown):
   - `Box (Rectangular)` ← **Simplest choice**
   - `Sphere`
   - `Cylinder`
   - `Cone`
   - `Torus`
   - `Parallelepiped`
   - `Custom 2D Extrusion`
   - `Custom 3D Mesh`
   - `Voronoi` ← **Random mesh**

   **c) Material** (select from menu):
   - If you have materials in the library, select them here
   - Otherwise leave `(None)` and configure later

   **d) Geometric Parameters** (change based on type):

   **For Box:**
   ```
   Center X (m):  0.0
   Center Y (m):  0.0
   Center Z (m):  0.0
   Width (m):     10.0
   Depth (m):     10.0
   Height (m):    10.0
   ```

   **For Cylinder:**
   ```
   Base Center X (m): 0.0
   Base Center Y (m): 0.0
   Base Center Z (m): 0.0
   Axis X: 0.0
   Axis Y: 0.0
   Axis Z: 1.0
   Radius (m):  5.0
   Height (m):  10.0
   ```

   **For Voronoi (random mesh):**
   ```
   Number of Sites: 100
   Width (m):  10.0
   Depth (m):  10.0
   Height (m): 10.0
   ```

   **e) Options**:
   - ✅ `Domain is active` (leave selected)
   - ✅ `Allow interaction with other domains` (if you want reactants to mix)

3. Click **"Create"**

The domain is created and the mesh is generated automatically!

#### Step 3: Configure Chemical Species

1. **Menu** → `Tools` → `Configure Species...`

2. In the dialog that opens:
   - **Species name**: `Ca2+`
   - **Initial concentration**: `0.01` mol/L
   - Click **"Add"**

3. Repeat for other species:
   - `CO3^2-`: `0.01` mol/L
   - `Cl-`: `0.02` mol/L
   - etc.

#### Step 4: Set Boundary Conditions

1. **Menu** → `Tools` → `Set Boundary Conditions...`

2. Choose the type of boundary condition:
   - **Inlet** (reactant inlet)
   - **Outlet** (product outlet)
   - **Fixed Temperature** (fixed temperature)
   - **Heat Flux** (heat flow)
   - **No-Slip Wall** (solid wall)

3. Example - Ca²⁺ Inlet:
   ```
   Name: CalciumInlet
   Type: Inlet
   Location: X Min (left face)
   Variable: Concentration
   Species: Ca2+
   Value: 0.05 mol/L
   ```

#### Step 5: Add Forces (Optional)

1. **Menu** → `Tools` → `Force Field Editor...`

2. Choose force type:
   - **Gravity** (gravity)
   - **Vortex** (vortex flow)
   - **Centrifugal** (centrifugal force)
   - **Custom** (custom)

3. Example - Gravity:
   ```
   Name: Gravity
   Type: Gravity
   Gravity Vector:
     X: 0.0
     Y: 0.0
     Z: -9.81
   ```

#### Step 6: Run the Simulation

**Method 1: Graphical interface** (if available)
- Click on **"Run Simulation"**

**Method 2: GeoScript** (recommended)

1. **Click on the "GeoScript" tab** in the lower panel

2. Write the script:

```geoscript
# Configure simulation parameters
SET_SIM_PARAMS 3600 1.0 true true true
# Arguments: total_time(s) time_step(s) enable_flow enable_heat enable_reactions

# Run simulation
RUN_PHYSICOCHEM_SIMULATION

# Export results
EXPORT_RESULTS ./reaction_results
```

3. Click **"Run Script"** or press `Ctrl+Enter`

#### Step 7: Visualize Results

After the simulation:

1. **Color Mode** (right panel):
   - Select `Temperature` to see temperature field
   - Select `Pressure` to see pressure field
   - Select `Concentration` and choose a species (e.g., `Ca2+`)

2. **Render Mode**:
   - `Wireframe`: Only cell edges
   - `Solid`: Solid colored cells
   - `Points`: Only cell centers

3. **Slicing** (3D cutting):
   - ✅ Enable `Enable Slicing`
   - Move the slider to cut the reactor and see inside

---

### Complete GTK Example: Ca²⁺ + CO₃²⁻ → CaCO₃ Reaction

#### Complete GeoScript Script

Create a file `test_calcite_gtk.geoscript` with:

```geoscript
# ========================================
# Calcite Precipitation Test - GTK
# ========================================

# 1. Create new PhysicoChem dataset
# (or use the already loaded default one)

# 2. Add chemical species
ADD_SPECIES Ca2+ 0.02
ADD_SPECIES CO3^2- 0.02
ADD_SPECIES Na+ 0.01
ADD_SPECIES Cl- 0.01

# 3. Set initial concentrations for all cells
SET_INITIAL_CONCENTRATION Ca2+ 0.01
SET_INITIAL_CONCENTRATION CO3^2- 0.01

# 4. Configure boundary condition - Inlet with high concentration
ADD_BOUNDARY_CONDITION Inlet XMin Concentration Ca2+ 0.05
ADD_BOUNDARY_CONDITION Inlet XMin Concentration CO3^2- 0.05

# 5. Add gravity
ADD_FORCE Gravity 0 0 -9.81

# 6. Configure simulation parameters
SET_SIM_PARAMS 1800 0.5 true true true
# 30 minutes, timestep 0.5s, flow ON, heat ON, reactions ON

# 7. Enable nucleation
ENABLE_NUCLEATION Calcite 0.0 0.0 0.0 1e6 1.2
# Arguments: mineral X Y Z rate critical_supersaturation

# 8. Run simulation
RUN_PHYSICOCHEM_SIMULATION

# 9. Print results
PRINT "Simulation completed!"
PRINT "Visualize results with Color Mode → Concentration → Ca2+"
PRINT "Or Color Mode → Mineral Precipitation → Calcite"

# 10. Export
EXPORT_RESULTS ./calcite_gtk_results
```

#### Running the Script

1. **In the GeoScript tab** of the GTK application
2. Paste the content above
3. Click **"Run Script"**
4. Watch the simulation in real-time in the 3D viewport!

---

### Tips for GTK Interface

#### 3D Navigation
- **Rotate**: Right mouse + drag
- **Zoom**: Mouse wheel
- **Pan**: Middle mouse + drag (or Shift + right mouse)

#### Cell Selection
- **Single cell**: Left click on the cell
- **XY plane**: "Select XY plane" button
- **XZ plane**: "Select XZ plane" button
- **YZ plane**: "Select YZ plane" button
- **Deselect all**: Click on background

#### Visualization
- **Wireframe**: See only edges (faster)
- **Solid**: Full colored cells (clearer)
- **Slicing**: Cut the reactor to see inside
- **Isosurface**: Show surfaces at constant value

#### Camera Controls (right panel)
- **Yaw**: Horizontal rotation (-180° to 180°)
- **Pitch**: Vertical tilt (-90° to 90°)
- **Zoom**: Zoom level (0.1x to 8x)
- **Reset View**: Return to default view

---

## Example 1: Simple Reaction in a Box Reactor

### Scenario
Two stacked boxes with different reactants that can mix through an interface.

### C# Code

```csharp
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Analysis.PhysicoChem;

// Create the dataset
var dataset = new PhysicoChemDataset("SimpleReactor",
    "Two stacked boxes with Ca2+ and HCO3- reactants");

// Top box with Ca2+ (calcium ion)
var topBox = new ReactorDomain("TopBox", new ReactorGeometry
{
    Type = GeometryType.Box,
    Center = (0.5, 0.5, 0.75),  // Center in meters
    Dimensions = (1.0, 1.0, 0.5) // Width x Depth x Height (m)
});

topBox.Material = new MaterialProperties
{
    Porosity = 0.3,           // 30% porosity
    Permeability = 1e-12      // m²
};

topBox.InitialConditions = new InitialConditions
{
    Temperature = 298.15,     // 25°C
    Pressure = 101325.0,      // 1 atm
    Concentrations = new Dictionary<string, double>
    {
        {"Ca2+", 0.01},       // 0.01 mol/L
        {"Cl-", 0.02}
    }
};

// Bottom box with HCO3- (bicarbonate)
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
        {"HCO3-", 0.01},      // 0.01 mol/L
        {"Na+", 0.01}
    }
};

// Add the domains
dataset.AddDomain(topBox);
dataset.AddDomain(bottomBox);

// Create interactive interface between boxes
var boundary = new BoundaryCondition("Interface",
    BoundaryType.Interactive,
    BoundaryLocation.Custom);

boundary.CustomRegionCenter = (0.5, 0.5, 0.5); // At the interface
boundary.CustomRegionRadius = 0.1;
boundary.IsActive = true; // Reactants can mix

dataset.BoundaryConditions.Add(boundary);

// Add gravity
var gravity = new ForceField("Gravity", ForceType.Gravity)
{
    GravityVector = (0, 0, -9.81)
};
dataset.Forces.Add(gravity);

// Configure simulation
dataset.SimulationParams.TotalTime = 3600.0;      // 1 hour
dataset.SimulationParams.TimeStep = 1.0;          // 1 second
dataset.SimulationParams.EnableReactiveTransport = true;
dataset.SimulationParams.EnableFlow = true;
dataset.SimulationParams.EnableHeatTransfer = true;

// Run the simulation
var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();

// Analyze results
var finalState = dataset.CurrentState;
Console.WriteLine($"Final average temperature: {finalState.Temperature.Average()} K");
Console.WriteLine($"Simulation completed!");
```

### What does this code do?

1. **Creates two boxes** separated vertically
2. Top box contains **Ca²⁺** (calcium)
3. Bottom box contains **HCO₃⁻** (bicarbonate)
4. At the interface reactants mix and can react forming **CaCO₃** (calcite)
5. Gravity influences downward flow

---

## Example 2: Calcite Precipitation

### Scenario
Cylindrical reactor with vortex flow where calcium and carbonate precipitate forming calcite.

### C# Code

```csharp
// Create cylindrical reactor
var dataset = new PhysicoChemDataset("CalciteReactor",
    "CaCO3 precipitation in cylindrical reactor");

var cylinder = new ReactorDomain("Reactor", new ReactorGeometry
{
    Type = GeometryType.Cylinder,
    Center = (0, 0, 0),
    Radius = 0.5,      // 0.5 m radius
    Height = 1.0,      // 1 m height
    InnerRadius = 0.0  // Solid cylinder (not hollow)
});

cylinder.Material = new MaterialProperties
{
    Porosity = 0.4,
    Permeability = 1e-11
};

// Supersaturated initial conditions for precipitation
cylinder.InitialConditions = new InitialConditions
{
    Temperature = 350.0,   // 77°C - elevated temperature
    Pressure = 200000.0,   // 2 bar
    Concentrations = new Dictionary<string, double>
    {
        {"Ca2+", 0.02},    // High concentration
        {"CO3^2-", 0.02}   // High concentration → supersaturation
    }
};

dataset.AddDomain(cylinder);

// Add vortex force field for mixing
var vortex = new ForceField("CentralVortex", ForceType.Vortex)
{
    VortexCenter = (0, 0, 0.5),
    VortexAxis = (0, 0, 1),
    VortexStrength = 10.0,  // 10 rad/s
    VortexRadius = 0.4
};

dataset.Forces.Add(vortex);

// Add nucleation site at center
var nucleationSite = new NucleationSite("CentralNucleation",
    (0, 0, 0.5),
    "Calcite");

nucleationSite.NucleationRate = 1e6;           // nuclei/s
nucleationSite.CriticalSupersaturation = 1.2;

dataset.NucleationSites.Add(nucleationSite);

// Enable nucleation
dataset.SimulationParams.EnableNucleation = true;
dataset.SimulationParams.TotalTime = 1800.0;   // 30 minutes
dataset.SimulationParams.TimeStep = 0.5;

// Run
var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();

Console.WriteLine("Calcite precipitation completed!");
```

### Chemical Reaction

```
Ca²⁺ + CO₃²⁻ → CaCO₃(s) ↓
```

When concentration is supersaturated (Ω > 1.2), solid calcite forms and precipitates.

---

## Example 3: Reactive Transport in PNM

### Scenario
Reactive transport simulation through a pore network extracted from CT images, with calcite precipitation reducing permeability.

### Method 1: C# Code

```csharp
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Data.Pnm;

// Assume you already have a loaded PNM
// var pnm = ... (generated from CT scan)

// Configure simulation
var options = new PNMReactiveTransportOptions
{
    TotalTime = 3600.0,          // 1 hour
    TimeStep = 1.0,              // 1 second
    OutputInterval = 60.0,       // Save every minute

    // Flow parameters
    FlowAxis = FlowAxis.Z,
    InletPressure = 2.0f,        // Pa
    OutletPressure = 0.0f,       // Pa
    FluidViscosity = 1.0f,       // cP (water)
    FluidDensity = 1000f,        // kg/m³

    // Heat transfer
    InletTemperature = 298.15f,  // 25°C
    ThermalConductivity = 0.6f,  // W/(m·K)
    SpecificHeat = 4184f,        // J/(kg·K)

    // Transport
    MolecularDiffusivity = 2.299e-9f,  // m²/s
    Dispersivity = 0.1f,               // m

    // Initial conditions
    InitialConcentrations = new Dictionary<string, float>
    {
        {"Ca2+", 0.005f},      // mol/L (low initial concentration)
        {"CO3^2-", 0.005f}
    },
    InletConcentrations = new Dictionary<string, float>
    {
        {"Ca2+", 0.02f},       // mol/L (high → supersaturation)
        {"CO3^2-", 0.02f}
    },
    InitialMinerals = new Dictionary<string, float>
    {
        {"Calcite", 0.02f}     // 2% of pore volume
    },

    // Reaction control
    EnableReactions = true,
    UpdateGeometry = true,
    MinPoreRadius = 0.1f,      // voxel
    MinThroatRadius = 0.05f    // voxel
};

// Run simulation with progress reporting
var progress = new Progress<(float, string)>(p =>
{
    Console.WriteLine($"{p.Item1:P0}: {p.Item2}");
});

var results = PNMReactiveTransport.Solve(pnm, options, progress);

// Results
Console.WriteLine($"Initial permeability: {results.InitialPermeability:E3} mD");
Console.WriteLine($"Final permeability: {results.FinalPermeability:E3} mD");
Console.WriteLine($"Change: {results.PermeabilityChange:P2}");
```

### Method 2: GeoScript (simpler!)

Create a file `reaction_example.geoscript`:

```geoscript
# Example: PNM Reactive Transport
# Assume you already have a loaded PNM dataset

# 1. Set chemical species
SET_PNM_SPECIES Ca2+ 0.02 0.005
# Arguments: species_name inlet_concentration(mol/L) initial_concentration(mol/L)

SET_PNM_SPECIES CO3^2- 0.02 0.005

# 2. Set initial minerals (optional)
SET_PNM_MINERALS Calcite 0.02
# Arguments: mineral_name volume_fraction (2% of pores filled with calcite)

# 3. Run simulation
RUN_PNM_REACTIVE_TRANSPORT 3600 1.0 298.15 2.0 0.0
# Arguments: total_time(s) time_step(s) temp_inlet(K) press_inlet(Pa) press_outlet(Pa)

# 4. Export results
EXPORT_PNM_RESULTS ./reaction_results
# Creates:
#   - summary.csv: Overall metrics
#   - time_series.csv: Permeability evolution
#   - final_pores.csv: Final state of all pores
```

Then run the script in the toolkit.

---

## Visualizing Results

### In the GTK interface (GUI)

1. **Open the dataset** after simulation
2. Select the **3D viewport**
3. Choose the **color mode**:
   - **Temperature**: Visualize temperature field
   - **Pressure**: Visualize pressure field
   - **Species Concentration**: Select a species (e.g., Ca²⁺, CO₃²⁻)
   - **Mineral Precipitation**: Select a mineral (e.g., Calcite)
   - **Reaction Rate**: Show where reactions are most active

4. **Interaction**:
   - Click on a cell to see details (T, P, concentrations)
   - Rotate with right mouse
   - Zoom with wheel

### Output Files (PNM)

#### `summary.csv`
```csv
Metric,Value
Initial Permeability (mD),125.5
Final Permeability (mD),98.3
Permeability Change (%),-21.7
Computation Time (s),45.2
Total Iterations,3600
Convergence,True
```

#### `time_series.csv`
```csv
Time (s),Permeability (mD)
0.0,125.5
60.0,123.1
120.0,120.8
...
3600.0,98.3
```

#### `final_pores.csv`
```csv
PoreID,X,Y,Z,Radius,Volume,Pressure,Temperature,Ca2+,CO3^2-,Calcite_Volume
1,10.5,15.2,8.3,2.45,61.2,1.85,298.5,0.0045,0.0042,3.2
2,12.1,14.8,9.1,2.12,39.8,1.78,298.7,0.0048,0.0046,2.1
...
```

---

## Troubleshooting

### Problem: Simulation diverges

**Solutions:**
- Reduce the `TimeStep` (e.g., from 1.0 to 0.1 seconds)
- Verify initial conditions (concentrations not too high)
- Check that material properties are reasonable

```csharp
dataset.SimulationParams.TimeStep = 0.1; // Reduced
```

### Problem: No reaction occurs

**Solutions:**
- Verify that `EnableReactiveTransport = true`
- Check concentrations (must be supersaturated for precipitation)
- Ensure reactants are present in both domains/cells

```csharp
dataset.SimulationParams.EnableReactiveTransport = true;
dataset.SimulationParams.EnableReactions = true;  // For PNM
```

### Problem: Simulation very slow

**Solutions:**
- Increase `TimeStep` (but watch for stability!)
- Reduce mesh resolution
- Enable GPU acceleration if available

```csharp
dataset.SimulationParams.UseGPU = true;
dataset.SimulationParams.TimeStep = 5.0; // Increased
```

### Problem: Output files not found

**Solutions:**
- Verify export path
- Ensure simulation completed
- Check write permissions

```csharp
// Use absolute path
EXPORT_PNM_RESULTS /home/user/results
```

---

## Common Parameters

### Temperatures (K)
- **Ambient**: 298.15 K (25°C)
- **Geothermal**: 350-450 K (77-177°C)
- **Hydrothermal**: 450-600 K (177-327°C)

### Pressures (Pa)
- **Atmospheric**: 101325 Pa (1 atm)
- **Underground (100m)**: ~1 MPa
- **Deep (1km)**: ~10 MPa
- **Geothermal**: 10-50 MPa

### Typical Concentrations (mol/L)
- **Pure water**: 10⁻⁷ (H⁺, OH⁻)
- **Seawater**: 0.035 (NaCl)
- **Aquifers**: 0.001-0.01 (dissolved ions)
- **Brines**: 0.1-5.0 (high salinity)

### Porosity
- **Sandstone**: 0.15-0.30 (15-30%)
- **Limestone**: 0.05-0.20 (5-20%)
- **Fractured**: 0.01-0.05 (1-5%)
- **Permeable**: 0.30-0.50 (30-50%)

---

## Next Steps

1. **Experiment** with parameters (temperatures, concentrations, geometries)
2. **Create custom geometries** (2D profiles, Voronoi meshes)
3. **Add more chemical species** and complex reactions
4. **Explore parametric sweeps** for sensitivity analysis
5. **Couple** with geothermal simulations

---

## References

- **Complete PhysicoChem Guide**: `PHYSICOCHEM_GUIDE.md`
- **PNM Reactive Transport**: `Documentation/PNM_REACTIVE_TRANSPORT.md`
- **GeoScript Manual**: `GEOSCRIPT_MANUAL.md`
- **Examples**: `Examples/PNM_ReactiveTransport_Example.geoscript`

---

**Happy simulating!**

---

**© 2026 GeoscientistToolkit Project**
