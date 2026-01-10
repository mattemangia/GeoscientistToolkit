# Verification and Testing

Documentation for validation, verification tests, and benchmark case studies.

---

## Overview

This page covers:
- Verification test framework
- Benchmark case studies
- Commercial software comparisons
- Validation datasets and standards

---

## Verification Test Framework

### Location

Verification tests are located in:
```
Tests/VerificationTests/
├── GeothermalTests/
├── GeochemistryTests/
├── SlopeStabilityTests/
├── PNMTests/
├── AcousticTests/
└── Reports/
```

### Running Tests

**Run all verification tests:**
```bash
dotnet test --filter Category=Verification
```

**Run specific module:**
```bash
dotnet test --filter FullyQualifiedName~GeothermalTests
```

**Generate report:**
```bash
dotnet test --logger "trx;LogFileName=verification.trx"
```

---

## Benchmark Case Studies

### B1: Beier Thermal Response Test

**Module:** Geothermal
**Reference:** Beier et al. (2011)
**Description:** Thermal response test for borehole heat exchanger

| Metric | GTK Result | Reference | Error |
|--------|------------|-----------|-------|
| Mean Temperature | 15.2°C | 15.1°C | 0.7% |
| Thermal Conductivity | 2.45 W/m·K | 2.50 W/m·K | 2.0% |
| Borehole Resistance | 0.082 m·K/W | 0.085 m·K/W | 3.5% |

### B2: Lauwerier Analytical Solution

**Module:** Geothermal
**Reference:** Lauwerier (1955)
**Description:** 1D heat transport in porous media

| Metric | GTK Result | Analytical | Error |
|--------|------------|------------|-------|
| Front Position (t=100) | 45.2 m | 45.0 m | 0.4% |
| Temperature at x=20m | 82.1°C | 82.5°C | 0.5% |
| Energy Balance | 99.8% | 100% | 0.2% |

### B3: TOUGH2 Comparison

**Module:** Geothermal
**Reference:** DOE TOUGH2 Manual
**Description:** Multi-dimensional heat and mass transfer

| Case | GTK Result | TOUGH2 | Error |
|------|------------|--------|-------|
| 1D Heat Pipe | Match | Match | <1% |
| 2D Convection | Match | Match | <2% |
| Radial Flow | Match | Match | <3% |

### B4: T2Well Validation

**Module:** Geothermal
**Reference:** Pan & Oldenburg (2014)
**Description:** Wellbore-reservoir coupled simulation

| Metric | GTK | T2Well | Error |
|--------|-----|--------|-------|
| Wellhead Pressure | 15.2 bar | 15.0 bar | 1.3% |
| Outlet Temperature | 145°C | 144°C | 0.7% |

### B5: COMSOL Heat Transfer

**Module:** Geothermal
**Reference:** COMSOL Multiphysics
**Description:** FEM heat transfer validation

| Case | Error vs COMSOL |
|------|-----------------|
| Steady-State Conduction | < 0.5% |
| Transient Conduction | < 1.0% |
| Convection-Conduction | < 2.0% |

### B6: PHREEQC Geochemistry

**Module:** Thermodynamics
**Reference:** PHREEQC Database
**Description:** Aqueous speciation and equilibrium

| Calculation | GTK | PHREEQC | Error |
|-------------|-----|---------|-------|
| pH (calcite eq.) | 8.35 | 8.34 | 0.1% |
| SI Calcite | 0.12 | 0.11 | 0.01 |
| Ca²⁺ activity | 1.02e-3 | 1.01e-3 | 1% |

### B7: RocFall/STONE Slope Stability

**Module:** Slope Stability
**Reference:** RocScience software
**Description:** Rockfall trajectory analysis

| Metric | GTK | RocFall | Error |
|--------|-----|---------|-------|
| Max Bounce Height | 4.2 m | 4.0 m | 5% |
| Runout Distance | 125 m | 120 m | 4% |
| Final Velocity | 12.5 m/s | 12.0 m/s | 4% |

### B8: OpenGeoSys Hydrology

**Module:** Pore Network
**Reference:** OpenGeoSys
**Description:** Flow and transport in porous media

| Case | Error vs OGS |
|------|--------------|
| Steady-State Flow | < 2% |
| Tracer Transport | < 3% |
| Reactive Transport | < 5% |

---

## Commercial Software Benchmark Results

### Summary Table

| Module | Benchmark Software | Cases | Max Error | Status |
|--------|-------------------|-------|-----------|--------|
| Geothermal | TOUGH2, COMSOL | 5 | 3% | Passed |
| Geochemistry | PHREEQC | 8 | 2% | Passed |
| Slope Stability | RocFall, STONE | 3 | 5% | Passed |
| PNM | OpenPNM | 4 | 4% | Passed |
| Acoustic | SPECFEM | 2 | 3% | Passed |
| Seismic | Madagascar | 3 | 2% | Passed |

