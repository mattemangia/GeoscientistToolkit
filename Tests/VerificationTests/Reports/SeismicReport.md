# Seismic Simulation Report

## Scenario
- **Simulazione**: propagazione onde P/S con EarthquakeSimulationEngine su griglia minima.
- **Algoritmi**: solving elastico semplificato con stime di arrivo.

## Parametri principali
- Griglia: 10×10×10
- Durata: 4 s
- Ipocentro: 5 km

## Verifiche
- Tempo di arrivo S > P.
- Rapporto S/P maggiore di 1.2.

## Output atteso
- P arrivano prima delle S; rapporto temporale coerente con vP/vS.

## Test correlati
- `SeismicSimulation_PAndSArrivalsMatchVelocityRatio`
