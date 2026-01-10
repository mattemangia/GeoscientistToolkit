# Geothermal Simulation

Comprehensive guide for geothermal system simulation and analysis in Geoscientist's Toolkit.

---

## Overview

The Geothermal Simulation module provides:
- Heat transfer modeling in subsurface
- Borehole heat exchanger (BHE) simulation
- Fluid flow modeling
- Seasonal thermal energy storage (BTES)
- Organic Rankine Cycle (ORC) analysis

---

## Getting Started

### Prerequisites

To run geothermal simulations, you need:
- Borehole dataset with temperature and lithology data
- Material properties defined for each lithology
- Simulation parameters configured

### Basic Workflow

1. Load borehole dataset with temperature/lithology
2. Configure simulation in **Tools → Analysis → Geothermal**
3. Set mesh resolution and boundary conditions
4. Define injection/production parameters
5. Run simulation
6. Visualize temperature distribution and flow paths

---

## Simulation Types

### Single Borehole Heat Exchanger (BHE)

Simulate heat extraction from a single borehole.

**Parameters:**
| Parameter | Description | Units |
|-----------|-------------|-------|
| Borehole Depth | Total depth | m |
| Borehole Radius | Wellbore radius | m |
| Pipe Configuration | U-tube, coaxial | - |
| Flow Rate | Fluid circulation rate | L/min |
| Inlet Temperature | Injection temperature | °C |

**Output:**
- Temperature profile along borehole
- Heat extraction rate
- Outlet temperature vs time
- Ground temperature evolution

### Borehole Thermal Energy Storage (BTES)

Model seasonal heat storage in multiple boreholes.

**Configuration:**
- Number of boreholes
- Borehole spacing
- Array geometry (rectangular, hexagonal)
- Injection/extraction schedule

**Analysis:**
- Storage efficiency
- Thermal interference
- Long-term performance
- Economic assessment

### Multi-Borehole Systems

Simulate multiple interacting boreholes.

**Features:**
- Thermal interference modeling
- Optimization of borehole spacing
- Field-scale heat extraction
- Production decline analysis

---

## Advanced Solver Modules

### MultiphaseFlowSolver

Model water-steam-CO2 systems with salinity effects.

**Capabilities:**
- Two-phase water-steam flow
- CO2 transport
- Salinity effects on phase behavior
- Boiling and condensation

### AdaptiveMeshRefinement

Dynamic grid refinement for accuracy.

**Features:**
- Automatic mesh refinement near wellbores
- Coarsening in uniform regions
- Error-based adaptation
- Performance optimization

### TimeVaryingBoundaryConditions

Model seasonal and daily variations.

**Applications:**
- Seasonal temperature cycles
- Daily demand patterns
- Weather-dependent extraction
- Load following operation

### EnhancedHVACCalculator

Realistic heat pump COP calculations.

**Includes:**
- Temperature-dependent COP
- Part-load efficiency
- Auxiliary power consumption
- System integration

### FracturedMediaSolver

Dual-continuum model for fractured reservoirs.

**Features:**
- Matrix-fracture heat transfer
- Preferential flow paths
- Enhanced permeability zones
- Natural fracture networks

---

## Configuration

### Mesh Settings

| Parameter | Description | Recommended |
|-----------|-------------|-------------|
| Resolution X | Horizontal cells | 50-200 |
| Resolution Y | Horizontal cells | 50-200 |
| Resolution Z | Vertical cells | 20-100 |
| Cell Size | Physical size | 1-10 m |

### Material Properties

Required properties for each lithology:
- Thermal conductivity (W/m·K)
- Heat capacity (J/kg·K)
- Density (kg/m³)
- Porosity
- Permeability (optional for flow)

### Boundary Conditions

| Boundary | Options |
|----------|---------|
| Surface | Fixed temperature, heat flux |
| Bottom | Heat flux, temperature gradient |
| Lateral | No-flow, periodic, fixed |

---

## Running Simulations

### CPU vs GPU

| Mode | Best For |
|------|----------|
| CPU | Small grids, debugging |
| GPU (OpenCL) | Large grids, production runs |

Typical speedup with GPU: 5-20x

### Simulation Time

