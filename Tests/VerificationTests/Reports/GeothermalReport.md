# Geothermal Dual‑Continuum Report

## Scenario
- **Simulazione**: trasferimento termico matrix‑fracture su mesh minimale.
- **Algoritmi**: FracturedMediaSolver (Warren‑Root).

## Parametri principali
- Mesh: 3×1×3
- T matrice: 280 K
- T frattura: 320 K
- Apertura frattura: 1e‑4 m

## Verifiche
- Il solver riduce il gap termico dopo un passo di 10 s.

## Output atteso
- T matrice centrale ↑, T frattura centrale ↓.

## Test correlati
- `Geothermal_DualContinuumExchange_ReducesTemperatureGap`