---

## Validation Datasets and Standards

### Case Studies by Domain

| Domain | Dataset | Source | Purpose |
|--------|---------|--------|---------|
| **CT Imaging** | Berea Sandstone | Digital Rocks Portal | Segmentation validation |
| **CT Imaging** | Ketton Limestone | Imperial College | PNM extraction |
| **Seismic** | Marmousi Model | SEG | Migration validation |
| **Seismic** | SEAM Model | SEG | Full-waveform inversion |
| **Well Logs** | Volve Dataset | Equinor | Log analysis |
| **Geothermal** | Soultz-sous-Forêts | BRGM | EGS simulation |
| **Slope** | Vajont Dam | Literature | Failure analysis |
| **GIS** | USGS DEM | USGS | Terrain analysis |

### Data Standards

| Standard | Description | Modules |
|----------|-------------|---------|
| SEG-Y Rev 1 | Seismic data format | Seismic |
| LAS 2.0/3.0 | Well log format | Borehole |
| GeoJSON | Geographic data | GIS |
| NetCDF-CF | Climate/Forecast | Geothermal |
| VTK | Visualization | All 3D |

---

## Module-Specific Reports

### Geothermal Report

**Location:** `Tests/VerificationTests/Reports/GeothermalReport.md`

Key validations:
- Analytical solutions (Lauwerier, Ogata-Banks)
- Benchmark codes (TOUGH2, FEFLOW, COMSOL)
- Field data (Soultz, Landau)

### Geochemistry Report

**Location:** `Tests/VerificationTests/Reports/PhysicoChemReport.md`

Key validations:
- PHREEQC equilibrium calculations
- EQ3/6 speciation
- Experimental solubility data

### Pore Network Report

**Location:** `Tests/VerificationTests/Reports/PnmReport.md`

Key validations:
- OpenPNM permeability
- Experimental Pc curves
- LBM flow simulations

### Slope Stability Report

**Location:** `Tests/VerificationTests/Reports/SlopeStabilityReport.md`

Key validations:
- Analytical solutions (infinite slope, planar)
- Software comparisons (RocFall, STONE, UDEC)
- Historical case studies

### Acoustic Report

**Location:** `Tests/VerificationTests/Reports/AcousticReport.md`

Key validations:
- Analytical wavefield solutions
- SPECFEM3D comparison
- Laboratory measurements

---

## Running Verification Suite

### Full Suite

```bash
cd Tests/VerificationTests
dotnet test --verbosity normal
```

### With Report Generation

```bash
dotnet test --logger "html;LogFileName=report.html"
```

### Continuous Integration

The verification suite runs automatically on:
- Pull requests
- Merges to main branch
- Nightly builds

See `.github/workflows/ci.yml` for configuration.

---

## Adding Verification Tests

### Test Structure

```csharp
[TestClass]
public class MyModuleVerificationTests
{
    [TestMethod]
    [TestCategory("Verification")]
    public void AnalyticalCase_Description_MatchesExpected()
    {
        // Arrange
        var input = CreateTestInput();
        var expected = CalculateAnalyticalSolution(input);

        // Act
        var solver = new MySolver();
        var result = solver.Run(input);

        // Assert
        var error = Math.Abs(result - expected) / expected;
        Assert.IsTrue(error < 0.05,
            $"Error {error:P2} exceeds 5% threshold");
    }
}
```

### Documentation

For each verification case, document:
1. Problem description
2. Reference solution source
3. Input parameters
4. Expected results
5. Acceptance criteria

---

## Performance Benchmarks

### Computation Time

| Operation | Grid Size | CPU Time | GPU Time | Speedup |
|-----------|-----------|----------|----------|---------|
| Geothermal | 100³ | 10 min | 1 min | 10x |
| Acoustic | 200³ | 30 min | 5 min | 6x |
| Triaxial | 100³ | 15 min | 2 min | 7.5x |
| PNM Flow | 50k pores | 5 min | 30 sec | 10x |

### Memory Usage

| Dataset Type | Size | RAM Required |
|--------------|------|--------------|
| CT Stack | 512³ | 512 MB |
| CT Stack | 1024³ | 4 GB |
| PNM | 100k pores | 1 GB |
| Seismic | 1000 traces | 200 MB |

---

## Related Pages

- [User Guide](User-Guide.md) - Application documentation
- [Developer Guide](Developer-Guide.md) - Adding tests
- [API Reference](API-Reference.md) - Verification API
- [Home](Home.md) - Wiki home page
