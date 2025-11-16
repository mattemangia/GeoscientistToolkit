# Earthquake Simulator - Subsurface GIS Integration Changelog

## Summary

Enhanced the GeoscientistToolkit with earthquake-subsurface GIS integration, allowing visualization of seismic wave propagation in 3D subsurface models, and added functionality to create 3D geological models from 2D map selections.

## Changes Made

### 1. Bug Fixes

#### Fixed Critical Bug in WavePropagationEngine.cs
- **File**: `Analysis/Seismology/WavePropagationEngine.cs`
- **Lines**: 203-211
- **Issue**: Stress divergence calculation was incorrect
  - The acceleration calculation reused `dsigma_xx_dx` for all three components
  - This violated the physics of elastic wave propagation
- **Fix**: Properly compute stress divergence using centered finite differences on strain rates
- **Impact**: Significantly improved accuracy of seismic wave simulations

### 2. New Files Created

#### EarthquakeSubsurfaceIntegration.cs
- **Location**: `Analysis/Seismology/EarthquakeSubsurfaceIntegration.cs`
- **Purpose**: Integration layer between earthquake simulator and subsurface GIS
- **Key Features**:
  - Create 3D subsurface datasets from earthquake simulation results
  - Track seismic wave evolution over time in the subsurface
  - Extract time series data at specific locations
  - Generate depth slice layers for visualization
  - Export seismic voxel data with peak ground motion parameters

#### SubsurfaceGISBuilder.cs
- **Location**: `Data/GIS/SubsurfaceGISBuilder.cs`
- **Purpose**: Build 3D subsurface geological models from 2D geological maps
- **Key Features**:
  - Create 3D geology from rectangle selection on 2D maps
  - Support for multiple depth layering models:
    - Uniform: Same lithology throughout
    - LayeredWithTransitions: Realistic stratigraphic layers
    - WeatheredToFresh: Weathering profile
    - SedimentarySequence: Typical sedimentary sequence
  - Automatic property assignment (porosity, permeability, density)
  - Integration with heightmaps for elevation
  - Support for both raster and polygon geological maps

#### Documentation
- **File**: `docs/EARTHQUAKE_SUBSURFACE_INTEGRATION.md`
- **Content**: Comprehensive guide with examples, API documentation, and use cases

## New Data Structures

### SeismicVoxelData
- Stores time-series seismic data at a point
- Includes displacement, velocity, acceleration histories
- Tracks peak values and arrival times

### Enhanced SubsurfaceVoxel
- Now supports both geological and seismic properties
- Extended parameters for earthquake data:
  - PGA, PGV, PGD (peak ground motion)
  - Wave arrival times and amplitudes
  - Damage ratios and MMI values
  - Fracture density
  - Distance from hypocenter

### DepthLayeringModel Enum
- Defines how lithology extends with depth
- Four models: Uniform, LayeredWithTransitions, WeatheredToFresh, SedimentarySequence

## Features

### Earthquake-Subsurface Integration

1. **3D Visualization of Earthquake Propagation**
   - Visualize seismic waves moving through 3D subsurface
   - Track wave evolution over time
   - Show peak ground motion at different depths

2. **Time Series Extraction**
   - Extract seismograms at any location
   - Get displacement, velocity, acceleration histories
   - Analyze wave arrival times and peak amplitudes

3. **Depth Slice Layers**
   - Create 2D layers at different depths
   - Visualize how seismic parameters vary with depth
   - Support for any parameter (amplitude, PGA, damage, etc.)

### 3D Geology from 2D Maps

1. **Rectangle Selection Tool**
   - Draw rectangle on 2D geological map
   - Automatically create 3D subsurface model
   - Extend lithology to depth using various models

2. **Smart Property Assignment**
   - Automatically assign geological properties
   - Default values for common lithologies
   - Depth-dependent properties (pressure, temperature)

3. **Flexible Depth Models**
   - Choose how lithology extends with depth
   - Weathering profiles
   - Sedimentary sequences
   - Custom layer transitions

## Use Cases

### Seismic Hazard Assessment
- Visualize earthquake propagation through geological structure
- Identify amplification zones
- Assess damage to underground infrastructure

### Geotechnical Engineering
- Design foundations and underground structures
- Assess liquefaction potential in 3D
- Understand subsurface response to earthquakes

### Resource Exploration
- Map earthquake-induced fractures
- Understand stress changes in reservoirs
- Design microseismic monitoring

### Education and Research
- Teach earthquake physics with 3D visualizations
- Research wave propagation in heterogeneous media
- Validate seismic models

## API Examples

### Create Seismic Subsurface Dataset
```csharp
var subsurfaceDataset = EarthquakeSubsurfaceIntegration.CreateSeismicSubsurfaceDataset(
    engine, parameters, results,
    subsurfaceResolutionX: 50,
    subsurfaceResolutionY: 50,
    subsurfaceResolutionZ: 30
);
```

### Create 3D Geology from 2D Map
```csharp
var subsurface3D = SubsurfaceGISBuilder.CreateFrom2DGeologicalMap(
    geologicalMap: geologicalMap,
    rectangle: selectionRectangle,
    depthRange: (0f, 1000f),
    heightmap: heightmap,
    layerThicknessModel: DepthLayeringModel.LayeredWithTransitions
);
```

### Extract Seismic Time Series
```csharp
var timeSeries = EarthquakeSubsurfaceIntegration.ExtractSeismicTimeSeries(
    location, parameters, results
);
```

## Testing

Manual testing performed on:
- Earthquake simulation parameter validation
- Subsurface dataset creation from earthquake results
- 3D geology generation from 2D maps
- Time series extraction

## Future Enhancements

Potential improvements:
- Real-time visualization of wave propagation
- GPU acceleration for large grids
- Advanced interpolation methods (kriging, splines)
- Integration with real seismic catalogs
- Machine learning for lithology prediction
- Multi-event earthquake sequences

## Dependencies

No new external dependencies added. Uses existing GeoscientistToolkit infrastructure:
- GIS framework
- Borehole datasets
- Vector and matrix utilities
- Logging system

## Performance Considerations

- Memory usage scales with grid resolution (NX × NY × NZ)
- Time snapshot storage can be significant for long simulations
- 3D geology generation is parallelizable (future optimization)
- Recommend starting with lower resolutions for testing

## Breaking Changes

None. All changes are additive and backward compatible.

## Documentation

- `EARTHQUAKE_SUBSURFACE_INTEGRATION.md`: Complete guide with examples
- Inline code documentation with XML comments
- Algorithm references in comments
