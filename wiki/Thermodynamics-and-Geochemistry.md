# Thermodynamics and Geochemistry

Guide for thermodynamic calculations, phase diagrams, and geochemical modeling in Geoscientist's Toolkit.

---

## Overview

The Thermodynamics module provides:
- Chemical equilibrium calculations
- Phase diagram generation
- Mineral saturation analysis
- Evaporation modeling
- Reaction balancing
- Extended compound library

---

## Core Architecture

Thermodynamic workflows are built around a few core components:

- **CompoundLibrary**: central database of thermodynamic species and phase metadata.
- **ReactionGenerator**: parses formulas and builds reaction networks.
- **ThermodynamicSolver**: Gibbs free-energy minimization and aqueous speciation.
- **PhaseDiagramGenerator**: creates binary/ternary/P–T/energy diagrams.
- **Kinetics/Reactive Transport**: time-stepping and coupled mass transfer tools.

These are used by GeoScript commands and by higher-level modules like PhysicoChem and PNM reactive transport.

---

## Thermodynamic State Model

Thermodynamic calculations use a state object that contains:

- **Temperature (K)** and **pressure (bar)**
- **Volume (L)** or total water amount
- **Elemental composition** (element → moles)
- **Species moles** (species → moles)
- **Activities, pH, pe, ionic strength**

This state is generated either from **GeoScript input** (e.g., `EQUILIBRATE`, `REACT`) or from **Table datasets** with species columns and temperature/pressure metadata.

---

## GeoScript Thermodynamics Commands

Common commands include:

- `EQUILIBRATE` – equilibrium speciation and phase separation
- `REACT` – chemical reaction equilibrium for custom reactions
- `SATURATION` / `SATURATION_INDEX` – mineral saturation states
- `EVAPORATE` – progressive concentration/precipitation modeling
- `BALANCE_REACTION` – stoichiometric balancing
- `CREATE_DIAGRAM` – phase diagrams (binary/ternary/PT/energy)
- `CALCULATE_PHASES` – split results into solid/aqueous/gas phases
- `CALCULATE_CARBONATE_ALKALINITY` – carbonate speciation from alkalinity
- `DIAGNOSTIC_THERMO` – solver diagnostics and benchmarks

---

## Compound Library

### Overview

The toolkit includes an extensive thermodynamic database with 60+ compounds:

| Category | Examples |
|----------|----------|
| Silicates | Quartz, Feldspar, Olivine, Pyroxene |
| Carbonates | Calcite, Dolomite, Aragonite |
| Sulfates | Gypsum, Anhydrite, Barite |
| Chlorides | Halite, Sylvite |
| Oxides | Hematite, Magnetite, Goethite |
| Aqueous Ions | Ca²⁺, Mg²⁺, Na⁺, Cl⁻, SO₄²⁻ |
| Gases | CO₂, O₂, H₂S |

### Data Sources

- Holland & Powell (thermodynamic dataset)
- PHREEQC database
- Robie & Hemingway (1995)
- SUPCRT92

### Accessing the Library

Go to **Tools → Compound Library** to:
- Browse available compounds
- View thermodynamic properties
- Add custom compounds
- Edit kinetic parameters

---

## Chemical Equilibrium

### Equilibrium Solver

Calculate aqueous speciation at equilibrium.

**Input:**
- Total element concentrations
- Temperature and pressure
- pH (or charge balance)

**Output:**
- Species concentrations
- Activity coefficients
- pH and ionic strength
- Saturation indices

### GeoScript Command

```geoscript
EQUILIBRATE |>
  SATURATION MINERALS 'Calcite', 'Dolomite', 'Gypsum'
```

### Unit Conversion

The system automatically converts concentration units:

| Unit | Type | Conversion |
|------|------|-----------|
| mg/L, ppm | Mass | to mol/L using molar mass |
| µg/L, ppb | Mass | to mol/L using molar mass |
| g/L | Mass | to mol/L using molar mass |
| mol/L, M | Molar | No conversion |
| mmol/L | Molar | / 1000 |
| µmol/L | Molar | / 1,000,000 |

