# GAIA × PRISM — Analisi di convergenza verificabile

> **Stato dell'audit:** 16 luglio 2026
>
> **Snapshot esaminati:** GAIA `e2177b2`; PRISM `14247f8`
>
> **Perimetro:** ispezione statica dei due repository locali, delle solution, dei progetti, dei sorgenti e dei test. I test non sono stati eseguiti come parte di questa revisione.
>
> **Obiettivo:** descrivere capacità, maturità e confini delle due piattaforme senza trasformare LOC, nomi dei moduli o feature dichiarate in prove di superiorità scientifica.

## 1. Conclusione esecutiva

La precedente lettura “GAIA = core/imaging, PRISM = basin/AI” coglie una parte della specializzazione, ma sottostima nettamente PRISM.

**PRISM è oggi la piattaforma più avanzata nel workflow geoscientifico end-to-end.** Il vantaggio non riguarda soltanto PINN o scala di bacino: comprende acquisizione multi-sorgente, armonizzazione e provenance, modellazione 3D/4D, inversione geofisica, InSAR/TomoSAR, rischi concatenati, modellazione di reservoir, assimilazione e scenari, ranking scientifico, gestione dei checkpoint, incertezza, GUI integrata, numerosi comandi headless ed export tecnici. Queste capacità sono collegate nello stesso workspace, non sono soltanto solver isolati.

**GAIA conserva un vantaggio netto in un insieme più ristretto ma importante di workflow sperimentali e core/pore-scale:** CT/µCT, segmentazione assistita da modelli ONNX, PNM e dual-PNM, NMR, omogeneizzazione termica da voxel, simulazioni acustiche su volumi CT, petrografia/texture, fotogrammetria e un DSL generalista (GeoScript). GAIA è anche più estensibile come piattaforma di dataset eterogenei e add-in.

La strategia corretta non è una divisione simmetrica né la migrazione automatica di moduli in base al numero di righe:

- **PRISM deve essere il sistema principale** per costruzione del modello di sottosuolo, geofisica/inversione, remote sensing, geohazard, reservoir/geotermia field-scale, AI physics-informed, scenari e decision support.
- **GAIA deve essere il sistema specializzato** per imaging e caratterizzazione di laboratorio/core/pore-scale, estrazione di proprietà da CT, simulazioni voxel-based e automazione GeoScript.
- L'integrazione deve avvenire mediante contratti versionati e formati standard; non copiando interi solver tra repository.

## 2. Metodo e significato dei giudizi

Ogni capacità è valutata su evidenze distinte:

| Livello | Significato |
|---|---|
| **P** — presente | Esiste codice sostanziale, non soltanto documentazione o una voce UI. |
| **I** — integrato | La capacità è raggiungibile dal workflow principale, dalla GUI o da CLI/API. |
| **T** — testato | Esistono test automatici pertinenti nel repository. Non implica che siano stati eseguiti in questo audit. |
| **V** — validato | Esiste confronto esplicito con soluzione analitica, benchmark pubblicato o dataset di riferimento con tolleranza. |
| **O** — operativo | Sono gestiti persistenza, errori, fallback, cancellazione, export e riproducibilità. |

Un modulo può essere matematicamente sofisticato ma non operativo; un workflow molto integrato può non essere ancora validato scientificamente. Per questo il documento evita aggettivi non misurabili come “eccellente”.

### Evidenza quantitativa riproducibile

| Indicatore statico | GAIA | PRISM | Interpretazione corretta |
|---|---:|---:|---|
| Progetti nella solution | 9 | 17 | PRISM ha una scomposizione funzionale più ampia; il dato non misura da solo la qualità. |
| File C# nel repository | 728 | 1.280 | Misura approssimativa della superficie implementata, non della correttezza. |
| Metodi marcati `[Fact]`/`[Theory]` nelle suite principali | 24 | 1.327 | Vantaggio ingegneristico molto forte di PRISM; il numero non equivale a 1.327 validazioni indipendenti. |
| Runtime principale | .NET 10, ImGui.NET/Veldrid | .NET 8, ImGui.NET/OpenTK/Mapsui | GAIA ha runtime più recente; PRISM ha uno stack cartografico e AI più ricco. |
| Automazione | GeoScript, API, NodeEndpoint, CLI test/diagnostic | Ampia CLI headless, scheduler, action engine, adapter in-process | Approcci diversi; PRISM è più completo per pipeline operative, GAIA ha il DSL più generale. |

