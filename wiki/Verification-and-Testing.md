# Verification and Testing

This page documents the verification test framework and points to the detailed verification reports. Each report includes the required fields: source of data with DOI, general situation, input data, equation, theoretical results, actual results, error, and pass/fail.

---

## Verification Test Framework

### Location

Verification tests and reports are located in:
```
Tests/VerificationTests/
├── SimulationVerificationTests.cs
└── Reports/
    ├── AcousticReport.md
    ├── GeothermalReport.md
    ├── HydrologyReport.md
    ├── MultiphaseReport.md
    ├── NuclearReactorReport.md
    ├── PhysicoChemReport.md
    ├── PnmReport.md
    ├── SeismicReport.md
    ├── SlopeStabilityReport.md
    ├── TriaxialReport.md
    └── TwoDGeologyBearingCapacityReport.md
```

### Running Tests

**Run all verification tests:**
```bash
dotnet test --filter Category=Verification
```

**Run this suite only:**
```bash
dotnet test Tests/VerificationTests/VerificationTests.csproj
```

### Per-Test Verification Statistics (Summary)

Each verification test tracks **inputs**, **expected values**, **observed outputs**, and **error/variance**. See `VerificationReport.md` for full details.

| # | Test | Key Inputs | Expected | Observed | Error / Variance |
| --- | --- | --- | --- | --- | --- |
| 1 | Triaxial compression | c=26.84 MPa, φ=51°, σ₃=10 MPa | 231.33 MPa | 231.55 MPa | 0.10% |
| 2 | Elastic P-wave | Vp=5.8 km/s, dist=10 km | 1.724 s | 1.599 s | 7.26% |
| 3a | Gravity drop | g=9.81 m/s², t=2 s | 19.62 m | 19.60 m | 0.10% |
| 3b | Sliding block | slope=45°, φ=30°, t=1 s | 1.47 m | 1.47 m | 0.0% |
| 4 | Water Psat | T=373.15 K | 101,325 Pa | 101,417.98 Pa | 0.09% |
| 5 | PNM permeability | r=1 μm, L=20 μm | ~2–4 mD | 6.96 mD | Order-of-magnitude |
| 6 | Sound speed | K=2.2 GPa, ρ=1000 kg/m³ | 1483.2 m/s | 1543.2 m/s | 4.0% |
| 7 | Heat conduction | α=8×10⁻⁷ m²/s, t=2000 s | ~86°C | 85.75°C | < 1.0% |
| 8 | D8 accumulation | 5×5 DEM | ≈25 cells | 20/25 cells | ~20% low |
| 9 | BHE cooling | depth=100 m, inlet=20°C | Cooling trend | 19.54°C | Trend-consistent |
| 10 | Deep coaxial | depth=3000 m, inlet=40°C | Heating trend | 64.48°C | Trend-consistent |
| 11 | Fractional crystallization | F=0.5, Dₙᵢ=10, Dᵣᵦ=0.001 | Ni 0.39 / Rb 9.98 ppm | Ni 0.39 / Rb 9.98 ppm | < 2% |
| 12 | ACD/CCD horizons | [Ca²⁺]=0.01028, γCa=0.2 | 900 m / 4200 m | 970 m / 4130 m | 7.6% / 1.6% |
| 13 | Multiphase reservoir | 16³ grid, CH₄ intrusion | Buoyancy + gradients | Assertions satisfied | Assertion-based |
| 14 | ORC power | 12³ grid, R600a | Net power >0 | Net power >0; η=3.7% | In-range |
| 15 | Point kinetics | β=0.0065, ρ=100 pcm | Period in seconds | 0.3–3× expectation | In-range |
| 16 | D₂O moderation | σa(D₂O)=0.0013 barn | Ratio ≫ H₂O | Ratios in range | In-range |
| 17 | Xe-135 poisoning | σa=2.65×10⁶ barn | Peak 10–12 hr | Peak 8–14 hr | In-range |
| 18 | Thermal efficiency | PWR/CANDU power | 32–34% / 30–32% | 30–38% / 28–35% | In-range |

---

### Sensitivity Analysis (Qualitative)

The verification suite also documents **directional sensitivity** (how outputs change with key inputs) based on the governing equations used by each test.

