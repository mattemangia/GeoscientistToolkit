# Nuclear Reactor Verification Report

This report documents point-kinetics and reactor-physics verification tests.

---

## Test: Point Kinetics with Delayed Neutrons

- **Source of data:** Six-group delayed neutron data and kinetics references. DOI: [10.13182/NSE99-A2029](https://doi.org/10.13182/NSE99-A2029).
- **General situation:** A sub-prompt-critical step reactivity insertion should produce a slow power rise governed by delayed neutron groups.
- **Input data:**
  - Total delayed fraction \(\beta = 0.0065\)
  - Six-group fractions and decay constants (U-235)
  - Generation time \(\Lambda = 1\times10^{-4}\) s
  - Reactivity insertion: 100 pcm
- **Equation for calculation:**
  - Point kinetics: \(\dot{n} = \tfrac{\rho-\beta}{\Lambda} n + \sum \lambda_i C_i\)
  - Simplified period (\(\rho \ll \beta\)): \(T \approx \tfrac{\beta}{\lambda_{eff} \rho}\)
- **Theoretical results:**
  - Period on the order of seconds (not prompt).
- **Actual results (simulation assertion):**
  - Power increases and measured period falls within 0.3–3× the simplified expectation.
- **Error:** Within a factor-of-3 tolerance for simplified kinetics.
- **Pass/Fail:** **PASS** (asserted by `NuclearReactor_PointKinetics_DelayedNeutronsMatchKeepin`).

---

## Test: Heavy Water Moderator Properties

- **Source of data:** D2O absorption cross-section measurements. DOI: [10.1016/0022-3107(69)90009-4](https://doi.org/10.1016/0022-3107(69)90009-4).
- **General situation:** Heavy water’s low absorption cross section yields a much larger moderation ratio compared with light water, enabling natural-uranium CANDU operation.
- **Input data:**
  - D2O \(\sigma_a\): 0.0013 barn
  - H2O \(\sigma_a\): 0.664 barn
  - Expected moderation ratio: D2O ≈ 5670, H2O ≈ 71
- **Equation for calculation:**
  - Moderation ratio: \(\Sigma_s/\Sigma_a\)
- **Theoretical results:**
  - D2O moderation ratio ≫ H2O (\(>50\times\))
- **Actual results (simulation assertion):**
  - D2O and H2O moderation ratios are in the expected ranges and D2O advantage > 50×.
- **Error:** Within asserted ranges.
- **Pass/Fail:** **PASS** (asserted by `NuclearReactor_HeavyWaterModerator_MatchesGlasstoneData`).

---

## Test: Xenon-135 Poisoning Dynamics

- **Source of data:** Xenon-135 retention and poisoning behavior. DOI: [10.2172/4319418](https://doi.org/10.2172/4319418).
- **General situation:** Xenon-135 builds up after shutdown, peaking around 10–12 hours and exceeding equilibrium worth.
- **Input data:**
  - Xe-135 \(\sigma_a = 2.65\times10^6\) barn
  - Decay constants: \(\lambda_{Xe} = 2.09\times10^{-5}\) s⁻¹, \(\lambda_{I} = 2.87\times10^{-5}\) s⁻¹
  - Thermal flux: \(3\times10^{13}\) n/cm²·s
- **Equation for calculation:**
  - Equilibrium xenon: \(Xe_{eq} = \tfrac{(\gamma_{Xe}+\gamma_I)\Sigma_f\phi}{\lambda_{Xe}+\sigma_{Xe}\phi}\)
  - Reactivity worth: \(\Delta\rho = -\sigma_{Xe} Xe/\Sigma_a\)
- **Theoretical results:**
  - Peak xenon at ~10–12 hours after shutdown, peak worth exceeds equilibrium.
- **Actual results (simulation assertion):**
  - Peak time in 8–14 hours and peak worth magnitude > equilibrium worth.
- **Error:** Within time-window and worth-range assertions.
- **Pass/Fail:** **PASS** (asserted by `NuclearReactor_XenonPoisoning_MatchesStaceyFormula`).

---

## Test: Thermal Efficiency and Power Balance

- **Source of data:** Pressurized-water reactor thermal-hydraulic reference data. DOI: [10.2172/5277747](https://doi.org/10.2172/5277747).
- **General situation:** Thermal-to-electric conversion efficiencies for large reactors should fall within typical operational ranges, and heat balance should satisfy \(Q_{in} = Q_{elec} + Q_{rejected}\).
- **Input data:**
  - PWR: thermal power 2500–4500 MWth, electrical power 800–1500 MWe
  - CANDU: thermal power 1800–2500 MWth, electrical power 600–800 MWe
- **Equation for calculation:**
  - Efficiency: \(\eta = P_{elec}/P_{thermal}\)
  - Heat balance: \(Q_{in} = Q_{elec} + Q_{rejected}\)
- **Theoretical results:**
  - PWR efficiency 30–38%
  - CANDU efficiency 28–35%
- **Actual results (simulation assertion):**
  - Computed efficiencies fall within the expected ranges; coolant heat removal matches thermal power to within ±10%.
- **Error:** ≤ 10% for heat balance tolerance.
- **Pass/Fail:** **PASS** (asserted by `NuclearReactor_ThermalEfficiency_MatchesIAEAData`).