I conteggi sono riferiti agli snapshot sopra indicati e devono essere rigenerati quando i repository cambiano.

## 3. Dove PRISM è sostanzialmente più avanti

### 3.1 Piattaforma integrata e ciclo di vita del modello

Il maggiore vantaggio di PRISM è architetturale e operativo:

- costruisce dataset da sorgenti geologiche, sismiche, idrogeologiche, termiche e SAR;
- armonizza litologie, priorità delle fonti, AOI, cache e provenance;
- addestra e interroga modelli in-process attraverso facade di modulo e adapter comuni;
- gestisce checkpoint, metadata, resume, warm-start, merge, distillazione teacher-student e rollback;
- applica un contratto condiviso alle boundary conditions: richiesta utente ∩ dati di scena ∩ metadata del checkpoint;
- valuta modelli con indici scientifici e ranking entropy-weighted TOPSIS;
- coordina ensemble XPINN e propagazione dell'incertezza;
- registra output diversi nello stesso viewport 3D e nell'Export Center;
- espone molti workflow anche da CLI headless, con cultura numerica invariabile.

GAIA possiede buoni componenti di progetto, dataset, scripting e calcolo distribuito, ma non presenta un ciclo altrettanto coerente per training, ranking, selezione, assimilazione e riuso dei modelli scientifici.

**Verdetto:** PRISM è avanti come prodotto geoscientifico integrato, non soltanto come collezione di algoritmi.

### 3.2 Geofisica, sismologia e inversione

| Capacità | GAIA | PRISM | Verdetto |
|---|---|---|---|
| Acquisizione sismologica | Loader e processing locali | FDSN event/station/dataselect, miniSEED, QuakeML, ITACA, inventory e waveform | **PRISM nettamente avanti** |
| Tomografia passiva | Non comparabile | PULSE: ray tracing, LSQR smorzato, smoothing, coverage e resolution diagnostics | **PRISM unico** |
| FWI | Non presente come pipeline completa | PINNACLE/FWI con workflow, artifact e diagnostica | **PRISM unico** |
| InSAR/TomoSAR | Non presente | ECHO: ingest SAR, coregistrazione, interferogrammi, filtering, unwrap, APS, PS/SBAS, focusing TomoSAR, geocoding | **PRISM unico** |
| Modellazione d'onda | FDTD e post-processing damage/fracture | SHAKE, wavefield global roadmap, mesh/voxel workflow, coupling con modelli di velocità | **Complementari**, ma PRISM ha il workflow più integrato |
| SEG-Y | Processing/visualizzazione e synthetic tie | Reader, synthetic output, PDFSEGY, raster→SEG-Y, integrazione PULSE/QUAKE | **PRISM più ampio end-to-end**; GAIA conserva processing specialistico |
| Rock physics | Relazioni distribuite tra comandi/moduli | Contratti e servizi condivisi nei workflow QUAKE/PULSE/SubNeRF | **PRISM più strutturato** |

