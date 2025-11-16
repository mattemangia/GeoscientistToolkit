# Earthquake Simulator - Subsurface GIS Integration

This document describes the integration between the earthquake simulator and subsurface GIS datasets, allowing visualization of seismic wave propagation in 3D subsurface models.

## Features

### 1. Bug Fixes

**Fixed Critical Bug in WavePropagationEngine**
- **Location**: `Analysis/Seismology/WavePropagationEngine.cs` lines 203-211
- **Issue**: Stress divergence calculation was incorrect, reusing the same value for all three acceleration components
- **Fix**: Properly compute the divergence of the stress tensor using centered finite differences on strain rates
- **Impact**: More accurate seismic wave propagation simulations

### 2. Earthquake-Subsurface GIS Integration

Create 3D subsurface visualizations of earthquake wave propagation showing:
- Wave displacement, velocity, and acceleration evolution over time
- Peak ground motion parameters (PGA, PGV, PGD) in 3D
- Wave arrival times at different depths
- Fracture density distribution in the subsurface
- Stress distribution throughout the volume

#### Example Usage

```csharp
using GeoscientistToolkit.Analysis.Seismology;
using GeoscientistToolkit.Data.GIS;

// Set up earthquake simulation
var parameters = new EarthquakeSimulationParameters
{
    EpicenterLatitude = 37.0,
    EpicenterLongitude = -122.0,
    HypocenterDepthKm = 10.0,
    MomentMagnitude = 6.5,
    MinLatitude = 36.5,
    MaxLatitude = 37.5,
    MinLongitude = -122.5,
    MaxLongitude = -121.5,
    MaxDepthKm = 50.0,
    GridNX = 100,
    GridNY = 100,
    GridNZ = 50,
    SimulationDurationSeconds = 60.0,
    SaveWaveSnapshots = true,  // Required for time evolution
    SnapshotIntervalSteps = 100
};

// Run simulation
var engine = new EarthquakeSimulationEngine(parameters);
engine.Initialize();
var results = engine.Run();

// Create 3D subsurface GIS dataset from results
var subsurfaceDataset = EarthquakeSubsurfaceIntegration.CreateSeismicSubsurfaceDataset(
    engine,
    parameters,
    results,
    subsurfaceResolutionX: 50,
    subsurfaceResolutionY: 50,
    subsurfaceResolutionZ: 30
);

// Save the 3D dataset
subsurfaceDataset.SaveToFile("earthquake_3d_subsurface.subgis");

// Create depth slice layers for visualization
var depthLayers = EarthquakeSubsurfaceIntegration.CreateDepthSliceLayers(
    subsurfaceDataset,
    parameterName: "P_Wave_Amplitude_m",
    numberOfDepthSlices: 5
);

// Extract time series at a specific location
var location = new Vector3(-122.0f, 37.0f, -5000.0f); // lon, lat, depth (m)
var timeSeries = EarthquakeSubsurfaceIntegration.ExtractSeismicTimeSeries(
    location,
    parameters,
    results
);

Console.WriteLine($"Peak displacement: {timeSeries.PeakDisplacement:F4} m");
Console.WriteLine($"Peak velocity: {timeSeries.PeakVelocity:F4} m/s");
Console.WriteLine($"Time of peak arrival: {timeSeries.TimeOfPeakArrival:F2} s");
```

### 3. 3D Geology from 2D Rectangle Selection

Create 3D subsurface geological models from rectangular selections on 2D geological maps.

#### Features

- **Multiple Depth Layering Models**:
  - `Uniform`: Same lithology throughout depth
  - `LayeredWithTransitions`: Realistic layered geology with transitions
  - `WeatheredToFresh`: Weathered surface transitioning to fresh bedrock
  - `SedimentarySequence`: Typical sedimentary stratigraphic sequence

- **Automatic Property Assignment**: Automatically assigns geological properties (porosity, permeability, density) based on lithology

- **Elevation Integration**: Can use heightmaps for realistic surface topography

#### Example Usage

