# Seismic Simulation Verification Report

This report documents P/S wave arrival verification.

---

## Test: P- and S-Wave Arrival Ratio

- **Source of data:** PREM elastic velocity model. DOI: [10.1016/0031-9201(81)90046-7](https://doi.org/10.1016/0031-9201(81)90046-7).
- **General situation:** P-waves travel faster than S-waves; arrival-time ratios follow \(t_s/t_p = V_p/V_s\).
- **Input data:**
  - Grid: 10 × 10 × 10
  - Simulation duration: 4 s
  - Hypocenter depth: 5 km
  - Crustal velocities: \(V_p\) and \(V_s\) from PREM upper crust
- **Equation for calculation:**
  - Arrival time: \(t = d / V\)
  - Ratio: \(t_s/t_p = V_p/V_s\)
- **Theoretical results:**
  - For typical crustal values (\(V_p \approx 5.8\) km/s, \(V_s \approx 3.2\) km/s), \(t_s/t_p \approx 1.81\).
- **Actual results (simulation assertion):**
  - \(t_p > 0\)
  - \(t_s > t_p\)
  - \(t_s/t_p > 1.2\)
- **Error:** Not applicable (ratio threshold assertion).
- **Pass/Fail:** **PASS** (asserted by `SeismicSimulation_PAndSArrivalsMatchVelocityRatio`).
