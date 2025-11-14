# Real-time Photogrammetry Pipeline

Sistema completo di fotogrammetria real-time per GeoscientistToolkit.

## Caratteristiche

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

## Configurazione

### 1. Posizionare i modelli

Creare una cartella `models` nel progetto e copiare i file ONNX:

```
GeoscientistToolkit/
├── models/
│   ├── midas_small.onnx
│   ├── superpoint.onnx
│   └── lightglue.onnx (opzionale)
```

### 2. Configurare la pipeline

Dalla finestra "Real-time Photogrammetry":

1. **Tab Configuration**:
   - Inserire i percorsi ai modelli ONNX
   - Selezionare il tipo di modello depth
   - Abilitare GPU se disponibile
   - Impostare risoluzione target (640x480 consigliato per real-time)
   - Configurare camera intrinsics (o usare auto-stima)
   - Cliccare "Initialize Pipeline"

2. **Tab Capture**:
   - Selezionare sorgente video (webcam o file)
   - Cliccare "Start Capture"
   - Il sistema processerà i frame in tempo reale

3. **Tab Keyframes**:
   - Visualizzare i keyframe creati automaticamente
   - Eseguire bundle adjustment per raffinare

4. **Tab Georeferencing**:
   - Aggiungere almeno 3 Ground Control Points (GCP)
   - Inserire coordinate locali e mondiali
   - Calcolare la trasformazione di georeferenziazione
   - Opzionale: refinement con altitudine

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
