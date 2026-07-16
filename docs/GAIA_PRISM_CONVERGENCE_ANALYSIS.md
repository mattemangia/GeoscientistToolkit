# GAIA × PRISM — Analisi di Convergenza e Raccomandazioni

> **Scopo**: identificare le sovrapposizioni funzionali tra GAIA e PRISM, valutare quale implementazione è più matura/affidabile, e raccomandare quale versione conservare per ogni capacità condivisa. Le due piattaforme devono diventare **complementari**, non ridondanti.

---

## TL;DR Strategico

| Principio | Raccomandazione |
|---|---|
| **GAIA** = piattaforma **core-scale / pore-scale / imaging** | Pore networking, CT segmentation, NMR, thermal conductivity, photogrammetry, geochemistry, GeoScript |
| **PRISM** = piattaforma **basin-scale / reservoir-scale / AI-driven** | PINN/TorchSharp, InSAR, FWI, limit analysis, reservoir flow, cascade hazards, BRIDGE orchestration |
| **Moduli sovrapposti** | 8 aree con duplicazione significativa (vedi §2) |
| **Regola d'oro** | Dove PRISM ha un'implementazione più matura e testata, **migrare**. Dove GAIA è unico o superiore, **conservare**. Dove entrambi aggiungono valore a scale diverse, **mantenere entrambi** con interfaccia condivisa. |

---

## 1. Matrice delle Capacità

### 1.1 Capacità presenti in GAIA ma NON in PRISM

| Capacità | Modulo GAIA | Note |
|---|---|---|
| **CT Imaging & Segmentation** | `Data/CtImageStack/`, `Tools/CtImageStack/AISegmentation/` | SAM2/MicroSAM/Grounding DINO, brush/lasso/magic wand, streaming volume. Unico in GAIA. |
| **Pore Network Modeling (PNM)** | `Analysis/PNM/` | Estrazione rete pori da CT, permeabilità, trasporto reattivo. Unico in GAIA. |
| **Dual PNM** | `Analysis/PNM/DualPNMGenerator.cs` | Macro-micro pore coupling. Unico. |
| **NMR Simulation** | `Analysis/NMR/` | T2 relaxation random walk, T1-T2 maps. Unico. |
| **Thermal Conductivity (homogenisation)** | `Analysis/ThermalConductivity/` | FEM + OpenCL da volumi CT. Unico. |
| **Acoustic FDTD Simulation** | `Analysis/AcousticSimulation/` | Velocity-stress staggered grid su CT. Unico. |
| **Texture Classification** | `Analysis/TextureClassification/` | GLCM + OpenCL su thin sections. Unico. |
| **Ambient Occlusion Segmentation** | `Analysis/AmbientOcclusionSegmentation/` | Unico. |
| **GeoScript (DSL)** | `Scripting/GeoScript/`, `Business/GeoScript*.cs` | Linguaggio di scripting completo con pipeline `|>`. Unico. |
| **Stratigraphy Manager (8 sistemi)** | `Business/Stratigraphies/` | Internazionale + 7 nazionali. Unico. |
| **Petrology (igneous)** | `Business/Petrology/` | Cristallizzazione frazionata, diagrammi di fase. Unico. |
| **2D Geological Cross-Sections** | `Data/TwoDGeology/` | Editor interattivo, restoration strutturale. Unico. |
| **NeRF (Instant-NGP)** | `Data/Nerf/` | Sperimentale, CPU-only. Unico ma immaturo. |
| **GeoScript Pipeline DSL** | `Scripting/` | Pipeline `|>` con ~150 comandi. Unico. |

### 1.2 Capacità presenti in PRISM ma NON in GAIA

