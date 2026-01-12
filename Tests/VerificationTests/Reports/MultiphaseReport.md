# Multiphase Flow Verification Report

This report documents the water–steam phase transition test.

---

## Test: Water–Steam Transition Updates Saturations

- **Source of data:** IAPWS water/steam formulation (thermodynamic properties). DOI: [10.1063/1.1461829](https://doi.org/10.1063/1.1461829).
- **General situation:** At 450 K and 1 bar, the EOS should indicate a two-phase state where vapor saturation is non-zero and total saturation is conserved.
- **Input data:**
  - Grid: 3 × 3 × 3
  - Temperature: 450 K
  - Pressure: 1 bar (1e5 Pa)
  - Enthalpy: 2.6 × 10⁶ J/kg
  - Initial saturations: liquid 0.9, vapor 0.1, gas 0.0
- **Equation for calculation:**
  - Saturation constraint: \(S_l + S_v + S_g = 1\)
- **Theoretical results:**
  - Vapor saturation should remain > 0 at superheated conditions.
  - Total saturation should remain ~1 (mass conservation).
- **Actual results (simulation assertion):**
  - \(S_v > 0\)
  - \(S_l + S_v + S_g \in [0.99, 1.01]\)
- **Error:** ≤ 1% mass-balance tolerance.
- **Pass/Fail:** **PASS** (asserted by `MultiphaseFlow_WaterSteamTransition_UpdatesSaturations`).
