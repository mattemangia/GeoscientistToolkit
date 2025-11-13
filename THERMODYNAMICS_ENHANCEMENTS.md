# Thermodynamic System Enhancements

## Overview
Major enhancements to the thermodynamic simulation system, adding 60+ new compounds, new GeoScript commands, and improved reaction calculations.

## New Features

### 1. Extended Compound Database (60+ new compounds)
**File**: `Business/CompoundLibraryExtensions.cs`

Added comprehensive thermodynamic data from authoritative sources:
- **Holland & Powell (2011)**: Silicate minerals (olivines, pyroxenes, feldspars)
- **PHREEQC database**: Aqueous species and evaporite minerals
- **Robie & Hemingway (1995)**: Carbonate, sulfate, and oxide minerals
- **SUPCRT92**: Gas species

**New compounds include**:
- **Silicates**: Forsterite, Fayalite, Enstatite, Ferrosilite, Diopside, Albite, Anorthite, Orthoclase
- **Carbonates**: Magnesite, Siderite, Rhodochrosite, Strontianite
- **Sulfates**: Anhydrite, Barite, Celestite, Anglesite
- **Chlorides**: Sylvite, Carnallite, Bischofite
- **Oxides**: Corundum, Periclase, Wustite, Magnetite, Chromite
- **Aqueous ions**: Sr²⁺, Ba²⁺, Mn²⁺, Zn²⁺, Pb²⁺, Al³⁺, NO₃⁻, PO₄³⁻, F⁻, Br⁻
- **Gases**: N₂, O₂, CH₄, H₂, H₂S, NH₃

### 2. New GeoScript Commands
**File**: `Business/GeoScriptThermodynamicsExtensions.cs`

#### `CALCULATE_PHASES`
Separates thermodynamic equilibrium results into solid, aqueous, and gas phases.

**Usage**:
```
CALCULATE_PHASES
```

**Output columns**:
- `SolidPhase_g`: Total mass of solid phase
- `AqueousPhase_g`: Total mass of aqueous phase
- `GasPhase_g`: Total mass of gas phase
- `SolidMinerals`: List of minerals with quantities
- `AqueousIons`: List of aqueous species with quantities
- `Gases`: List of gas species with quantities

#### `CALCULATE_CARBONATE_ALKALINITY`
Calculates HCO₃⁻ and CO₃²⁻ from total alkalinity and pH using carbonate equilibria.

**Usage**:
```
CALCULATE_CARBONATE_ALKALINITY ALKALINITY_COL 'ColumnName' [TEMP_COL 'TempColumn'] [PH_COL 'pHColumn']
```

**Features**:
- Temperature-dependent equilibrium constants (Millero 1995)
- Calculates complete carbonate speciation (H₂CO₃, HCO₃⁻, CO₃²⁻)
- Outputs DIC (Dissolved Inorganic Carbon)

**Theory**:
Based on carbonate equilibrium system:
- pKa₁ (H₂CO₃ ⇌ HCO₃⁻ + H⁺) ~ 6.35 at 25°C
- pKa₂ (HCO₃⁻ ⇌ CO₃²⁻ + H⁺) ~ 10.33 at 25°C
- Uses alpha factors: α₀ (H₂CO₃*), α₁ (HCO₃⁻), α₂ (CO₃²⁻)
- Alkalinity = [HCO₃⁻] + 2[CO₃²⁻]

### 3. Enhanced REACT Command
**File**: `Business/GeoScript.cs` (modified)

The `REACT` command now provides:
- **Phase separation**: Groups products by phase (Solid/Aqueous/Gas)
- **Mineral identification**: Labels solid phases as "(mineral)"
- **Extended data**: Shows chemical formula, mass, and mole fractions
- **Summary statistics**: Logs total moles per phase, pH, pe, ionic strength

**Example**:
```
REACT CaCO3+SiO2 TEMP 800 K
```
Output includes:
- CaSiO₃ (mineral) - Wollastonite
- CO₂(g) - Carbon dioxide gas

## System Architecture

### Thermodynamic Solver Workflow
```
User Input (REACT, EQUILIBRATE, etc.)
    ↓
Create ThermodynamicState (initial composition)
    ↓
ReactionGenerator.GenerateReactions()
    ↓
ThermodynamicSolver.SolveEquilibrium()
    - Gibbs Energy Minimization
    - Element Conservation Constraints
    - Newton-Raphson Iteration
    ↓
Final State (equilibrium composition)
    ↓
Phase Separation & Output
```

### Key Classes
- **CompoundLibrary**: Singleton containing all compound data
- **ThermodynamicSolver**: Gibbs minimization solver
- **ReactionGenerator**: Automatically generates reactions from thermodynamic data
- **ActivityCoefficientCalculator**: Calculates non-ideal solution behavior

