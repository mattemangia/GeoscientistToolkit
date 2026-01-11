# Seismic Analysis

Guide for seismic data processing, visualization, and earthquake simulation in Geoscientist's Toolkit.

---

## Overview

The Seismic Analysis module provides:
- SEG-Y file loading and visualization
- Trace analysis and display modes
- **3D Seismic Cube creation from intersecting 2D lines**
- **Perpendicular line insertion and correlation**
- **Line normalization for amplitude/frequency/phase matching**
- **Advanced Processing: noise removal, data correction, velocity analysis, stacking, migration**
- **Seismic cube export/import with compression (.seiscube)**
- Borehole-seismic integration
- Earthquake simulation
- Subsurface GIS integration

---

## Loading Seismic Data

### Supported Formats

| Format | Extension | Notes |
|--------|-----------|-------|
| SEG-Y | .sgy, .segy | Rev 0 and Rev 1 |
| Seismic Cube | .seiscube | Compressed cube format with embedded data |
| Custom | .seismic | Native format |

### Import Workflow

1. Go to **File → Import**
2. Select **Seismic Data**
3. Choose your SEG-Y file
4. Review header information in the import dialog
5. Click **Import**

### SEG-Y Header Parsing

The parser automatically detects:
- Byte order (big-endian/little-endian)
- Sample format (IBM float, IEEE float, etc.)
- Trace length and sample rate
- Inline/crossline numbers
- Coordinate information

---

## Seismic Viewer

### Display Modes

| Mode | Description | Best For |
|------|-------------|----------|
| **Wiggle Trace** | Traditional oscillating line | Detailed trace analysis |
| **Variable Area** | Filled positive amplitudes | Structural interpretation |
| **Color Map** | Amplitude-to-color mapping | Overview visualization |
| **Combined** | Wiggle + color background | Comprehensive view |

### Navigation

| Control | Action |
|---------|--------|
| `+` / `-` | Adjust gain |
| `C` | Cycle color maps |
| Mouse Drag | Select trace range |
| Scroll | Navigate traces |

### Color Maps

Available color maps:
- Seismic (red-white-blue)
- Grayscale
- Rainbow
- Thermal
- Custom user-defined

---

## Trace Analysis

### Single Trace View

1. Click on a trace in the main view
2. Trace detail panel shows:
   - Amplitude vs time/depth
   - Header information
   - Statistics (min, max, RMS)

### Amplitude Analysis

Tools for amplitude interpretation:
- Amplitude extraction at horizons
- RMS amplitude calculation
- Instantaneous amplitude
- Envelope calculation

---

## 3D Seismic Cube

The Seismic Cube feature allows building 3D seismic volumes from multiple intersecting 2D seismic lines. This is particularly useful when full 3D seismic surveys are not available but multiple 2D lines cross each other.

### Key Features

- **Perpendicular Line Insertion**: Add lines perpendicular to existing profiles at any trace location
- **Automatic Intersection Detection**: Finds all crossing points between lines
- **Line Normalization**: Matches amplitude, frequency, and phase at intersections
- **Volume Regularization**: Creates a uniform 3D grid from scattered line data
- **Package Definition**: Define seismic packages/horizons across the cube
- **GIS Export**: Generate subsurface GIS maps from interpreted packages

### Creating a Seismic Cube

#### From the UI

1. Load multiple SEG-Y files (intersecting 2D lines)
2. Go to **Tools → Analysis → Create Seismic Cube**
3. Add lines to the cube:
   - Select "Add Line" and choose a loaded seismic dataset
   - Define the line geometry (coordinates from headers or manual input)
4. For perpendicular lines:
   - Select an existing line as the base
   - Choose the trace index for insertion
   - Add a new seismic dataset as the perpendicular line
5. Click "Detect Intersections" to find all line crossings
6. Apply normalization at intersections
7. Build the regularized 3D volume

#### From GeoScript

