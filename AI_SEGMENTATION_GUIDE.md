# AI Segmentation Module Guide

## Overview

GeoscientistToolkit now includes advanced AI-powered segmentation capabilities based on state-of-the-art models from the CTS project. This module provides three main AI models and combined pipelines for automatic and interactive segmentation of CT image stacks.

## Models Included

### 1. SAM2 (Segment Anything Model 2)
- **Type**: Interactive point-based segmentation
- **Use case**: Precise segmentation with minimal user input
- **Models required**:
  - `sam2.1_large.encoder.onnx`
  - `sam2.1_large.decoder.onnx`

### 2. MicroSAM
- **Type**: Microscopy-optimized SAM
- **Use case**: High-resolution microscopy images, zero-shot segmentation
- **Models required**:
  - `micro-sam-encoder.onnx`
  - `micro-sam-decoder.onnx`

### 3. Grounding DINO
- **Type**: Text-prompted object detection
- **Use case**: Automatic detection of objects based on natural language descriptions
- **Models required**:
  - `g_dino.onnx`
  - `vocab.txt`

## Installation

### 1. Download ONNX Models

Download the pre-trained ONNX models from the CTS repository or official sources. Models must be compatible with the modified tensor formats used in CTS.

### 2. Place Models in ONNX Directory

Copy all model files to the `ONNX/` directory in your GeoscientistToolkit installation:

```
GeoscientistToolkit/
├── ONNX/
│   ├── sam2.1_large.encoder.onnx
│   ├── sam2.1_large.decoder.onnx
│   ├── micro-sam-encoder.onnx
│   ├── micro-sam-decoder.onnx
│   ├── g_dino.onnx
│   └── vocab.txt
```

### 3. Configure Paths

Open GeoscientistToolkit and navigate to:
**CT Combined Tools → AI Segmentation → AI Settings**

Use the file browser to configure the correct paths for each model if they differ from the defaults.

## Usage

### AI Settings Tool

Configure model paths, performance settings, and advanced parameters:

- **GPU Acceleration**: Enable CUDA/DirectML for faster inference
- **CPU Threads**: Adjust thread count for CPU inference
- **Confidence Threshold**: Minimum detection confidence (Grounding DINO)
- **IoU Threshold**: Non-Maximum Suppression threshold
- **Point Strategy**: How to convert bounding boxes to SAM prompts

### SAM2 Interactive Tool

**Workflow:**

1. Open a CT Image Stack dataset
2. Navigate to **AI Segmentation → SAM2 Interactive**
3. Click **Activate Interactive Mode**
4. Select **Point Mode** (Positive or Negative):
   - **Positive**: Click areas to include in segmentation
   - **Negative**: Click areas to exclude from segmentation
5. Click on the CT viewer to add points
6. Click **Run Segmentation** to generate mask
7. Select target material and click **Apply to Material**

**Features:**
- Real-time point-based segmentation
- Support for multiple positive/negative points
- Cached image embeddings for fast re-segmentation
- Auto-apply mode for instant material assignment

### Grounding DINO + SAM Pipeline

**Workflow:**

1. Open a CT Image Stack dataset
2. Navigate to **AI Segmentation → Grounding DINO + SAM**
3. Select segmentation model (SAM2 or MicroSAM)
4. Enter text prompt describing objects to detect, e.g.:
   ```
   rock . mineral . crystal .
   ```
5. Choose point placement strategy:
   - **CenterPoint**: Single point at bbox center (fastest)
   - **CornerPoints**: 4 points at corners
   - **BoxOutline**: Points along perimeter
   - **WeightedGrid**: 3×3 grid (recommended)
   - **BoxFill**: Dense 5×5 grid (most accurate)
6. Select target material for assignment
7. Choose slice to process
8. Click **Detect & Segment**
9. Review detected objects in results list
10. Click **Apply All to Material** to assign all masks

