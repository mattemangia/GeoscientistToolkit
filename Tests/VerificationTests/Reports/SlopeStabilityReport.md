# Slope Stability Simulation Report

## Scenario
- **Simulazioni**: caduta libera con gravità costante e scorrimento su gravità inclinata.
- **Algoritmi**: DEM con integrazione esplicita, contatto disattivato (singolo blocco).

## Parametri principali
- Blocco cubico: 1 m
- Densità: 2500 kg/m³
- Gravità: 9.81 m/s²
- Step: 0.01 s

## Verifiche
- **Caduta libera**: confronto con $z = z_0 - 0.5 g t^2$.
- **Scorrimento**: confronto con $x = 0.5 g \sin(\theta) t^2$ (θ = 30°).

## Output atteso
- Scostamento verticale < 5 cm rispetto all’analitica.
- Spostamento lungo pendenza entro ±2 cm dal valore teorico.

## Test correlati
- `SlopeStability_GravityDrop_MatchesAnalyticalFreeFall`
- `SlopeStability_TiltedGravitySliding_MatchesDownslopeDisplacement`
