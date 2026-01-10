# Seismic Analysis

Guide for seismic data processing, visualization, and earthquake simulation in Geoscientist's Toolkit.

---

## Overview

The Seismic Analysis module provides:
- SEG-Y file loading and visualization
- Trace analysis and display modes
- Borehole-seismic integration
- Earthquake simulation
- Subsurface GIS integration

---

## Loading Seismic Data

### Supported Formats

| Format | Extension | Notes |
|--------|-----------|-------|
| SEG-Y | .sgy, .segy | Rev 0 and Rev 1 |
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

### SIMULATE_EARTHQUAKE

Run earthquake simulation (extended GeoScript).

```geoscript
SIMULATE_EARTHQUAKE
    FAULT 'fault_gis_dataset'
    MAGNITUDE 7.5
    DEPTH 15
    HYPOCENTER_LAT -33.5 HYPOCENTER_LON 150.2
    RAKE 90
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

- [User Guide](User-Guide) - Application documentation
- [Geothermal Simulation](Geothermal-Simulation) - Related subsurface analysis
- [GeoScript Manual](GeoScript-Manual) - Scripting reference
- [Home](Home) - Wiki home page
