# Thermodynamic Solver vs TOUGH Simulators - Capability Comparison

## Executive Summary

This document compares the GeoscientistToolkit thermodynamic solver capabilities with the TOUGH (Transport Of Unsaturated Groundwater and Heat) family of simulators developed at Lawrence Berkeley National Laboratory.

**Date:** 2025-11-14
**Status:** ✅ Most thermodynamic features present, ⚠️ Some multiphase flow coupling needs enhancement

---

## 1. TOUGH Simulator Family Overview

### Core TOUGH Capabilities
- **TOUGH2/TOUGH3**: Multiphase fluid and heat flow in porous/fractured media
- **TOUGHREACT**: Reactive transport with chemical equilibrium and kinetics
- **ECO2N/ECO2M**: Specialized EOS modules for H₂O-NaCl-CO₂ systems
- **Applications**: Geothermal reservoirs, CO₂ sequestration, nuclear waste, vadose zone

### Key Physics
1. **Multiphase Flow**: Simultaneous water, steam, and non-condensible gas (NCG) phases
2. **Darcy's Law**: Multiphase extension with relative permeability
3. **Heat Transfer**: Conduction + convection (sensible + latent heat)
4. **Mass Transport**: Advection + diffusion + dispersion
5. **Reactive Transport**: Mineral dissolution/precipitation feedback on flow
6. **Fractured Media**: Double-porosity, dual-permeability, MINC methods

---

## 2. Feature-by-Feature Comparison

### 2.1 Thermodynamic Equilibrium ✅ COMPLETE

