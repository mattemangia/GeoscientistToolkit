# Geomechanical Simulation

Guide to stress/strain, failure, and damage modeling on CT volumes, with optional pore-pressure and geothermal coupling.

---

## Overview

The Geomechanics module solves voxel-scale stress/strain and failure using elastic/plastic constitutive models. It supports multiple failure criteria, damage evolution, pore-pressure coupling, and links to permeability and acoustic datasets.

**Key capabilities:**
- Uniaxial/biaxial/triaxial loading
- Mohr-Coulomb, Drucker–Prager, Hoek–Brown, and Griffith failure criteria
- Plasticity and damage evolution
- Pore-pressure and Biot coupling
- Fluid injection and geothermal effects
- Real-time visualization with Mohr circles

---

## Entry Point

1. Select a **CT Image Stack** dataset.
2. Open **CT Tools → Analysis → Geomechanical Simulation**.
3. Choose materials, configure loading, and run.

### Triaxial Simulation Tool

A dedicated triaxial testing UI is available from the main menu:

1. Open **Tools → Triaxial Simulation**.
2. Select a material (and optional PNM dataset).
3. Configure confining pressure, axial loading, and failure criteria.
4. Run the test and review stress-strain curves, Mohr circles, and 3D visualization.

---

## Core Inputs

### Material Properties
Set base properties for the selected materials:
- Young’s modulus
- Poisson ratio
- Density
- Cohesion and friction angle
- Tensile strength

### Loading Conditions
- Uniaxial, biaxial, triaxial, or custom stress tensor
- Principal stress magnitudes (σ1, σ2, σ3)
- Principal stress direction

### Failure and Damage
- Choose a failure criterion (Mohr–Coulomb, Drucker–Prager, Hoek–Brown, Griffith)
- Enable damage evolution and select a damage model
- Configure damage thresholds and growth rate

### Pore-Pressure Coupling
- Enable pore pressure
- Set Biot coefficient and baseline pore pressure

### Computational Settings
- Choose CPU or GPU backend
- Configure iteration count and convergence tolerance
- Optional offloading for large volumes

---

## Outputs

Simulation results include:

- Full stress tensor fields (σxx, σyy, σzz, σxy, σxz, σyz)
- Strain tensor fields
- Principal stresses (σ1, σ2, σ3)
- Failure index, damage field, and fracture mask
- Mohr circle statistics
- Convergence diagnostics and runtime metadata

Results can be exported to CSV or visualized directly in the tool UI.

---

## GeoScript Automation

Run geomechanical simulations from GeoScript, including dataset-field references:

```geoscript
SIMULATE_GEOMECH sigma1=100 sigma2=50 sigma3=20 use_gpu=true
SIMULATE_GEOMECH porosity=examplepnmdataset.porosity sigma1=120 sigma2=60 sigma3=40
```

---

## Advanced Workflows

### Fluid Injection & Geothermal Coupling
Enable the fluid/geothermal modules to simulate:
- Elevated pore pressure around injection points
- Thermal expansion effects
- Geothermal gradient-based stress changes

### Integration With PNM & Acoustic Data
- Load a **PNM dataset** to map permeability or pore structure into the geomechanical solver.
- Link an **Acoustic Volume** for combined rock physics workflows.

---

## Related Pages

- [Thermal Conductivity](Thermal-Conductivity.md)
- [NMR Simulation](NMR-Simulation.md)
- [Geothermal Simulation](Geothermal-Simulation.md)
