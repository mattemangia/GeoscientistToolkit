# PNM Absolute Permeability Verification Report

This report documents the single-tube permeability verification test.

---

## Test: Single Tube (Poiseuille)

- **Source of data:** Network model for porous media flow. DOI: [10.2118/574-G](https://doi.org/10.2118/574-G).
- **General situation:** A single throat connecting two pores should reproduce Darcy permeability predicted by Poiseuille flow in a cylindrical capillary.
- **Input data:**
  - Throat radius: 0.5 μm
  - Throat length: 10 μm
  - Viscosity: 1 mPa·s
  - Pressure drop: 100 Pa
- **Equation for calculation:**
  - Capillary permeability: \(k = r^2/8\)
- **Theoretical results:**
  - \(k = (0.5\times10^{-6})^2/8 = 3.125\times10^{-14}\) m² ≈ 31.7 mD
- **Actual results (simulation assertion):**
  - Darcy permeability is in the range 10–40 mD.
- **Error:** ≤ ~68% relative error from the theoretical value (tolerance band for voxelized networks).
- **Pass/Fail:** **PASS** (asserted by `PnmAbsolutePermeability_SingleTubeMatchesPoiseuille`).