| Feature | TOUGH | GeoscientistToolkit | Status |
|---------|-------|---------------------|--------|
| **Gibbs Energy Minimization** | ✓ | ✓ | ✅ Implemented |
| **Aqueous Speciation** | ✓ (PHREEQC-based) | ✓ | ✅ Complete |
| **Mineral Equilibrium** | ✓ | ✓ | ✅ Complete |
| **Gas Phase Equilibrium** | ✓ | ✓ (Henry's Law) | ✅ Complete |
| **Surface Complexation** | ✓ | ✓ (Dzombak-Morel) | ✅ Complete |
| **Solid Solutions** | ✓ | ✓ (ideal + regular) | ✅ Complete |
| **Redox Reactions** | ✓ | ✓ (pe calculations) | ✅ Complete |

**Assessment**: GeoscientistToolkit has **equivalent or superior** thermodynamic equilibrium capabilities.

---

### 2.2 Activity Coefficient Models ✅ COMPLETE

| Model | TOUGH | GeoscientistToolkit | Notes |
|-------|-------|---------------------|-------|
| **Debye-Hückel** | ✓ (extended) | ✓ (simple + extended) | Both have Davies equation |
| **Pitzer Model** | ✓ | ✓ (full implementation) | Up to 6 M ionic strength |
| **Temperature Dependence** | ✓ | ✓ (Van't Hoff + Cp) | Complete |
| **High-Salinity Brines** | ✓ | ✓ (Pitzer handles) | ✅ Complete |

**Assessment**: **Equivalent** activity coefficient capabilities.

---

### 2.3 Water/Steam Properties ✅ COMPLETE

| Property | TOUGH | GeoscientistToolkit | Status |
|----------|-------|---------------------|--------|
| **IAPWS-95** | - | ✓ | GT more rigorous |
| **IAPWS-97** | ✓ | ✓ | ✅ Both have it |
| **IAPWS 2008** | ✓ (TOUGH2 V2) | - | Minor difference |
| **Density (liquid)** | ✓ | ✓ (Region 1) | ✅ Complete |
| **Density (vapor)** | ✓ | ✓ (Region 2) | ✅ Complete |
| **Dielectric Constant** | ✓ | ✓ (Fernández 1997) | ✅ Complete |
| **Saturation Pressure** | ✓ | Partial | ⚠️ See Section 3.1 |
| **Enthalpy** | ✓ | Partial | ⚠️ See Section 3.1 |

**Assessment**: Water properties are **mostly complete**, minor enhancements needed for saturation curves.

---

### 2.4 Equation of State (EOS) Modules ⚠️ PARTIAL

| EOS Module | TOUGH | GeoscientistToolkit | Status |
|------------|-------|---------------------|--------|
| **EOS1 (pure water)** | ✓ | ✓ (IAPWS) | ✅ Complete |
| **EOS2 (H₂O-CO₂)** | ✓ | Partial | ⚠️ No phase split |
| **EOS3 (air-water)** | ✓ | - | ❌ Not implemented |
| **ECO2N (H₂O-NaCl-CO₂)** | ✓ | Partial | ⚠️ See Section 3.2 |
| **ECO2M (supercritical CO₂)** | ✓ | - | ❌ Not implemented |
| **EWASG (H₂O-salt-NCG)** | ✓ | - | ❌ Not implemented |
| **Phase Transitions** | ✓ (automatic) | - | ⚠️ See Section 3.3 |

**Assessment**: Basic EOS present, but **multiphase splits and specialized mixtures need work**.

---

### 2.5 Kinetic Reactions ✅ MOSTLY COMPLETE

| Feature | TOUGH | GeoscientistToolkit | Status |
|---------|-------|---------------------|--------|
| **Transition State Theory** | ✓ | ✓ | ✅ Complete |
| **Arrhenius Temperature** | ✓ | ✓ | ✅ Complete |
| **Empirical Rate Laws** | ✓ | ✓ (Lasaga, Palandri) | ✅ Complete |
| **Surface Area Effects** | ✓ | ✓ | ✅ Complete |
| **Saturation State (Ω)** | ✓ | ✓ | ✅ Complete |
| **Acid/Base Catalysis** | ✓ | ✓ | ✅ Complete |
| **BDF Time Integration** | ✓ | ✓ | ✅ Complete |

**Assessment**: Kinetics are **equivalent**.

---

### 2.6 Flow and Transport ⚠️ PARTIAL IMPLEMENTATION

| Feature | TOUGH | GeoscientistToolkit | Status |
|---------|-------|---------------------|--------|
| **Darcy's Law (single-phase)** | ✓ | ✓ (geothermal module) | ✅ Present |
| **Multiphase Darcy** | ✓ | - | ❌ Missing |
| **Relative Permeability** | ✓ (kr functions) | - | ❌ See Section 3.4 |
| **Capillary Pressure** | ✓ | - | ❌ See Section 3.4 |
| **Heat Conduction** | ✓ | ✓ | ✅ Complete |
| **Advective Heat Transfer** | ✓ | ✓ | ✅ Complete |
| **Latent Heat (phase change)** | ✓ | - | ⚠️ See Section 3.3 |
| **Advection-Dispersion** | ✓ | Partial | ⚠️ See Section 3.5 |
| **Diffusion** | ✓ (multiphase) | Partial | ⚠️ Limited to aqueous |

**Assessment**: Single-phase flow OK, **multiphase flow needs implementation**.

---

### 2.7 Reactive Transport Coupling ⚠️ PLACEHOLDER

| Feature | TOUGH | GeoscientistToolkit | Status |
|---------|-------|---------------------|--------|
| **Sequential Iteration** | ✓ (SIA) | Placeholder | ⚠️ See Section 3.5 |
| **Operator Splitting** | ✓ | - | ❌ Not implemented |
| **Porosity Update** | ✓ (from minerals) | Placeholder | ⚠️ See Section 3.6 |
| **Permeability Update** | ✓ (k-φ relations) | Placeholder | ⚠️ See Section 3.6 |
| **Feedback on Flow** | ✓ (fully coupled) | Placeholder | ⚠️ See Section 3.6 |

**Assessment**: **Major gap** - reactive transport coupling is not fully implemented.

---

### 2.8 Spatial Discretization ✅ PRESENT

| Feature | TOUGH | GeoscientistToolkit | Status |
|---------|-------|---------------------|--------|
| **3D Finite Difference** | ✓ | ✓ | ✅ Geothermal module |
| **Cylindrical Coordinates** | ✓ | ✓ | ✅ For boreholes |
| **Cartesian Grid** | ✓ | ✓ | ✅ CT-based |
| **Adaptive Time Stepping** | ✓ | ✓ (CFL-based) | ✅ Complete |
| **Newton-Raphson** | ✓ | ✓ | ✅ Complete |

**Assessment**: Spatial discretization is **adequate**.

---

### 2.9 Fractured Media ❌ NOT IMPLEMENTED

| Feature | TOUGH | GeoscientistToolkit | Status |
|---------|-------|---------------------|--------|
| **Double Porosity** | ✓ | - | ❌ Not present |
| **Dual Permeability** | ✓ | - | ❌ Not present |
| **MINC (Multiple INteracting Continua)** | ✓ | - | ❌ Not present |
| **Discrete Fracture Networks** | ✓ (TOUGH-FLAC) | - | ❌ Not present |

**Assessment**: **Not implemented** - would require new module.

---

### 2.10 Compound Database ✅ EXCELLENT

| Database | TOUGH | GeoscientistToolkit | Status |
|----------|-------|---------------------|--------|
| **Mineral Thermodynamics** | ✓ (PHREEQC) | ✓ (Holland-Powell, PHREEQC) | ✅ Excellent |
| **Aqueous Species** | ✓ | ✓ (60+ species) | ✅ Complete |
| **Gas Species** | ✓ | ✓ (SUPCRT92) | ✅ Complete |
| **Surface Species** | ✓ | ✓ | ✅ Complete |
| **Temperature Extrapolation** | ✓ | ✓ | ✅ Complete |

**Assessment**: Database quality is **excellent**, potentially better than standard TOUGH databases.

---

### 2.11 Performance Optimization ✅ SUPERIOR

| Feature | TOUGH | GeoscientistToolkit | Status |
|---------|-------|---------------------|--------|
| **Parallel Computing** | ✓ (TOUGH2-MP) | - | ❌ CPU only (no MPI) |
| **GPU Acceleration** | - | ✓ (OpenCL) | ✅ GT advantage |
| **SIMD Vectorization** | - | ✓ (AVX2, NEON) | ✅ GT advantage |
| **Sparse Matrix Solvers** | ✓ | ✓ | ✅ Both |

**Assessment**: GT has **superior GPU/SIMD** optimization, TOUGH has better **MPI parallelism**.

---

## 3. Missing Features and Implementation Recommendations

### 3.1 Enhanced Water/Steam Properties (PRIORITY: MEDIUM)

**What's Missing:**
- Saturation pressure curve (P_sat vs T)
- Enthalpy functions for liquid and vapor
- Entropy calculations
- Phase boundary detection

**Implementation:**
```csharp
// Add to WaterPropertiesIAPWS.cs
public static double GetSaturationPressure(double T_K);
public static double GetSaturationTemperature(double P_MPa);
public static double GetEnthalpy(double T_K, double P_MPa, WaterPhase phase);
public static double GetEntropy(double T_K, double P_MPa, WaterPhase phase);
```

**Effort**: 2-3 days
**Impact**: Required for phase transition calculations

---

### 3.2 Multiphase EOS Module (PRIORITY: HIGH)

**What's Missing:**
- Automatic phase split for H₂O-CO₂-NaCl systems
- Supercritical CO₂ properties
- Non-condensible gas (NCG) solubility in brine
- Mutual solubility (CO₂ in water vs water in CO₂ phase)

**Implementation:**
Create new file: `Analysis/Thermodynamic/MultiphaseEOS.cs`

Key classes:
- `ECO2N_EOS` - Water-NaCl-CO2 phase behavior (like TOUGH's ECO2N)
- `PhaseEnvelope` - P-T-x phase boundary calculation
- `FlashCalculation` - Two-phase split at given P-T-z

**Effort**: 1-2 weeks
**Impact**: Critical for CO₂ sequestration simulations

---

### 3.3 Phase Transition Handler (PRIORITY: HIGH)

**What's Missing:**
- Detection of boiling/condensation
- Latent heat release/absorption
- Automatic switch between liquid/vapor EOS
- Dew point and bubble point calculations

**Implementation:**
```csharp
public class PhaseTransitionHandler
{
    public (Phase dominantPhase, double vaporFraction) DeterminePhase(
        double T_K, double P_MPa, Composition composition);

    public double CalculateLatentHeat(double T_K, double P_MPa);

    public bool IsBoiling(double T_K, double P_MPa);
}
```

**Effort**: 3-5 days
**Impact**: Essential for geothermal applications

---

### 3.4 Relative Permeability and Capillary Pressure (PRIORITY: HIGH)

**What's Missing:**
- kr-S relationships (Corey, Brooks-Corey, van Genuchten)
- Capillary pressure Pc(S) functions
- Hysteresis effects
- Residual saturations

**Implementation:**
Create: `Analysis/Multiphase/RelativePermeability.cs`

```csharp
public class RelativePermeabilityModels
{
    public static double Corey(double S_eff, double n);
    public static double VanGenuchten(double S_eff, double m);
    public static double BrooksCorey(double S_eff, double lambda);
}

public class CapillaryPressure
{
    public static double VanGenuchten(double S_eff, double alpha, double m);
    public static double Leverett(double S_eff, double permeability, double porosity);
}
```

**Effort**: 3-4 days
**Impact**: Required for multiphase flow

---

### 3.5 Reactive Transport Solver (PRIORITY: CRITICAL)

**What's Missing:**
- Full coupling of flow + transport + reactions
- Advection-dispersion-reaction (ADR) solver
- Mass balance for each component across phases
- Operator splitting schemes

**Implementation:**
Create: `Analysis/Thermodynamic/ReactiveTransportSolver.cs`

Replace placeholder in `GeothermalThermodynamicsIntegration.cs` with:

```csharp
public class ReactiveTransportSolver
{
    // Sequential Iterative Approach (like TOUGHREACT)
    public void SolveTransportStep(
        float[,,] velocity,
        float[,,] concentration,
        float[,,] porosity,
        double dt);

    public void SolveReactionStep(
        float[,,] concentration,
        float[,,] temperature,
        float[,,] pressure,
        double dt);

    // Feedback
    public void UpdatePorosityFromMinerals(Dictionary<string, float[,,]> minerals);
    public void UpdatePermeabilityFromPorosity(float[,,] porosity, float[,,] permeability);
}
```

**Effort**: 2-3 weeks
**Impact**: **CRITICAL** - this is the main gap vs TOUGHREACT

---

### 3.6 Porosity-Permeability Coupling (PRIORITY: HIGH)

**What's Missing:**
- Kozeny-Carman relationship: k = k₀(φ/φ₀)³ · [(1-φ₀)/(1-φ)]²
- Cubic law for fractures: k_f = b²/12
- Verma-Pruess model
- Carmen-Kozeny with tortuosity

**Implementation:**
```csharp
public class PorosityPermeabilityCoupling
{
    public static double KozenyCarman(double phi, double phi0, double k0);
    public static double CubicLaw(double aperture);
    public static double VermaPruess(double phi, double phi_crit, double k0);
}
```

**Effort**: 1-2 days
**Impact**: Essential for reactive transport feedback

---

### 3.7 Multiphase Darcy Flow (PRIORITY: HIGH)

**What's Missing:**
- Darcy velocity for each phase: v_α = -(k·kr_α/μ_α)·(∇P_α - ρ_α·g)
- Phase mobilities
- Pressure coupling (P_α = P_ref + Pc_α)

**Implementation:**
Extend `GeothermalSimulationSolver.cs`:

```csharp
public class MultiphaseDarcySolver
{
    public void SolveMultiphaseFlow(
        float[,,] saturation_water,
        float[,,] saturation_steam,
        float[,,] saturation_gas,
        float[,,] pressure,
        float[,,] temperature,
        double dt);

    private float[,,] CalculatePhaseVelocity(
        Phase phase,
        float[,,] saturation,
        float[,,] pressure,
        float[,,] viscosity);
}
```

**Effort**: 1-2 weeks
**Impact**: Core multiphase capability

---

## 4. Priority Implementation Roadmap

### Phase 1: Foundation (1-2 weeks)
1. ✅ Enhanced IAPWS water/steam properties (saturation curve, enthalpy)
2. ✅ Phase transition handler (boiling/condensation)
3. ✅ Relative permeability models

### Phase 2: Multiphase Flow (2-3 weeks)
4. ✅ Multiphase Darcy solver
5. ✅ Capillary pressure functions
6. ✅ Porosity-permeability coupling

### Phase 3: Reactive Transport (3-4 weeks)
7. ✅ Full reactive transport coupling (replace placeholders)
8. ✅ Advection-dispersion solver
9. ✅ Mineral feedback on flow properties

### Phase 4: Advanced EOS (2-3 weeks - OPTIONAL)
10. ECO2N-like H₂O-NaCl-CO₂ EOS
11. Supercritical CO₂ properties
12. Flash calculations for phase splits

---

## 5. Summary Assessment

### Strengths of GeoscientistToolkit vs TOUGH
1. ✅ **Superior thermodynamic database** (Holland-Powell + PHREEQC + SUPCRT92)
2. ✅ **More rigorous activity models** (full Pitzer implementation)
3. ✅ **Better performance optimization** (GPU/SIMD acceleration)
4. ✅ **Modern codebase** (C# vs Fortran)
5. ✅ **Excellent kinetics solver** (equivalent to TOUGHREACT)

### Gaps Compared to TOUGH
1. ❌ **Multiphase flow** - single phase only currently
2. ❌ **Reactive transport coupling** - placeholders exist but not implemented
3. ❌ **Specialized EOS modules** - no ECO2N/ECO2M equivalents
4. ❌ **Fractured media** - no double porosity/MINC
5. ⚠️ **Phase transitions** - not automatic

### Overall Verdict
**GeoscientistToolkit has SUPERIOR thermodynamics but INFERIOR flow/transport coupling.**

To match TOUGH capabilities, implement **Phases 1-3** of the roadmap (6-9 weeks effort).

---

## 6. References

### TOUGH Manuals
- Pruess, K., Oldenburg, C., & Moridis, G. (2012). TOUGH2 User's Guide Version 2.1. LBNL-43134.
- Jung, Y., Pau, G. S. H., Finsterle, S., & Pollyea, R. M. (2017). TOUGH3 User's Guide. LBNL-2001093.
- Xu, T., Spycher, N., Sonnenthal, E., Zhang, G., Zheng, L., & Pruess, K. (2011). TOUGHREACT Version 2.0. LBNL-DRAFT.

### EOS Modules
- Spycher, N., & Pruess, K. (2005). ECO2N: A TOUGH2 fluid property module for mixtures of water, NaCl, and CO₂. LBNL-57952.
- Spycher, N., & Pruess, K. (2010). ECO2M: A TOUGH2 Fluid Property Module for Mixtures of Water, NaCl, and CO₂. LBNL-3341E.

### Water Properties
- Wagner, W., & Pruß, A. (2002). The IAPWS formulation 1995 for the thermodynamic properties of ordinary water substance. J. Phys. Chem. Ref. Data, 31(2), 387-535.
- Fernández, D. P., et al. (1997). A formulation for the static permittivity of water and steam. J. Phys. Chem. Ref. Data, 26(4), 1125-1166.

---

**Document Version**: 1.0
**Last Updated**: 2025-11-14
**Next Review**: After Phase 1 implementation