```csharp
using GeoscientistToolkit.Data.GIS;
using System.Numerics;

// Load 2D geological map
var geologicalMap = GISDataset.LoadFromFile("geological_map.shp") as GISPolygonLayer;

// Define rectangle of interest (e.g., user draws on map)
var rectangle = new BoundingBox
{
    Min = new Vector3(-122.5f, 36.5f, 0),
    Max = new Vector3(-121.5f, 37.5f, 0)
};

// Optional: Load heightmap for elevation
var heightmap = GISDataset.LoadFromFile("elevation.tif") as GISRasterLayer;

// Create 3D subsurface model from 2D map
var subsurface3D = SubsurfaceGISBuilder.CreateFrom2DGeologicalMap(
    geologicalMap: geologicalMap,
    rectangle: rectangle,
    depthRange: (minDepth: 0f, maxDepth: 1000f), // 0-1000m depth
    heightmap: heightmap,
    resolutionX: 50,
    resolutionY: 50,
    resolutionZ: 30,
    layerThicknessModel: DepthLayeringModel.LayeredWithTransitions
);

// Save 3D model
subsurface3D.SaveToFile("geology_3d.subgis");

// Export to CSV for external analysis
subsurface3D.ExportVoxelsToCsv("geology_3d_voxels.csv");

// Inspect voxels
foreach (var voxel in subsurface3D.VoxelGrid.Take(10))
{
    Console.WriteLine($"Position: {voxel.Position}");
    Console.WriteLine($"Lithology: {voxel.LithologyType}");
    Console.WriteLine($"Depth: {voxel.Parameters["Depth_m"]:F1} m");
    Console.WriteLine($"Porosity: {voxel.Parameters["Porosity"]:F3}");
    Console.WriteLine($"Temperature: {voxel.Parameters["Estimated_Temperature_C"]:F1} °C");
    Console.WriteLine();
}
```

## Data Structures

### SeismicVoxelData

Contains time-series seismic data at a point:
- `Position`: 3D location (X=longitude, Y=latitude, Z=elevation/depth)
- `DisplacementHistory`: Array of displacement values over time
- `VelocityHistory`: Array of velocity values over time
- `AccelerationHistory`: Array of acceleration values over time
- `StressHistory`: Array of stress values over time
- `PeakDisplacement/Velocity/Acceleration/Stress`: Maximum values
- `TimeOfPeakArrival`: Time when peak amplitude arrived

### SubsurfaceVoxel (Enhanced)

3D voxel with geological or seismic properties:
- `Position`: 3D coordinates
- `LithologyType`: Name of lithology or "Seismic" for earthquake data
- `Parameters`: Dictionary of properties:
  - For geology: `Porosity`, `Permeability_mD`, `Density_g_cm3`, `Depth_m`, `Temperature_C`, etc.
  - For seismic: `PGA_g`, `PGV_cm_s`, `P_Wave_Amplitude_m`, `Damage_Ratio`, `Fracture_Density`, etc.
- `Confidence`: 0-1 confidence value

## Visualization

### Recommended Workflow

1. **Run Earthquake Simulation**
   - Configure parameters with `SaveWaveSnapshots = true`
   - Run simulation to get results

2. **Create Subsurface Dataset**
   - Use `EarthquakeSubsurfaceIntegration.CreateSeismicSubsurfaceDataset()`
   - This creates a 3D voxel grid with seismic properties

3. **Visualize Depth Slices**
   - Use `CreateDepthSliceLayers()` to create 2D slices at different depths
   - Visualize parameters like wave amplitude, PGA, fracture density

4. **Extract Time Series**
   - Use `ExtractSeismicTimeSeries()` to get waveforms at specific locations
   - Analyze wave arrival, peak amplitudes, durations

5. **Combine with Geology**
   - Create 3D geology model using `SubsurfaceGISBuilder`
   - Overlay seismic data on geological structure
   - Analyze how lithology affects wave propagation

## Applications

### Seismic Hazard Assessment
- Visualize how earthquakes propagate through complex 3D geology
- Identify areas of amplified shaking due to geological structure
- Assess damage potential at different depths (underground infrastructure)

### Geotechnical Engineering
- Understand subsurface response to earthquakes
- Design foundations and underground structures
- Assess liquefaction potential in 3D

### Resource Exploration
- Use earthquake-induced fractures to map reservoir connectivity
- Understand stress changes in geothermal or hydrocarbon reservoirs
- Design microseismic monitoring networks

### Education and Research
- Teach earthquake physics with 3D visualizations
- Research wave propagation in heterogeneous media
- Validate seismic codes and building standards

## Performance Notes

- **Subsurface Resolution**: Higher resolution (e.g., 100x100x50) provides more detail but requires more memory
- **Time Snapshots**: More snapshots provide better temporal resolution but increase storage
- **Grid Size**: Earthquake simulations scale with grid size; use smaller domains for testing

## References

- Komatitsch & Vilotte (1998) - Spectral element method for seismic wave propagation
- Aki & Richards (2002) - Quantitative Seismology
- HAZUS-MH methodology for damage assessment
