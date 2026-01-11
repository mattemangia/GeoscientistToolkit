# CT Imaging and Segmentation

Comprehensive guide for CT imaging, segmentation, and AI-powered analysis in Geoscientist's Toolkit.

---

## Overview

The CT Imaging module provides tools for:
- Loading and visualizing 3D CT volumes
- Manual segmentation (brush, lasso, magic wand)
- AI-powered segmentation (SAM2, MicroSAM, Grounding DINO)
- Material definition and assignment
- Mesh extraction and export

---

## Getting Started

### Loading CT Data

1. Go to **File → Import**
2. Select your CT data format:
   - **TIFF stack**: Folder of numbered TIFF images
   - **DICOM**: Medical CT format
   - **.ctstack**: Native format
3. The CT volume appears in the Datasets panel

### CT Viewer Features

The CT viewer provides:
- **3D Volume Rendering**: Raycasting visualization
- **Slice Views**: XY, XZ, YZ plane navigation
- **Multi-planar Reconstruction**: Arbitrary slice angles
- **Transfer Function**: Customizable opacity/color mapping

### Navigation Controls

| Control | Action |
|---------|--------|
| Mouse Wheel | Navigate slices |
| Middle Mouse | Pan view |
| Right Mouse | Rotate (3D view) |
| Scroll | Zoom in/out |

---

## Manual Segmentation Tools

### Brush Tool (B)

Paint regions directly on slices.

**Usage:**
1. Press `B` or select Brush from toolbar
2. Adjust brush size with slider
3. Click and drag to paint
4. Shift+click to erase

### Lasso Tool (L)

Select irregular areas with freehand drawing.

**Usage:**
1. Press `L` or select Lasso from toolbar
2. Click and drag to draw selection
3. Release to close selection
4. Apply to current material

### Magic Wand (W)

Threshold-based region growing selection.

**Usage:**
1. Press `W` or select Magic Wand from toolbar
2. Adjust tolerance slider
3. Click on a region to select connected pixels
4. Hold Shift to add to selection

### Fill Tool

Flood fill enclosed regions.

**Usage:**
1. Select Fill from toolbar
2. Click inside enclosed region
3. Region fills with current material

---

## AI-Powered Segmentation

The toolkit includes state-of-the-art AI models for automatic segmentation.

### Available Models

| Model | Type | Best For |
|-------|------|----------|
| **SAM2** | Interactive point-based | Precise segmentation with user guidance |
| **MicroSAM** | Microscopy-optimized | High-resolution microscopy images |
| **Grounding DINO** | Text-prompted | Automatic detection from descriptions |

### Installation

1. **Download ONNX Models**

   Download the pre-trained models and place in the `ONNX/` directory:

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

2. **Configure Paths**

   Go to **CT Combined Tools → AI Segmentation → AI Settings** and verify model paths.

### SAM2 Interactive Tool

**Workflow:**

1. Open a CT Image Stack dataset
2. Navigate to **AI Segmentation → SAM2 Interactive**
3. Click **Activate Interactive Mode**
4. Select point mode:
   - **Positive**: Click areas to include
   - **Negative**: Click areas to exclude
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
4. Enter text prompt describing objects:
   ```
   rock . mineral . crystal .
   ```
5. Choose point placement strategy:
   - **CenterPoint**: Single point at bbox center (fastest)
   - **CornerPoints**: 4 points at corners
   - **BoxOutline**: Points along perimeter
   - **WeightedGrid**: 3×3 grid (recommended)
   - **BoxFill**: Dense 5×5 grid (most accurate)
6. Select target material
7. Choose slice to process
8. Click **Detect & Segment**
9. Review detected objects
10. Click **Apply All to Material**

**Text Prompt Tips:**
- Separate objects with dots and spaces: `object1 . object2 . object3 .`
- Use specific geological terms: `quartz . feldspar . biotite .`
- Combine general and specific: `mineral . crystal . pore .`

### AI Settings

Configure AI performance:

| Setting | Description |
|---------|-------------|
| GPU Acceleration | Enable CUDA/DirectML for faster inference |
| CPU Threads | Thread count for CPU inference |
| Confidence Threshold | Minimum detection confidence |
| IoU Threshold | Non-Maximum Suppression threshold |
| Point Strategy | How to convert boxes to SAM prompts |

---

## Materials

### Creating Materials

