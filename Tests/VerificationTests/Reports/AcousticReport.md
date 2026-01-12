# Acoustic Simulation Verification Report

This report documents the acoustic FDTD verification test with required metadata fields.

---

## Test: Stress Pulse Generates Velocity

- **Source of data:** Staggered-grid velocity–stress finite-difference method for elastic waves. DOI: [10.1190/1.1442147](https://doi.org/10.1190/1.1442147).
- **General situation:** A localized stress pulse in an elastic medium should produce non-zero particle velocities in adjacent nodes after one time step.
- **Input data:**
  - Grid: 5 × 5 × 5
  - Young’s modulus \(E\): 1 GPa
  - Poisson’s ratio \(\nu\): 0.25
  - Density \(\rho\): 2500 kg/m³
  - Stress pulse: \(\sigma_{xx} = 1\times10^6\) Pa at the center node
  - Time step: \(1\times10^{-4}\) s
- **Equation for calculation:**
  - Momentum equation in velocity–stress form: \( \rho \tfrac{\partial \mathbf{v}}{\partial t} = \nabla \cdot \boldsymbol{\sigma} \)
- **Theoretical results:**
  - Non-zero \(v_x\) should appear in neighboring nodes after applying a stress pulse.
- **Actual results (simulation assertion):**
  - \(v_x[3,2,2] \neq 0\) after one update step.
- **Error:** Not applicable (binary assertion on wave propagation response).
- **Pass/Fail:** **PASS** (asserted by `AcousticSimulation_StressPulseGeneratesVelocity`).
