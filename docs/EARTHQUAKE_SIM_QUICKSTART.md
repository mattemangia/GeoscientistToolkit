# Earthquake Simulation System - Implementation Quick Start

## Key Insights for Building an Earthquake Simulator

### 1. Architectural Fit

The GeoscientistToolkit has **perfect infrastructure** for earthquake simulation:

- **Stress Analysis Ready**: Geomechanical solver already computes stress tensors
- **3D Grid System**: SubsurfaceGISDataset provides voxel grids for fault representation
- **Geospatial Integration**: GIS layers for epicenter/fault mapping
- **Data Management**: Multi-dataset linking (boreholes → seismic observations)
- **Extensibility**: Established patterns for adding new Analysis modules

### 2. Core Data Structures to Reuse

#### For Fault Geometry:
```csharp
// Option A: Use SubsurfaceGISDataset
SubsurfaceGISDataset faultModel = new SubsurfaceGISDataset(name, path)
{
    GridResolutionX = 100,     // Fault trace resolution
    GridResolutionY = 100,
    GridResolutionZ = 50,      // Depth discretization
    VoxelSize = new Vector3(100, 100, 200),  // meters
    VoxelGrid = /* fault lithology cells */
};

// Option B: Use GISDataset for fault traces
GISDataset faultMap = new GISDataset("Active Faults", "")
{
    Layers = new List<GISLayer>
    {
        new GISLayer 
        { 
            Name = "Fault Lines",
            Type = LayerType.Vector,
            Features = /* Fault LineString features with dip/rake/moment */
        }
    }
};
```

#### For Stress State:
```csharp
// Leverage GeomechanicalResults structure
public class EarthquakeSimulationResults
{
    // From GeomechanicalResults (reuse):
    public float[,,] StressXX { get; set; }
    public float[,,] StressYY { get; set; }
    public float[,,] StressZZ { get; set; }
    public float[,,] StressXY { get; set; }
    
    // Earthquake-specific additions:
    public float[,,] CoulombStress { get; set; }  // σ_n + μ(τ - Δσ)
    public float[,,] SlipDisplacement { get; set; }  // Rupture slip (m)
    public bool[,,] RupturedCells { get; set; }  // Cells that failed
    
    // Seismic moment & magnitude
    public float SeismicMoment { get; set; }  // M_0 (dyne-cm)
    public float MagnitudeLocal { get; set; }  // M_L
    public float MomentMagnitude { get; set; }  // M_w
}
```

#### For Earthquake Catalog:
```csharp
// Use TableDataset to store seismic catalog
TableDataset catalog = new TableDataset("Earthquake Catalog", "catalog.csv")
{
    // Columns:
    // Time (datetime)
    // Latitude, Longitude, Depth (km)
    // Magnitude (Mw)
    // Magnitude_Local (ML)
    // SeismicMoment (dyne-cm)
    // Strike (degrees), Dip, Rake
    // MainShock (boolean)
    // EventType (MainShock, Foreshock, Aftershock)
};
```

### 3. Implementation Structure

Create new module: `/Analysis/EarthquakeSimulation/`

```
EarthquakeSimulation/
├── EarthquakeSimulationParameters.cs    (Input configuration)
├── EarthquakeSimulationResults.cs       (Output data)
├── EarthquakeSimulationCPU.cs           (CPU solver, 2000-3000 LOC)
├── EarthquakeSimulationGPU.cs           (OpenCL solver, optional)
├── EarthquakeSimulationUI.cs            (ImGui interface)
├── RuptureModel.cs                      (Shear crack dynamics)
├── CoulombStressCalculator.cs           (Failure criterion)
├── GroundMotionModel.cs                 (Attenuation relations)
└── EarthquakeDataset.cs                 (New Dataset type)
```

### 4. Data Flow

```
Project Loads
    ↓
MainWindow displays GISDataset (Fault Map)
    ↓
User selects fault + defines hypocenter
    ↓
EarthquakeSimulationUI opens with parameters
    ├── Fault geometry (from GISDataset)
    ├── Stress state (from GeomechanicalResults)
    ├── Material properties (from MaterialLibrary)
    └── Earthquake parameters (magnitude, depth, rake)
    ↓
EarthquakeSimulationCPU::Simulate()
    ├── Compute Coulomb stress on fault
    ├── Solve rupture nucleation
    ├── Propagate rupture in 3D
    ├── Calculate seismic moment release
    └── Generate synthetic catalog (if stochastic)
    ↓
Results stored in EarthquakeDataset
    ├── SlipDisplacement[,,] field
    ├── CoulombStress[,,] field
    ├── RupturedCells[,,] mask
    └── EarthquakeDataset.EventCatalog (TableDataset)
    ↓
Visualization:
    ├── GISViewer: Epicenters on fault map
    ├── Volume viewer: Slip distribution in 3D
    ├── TableViewer: Event catalog
    └── Stereogram: Focal mechanism (strike/dip/rake)
```

### 5. Key Integration Points

#### A. DatasetType Enum (Data/Dataset.cs)
```csharp
public enum DatasetType
{
    // ... existing types ...
    EarthquakeSimulation,  // ADD THIS
}
```

#### B. EarthquakeDataset Class (Data/Earthquake/EarthquakeDataset.cs)
```csharp
public class EarthquakeDataset : Dataset
{
    public EarthquakeDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.EarthquakeSimulation;
    }
    
    // Simulation outputs
    public EarthquakeSimulationResults Results { get; set; }
    
    // Associated geometry
    public GISDataset FaultGeometry { get; set; }
    public SubsurfaceGISDataset VelocityModel { get; set; }
    
    // Catalog of events
    public TableDataset EventCatalog { get; set; }
    
    public override long GetSizeInBytes() { /* ... */ }
    public override void Load() { /* ... */ }
    public override void Unload() { /* ... */ }
}
```