1. Open **Tools → Segmentation → Material Manager**
2. Click **Add Material**
3. Configure properties:
   - **Name**: Descriptive identifier
   - **Color**: RGBA for visualization
   - **Density**: kg/m³
   - **Porosity**: 0-1 fraction
   - **Thermal/Mechanical properties**: Optional

### Assigning Materials

1. Create a selection using any segmentation tool
2. In Material Manager, select target material
3. Click **Apply Selection to Material**

### Material Statistics

View statistics for each material:
- Volume fraction
- Voxel count
- Surface area
- Spatial distribution

---

## Mesh Extraction

Export segmented materials as 3D meshes.

### Export Workflow

1. Select the CT dataset with segmented materials
2. Go to **Tools → Export → Mesh Extraction**
3. Choose algorithm:
   - **Marching Cubes**: Fast, good for smooth surfaces
   - **Surface Nets**: Smoother results, slower
4. Select materials to export
5. Configure mesh settings (resolution, smoothing)
6. Choose output format (OBJ, STL)
7. Click **Export**

---

## GeoScript CT Commands

### CT_SEGMENT

Perform 3D segmentation.

```geoscript
CT_SEGMENT method=threshold min=100 max=200 material=1
CT_SEGMENT method=otsu material=1
```

### CT_FILTER3D

Apply 3D filters.

```geoscript
CT_FILTER3D type=gaussian size=5
CT_FILTER3D type=median size=3
```

### CT_ADD_MATERIAL

Define material properties.

```geoscript
CT_ADD_MATERIAL name='Pore' color=0,0,255
```

### CT_REMOVE_MATERIAL

Remove material definitions.

```geoscript
CT_REMOVE_MATERIAL id=1
```

### CT_ANALYZE_POROSITY

Calculate porosity.

```geoscript
CT_ANALYZE_POROSITY void_material=1
```

### CT_CROP

Extract sub-volume.

```geoscript
CT_CROP x=0 y=0 z=0 width=100 height=100 depth=100
```

### CT_EXTRACT_SLICE

Extract 2D slice.

```geoscript
CT_EXTRACT_SLICE index=50 axis=z
```

### CT_LABEL_ANALYSIS

Analyze labeled volumes.

```geoscript
CT_LABEL_ANALYSIS
```

---

## Performance Optimization

### GPU Acceleration

**CUDA (NVIDIA GPUs):**
- Requires CUDA Toolkit 11.x or 12.x
- Install cuDNN libraries
- Enable in AI Settings

**DirectML (Windows):**
- Automatic fallback on Windows
- Supports NVIDIA, AMD, and Intel GPUs

### Memory Management

- **Image caching**: SAM models cache encoded images
- **Clear cache**: Use "Clear Cache" when switching slices
- **Batch processing**: Process one slice at a time for large datasets

### Speed Benchmarks

| Operation | GPU (RTX 3080) | CPU (16 cores) |
|-----------|----------------|----------------|
| SAM2 Encoder | 50ms | 800ms |
| SAM2 Decoder | 30ms | 200ms |
| MicroSAM | 40ms | 600ms |
| Grounding DINO | 100ms | 1500ms |
| Full Pipeline | 200ms | 2500ms |

---

## Troubleshooting

### Models Not Loading

**Error:** "Model files not found"

**Solutions:**
- Check model paths in AI Settings
- Verify files exist in ONNX directory
- Ensure files are actual ONNX models (not corrupted)

### CUDA Errors

**Error:** "CUDA not available"

**Solutions:**
- Install CUDA Toolkit
- Update NVIDIA drivers
- Restart application after installation

### Out of Memory

**Error:** OOM during inference

**Solutions:**
- Reduce image resolution
- Disable GPU and use CPU
- Close other applications
- Process smaller regions

### Poor Segmentation Quality

**Solutions:**
- Add more points (SAM2 Interactive)
- Use different point strategy
- Adjust confidence threshold
- Try MicroSAM for high-resolution images

---

## Related Pages

- [GeoScript Image Operations](GeoScript-Image-Operations.md) - Image processing reference
- [GeoScript Manual](GeoScript-Manual.md) - Complete scripting guide
- [User Guide](User-Guide.md) - Application documentation
- [Pore Network Modeling](Pore-Network-Modeling.md) - PNM from CT data
- [Home](Home.md) - Wiki home page
