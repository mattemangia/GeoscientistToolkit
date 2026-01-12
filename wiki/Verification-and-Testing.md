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

---

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
