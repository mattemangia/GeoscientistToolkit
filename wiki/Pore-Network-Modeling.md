# Pore Network Modeling

Guide for pore network extraction, analysis, and flow simulation in Geoscientist's Toolkit.

---

## Overview

The Pore Network Modeling (PNM) module provides:
- Network extraction from segmented CT volumes
- Permeability calculation
- Multiphase flow simulation
- Reactive transport modeling
- Dual-porosity systems (macro-micro coupling)

---

## Getting Started

### Prerequisites

To create a pore network, you need:
- Segmented CT volume with pore space identified
- Material assigned to pore phase
- Network generation parameters configured

### Basic Workflow

1. Load CT volume and segment pore space
2. Generate pore network using **Tools → PNM → Extract Network**
3. Visualize network (pores as spheres, throats as cylinders)
4. Run permeability calculation
5. Export results or continue with flow simulation

---

## Network Extraction

### Extraction Methods

| Method | Description | Best For |
|--------|-------------|----------|
| Maximal Ball | Inscribed sphere algorithm | General use |
| Watershed | Watershed-based | Complex geometries |
| Medial Axis | Skeleton-based | Elongated pores |

### Network Parameters

| Parameter | Description | Typical Range |
|-----------|-------------|---------------|
| Min Pore Radius | Minimum pore size to include | 1-10 µm |
| Max Throat Length | Maximum throat length | 10-100 µm |
| Connectivity | Minimum coordination number | 1-3 |

### Network Statistics

Generated networks include:
- **Pore count**: Number of pore bodies
- **Throat count**: Number of connections
- **Coordination number**: Average connections per pore
- **Pore size distribution**: Radius statistics
- **Throat size distribution**: Radius/length statistics
- **Porosity**: Network porosity vs image porosity

---

## Visualization

### 3D Network View

- Pores displayed as spheres (size = pore radius)
- Throats displayed as cylinders
- Color by property (radius, coordination, etc.)
- Interactive rotation and zoom

### Distribution Plots

- Pore radius distribution
- Throat radius distribution
- Coordination number distribution
- Pore-throat aspect ratio

---

## Permeability Calculation

### Single-Phase Permeability

Calculate absolute permeability using network flow simulation.

**Method:**
1. Apply pressure gradient across network
2. Solve for pressure in each pore
3. Calculate total flow rate
4. Compute permeability tensor (kx, ky, kz)

### Directional Permeability

Run simulation in each direction:
- X-direction: Pressure gradient along X
- Y-direction: Pressure gradient along Y
- Z-direction: Pressure gradient along Z

### GeoScript Command

```geoscript
PNM_CALCULATE_PERMEABILITY direction=all
PNM_CALCULATE_PERMEABILITY direction=x
```

---

## Multiphase Flow

### Capillary Pressure Curves

#### Drainage Simulation

Oil displacing water (non-wetting phase invasion).

```geoscript
PNM_DRAINAGE_SIMULATION contact_angle=30 interfacial_tension=0.03
```

**Parameters:**
| Parameter | Description | Units |
|-----------|-------------|-------|
| contact_angle | Wetting angle | degrees |
| interfacial_tension | IFT | N/m |

**Output:**
- Capillary pressure curve (Pc vs Sw)
- Residual saturation
- Invasion sequence

#### Imbibition Simulation

Water displacing oil (wetting phase invasion).

```geoscript
PNM_IMBIBITION_SIMULATION contact_angle=60 interfacial_tension=0.03
```

### Relative Permeability

Calculate relative permeability curves:
- kr_w(Sw) - Water relative permeability
- kr_o(Sw) - Oil relative permeability

---

## Reactive Transport

### Overview

Simulate chemical reactions in pore networks including:
- Species transport (advection + diffusion)
- Mineral dissolution/precipitation
- Porosity and permeability evolution

### Configuration

Define chemical system:
- Species: Aqueous components (Ca2+, CO3^2-, H+, etc.)
- Minerals: Solid phases (calcite, quartz, etc.)
- Initial concentrations
- Boundary conditions

### GeoScript Commands

