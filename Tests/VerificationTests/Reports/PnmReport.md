# PNM Absolute Permeability Report

## Scenario
- **Simulazione**: singolo tubo con 2 pori e 1 gola.
- **Algoritmi**: AbsolutePermeability (Darcy).

## Parametri principali
- Lunghezza: 10 voxel (10 µm con voxel size = 1 µm)
- Raggio gola: 0.5 voxel = 0.5 µm
- ΔP: 100 Pa
- Viscosità: 1 cP (1 mPa·s)

## Verifiche
- Permeabilità Darcy nell'ordine di grandezza atteso per capillare singolo.

## Calcolo teorico (Hagen-Poiseuille)
Per un capillare cilindrico singolo:
- k = r² / 8 = (0.5×10⁻⁶)² / 8 = 3.125×10⁻¹⁴ m²
- 1 mD = 9.869×10⁻¹⁶ m²
- k ≈ 31.7 mD

## Output atteso
- 10–40 mD (considerando approssimazioni geometriche della rete di pori).

## Test correlati
- `PnmAbsolutePermeability_SingleTubeMatchesPoiseuille`
