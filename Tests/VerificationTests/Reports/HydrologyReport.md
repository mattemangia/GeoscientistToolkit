# Hydrology Simulation Report

## Scenario
- **Simulazione**: flow direction D8 e flow accumulation su DEM sintetico.
- **Algoritmi**: GISOperationsImpl.CalculateD8FlowDirection / CalculateFlowAccumulation.

## Parametri principali
- DEM 5×5 con pendenza verso sud‑est.

## Verifiche
- La cella di valle accumula più contributi rispetto a celle di monte.

## Output atteso
- Accumulo nella cella [4,4] > 1.

## Test correlati
- `Hydrology_D8FlowAccumulation_MatchesBenchmarkPattern`