Il supporto FDSN non è una semplice feature accessoria: usa interfacce standard per eventi, stazioni e serie temporali, con miniSEED/StationXML/QuakeML come formati interoperabili. La specifica ufficiale è mantenuta dalla [FDSN](https://www.fdsn.org/webservices/).

### 3.3 AI physics-informed e gestione del training

PRISM è molto più avanti nell'AI scientifica:

- PINN 3D/4D per crosta/sismica, acquiferi, geotermia e geohazard;
- autograd TorchSharp per residui PDE, incluse derivate di secondo ordine;
- SubNeRF per rappresentazioni implicite continue del sottosuolo;
- loss geologiche, petrofisiche, strutturali e di boundary compliance;
- training studio, scheduler, cancellazione, log live e checkpoint discovery;
- ranking multi-criterio, ensemble XPINN, distillazione e uncertainty quantification;
- query cross-model e warm-start tra domini.

GAIA usa ONNX in modo valido e utile per inferenza di modelli di visione (SAM2, MicroSAM, Grounding DINO), ma non offre un'infrastruttura equivalente per addestramento scientifico, inversione o assimilazione physics-informed.

**Correzione rispetto al documento precedente:** NeRF non è esclusivo di GAIA. PRISM contiene SubNeRF e lo integra nel modello di sottosuolo; la cartella `Prism.SubNeRF` non è un progetto della solution, ma la capacità è ospitata e orchestrata dall'app principale. GAIA conserva invece un'implementazione Instant-NGP orientata alla ricostruzione visuale, con finalità diversa.

### 3.4 Geohazard e osservazione della Terra

CASCADE ed ECHO danno a PRISM una copertura che GAIA non possiede:

- catena meteo → pioggia/vento → alluvione → instabilità/runout → costa;
- DEM e batimetria, routing pluviale, infiltrazione, Voellmy-Salm, slope failure e jointed-rock kinematics;
- strutture costiere, trasporto litoraneo, arretramento e danno catastale;
- SAR multi-provider e deformazione del suolo;
- scenari dataset-free su DEM/mesh, oltre ai progetti completi;
- visualizzazione e animazione dei risultati nello stesso workspace.

Sentinel-1 è concepito da ESA per osservazione radar all-weather/day-night e applicazioni che includono deformazioni, alluvioni e terremoti; l'integrazione di queste osservazioni nei workflow di rischio è quindi un vantaggio sostanziale, non solo UI ([ESA Sentinel-1](https://www.esa.int/Applications/Observing_the_Earth/Copernicus/Sentinel-1/Introducing_Sentinel-1)).

### 3.5 BRIDGE: scenari, assimilazione e decision support

La precedente descrizione di BRIDGE come semplice “orchestrator what-if/sweep” è incompleta. Il codice include:

- scenari e interventi con provenance e livelli di confidenza;
- baseline cache e confronti controfattuali;
- Latin Hypercube, algoritmi genetici e simulated annealing;
- ensemble smoother, particle filter e una procedura 4D-Var;
- osservazioni multi-anchor, misfit, posteriori e ranking di plausibilità;
- adapter verso motori scientifici e controlli di validità dello scenario.

GAIA non ha un equivalente. GeoScript può concatenare operazioni, ma un DSL di pipeline non sostituisce assimilazione, posterior estimation o ranking controfattuale.

### 3.6 Reservoir, geotermia field-scale e techno-economics

| Area | GAIA | PRISM | Verdetto |
|---|---|---|---|
| Reservoir flow | Multiphase e reactive transport soprattutto core/pore-scale | ReservoirFlux: mesh, bilanci di massa/energia, EOS acqua-energia, Newton solve, pozzi/BHE, snapshot e coupling | **PRISM avanti a scala reservoir** |
| Geotermia | THM classico, dual continuum, ORC, BTES, HVAC | natural-state PINN, ReservoirFlux, CRAFT, BHE, TerraYield e integrazione con tomography | **PRISM avanti nel workflow field-to-economics** |
| Techno-economics | Modello economico integrato nel modulo GAIA | TerraYield: impianto, reservoir, well field, drilling correlations, LCOE/LCOH e coupling | **PRISM avanti** |
| Proprietà da CT | Dirette da imaging/PNM | Riceve proprietà voxel/mesh, ma non ha la stessa pipeline CT | **GAIA avanti come sorgente core-scale** |

La formulazione acqua/vapore deve essere confrontata con le release ufficiali IAPWS e con punti di controllo pubblicati, non sostituita genericamente con una correlazione di densità. IAPWS-IF97 è la formulazione industriale ufficiale ([IAPWS R7-97](https://www.iapws.org/relguide/IF97-Rev.pdf)).

### 3.7 Geotecnica e stabilità

PRISM non offre solo limit analysis:

- FORGE include stabilità 2D/3D, cinematismi, Newmark e integrazione con osservazioni/campi;
- Prism.Geotech include FEM esplicito tetraedrico, import mesh, materiali non lineari, giunti, strain softening, CPU/OpenCL e output tecnici;
- CASCADE include infinite slope transiente, runout e cinematica di roccia fratturata;
- BRIDGE può esplorare e assimilare scenari geotecnici;
- ECHO fornisce osservazioni di deformazione utilizzabili nei workflow di stabilità.

GAIA conserva due nicchie reali: DEM a blocchi e simulazione triassiale/CT-voxel. Tuttavia, come piattaforma per analisi geotecnica territoriale e integrazione observation-to-model, **PRISM è molto più avanti**.

## 4. Dove GAIA conserva un vantaggio reale

### 4.1 Imaging CT/µCT e caratterizzazione digitale della roccia

GAIA ha una pipeline specializzata non replicata da PRISM:

- stack CT e gestione di volumi grandi/streaming;
- strumenti manuali e assistiti di segmentazione 2D/3D;
- inferenza ONNX con modelli di visione generalisti e microscopy-oriented;
- estrazione geometrica e quantitativa dal volume;
- passaggio CT → PNM/dual-PNM → permeabilità/trasporto;
- NMR random walk e mappe di rilassamento;
- omogeneizzazione della conducibilità termica;
- acustica su geometrie derivate dalla CT;
- dissoluzione/trasporto reattivo voxel-based.

PRISM dispone di segmentazione volumetrica per modelli di reservoir e di `CTDissolutionSimulator` in GeoGenesis, quindi “segmentazione” e “CT dissolution” non devono essere dichiarate genericamente esclusive di GAIA. Il vantaggio GAIA è la **profondità del workflow digital-rock**, dalla sorgente immagine alle proprietà pore-scale.

### 4.2 PNM, dual-PNM e NMR

Non sono stati individuati equivalenti sostanziali in PRISM per:

- estrazione di pore network da immagini;
- accoppiamento macro/microporosità dual-PNM;
- simulazione NMR T1/T2 mediante random walk;
- upscaling diretto di proprietà petrofisiche dalla microstruttura.

Questi moduli devono restare in GAIA e diventare la sorgente certificata di priors/upscaled properties per CRAFT, ReservoirFlux, SubNeRF e QUAKE.

### 4.3 GeoScript e automazione dataset-centrica

GeoScript rimane una capacità distintiva: consente pipeline uniformi su immagini, tabelle, GIS, CT, borehole, seismic, mesh, PNM, termodinamica e petrologia. PRISM ha una CLI molto più ampia per workflow applicativi e training, ma non un DSL embeddable equivalente.

La convergenza corretta è:

- GeoScript per trasformazioni riproducibili e preprocessing in GAIA;
- CLI/API PRISM per training, inversione, scenari e simulazioni field-scale;
- un adapter esplicito, con manifest degli input/output, invece di comandi shell non tipizzati.

### 4.4 Fotogrammetria e ricostruzione da immagini

GAIA conserva il vantaggio su SfM real-time/offline, feature matching, mesh, orthomosaic e DEM da immagini. SubNeRF di PRISM non è un sostituto: ricostruisce implicitamente proprietà del sottosuolo da investigazioni sparse, non una scena fotogrammetrica con lo stesso obiettivo metrico.

### 4.5 Petrologia, stratigrafie multiple e laboratorio virtuale

GAIA offre capacità più specialistiche in:

- petrologia ignea e cristallizzazione frazionata;
- cataloghi stratigrafici internazionali/nazionali espliciti;
- triaxial lab simulation;
- texture analysis di sezioni sottili;
- simulazioni nucleari/PhysicoChem non centrali al modello di sottosuolo PRISM.

PRISM è comunque più avanzato nella gestione operativa di litologie, correlazioni, strutture e consistenza geologica del modello 3D. “Stratigrafia presente in GAIA” non implica superiorità generale nell'interpretazione geologica.

## 5. Matrice corretta delle responsabilità

| Dominio | Sistema principale | Sistema complementare | Motivazione |
|---|---|---|---|
| CT/µCT, digital rock, PNM, NMR | **GAIA** | PRISM come consumatore | GAIA estrae proprietà dalla microstruttura. |
| Segmentazione di reservoir 3D | **PRISM** | GAIA per segmentazione CT specialistica | PRISM integra label, CRAFT, viewport ed export reservoir. |
| Dataset territoriali e provenance | **PRISM** | GAIA per loader scientifici specialistici | PRISM ha acquisizione e harmonisation multi-provider. |
| GIS operativo e mappe di progetto | **PRISM** | GAIA per analisi GIS dataset-centrica | PRISM combina Mapsui, AOI, fonti e modelli; GAIA ha tool GIS generici. |
| Borehole e correlazione | **PRISM** | GAIA per editing/visualizzazione specialistica | PRISM copre ingest→correlation→model→export. |
| Sismologia, tomography, FWI | **PRISM** | GAIA per alcuni filtri e seismic cube | PRISM ha la catena di inversione e dati standard. |
| InSAR/TomoSAR | **PRISM** | — | ECHO non ha equivalente GAIA. |
| PINN/implicit subsurface AI | **PRISM** | GAIA per inference vision ONNX | Training e physics losses sono capacità PRISM. |
| Geohazard e costa | **PRISM** | GAIA per DEM/visualizzazioni specifiche | CASCADE unifica forcing, hazard e impatti. |
| Geotecnica territoriale | **PRISM** | GAIA per DEM/triassiale/voxel | FORGE + Geotech + CASCADE + ECHO + BRIDGE. |
| Reservoir/geotermia field-scale | **PRISM** | GAIA per proprietà core-scale e ORC specifico | ReservoirFlux/TerraYield/CRAFT danno continuità operativa. |
| Geochimica core/voxel | **GAIA** | PRISM GeoGenesis per coupling field-scale | La scelta dipende dalla scala, non dalla duplicazione nominale. |
| Scenari, assimilazione, ranking | **PRISM** | — | BRIDGE e ranking scientifico non hanno equivalente. |
| Fotogrammetria/SfM | **GAIA** | — | Pipeline metrica da immagini non presente in PRISM. |
| Scripting di trasformazione | **GAIA** | PRISM CLI per workflow applicativi | GeoScript e CLI risolvono problemi diversi. |
| Visualizzazione integrata 3D/4D | **PRISM** | GAIA per viewer per-dataset | PRISM registra modelli e simulazioni nello stesso spazio interrogabile. |

## 6. Correzioni di affermazioni precedenti

| Affermazione precedente | Correzione |
|---|---|
| PRISM ha circa 200 test, GAIA circa 12 | Gli snapshot contengono rispettivamente **1.327** e **24** metodi `[Fact]/[Theory]` nelle suite principali. Sono conteggi statici, non esiti di esecuzione. |
| PRISM non ha NeRF | PRISM ha **SubNeRF** integrato nell'app; non è un progetto autonomo incluso nella solution. Il suo scopo differisce dall'Instant-NGP di GAIA. |
| PRISM non ha GIS dedicato | PRISM ha una forte piattaforma cartografica e di acquisizione (Mapsui/SkiaSharp, AOI, basemap, dataset builder, provider geologici). GAIA ha un modello `Dataset` GIS più generalista; non è corretto dedurne che PRISM sia privo di GIS. |
| Segmentazione è esclusiva GAIA | PRISM ha segmentazione 3D di volumi/reservoir; GAIA è avanti nella segmentazione CT e nell'inferenza ONNX. |
| Geothermal classico: GAIA superiore in generale | GAIA ha solver classici e componenti core/system-scale validi; PRISM è superiore nel workflow field-scale integrato con reservoir, tomography, PINN, BHE e economics. |
| PRISM fa solo upper-bound in geotecnica | PRISM combina FORGE, FEM esplicito, CASCADE, Newmark, jointed rock, InSAR e assimilazione BRIDGE. |
| GAIA è superiore in borehole correlation | GAIA ha buoni strumenti specialistici; PRISM copre una catena più ampia da estrazione/digitizzazione a interpolazione, modello e viewport. |
| PRISM dovrebbe migrare da TorchSharp a ONNX | Non come sostituzione generale: ONNX Runtime è adatto all'inferenza portabile; TorchSharp è necessario per training/autograd dei PINN. Ha senso esportare modelli compatibili, non eliminare il backend di training. |
| Il fix corretto per IAPWS-97 è “usare Kell” | Una correlazione Kell può essere valida nel proprio intervallo per acqua liquida, ma non sostituisce IAPWS-IF97 nell'intero dominio termodinamico. Servono regione, unità e benchmark ufficiali. |

## 7. Problemi tecnici da trattare con cautela

### 7.1 GAIA

- `EstimateContactArea()` nel DEM e lock ordering richiedono un audit dedicato prima di dichiarare affidabilità ingegneristica.
- Flag SIMD/GPU non collegati non devono essere presentati come accelerazione disponibile.
- La copertura di test è troppo ridotta rispetto alla superficie numerica del prodotto.
- Le feature uniche devono essere accompagnate da benchmark quantitativi, non solo da presenza nel codice.
- Nullable e warning soppressi riducono la capacità del build di evidenziare difetti.

### 7.2 PRISM

- L'ampiezza della suite non dimostra automaticamente validazione peer-reviewed di ogni modulo.
- Alcuni percorsi GPU/OpenCL hanno fallback CPU, ma la parity va documentata per singolo kernel e device.
- La grande `PrismApp` partial e la costruzione diretta dei servizi aumentano accoppiamento e costo di manutenzione.
- `Prism.SubNeRF` e `Prism.Orchestrator` presenti nell'albero ma fuori solution possono creare ambiguità documentale.
- Feature definite come roadmap o prepared interface non devono apparire nelle matrici come complete.
- L'uso di numerosi provider esterni richiede test periodici di endpoint, schema, licenza e provenance.

## 8. Roadmap di convergenza aggiornata

### Priorità 0 — Contratto e inventario

1. Generare automaticamente un capability manifest per entrambi i repository: modulo, entry point, input, output, backend, test, benchmark, stato sperimentale.
2. Definire una tassonomia comune delle scale: voxel/pore, plug/core, well, reservoir, field/basin, regional.
3. Vietare nei documenti i giudizi “superiore/eccellente” privi di criterio, evidenza e snapshot.

### Priorità 1 — Ponte GAIA digital-rock → PRISM subsurface

4. Esportare da GAIA porosità, permeabilità tensoriale, tortuosità, saturazione, conducibilità, Vp/Vs e incertezza.
5. Importare tali proprietà in PRISM come priors versionati per CRAFT, SubNeRF, QUAKE e ReservoirFlux.
6. Conservare nel manifest provenienza CT, voxel size, segmentazione, REV, metodo di upscaling e unità.

### Priorità 2 — Interoperabilità

7. Preferire VTK/VTI/VTU, NetCDF/CF, GeoTIFF, SEG-Y, LAS, QuakeML/StationXML/miniSEED e JSON schema versionati.
8. Non usare `.gtp` come formato comune primario: è un contenitore applicativo GAIA, meno interoperabile di standard espliciti.
9. Definire CRS, asse verticale, datum, convenzione di profondità, ordine degli assi, unità e NoData in ogni scambio.

### Priorità 3 — Riutilizzo senza duplicazione

10. Esporre GeoScript come producer/preprocessor e PRISM CLI/API come consumer tipizzato.
11. Evitare copie manuali di termodinamica e geochimica: estrarre librerie condivise soltanto dopo test di equivalenza e analisi delle dipendenze.
12. Usare PRISM BRIDGE per scenari multi-scala richiamando adapter GAIA headless, con cancellazione e manifest degli artifact.

### Priorità 4 — Certificazione scientifica

13. Portare in GAIA il pattern di test analitici/benchmark con DOI e tolleranza, iniziando dai moduli unici.
14. Classificare anche i test PRISM in unit, integration, parity, analytic benchmark e published-data validation.
15. Creare benchmark congiunti CT→PNM→upscaling→ReservoirFlux e seismic acquisition→PULSE/QUAKE→geological consistency.

## 9. Decisione finale

La convergenza non deve essere presentata come un equilibrio tra due piattaforme di maturità generale equivalente.

**PRISM è il candidato naturale a piattaforma principale per il modello geoscientifico integrato e operativo.** È più avanti per ampiezza dei workflow, integrazione tra osservazioni e modelli, AI scientifica, inversione, scenari, ranking, geohazard, reservoir, automazione e copertura di test.

**GAIA non va ridotta né assorbita:** ha una specializzazione preziosa e difficilmente sostituibile nel digital rock, nell'imaging, nel PNM/NMR, nelle simulazioni voxel-based, nella fotogrammetria e nell'automazione GeoScript. Il suo valore cresce se diventa il laboratorio/core-scale engine che produce proprietà e vincoli con provenance per PRISM.

La direzione raccomandata è quindi:

> **GAIA misura, segmenta, simula e fa upscaling dalla microstruttura; PRISM acquisisce, integra, inverte, assimila, valuta scenari e costruisce il modello 3D/4D del sottosuolo.**

Questa separazione riconosce correttamente che, per molti aspetti cruciali, PRISM è già molto più avanti, senza cancellare le aree in cui GAIA rimane realmente unica.
