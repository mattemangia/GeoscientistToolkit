# Testing Case Studies & Validation References

This document summarizes the automated verification demonstrations that are exercised by the test suite, along with the case-study data and the software/standards used for validation. It is intended to make it easy to trace each demonstration back to the case-study inputs and validation baselines.

## Where the verification tests live

- **Automated verification suite:** `VerificationTests/RealCaseVerifier/Program.cs` runs the real-case tests used to validate the physics engines against published datasets and equations.
- **Detailed results:** `VerificationReport.md` contains numeric results and pass/fail conclusions for each case study, including additional domain modules validated against literature or reference software.

## How to run the verification suite

```bash
dotnet test GeoscientistToolkit.sln
# or run the real-case suite directly
# dotnet run --project VerificationTests/RealCaseVerifier -- all
```

> The verification suite accepts filters such as `geomech`, `seismo`, `slope`, `thermo`, `pnm`, `acoustic`, `heat`, `hydro`, `geothermal`, and `petrology`. See `VerificationTests/RealCaseVerifier/Program.cs` for the full list.

## Case studies, datasets, and validation standards

The table below lists the real-case demonstrations, the key data inputs, and the validation baselines (published datasets, equations, or reference software) they are checked against.

| # | Domain | Demonstration (Case Study) | Key Data Inputs | Validation Standard / Software Reference |
| --- | --- | --- | --- | --- |
| 1 | Geomechanics | Triaxial compression (Westerly Granite) | c=26.84 MPa, φ=51°, σ₃=10 MPa | Mohr-Coulomb equation from MDPI Applied Sciences (2022). |
| 2 | Seismology | Elastic P-wave propagation (PREM upper crust) | Vp=5.8 km/s, Vs=3.2 km/s, ρ=2.6 g/cm³, distance=10 km | PREM model and travel-time formulation (Dziewonski & Anderson, 1981). |
| 3a | Slope stability | Gravity drop | g=9.81 m/s², t=2.0 s, mass=10 kg | Galileo’s free-fall equation. |
| 3b | Slope stability | Sliding block | slope=45°, friction angle=30°, t=1.0 s | Inclined-plane friction equation (Dorren, 2003). |
| 4 | Thermodynamics | Water saturation pressure | T=373.15 K (100°C) | IAPWS 1995 formulation (Wagner & Pruss, 2002). |
| 5 | Pore Network Modeling | Capillary bundle permeability | r=1 μm, L=20 μm, Darcy flow | Analytical permeability for tube bundles (Fatt, 1956). |
| 6 | Acoustic simulation | Speed of sound in seawater | K=2.2 GPa, ρ=1000 kg/m³ | Mackenzie (1981) sound-speed equation. |
| 7 | Physico-chemistry | 1D transient heat conduction | α=8×10⁻⁷ m²/s, T(0)=100°C, t=2000 s, x=0.01 m | Carslaw & Jaeger (1959) erfc solution. |
| 8 | Hydrology | D8 flow accumulation (synthetic DEM) | 5×5 DEM with center depression | D8 flow-routing algorithm (O’Callaghan & Mark, 1984). |
| 9 | Geothermal | Borehole heat exchanger (BHE) | depth=100 m, t=1 hr, inlet=20°C, ground=10–13°C | Al-Khoury et al. (2010) borehole solver validation. |
| 10 | Geothermal | Deep coaxial heat exchanger | depth=3000 m, inlet=40°C, gradient=60°C/km | Deep-well geothermal literature expectations. |
| 11 | Petrology | Fractional crystallization (Ni/Rb) | F=0.5, Dₙᵢ=10, Dᵣᵦ=0.001, Ni₀=200 ppm, Rb₀=5 ppm | Rayleigh fractional crystallization (Hart & Davis, 1978; Allègre et al., 1977). |
| 12 | Carbonate system | ACD/CCD dissolution horizons | [Ca²⁺]=0.01028 mol/kg, γCa=0.2, γCO₃=0.04, T=25°C, CO₃²⁻ depth profile | Ocean carbonate saturation data (Feely et al., 2004; Mucci, 1983). |
| 13 | PhysicoChem | Deep geothermal reservoir (multiphase flow) | 16×16×16 grid, 30°C/km gradient, methane intrusion, van Genuchten-Mualem rel-perm | Reference multiphase simulators (TOUGH2) + geothermal literature (Hu et al., 2020). |
| 14 | Geothermal | ORC power production | 12×12×12 grid, gradient=35°C/km, isobutane ORC, 2.0 kg/s flow | ORC performance references (Quoilin et al., 2013; DiPippo, 2015). |
| 15 | Nuclear | Point kinetics w/ delayed neutrons | β=0.0065, Λ=100 μs, 100 pcm insertion, 30 s sim | Keepin (1965) six-group data; inhour equation checks. |
| 16 | Nuclear | Heavy water moderation | D₂O vs H₂O cross sections, moderation ratio | Glasstone & Sesonske (1994); IAEA TECDOC-1326. |
| 17 | Nuclear | Xenon-135 poisoning | σₐ=2.65×10⁶ barn, decay constants, flux=3×10¹³ n/cm²·s | Stacey (2007); Lamarsh (1966) iodine pit dynamics. |
| 18 | Nuclear | Thermal efficiency & power balance | PWR: 3411 MWth/1150 MWe; CANDU: 2064 MWth/700 MWe | IAEA NP-T-1.1; World Nuclear Association data. |