| Capacità | Modulo PRISM | Note |
|---|---|---|
| **PINN / TorchSharp (tutti i moduli)** | `Aquifer`, `Geothermal`, `CASCADE`, `FORGE`, `QUAKE`, `DELVE` | Physics-informed neural networks con autograd. Unico in PRISM. |
| **InSAR / TomoSAR (ECHO)** | `Prism/Services/Echo/` | Pipeline InSAR completa (PSInSAR, SBAS, TomoSAR). Unico. |
| **FWI (Full-Waveform Inversion)** | `PINNACLE` / `Prism/Services/Fwi/` | Adjoint-state FWI. Unico. |
| **Limit Analysis (FORGE)** | `Prism.FORGE/Stability/` | Upper-bound 2D/3D, log-spiral, Newmark. Unico. |
| **BRIDGE (multi-physics orchestrator)** | `Prism.BRIDGE/` | What-if, counterfactual, sweep, calibration. Unico. |
| **ReservoirFluxEngine** | `Prism.ReservoirFluxEngine/` | TOUGH2-class Newton solver, IAPWS-97, BHE. Unico. |
| **CASCADE (geohazards)** | `Prism.CASCADE/` | Flood, landslide runout, coastal erosion, meteo. Unico. |
| **FDSN / miniSEED / QuakeML** | `Prism.QUAKE/Fdsn/` | Web services sismologici. Unico. |
| **DELVE (section balancing)** | `Prism.DELVE/` | Balancing strutturale con PINN. Unico. |
| **Borehole Digitizer (PDF→LAS)** | `Prism.BoreholeDigitizer/` | Unico. |
| **PDFSEGY (raster→SEG-Y)** | `Prism.PDFSEGY/` | Unico. |
| **Geothermal PINN** | `Prism.Geothermal/` | Natural-state hydrothermal PINN. Unico. |
| **Weather / Meteo PINN** | `Prism.CASCADE/Meteo/` | Unico. |
| **XPINN Ensemble Coordinator** | Servizi app | Ensemble di PINN con TOPSIS ranking. Unico. |
| **TerraYield (techno-economic)** | `Prism.TerraYield/` | LCOE/LCOH, drilling costs, BTES. Unico. |
| **Geotech Explicit FEM** | `Prism.Geotech/` | Dynamic relaxation tet FEM con CPU/GPU parity. Unico. |

### 1.3 Capacità presenti in ENTRAMBE (sovraposizioni)

---

## 2. Analisi Dettagliata delle Sovrapposizioni

### 2.1 Geomechanics / Slope Stability

| Aspetto | GAIA | PRISM | Raccomandazione |
|---|---|---|---|
| **Slope stability 3D** | `Analysis/SlopeStability/` — DEM con GJK+EPA, ~5,500 LOC, CPU only, SIMD dichiarato ma non implementato. 2 test. | `Prism.FORGE/Stability/` — limit analysis (upper-bound log-spiral), ~2,000 LOC, CPU + PINN. 14 test. `Prism.Geotech/` — explicit FEM tet, ~3,000 LOC, CPU+OpenCL, 8 test. | **Approcci complementari**. GAIA fa DEM (discrete element), PRISM fa limit analysis + explicit FEM. Metodologie diverse per domande diverse. **Mantenere entrambi.** |
| **Triaxial simulation** | `Analysis/Geomechanics/TriaxialSimulation.cs` — mesh cilindrico, Mohr circle, OpenCL. 1 test. | Non presente direttamente, ma `Prism.Geotech` ha servo-controlled platens. | **GAIA** (più specializzato per triaxial lab test). |
| **FEM damage/plasticity** | `GeomechanicalSimulationCPU` (4 partials) — PCG, Mazars damage, radial return. CPU+GPU. | `Prism.Geotech/GeotechExplicitSolver` — FLAC-style local damping, strain softening, ubiquitous joints. **CPU↔GPU parity garantita**. | **PRISM superiore** per ingegneria geotecnica (più test, GPU parity, mesh quality, VTK export). GAIA migliore per **accoppiamento CT→FEM** (working directly on voxellated data). |
| **Bearing capacity** | `Data/TwoDGeology/Geomechanics/GeometricPrimitives2D.cs` — Terzaghi Nc/Nq/Nγ. 2 test. | `Prism.FORGE/Stability/LimitAnalysisSlope2D.cs` — log-spiral upper bound. | **Complementari**. GAIA fa Terzaghi factors (classico), PRISM fa upper-bound plasticity. |
| **Earthquake loading** | `Analysis/SlopeStability/EarthquakeLoad.cs` — seismic coefficient. | `Prism.FORGE/ForgeSeismicNewmark.cs` — Newmark sliding block. | **PRISM superiore** (Newmark è lo standard). |