```geoscript
# Configure species
SET_PNM_SPECIES Ca2+ 0.01 0.005

# Configure minerals
SET_PNM_MINERALS Calcite 0.02

# Run simulation
RUN_PNM_REACTIVE_TRANSPORT 1000 0.01 298 1.5e7 1.0e7

# Export results
EXPORT_PNM_RESULTS 'results.csv'
```

### Output

- Concentration fields over time
- Mineral amounts over time
- Porosity evolution
- Permeability evolution
- Mass balance verification

---

## Dual-PNM Systems

### Overview

Couple macro-pore and micro-pore networks for rocks with dual porosity (e.g., fractured media, carbonates).

### Network Coupling

| Mode | Description |
|------|-------------|
| Parallel | Macro and micro flow in parallel |
| Series | Flow through macro then micro |
| Mass Transfer | Exchange between networks |

### Workflow

1. Extract macro network from CT (low resolution)
2. Extract micro network from SEM (high resolution)
3. Define coupling parameters
4. Run coupled simulation

### Applications

- Fractured reservoirs
- Carbonate rocks
- Shales with organic porosity
- Coal seam gas

---

## Network Operations

### Filtering

Remove small features or isolated clusters.

```geoscript
# Filter by pore size
PNM_FILTER_PORES min_radius=1.0 max_radius=100.0

# Filter by coordination
PNM_FILTER_PORES min_coord=2

# Filter throats
PNM_FILTER_THROATS min_radius=0.5 max_length=50.0
```

### Extract Largest Cluster

Remove isolated pore clusters.

```geoscript
PNM_EXTRACT_LARGEST_CLUSTER
```

### Statistics

Calculate network statistics.

```geoscript
PNM_STATISTICS
```

**Output:**
- Total pores and throats
- Average coordination number
- Pore/throat size distributions
- Network porosity
- Connectivity metrics

---

## Complete Workflow Example

```geoscript
# 1. Load and segment CT data
LOAD "core_scan.ctstack" AS "CoreCT"

# 2. Extract pore network
WITH "CoreCT" DO
    GENERATE_PNM min_pore_radius=1.0
TO "CorePNM"

# 3. Clean network
WITH "CorePNM" DO
    PNM_EXTRACT_LARGEST_CLUSTER |>
    PNM_FILTER_PORES min_coord=2
TO "CorePNM_Clean"

# 4. Calculate permeability
WITH "CorePNM_Clean" DO
    PNM_CALCULATE_PERMEABILITY direction=all
TO "Permeability_Results"

# 5. Run drainage simulation
WITH "CorePNM_Clean" DO
    PNM_DRAINAGE_SIMULATION contact_angle=30 interfacial_tension=0.03
TO "Drainage_Results"

# 6. Get statistics
WITH "CorePNM_Clean" DO
    PNM_STATISTICS
```

---

## Performance Notes

### Memory Requirements

| Network Size | RAM Required |
|--------------|--------------|
| 10,000 pores | 100 MB |
| 100,000 pores | 1 GB |
| 1,000,000 pores | 10 GB |

### Computation Time

| Operation | 10k Pores | 100k Pores |
|-----------|-----------|------------|
| Permeability | 5 sec | 2 min |
| Drainage | 10 sec | 5 min |
| Reactive Transport | 1 min | 30 min |

---

## Troubleshooting

### Unrealistic Permeability

**Check:**
- Pore/throat size calibration
- Network connectivity
- Boundary conditions
- Comparison with image-based porosity

### Memory Issues

**Solutions:**
- Extract smaller subvolume
- Increase minimum pore size threshold
- Filter small features before simulation

### Poor Connectivity

**Solutions:**
- Lower connectivity threshold
- Check segmentation quality
- Verify pore phase is continuous

---

## References

- Dong & Blunt (2009): Pore-network extraction
- Valvatne & Blunt (2004): Predictive pore-scale modeling
- Raoof & Hassanizadeh (2010): Reactive transport in pore networks

---

## Related Pages

- [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md) - Network input preparation
- [Thermodynamics and Geochemistry](Thermodynamics-and-Geochemistry.md) - Reactive transport chemistry
- [User Guide](User-Guide.md) - Application documentation
- [Home](Home.md) - Wiki home page
