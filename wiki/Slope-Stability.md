# Slope Stability

Guide for slope stability analysis and landslide simulation in Geoscientist's Toolkit.

---

## Overview

The Slope Stability module provides:
- DEM-based 3D block simulation
- 2D cross-section stability analysis
- Multiple failure criteria
- Earthquake loading
- Physics-based simulation
- GeoScript integration

---

## Getting Started

### Input Data

Required:
- Digital Elevation Model (DEM)
- Material properties (rock/soil)

Optional:
- Joint set definitions
- Groundwater table
- Earthquake parameters
- External loads

### Basic Workflow

1. Load DEM as GIS dataset
2. Go to **Tools → Analysis → Slope Stability**
3. Define materials and joint sets
4. Configure analysis parameters
5. Run stability analysis
6. Visualize results and factor of safety

---

## Analysis Types

### 3D Block Analysis

DEM-based discrete element simulation.

**Method:**
1. Generate 3D block geometry from DEM
2. Define joint sets (orientation, spacing)
3. Apply constitutive models
4. Run dynamic simulation
5. Track block movements and failures

### 2D Cross-Section Analysis

Traditional limit equilibrium methods.

**Methods Available:**
| Method | Description |
|--------|-------------|
| Bishop | Circular slip surfaces |
| Janbu | Non-circular surfaces |
| Spencer | Force and moment equilibrium |
| Morgenstern-Price | Variable interslice forces |

---

## Block Generation

### Joint Sets

Define discontinuity sets:

| Parameter | Description | Units |
|-----------|-------------|-------|
| Dip | Inclination from horizontal | degrees |
| Dip Direction | Azimuth of dip | degrees |
| Spacing | Distance between joints | m |
| Persistence | Joint continuity | 0-1 |

### Block Properties

Generated blocks have:
- Volume and mass
- Contact surfaces
- Centroid location
- Orientation

---

## Material Properties

### Rock Mass Properties

| Property | Description | Units |
|----------|-------------|-------|
| Density | Rock density | kg/m³ |
| Elastic Modulus | Young's modulus | GPa |
| Poisson Ratio | Lateral strain ratio | - |
| Cohesion | Shear strength | kPa |
| Friction Angle | Internal friction | degrees |
| Tensile Strength | Tensile resistance | kPa |

### Joint Properties

| Property | Description | Units |
|----------|-------------|-------|
| Normal Stiffness | Joint compression | GPa/m |
| Shear Stiffness | Joint shear | GPa/m |
| Friction | Joint friction angle | degrees |
| Cohesion | Joint cohesion | kPa |

---

## Failure Criteria

### Mohr-Coulomb

Classic failure criterion:
```
τ = c + σ_n × tan(φ)

Where:
- τ = shear strength
- c = cohesion
- σ_n = normal stress
- φ = friction angle
```

### Hoek-Brown

For rock masses:
```
σ_1 = σ_3 + σ_ci × (m_b × σ_3/σ_ci + s)^a

Where:
- σ_ci = intact rock strength
- m_b, s, a = rock mass parameters
```

### Barton-Bandis

For rock joints:
```
τ = σ_n × tan(JRC × log10(JCS/σ_n) + φ_r)

Where:
- JRC = Joint Roughness Coefficient
- JCS = Joint wall Compressive Strength
- φ_r = residual friction angle
```

---

## Earthquake Loading

### Configuration

| Parameter | Description | Units |
|-----------|-------------|-------|
| Magnitude | Earthquake magnitude | Mw |
| Epicenter | Location (Lat, Lon) | degrees |
| Depth | Focal depth | km |
| Distance | Site distance | km |

### Ground Motion

Auto-calculated from magnitude and distance:
- **PGA**: Peak Ground Acceleration (g)
- **PGV**: Peak Ground Velocity (m/s)
- **PGD**: Peak Ground Displacement (m)

### Time Functions

| Function | Description |
|----------|-------------|
| Sinusoidal | Simple harmonic |
| Ricker Wavelet | Seismic wavelet |
| Recorded | Real earthquake record |

---

## Physics Simulation

### Simulation Parameters

| Parameter | Description | Default |
|-----------|-------------|---------|
| Time Step | Integration step | 0.001 s |
| Total Time | Simulation duration | 10 s |
| Damping | Energy dissipation | 0.05 |

### Simulation Modes

| Mode | Description |
|------|-------------|
| Static | Equilibrium analysis |
| Quasi-static | Slow loading |
| Dynamic | Full dynamic simulation |
| Earthquake | Seismic loading |

### Boundary Conditions

| Condition | Description |
|-----------|-------------|
| Fixed | No displacement |
| Roller | No normal displacement |
| Free | No constraint |
| Absorbing | Energy absorption |

---

## 2D Section Viewer

### Creating Cross-Sections

1. Draw line on DEM map view
2. Generate 2D section profile
3. Define slip surfaces
4. Run limit equilibrium analysis

### Interactive Features

- Real-time overlays
- Opacity controls
- Fault rendering
- Restoration preview

### Flexural Slip Unfolding

Interactive restoration with:
- Slider-driven unfolding
- Percentage controls
- Present-day vs restored comparison
- Visibility toggles

---

## GeoScript Commands

```geoscript
# Define materials
SLOPE_ADD_MATERIAL
    NAME 'Rock'
    DENSITY 2650
    COHESION 50
    FRICTION 35

# Define joint set
SLOPE_ADD_JOINTSET
    DIP 45
    DIP_DIRECTION 180
    SPACING 2.0
    FRICTION 30

# Run analysis
SLOPE_ANALYZE
    DEM 'dem_dataset'
    MODE dynamic
    TIME 10
    EARTHQUAKE magnitude=6.5 depth=10
```

---

## Visualization

### 3D View

- Block geometry with colors by status
- Displacement vectors
- Velocity contours
- Contact forces

### 2D Section View

- Profile with slip surfaces
- Factor of safety distribution
- Critical slip surface highlighting
- Groundwater table

### Animation

- Time-lapse of block movement
- Failure progression
- Energy dissipation

---

## Export Formats

| Format | Description |
|--------|-------------|
| CSV | Tabular results |
| VTK | 3D visualization |
| JSON | Full data structure |
| GeoJSON | GIS integration |

---

## Performance

### Computation Time

| Blocks | Static | Dynamic (10s) |
|--------|--------|---------------|
| 100 | 1 sec | 10 sec |
| 1,000 | 10 sec | 2 min |
| 10,000 | 2 min | 20 min |

### Optimization Tips

- Use appropriate time step
- Enable damping for faster convergence
- Limit simulation domain
- Use GPU acceleration when available

---

## Troubleshooting

### Blocks Explode

**Solutions:**
- Reduce time step
- Increase damping
- Check material properties
- Verify joint stiffness

### No Failure Occurs

**Check:**
- Material strength not too high
- Loading is sufficient
- Simulation time is adequate
- Boundary conditions allow movement

### Unrealistic Results

**Verify:**
- DEM quality and resolution
- Material property values
- Joint set geometry
- Loading conditions

---

## References

- Hoek & Brown (2019): The Hoek-Brown failure criterion
- Barton & Choubey (1977): The shear strength of rock joints
- Newmark (1965): Effects of earthquakes on dams and embankments
- Itasca (2019): UDEC/3DEC Theory and Background

---

## Related Pages

- [Seismic Analysis](Seismic-Analysis.md) - Earthquake simulation
- [User Guide](User-Guide.md) - Application documentation
- [Verification and Testing](Verification-and-Testing.md) - Benchmark results
- [Home](Home.md) - Wiki home page