#### Raccomandazione Geomechanics
- **DEM (GAIA)**: conservare in GAIA — è unico per simulazione di blocchi discreti
- **Limit analysis (PRISM)**: conservare in PRISM — è più testato e maturo
- **Explicit FEM (PRISM)**: conservare in PRISM — CPU↔GPU parity è best-in-class
- **CT-voxellated FEM (GAIA)**: conservare in GAIA — unico per accoppiamento CT→stress
- **Triaxial lab test (GAIA)**: conservare in GAIA — più specializzato
- **⚠️ Bug noto GAIA**: `EstimateContactArea()` ritorna sempre `1.0f` nel DEM. Il pattern `lock(A) lock(B)` può deadlockare se l'ordine degli ID si inverte. Verificare se PRISM ha risoluzioni applicabili.

---

### 2.2 Thermodynamics & Geochemistry

| Aspetto | GAIA | PRISM | Raccomandazione |
|---|---|---|---|
| **Thermodynamic solver** | `Analysis/Thermodynamic/` — Gibbs minimization, Debye-Hückel/Davies/Pitzer, ~5,000 LOC, CPU+SIMD+OpenCL. | `Prism.GeoGenesis/Thermodynamics/` — stessi metodi, ~2,500 LOC, CPU+SIMD+OpenCL. | **GAIA più completo** (più LOC, Pitzer model, phase diagrams). **Ma PRISM ha fix importante**: density IAPWS-97 Region-1 corretto con Kell (1975) polynomial — GAIA potrebbe avere lo stesso bug. **Verificare.** |
| **Compound library** | `Business/CompoundLibrary.cs` + 4 extensions — 100+ compounds, ~4,000 LOC. | `Prism.GeoGenesis/Materials/CompoundLibrary.cs` + extensions — simile, ~1,500 LOC. | **GAIA più esteso** (più composti, più estensioni). Ma verificare che GAIA includa il **fix Kell density**. |
| **Reactive transport** | `Analysis/Thermodynamic/ReactiveTransportSolver.cs` — SIA operator splitting. | `Prism.GeoGenesis/Thermodynamics/ReactiveTransportSolver.cs` — stesso approccio. Entrambi documentano limitazioni (Forward Euler, no full kinetics). | **Equivalenti**. Mantenere GAIA (più integrato con PNM e CT dissolution). |
| **CT dissolution** | `CTDissolutionSimulator.cs` — dissoluzione minerale su voxel CT. | `Prism.GeoGenesis/Thermodynamics/CTDissolutionSimulator.cs` — stesso concetto. | **GAIA** (più integrato con il modulo CT). |
| **Multiphase flow** | `Analysis/Multiphase/MultiphaseFlowSolver.cs` — IMPES, EOS1-4. ~1,200 LOC. | `Prism.GeoGenesis/Multiphase/MultiphaseFlowSolver.cs` — simile. ~400 LOC. | **GAIA più completo** ma PRISM ha accoppiamento con ReservoirFluxEngine. |

#### Raccomandazione Thermodynamics
- **Conservare GAIA come implementazione principale** (più estesa, più integrata con CT/PNM)
- **⚠️ Azione critica**: verificare se GAIA ha il bug IAPWS-97 density che PRISM ha corretto (Kell 1975 polynomial in `WaterProperties.cs`). Se sì, applicare il fix da PRISM.
- **Reactive transport**: mantenere entrambi — GAIA per CT-scale, PRISM per reservoir-scale (via ReservoirFluxEngine)

---

### 2.3 Geothermal Simulation

