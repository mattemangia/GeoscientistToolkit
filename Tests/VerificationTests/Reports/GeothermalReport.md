# Geothermal Verification Report

This report documents geothermal verification tests and includes required metadata fields.

---

## Test: Dual-Continuum Matrix–Fracture Heat Exchange

- **Source of data:** Warren–Root dual-porosity exchange model. DOI: [10.2118/426-PA](https://doi.org/10.2118/426-PA).
- **General situation:** Fracture and matrix temperatures equilibrate via inter-porosity heat exchange in a dual-continuum model.
- **Input data:**
  - Mesh: 3 × 1 × 3
  - Matrix temperature: 280 K
  - Fracture temperature: 320 K
  - Fracture aperture: 1e-4 m
  - Exchange time step: 10 s
- **Equation for calculation:**
  - \( \tfrac{\partial T_m}{\partial t} = \alpha (T_f - T_m) \), \( \tfrac{\partial T_f}{\partial t} = -\alpha (T_f - T_m) \)
- **Theoretical results:**
  - Matrix temperature should increase; fracture temperature should decrease after the exchange step.
- **Actual results (simulation assertion):**
  - Center matrix cell temperature increases above 280 K.
  - Center fracture cell temperature decreases below 320 K.
- **Error:** Not applicable (directional exchange check).
- **Pass/Fail:** **PASS** (asserted by `Geothermal_DualContinuumExchange_ReducesTemperatureGap`).

---

## Test: Coaxial Partial-Depth Heat Exchanger (Thermal Plume)

- **Source of data:** Borehole heat exchanger thermal plume behavior. DOI: [10.1016/j.cageo.2009.12.010](https://doi.org/10.1016/j.cageo.2009.12.010).
- **General situation:** A coaxial exchanger terminates at mid-depth; thermal diffusion should produce a cooling plume that extends below the endpoint with gradual fading.
- **Input data:**
  - Domain: 60 m × 60 m × 120 m (20 × 20 × 24 grid)
  - Geothermal gradient: 0.03 °C/m
  - Inlet fluid temperature: 5 °C
  - Heat extraction in active zone: 10 kW/m³
  - Thermal conductivity layers: 1.8–3.5 W/m·K
- **Equation for calculation:**
  - Heat diffusion: \( \rho c_p \tfrac{\partial T}{\partial t} = \nabla \cdot (k \nabla T) + Q \)
- **Theoretical results:**
  - Cooling at exchanger endpoint and just below it.
  - Cooling magnitude should taper with depth below the endpoint.
- **Actual results (simulation assertion):**
  - Temperature at endpoint < far-field temperature.
  - Temperature just below endpoint < far-field temperature.
  - Cooling effect decreases with depth (no abrupt discontinuity).
- **Error:** Not applicable (profile-shape and inequality assertions).
- **Pass/Fail:** **PASS** (asserted by `Geothermal_CoaxialPartialDepth_ThermalPlumeExtendsBelow`).
