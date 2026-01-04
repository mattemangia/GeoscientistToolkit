# Nuclear Reactor Simulation System Guide

## Overview

The GeoscientistToolkit includes a comprehensive nuclear reactor simulation module integrated with the PhysicoChem framework. This system enables simulation of nuclear fission reactors for power generation, including:

- **Reactor Types**: PWR, BWR, PHWR (CANDU), HTGR, LMFBR, Research reactors
- **Physics**: Neutron diffusion, point kinetics, thermal-hydraulics
- **Moderators**: Light water (H2O), Heavy water (D2O), Graphite, Beryllium
- **Control Systems**: Control rods, soluble boron, xenon/samarium poisoning
- **Visualization**: 2D/3D neutron flux, power distribution, temperature fields

## Quick Start

### Creating a PWR Reactor

```csharp
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Analysis.PhysicoChem;

// Create reactor parameters
var reactor = new NuclearReactorParameters();
reactor.InitializePWR();

// Create solver
var solver = new NuclearReactorSolver(reactor);

// Solve criticality
double keff = solver.SolveCriticality();
Console.WriteLine($"keff = {keff:F5}");

// Run transient
var history = solver.RunTransient(endTime: 100, dt: 0.01);
```

### Creating a CANDU Reactor (Heavy Water)

```csharp
var candu = new NuclearReactorParameters();
candu.InitializeCANDU();

// CANDU uses natural uranium (0.71% enrichment)
// due to excellent D2O moderator properties
Console.WriteLine($"Fuel enrichment: {candu.FuelAssemblies[0].EnrichmentPercent}%");
Console.WriteLine($"Moderator: {candu.Moderator.Type}"); // HeavyWater
```

## Reactor Types

### PWR (Pressurized Water Reactor)
- **Power**: 3411 MWth / 1150 MWe typical
- **Moderator/Coolant**: Light water (H2O) at 15.5 MPa
- **Fuel**: Enriched uranium (3-5% U-235)
- **Core**: 193 fuel assemblies, 17x17 rod array

### PHWR/CANDU
- **Power**: 2064 MWth / 700 MWe (CANDU-6)
- **Moderator**: Heavy water (D2O) at low pressure
- **Coolant**: Heavy water at 10 MPa
- **Fuel**: Natural uranium (0.71% U-235)
- **Advantage**: No enrichment required due to low D2O absorption

## Key Physics Models

### 1. Neutron Diffusion (Two-Group)

The solver implements two-group neutron diffusion:

```
Fast group:  -D₁∇²φ₁ + (Σa₁ + Σs₁₂)φ₁ = χ₁νΣf(φ₁ + φ₂)/keff
Thermal group: -D₂∇²φ₂ + Σa₂φ₂ = Σs₁₂φ₁
```

### 2. Point Kinetics (Six-Group Delayed Neutrons)

```
dn/dt = (ρ - β)/Λ × n + Σᵢ λᵢCᵢ
dCᵢ/dt = βᵢ/Λ × n - λᵢCᵢ
```

Where:
- n = neutron density (relative power)
- ρ = reactivity
- β = total delayed neutron fraction (0.0065 for U-235)
- Λ = neutron generation time (~100 μs for PWR)
- Cᵢ = precursor concentration for group i
- λᵢ = decay constant for group i

### 3. Xenon-135 Dynamics

```
dI/dt = γI × Σf × φ - λI × I
dXe/dt = γXe × Σf × φ + λI × I - λXe × Xe - σXe × φ × Xe
```

Xe-135 has a huge absorption cross section (2.65 Mbarn) causing:
- Equilibrium poisoning: ~2500 pcm
- Peak xenon after shutdown: ~11 hours ("iodine pit")

## Moderator Properties

| Property | H2O (Light) | D2O (Heavy) | Graphite |
|----------|-------------|-------------|----------|
| σ_absorption (barn) | 0.664 | 0.0013 | 0.0034 |
| σ_scattering (barn) | 49.2 | 10.6 | 4.7 |
| Moderation ratio | 71 | 5670 | 192 |
| ξ (energy decrement) | 0.920 | 0.509 | 0.158 |
| Collisions to thermalize | 18 | 35 | 115 |

