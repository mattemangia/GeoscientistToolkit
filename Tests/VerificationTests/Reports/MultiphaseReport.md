# Multiphase Flow Report

## Scenario
- **Simulazione**: transizione acqua‑vapore in EOS1 (WaterSteam).
- **Algoritmi**: MultiphaseFlowSolver IMPES con equilibrio di fase.

## Parametri principali
- Griglia: 3×3×3
- T = 450 K, P = 1 bar
- Enthalpy ~2.6e6 J/kg

## Verifiche
- Saturazioni aggiornate e normalizzate (somma ≈ 1).

## Output atteso
- Vapor saturation > 0.

## Test correlati
- `MultiphaseFlow_WaterSteamTransition_UpdatesSaturations`
