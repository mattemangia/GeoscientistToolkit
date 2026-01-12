# Hydrology Verification Report

This report documents the D8 flow-direction and accumulation verification test.

---

## Test: D8 Flow Direction and Accumulation

- **Source of data:** D8 flow-direction algorithm for drainage extraction. DOI: [10.1016/S0734-189X(84)80011-0](https://doi.org/10.1016/S0734-189X(84)80011-0).
- **General situation:** A synthetic 5 × 5 DEM with consistent downslope gradients should route flow toward the lowest corner, producing higher accumulation at the outlet.
- **Input data:**
  - DEM: \(z(i,j) = 100 - 2i - j\) (downslope to southeast)
  - Grid size: 5 × 5
- **Equation for calculation:**
  - Flow accumulation: \(A(i) = 1 + \sum A(\text{upstream neighbors})\)
- **Theoretical results:**
  - The lowest cell should receive flow from multiple upstream cells; accumulation should exceed 1.
- **Actual results (simulation assertion):**
  - Accumulation at [4,4] ≥ accumulation at [0,0] and > 1.
- **Error:** Not applicable (ordering and minimum-accumulation assertions).
- **Pass/Fail:** **PASS** (asserted by `Hydrology_D8FlowAccumulation_MatchesBenchmarkPattern`).