| Aspetto | GAIA | PRISM | Raccomandazione |
|---|---|---|---|
| **Geothermal solver (classico)** | `Analysis/Geothermal/` — coupled THM, dual-continuum (Warren-Root/MINC), ORC, BTES, HVAC, economics. ~7,000 LOC. CPU+GPU+SIMD. 25+ citazioni. 1 test. | Non ha solver classico equivalente (usa PINN per geothermal). | **GAIA superiore** per simulazione classica. Il modulo geotermico è il più completo di GAIA. |
| **Geothermal PINN** | Non presente. | `Prism.Geothermal/` — natural-state hydrothermal PINN, LayerNorm + residual connections, Fourier PDE residual (2nd-order autograd). ~2,800 LOC. 7 test. | **PRISM unico** per AI-driven geothermal. |
| **Reservoir flow** | Non presente (multiphase flow solo per pore-scale). | `Prism.ReservoirFluxEngine/` — TOUGH2-class Newton solver, IAPWS-97, BHE (coaxial/U-tube/open-loop). ~4,500 LOC. CPU+OpenCL. 4 test. | **PRISM superiore** per reservoir-scale. |
| **BTES** | `Analysis/Geothermal/BTESOpenCLSolver.cs` — GPU. | `Prism.TerraYield/BTES/` — g-function superposition, seasonal profiles. | **Complementari**. GAIA per GPU compute, PRISM per techno-economic. |
| **ORC** | `Analysis/Geothermal/ORCSimulation.cs` — SIMD, `ORCFluidLibrary`. | Non presente direttamente. | **GAIA unico**. |
| **Economics** | `Analysis/Geothermal/GeothermalEconomics.cs`. | `Prism.TerraYield/Economics/` — LCOE/LCOH/LCOC, NPV/IRR, 17 drilling cost correlations. | **PRISM superiore** per economics (più dettagliato). |

#### Raccomandazione Geothermal
- **Solver classico (GAIA)**: conservare — è il miglior modulo di GAIA
- **PINN geothermal (PRISM)**: conservare — unico
- **ReservoirFlux (PRISM)**: conservare — più completo per reservoir-scale
- **ORC (GAIA)**: conservare — unico
- **Economics (PRISM)**: conservare — più dettagliato

---

### 2.4 Photogrammetry & 3D Reconstruction

| Aspetto | GAIA | PRISM | Raccomandazione |
|---|---|---|---|
| **Real-time SfM** | `Analysis/Photogrammetry/` — MiDaS + SuperPoint + LightGlue + depth-aware RANSAC. OpenCvSharp. ~3,000 LOC. 1 test. | Non presente. | **GAIA unico**. |
| **Offline SfM** | `UI/Utils/Panorama/` — SIFT SIMD, reconstruction engine, mesh generator, orthomosaic, DEM. ~5,000+ LOC. | Non presente. | **GAIA unico**. |
| **NeRF** | `Data/Nerf/` — Instant-NGP hash grid, CPU only. ~2,500 LOC. Sperimentale. | Non presente. | **GAIA unico ma sperimentale**. Richiede GPU acceleration per essere praticabile. |

#### Raccomandazione Photogrammetry
- **Tutto in GAIA** — PRISM non ha equivalenti. È un punto di forza unico di GAIA.

---

### 2.5 Seismic Data Processing & Earthquake Simulation

| Aspetto | GAIA | PRISM | Raccomandazione |
|---|---|---|---|
| **SEG-Y processing** | `Data/Seismic/` — parser, 6 denoising methods, seismic cube, GIS export. ~3,500 LOC. | `Prism.QUAKE/` — SEG-Y reader, FDSN, miniSEED. `Prism.PDFSEGY/` — raster→SEG-Y. | **Complementari**. GAIA per processing chain (filter/deconv/stack), PRISM per data acquisition (FDSN/miniSEED). |
| **Earthquake simulation** | `Analysis/Seismology/` — staggered-grid FD, damage mapping, fracture analysis. ~2,500 LOC. 1 test. GPU non implementato. | `SHAKE` (in app) — deterministico wave modelling. Non contiene la stessa catena damage/fracture. | **GAIA** per damage/fracture post-processing. **Verificare** se PRISM SHAKE ha implementazioni più recenti dello stesso solver. |
| **Seismic inversion** | Non presente. | `PINNACLE/FWI/` — adjoint-state FWI completo. | **PRISM unico**. |
| **Rock physics** | Empirical relations nei comandi GeoScript (Gardner, Castagna, Gassmann). | `Prism.QUAKE/RockPhysics/` — stessa suite + acoustic/elastic impedance. | **PRISM più strutturato**. GAIA dovrebbe allinearsi. |
| **Borehole-seismic** | Synthetic seismogram, well-to-seismic tie. | Non presente direttamente. | **GAIA unico**. |

#### Raccomandazione Seismic
- **Processing chain (GAIA)**: conservare
- **Earthquake simulation (GAIA)**: conservare, ma **implementare GPU** (il flag `useGpu` esiste ma non è wired)
- **FDSN/miniSEED (PRISM)**: conservare — unico
- **FWI (PRISM)**: conservare — unico
- **Rock physics**: **allineare GAIA a PRISM** (strutturare meglio le relazioni empiriche)

