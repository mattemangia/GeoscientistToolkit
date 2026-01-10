# Multiphase Flow (PhysicoChem)

Guide to multiphase water/steam/NCG simulations in PhysicoChem reactors.

---

## Overview

The multiphase flow solver models coupled mass and energy transport for water–steam–non‑condensable‑gas systems. It is integrated with the PhysicoChem reactor framework and can be activated via GeoScript commands.

**Supported EOS types:**
- WaterSteam (two-phase)
- WaterCO2
- WaterAir
- WaterH2S
- WaterMethane

---

## Enabling Multiphase Flow

Multiphase flow is enabled on a PhysicoChem dataset using GeoScript:

```geoscript
ENABLE_MULTIPHASE WaterCO2
```

This sets multiphase flags in the PhysicoChem simulation parameters and selects the EOS type.

---

## Key Parameters

Use GeoScript to configure the multiphase properties:

```geoscript
SET_MULTIPHASE_PARAMS S_lr=0.05 S_gr=0.01 m=0.5 alpha=1e-4
SET_KR_MODEL VanGenuchten
SET_PC_MODEL VanGenuchten
```

**Parameter meanings:**
- `S_lr` / `S_gr`: residual liquid/gas saturations
- `m`, `alpha`: van Genuchten parameters
- `SET_KR_MODEL`: relative permeability model (VanGenuchten/Corey/Linear/Grant)
- `SET_PC_MODEL`: capillary pressure model (VanGenuchten/BrooksCorey/Linear/Leverett)

---

## Gas Phase and Compositional Inputs

Add gas phases to domain initial conditions:

```geoscript
ADD_GAS_PHASE domain_name CO2 0.2 5e5
```

This command sets gas saturation and estimates dissolved gas via Henry’s law.

---

## Results

Multiphase flow contributes the following fields to PhysicoChem states:

- Liquid, vapor, and gas saturations
- EOS-dependent pressure/temperature updates
- Phase equilibrium updates for each time step

---

## Related Pages

- [PhysicoChem Reactors](PhysicoChem-Reactors.md)
- [Thermodynamics and Geochemistry](Thermodynamics-and-Geochemistry.md)
- [Simulation Modules](Simulation-Modules.md)
