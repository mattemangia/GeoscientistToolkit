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
2. Go to **Tools panel** (right side panel)
3. Click the **Category** dropdown
4. Select **"Advanced"**
5. Use the **tabs** at the top to switch between processing steps:
   - Noise Removal
   - Data Correction
   - Velocity Analysis
   - Stacking
   - Migration

![Advanced Processing Location](images/seismic-advanced-processing.png)

### Processing Workflow

The recommended processing order is:

```
Raw SEG-Y → Noise Removal → Data Correction → Velocity Analysis → NMO + Stacking → Migration
```

---

### Step-by-Step: Noise Removal

**Goal:** Remove unwanted noise to enhance signal quality before further processing.

#### Tutorial: Remove Spike Noise with Median Filter

1. **Load your seismic dataset** (File → Import → .sgy file)
2. **Select the dataset** in Project Tree
3. Open **Tools panel → Advanced → Noise Removal** tab
4. In "Noise Removal Method" section:
   - Select **"MedianFilter"** from the dropdown
5. In "Method Parameters" section:
   - Set **Window Size** to **5** (for typical spike noise)
   - Increase to **9-15** for more aggressive filtering
6. Click **"Apply Noise Removal"** button
7. **Watch the progress bar** - processing runs in background
8. View results in the **Seismic Viewer** - noise should be reduced

#### Tutorial: Remove Ground Roll with F-K Filter

Ground roll is linear coherent noise that appears as steep dipping events.

1. Select **"FKFilter"** from the Method dropdown
2. Set **Low Cut Velocity** to **1000** m/s (rejects slow events)
3. Set **High Cut Velocity** to **5000** m/s (keeps signal velocities)
4. Click **"Apply Noise Removal"**
5. Ground roll events should be attenuated

#### Tutorial: Attenuate Random Noise with SVD

1. Select **"SingularValueDecomposition"** method
2. Set **Components to Keep** to **10-20**
   - Lower values = more aggressive noise removal
   - Higher values = preserve more signal detail
3. Click **"Apply Noise Removal"**

#### Available Methods

| Method | Description | Best For |
|--------|-------------|----------|
| **Median Filter** | Sliding window median | Spike/impulse noise |
| **F-K Filter** | Frequency-wavenumber filtering | Linear coherent noise (ground roll) |
| **SVD Filter** | Singular value decomposition | Random noise attenuation |
| **Wavelet Denoising** | Haar wavelet soft thresholding | Broadband random noise |
| **Adaptive Subtraction** | Model-based noise subtraction | Predictable noise patterns |
| **Spike Deconvolution** | High-amplitude spike removal | Instrument artifacts |

---

### Step-by-Step: Data Correction

**Goal:** Correct for acquisition geometry, propagation effects, and instrument response.

#### Tutorial: Apply Amplitude Corrections

Amplitude corrections compensate for energy loss with depth.

1. Go to **Tools panel → Advanced → Data Correction** tab
2. **Static Corrections section:**
   - Check **"Apply Static Correction"** if needed
   - Enter **Datum Shift** in ms (positive = shift down)
3. **Amplitude Corrections section:**
   - Check **"Spherical Divergence"** for geometric spreading correction
     - Set **Average Velocity** (typically 2000-3000 m/s)
     - Set **Gain** (start with 0.001, adjust as needed)
   - Check **"Geometric Spreading"** for t^n correction
     - Set **Exponent** (1.0 = linear, 2.0 = quadratic, typically 1.5)
4. Click **"Apply Data Corrections"**
5. View results - deep reflectors should now be visible

#### Tutorial: Apply Q Compensation (Inverse Q Filtering)

Q compensation recovers high frequencies lost to earth absorption.

1. In **Attenuation (Q) Compensation** section:
   - Check **"Apply Q Compensation"**
   - Set **Q Factor** (typically 50-200, lower = more attenuation)
   - Set **Dominant Frequency** (typically 25-40 Hz)
   - Set **Max Gain** (limit to prevent noise amplification, try 10)
2. Click **"Apply Data Corrections"**
3. Check frequency content - higher frequencies should be restored