#### C. MainWindow Registration (UI/MainWindow.cs)
```csharp
// In SubmitUI() method, add viewer factory:
if (_selectedDataset is EarthquakeDataset earthquakeDs)
{
    var viewer = new EarthquakeViewer(earthquakeDs);
    _viewers.Add(viewer);
}
```

#### D. Tools Integration (UI/DatasetUIFactory.cs)
```csharp
case DatasetType.EarthquakeSimulation:
    return new EarthquakeSimulationUI();
```

#### E. GeoScript Commands (Business/GeoScriptExtensions.cs)
```geoscript
// New commands:
SIMULATE_EARTHQUAKE 
    FAULT 'fault_gis_dataset'
    MAGNITUDE 7.5
    DEPTH 15  -- km
    HYPOCENTER_LAT -33.5 HYPOCENTER_LON 150.2
    RAKE 90  -- degrees (90=thrust, 0=strike-slip)

COMPUTE_SEISMIC_HAZARD
    CATALOG 'catalog_dataset'
    GRID 0.1  -- 0.1 degree resolution
    TIME_WINDOW 100  -- years
```

### 6. Mathematical Framework

#### Coulomb Stress Change (Key Failure Criterion)
```
Δσ_c = Δτ + μ(Δσ_n - ΔP)

Where:
- Δτ = change in shear stress on fault
- Δσ_n = change in normal stress on fault
- ΔP = change in pore pressure
- μ = coefficient of friction (0.4 typical)

Failure if: Δσ_c > threshold (typically 0.1 MPa)
```

#### Seismic Moment (Earthquake Size)
```
M_0 = μ × A × D

Where:
- μ = rigidity (shear modulus, GPa)
- A = fault rupture area (m²)
- D = average slip (m)

Moment Magnitude: M_w = 2/3 × log10(M_0) - 10.7
```

#### Wave Equation (for Ground Motion)
```
∂²u/∂t² = c² ∇²u

Where:
- u = displacement
- c = P/S wave velocity
- Solve via FEM on structured grid
```

### 7. Performance Considerations

#### CPU Implementation Strategy
- **SIMD-optimized**: Use `Vector<float>` for parallel stress calculations
- **Multi-threaded**: Process fault cells concurrently
- **Streaming**: Large catalogs via database backend

#### GPU Implementation Strategy
- **OpenCL kernels** for:
  - Coulomb stress tensor computation (embarrassingly parallel)
  - Rupture front propagation (stencil operation)
  - Moment magnitude calculation (reduction operation)
- Typical speedup: 10-100x vs CPU for large grids

### 8. Validation & Testing

#### Real Data Integration
```csharp
// Link to real seismic network data
BoreholeDataset seismoMeter = /* load accelerometer borehole */;
earthquakeDataset.ValidateAgainstObservedMoments(seismoMeter);
```

#### Comparison with Known Events
```csharp
// Test against historical earthquakes
// e.g., 2011 Christchurch (Mw 6.2) or Tohoku (Mw 9.1)
// Validate: magnitude, focal mechanism, ground motion

double computedMoment = earthquakeResults.SeismicMoment;
double observedMoment = 10^(1.5 * Mw + 4.8);  // From catalog
float error = Math.Abs(computedMoment - observedMoment) / observedMoment;
// Goal: < 10% error
```

### 9. Next Steps Priority

#### Phase 1 (Week 1-2): Core Data Structures
1. Create `EarthquakeDataset` class
2. Define `EarthquakeSimulationParameters`
3. Define `EarthquakeSimulationResults`
4. Add to `DatasetType` enum

#### Phase 2 (Week 2-4): CPU Solver
1. Implement Coulomb stress calculator
2. Implement rupture nucleation
3. Implement 3D rupture propagation
4. Add unit tests with known solutions

#### Phase 3 (Week 4-5): UI & Visualization
1. Create `EarthquakeSimulationUI` (ImGui interface)
2. Create `EarthquakeViewer` (3D visualization)
3. Register in `DatasetUIFactory`
4. Add to `MainWindow`

#### Phase 4 (Week 5-6): GeoScript & Export
1. Add GeoScript commands
2. Implement catalog export (CSV, GeoJSON)
3. Add uncertainty quantification
4. Integration testing

#### Phase 5 (Week 6+): GPU & Advanced Features
1. OpenCL GPU implementation
2. Stochastic catalog generation (ETAS model)
3. Time-dependent hazard
4. Performance optimization

### 10. References & Libraries

#### Seismology Theory
- Kanamori & Brodsky (2004): The Physics of Earthquakes
- Lay & Wallace (1995): Modern Global Seismology
- Aki & Richards (2002): Quantitative Seismology

#### Numerical Methods
- Moczo et al. (2007): The Finite Difference Method in Seismology
- Chaljub et al. (2007): 3-D Numerical Simulations of Earthquake Ground Motion
- Komatitsch & Tromp (1999): Introduction to the spectral element method for 3-D seismic wave propagation

#### Earthquake Models
- Okada (1992): Internal deformation due to shear and tensile faults in a half-space
- Harris (1998): Introduction to Special Section: Stress Transfer, Earthquake Triggering, and Seismic Hazard
- Gutenberg & Richter (1956): Magnitude and Energy of Earthquakes

#### Libraries to Consider
- NetCDF.NET for seismic data I/O
- NLopt for optimization (parameter estimation)
- MathNet.Numerics (already available)

---

**Document Location**: `/home/user/GeoscientistToolkit/EARTHQUAKE_SIM_QUICKSTART.md`

Ready to implement earthquake simulation!