---

### 2.6 PhysicoChem / Multiphysics Reactors

| Aspetto | GAIA | PRISM | Raccomandazione |
|---|---|---|---|
| **Reactor simulation** | `Analysis/PhysicoChem/` — Darcy/NS flow, heat, reactive transport, nucleation, force fields. + `NuclearReactorSolver` (neutron diffusion, point kinetics, Xe/Sm poisoning). ~2,000 LOC. 2 test. | `Prism.GeoGenesis/Reactor/` — virtual 3D reactor, speciation, precipitation. ~1,200 LOC. | **Complementari**. GAIA più fisica (NS flow, nuclear), PRISM più geochemica. |
| **Parameter sweep** | `Data/PhysicoChem/ParameterSweep.cs` + `ParameterSweepManager.cs`. | `Prism.BRIDGE/BridgeSweepRunner.cs` — più maturo (calibration, hypothesis ranking). | **PRISM superiore** per orchestration. |

#### Raccomandazione PhysicoChem
- **Reactor core (GAIA)**: conservare — unico per NS flow + nuclear
- **Reactor geochemistry (PRISM GeoGenesis)**: conservare
- **Sweep/orchestration**: usare BRIDGE (PRISM)

---

### 2.7 Borehole & Well Data

| Aspetto | GAIA | PRISM | Raccomandazione |
|---|---|---|---|
| **LAS parsing** | `Data/Loaders/LASLoader.cs`. | `Prism.Core/LasParser.cs`. | Equivalenti. Allineare a uno solo. |
| **Well log analysis** | `Data/Borehole/` — lithology editing, correlation, synthetic seismic, porosity/saturation. | `Prism.Core/` — extraction multi-source, lithology, kriging. | **Complementari**. GAIA per editing/correlation, PRISM per data extraction. |
| **Borehole correlation** | `Data/Borehole/BoreholeLogCorrelation.cs`, `ProfileCorrelationSystem.cs`, 3D viewers. | `Prism.Core/Correlation/` — kriging, Delaunay, isosurface. | **Complementari**. GAIA per well-to-well correlation visuale, PRISM per spatial interpolation. |
| **Digitizer** | Non presente. | `Prism.BoreholeDigitizer/` — raster→LAS. | **PRISM unico**. |

---

### 2.8 GIS & Geological Mapping

| Aspetto | GAIA | PRISM | Raccomandazione |
|---|---|---|---|
| **GIS dataset** | `Data/GIS/` — vector/raster, basemaps, geological mapping, subsurface GIS, hydrological analysis, satellite tools. | Non presente come modulo GIS dedicato. | **GAIA unico**. |
| **Cross-section editor** | `Data/TwoDGeology/` — editor interattivo, structural restoration, animation export. | `Prism.DELVE/` — section building con PINN. | **Complementari**. GAIA per editing interattivo, PRISM per AI-assisted balancing. |
| **Stratigraphy** | `Business/Stratigraphies/` — 8 sistemi (Int, FR, DE, ES, IT, UK, US, Mammal Ages). | Non presente. | **GAIA unico**. |

---

## 3. Sintesi: Cosa Conservare Dove

### 3.1 Moduli da conservare in GAIA (unico o superiore)

| Modulo | Motivo |
|---|---|
| **CT Imaging & AI Segmentation** | Unico. SAM2/MicroSAM/Grounding DINO + segmentation tools. |
| **Pore Network Modeling** | Unico. Estrazione da CT, permeabilità, trasporto reattivo. |
| **NMR Simulation** | Unico. T2 relaxation, T1-T2 maps. |
| **Thermal Conductivity** | Unico. Homogenisation FEM da CT. |
| **Acoustic FDTD** | Unico. Velocity-stress su CT. |
| **Photogrammetry (real-time + offline)** | Unico. SfM completo. |
| **GeoScript DSL** | Unico. Linguaggio scripting con pipeline `|>`. |
| **Stratigraphy Manager** | Unico. 8 sistemi nazionali. |
| **Geothermal classical solver** | Superiore. Modulo più completo (~7,000 LOC, THM, ORC, BTES). |
| **Thermodynamic solver** | Più esteso. Pitzer, phase diagrams, CT dissolution. |
| **Compound Library** | Più esteso. 100+ composti con estensioni. |
| **Petrology (igneous)** | Unico. Cristallizzazione frazionata, Kd library. |
| **Seismic processing chain** | Unico. 6 denoising methods, seismic cube. |
| **2D Geological editor** | Unico. Editor interattivo con restoration. |
| **Borehole correlation** | Superiore per visual correlation e 3D fence diagrams. |
| **Triaxial lab simulation** | Unico per lab test. |
| **DEM slope stability** | Unico per discrete element blocks. |
| **PhysicoChem reactor** | Unico per NS flow + nuclear reactor physics. |
| **GIS module** | Unico. Vector/raster/subsurface/hydrological. |