#### Tutorial: Apply Deconvolution

Deconvolution sharpens the source wavelet for better resolution.

1. In **Deconvolution** section:
   - Check **"Source Wavelet Deconvolution"**
   - Set **Filter Length** to **100** samples (adjust based on wavelet length)
   - Set **Prewhitening** to **0.01** (stabilizes computation)
2. Click **"Apply Data Corrections"**
3. Reflectors should appear sharper

#### Tutorial: Fix Polarity Reversals

Some traces may have flipped polarity due to instrument issues.

1. In **Polarity Correction** section:
   - Check **"Apply Polarity Correction"**
   - Set **Reference Trace** to a trace number with known good polarity (e.g., 0)
2. Click **"Apply Data Corrections"**
3. Inverted traces will be flipped to match the reference

#### Available Corrections

| Correction | Purpose | Parameters |
|------------|---------|------------|
| **Static Correction** | Align traces to datum | Datum shift (ms) |
| **Spherical Divergence** | Compensate wavefront spreading | Velocity, Gain |
| **Geometric Spreading** | Time-power correction (t^n) | Exponent (1.0-2.5) |
| **Q Compensation** | Frequency-dependent attenuation | Q factor, Dominant freq |
| **Deconvolution** | Sharpen source wavelet | Filter length, Prewhitening |
| **Polarity** | Correct flipped traces | Reference trace |

---

### Step-by-Step: Velocity Analysis

**Goal:** Determine the velocity function needed for NMO correction and stacking.

#### Tutorial: Run Velocity Analysis

1. Go to **Tools panel → Advanced → Velocity Analysis** tab
2. **Configure Velocity Scan Parameters:**
   - Set **Min Velocity** to **1500** m/s (water/shallow sediments)
   - Set **Max Velocity** to **5000** m/s (deep carbonates/basement)
   - Set **Velocity Steps** to **100** (higher = finer resolution)
   - Set **Time Steps** to **200** (vertical resolution of picks)
3. **Configure Semblance Parameters:**
   - Set **Window (ms)** to **50** ms (analysis window size)
   - Set **Trace Spacing (m)** to match your acquisition (typically 25 m)
   - Set **Pick Threshold** to **0.3** (lower = more picks, higher = only strong events)
4. Click **"Run Velocity Analysis"**
5. **Watch the progress bar** as semblance is computed
6. **View results** in the output sections:
   - **Velocity Function table**: Time vs. Velocity picks
   - **Interval Velocities**: Derived from Dix equation

#### Interpreting Results

- **High semblance** (>0.5) indicates strong, coherent reflectors
- **Velocity increasing with depth** is typical (compaction)
- **Velocity inversions** may indicate gas or overpressure
- Check interval velocities for geological reasonableness

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

---

### Step-by-Step: NMO Correction and Stacking

**Goal:** Flatten hyperbolic reflections and stack traces to improve signal-to-noise ratio.

> **IMPORTANT:** You must run **Velocity Analysis** first! The NMO+Stack tool uses the velocity picks from velocity analysis.

#### Tutorial: Apply NMO and Stack Traces

1. **First, run Velocity Analysis** (see previous section)
   - Ensure you have velocity picks in the "Velocity Function" table
2. Go to **Tools panel → Advanced → Stacking** tab
3. **Configure NMO and Stacking Parameters:**
   - Set **CDP Fold** (number of traces per stack point)
     - Typical values: 6, 12, 24, 48
     - Higher fold = better S/N but fewer output traces
   - Set **Trace Spacing (m)** to match your acquisition
4. **Configure Stretch Mute:**
   - Check **"Apply Stretch Mute"**
   - Set **Mute Percentage** to **30%** (removes stretched samples near far offsets)
5. Click **"Apply NMO and Stack"**
6. **Monitor progress bar** - NMO correction is applied trace-by-trace
7. **Result**: A new stacked dataset with improved signal-to-noise

#### Understanding the Output

- Stacked data has **fewer traces** (grouped by CDP fold)
- **Signal-to-noise ratio** improves by √N where N is the fold
- Horizontal reflectors should appear **flatter** after NMO
- **Residual moveout** indicates velocity errors

