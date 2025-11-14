# Real-time Photogrammetry Pipeline

Sistema completo di fotogrammetria real-time per GeoscientistToolkit con gestione automatica dei modelli ONNX e esportazione avanzata.

## 🎯 Caratteristiche Principali

- **Stima della profondità**: Supporto per modelli ONNX (MiDaS, DPT, ZoeDepth)
- **Rilevamento keypoint**: SuperPoint con descrittori deep learning
- **Feature matching**: LightGlue per matching veloce e accurato (con fallback a brute-force)
- **RANSAC depth-aware**: Stima della posa con vincoli di profondità
- **Keyframe 2.5D**: Sistema di keyframe con PnP e bundle adjustment
- **Acquisizione video**: Supporto webcam e file video
- **Georeferenziazione**: Sistema GCP con refinement altitudine
- **Accelerazione GPU**: Supporto CUDA per inferenza ONNX

## Requisiti

### Pacchetti NuGet (già inclusi nel .csproj)
- Microsoft.ML.OnnxRuntime (>=1.19.2)
- Microsoft.ML.OnnxRuntime.Gpu (>=1.19.2) - per GPU
- OpenCvSharp4 (>=4.10.0)
- MathNet.Numerics (già presente)

### Modelli ONNX

I modelli ONNX devono essere scaricati separatamente:

#### 1. Depth Estimation

**MiDaS Small** (consigliato per real-time):
- Download: https://github.com/isl-org/MiDaS/releases
- File: `midas_v21_small_256.onnx` o simile
- Dimensione input: 384x384

**ZoeDepth** (per profondità metric-aware):
- Download: https://github.com/isl-org/ZoeDepth
- Convertire in ONNX usando script Python
- Dimensione input: 512x384

#### 2. Keypoint Detection

**SuperPoint**:
- Download: https://github.com/magicleap/SuperPointPretrainedNetwork
- Convertire in ONNX oppure usare port già convertiti:
  - https://github.com/PINTO0309/PINTO_model_zoo (SuperPoint ONNX)
- Input: Grayscale 1CHW

#### 3. Feature Matching

**LightGlue** (opzionale):
- Download: https://github.com/cvg/LightGlue
- Convertire in ONNX
- Input: Coppie di descrittori

**Nota**: Se LightGlue non è disponibile, il sistema usa automaticamente brute-force matching.

## 🚀 Quick Start (Nuovo!)

### Metodo 1: Download Automatico (Consigliato)

1. **Apri le Impostazioni**:
   ```
   Menu: Edit → Settings → Photogrammetry
   ```

2. **Download Automatico dei Modelli**:
   - Clicca "Download MiDaS Small (Depth)" - scarica automaticamente il modello di profondità (~20 MB)
   - Clicca "Download SuperPoint" - scarica automaticamente il rilevatore di keypoint (~5 MB)
   - (Opzionale) "Download LightGlue" se disponibile

3. **Configura Pipeline**:
   - Abilita GPU se disponibile (richiede CUDA)
   - Imposta risoluzione target (640x480 consigliato)
   - Configura camera intrinsics (o usa auto-stima)
   - Clicca "Apply" per salvare

4. **Avvia Fotogrammetria**:
   ```
   Menu: Tools → Real-time Photogrammetry
   Tab Configuration → Initialize Pipeline
   Tab Capture → Start Capture
   ```

### Metodo 2: Selezione Manuale

1. **Scarica i modelli** manualmente (vedi `ModelDownloadGuide.md`)

2. **Configura percorsi nelle Settings**:
   ```
   Edit → Settings → Photogrammetry
   ```
   - Usa il pulsante "Browse..." per selezionare ogni modello ONNX
   - Imposta la cartella Models Directory

3. **Salva e inizializza** la pipeline

## ⚙️ Configurazione Avanzata

### Settings → Photogrammetry

Le impostazioni di fotogrammetria sono completamente integrate nel sistema di configurazione:

