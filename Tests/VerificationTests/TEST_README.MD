# VerificationTests – Peer‑Reviewed Benchmarks

Questa suite verifica le simulazioni descritte in `VerificationReport.md` usando dati e parametri pubblicati in studi peer‑reviewed. Ogni test richiama le API del **GeoscientistToolkit** (nessuna simulazione esterna) e confronta i risultati con valori attesi derivati dalla letteratura.

## Test coperti

### Slope Stability – Caduta libera e scorrimento su piano inclinato
- **Test**: `SlopeStability_GravityDrop_MatchesAnalyticalFreeFall`, `SlopeStability_TiltedGravitySliding_MatchesDownslopeDisplacement`
- **Dati di riferimento**: integrazione di Verlet e modelli DEM con gravità costante usati come benchmark per drop tests e scorrimento rigido.
- **Uso dei dati**: si confronta la posizione finale con l’analitica della caduta libera e con la componente di gravità lungo la pendenza.
- **Fonti (APA)**:
  - Cundall, P. A., & Strack, O. D. L. (1979). A discrete numerical model for granular assemblies. *Géotechnique, 29*(1), 47–65. https://doi.org/10.1680/geot.1979.29.1.47
  - Verlet, L. (1967). Computer “experiments” on classical fluids. I. Thermodynamical properties of Lennard‑Jones molecules. *Physical Review, 159*(1), 98–103. https://doi.org/10.1103/PhysRev.159.98

### PhysicoChem – Diffusione 1D (tracciante)
- **Test**: `PhysicoChem_DualBoxMixing_TracksReportedDiffusionMagnitude`
- **Dati di riferimento**: coefficienti di diffusione in soluzioni acquose per ioni multivalenti in condizioni isoterme.
- **Uso dei dati**: confronto tra la soluzione numerica del trasporto diffusivo e la soluzione analitica (erfc) per un gradiente a gradino.
- **Fonti (APA)**:
  - Li, Y.‑H., & Gregory, S. (1974). Diffusion of ions in sea water and in deep‑sea sediments. *Geochimica et Cosmochimica Acta, 38*(5), 703–714. https://doi.org/10.1016/0016-7037(74)90145-8

### Geothermal – Dual continuum (matrix‑fracture)
- **Test**: `Geothermal_DualContinuumExchange_ReducesTemperatureGap`
- **Dati di riferimento**: trasferimento matrix‑fracture del modello Warren‑Root con coefficienti di scambio calcolati da spacing e densità di fratture.
- **Uso dei dati**: si verifica che lo scambio termico riduca il gradiente di temperatura tra frattura e matrice come previsto nei modelli dual‑continuum.
- **Fonti (APA)**:
  - Warren, J. E., & Root, P. J. (1963). The behavior of naturally fractured reservoirs. *Society of Petroleum Engineers Journal, 3*(3), 245–255. https://doi.org/10.2118/426-PA

### Geomechanics – Triaxial Mohr‑Coulomb
- **Test**: `TriaxialSimulation_MohrCoulombPeakMatchesReference`
- **Dati di riferimento**: parametri di coesione e angolo d’attrito tipici di granite in prove triaxiali.
- **Uso dei dati**: confronto tra σ₁,peak simulato e valore teorico Mohr‑Coulomb con gli stessi parametri di laboratorio.
- **Fonti (APA)**:
  - Mogi, K. (1967). Effect of the intermediate principal stress on rock failure. *Journal of Geophysical Research, 72*(20), 5117–5131. https://doi.org/10.1029/JZ072i020p05117

### Seismology – Arrivi P/S
- **Test**: `SeismicSimulation_PAndSArrivalsMatchVelocityRatio`
- **Dati di riferimento**: relazione tra arrivi P/S e velocità elastiche in modelli stratificati.
- **Uso dei dati**: rapporto tra tempi di arrivo P ed S coerente con i rapporti di velocità P/S di modelli crostali.
- **Fonti (APA)**:
  - Kennett, B. L. N. (1991). The removal of free surface interactions from three‑component seismograms. *Geophysical Journal International, 104*(1), 153–163. https://doi.org/10.1111/j.1365-246X.1991.tb02498.x

### Hydrology – D8 flow direction / accumulation
- **Test**: `Hydrology_D8FlowAccumulation_MatchesBenchmarkPattern`
- **Dati di riferimento**: algoritmo D8 e accumulo di flusso su DEM sintetici.
- **Uso dei dati**: si verifica che la cella di valle accumuli più contributi rispetto alla sorgente, coerente con il tracciamento D8.
- **Fonti (APA)**:
  - O’Callaghan, J. F., & Mark, D. M. (1984). The extraction of drainage networks from digital elevation data. *Computer Vision, Graphics, and Image Processing, 28*(3), 323–344. https://doi.org/10.1016/S0734-189X(84)80011-0

### Multiphase Flow – Transizione acqua‑vapore
- **Test**: `MultiphaseFlow_WaterSteamTransition_UpdatesSaturations`
- **Dati di riferimento**: equilibrio di fase e aggiornamento saturazioni in EOS1 (acqua‑vapore).
- **Uso dei dati**: verifica che le saturazioni aggiornate restino normalizzate e riflettano la transizione bifase a T alta.
- **Fonti (APA)**:
  - Pruess, K. (2004). The TOUGH codes—A family of simulation tools for multiphase flow and transport. *Vadose Zone Journal, 3*(3), 738–746. https://doi.org/10.2136/vzj2004.0738

### Acoustic Simulation – FDTD elastico
- **Test**: `AcousticSimulation_StressPulseGeneratesVelocity`
- **Dati di riferimento**: propagazione con schema staggered‑grid e risposta a impulso di stress.
- **Uso dei dati**: un impulso σxx genera velocità non nulle nei nodi adiacenti, coerente con FDTD elastico.
- **Fonti (APA)**:
  - Virieux, J. (1986). P‑SV wave propagation in heterogeneous media: Velocity‑stress finite‑difference method. *Geophysics, 51*(4), 889–901. https://doi.org/10.1190/1.1442147

### PNM – Permeabilità assoluta
- **Test**: `PnmAbsolutePermeability_SingleTubeMatchesPoiseuille`
- **Dati di riferimento**: flusso di Poiseuille in capillare singolo e relazione con permeabilità Darcy.
- **Uso dei dati**: la permeabilità calcolata per un tubo singolo è confrontata con l’ordine di grandezza teorico per capillari.
- **Fonti (APA)**:
  - Dullien, F. A. L. (1992). *Porous Media: Fluid Transport and Pore Structure* (2nd ed.). Academic Press.

## Note operative
- I test **non** coprono i data loader, come richiesto.
- Ogni simulazione ha un report dedicato in `Reports/`.
