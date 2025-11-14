# Guida al Download dei Modelli ONNX

Questa guida spiega come ottenere i modelli ONNX necessari per la fotogrammetria real-time.

## Opzione 1: Modelli Pre-convertiti (Più Semplice)

### MiDaS Small (Depth Estimation)

1. Visita il repository PINTO Model Zoo:
   ```
   https://github.com/PINTO0309/PINTO_model_zoo
   ```

2. Naviga a:
   ```
   142_midas/01_float32/
   ```

3. Scarica `midas_v21_small_256.onnx`

4. Copia nella cartella `models/`:
   ```
   GeoscientistToolkit/models/midas_small.onnx
   ```

### SuperPoint (Keypoint Detection)

1. Dal PINTO Model Zoo:
   ```
   https://github.com/PINTO0309/PINTO_model_zoo/tree/main/144_SuperPoint
   ```

2. Scarica `superpoint_lightglue.onnx`

3. Copia nella cartella `models/`:
   ```
   GeoscientistToolkit/models/superpoint.onnx
   ```

### LightGlue (Feature Matching) - Opzionale

1. Repository LightGlue:
   ```
   https://github.com/cvg/LightGlue
   ```

2. Seguire le istruzioni per export ONNX nel README

3. Oppure cercare versioni pre-convertite su Hugging Face:
   ```
   https://huggingface.co/models?search=lightglue+onnx
   ```

## Opzione 2: Conversione Manuale da PyTorch

### Requisiti Python

```bash
pip install torch torchvision onnx onnxruntime numpy
```

### MiDaS - Script di Conversione

Crea un file `convert_midas.py`:

```python
import torch
import torch.onnx

# Scarica MiDaS
model = torch.hub.load("intel-isl/MiDaS", "MiDaS_small")
model.eval()

# Input dummy
dummy_input = torch.randn(1, 3, 256, 256)

# Export ONNX
torch.onnx.export(
    model,
    dummy_input,
    "midas_small.onnx",
    export_params=True,
    opset_version=11,
    do_constant_folding=True,
    input_names=['input'],
    output_names=['output'],
    dynamic_axes={
        'input': {0: 'batch_size', 2: 'height', 3: 'width'},
        'output': {0: 'batch_size', 2: 'height', 3: 'width'}
    }
)

print("MiDaS ONNX export completed!")
```

Esegui:
```bash
python convert_midas.py
```

### SuperPoint - Script di Conversione

Crea un file `convert_superpoint.py`:

```python
import torch
import torch.onnx
import sys

# Clone e installa SuperPoint
# git clone https://github.com/magicleap/SuperPointPretrainedNetwork
# Aggiungi il path
sys.path.append('./SuperPointPretrainedNetwork')

from models.SuperPointNet import SuperPointNet

# Carica il modello
model = SuperPointNet()
model.load_state_dict(torch.load('superpoint_v1.pth'))
model.eval()

# Input dummy (grayscale)
dummy_input = torch.randn(1, 1, 480, 640)

# Export ONNX
torch.onnx.export(
    model,
    dummy_input,
    "superpoint.onnx",
    export_params=True,
    opset_version=11,
    do_constant_folding=True,
    input_names=['image'],
    output_names=['keypoints', 'descriptors', 'scores'],
    dynamic_axes={
        'image': {0: 'batch_size', 2: 'height', 3: 'width'}
    }
)

print("SuperPoint ONNX export completed!")
```

## Opzione 3: Usare solo OpenCV (Nessun Modello Richiesto)

Se non vuoi scaricare modelli ONNX, puoi usare la pipeline con feature tradizionali:

1. **Depth**: Ometti il modello depth (il sistema userà triangolazione)
2. **Keypoints**: Il sistema userà automaticamente ORB features di OpenCV
3. **Matching**: Brute-force matcher integrato

**Nota**: Le performance saranno inferiori rispetto ai modelli deep learning, ma è comunque funzionante.

### Configurazione senza modelli

Nella finestra "Real-time Photogrammetry":
- Lascia vuoti i campi per i modelli ONNX
- Clicca "Initialize Pipeline"
- Il sistema userà automaticamente i fallback

## Verifica dei Modelli

Dopo aver scaricato/convertito i modelli, verifica che siano validi:

### Test con Python

```python
import onnxruntime as ort
import numpy as np

# Test MiDaS
session = ort.InferenceSession("midas_small.onnx")
dummy = np.random.randn(1, 3, 256, 256).astype(np.float32)
output = session.run(None, {"input": dummy})
print("MiDaS OK:", output[0].shape)

# Test SuperPoint
session = ort.InferenceSession("superpoint.onnx")
dummy = np.random.randn(1, 1, 480, 640).astype(np.float32)
output = session.run(None, {"image": dummy})
print("SuperPoint OK:", len(output), "outputs")
```

## Dimensioni dei File

- **MiDaS Small**: ~20-30 MB
- **MiDaS v2.1 Large**: ~100-200 MB
- **SuperPoint**: ~5-10 MB
- **LightGlue**: ~30-50 MB
- **ZoeDepth**: ~50-100 MB

## Link Utili

- **PINTO Model Zoo** (modelli pre-convertiti): https://github.com/PINTO0309/PINTO_model_zoo
- **Hugging Face** (modelli ONNX): https://huggingface.co/models?library=onnx
- **ONNX Model Zoo**: https://github.com/onnx/models
- **Intel OpenVINO Toolkit**: https://docs.openvino.ai/latest/omz_models_group_intel.html

## Risoluzione Problemi

### "Model format not recognized"
- Verifica che il file sia effettivamente ONNX (apri con editor di testo, dovrebbe iniziare con byte magici)
- Riconverti usando opset_version più recente

### "Dimension mismatch"
- Verifica le dimensioni di input/output del modello
- Potrebbe essere necessario modificare il preprocessing nel codice

### "Missing operators"
- Installa ONNX Runtime più recente
- Alcuni operatori potrebbero non essere supportati, usa opset_version più basso

## Supporto

Per problemi con i modelli, apri un issue su:
https://github.com/mattemangia/GeoscientistToolkit/issues