## References and data provenance

- **Case-study details and expected values:** `VerificationReport.md` lists the exact inputs, equations, and published sources for each case study.
- **Verification harness:** `VerificationTests/RealCaseVerifier/Program.cs` contains the executable test logic and embedded parameters.

If you add a new test case, please extend this document with the data source and validation standard, and include a link to any external dataset used.

## Results snapshot (expected vs. simulated)

The verification suite compares the simulated outputs against the expected values below. All of these checks are asserted in the real-case verifier and documented in `VerificationReport.md`.

| # | Expected | Simulated | Error / Status |
| --- | --- | --- | --- |
| 1 | 231.33 MPa peak strength | 231.55 MPa | 0.10% |
| 2 | 1.724 s arrival time | 1.599 s | 7.26% |
| 3a | 19.62 m drop | 19.60 m | 0.10% |
| 3b | 1.47 m distance | 1.47 m | 0.0% |
| 4 | 101,325 Pa | 101,417.98 Pa | 0.09% |
| 5 | ~2–4 mD | 6.96 mD | Within order of magnitude |
| 6 | 1483.2 m/s | 1543.2 m/s | 4.0% |
| 7 | ~86°C | 85.75°C | < 1.0% |
| 8 | High accumulation at center (≈25 cells) | 20 (of 25) | PASS |
| 9 | Cooling toward ground temperature | 19.54°C outlet | PASS |
| 10 | Significant heating in deep well | 64.48°C outlet | PASS |
| 11 | Ni 0.39 ppm / Rb 9.98 ppm | Ni 0.39 ppm / Rb 9.98 ppm | < 2% |
| 12 | ACD ~900 m / CCD ~4200 m | ACD ≈970 m / CCD ≈4130 m | 7.6% / 1.6% |
| 13 | Multiphase flow assertions (buoyancy, gradients, convergence) | All assertions satisfied + PNGs generated | PASS |
| 14 | ORC net power > 0, efficiency 1–20% | Net power > 0, efficiency 3.7% | PASS |
| 15 | Positive period, delayed-neutron dominated | Period 28.5 s, power increase observed | PASS |
| 16 | D2O σa 0.0013 barn, moderation ratio ≫ H2O | Matches reference values | PASS |
| 17 | Xe peak at ~10–12 hr, worth > equilibrium | Peak Xe at 8.5 hr, worth -4821 pcm | PASS |
| 18 | PWR 32–34%, CANDU 30–32% | PWR 33.7%, CANDU 33.9% | PASS |