#### **Model Paths**
- `Depth Model`: Percorso al modello ONNX per depth estimation
- `SuperPoint Model`: Percorso al modello per keypoint detection
- `LightGlue Model`: (Opzionale) Percorso al matcher
- `Models Directory`: Cartella di default per i modelli

#### **Pipeline Settings**
- `Use GPU Acceleration`: Abilita CUDA se disponibile
- `Depth Model Type`: MiDaS Small / DPT Small / ZoeDepth
- `Keyframe Interval`: Crea keyframe ogni N frame (1-30)
- `Target Width/Height`: Risoluzione elaborazione (320-1920 / 240-1080)

#### **Camera Intrinsics**
- `Focal Length X/Y`: Lunghezza focale in pixel
- `Principal Point X/Y`: Centro ottico
- Pulsante "Auto-estimate": Calcola da risoluzione

#### **Export Settings**
- `Default Export Format`: PLY / XYZ / OBJ
- `Export Textured Mesh`: Include texture (quando disponibile)
- `Export Camera Path`: Esporta traiettoria camera

### Configurare la Pipeline dalla Finestra RT Photogrammetry

Dalla finestra "Real-time Photogrammetry":

1. **Tab Configuration**:
   - Inserire i percorsi ai modelli ONNX
   - Selezionare il tipo di modello depth
   - Abilitare GPU se disponibile
   - Impostare risoluzione target (640x480 consigliato per real-time)
   - Configurare camera intrinsics (o usare auto-stima)
   - Cliccare "Initialize Pipeline"

2. **Tab Capture**:
   - Selezionare sorgente video:
     - **Webcam**: Scegli dalla lista di telecamere rilevate
     - **File Video**: Usa "Browse..." per selezionare file (.mp4, .avi, .mov, ecc.)
   - Cliccare "Start Capture"
   - Visualizzazione live di frame e depth map
   - Il sistema processerà i frame in tempo reale

3. **Tab Keyframes**:
   - Tabella con tutti i keyframe creati
   - Informazioni: Frame ID, Timestamp, Numero punti 3D
   - Pulsante "Perform Bundle Adjustment" per raffinamento

4. **Tab Georeferencing**:
   - **Aggiungere GCP**:
     - Nome del punto
     - Posizione locale (x,y,z) nel sistema della ricostruzione
     - Posizione mondo (E,N,Alt) in coordinate reali (UTM, lat/lon, ecc.)
     - Accuratezza in metri
   - **Gestione GCP**:
     - Tabella con tutti i GCP
     - Checkbox per attivare/disattivare
     - Pulsante "Remove" per eliminare
   - **Calcolo Transform**:
     - Richiede minimo 3 GCP attivi
     - Checkbox "Refine with Altitude" per accuratezza verticale
     - Mostra risultati: GCP usati, errore RMS, scala, traslazione, rotazione

5. **Tab Statistics**:
   - Frames processati totali
   - Tempo medio di elaborazione
   - FPS corrente
   - Grafico processing time
   - Totale keyframes e GCP

## 📤 Esportazione

### Menu File → Export

Tutte le esportazioni supportano georeferenziazione automatica se ≥3 GCP sono disponibili:

#### **Export Point Cloud**
Formati disponibili:
- **PLY (Polygon File Format)**:
  - ASCII format
  - Include coordinate XYZ e colori RGB
  - Compatibile con MeshLab, CloudCompare, Blender

- **XYZ (Simple Text)**:
  - Formato testo semplice
  - Una riga per punto: `X Y Z R G B`
  - Facile import in software GIS

- **OBJ (Wavefront)**:
  - Solo vertici (point cloud)
  - File .mtl separato per colori
  - Compatibile con tutti i software 3D

**Come esportare**:
1. Menu File → Export Point Cloud...
2. Scegli nome file e formato (.ply, .xyz, .obj)
3. Il sistema esporta automaticamente tutti i punti 3D dai keyframes
4. Se GCP disponibili, applica georeferenziazione

#### **Export Mesh**
- Attualmente esporta come point cloud in formato OBJ
- Nota: TSDF fusion per mesh densa non ancora implementato
- Roadmap futura: marching cubes, texturing

