# PhysicoChem Verification Report

This report documents PhysicoChem verification tests (diffusion and geothermal/ORC coupling).

---

## Test: 1D Diffusion (Tracer Step)

- **Source of data:** Analytical diffusion in porous media. DOI: [10.1016/j.mri.2007.01.066](https://doi.org/10.1016/j.mri.2007.01.066).
- **General situation:** A step-function tracer concentration diffuses in 1D porous media under molecular diffusion.
- **Input data:**
  - Grid: 21 cells, \(\Delta x = 1\) mm
  - Diffusion coefficient: \(D = 1\times10^{-9}\) m²/s
  - Time: 3600 s (36 × 100 s)
  - Initial concentration: \(C_0 = 0.01\) (left half), 0 (right half)
- **Equation for calculation:**
  - \( C(x,t) = 0.5 C_0 \left[1 - \operatorname{erf}\left(\tfrac{x}{2\sqrt{Dt}}\right)\right] \)
- **Theoretical results:**
  - At \(x = 0.5\) mm and \(t = 3600\) s: \(C \approx 0.00426\)
- **Actual results (simulation assertion):**
  - Observed concentration at the midpoint is within \([0.00213, 0.00639]\) (±50%).
- **Error:** ≤ 50% (tolerance for coarse-grid diffusion).
- **Pass/Fail:** **PASS** (asserted by `PhysicoChem_DualBoxMixing_TracksReportedDiffusionMagnitude`).

---

## Test: Deep Geothermal Reservoir with Gas Bubbles

- **Source of data:** Coaxial heat exchanger and geothermal flow coupling. DOI: [10.1016/j.energy.2020.117414](https://doi.org/10.1016/j.energy.2020.117414). Relative permeability model basis. DOI: [10.2136/sssaj1980.03615995004400050002x](https://doi.org/10.2136/sssaj1980.03615995004400050002x).
- **General situation:** A 3D reservoir with a coaxial exchanger and gas intrusion should show buoyant gas rise, geothermal gradients, and local cooling.
- **Input data:**
  - Grid: 16 × 16 × 16, domain 100 m³
  - Geothermal gradient: 30 °C/km
  - Thermal conductivity: 1.5–4.0 W/m·K with depth
  - Gas intrusion: methane saturation 30% near bottom
  - Coaxial exchanger: 10 °C inlet, central column
- **Equation for calculation:**
  - Hydrostatic pressure: \(P = P_0 + \rho g z\)
  - Heat diffusion: \( \rho c_p \tfrac{\partial T}{\partial t} = \nabla \cdot (k \nabla T) + Q \)
  - Relative permeability: van Genuchten–Mualem model
- **Theoretical results:**
  - Gas saturation increases toward the surface (buoyancy)
  - Temperature near exchanger decreases vs. far field
  - Pressure increases with depth
- **Actual results (simulation assertion):**
  - Gas near surface > 0
  - Temperature near probe < far-field temperature
  - Pressure at depth > pressure at surface
- **Error:** Not applicable (directional/inequality assertions).
- **Pass/Fail:** **PASS** (asserted by `PhysicoChem_DeepGeothermalReservoir_WithCoaxialExchangerAndGasBubbles`).

---

## Test: ORC Heat Transfer and Energy Production

- **Source of data:** Low-temperature ORC performance survey. DOI: [10.1016/j.rser.2013.01.028](https://doi.org/10.1016/j.rser.2013.01.028). Coaxial geothermal exchanger modeling. DOI: [10.1016/j.energy.2020.117414](https://doi.org/10.1016/j.energy.2020.117414).
- **General situation:** A coaxial heat exchanger provides heat to an ORC cycle; the system should deliver positive net power and realistic efficiency.
- **Input data:**
  - Grid: 12 × 12 × 12, domain 50 m³
  - Geothermal gradient: 2 °C/m (enhanced)
  - Coaxial exchanger depth: 40 m
  - Water flow: 2.0 kg/s, inlet 25 °C
  - ORC evaporator: 80 °C; condenser: 30 °C
  - Turbine efficiency: 80%; pump efficiency: 75%
- **Equation for calculation:**
  - Heat transfer: \(Q = \dot{m} c_p (T_{out} - T_{in})\)
  - Carnot limit: \(\eta_{Carnot} = 1 - T_c/T_h\)
  - Net power: \(W_{net} = W_{turbine} - W_{pump}\)
- **Theoretical results:**
  - Net power > 0
  - Efficiency in 1–20% range for low-temperature ORC
- **Actual results (simulation assertion):**
  - Outlet temperature > inlet temperature
  - \(W_{net} > 0\)
  - Efficiency in 1–20%
- **Error:** Not applicable (range assertions).
- **Pass/Fail:** **PASS** (asserted by `PhysicoChem_ORC_HeatTransferAndEnergyProduction`).
