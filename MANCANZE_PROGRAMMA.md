# Cosa manca attualmente (analisi approfondita del repository)

> Questa nota si basa su un’ispezione del codice e della documentazione presenti nel repository.
> Non ho eseguito il programma né validato runtime/feature, quindi le mancanze sotto elencate
> sono dedotte da TODO, placeholder, documentazione incompleta o riferimenti a file assenti.

## 1) Funzionalità dichiarate ma non completate (TODO/placeholder nel codice)

### GIS / Idrologia
- **Dialog per caricare GeoTIFF**: in `Data/GIS/Tools/HydrologicalAnalysisTool.cs` c’è un
  pulsante “Load Elevation GeoTIFF…” che non apre alcun file dialog (TODO esplicito).
  Questo rende l’analisi idrologica poco fruibile se non si hanno già layer DEM caricati.

### GeoScript / Multiphase
- **Esecuzione reale di RUN_MULTIPHASE**: in `Business/GeoScriptMultiphaseExtensions.cs`, il
  comando `RUN_MULTIPHASE` logga i parametri ma non chiama il solver
  (“TODO: Actually run the simulation here”). Il flusso GeoScript risulta quindi
  incompleto rispetto alla promessa di “run” delle simulazioni.

### PNM Dual Porosity
- **Estrazione micro‑PNM da SEM**: in `Analysis/PNM/DualPNMGenerator.cs`, la generazione di
  micro‑rete da immagini SEM è un placeholder (“TODO: Implement actual PNM extraction from SEM image”).
  Il modulo Dual PNM risulta parzialmente simulato (micro‑porosità e permeabilità fittizie).

### Geotermia
- **Visualizzazione 2D di precipitazione**: in `Analysis/Geothermal/GeothermalSimulationTools.cs`
  la sezione “2D Precipitation Maps” è un placeholder (“[2D slice visualization will be rendered here]”).
- **Rendering ORC con device grafico**: stessa classe, il blocco ORC include un TODO per l’accesso
  al `GraphicsDevice`, con fallback testuale.

### Multiphase Reactive Transport
- **Grid spacing hardcoded**: in `Analysis/Thermodynamic/MultiphaseReactiveTransportSolver.cs`
  `GridSpacing` è impostato a `(1,1,1)` con TODO per recuperarlo dai parametri.
  Questo indica un’incompleta connessione tra parametri reali e solver.

### Slope Stability (2D)
- **Stress max non calcolato**: in `Analysis/SlopeStability/SlopeStability2DSimulator.cs`
  il campo `MaxStress` è fissato a `0` con TODO per il calcolo reale.

### Image Stack Organizer
- **Rinomina gruppi**: in `Data/Image/ImageStackOrganizerDialog.cs` il menu “Rename” è
  un placeholder (“TODO: Implement rename”).

### Altro (indicazioni puntuali)
- `Analysis/PhysicoChem/PhysicoChemSolver.cs`: `InitialPermeability` è inizializzata uguale alla
  permeabilità corrente con TODO per un tracciamento separato.
- `Analysis/SlopeStability/BlockGenerator2D.cs`: TODO per estrazione poligoni corretta.
- `Analysis/Geothermal/GeothermalSimulationTools.cs`: TODO per visualizzazioni 2D/ORC.

## 2) Documentazione incompleta o incoerente

- **CHANGELOG mancante**: `README.md` cita `CHANGELOG.md`, ma il file non è presente.
- **Screenshots “coming soon”**: nella sezione “Screenshots” del README si indica che
  arriveranno immagini, ma non ci sono esempi reali.
- **Onboarding “sparso”**: esistono molte guide (README, GUIDE.md, docs/, GEOSCRIPT_MANUAL.md,
  PHYSICOCHEM_GUIDE.md, QUICK_START_REACTIONS.md), ma non una singola pagina “start here” che
  accompagni l’utente in un flusso lineare (installazione → progetto → dataset → output).

## 3) Test e qualità

- **Assenza di progetti di test**: nel repo sono presenti solo progetti di app/utility
  (`GeoscientistToolkit.csproj`, `GTK`, `NodeEndpoint`, `InstallerWizard`, `InstallerPackager`).
  Non risultano progetti `*.Tests.csproj`.
- **Verifiche “ad hoc”**: `VerificationReport.md` parla di test temporanei eliminati.
  Questo suggerisce che le verifiche non sono integrate in una suite ripetibile.
- **CI/CD non evidente**: non c’è directory `.github/workflows`, quindi non appare
  una pipeline automatica per build/test.

## 4) Dipendenze e setup

- **Requisiti estesi non centralizzati**: README elenca prerequisiti di base, ma non è
  chiaro se esistano componenti opzionali (OpenCL, ONNX, GDAL, modelli AI) con
  versioni minime e come installarli su ciascun OS.
- **NodeEndpoint**: è presente un progetto server con API/worker, ma non è chiaro
  un workflow di deployment/avvio “production‑like” (solo `dotnet run`).

## 5) Lacune organizzative

- **Roadmap**: non appare un documento di roadmap/versioning (oltre al README).
- **Contribuzione**: README spiega come contribuire, ma manca un `CONTRIBUTING.md`
  con standard dettagliati, checklist e convenzioni.

---

## Sintesi delle mancanze più impattanti

1. **Esecuzione reale di simulazioni multiphase via GeoScript** (RUN_MULTIPHASE)
2. **Estrazione micro‑PNM da immagini SEM** (Dual PNM)
3. **Funzionalità GIS incomplete** (caricamento GeoTIFF, idrologia)
4. **Visualizzazioni geotermiche parziali** (mappe 2D e ORC)
5. **Suite di test automatizzata + CI**
6. **Documentazione unificata e CHANGELOG reale**

Se vuoi, posso trasformare questo elenco in issue tecniche o un piano di lavoro ordinato per priorità.