### 3.2 Moduli da conservare in PRISM (unico o superiore)

| Modulo | Motivo |
|---|---|
| **Tutti i PINN (6 moduli)** | Unico. TorchSharp autograd, physics losses. |
| **InSAR/TomoSAR (ECHO)** | Unico. Pipeline completa. |
| **FWI (PINNACLE)** | Unico. Adjoint-state inversion. |
| **Limit Analysis (FORGE)** | Superiore. 14 test, upper-bound 2D/3D, Newmark. |
| **Explicit FEM Geotech** | Superiore. CPU↔GPU parity, VTK export, Gmsh import. |
| **BRIDGE orchestrator** | Unico. What-if, counterfactual, sweep, calibration. |
| **ReservoirFluxEngine** | Unico. TOUGH2-class Newton, IAPWS-97. |
| **CASCADE geohazards** | Unico. Flood, landslide, coastal, meteo. |
| **WaveEngines** | Unico. SWE spectral + HydroFlux con GPU. |
| **FDSN/miniSEED/QuakeML** | Unico. Web services sismologici. |
| **DELVE section balancing** | Unico. PINN-assisted structural balancing. |
| **TerraYield economics** | Superiore. LCOE/LCOH, 17 drilling cost models. |
| **Borehole Digitizer** | Unico. Raster→LAS. |
| **PDFSEGY** | Unico. Raster→SEG-Y. |
| **Data extraction (Core)** | Superiore. Multi-source (ISPRA/SGI/EGDI/USGS). |

### 3.3 Moduli con valore in entrambi (complementari a scale diverse)

| Capacità | GAIA ruolo | PRISM ruolo |
|---|---|---|
| **Slope stability** | DEM (pore/block scale) | Limit analysis + FEM (engineering scale) |
| **Geothermal** | Classical solver + ORC (system design) | PINN + ReservoirFlux (field-scale inversion) |
| **Thermodynamics** | Equilibrium + reactive transport (CT/pore scale) | Reservoir-scale reactive transport (TOUGHREACT-style) |
| **Seismic** | Processing chain + earthquake simulation | FWI + FDSN + tomography |
| **Borehole** | Editing + correlation + synthetic seismic | Data extraction + digitizing + kriging |
| **Photogrammetry/NeRF** | Reconstruction pipeline | — |
| **Multiphase flow** | Core-scale EOS | Reservoir-scale (ReservoirFluxEngine) |

---

## 4. Bug e Fix da Trasferire da PRISM a GAIA

Questi sono problemi identificati in GAIA che PRISM ha già risolto:

| # | Bug in GAIA | Fix in PRISM | File PRISM di riferimento |
|---|---|---|---|
| 1 | **IAPWS-97 density (Region 1)** — può ritornare ~5 kg/m³ invece di ~997 kg/m³ | Sostituito con Kell (1975) polynomial validato | `Prism.GeoGenesis/Thermodynamics/WaterProperties.cs` |
| 2 | **SlopeStability `EstimateContactArea()`** ritorna sempre `1.0f` | PRISM non ha DEM, ma il pattern di contatto in `GeotechExplicitSolver` gestisce area correttamente via tet mesh | `Prism.Geotech/Physics/GeotechExplicitSolver.cs` |
| 3 | **SlopeStability lock ordering** — `lock(A) lock(B)` può deadlockare se gli ID si invertono | PRISM Geotech non usa lock espliciti (parallelismo a livello elementale) | — |
| 4 | **SIMD dichiarato ma non usato** in SlopeStability (`UseSIMD` flag non wired) | PRISM usa `System.Runtime.Intrinsics` effettivamente in GeoGenesis Thermodynamics | `Prism.GeoGenesis/Thermodynamics/ThermodynamicsSIMD.cs` |
| 5 | **Seismology GPU non implementato** (`useGpu` flag esiste ma non wired) | PRISM HydroFlux GPU self-validates contro CPU prima di usare | `Prism.CASCADE.WaveEngines/HydroFlux/HydroFluxOpenCL.cs` |
| 6 | **Multiphase flow semplificato** — enthalpy-based quality invece di flash completo | `ReservoirFluxEngine` ha phase transition handling completo con root finder lungo curva di saturazione | `Prism.ReservoirFluxEngine/EOS/EosWaterEnergy.cs` |