**Column header format:** `"Ca (mg/L)"` or `"Na [ppm]"`

---

## Phase Diagrams

### Diagram Types

| Type | Description | Example |
|------|-------------|---------|
| Binary | Two-component system | NaCl-H₂O |
| Ternary | Three-component system | SiO₂-Al₂O₃-CaO |
| P-T | Pressure-Temperature | CO₂ phase diagram |
| Activity-Activity | Ion activity diagram | Ca²⁺ vs CO₃²⁻ |
| Gibbs Energy | Mixing energy | Solid solutions |

### GeoScript Commands

```geoscript
# Binary phase diagram
CREATE_DIAGRAM BINARY FROM 'NaCl' AND 'H2O' TEMP 298 K PRES 1 BAR

# Ternary diagram
CREATE_DIAGRAM TERNARY FROM 'SiO2', 'Al2O3', 'CaO' TEMP 1500 K PRES 1 BAR

# P-T diagram
CREATE_DIAGRAM PT FOR COMP('H2O'=1.0,'CO2'=0.1) T_RANGE(273-373) K P_RANGE(1-100) BAR

# Gibbs energy diagram
CREATE_DIAGRAM ENERGY FROM 'CaCO3' AND 'MgCO3' TEMP 298 K PRES 1 BAR
```

---

## Saturation Analysis

### Saturation Index

Calculate mineral saturation state:

```
SI = log₁₀(IAP/Ksp)

Where:
- IAP = Ion Activity Product
- Ksp = Solubility Product

Interpretation:
- SI > 0: Supersaturated (precipitation)
- SI = 0: At equilibrium
- SI < 0: Undersaturated (dissolution)
```

### GeoScript Command

```geoscript
SATURATION MINERALS 'Calcite', 'Dolomite', 'Gypsum', 'Halite'
```

**Output columns:** `SI_Calcite`, `SI_Dolomite`, etc.

---

## Reaction Modeling

### Reaction Command

Calculate equilibrium products of reactants.

```geoscript
# Simple reaction
REACT H2O

# Salt dissolution
REACT NaCl
REACT CaCl2 + Na2CO3

# Mineral-acid reaction
REACT CaCO3 + HCl TEMP 25 C PRES 1 BAR

# Complex species
REACT Al2Si2O5(OH)4 + H2SO4 TEMP 350 K
```

### Formula Notation

Chemical formulas can use:
- Plain numbers: `H2O`, `NaCl`, `CaCO3`
- Subscripts: `H₂O`, `NaCl`, `CaCO₃`

Both formats are automatically normalized to match the database.

### Output

Results organized by phase:
- Solid products (minerals)
- Aqueous products (dissolved species)
- Gas products
- Moles, mass, and mole fractions

---

## Reactive Transport and Kinetics

Thermodynamic solvers are also used in time-stepping contexts:

- **Reactive transport**: coupled advection/diffusion and reaction updates
- **Kinetics**: reaction-rate control for mineral dissolution/precipitation
- **Multiphase coupling**: EOS-aware speciation in PhysicoChem and PNM workflows

These solvers use the same compound library and phase rules, ensuring consistency between equilibrium and transient models.

---

## Evaporation Modeling

### EVAPORATE Command

Simulate evaporation and mineral precipitation.

```geoscript
# Simulate 10x evaporation
EVAPORATE UPTO 10x STEPS 50 MINERALS 'Halite', 'Gypsum', 'Calcite'

# Seawater evaporation
EVAPORATE UPTO 100x STEPS 100 MINERALS 'Halite', 'Gypsum', 'Calcite', 'Dolomite'
```

### Output

Table showing at each step:
- Evaporation factor
- Remaining volume
- pH
- Ionic strength
- Precipitated mineral amounts

---

## Reaction Balancing

### BALANCE_REACTION Command

Generate balanced dissolution reactions.

```geoscript
BALANCE_REACTION 'Calcite'
BALANCE_REACTION 'Dolomite'
```