#### Troubleshooting

| Problem | Solution |
|---------|----------|
| Reflectors still curved after NMO | Velocity too low - rerun velocity analysis |
| Reflectors over-corrected (smile) | Velocity too high |
| Shallow data stretched/muted | Reduce stretch mute percentage |
| Poor stack quality | Check for consistent polarity, apply corrections first |

#### NMO Equation

```
t = √(t₀² + x²/v²)

Where:
- t = observed travel time
- t₀ = zero-offset travel time
- x = source-receiver offset
- v = RMS velocity
```

---

### Step-by-Step: Migration

**Goal:** Move dipping reflectors to their true subsurface positions and collapse diffractions.

Migration is typically the **final processing step** and transforms seismic data from time domain to depth or migrated time domain.

#### Tutorial: Apply Kirchhoff Migration (Most Common)

Kirchhoff migration is versatile and handles most geological scenarios.

1. Go to **Tools panel → Advanced → Migration** tab
2. **Select Migration Method:**
   - Choose **"Kirchhoff"** from the dropdown
   - Description: "Diffraction summation - handles lateral velocity variations"
3. **Configure Migration Parameters:**
   - Set **Migration Velocity** to your average velocity (e.g., 2500 m/s)
     - Use velocity from velocity analysis or well data
   - Set **Trace Spacing (m)** to match your data (e.g., 25 m)
   - Set **Aperture (traces)** to **100**
     - Larger = more accurate but slower
     - Too small = migration artifacts
4. Click **"Apply Migration"**
5. **Watch progress bar** - Kirchhoff migrates trace-by-trace
6. **View results:**
   - Dipping reflectors should now be in correct positions
   - Diffractions (point scatterers) should be collapsed
   - Data shows **"Data is migrated"** indicator

#### Tutorial: Apply Phase Shift Migration (Fast)

Use for quick processing with constant velocity.

1. Select **"PhaseShift"** from the Method dropdown
2. Set **Migration Velocity** (use average velocity)
3. Click **"Apply Migration"**
4. **Much faster** than Kirchhoff but assumes constant velocity

#### Tutorial: Apply Finite Difference Migration

Best for moderate dips and lateral velocity variations.

1. Select **"FiniteDifference"** from the Method dropdown
2. Set **Migration Velocity**
3. Set **Depth Steps** to **100-200** (more = more accurate, slower)
4. Click **"Apply Migration"**

#### Tutorial: Apply Stolt F-K Migration (Fastest)

Quick-look migration for exploration.

1. Select **"StoltFK"** from the Method dropdown
2. Set **Migration Velocity**
3. Click **"Apply Migration"**
4. **Very fast** - good for initial assessment

#### Choosing the Right Migration Method

| Geology | Recommended Method | Why |
|---------|-------------------|-----|
| Simple, flat layers | Stolt F-K or Phase Shift | Fast, velocity is constant |
| Moderate dips | Finite Difference | Handles 15-degree dips accurately |
| Complex structure, faults | Kirchhoff | Handles all dips, point diffractors |
| Strong lateral velocity changes | Kirchhoff | Adapts to velocity variations |

#### Available Methods

| Method | Type | Description | Use Case |
|--------|------|-------------|----------|
| **Kirchhoff** | Time domain | Diffraction summation | Variable velocity, steep dips |
| **Phase Shift** | F-K domain | Phase rotation in spectrum | Constant velocity, fast |
| **Finite Difference** | Wave equation | 15-degree wave propagation | Moderate dips, lateral variation |
| **Stolt F-K** | F-K domain | Wavenumber mapping | Constant velocity, very fast |

#### Migration Tips

- **Migrated data** shows reflectors at true positions - use for interpretation
- **Unmigrated (stacked) data** preserves hyperbolic moveout - use for velocity analysis
- Migration can **amplify noise** - apply noise removal first
- **Over-migration** (velocity too high) creates "smiles"
- **Under-migration** (velocity too low) leaves residual diffractions

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