---

## 5. Raccomandazioni Architetturali

### 5.1 Divisione del lavoro tra piattaforme

```
┌──────────────────────────────────────────────────────────────────┐
│                        UTENTE FINALE                             │
│  (geoscienziato, ingegnere, ricercatore)                         │
└──────────────┬──────────────────────────┬────────────────────────┘
               │                          │
     ┌─────────▼──────────┐    ┌─────────▼──────────┐
     │       GAIA         │    │       PRISM        │
     │  (Core & Imaging)  │    │  (Basin & AI)      │
     │                    │    │                    │
     │  • CT/μCT imaging  │    │  • PINN (6 moduli) │
     │  • AI segmentation │    │  • InSAR (ECHO)    │
     │  • PNM/NMR         │    │  • FWI             │
     │  • Acoustic FDTD   │    │  • BRIDGE          │
     │  • Thermal cond.   │    │  • CASCADE         │
     │  • Photogrammetry  │    │  • ReservoirFlux   │
     │  • Geochemistry    │    │  • FORGE           │
     │  • GeoScript DSL   │    │  • QUAKE/FDSN      │
     │  • Seismic proc.   │    │  • DELVE           │
     │  • 2D geology      │    │  • TerraYield      │
     └────────┬───────────┘    └────────┬───────────┘
              │                         │
              └──────────┬──────────────┘
                         │
              ┌──────────▼──────────┐
              │   INTERFACCIA       │
              │   (API/NodeEndpoint │
              │    / file exchange) │
              └─────────────────────┘
```

### 5.2 Condivisione di dati tra piattaforme

- **GAIA → PRISM**: GAIA può esportare proprietà di roccia (porosità, permeabilità, conductività termica, Vp/Vs) derivate da CT/PNM come **tabelle di lookup** o **volumi voxelizzati** che PRISM può usare come prior nei PINN o come input mesh per ReservoirFlux.
- **PRISM → GAIA**: PRISM può esportare modelli 3D invertiti (velocity models, stress fields, temperature fields) come **volumi** che GAIA può visualizzare nel suo viewer 3D.
- **Formato comune**: usare formato binario GAIA (`.gtp` project + dataset DTO) o NetCDF/VTK come ponte.

### 5.3 Standard scientifici condivisi

Entrambe le piattaforme dovrebbero adottare:
- **ONNX Runtime** come unico backend AI (GAIA già lo usa, PRISM usa TorchSharp — considerare migrazione o bridge)
- **OpenCL via Silk.NET** (già condiviso)
- **xUnit verification tests** con citazioni peer-reviewed (PRISM è il gold standard qui — ~200 test vs ~12 di GAIA)
- **AGENTS.md multi-agent system** (già condiviso, allineare gli agent)

---

## 6. Roadmap di Consolidamento (priorità)

### Priorità 1 — Fix critici (immediato)
1. **Trasferire fix IAPWS-97 density** da PRISM a GAIA (`WaterProperties.cs`)
2. **Fix lock ordering** in GAIA SlopeStability DEM
3. **Implementare GPU path** in GAIA Seismology (`useGpu` flag è già presente)

### Priorità 2 — Allineamento (breve termine)
4. **Aggiungere verification tests** a GAIA moduli senza test: ThermalConductivity, NMR, TextureClassification, Seismic processing (seguire il pattern PRISM con citazioni DOI)
5. **Wire up SIMD** effettivo in GAIA SlopeStability o rimuovere il flag `UseSIMD`
6. **Strutturare rock physics** in GAIA come classe dedicata (allineare a `Prism.QUAKE/RockPhysics.cs`)