**Text Prompt Tips:**
- Separate object names with dots and spaces: `object1 . object2 . object3 .`
- Use specific geological terms: `quartz . feldspar . biotite .`
- Combine general and specific terms: `mineral . crystal . pore .`

## Performance Optimization

### GPU Acceleration

**CUDA (NVIDIA GPUs):**
- Requires CUDA Toolkit 11.x or 12.x
- Install `cudnn` libraries
- Enable in AI Settings

**DirectML (Windows, any GPU):**
- Automatic fallback on Windows
- Supports NVIDIA, AMD, and Intel GPUs

### Memory Management

- **Image caching**: SAM models cache encoded images for faster re-segmentation
- **Clear cache**: Use "Clear Cache" buttons when processing different slices
- **Batch processing**: For large datasets, process one slice at a time

### Speed Benchmarks (approximate)

| Model | Input Size | GPU (RTX 3080) | CPU (16 cores) |
|-------|-----------|----------------|----------------|
| SAM2 Encoder | 1024×1024 | 50ms | 800ms |
| SAM2 Decoder | 1 point | 30ms | 200ms |
| MicroSAM | 1024×1024 | 40ms | 600ms |
| Grounding DINO | 800×800 | 100ms | 1500ms |
| Full Pipeline | - | 200ms | 2500ms |

## Advanced Features

### Zero-Shot Segmentation (MicroSAM)

MicroSAM supports automatic segmentation without user prompts:

1. Set point label to `-1` for zero-shot mode
2. Model generates up to 5 candidate masks
3. Masks filtered by IoU threshold (configurable in settings)
4. Best quality masks returned

### Multi-Object Detection

Grounding DINO can detect multiple object classes in one pass:

```
Input: "pore . grain . cement ."
Output: Separate detections for each class
```

### Material Assignment Workflow

Recommended workflow for material segmentation:

1. **Create materials** using Material Manager
2. **Use Grounding DINO + SAM** for automatic detection
3. **Refine with SAM2 Interactive** for precise boundaries
4. **Apply to materials** for 3D analysis

## Troubleshooting

### Models Not Loading

**Error**: "Model files not found"
- Check model paths in AI Settings
- Verify files exist in ONNX directory
- Ensure files are actual ONNX models (not corrupted)

### CUDA Errors

**Error**: "CUDA not available"
- Install CUDA Toolkit
- Update NVIDIA drivers
- Restart application after installation

### Out of Memory

**Error**: OOM during inference
- Reduce image resolution
- Disable GPU and use CPU
- Close other applications
- Process smaller regions

### Poor Segmentation Quality

**Issue**: Inaccurate masks
- Add more points (SAM2 Interactive)
- Use different point strategy (Pipeline)
- Adjust confidence threshold
- Try MicroSAM for high-resolution images

## API Reference

### AISegmentationSettings

Singleton settings manager for all AI segmentation modules.

```csharp
var settings = AISegmentationSettings.Instance;
settings.UseGpu = true;
settings.ConfidenceThreshold = 0.3f;
settings.Save();
```

### Sam2Segmenter

```csharp
using var segmenter = new Sam2Segmenter();
var points = new List<(float x, float y)> { (100, 100), (200, 200) };
var labels = new List<float> { 1.0f, 1.0f }; // positive points
byte[,] mask = segmenter.Segment(image, points, labels);
```

### GroundingSamPipeline

```csharp
using var pipeline = new GroundingSamPipeline(
    GroundingSamPipeline.SegmenterType.SAM2
);
var results = pipeline.DetectAndSegment(
    image,
    "rock . mineral .",
    PointPlacementStrategy.WeightedGrid
);
```

## Credits

This implementation is based on the CTS (CT Segmenter) project by mattemangia.

Models and architecture:
- **SAM2**: Meta AI (Segment Anything)
- **MicroSAM**: Computational Cell Analytics Lab
- **Grounding DINO**: IDEA Research

## License

AI segmentation modules follow the same license as GeoscientistToolkit.
ONNX models retain their original licenses from respective projects.
