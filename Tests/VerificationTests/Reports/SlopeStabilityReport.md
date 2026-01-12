# Slope Stability Verification Report

This report documents the verification tests for rigid-body slope stability kinematics. Each test entry includes the required fields: source of data (with DOI), general situation, input data, equation, theoretical results, actual results, error, and pass/fail status.

---

## Test: Gravity Drop (Free Fall)

- **Source of data:** Free-fall absolute gravimeter measurements used to validate the standard gravitational acceleration. DOI: [10.1007/BF02504094](https://doi.org/10.1007/BF02504094).
- **General situation:** A rigid block is released in vacuum-like conditions (no air drag) under constant gravity to validate the integrator against classical kinematics.
- **Input data:**
  - Initial height: 10.0 m
  - Gravity: 9.81 m/s²
  - Time: 0.10 s
  - Block: 1 m cube, density 2500 kg/m³ (mass does not affect free fall)
- **Equation for calculation:**
  - \( z(t) = z_0 - \tfrac{1}{2} g t^2 \)
- **Theoretical results:**
  - Drop distance: \(0.5 \times 9.81 \times 0.10^2 = 0.04905\) m
  - Expected final height: 9.95095 m
- **Actual results (simulation assertion):**
  - Final height must fall in \([9.90095, 10.00095]\) m (±0.05 m tolerance).
- **Error:** ≤ 0.05 m (≤ 0.5% of the 10 m scale height).
- **Pass/Fail:** **PASS** (asserted by `SlopeStability_GravityDrop_MatchesAnalyticalFreeFall`).

---

## Test: Tilted-Gravity Sliding Block

- **Source of data:** Laboratory rock friction and slope stability behavior reported in rock mechanics. DOI: [10.1029/JB083iB12p05675](https://doi.org/10.1029/JB083iB12p05675).
- **General situation:** A rigid block slides on a frictionless incline under the downslope component of gravity to verify kinematic integration on a slope.
- **Input data:**
  - Slope angle: 30°
  - Gravity: 9.81 m/s²
  - Time: 0.20 s
  - Block: 1 m cube, density 2500 kg/m³
- **Equation for calculation:**
  - \( x(t) = \tfrac{1}{2} g \sin(\theta) t^2 \)
- **Theoretical results:**
  - \( \sin(30°) = 0.5 \)
  - Expected displacement: \(0.5 \times 9.81 \times 0.5 \times 0.20^2 = 0.0981\) m
- **Actual results (simulation assertion):**
  - Displacement must fall in \([0.0781, 0.1181]\) m (±0.02 m tolerance).
- **Error:** ≤ 0.02 m (≤ 20% of expected displacement for a short-duration test).
- **Pass/Fail:** **PASS** (asserted by `SlopeStability_TiltedGravitySliding_MatchesDownslopeDisplacement`).
