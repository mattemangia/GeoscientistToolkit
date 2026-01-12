# 2D Geomechanics Bearing Capacity Verification Report

This report documents strip footing bearing-capacity verification tests.

---

## Test: Strip Footing on Sand (Reference Factors)

- **Source of data:** Strip footing bearing-capacity studies on sand. DOI: [10.1063/1.5062630](https://doi.org/10.1063/1.5062630).
- **General situation:** A strip footing on Mohr–Coulomb soil is loaded until yielding; the ultimate bearing pressure is compared with Terzaghi factors.
- **Input data:**
  - Footing width: 4.0 m
  - Soil depth: 25.0 m
  - Cohesion: 20 kPa
  - Friction angle: 30°
  - Density: 1800 kg/m³
  - Smooth-footing reduction factor: 0.6
- **Equation for calculation:**
  - Terzaghi strip footing capacity:
    - \(q_u = c N_c + \gamma D N_q + 0.5 \gamma B N_\gamma\)
    - \(N_q = e^{\pi \tan\phi} \tan^2(45^\circ + \phi/2)\)
    - \(N_c = (N_q - 1)/\tan\phi\)
    - \(N_\gamma = 2(N_q + 1)\tan\phi\)
- **Theoretical results:**
  - Ultimate capacity from the formula (post-multiplied by 0.6) defines the expected benchmark.
- **Actual results (simulation assertion):**
  - FEM estimate must be within ±5% of the theoretical capacity.
- **Error:** ≤ 5%.
- **Pass/Fail:** **PASS** (asserted by `TwoDGeology_BearingCapacityStripFooting_AlignsWithReferenceFactors`).

---

## Test: Strip Footing on Cohesive Clay

- **Source of data:** Bearing-capacity factors for strip footings on Mohr–Coulomb soils. DOI: [10.1139/t93-099](https://doi.org/10.1139/t93-099).
- **General situation:** Same strip footing setup with higher cohesion and lower friction angle to validate the model across soil classes.
- **Input data:**
  - Footing width: 3.0 m
  - Soil depth: 20.0 m
  - Cohesion: 50 kPa
  - Friction angle: 25°
  - Density: 1900 kg/m³
  - Smooth-footing reduction factor: 0.6
- **Equation for calculation:**
  - Same Terzaghi strip-footing capacity equation as above.
- **Theoretical results:**
  - Ultimate capacity from the formula (post-multiplied by 0.6) defines the expected benchmark.
- **Actual results (simulation assertion):**
  - FEM estimate must be within ±5% of the theoretical capacity.
- **Error:** ≤ 5%.
- **Pass/Fail:** **PASS** (asserted by `TwoDGeology_BearingCapacityStripFooting_CohesiveClayMatchesReferenceFactors`).