Heavy water's extremely low absorption allows natural uranium to achieve criticality, while light water requires enriched fuel.

## Control Systems

### Control Rods
- **Materials**: Ag-In-Cd, B4C, Hafnium
- **Function**: Absorb neutrons, reduce keff
- **Worth**: Typically 500-2000 pcm per bank
- **Insertion curve**: S-shaped integral worth

### Soluble Boron (PWR)
- **Concentration**: 0-2000 ppm
- **Worth**: ~-10 pcm/ppm
- **Use**: Slow reactivity control, burnup compensation

### SCRAM (Emergency Shutdown)
- Rapid full insertion of all control rods
- Triggered by: High power, short period, high temperature

## Thermal-Hydraulics

### Fuel Temperature Limits
- Max fuel centerline: 2800°C (melting at 2865°C)
- Max cladding: 1200°C (Zircaloy)
- Min DNBR: 1.3 (Departure from Nucleate Boiling)

### Coolant Parameters (PWR)
- Inlet: 292°C
- Outlet: 326°C
- Pressure: 15.5 MPa
- Mass flow: 17,400 kg/s

### Heat Transfer
```
Q = ṁ × cp × ΔT
```

For PWR: 17400 × 5500 × 34 = 3.25 GW (thermal power)

## UI Components

### GTK Dialog: NuclearReactorConfigDialog

The configuration dialog provides:
1. **Core Design Tab**: Reactor type, power ratings, geometry
2. **Moderator/Coolant Tab**: Moderator type, D2O purity, coolant parameters
3. **Fuel Tab**: Enrichment, rod geometry, cladding material
4. **Control Tab**: Control rod banks, boron concentration
5. **Safety Tab**: SCRAM setpoints, ECCS configuration

### ImGui Visualizer: NuclearReactorVisualizer

Provides real-time visualization:
- **2D Axial Slice**: Vertical cross-section of core
- **2D Radial Slice**: Horizontal cross-section at selected height
- **3D Core View**: Isometric view with rotation
- **Time Charts**: Power, reactivity, temperature, xenon history

### Control Panel
- Control rod position sliders
- Boron concentration adjustment
- External reactivity perturbation
- Simulation start/pause/reset
- SCRAM button

### Instrument Panel
- keff and reactivity display
- Reactor period
- Peak fuel/clad temperatures
- DNBR margin
- Fission product concentrations
- Safety status indicator

## Validation

The nuclear reactor module has been validated against peer-reviewed literature:

| Test | Reference | Result |
|------|-----------|--------|
| Point kinetics | Keepin (1965) | PASS |
| D2O moderation | Glasstone & Sesonske (1994) | PASS |
| Xenon dynamics | Stacey (2007) | PASS |
| Thermal efficiency | IAEA NP-T-1.1 (2009) | PASS |

See `VerificationReport.md` sections 15-18 for detailed validation results.

## References

1. Keepin, G.R. (1965). *Physics of Nuclear Kinetics*. Addison-Wesley.
2. Duderstadt, J.J. & Hamilton, L.J. (1976). *Nuclear Reactor Analysis*. Wiley.
3. Glasstone, S. & Sesonske, A. (1994). *Nuclear Reactor Engineering* (4th ed.).
4. Stacey, W.M. (2007). *Nuclear Reactor Physics* (2nd ed.). Wiley.
5. IAEA Nuclear Energy Series NP-T-1.1 (2009).

## Safety Notice

This simulation is for educational and research purposes only. Actual nuclear reactor operation requires extensive training, licensing, and regulatory compliance. The simplified models used here do not capture all phenomena important to reactor safety.

## Code Files

| File | Description |
|------|-------------|
| `Data/PhysicoChem/PhysicoChemMesh.cs` | Nuclear reactor parameter classes |
| `Analysis/PhysicoChem/NuclearReactorSolver.cs` | Physics solver |
| `GTK/Dialogs/NuclearReactorConfigDialog.cs` | Configuration UI |
| `UI/Windows/NuclearReactorVisualizer.cs` | 2D/3D visualization |
| `Tests/VerificationTests/SimulationVerificationTests.cs` | Validation tests |