```geoscript
# Create a seismic cube from lines
CUBE_CREATE name="Survey_Cube" survey="Field_2024"

# Add lines with geometry
CUBE_ADD_LINE cube="Survey_Cube" line="Line_001"
  start_x=0 start_y=0 end_x=5000 end_y=0 azimuth=90

CUBE_ADD_LINE cube="Survey_Cube" line="Line_002"
  start_x=2500 start_y=-2500 end_x=2500 end_y=2500 azimuth=0

# Add perpendicular line at trace 100 of Line_001
CUBE_ADD_PERPENDICULAR cube="Survey_Cube" base="Line_001"
  trace=100 line="Crossline_001"

# Detect intersections and normalize
CUBE_DETECT_INTERSECTIONS cube="Survey_Cube"
CUBE_NORMALIZE cube="Survey_Cube"

# Build regularized volume
CUBE_BUILD_VOLUME cube="Survey_Cube"
  inline_spacing=25 crossline_spacing=25 sample_interval=4
```

### Line Normalization System

The normalization system ensures consistent amplitude, frequency, and phase characteristics at line intersections.

#### Normalization Methods

| Parameter | Method | Description |
|-----------|--------|-------------|
| **Amplitude** | RMS, Mean, Peak, Median, Balanced | Matches trace amplitudes at crossings |
| **Frequency** | Bandpass Filtering | Applies consistent frequency band to all lines |
| **Phase** | Cross-correlation | Aligns phases using optimal time shifts |
| **Smoothing** | Transition Zone | Gradual blending at intersection areas |

#### Configuration Options

```geoscript
# Configure normalization settings
CUBE_SET_NORMALIZATION cube="Survey_Cube"
  amplitude_method=balanced
  frequency_low=10 frequency_high=80
  match_phase=true
  transition_traces=5
  window_traces=10
```

#### Quality Metrics

After normalization, the following metrics are calculated:
- **Tie Quality**: Cross-correlation coefficient (0-1)
- **Amplitude Mismatch**: Relative amplitude difference
- **Phase Mismatch**: Sample shift required for alignment
- **Frequency Mismatch**: Spectral centroid difference

### Cube Viewer

The Seismic Cube Viewer provides multiple view modes:

| View Mode | Description |
|-----------|-------------|
| **Time Slice** | Horizontal slice at constant time/depth |
| **Inline Section** | Vertical section along inline direction |
| **Crossline Section** | Vertical section along crossline direction |
| **3D Line View** | Map view showing all lines and intersections |

#### Navigation Controls

| Control | Action |
|---------|--------|
| Time/Inline/Crossline Slider | Navigate through volume |
| Gain Slider | Adjust display amplitude |
| Color Map | Change visualization style |
| Grid Toggle | Show/hide grid overlay |
| Lines Toggle | Show/hide line positions |
| Intersections Toggle | Show/hide intersection markers |

### Package Definition

Seismic packages represent geological units or horizons within the cube.

#### Creating Packages

1. In the Tools panel, select "Packages"
2. Click "Create Package"
3. Define package properties:
   - Name and description
   - Color for visualization
   - Lithology type
   - Seismic facies
4. Pick horizon points on the sections
5. The system interpolates a continuous horizon surface

#### Package Properties

| Property | Description |
|----------|-------------|
| Name | Identifier for the package |
| Color | Display color (RGBA) |
| Lithology Type | Rock type classification |
| Seismic Facies | Seismic character description |
| Confidence | Interpretation confidence (0-1) |
| Horizon Points | Picked points defining the top |
| Horizon Grid | Interpolated elevation grid |

### Subsurface GIS Export

Convert interpreted seismic packages to 3D GIS format for integration with other subsurface data.

#### Export Process

1. Ensure packages are defined with horizon picks
2. Go to **Tools → Export → Generate Subsurface GIS**
3. Configure export options:
   - Grid resolution
   - Parameter selection (amplitude, lithology, etc.)
4. Generate the SubsurfaceGIS dataset

#### Exported Data

- **Voxel Grid**: 3D distribution of lithologies and parameters
- **Layer Boundaries**: Horizon surfaces for each package
- **Amplitude Data**: Original seismic amplitudes
- **Package Statistics**: Amplitude statistics per package

#### GeoScript Export

```geoscript
# Export cube to SubsurfaceGIS
CUBE_EXPORT_GIS cube="Survey_Cube" output="Subsurface_Model"

# Export time slice as raster
CUBE_EXPORT_SLICE cube="Survey_Cube" time=1500
  output="TimeSlice_1500ms.tif"

# Get package statistics
CUBE_STATISTICS cube="Survey_Cube"
```

### Seismic Cube Export/Import (.seiscube)

The `.seiscube` file format provides efficient storage and sharing of seismic cube datasets with intelligent compression.

#### Export Options

