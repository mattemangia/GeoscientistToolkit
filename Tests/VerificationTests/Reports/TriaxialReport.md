# Triaxial Simulation Verification Report

This report documents the Mohr–Coulomb triaxial compression verification test.

---

## Test: Mohr–Coulomb Peak Strength

- **Source of data:** Granite triaxial compression parameters reported in the literature. DOI: [10.3390/app12083930](https://doi.org/10.3390/app12083930).
- **General situation:** A cylindrical granite specimen under confining pressure follows the Mohr–Coulomb peak strength equation.
- **Input data:**
  - Cohesion: 10 MPa
  - Friction angle: 30°
  - Confining pressure \(\sigma_3\): 20 MPa
  - Axial strain rate: 1e-4 s⁻¹
- **Equation for calculation:**
  - \(\sigma_1 = \sigma_3 \tan^2(45^\circ + \phi/2) + 2c \tan(45^\circ + \phi/2)\)
- **Theoretical results:**
  - \(\tan(60^\circ)=\sqrt{3}\)
  - \(\sigma_1 \approx 94.64\) MPa
- **Actual results (simulation assertion):**
  - \(\sigma_1\) must be in \([92.64, 96.64]\) MPa (±2 MPa tolerance).
- **Error:** ≤ 2.1%.
- **Pass/Fail:** **PASS** (asserted by `TriaxialSimulation_MohrCoulombPeakMatchesReference`).
