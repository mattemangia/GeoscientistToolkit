# Acoustic Simulation Report

## Scenario
- **Simulazione**: impulso di stress su schema FDTD elastico.
- **Algoritmi**: AcousticSimulatorCPU (staggered‑grid).

## Parametri principali
- Griglia: 5×5×5
- E = 1 GPa, ν = 0.25, ρ = 2500 kg/m³
- Impulso σxx al centro

## Verifiche
- Aggiornamento delle velocità non nullo nel nodo centrale.

## Output atteso
- vx[2,2,2] ≠ 0 dopo un passo.

## Test correlati
- `AcousticSimulation_StressPulseGeneratesVelocity`