| # | Test | Sensitive Inputs | Expected Sensitivity |
| --- | --- | --- | --- |
| 1 | Triaxial compression | c, φ, σ₃ | Peak strength increases with c, φ, and σ₃. |
| 2 | Elastic P-wave | Vp, distance | Arrival time decreases with higher Vp; increases with distance. |
| 3a | Gravity drop | g, t | Drop distance scales with g and t². |
| 3b | Sliding block | slope θ, friction φ | Acceleration increases with θ and decreases with φ. |
| 4 | Water Psat | temperature T | Psat increases sharply with T (IAPWS). |
| 5 | PNM permeability | radius r, porosity φ | Permeability increases with r² and φ. |
| 6 | Sound speed | K, ρ | Velocity increases with K and decreases with ρ. |
| 7 | Heat conduction | α, t, x | Temperature increases with α and t; decreases with x. |
| 8 | D8 accumulation | DEM gradients | Accumulation increases with convergent gradients. |
| 9 | BHE cooling | inlet/ground T | Outlet trends toward ground temperature. |
| 10 | Deep coaxial | gradient, depth | Outlet temperature increases with depth and gradient. |
| 11 | Fractional crystallization | F, D | Compatible elements decrease as F drops; incompatible increase. |
| 12 | ACD/CCD horizons | carbonate activity | Horizons deepen with lower carbonate activity. |
| 13 | Multiphase reservoir | density contrast | Buoyancy rises with density contrast. |
| 14 | ORC power | ΔT, efficiency | Net power rises with ΔT and efficiency. |
| 15 | Point kinetics | β, ρ, Λ | Period decreases with higher ρ and lower β. |
| 16 | D₂O moderation | σa, Σs | Moderation ratio decreases with higher absorption. |
| 17 | Xe-135 poisoning | σa, flux | Peak worth increases with σa and flux. |
| 18 | Thermal efficiency | Pₑ/Pₜ | Efficiency increases with higher electric output. |

---

### Residual Analysis (Observed − Expected)

Residuals are listed where the tests compare numeric expected values to observed outputs. For range- or assertion-based tests, residuals are **N/A**.

| # | Test | Residual |
| --- | --- | --- |
| 1 | Triaxial compression | +0.22 MPa |
| 2 | Elastic P-wave | −0.125 s |
| 3a | Gravity drop | −0.02 m |
| 3b | Sliding block | 0.00 m |
| 4 | Water Psat | +92.98 Pa |
| 5 | PNM permeability | N/A |
| 6 | Sound speed | +60.0 m/s |
| 7 | Heat conduction | −0.25 °C |
| 8 | D8 accumulation | −5 cells |
| 9 | BHE cooling | N/A |
| 10 | Deep coaxial | N/A |
| 11 | Fractional crystallization | 0.00 ppm (Ni), 0.00 ppm (Rb) |
| 12 | ACD/CCD horizons | +70 m (ACD), −70 m (CCD) |
| 13 | Multiphase reservoir | N/A |
| 14 | ORC power | N/A |
| 15 | Point kinetics | N/A |
| 16 | D₂O moderation | N/A |
| 17 | Xe-135 poisoning | N/A |
| 18 | Thermal efficiency | N/A |

## Verification Reports (Required Fields)

Each report below includes DOI-backed sources and the required metadata fields.

- **Acoustic FDTD:** `Tests/VerificationTests/Reports/AcousticReport.md`
- **Geothermal (dual-continuum + thermal plume):** `Tests/VerificationTests/Reports/GeothermalReport.md`
- **Hydrology (D8 flow):** `Tests/VerificationTests/Reports/HydrologyReport.md`
- **Multiphase (water/steam EOS):** `Tests/VerificationTests/Reports/MultiphaseReport.md`
- **Nuclear reactor physics:** `Tests/VerificationTests/Reports/NuclearReactorReport.md`
- **PhysicoChem (diffusion + geothermal/ORC coupling):** `Tests/VerificationTests/Reports/PhysicoChemReport.md`
- **PNM (single-tube permeability):** `Tests/VerificationTests/Reports/PnmReport.md`
- **Seismic P/S arrivals:** `Tests/VerificationTests/Reports/SeismicReport.md`
- **Slope stability (free fall + sliding):** `Tests/VerificationTests/Reports/SlopeStabilityReport.md`
- **Triaxial geomechanics:** `Tests/VerificationTests/Reports/TriaxialReport.md`
- **2D geomechanics bearing capacity:** `Tests/VerificationTests/Reports/TwoDGeologyBearingCapacityReport.md`

---

## Notes

- The consolidated historical report remains in `VerificationReport.md` for legacy context.
- All verification tests referenced above are implemented in `Tests/VerificationTests/SimulationVerificationTests.cs`.
