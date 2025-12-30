# PhysicoChem Simulation Report

## Scenario
- **Simulazione**: diffusione 1D in un mezzo poroso con gradiente di concentrazione (tracciante).
- **Algoritmi**: ReactiveTransportSolver (diffusione molecolare).

## Parametri principali
- Griglia 1D: 21 celle con $\Delta x = 1$ mm.
- Concentrazione iniziale a gradino: 0.01 mol/L nella metà sinistra, 0 nella metà destra.
- Tempo totale: 3600 s (36 step da 100 s).
- Ca²⁺ iniziale: 0.01 mol/L nel box superiore.
- HCO₃⁻ iniziale: 0.01 mol/L nel box inferiore.
- Gravità attiva, boundary interattiva all’interfaccia.

## Verifiche
- Confronto con soluzione analitica dell’equazione di diffusione (erfc).

## Output atteso
- Concentrazione nella cella a destra dell’interfaccia entro ±50% del valore analitico.

## Test correlati
- `PhysicoChem_DualBoxMixing_TracksReportedDiffusionMagnitude`