## Scientific Validation

### Thermodynamic Consistency
All data is internally consistent within each source:
- Holland & Powell (2011): Internally consistent dataset for metamorphic rocks
- PHREEQC: Validated against WATEQ4F and MINTEQ databases
- Standard state: 298.15 K, 1 bar

### Key Equations

**Gibbs Energy Minimization**:
```
Minimize: G = Σᵢ nᵢμᵢ
Subject to: Aᵢⱼnⱼ = bᵢ (element conservation)
           nⱼ ≥ 0 (non-negativity)
```

**Chemical Potential**:
```
μᵢ = μᵢ° + RT ln(aᵢ)
```

**Van't Hoff Equation** (temperature correction):
```
ln K(T) = ln K(T₀) - (ΔH°/R)(1/T - 1/T₀) + (ΔCp°/R)[T₀/T - 1 - ln(T₀/T)]
```

**Carbonate Speciation**:
```
α₀ = [H⁺]² / D
α₁ = [H⁺]Ka₁ / D
α₂ = Ka₁Ka₂ / D
where D = [H⁺]² + [H⁺]Ka₁ + Ka₁Ka₂
```

## Usage Examples

### Example 1: Simple Dissolution
```geoscript
REACT NaCl TEMP 25 C
```
**Expected output**:
- Aqueous: Na⁺, Cl⁻
- pH ≈ 7 (neutral salt)

### Example 2: Mineral Formation
```geoscript
REACT CaCO3+SiO2 TEMP 800 K PRES 1 BAR
```
**Expected output**:
- Solid (mineral): CaSiO₃ (Wollastonite)
- Gas: CO₂(g)

### Example 3: Phase Analysis
```geoscript
EQUILIBRATE |> CALCULATE_PHASES
```
Equilibrates water chemistry then separates into phases.

### Example 4: Carbonate System
```geoscript
CALCULATE_CARBONATE_ALKALINITY ALKALINITY_COL 'Alk_mgL_CaCO3' PH_COL 'pH' TEMP_COL 'Temp_C'
```
Calculates HCO₃⁻ and CO₃²⁻ from field alkalinity measurements.

## Future Enhancements

### Suggested Additions
1. **Solid Solutions**: Full implementation of non-ideal solid solution models (Margules, Redlich-Kister)
2. **Kinetics**: Dissolution/precipitation rate laws (Lasaga, Steefel)
3. **Pitzer Model**: High ionic strength corrections (I > 1 M)
4. **Eh-pH Diagrams**: Pourbaix diagram generation
5. **Reaction Path Modeling**: Time-series mineral precipitation sequences
6. **Surface Complexation**: Extended triple-layer model (TLM)

### Additional Data Sources to Consider
- **Berman (1988)**: Alternative metamorphic database
- **Holland-Powell (1998)**: Earlier version for comparison
- **Shock et al. (1997)**: High temperature aqueous species
- **Thermoddem (BRGM)**: European geochemical database

## References

1. Holland, T.J.B. & Powell, R., 2011. An improved and extended internally consistent thermodynamic dataset for phases of petrological interest. Journal of Metamorphic Geology, 29(3), 333-383.

2. Parkhurst, D.L. & Appelo, C.A.J., 2013. Description of input and examples for PHREEQC version 3. USGS Techniques and Methods, Book 6, Chapter A43.

3. Robie, R.A. & Hemingway, B.S., 1995. Thermodynamic Properties of Minerals and Related Substances at 298.15 K and 1 Bar. USGS Bulletin 2131.

4. Millero, F.J., 1995. Thermodynamics of the carbon dioxide system in the oceans. Geochimica et Cosmochimica Acta, 59(4), 661-677.

5. Stumm, W. & Morgan, J.J., 1996. Aquatic Chemistry, 3rd ed. Wiley-Interscience.

6. Bethke, C.M., 2008. Geochemical and Biogeochemical Reaction Modeling, 2nd ed. Cambridge University Press.

7. Johnson, J.W., Oelkers, E.H. & Helgeson, H.C., 1992. SUPCRT92: A software package for calculating standard molal thermodynamic properties. Computers & Geosciences, 18, 899-947.

## Implementation Notes

### Code Quality
- All thermodynamic data includes source citations
- Follows existing code patterns and conventions
- Uses rigorous scientific algorithms (not empirical fits)
- Extensive inline documentation with references

### Performance Considerations
- Compound library uses singleton pattern for efficiency
- Reaction generation is cached during solver runs
- SVD decomposition for robust matrix solutions
- Line search with backtracking for convergence

### Testing Recommendations
1. Verify NaCl dissolution → Na⁺ + Cl⁻
2. Check calcite solubility vs. temperature
3. Validate pH calculations against buffer solutions
4. Compare with PHREEQC output for same input