| Option | Description | Default |
|--------|-------------|---------|
| **Compression Level** | None (0), Fast (1), Optimal (2), Maximum (3) | Optimal (2) |
| **Embed Seismic Data** | Include trace data in file vs. reference external SEG-Y | Yes |
| **Include Volume** | Include regularized 3D volume | Yes |

#### Export Workflow

1. Open SeismicCubeTools → Export tab
2. Configure compression and embedding options
3. Review estimated file size
4. Click "Export Seismic Cube..."
5. Choose output location and filename
6. Progress bar shows export status

#### Import Workflow

1. Go to **File → Import**
2. Select **Seismic Cube** (.seiscube)
3. Preview shows line count, intersections, bounds
4. Click **Import**

#### File Format Details

- **Magic Number**: `SEISCUBE` (8 bytes)
- **Compression**: GZip for metadata and volume, 16-bit quantization for traces
- **Typical Compression Ratios**: 30-70% of original size

---

## Advanced Seismic Processing

The Advanced Processing tools prepare seismic data for geological interpretation through a complete processing workflow: noise removal, data correction, velocity analysis, stacking, and migration.

### Accessing Advanced Processing

1. Select a Seismic dataset in the Project Tree
2. Go to **Tools panel → Advanced** category
3. Choose the processing step (Noise Removal, Data Correction, etc.)

### Processing Workflow

```
Raw SEG-Y → Noise Removal → Data Correction → Velocity Analysis → NMO + Stacking → Migration
```

### Noise Removal

Remove unwanted noise to enhance signal quality.

#### Available Methods

| Method | Description | Best For |
|--------|-------------|----------|
| **Median Filter** | Sliding window median | Spike/impulse noise |
| **F-K Filter** | Frequency-wavenumber filtering | Linear coherent noise (ground roll) |
| **SVD Filter** | Singular value decomposition | Random noise attenuation |
| **Wavelet Denoising** | Haar wavelet soft thresholding | Broadband random noise |
| **Adaptive Subtraction** | Model-based noise subtraction | Predictable noise patterns |
| **Spike Deconvolution** | High-amplitude spike removal | Instrument artifacts |

#### Parameters

- **Window Size**: Median filter window (3-21 samples)
- **SVD Components**: Number of components to keep (1-50)
- **Wavelet Threshold**: Soft threshold level (0.01-1.0)
- **Spike Threshold**: RMS multiplier for spike detection (2-10)

### Data Correction

Apply corrections for acquisition and propagation effects.

#### Static Corrections

Correct for near-surface effects and datum alignment.

| Parameter | Description |
|-----------|-------------|
| Datum Shift | Bulk time shift (ms) |
| Per-trace Statics | Individual trace adjustments |

#### Amplitude Corrections

| Correction | Purpose | Parameters |
|------------|---------|------------|
| **Spherical Divergence** | Compensate wavefront spreading | Velocity, Gain |
| **Geometric Spreading** | Time-power correction (t^n) | Exponent (1.0-2.5) |
| **Q Compensation** | Frequency-dependent attenuation | Q factor, Dominant frequency |

#### Deconvolution

- **Source Wavelet Deconvolution**: Compress source wavelet using Wiener filter
- **Filter Length**: 10-200 samples
- **Prewhitening**: Stabilization factor (0.001-0.1)

#### Polarity Correction

Detect and correct polarity reversals using cross-correlation with a reference trace.

### Velocity Analysis

Determine interval and RMS velocities for NMO correction.

#### Semblance Analysis

The semblance method scans velocity values and computes stack coherence:

```
Semblance = Σ(stacked amplitude)² / (N × Σ(individual amplitudes²))
```

#### Parameters

| Parameter | Description | Typical Range |
|-----------|-------------|---------------|
| Min Velocity | Lower velocity bound | 1500 m/s |
| Max Velocity | Upper velocity bound | 6000 m/s |
| Velocity Steps | Scan resolution | 50-200 |
| Time Steps | Vertical resolution | 100-500 |
| Semblance Window | Analysis window size | 30-100 ms |
| Pick Threshold | Minimum semblance for picks | 0.2-0.5 |

#### Output

- **Velocity Picks**: Time-velocity pairs for NMO
- **Interval Velocities**: Computed via Dix equation
- **Semblance Panel**: 2D velocity scan display

### NMO Correction and Stacking