### Priorità 3 — Integrazione (medio termine)
7. **Definire API contract** tra GAIA e PRISM per scambio volumi/proprietà
8. **Ponte CT→Reservoir**: GAIA estrae PNM properties → PRISM le usa come prior in ReservoirFlux
9. **Ponte PINN→Visualizer**: PRISM addestra PINN → GAIA visualizza i risultati 3D
10. **GeoScript come orchestrator**: estendere GeoScript per lanciare comandi PRISM via NodeEndpoint

### Priorità 4 — Innovazione congiunta (lungo termine)
11. **PINN su CT data**: usare PINN PRISM come metodo alternativo alla FDTD GAIA per acoustic/thermal simulation su volumi CT
12. **BRIDGE per multi-scale**: estendere BRIDGE (PRISM) per orchestrare workflow GAIA (CT→PNM→upscaling→reservoir)
13. **Foundation model congiunto**: pre-training su dataset CT (GAIA) + inversione geofisica (PRISM)

---

## 7. Tabella Riassuntiva Finale

| Area | GAIA | PRISM | Conservare in | Note |
|---|---|---|---|---|
| CT Imaging & Segmentation | ✅ eccellente | ❌ | **GAIA** | Unico |
| PNM | ✅ eccellente | ❌ | **GAIA** | Unico |
| NMR | ✅ buono | ❌ | **GAIA** | Unico |
| Thermal Conductivity | ✅ buono | ❌ | **GAIA** | Unico |
| Acoustic FDTD | ✅ buono | ❌ | **GAIA** | Unico |
| Photogrammetry | ✅ buono | ❌ | **GAIA** | Unico |
| GeoScript | ✅ eccellente | ❌ | **GAIA** | Unico |
| Stratigraphy | ✅ buono | ❌ | **GAIA** | Unico |
| Petrology | ✅ buono | ❌ | **GAIA** | Unico |
| Geothermal (classical) | ✅ eccellente | ❌ | **GAIA** | Miglior modulo |
| Thermodynamics | ✅ esteso | ✅ simile | **GAIA** (fix da PRISM) | Verificare bug density |
| Compound Library | ✅ esteso | ✅ simile | **GAIA** | Più completo |
| Seismic processing | ✅ buono | — | **GAIA** | PRISM ha FDSN separato |
| 2D Geology editor | ✅ buono | ❌ | **GAIA** | Unico |
| GIS | ✅ buono | ❌ | **GAIA** | Unico |
| DEM slope stability | ✅ buono | ❌ | **GAIA** | Unico (DEM) |
| Triaxial simulation | ✅ buono | ❌ | **GAIA** | Unico |
| PhysicoChem reactor | ✅ buono | ✅ simile | **GAIA** (nuclear unico) | |
| PINN (tutti) | ❌ | ✅ eccellente | **PRISM** | Unico |
| InSAR/TomoSAR | ❌ | ✅ eccellente | **PRISM** | Unico |
| FWI | ❌ | ✅ buono | **PRISM** | Unico |
| Limit Analysis | ❌ | ✅ eccellente | **PRISM** | 14 test |
| Explicit FEM Geotech | ❌ | ✅ eccellente | **PRISM** | GPU parity |
| BRIDGE | ❌ | ✅ eccellente | **PRISM** | Unico |
| ReservoirFlux | ❌ | ✅ eccellente | **PRISM** | Unico |
| CASCADE hazards | ❌ | ✅ eccellente | **PRISM** | Unico |
| WaveEngines | ❌ | ✅ eccellente | **PRISM** | Unico |
| FDSN/miniSEED | ❌ | ✅ buono | **PRISM** | Unico |
| DELVE balancing | ❌ | ✅ buono | **PRISM** | Unico |
| TerraYield | ❌ | ✅ buono | **PRISM** | Unico |
| Borehole Digitizer | ❌ | ✅ buono | **PRISM** | Unico |
| Data extraction | ❌ | ✅ eccellente | **PRISM** | Unico |
| NeRF | ⚠️ sperimentale | ❌ | **GAIA** (da maturare) | CPU-only, lento |
| Texture Classification | ⚠️ sperimentale | ❌ | **GAIA** (da maturare) | Simplistic features |