#### **Export Camera Path**
- Esporta traiettoria completa della camera
- Formato: `FrameID X Y Z QuatX QuatY QuatZ QuatW Timestamp`
- Utile per:
  - Visualizzazione percorso
  - Import in software animazione
  - Analisi movimento camera

## Workflow consigliato

### Pipeline Real-time (monocamera)

1. **Pre-processing**:
   - Undistort (se disponibili parametri di distorsione)
   - Resize a risoluzione target
   - Normalizzazione

2. **Depth Estimation**:
   - Inferenza modello depth (MiDaS/ZoeDepth)
   - Output: depth map relativa per frame

3. **Keypoint & Matching**:
   - **Opzione A**: SuperPoint + LightGlue (sparse, veloce)
   - **Opzione B**: ORB features (fallback, più lento ma senza modelli)

4. **RANSAC Depth-Aware**:
   - Stima Essential matrix con RANSAC
   - Allineamento scala usando depth map
   - Filtraggio outlier con vincoli di profondità

5. **Keyframe 2.5D**:
   - Creazione keyframe ogni N frame
   - Salvataggio keypoints con profondità (3D sparse)
   - PnP+RANSAC per tracking contro keyframe
   - Bundle adjustment in background

6. **Georeferencing**:
   - Raccolta GCP (minimo 3 punti)
   - Calcolo trasformazione di similarità (7 parametri)
   - Refinement altitudine opzionale

## Performance

### Tempi di elaborazione tipici (CPU i7, risoluzione 640x480)

- MiDaS Small: ~100-200 ms/frame
- SuperPoint: ~50-100 ms/frame
- LightGlue: ~30-50 ms/frame
- RANSAC + Pose: ~10-20 ms/frame
- **Totale**: ~200-400 ms/frame (~2-5 FPS)

### Con GPU (NVIDIA RTX 3060)

- MiDaS Small: ~10-20 ms/frame
- SuperPoint: ~5-10 ms/frame
- LightGlue: ~5-10 ms/frame
- **Totale**: ~30-50 ms/frame (~20-30 FPS)

## Troubleshooting

### "Failed to load model"
- Verificare che il percorso al file ONNX sia corretto
- Verificare che il modello sia nel formato ONNX corretto
- Controllare i log per dettagli

### "CUDA not available"
- Installare CUDA Toolkit (11.x o 12.x)
- Installare driver NVIDIA aggiornati
- Il sistema userà automaticamente CPU se CUDA non è disponibile

### "Not enough matches"
- Ridurre il threshold di confidence nei keypoint
- Verificare che la scena abbia texture sufficiente
- Provare diverse impostazioni di esposizione camera

### Frame rate basso
- Ridurre risoluzione target
- Disabilitare modelli pesanti (usare MiDaS Small invece di ZoeDepth)
- Abilitare GPU acceleration
- Aumentare keyframe interval

## Limitazioni

1. **Scala ambigua**: La ricostruzione monoculare ha scala arbitraria. Usare GCP per scala assoluta.
2. **Texture necessaria**: Scene senza texture (pareti bianche) producono pochi keypoint.
3. **Movimento lento**: Per risultati migliori, muovere la camera lentamente.
4. **Depth relativa**: MiDaS produce profondità relativa, non metrica. ZoeDepth è più accurato ma più lento.

## Sviluppi futuri

- [ ] Ricostruzione densa con TSDF fusion
- [ ] Meshing real-time con marching cubes
- [ ] Texturing automatico
- [ ] Loop closure detection
- [ ] Export PLY/OBJ point cloud
- [ ] Multi-camera support
- [ ] IMU fusion per posa più robusta

## Riferimenti

- MiDaS: https://github.com/isl-org/MiDaS
- ZoeDepth: https://github.com/isl-org/ZoeDepth
- SuperPoint: https://github.com/magicleap/SuperPointPretrainedNetwork
- LightGlue: https://github.com/cvg/LightGlue
- OpenCV: https://opencv.org/
- ONNX Runtime: https://onnxruntime.ai/