| Grid Size | Time (CPU) | Time (GPU) |
|-----------|------------|------------|
| 50³ | 1-5 min | 30 sec |
| 100³ | 10-30 min | 2-5 min |
| 200³ | 1-2 hours | 5-15 min |

### Convergence Criteria

| Parameter | Default | Description |
|-----------|---------|-------------|
| Max Iterations | 1000 | Per time step |
| Temperature Tolerance | 1e-6 | Relative change |
| Pressure Tolerance | 1e-5 | Relative change |

---

## Visualization

### Temperature Field

- 3D volume rendering
- 2D slice views (XY, XZ, YZ)
- Isosurfaces at temperature values
- Animation of time evolution

### Flow Paths

- Streamlines from injection to production
- Velocity vectors
- Flow rate distribution
- Residence time visualization

### Time Series

- Extraction rate vs time
- Temperature histories
- Energy balance plots
- Comparison with analytical solutions

---

## Organic Rankine Cycle (ORC) Analysis

### Overview

Evaluate power generation efficiency from geothermal fluids.

### Working Fluids

| Fluid | Temperature Range | Efficiency |
|-------|-------------------|------------|
| R245fa | 80-150°C | 8-12% |
| Isobutane | 100-180°C | 10-15% |
| n-Pentane | 120-200°C | 12-18% |

### Analysis Output

- Thermal efficiency
- Net power output
- Heat exchanger requirements
- Economic indicators

---

## GeoScript Commands

### Basic Simulation

```geoscript
# Configure and run geothermal simulation
GEOTHERMAL_SETUP
    BOREHOLE 'well_data'
    DEPTH 100
    INLET_TEMP 5
    FLOW_RATE 2.0

GEOTHERMAL_RUN
    TIME 365 days
    TIMESTEP 1 day
```

### Advanced Options

```geoscript
# Enable advanced features
GEOTHERMAL_SETUP
    ENABLE_MULTIPHASE true
    ENABLE_AMR true
    FRACTURED_MEDIA true
```

---

## Integration Guide

### Execution Order

1. Initialize geometry and mesh
2. Apply material properties
3. Set boundary conditions
4. Initialize solver modules
5. Time-stepping loop:
   - Update boundary conditions
   - Solve flow (if enabled)
   - Solve heat transfer
   - Update mesh (if AMR enabled)
   - Store results
6. Post-process and visualize

### Feature Toggles

| Feature | Default | Performance Impact |
|---------|---------|-------------------|
| Multiphase | Off | High |
| AMR | Off | Medium |
| Time-varying BC | Off | Low |
| Fractured Media | Off | High |
| HVAC Calculator | Off | Low |

---

## Validation

### Benchmark Cases

The geothermal solver has been validated against:

| Case | Reference | Error |
|------|-----------|-------|
| Beier TRT | Beier et al. (2011) | < 5% |
| Lauwerier | Analytical solution | < 2% |
| TOUGH2 | DOE software | < 3% |
| T2Well | Enhanced TOUGH | < 5% |
| COMSOL | FEM reference | < 3% |

See [Verification and Testing](Verification-and-Testing.md) for detailed results.

---

## Troubleshooting

### Simulation Diverges

**Symptoms:** Temperature oscillates or becomes unrealistic

**Solutions:**
- Reduce time step
- Increase mesh resolution
- Check material property values
- Verify boundary conditions

### Slow Performance

**Solutions:**
- Enable GPU acceleration
- Reduce mesh resolution
- Disable unused solver modules
- Use adaptive mesh refinement

### Unrealistic Results

**Check:**
- Material properties are in correct units
- Boundary conditions are physical
- Initial conditions are reasonable
- Simulation time is appropriate

---

## References

- Beier et al. (2011): Thermal response test analysis
- Lauwerier (1955): Heat flow in porous media
- Pruess et al. (1999): TOUGH2 User's Guide
- Pan & Oldenburg (2014): T2Well Manual

---

## Related Pages

- [Thermodynamics and Geochemistry](Thermodynamics-and-Geochemistry.md) - Chemical systems
- [User Guide](User-Guide.md) - Application documentation
- [Verification and Testing](Verification-and-Testing.md) - Benchmark results
- [Home](Home.md) - Wiki home page
