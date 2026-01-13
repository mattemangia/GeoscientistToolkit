# Slope Stability Analysis System

## Overview

Complete 3D slope stability analysis system based on the Discrete Element Method (DEM). This system provides advanced capabilities for analyzing landslides and rock slope failures on LIDAR-scanned terrain.

## Features

### Core Capabilities

- **Block Generation**: Automatic generation of discrete blocks from 3D meshes using Discrete Fracture Network (DFN) methodology
- **Joint Sets**: Define and apply geological discontinuities with full mechanical properties
- **Multiple Constitutive Models**: Elastic, Plastic, Brittle, Elasto-Plastic, and Visco-Elastic behavior
- **Failure Criteria**: Mohr-Coulomb, Hoek-Brown, Drucker-Prager, Griffith, Von Mises, and Tresca
- **Earthquake Loading**: Realistic seismic wave propagation with epicenter and intensity control
- **Physics Simulation**: SIMD-optimized multithreaded DEM with ARM NEON support
- **Advanced Visualization**:
  - 3D viewer with color mapping, displacement vectors, and animation
  - 2D section viewer with cross-sectional views (along-strike, along-dip, plan view, custom)
- **GeoScript Integration**: Full programmatic control via scripting language
- **Import/Export**: Import external simulation results and export in multiple formats

### Key Differentiators from commercial DEM tools

1. **Final State Visualization**: Shows where blocks actually fall and settle
2. **Real-time Animation**: Interactive timeline to visualize progressive failure
3. **Color Mapping**: Advanced visualization of displacement, velocity, stress, and other parameters
4. **Modern Architecture**: Built on .NET 8 with cross-platform support (Windows, Linux, macOS)
5. **Open Integration**: Easy integration with other geoscientific tools in the toolkit

## Architecture

### Data Structures

- **Block**: Rigid polyhedral block with geometric, physical, and state properties
- **JointSet**: Geological discontinuity set with orientation, spacing, and mechanical properties
- **ContactInterface**: Contact between blocks with friction, cohesion, and sliding behavior
- **SlopeStabilityMaterial**: Material properties with constitutive model
- **SlopeStabilityParameters**: Simulation parameters (time, damping, boundary conditions)
- **SlopeStabilityResults**: Complete results with block displacements, contacts, and statistics

### Physics Engine

The simulator implements a full Discrete Element Method:

1. **Contact Detection**:
   - Broad-phase: Spatial hash grid for O(n log n) contact detection
   - Narrow-phase: GJK (Gilbert-Johnson-Keerthi) and EPA (Expanding Polytope Algorithm) for accurate convex hull collision detection
2. **Force Calculation**:
   - Gravity
   - Normal and shear contact forces
   - Earthquake loading
   - Fluid pressure (pore pressure)
3. **Time Integration**: Velocity Verlet scheme for stable integration with full rigid body dynamics (including rotational dynamics with inertia tensor inversion)
4. **Constitutive Models**: Elastic, plastic, and brittle behavior with damage evolution
5. **Optimization**: SIMD vectorization and multithreading for maximum performance

### Block Generation Algorithm

Based on distinct-element block modeling methodology:

1. **Input**: 3D mesh (OBJ, STL) + Joint sets
2. **DFN Application**: Joint planes split mesh into discrete blocks
3. **Sorting**: Joints sorted by spacing (larger first to minimize cuts)
4. **Cutting**: Sequential cutting with plane-mesh intersection
5. **Post-processing**: Remove small blocks, merge slivers, calculate properties

## Usage

### GUI Workflow

1. **Import Mesh**: Load LIDAR scan or geological model (OBJ/STL format)
2. **Define Joint Sets**: Add discontinuities with dip, dip direction, spacing, and mechanical properties
3. **Generate Blocks**: Automatic block generation using DFN algorithm
4. **Assign Materials**: Set material properties for blocks (presets available: Granite, Limestone, etc.)
5. **Configure Simulation**: Set time parameters, boundary conditions, damping
6. **Optional - Add Earthquake**: Define seismic loading with magnitude and epicenter
7. **Run Simulation**: Execute DEM simulation with progress monitoring
8. **Visualize Results**:
   - **3D View**: Complete slope geometry with blocks, joints, and simulation results
   - **2D Section View**: Cross-sectional views for detailed analysis
9. **Export Results**: Save to CSV, VTK, or JSON for further analysis

### 2D Section Viewer

The 2D section viewer provides professional cross-sectional analysis similar to specialized commercial tools:

**Features:**
- Multiple predefined section planes:
  - Along-Strike: Perpendicular to slope dip direction
  - Along-Dip: Parallel to slope dip direction
  - Plan View: Horizontal section through slope
  - Custom: User-defined orientation
- Joint trace visualization showing discontinuity intersections with section plane
- Displacement vector display projected onto section
- Color mapping (displacement, velocity, material, safety factor, stress)
- Water table visualization
- Interactive navigation (pan, zoom)
- Grid and axis display

**Usage:**
1. Navigate to the "Views" tab in the tools panel
2. Click "Open 2D Section Viewer"
3. Select section plane from dropdown
4. Configure display options (show blocks, joints, vectors, etc.)
5. Use mouse wheel to zoom, right-click to pan
6. Edit custom section plane with origin and normal vector

### GeoScript Examples

```geoscript
# Load mesh and create slope analysis
mesh.obj |> SLOPE_GENERATE_BLOCKS target_size=1.5 remove_small=true

# Add joint sets (geological discontinuities)
|> SLOPE_ADD_JOINT_SET dip=45 dip_dir=90 spacing=1.0 friction=30 cohesion=0.5 name="Main Fracture"
|> SLOPE_ADD_JOINT_SET dip=60 dip_dir=270 spacing=2.0 friction=35 cohesion=1.0 name="Secondary"

# Assign material properties
|> SLOPE_SET_MATERIAL preset=granite

# Add earthquake trigger
|> SLOPE_ADD_EARTHQUAKE magnitude=6.0 epicenter_x=100 epicenter_y=50 start_time=2.0

# Run simulation
|> SLOPE_SIMULATE time=20.0 timestep=0.0005 mode=dynamic threads=8

# Export results
|> SLOPE_EXPORT path="landslide_results.csv" format=csv
```

### Programmatic API

```csharp
// Create dataset from mesh
var mesh = new Mesh3DDataset("lidar_scan.obj");
mesh.Load();

var slopeDataset = new SlopeStabilityDataset
{
    Name = "Landslide Analysis",
    SourceMeshPath = "lidar_scan.obj"
};

// Add joint set
var jointSet = new JointSet
{
    Name = "Main Discontinuity",
    Dip = 45.0f,
    DipDirection = 90.0f,
    Spacing = 1.0f,
    FrictionAngle = 30.0f,
    Cohesion = 0.5e6f  // 0.5 MPa
};
slopeDataset.JointSets.Add(jointSet);

// Generate blocks
var generator = new BlockGenerator(slopeDataset.BlockGenSettings);
slopeDataset.Blocks = generator.GenerateBlocks(mesh, slopeDataset.JointSets);

// Configure simulation
slopeDataset.Parameters.TotalTime = 20.0f;
slopeDataset.Parameters.TimeStep = 0.001f;
slopeDataset.Parameters.Mode = SimulationMode.Dynamic;
slopeDataset.Parameters.UseMultithreading = true;

// Add earthquake
var earthquake = EarthquakeLoad.CreatePreset(6.0f, new Vector3(100, 50, 0));
earthquake.StartTime = 2.0f;
slopeDataset.Parameters.EarthquakeLoads.Add(earthquake);
slopeDataset.Parameters.EnableEarthquakeLoading = true;

// Run simulation
var simulator = new SlopeStabilitySimulator(slopeDataset, slopeDataset.Parameters);
var results = simulator.RunSimulation(
    progress => Console.WriteLine($"Progress: {progress * 100:F0}%"),
    status => Console.WriteLine(status));

// Analyze results
Console.WriteLine($"Max displacement: {results.MaxDisplacement:F4} m");
Console.WriteLine($"Failed blocks: {results.NumFailedBlocks}/{results.BlockResults.Count}");
Console.WriteLine($"Computation time: {results.ComputationTimeSeconds:F2} s");
```

## Configuration

### Simulation Parameters

- **Time Step**: Typically 0.001s (1ms). Automatically estimated from stiffness and mass
- **Total Time**: Duration to simulate (e.g., 10-20 seconds for dynamic analysis)
- **Damping**: Local damping (0.05) and optional viscous damping for quasi-static
- **Boundary Conditions**: Fixed base, fixed sides, or custom DOF constraints
- **Simulation Mode**:
  - Dynamic: Full inertial analysis
  - Quasi-Static: Strong damping for equilibrium
  - Static: Strength reduction method
- **Gravity Magnitude**: Configurable gravitational acceleration (default: 9.81 m/s²)

### Custom Gravitational Acceleration

The simulation supports custom gravity for different celestial bodies or specialized scenarios:

| Celestial Body | Gravity (m/s²) |
|----------------|---------------|
| Earth | 9.81 |
| Moon | 1.62 |
| Mars | 3.72 |
| Venus | 8.87 |

**Setting Custom Gravity:**

```csharp
// Set gravity magnitude (direction is automatically calculated based on slope angle)
parameters.SetGravityMagnitude(1.62f); // Moon gravity

// Or set the full gravity vector directly
parameters.UseCustomGravityDirection = true;
parameters.Gravity = new Vector3(0, 0, -3.72f); // Mars gravity
```

**GeoScript:**

```geoscript
# Set gravity magnitude
SLOPE_SET_GRAVITY magnitude=1.62

# Or set custom gravity vector
SLOPE_SET_GRAVITY x=0 y=0 z=-3.72 custom=true
```

### Material Properties

Each material requires:

- Density (kg/m³)
- Young's Modulus (Pa)
- Poisson's Ratio
- Cohesion (Pa)
- Friction Angle (degrees)
- Tensile Strength (Pa)

Presets available: Granite, Limestone, Sandstone, Shale, Clay, Sand, Weathered Rock, Basalt

### Joint Set Properties

- **Orientation**: Dip (0-90°) and Dip Direction (0-360°)
- **Spacing**: Distance between parallel joints (meters)
- **Mechanical**:
  - Normal Stiffness (kn): Pa/m
  - Shear Stiffness (ks): Pa/m
  - Friction Angle (φ): degrees
  - Cohesion (c): Pa
  - Tensile Strength (σt): Pa
  - Dilation Angle (ψ): degrees

### Earthquake Configuration

- **Magnitude**: Moment magnitude (Mw), typically 3-9
- **Epicenter**: 3D coordinates (x, y, z) in meters
- **Depth**: Focal depth in meters
- **Time Function**: Sine, Ricker wavelet, Stochastic, Kanai-Tajimi
- **Automatically Calculated**: PGA, PGV, PGD based on magnitude

## Performance

### Optimization

- **SIMD**: Automatic vectorization using System.Numerics.Vector
- **Multithreading**: Parallel contact detection and force calculation
- **ARM NEON**: Conditional compilation for ARM processors
- **Spatial Hashing**: O(n log n) contact detection instead of O(n²)

### Benchmark

Typical performance on modern hardware:

- 1000 blocks: ~50 ms/step (20 FPS)
- 10000 blocks: ~500 ms/step (2 FPS)
- 100000 blocks: ~5000 ms/step (0.2 FPS)

Performance scales approximately linearly with number of blocks (with spatial hashing).

## Export Formats

### CSV Format

Headers: BlockID, InitialX, InitialY, InitialZ, FinalX, FinalY, FinalZ, DisplacementX, DisplacementY, DisplacementZ, DisplacementMag, VelocityX, VelocityY, VelocityZ, VelocityMag, Mass, IsFixed, HasFailed

### VTK Format (ParaView)

POLYDATA format with:
- Points: Final block positions
- Scalars: Displacement magnitude
- Vectors: Displacement vectors

### JSON Format

Complete dataset serialization including blocks, materials, parameters, and results.

## Limitations and Future Work

### Current Limitations

1. **Block Geometry**: Convex hull approximation during splitting (full CSG would be better)
2. **Fluid Flow**: Simplified pore pressure (no full hydro-mechanical coupling)
3. **Deformability**: Blocks are rigid (could add FEM for deformable blocks)

### Future Enhancements

1. **GPU Acceleration**: CUDA/OpenCL for 100x speedup on large models
2. **Adaptive Time Stepping**: Automatic dt adjustment for stability
3. **Advanced Contact Models**: Non-linear, rate-dependent, temperature effects
4. **Coupling**: Full hydro-mechanical-thermal coupling
5. **Visualization**: Real-time GPU rendering with Veldrid shaders

## References

### Distinct-Element Method Documentation

- Standard DEM references and block-modeling theory (see academic papers below)

### Academic Papers

- Cundall, P.A. (1971). "A computer model for simulating progressive large-scale movements in blocky rock systems"
- Jing, L., & Hudson, J.A. (2002). "Numerical methods in rock mechanics". International Journal of Rock Mechanics and Mining Sciences.
- Lisjak, A., & Grasselli, G. (2014). "A review of discrete modeling techniques for fracturing processes in discontinuous rock masses". Journal of Rock Mechanics and Geotechnical Engineering.

### Related Software

- Commercial and academic DEM/rock mechanics tools

## License

Part of GeoscientistToolkit - See main project license.

## Support

For issues, questions, or contributions, please refer to the main GeoscientistToolkit repository.
