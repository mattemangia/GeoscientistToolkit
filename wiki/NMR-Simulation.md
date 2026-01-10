# NMR Simulation

Guide to pore-scale NMR modeling for CT image stacks, including T2/T1 spectra, pore-size conversion, and lab calibration.

---

## Overview

The NMR module simulates nuclear magnetic resonance decay using a random-walk method (CPU SIMD or OpenCL). It is designed for segmented CT volumes and returns synthetic decay curves, T2 distributions, and pore-size statistics.

**Key capabilities:**
- Random-walk NMR decay simulation
- T2/T1 distributions and 2D T1–T2 maps
- Pore size estimation from surface relaxivity
- Lab calibration and comparison reports
- Export of plots and CSV reports

---

## Entry Point

1. Select a **CT Image Stack** dataset.
2. Open **CT Tools → Analysis → NMR Simulation**.
3. Configure simulation parameters and run.

---

## Inputs and Prerequisites

- **Segmented CT volume** with pore space assigned to a material ID.
- **Material relaxivities** for solid matrix phases (surface relaxivity in μm/s).
- **Voxel size** and diffusion coefficient (defaults are tuned for water at room temperature).

---

## Configuration Parameters

The simulation parameters map to `NMRSimulationConfig`:

| Parameter | Description | Notes |
|-----------|-------------|-------|
| Number of Walkers | Random-walk particle count | Higher = smoother curves |
| Number of Steps | Total random-walk steps | Governs total time |
| Time Step (ms) | Step duration | Controls T2 range |
| Diffusion Coefficient | m²/s | Water ≈ 2e-9 |
| Voxel Size | meters | Typically derived from CT metadata |
| Pore Material ID | Material ID | Defines pore space |
| T2 Bin Count / Range | Spectrum bins | T2Min/T2Max in ms |
| T1–T2 Map | Enable + bins | Optional 2D map |
| Pore Shape Factor | Geometry factor | Sphere=3, cylinder=2, slit=1 |
| Use OpenCL | GPU backend | Falls back to SIMD if off |

---

## Outputs

NMR results are stored in the CT dataset and include:

- **Decay curve**: `TimePoints` and `Magnetization`
- **T2 distribution**: `T2Values`, `T2Amplitudes`
- **Pore size distribution**: `PoreSizes`, `PoreSizeDistribution`
- **Statistics**: Mean T2, geometric mean T2, total porosity
- **T1–T2 map** (optional): 2D correlation grid

---

## Pore-Size Conversion

The module uses the surface relaxivity relationship:

```
1/T2 = ρ * (S/V)
```

Where `ρ` is surface relaxivity and `S/V` depends on geometry:

| Geometry | S/V | Equivalent Radius |
|----------|-----|-------------------|
| Sphere | 3/r | r = 3ρT2 |
| Cylinder | 2/r | r = 2ρT2 |
| Slit | 1/r | r = ρT2 |

Set the **Pore Shape Factor** to control this mapping.

---

## Calibration to Laboratory Data

The NMR module includes lab calibration utilities:

- Import Bruker or CSV data
- Compare lab and simulated T2 distributions
- Produce calibration reports with agreement metrics

Use the **Lab Data Calibration** section to align simulation parameters with laboratory measurements.

---

## Export and Reporting

From the NMR tool, you can export:

- Decay curve (PNG)
- T2 distribution (PNG)
- Pore-size distribution (PNG)
- T1–T2 map (PNG/CSV)
- Full results CSV
- Text report

---

## GeoScript Automation

Run NMR simulations from GeoScript:

```geoscript
SIMULATE_NMR pore_material_id=1 steps=1000 timestep_ms=0.01 use_opencl=false
```

---

## Integration Notes

- NMR results are persisted with CT datasets and can be converted to a **Table Dataset** for further analysis.
- Borehole workflows can derive porosity from NMR results when CT and borehole data are linked.

---

## Related Pages

- [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md)
- [Thermal Conductivity](Thermal-Conductivity.md)
- [Geomechanical Simulation](Geomechanical-Simulation.md)
