# Guida Rapida: Avviare una Reazione di Esempio

**GeoscientistToolkit - Quick Start Guide per Simulazioni Reattive**

---

## Indice

1. [Introduzione](#introduzione)
2. [🚀 Start Veloce con Interfaccia GTK](#-start-veloce-con-interfaccia-gtk)
3. [Esempio 1: Reazione Semplice in un Reattore Box](#esempio-1-reazione-semplice-in-un-reattore-box)
4. [Esempio 2: Precipitazione di Calcite](#esempio-2-precipitazione-di-calcite)
5. [Esempio 3: Trasporto Reattivo in PNM](#esempio-3-trasporto-reattivo-in-pnm)
6. [Visualizzare i Risultati](#visualizzare-i-risultati)
7. [Troubleshooting](#troubleshooting)

---

## Introduzione

GeoscientistToolkit offre due sistemi principali per simulazioni reattive:

- **PhysicoChem**: Reattori multifisici con geometrie personalizzate (box, cilindri, sfere, mesh Voronoi)
- **PNM Reactive Transport**: Trasporto reattivo attraverso reti di pori da immagini CT

Questa guida ti mostra come avviare rapidamente una simulazione di esempio.

---

## 🚀 Start Veloce con Interfaccia GTK

### Il Modo più Semplice: Testare il Reattore di Default

All'avvio, GeoscientistToolkit GTK crea automaticamente un **reattore esotermico di default** pronto per essere testato!

#### Passo 1: Avviare l'Applicazione GTK

```bash
cd GeoscientistToolkit
./bin/GeoscientistToolkit-gtk
# oppure su Windows: bin\GeoscientistToolkit-gtk.exe
```

#### Passo 2: Visualizzare il Reattore di Default

Quando l'applicazione si apre, vedrai:

1. **Pannello sinistro**: Lista dei dataset
   - Dovrebbe esserci `Default Exothermic Reactor` già caricato
2. **Pannello centrale**: Viewport 3D
3. **Pannello destro**: Opzioni di visualizzazione e controlli

**Clicca** sul dataset `Default Exothermic Reactor` per selezionarlo.

#### Passo 3: Visualizzare la Mesh 3D

Il reattore di default è un cilindro con una griglia **3x3x3** di celle (27 celle totali):

- **Dimensioni**: Raggio 3.5m, Altezza 6.0m
- **Reactanti iniziali**:
  - `ReactantA`: 5.0 mol/L
  - `ReactantB`: 3.0 mol/L
  - `Product`: 0.0 mol/L (si forma durante la reazione)
  - `Catalyst`: 0.01 mol/L
- **Temperatura iniziale**: 298.15 K (25°C)
- **Reazione esotermica**: ReactantA + ReactantB → Product + Calore

Nel **Viewport 3D**:
- Dovresti vedere **27 box** disposti in una griglia 3D
- Ruota la vista con **mouse destro + trascina**
- Zoom con **rotella del mouse**
- Pan con **mouse centrale + trascina**

#### Passo 4: Ispezionare le Celle

1. **Clicca su una cella** (box) nel viewport 3D
2. Nel pannello destro vedrai:
   ```
   Selected Cell: Cell_13
   Temperature: 298.15 K
   Pressure: 101325.0 Pa
   Volume: 81.67 m³

   Concentrations:
     ReactantA: 5.00 mol/L
     ReactantB: 3.00 mol/L
     Product: 0.00 mol/L
     Catalyst: 0.01 mol/L
   ```

#### Passo 5: Selezionare un Piano di Celle

Nel pannello destro, sezione **"Plane Selection"**:

1. Clicca su **"Select XY plane"** → Seleziona tutte le celle sullo stesso piano orizzontale
2. Clicca su **"Select XZ plane"** → Seleziona celle su piano verticale lungo X
3. Clicca su **"Select YZ plane"** → Seleziona celle su piano verticale lungo Y

Le celle selezionate vengono evidenziate in **ciano brillante**.

#### Passo 6: Configurare una Reazione

Per configurare le specie chimiche e le reazioni:

1. **Menu superiore** → `Tools` → `Configure Species...`
2. Si apre un dialogo dove puoi:
   - Aggiungere nuove specie chimiche
   - Modificare concentrazioni iniziali
   - Configurare reazioni chimiche

Oppure usa il **GeoScript** (vedi sotto).

---

### Creare un Nuovo Reattore da Zero

#### Passo 1: Creare un Nuovo Dataset PhysicoChem

Nella **toolbar superiore**:
- Clicca sul pulsante **"Add PhysicoChem"** (icona con simbolo fisico-chimico)

Oppure:
- **Menu** → `File` → `New project`

Verrà creato un nuovo dataset vuoto chiamato `PhysicoChem_HHmmss`.

#### Passo 2: Creare un Dominio (Reattore)

1. **Toolbar** → Clicca su **"Create Domain"** (icona mesh)

   **Oppure:** `Tools` → `Create Domain...`

2. Si apre il **Domain Creator Dialog** con le seguenti opzioni:

   **a) Nome del Dominio**
   ```
   Domain Name: [ReattorePrincipale]
   ```

   **b) Tipo di Geometria** (seleziona dal menu a tendina):
   - `Box (Rectangular)` ← **Scelta più semplice**
   - `Sphere`
   - `Cylinder`
   - `Cone`
   - `Torus`
   - `Parallelepiped`
   - `Custom 2D Extrusion`
   - `Custom 3D Mesh`
   - `Voronoi` ← **Mesh casuale**

   **c) Materiale** (seleziona dal menu):
   - Se hai materiali nella libreria, selezionali qui
   - Altrimenti lascia `(None)` e configuralo dopo

   **d) Parametri Geometrici** (cambiano in base al tipo):

   **Per Box:**
   ```
   Center X (m):  0.0
   Center Y (m):  0.0
   Center Z (m):  0.0
   Width (m):     10.0
   Depth (m):     10.0
   Height (m):    10.0
   ```

   **Per Cylinder:**
   ```
   Base Center X (m): 0.0
   Base Center Y (m): 0.0
   Base Center Z (m): 0.0
   Axis X: 0.0
   Axis Y: 0.0
   Axis Z: 1.0
   Radius (m):  5.0
   Height (m):  10.0
   ```

   **Per Voronoi (mesh casuale):**
   ```
   Number of Sites: 100
   Width (m):  10.0
   Depth (m):  10.0
   Height (m): 10.0
   ```

   **e) Opzioni**:
   - ✅ `Domain is active` (lascia selezionato)
   - ✅ `Allow interaction with other domains` (se vuoi che i reactanti si mescolino)

3. Clicca **"Create"**

Il dominio viene creato e la mesh viene generata automaticamente!

#### Passo 3: Configurare le Specie Chimiche

1. **Menu** → `Tools` → `Configure Species...`

2. Nel dialogo che si apre:
   - **Nome specie**: `Ca2+`
   - **Concentrazione iniziale**: `0.01` mol/L
   - Clicca **"Add"**

3. Ripeti per altre specie:
   - `CO3^2-`: `0.01` mol/L
   - `Cl-`: `0.02` mol/L
   - ecc.

#### Passo 4: Impostare Boundary Conditions

1. **Menu** → `Tools` → `Set Boundary Conditions...`

2. Scegli il tipo di condizione al contorno:
   - **Inlet** (ingresso reactanti)
   - **Outlet** (uscita prodotti)
   - **Fixed Temperature** (temperatura fissata)
   - **Heat Flux** (flusso di calore)
   - **No-Slip Wall** (parete solida)

3. Esempio - Inlet di Ca²⁺:
   ```
   Name: InletCalcio
   Type: Inlet
   Location: X Min (faccia sinistra)
   Variable: Concentration
   Species: Ca2+
   Value: 0.05 mol/L
   ```

#### Passo 5: Aggiungere Forze (Opzionale)

1. **Menu** → `Tools` → `Force Field Editor...`

2. Scegli tipo di forza:
   - **Gravity** (gravità)
   - **Vortex** (flusso vorticoso)
   - **Centrifugal** (forza centrifuga)
   - **Custom** (personalizzato)

3. Esempio - Gravità:
   ```
   Name: Gravità
   Type: Gravity
   Gravity Vector:
     X: 0.0
     Y: 0.0
     Z: -9.81
   ```

#### Passo 6: Eseguire la Simulazione

**Metodo 1: Interfaccia grafica** (se disponibile)
- Clicca su **"Run Simulation"**

**Metodo 2: GeoScript** (consigliato)

1. **Clicca sul tab "GeoScript"** nel pannello inferiore

2. Scrivi lo script:

```geoscript
# Configura parametri simulazione
SET_SIM_PARAMS 3600 1.0 true true true
# Argomenti: tempo_totale(s) time_step(s) enable_flow enable_heat enable_reactions

# Esegui simulazione
RUN_PHYSICOCHEM_SIMULATION

# Esporta risultati
EXPORT_RESULTS ./risultati_reazione
```

3. Clicca **"Run Script"** o premi `Ctrl+Enter`

#### Passo 7: Visualizzare i Risultati

Dopo la simulazione:

1. **Color Mode** (pannello destro):
   - Seleziona `Temperature` per vedere il campo di temperatura
   - Seleziona `Pressure` per vedere il campo di pressione
   - Seleziona `Concentration` e scegli una specie (es. `Ca2+`)

2. **Render Mode**:
   - `Wireframe`: Solo bordi delle celle
   - `Solid`: Celle solide colorate
   - `Points`: Solo centri delle celle

3. **Slicing** (taglio 3D):
   - ✅ Abilita `Enable Slicing`
   - Muovi lo slider per tagliare il reattore e vedere l'interno

---

### Esempio Completo GTK: Reazione Ca²⁺ + CO₃²⁻ → CaCO₃

#### Script GeoScript Completo

Crea un file `test_calcite_gtk.geoscript` con:

```geoscript
# ========================================
# Test Precipitazione Calcite - GTK
# ========================================

# 1. Crea nuovo dataset PhysicoChem
# (oppure usa quello di default già caricato)

# 2. Aggiungi le specie chimiche
ADD_SPECIES Ca2+ 0.02
ADD_SPECIES CO3^2- 0.02
ADD_SPECIES Na+ 0.01
ADD_SPECIES Cl- 0.01

# 3. Imposta concentrazioni iniziali per tutte le celle
SET_INITIAL_CONCENTRATION Ca2+ 0.01
SET_INITIAL_CONCENTRATION CO3^2- 0.01

# 4. Configura boundary condition - Inlet con alta concentrazione
ADD_BOUNDARY_CONDITION Inlet XMin Concentration Ca2+ 0.05
ADD_BOUNDARY_CONDITION Inlet XMin Concentration CO3^2- 0.05

# 5. Aggiungi gravità
ADD_FORCE Gravity 0 0 -9.81

# 6. Configura parametri simulazione
SET_SIM_PARAMS 1800 0.5 true true true
# 30 minuti, timestep 0.5s, flow ON, heat ON, reactions ON

# 7. Abilita nucleazione
ENABLE_NUCLEATION Calcite 0.0 0.0 0.0 1e6 1.2
# Argomenti: minerale X Y Z rate critical_supersaturation

# 8. Esegui simulazione
RUN_PHYSICOCHEM_SIMULATION

# 9. Stampa risultati
PRINT "Simulazione completata!"
PRINT "Visualizza i risultati con Color Mode → Concentration → Ca2+"
PRINT "Oppure Color Mode → Mineral Precipitation → Calcite"

# 10. Esporta
EXPORT_RESULTS ./risultati_calcite_gtk
```

#### Eseguire lo Script

1. **Nel tab GeoScript** dell'applicazione GTK
2. Incolla il contenuto sopra
3. Clicca **"Run Script"**
4. Osserva la simulazione in tempo reale nel viewport 3D!

---

### Tips per l'Interfaccia GTK

#### Navigazione 3D
- **Ruota**: Mouse destro + trascina
- **Zoom**: Rotella mouse
- **Pan**: Mouse centrale + trascina (o Shift + mouse destro)

#### Selezione Celle
- **Singola cella**: Click sinistro sulla cella
- **Piano XY**: Pulsante "Select XY plane"
- **Piano XZ**: Pulsante "Select XZ plane"
- **Piano YZ**: Pulsante "Select YZ plane"
- **Deseleziona tutto**: Click su sfondo

#### Visualizzazione
- **Wireframe**: Vedi solo gli spigoli (più veloce)
- **Solid**: Celle colorate piene (più chiaro)
- **Slicing**: Taglia il reattore per vedere l'interno
- **Isosurface**: Mostra superfici a valore costante

#### Camera Controls (pannello destro)
- **Yaw**: Rotazione orizzontale (-180° a 180°)
- **Pitch**: Inclinazione verticale (-90° a 90°)
- **Zoom**: Livello di zoom (0.1x a 8x)
- **Reset View**: Torna alla vista di default

---

## Esempio 1: Reazione Semplice in un Reattore Box

### Scenario
Due box sovrapposti con reactanti diversi che possono mescolarsi attraverso un'interfaccia.

### Codice C#

```csharp
using GeoscientistToolkit.Data.PhysicoChem;
using GeoscientistToolkit.Analysis.PhysicoChem;

// Crea il dataset
var dataset = new PhysicoChemDataset("ReattoreSemplice",
    "Due box sovrapposti con reactanti Ca2+ e HCO3-");

// Box superiore con Ca2+ (ione calcio)
var topBox = new ReactorDomain("BoxSuperiore", new ReactorGeometry
{
    Type = GeometryType.Box,
    Center = (0.5, 0.5, 0.75),  // Centro in metri
    Dimensions = (1.0, 1.0, 0.5) // Larghezza x Profondità x Altezza (m)
});

topBox.Material = new MaterialProperties
{
    Porosity = 0.3,           // 30% porosità
    Permeability = 1e-12      // m²
};

topBox.InitialConditions = new InitialConditions
{
    Temperature = 298.15,     // 25°C
    Pressure = 101325.0,      // 1 atm
    Concentrations = new Dictionary<string, double>
    {
        {"Ca2+", 0.01},       // 0.01 mol/L
        {"Cl-", 0.02}
    }
};

// Box inferiore con HCO3- (bicarbonato)
var bottomBox = new ReactorDomain("BoxInferiore", new ReactorGeometry
{
    Type = GeometryType.Box,
    Center = (0.5, 0.5, 0.25),
    Dimensions = (1.0, 1.0, 0.5)
});

bottomBox.InitialConditions = new InitialConditions
{
    Temperature = 298.15,
    Pressure = 101325.0,
    Concentrations = new Dictionary<string, double>
    {
        {"HCO3-", 0.01},      // 0.01 mol/L
        {"Na+", 0.01}
    }
};

// Aggiungi i domini
dataset.AddDomain(topBox);
dataset.AddDomain(bottomBox);

// Crea interfaccia interattiva tra i box
var boundary = new BoundaryCondition("Interfaccia",
    BoundaryType.Interactive,
    BoundaryLocation.Custom);

boundary.CustomRegionCenter = (0.5, 0.5, 0.5); // All'interfaccia
boundary.CustomRegionRadius = 0.1;
boundary.IsActive = true; // I reactanti possono mescolarsi

dataset.BoundaryConditions.Add(boundary);

// Aggiungi gravità
var gravity = new ForceField("Gravità", ForceType.Gravity)
{
    GravityVector = (0, 0, -9.81)
};
dataset.Forces.Add(gravity);

// Configura la simulazione
dataset.SimulationParams.TotalTime = 3600.0;      // 1 ora
dataset.SimulationParams.TimeStep = 1.0;          // 1 secondo
dataset.SimulationParams.EnableReactiveTransport = true;
dataset.SimulationParams.EnableFlow = true;
dataset.SimulationParams.EnableHeatTransfer = true;

// Esegui la simulazione
var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();

// Analizza i risultati
var finalState = dataset.CurrentState;
Console.WriteLine($"Temperatura media finale: {finalState.Temperature.Average()} K");
Console.WriteLine($"Simulazione completata!");
```

### Cosa fa questo codice?

1. **Crea due box** separati verticalmente
2. Box superiore contiene **Ca²⁺** (calcio)
3. Box inferiore contiene **HCO₃⁻** (bicarbonato)
4. All'interfaccia i reactanti si mescolano e possono reagire formando **CaCO₃** (calcite)
5. La gravità influenza il flusso verso il basso

---

## Esempio 2: Precipitazione di Calcite

### Scenario
Reattore cilindrico con flusso vorticoso dove il calcio e carbonato precipitano formando calcite.

### Codice C#

```csharp
// Crea reattore cilindrico
var dataset = new PhysicoChemDataset("ReattoreCalcite",
    "Precipitazione di CaCO3 in reattore cilindrico");

var cylinder = new ReactorDomain("Reattore", new ReactorGeometry
{
    Type = GeometryType.Cylinder,
    Center = (0, 0, 0),
    Radius = 0.5,      // 0.5 m raggio
    Height = 1.0,      // 1 m altezza
    InnerRadius = 0.0  // Cilindro solido (non cavo)
});

cylinder.Material = new MaterialProperties
{
    Porosity = 0.4,
    Permeability = 1e-11
};

// Condizioni iniziali supersaturate per precipitazione
cylinder.InitialConditions = new InitialConditions
{
    Temperature = 350.0,   // 77°C - temperatura elevata
    Pressure = 200000.0,   // 2 bar
    Concentrations = new Dictionary<string, double>
    {
        {"Ca2+", 0.02},    // Alta concentrazione
        {"CO3^2-", 0.02}   // Alta concentrazione → supersaturazione
    }
};

dataset.AddDomain(cylinder);

// Aggiungi campo di forza vorticoso per mescolare
var vortex = new ForceField("VorticeCentrale", ForceType.Vortex)
{
    VortexCenter = (0, 0, 0.5),
    VortexAxis = (0, 0, 1),
    VortexStrength = 10.0,  // 10 rad/s
    VortexRadius = 0.4
};

dataset.Forces.Add(vortex);

// Aggiungi sito di nucleazione al centro
var nucleationSite = new NucleationSite("NucleazioneCentrale",
    (0, 0, 0.5),
    "Calcite");

nucleationSite.NucleationRate = 1e6;           // nuclei/s
nucleationSite.CriticalSupersaturation = 1.2;

dataset.NucleationSites.Add(nucleationSite);

// Abilita nucleazione
dataset.SimulationParams.EnableNucleation = true;
dataset.SimulationParams.TotalTime = 1800.0;   // 30 minuti
dataset.SimulationParams.TimeStep = 0.5;

// Esegui
var solver = new PhysicoChemSolver(dataset);
solver.RunSimulation();

Console.WriteLine("Precipitazione di calcite completata!");
```

### Reazione Chimica

```
Ca²⁺ + CO₃²⁻ → CaCO₃(s) ↓
```

Quando la concentrazione è supersaturata (Ω > 1.2), si forma calcite solida che precipita.

---

## Esempio 3: Trasporto Reattivo in PNM

### Scenario
Simulazione di trasporto reattivo attraverso una rete di pori estratta da immagini CT, con precipitazione di calcite che riduce la permeabilità.

### Metodo 1: Codice C#

```csharp
using GeoscientistToolkit.Analysis.Pnm;
using GeoscientistToolkit.Data.Pnm;

// Assumi di avere già un PNM caricato
// var pnm = ... (generato da CT scan)

// Configura simulazione
var options = new PNMReactiveTransportOptions
{
    TotalTime = 3600.0,          // 1 ora
    TimeStep = 1.0,              // 1 secondo
    OutputInterval = 60.0,       // Salva ogni minuto

    // Parametri di flusso
    FlowAxis = FlowAxis.Z,
    InletPressure = 2.0f,        // Pa
    OutletPressure = 0.0f,       // Pa
    FluidViscosity = 1.0f,       // cP (acqua)
    FluidDensity = 1000f,        // kg/m³

    // Trasferimento di calore
    InletTemperature = 298.15f,  // 25°C
    ThermalConductivity = 0.6f,  // W/(m·K)
    SpecificHeat = 4184f,        // J/(kg·K)

    // Trasporto
    MolecularDiffusivity = 2.299e-9f,  // m²/s
    Dispersivity = 0.1f,               // m

    // Condizioni iniziali
    InitialConcentrations = new Dictionary<string, float>
    {
        {"Ca2+", 0.005f},      // mol/L (bassa concentrazione iniziale)
        {"CO3^2-", 0.005f}
    },
    InletConcentrations = new Dictionary<string, float>
    {
        {"Ca2+", 0.02f},       // mol/L (alta → supersaturazione)
        {"CO3^2-", 0.02f}
    },
    InitialMinerals = new Dictionary<string, float>
    {
        {"Calcite", 0.02f}     // 2% del volume dei pori
    },

    // Controllo reazioni
    EnableReactions = true,
    UpdateGeometry = true,
    MinPoreRadius = 0.1f,      // voxel
    MinThroatRadius = 0.05f    // voxel
};

// Esegui simulazione con report di progresso
var progress = new Progress<(float, string)>(p =>
{
    Console.WriteLine($"{p.Item1:P0}: {p.Item2}");
});

var results = PNMReactiveTransport.Solve(pnm, options, progress);

// Risultati
Console.WriteLine($"Permeabilità iniziale: {results.InitialPermeability:E3} mD");
Console.WriteLine($"Permeabilità finale: {results.FinalPermeability:E3} mD");
Console.WriteLine($"Variazione: {results.PermeabilityChange:P2}");
```

### Metodo 2: Script GeoScript (più semplice!)

Crea un file `esempio_reazione.geoscript`:

```geoscript
# Esempio: Trasporto Reattivo PNM
# Assumi di avere già caricato un dataset PNM

# 1. Imposta le specie chimiche
SET_PNM_SPECIES Ca2+ 0.02 0.005
# Argomenti: nome_specie concentrazione_inlet(mol/L) concentrazione_iniziale(mol/L)

SET_PNM_SPECIES CO3^2- 0.02 0.005

# 2. Imposta minerali iniziali (opzionale)
SET_PNM_MINERALS Calcite 0.02
# Argomenti: nome_minerale frazione_volume (2% dei pori riempito con calcite)

# 3. Esegui simulazione
RUN_PNM_REACTIVE_TRANSPORT 3600 1.0 298.15 2.0 0.0
# Argomenti: tempo_totale(s) time_step(s) temp_inlet(K) press_inlet(Pa) press_outlet(Pa)

# 4. Esporta risultati
EXPORT_PNM_RESULTS ./risultati_reazione
# Crea:
#   - summary.csv: Metriche complessive
#   - time_series.csv: Evoluzione permeabilità
#   - final_pores.csv: Stato finale di tutti i pori
```

Poi esegui lo script nel toolkit.

---

## Visualizzare i Risultati

### Nell'interfaccia GTK (GUI)

1. **Apri il dataset** dopo la simulazione
2. Seleziona il **viewport 3D**
3. Scegli la **modalità di colore**:
   - **Temperature**: Visualizza il campo di temperatura
   - **Pressure**: Visualizza il campo di pressione
   - **Species Concentration**: Seleziona una specie (es. Ca²⁺, CO₃²⁻)
   - **Mineral Precipitation**: Seleziona un minerale (es. Calcite)
   - **Reaction Rate**: Mostra dove le reazioni sono più attive

4. **Interazione**:
   - Clicca su una cella per vedere dettagli (T, P, concentrazioni)
   - Ruota con mouse destro
   - Zoom con rotella

### File di Output (PNM)

#### `summary.csv`
```csv
Metrica,Valore
Permeabilità Iniziale (mD),125.5
Permeabilità Finale (mD),98.3
Variazione Permeabilità (%),-21.7
Tempo di Calcolo (s),45.2
Iterazioni Totali,3600
Convergenza,True
```

#### `time_series.csv`
```csv
Tempo (s),Permeabilità (mD)
0.0,125.5
60.0,123.1
120.0,120.8
...
3600.0,98.3
```

#### `final_pores.csv`
```csv
PoreID,X,Y,Z,Radius,Volume,Pressure,Temperature,Ca2+,CO3^2-,Calcite_Volume
1,10.5,15.2,8.3,2.45,61.2,1.85,298.5,0.0045,0.0042,3.2
2,12.1,14.8,9.1,2.12,39.8,1.78,298.7,0.0048,0.0046,2.1
...
```

---

## Troubleshooting

### Problema: La simulazione diverge

**Soluzioni:**
- Riduci il `TimeStep` (es. da 1.0 a 0.1 secondi)
- Verifica le condizioni iniziali (concentrazioni non troppo alte)
- Controlla che le proprietà dei materiali siano ragionevoli

```csharp
dataset.SimulationParams.TimeStep = 0.1; // Ridotto
```

### Problema: Nessuna reazione avviene

**Soluzioni:**
- Verifica che `EnableReactiveTransport = true`
- Controlla le concentrazioni (devono essere supersaturate per precipitazione)
- Assicurati che i reactanti siano presenti in entrambi i domini/celle

```csharp
dataset.SimulationParams.EnableReactiveTransport = true;
dataset.SimulationParams.EnableReactions = true;  // Per PNM
```

### Problema: Simulazione molto lenta

**Soluzioni:**
- Aumenta il `TimeStep` (ma attenzione alla stabilità!)
- Riduci la risoluzione della mesh
- Abilita l'accelerazione GPU se disponibile

```csharp
dataset.SimulationParams.UseGPU = true;
dataset.SimulationParams.TimeStep = 5.0; // Aumentato
```

### Problema: File di output non trovati

**Soluzioni:**
- Verifica il percorso di esportazione
- Assicurati che la simulazione sia completata
- Controlla i permessi di scrittura

```csharp
// Usa percorso assoluto
EXPORT_PNM_RESULTS /home/user/results
```

---

## Parametri Comuni

### Temperature (K)
- **Ambiente**: 298.15 K (25°C)
- **Geotermico**: 350-450 K (77-177°C)
- **Idrotermale**: 450-600 K (177-327°C)

### Pressioni (Pa)
- **Atmosferica**: 101325 Pa (1 atm)
- **Sotterranea (100m)**: ~1 MPa
- **Profonda (1km)**: ~10 MPa
- **Geotermico**: 10-50 MPa

### Concentrazioni tipiche (mol/L)
- **Acqua pura**: 10⁻⁷ (H⁺, OH⁻)
- **Acqua di mare**: 0.035 (NaCl)
- **Acquiferi**: 0.001-0.01 (ioni disciolti)
- **Brine**: 0.1-5.0 (alta salinità)

### Porosità
- **Arenaria**: 0.15-0.30 (15-30%)
- **Calcare**: 0.05-0.20 (5-20%)
- **Fratturato**: 0.01-0.05 (1-5%)
- **Permeabile**: 0.30-0.50 (30-50%)

---

## Prossimi Passi

1. **Sperimenta** con i parametri (temperature, concentrazioni, geometrie)
2. **Crea geometrie personalizzate** (profili 2D, mesh Voronoi)
3. **Aggiungi più specie** chimiche e reazioni complesse
4. **Esplora sweep parametrici** per analisi di sensibilità
5. **Accoppia** con simulazioni geotermiche

---

## Riferimenti

- **Guida Completa PhysicoChem**: `PHYSICOCHEM_GUIDE.md`
- **Trasporto Reattivo PNM**: `Documentation/PNM_REACTIVE_TRANSPORT.md`
- **Manuale GeoScript**: `GEOSCRIPT_MANUAL.md`
- **Esempi**: `Examples/PNM_ReactiveTransport_Example.geoscript`

---

**Buone simulazioni!** 🧪⚗️🔬

---

**© 2025 GeoscientistToolkit Project**