Normal Moveout correction flattens hyperbolic reflections; stacking improves signal-to-noise.

#### NMO Equation

```
t = √(t₀² + x²/v²)

Where:
- t = observed travel time
- t₀ = zero-offset travel time
- x = source-receiver offset
- v = RMS velocity
```

#### Stacking Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| CDP Fold | Traces per stack point | 12 |
| Trace Spacing | Offset increment | 25 m |
| Stretch Mute | Remove stretched samples | 30% |

#### Workflow

1. Run Velocity Analysis first
2. Set stacking parameters
3. Apply NMO + Stack
4. Result is a stacked section with improved S/N

### Migration

Move dipping reflectors to their true subsurface positions.

#### Available Methods

| Method | Type | Description | Use Case |
|--------|------|-------------|----------|
| **Kirchhoff** | Time domain | Diffraction summation | Variable velocity, steep dips |
| **Phase Shift** | F-K domain | Phase rotation in spectrum | Constant velocity, fast |
| **Finite Difference** | Wave equation | 15-degree wave propagation | Moderate dips, lateral variation |
| **Stolt F-K** | F-K domain | Wavenumber mapping | Constant velocity, very fast |

#### Parameters

| Parameter | Description | Typical Value |
|-----------|-------------|---------------|
| Migration Velocity | Constant or variable | 2000-4000 m/s |
| Aperture | Horizontal extent (Kirchhoff) | 50-200 traces |
| Depth Steps | Continuation steps (FD) | 50-500 |

#### When to Use Each Method

- **Kirchhoff**: Complex geology, lateral velocity changes
- **Phase Shift**: Simple geology, quick processing
- **Finite Difference**: Moderate complexity, accurate for dipping beds
- **Stolt**: Exploration-stage quick look

### Complete Processing Example

```geoscript
# Full processing workflow
SEIS_NOISE_REMOVE method=median window=5

SEIS_CORRECT
  spherical_divergence=true velocity=2500
  geometric_spreading=true exponent=1.5
  deconvolution=true filter_length=100

SEIS_VELOCITY_ANALYSIS
  min_velocity=1500 max_velocity=5000
  velocity_steps=100 time_steps=200

SEIS_NMO_STACK
  fold=12 stretch_mute=30

SEIS_MIGRATE method=kirchhoff
  velocity=2500 aperture=100
```

---

## Borehole-Seismic Integration

### Synthetic Seismogram Generation

Create synthetic seismograms from well logs:

1. Load borehole dataset with Vp and Density logs
2. Go to **Borehole Tools → Seismic**
3. Configure parameters:
   - Dominant frequency (typically 20-40 Hz)
   - Wavelet type (Ricker, Ormsby, etc.)
   - Polarity convention
4. Click **Generate Synthetic**

### Well-to-Seismic Tie

Correlate wells with seismic data:

1. Load both seismic and borehole datasets
2. Go to **Seismic Tools → Borehole Integration**
3. Select the synthetic seismogram
4. Choose correlation method:
   - Cross-correlation
   - Semblance
   - Manual picking
5. Adjust time shift and stretch
6. View correlation heatmap
7. Apply tie

### Correlation Visualization

The correlation viewer shows:
- Synthetic vs real trace comparison
- Correlation coefficient map
- Time-depth relationship
- Drift analysis

---

## Earthquake Simulation

### Overview

The earthquake simulation system provides:
- 3D earthquake modeling
- Stress tensor propagation
- Subsurface GIS volume generation
- Ground motion calculation

### Creating an Earthquake Simulation

1. Load or create a fault geometry (GIS dataset)
2. Go to **Tools → Analysis → Earthquake Simulation**
3. Configure parameters:
   - **Fault Geometry**: Select fault GIS layer
   - **Magnitude**: Target earthquake magnitude
   - **Depth**: Hypocenter depth (km)
   - **Hypocenter**: Lat/Lon coordinates
   - **Rake**: Slip direction (0=strike-slip, 90=thrust)

4. Run simulation

### Simulation Parameters

| Parameter | Description | Typical Range |
|-----------|-------------|---------------|
| Magnitude | Moment magnitude (Mw) | 4.0 - 9.0 |
| Depth | Hypocenter depth | 0 - 50 km |
| Strike | Fault orientation | 0 - 360° |
| Dip | Fault inclination | 0 - 90° |
| Rake | Slip direction | -180 - 180° |

### Mathematical Framework