### Output

- Balanced reaction equation
- Equilibrium constant (log K)
- Stoichiometric coefficients

---

## Complete Workflow Example

### Water Chemistry Analysis

```geoscript
# 1. Load water chemistry data
LOAD "water_samples.csv" AS "WaterData"

# 2. Calculate equilibrium speciation
WITH "WaterData" DO
    EQUILIBRATE
TO "Equilibrated"

# 3. Calculate saturation indices
WITH "Equilibrated" DO
    SATURATION MINERALS 'Calcite', 'Dolomite', 'Gypsum', 'Halite', 'Barite'
TO "Saturation_Results"

# 4. Identify waters prone to scaling
WITH "Saturation_Results" DO
    SELECT WHERE 'SI_Calcite' > 0 OR 'SI_Gypsum' > 0
TO "Scaling_Waters"
```

### Evaporation Sequence

```geoscript
# Simulate seawater evaporation
LOAD "seawater.csv" AS "Seawater"

WITH "Seawater" DO
    EVAPORATE UPTO 100x STEPS 200 MINERALS
        'Calcite', 'Gypsum', 'Halite', 'Sylvite', 'Carnallite'
TO "Evap_Sequence"
```

---

## GeoScript Thermo Commands Summary

| Command | Description |
|---------|-------------|
| `EQUILIBRATE` | Calculate aqueous equilibrium |
| `SATURATION` | Calculate mineral SI |
| `CREATE_DIAGRAM` | Generate phase diagram |
| `BALANCE_REACTION` | Balance mineral reaction |
| `EVAPORATE` | Simulate evaporation |
| `REACT` | Calculate reaction products |
| `CALCULATE_PHASES` | Phase-separated output |
| `CALCULATE_CARBONATE_ALKALINITY` | Carbonate speciation |

---

## Advanced Features

### Solid Solutions

Model continuous mineral compositions:
- Plagioclase (Ab-An)
- Calcite-Dolomite
- Olivine (Fo-Fa)

### Activity Coefficients

Available models:
- Debye-Hückel (dilute solutions)
- Davies (moderate ionic strength)
- Pitzer (high ionic strength, brines)

### Temperature Dependence

Properties vary with temperature using:
- Heat capacity polynomials
- Van't Hoff equation
- Empirical fits

---

## Troubleshooting

### Equilibrium Doesn't Converge

**Solutions:**
- Check initial concentrations are reasonable
- Verify element charges balance
- Try different initial pH guess
- Check for incompatible mineral assemblage

### Unrealistic SI Values

**Check:**
- Concentration units are correct
- Temperature is appropriate
- Activity model is suitable for ionic strength

### Missing Compounds

**Solutions:**
- Add custom compound to library
- Check spelling matches database
- Use alternative compound name

---

## References

- Holland, T. J. B., & Powell, R. (2011). An improved and extended internally consistent thermodynamic dataset for phases of petrological interest. *Journal of Metamorphic Geology, 29*(3), 333–383. https://doi.org/10.1111/j.1525-1314.2010.00923.x
- Parkhurst, D. L., & Appelo, C. A. J. (2013). *Description of input and examples for PHREEQC version 3*. U.S. Geological Survey Techniques and Methods, Book 6, Chapter A43. https://doi.org/10.3133/tm6A43
- Bethke, C. M. (2008). *Geochemical and biogeochemical reaction modeling* (2nd ed.). Cambridge University Press. https://doi.org/10.1017/CBO9780511619670
- Nordstrom, D. K., & Munoz, J. L. (1994). *Geochemical thermodynamics* (2nd ed.). Blackwell Scientific Publications.

---

## Related Pages

- [PhysicoChem Reactors](PhysicoChem-Reactors.md) - Reactor simulation
- [Pore Network Modeling](Pore-Network-Modeling.md) - Reactive transport
- [GeoScript Manual](GeoScript-Manual.md) - Scripting reference
- [Home](Home.md) - Wiki home page
