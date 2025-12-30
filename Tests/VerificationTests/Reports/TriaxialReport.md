# Triaxial Simulation Report

## Scenario
- **Simulazione**: compressione triaxiale con criterio Mohr‑Coulomb.
- **Algoritmi**: TriaxialSimulation CPU.

## Parametri principali
- Cohesion: 10 MPa
- Friction angle: 30°
- σ₃: 20 MPa
- Mesh cilindrica minima

## Verifiche
- σ₁,peak simulato vicino a:
  $\sigma_1 = \sigma_3 \tan^2(45^\circ + \phi/2) + 2c \tan(45^\circ + \phi/2)$

## Output atteso
- Differenza < 2 MPa rispetto al valore teorico.

## Test correlati
- `TriaxialSimulation_MohrCoulombPeakMatchesReference`
