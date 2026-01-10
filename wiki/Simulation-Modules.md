# Simulation Modules

This page is an index of the simulation-oriented modules in Geoscientist's Toolkit, with entry points, required datasets, and references to the underlying systems in the codebase.

---

## Quick Index

| Module | Primary Dataset | Entry Point | Outputs | Learn More |
|--------|-----------------|------------|---------|------------|
| **Acoustic Simulation** | CT Image Stack | CT Tools → Analysis → Acoustic Simulation | Wavefields, travel-time, acoustic volumes | [Acoustic Simulation](Acoustic-Simulation.md) |
| **NMR Simulation** | CT Image Stack | CT Tools → Analysis → NMR Simulation | T2/T1 distributions, porosity, pore size | [NMR Simulation](NMR-Simulation.md) |
| **Thermal Conductivity** | CT Image Stack | CT Tools → Analysis → Thermal Conductivity | Temperature field, effective k | [Thermal Conductivity](Thermal-Conductivity.md) |
| **Geomechanical Simulation** | CT Image Stack | CT Tools → Analysis → Geomechanical Simulation | Stress/strain, damage, failure indices | [Geomechanical Simulation](Geomechanical-Simulation.md) |
| **Pore Network Modeling** | PNM Dataset | Tools → PNM | Permeability, capillary curves, reactive transport | [Pore Network Modeling](Pore-Network-Modeling.md) |
| **Slope Stability** | DEM / GIS Surface | Tools → Analysis → Slope Stability | Factor of safety, block kinematics | [Slope Stability](Slope-Stability.md) |
| **Seismic & Earthquake** | Seismic Dataset | Tools → Analysis → Seismic | Seismic volumes, synthetic ties | [Seismic Analysis](Seismic-Analysis.md) |
| **Geothermal Simulation** | Borehole, Mesh, GIS | Tools → Analysis → Geothermal | Temperature, flow, heat extraction | [Geothermal Simulation](Geothermal-Simulation.md) |
| **Hydrological Analysis** | GIS Raster | GIS Tools → Hydrological Analysis | Flow paths, watersheds, rainfall response | [Hydrological Analysis](Hydrological-Analysis.md) |
| **Thermodynamics & Geochemistry** | Table Dataset | GeoScript / Tools → Compound Library | Equilibrium speciation, phase diagrams | [Thermodynamics and Geochemistry](Thermodynamics-and-Geochemistry.md) |
| **PhysicoChem Reactors** | PhysicoChem Dataset (Group) | Tools → Analysis → PhysicoChem | Multiphysics reactor states, param sweeps | [PhysicoChem Reactors](PhysicoChem-Reactors.md) |
| **Multiphase Flow (PhysicoChem)** | PhysicoChem Dataset | GeoScript (ENABLE_MULTIPHASE...) | Phase saturations, EOS outputs | [Multiphase Flow](Multiphase-Flow.md) |

---

## How Simulation Modules Are Organized

Most simulation logic lives in the `Analysis/` namespace, with datasets defined in `Data/`. UI entry points are typically in `UI/Tools/` (for CT/GIS datasets) or analysis-specific windows. GeoScript commands are defined in `Business/GeoScript*.cs` and can be used for automation.

### Typical Simulation Workflow

1. **Prepare the dataset** (segmentation, materials, or a structured grid).
2. **Configure parameters** in the tool UI or GeoScript.
3. **Run simulation** (CPU/GPU backends are offered where available).
4. **Review results** (plots, slices, 3D visualization) and export.

---

## Module Highlights

### CT-Scale Physical Simulations

The CT tool suite groups physics simulations in the **Analysis** category:
- **Acoustic Simulation** for wave propagation and tomography.
- **NMR Simulation** for T2/T1 spectra and pore-size inference.
- **Thermal Conductivity** for steady-state heat transport.
- **Geomechanical Simulation** for stress/strain and failure envelopes.

These modules operate directly on segmented CT volumes and pull physical properties from the material library.

### Reservoir & Field-Scale Simulations

- **Geothermal Simulation** runs coupled heat and flow, with optional geomechanical coupling.
- **Hydrological Analysis** performs flow routing, rainfall simulation, and water body tracking on GIS rasters.
- **Seismic & Earthquake** analyses focus on SEG-Y data and event modeling.

### Chemical & Multiphysics Simulation

- **Thermodynamics & Geochemistry** provides equilibrium and reaction tools driven by a compound library.
- **PhysicoChem** datasets model reactors with domains, boundary conditions, and multiphase flow.
- **Multiphase Flow** in PhysicoChem enables water/steam/NCG EOS models and relative permeability.

---

## Where to Look in the Codebase

| Area | What Lives There |
|------|------------------|
| `Analysis/AcousticSimulation/` | Acoustic wave solver, tomography tools |
| `Analysis/NMR/` | Random-walk NMR solver, T1/T2 map utilities |
| `Analysis/ThermalConductivity/` | Heat flow solver + UI |
| `Analysis/Geomechanics/` | FEM-like stress/strain and damage models |
| `Analysis/Geothermal/` | Borehole heat transfer + coupled geomechanics |
| `Analysis/Hydrological/` | Flow routing, rainfall, water body tracking |
| `Analysis/Thermodynamic/` | Equilibrium, kinetics, phase diagrams |
| `Analysis/PhysicoChem/` | Reactor solver, coupling, nuclear/ORC examples |
| `Business/GeoScript*Extensions.cs` | Scripted simulation commands |

---

## Related Pages

- [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md)
- [GeoScript Manual](GeoScript-Manual.md)
- [API Reference](API-Reference.md)