**Coulomb Stress Change:**
```
Δσ_c = Δτ + μ(Δσ_n - ΔP)

Where:
- Δτ = change in shear stress on fault
- Δσ_n = change in normal stress on fault
- ΔP = change in pore pressure
- μ = coefficient of friction (0.4 typical)
```

**Seismic Moment:**
```
M_0 = μ × A × D

Where:
- μ = rigidity (shear modulus, GPa)
- A = fault rupture area (m²)
- D = average slip (m)

Moment Magnitude: M_w = 2/3 × log10(M_0) - 10.7
```

### Output Data

The simulation generates:
- **Slip Distribution**: 3D field of fault slip
- **Coulomb Stress**: Stress changes in surrounding rock
- **Ground Motion**: PGA, PGV, PGD maps
- **Event Catalog**: Table of simulated events

---

## Subsurface GIS Integration

### Generating GIS Volumes

Create 3D GIS volumes from earthquake simulations:

1. Run earthquake simulation
2. Go to **Tools → GIS → Generate Subsurface Volume**
3. Select output parameters:
   - Peak Ground Acceleration (PGA)
   - Peak Ground Velocity (PGV)
   - Peak Ground Displacement (PGD)
   - Stress components
   - Fracture density

4. Configure grid resolution
5. Generate volume

### Depth Slices

Extract horizontal slices from 3D volumes:

1. Select subsurface GIS dataset
2. Use slider to select depth
3. View depth-slice map
4. Export as 2D GIS layer

### Time Series Extraction

Extract time series at specific locations:

1. Click on map location
2. View time history of selected parameter
3. Export as CSV table

---

## GeoScript Seismic Commands

### SEIS_FILTER

Apply frequency filters.

```geoscript
SEIS_FILTER type=bandpass low=10 high=80
SEIS_FILTER type=lowpass high=60
SEIS_FILTER type=highpass low=15
```

### SEIS_AGC

Automatic gain control.

```geoscript
SEIS_AGC window=500
```

### SEIS_VELOCITY_ANALYSIS

Velocity analysis for NMO.

```geoscript
SEIS_VELOCITY_ANALYSIS method=semblance
```

### SEIS_NMO_CORRECTION

Normal moveout correction.

```geoscript
SEIS_NMO_CORRECTION velocity=2000
```

### SEIS_STACK

Stack traces.

```geoscript
SEIS_STACK method=mean
```

### SEIS_MIGRATION

Seismic migration.

```geoscript
SEIS_MIGRATION method=kirchhoff aperture=1000
```

### SEIS_PICK_HORIZON

Pick seismic horizons.

```geoscript
SEIS_PICK_HORIZON method=auto
```

---

## Processing Workflow Example

### Standard Seismic Processing Pipeline

```geoscript
# Complete processing workflow
SEIS_FILTER type=bandpass low=10 high=80 |>
  SEIS_AGC window=500 |>
  SEIS_NMO_CORRECTION velocity=2000 |>
  SEIS_STACK method=mean |>
  SEIS_MIGRATION method=kirchhoff aperture=1000
```

---

## Troubleshooting

### SEG-Y Load Failure

**Symptom:** Error loading seismic files

**Solutions:**
- Verify SEG-Y revision (supports Rev 0, 1)
- Check byte order (big-endian vs little-endian)
- Examine trace headers for corruption
- Try different import options

### Poor Correlation

**Symptom:** Well tie shows poor correlation

**Solutions:**
- Check velocity log quality
- Adjust wavelet parameters
- Apply phase rotation
- Check depth-time conversion

### Memory Issues

**Symptom:** Out of memory with large surveys

**Solutions:**
- Load subset of traces
- Use lower resolution display
- Process in smaller chunks

---

## References

### Seismology Theory
- Kanamori & Brodsky (2004): The Physics of Earthquakes
- Lay & Wallace (1995): Modern Global Seismology
- Aki & Richards (2002): Quantitative Seismology

### Processing Methods
- Yilmaz (2001): Seismic Data Analysis
- Sheriff & Geldart (1995): Exploration Seismology

---

## Related Pages

- [User Guide](User-Guide.md) - Application documentation
- [Geothermal Simulation](Geothermal-Simulation.md) - Related subsurface analysis
- [GeoScript Manual](GeoScript-Manual.md) - Scripting reference
- [Home](Home.md) - Wiki home page
