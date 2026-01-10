# Thermal Conductivity

Guide to CT-based thermal conductivity simulation for heterogeneous materials.

---

## Overview

The thermal conductivity module performs steady-state heat flow simulations on segmented CT volumes. It computes effective thermal conductivity along the selected axis and provides temperature fields, isocontours, and isosurface exports.

**Key capabilities:**
- Multi-material heat transfer with per-material conductivities
- CPU (parallel/SIMD) and GPU (OpenCL) solvers
- Temperature slice visualization and isocontours
- STL export of isotherm surfaces
- CSV/PNG reporting

---

## Entry Point

1. Select a **CT Image Stack** dataset.
2. Open **CT Tools → Analysis → Thermal Conductivity**.
3. Assign material conductivities and run the solver.

---

## Configuration

### Material Conductivities
Assign thermal conductivity (W/m·K) for each material in the dataset. Values can be pulled from the material library.

### Boundary Conditions
Set hot and cold boundary temperatures (K) for the selected axis.

### Solver Options
- **Heat Flow Direction:** X, Y, or Z
- **Solver Backend:** C# Parallel, SIMD, or OpenCL
- **Convergence Tolerance** and **Max Iterations**
- **SOR Factor** for convergence acceleration

---

## Outputs

- Effective thermal conductivity along the chosen axis
- Temperature field and slice views
- Isocontour overlays and isosurface meshes
- CSV, PNG, and report exports

---

## Export Workflow

The tool provides export dialogs for:

- Slice images (PNG)
- Composite figures (PNG)
- Slice data (CSV)
- Thermal report (TXT/RTF)
- Isosurface mesh (STL)

---

## GeoScript Automation

Run thermal conductivity simulations from GeoScript:

```geoscript
SIMULATE_THERMAL_CONDUCTIVITY direction=z temperature_hot=373.15 temperature_cold=293.15
```

---

## Related Pages

- [NMR Simulation](NMR-Simulation.md)
- [Geomechanical Simulation](Geomechanical-Simulation.md)
- [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md)
