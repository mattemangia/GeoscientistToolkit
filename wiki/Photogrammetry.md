# Photogrammetry

Guide for 3D reconstruction from images using the photogrammetry pipeline in Geoscientist's Toolkit.

---

## Overview

The Photogrammetry module provides:
- Real-time depth estimation
- Keypoint detection and matching
- 3D reconstruction from images/video
- Georeferencing support
- GPU acceleration

---

## Key Features

| Feature | Description |
|---------|-------------|
| Depth Estimation | MiDaS-based monocular depth |
| Keypoint Detection | SuperPoint feature detection |
| Feature Matching | LightGlue matcher |
| Depth-Aware RANSAC | Robust pose estimation |
| 2.5D Keyframes | Depth-augmented keyframes |
| Video Acquisition | Live camera support |
| Georeferencing | GPS/coordinate integration |
| GPU Acceleration | ONNX Runtime acceleration |

---

## ONNX Model Requirements

### Required Models

Place these models in the `ONNX/` directory:

| Model | File | Purpose |
|-------|------|---------|
| MiDaS | `midas_v21.onnx` | Depth estimation |
| SuperPoint | `superpoint.onnx` | Keypoint detection |
| LightGlue | `lightglue.onnx` | Feature matching |

### Model Download

**Automatic Download:**
1. Go to **Tools → Photogrammetry → Settings**
2. Click **Download Models**
3. Models download automatically

**Manual Selection:**
1. Download models from official repositories
2. Place in `ONNX/` directory
3. Configure paths in settings

---

## Quick Start

### From Images

1. Go to **File → Import → Images**
2. Select multiple images of the scene
3. Go to **Tools → Analysis → Photogrammetry**
4. Configure pipeline settings
5. Click **Process**
6. View 3D point cloud result

### From Video

1. Go to **File → Import → Video**
2. Select video file
3. Go to **Tools → Analysis → Photogrammetry**
4. Configure frame extraction settings
5. Click **Process Video**
6. View 3D reconstruction

---

## Pipeline Configuration

### Model Paths

Configure paths to ONNX models:
- Depth model (MiDaS)
- Keypoint model (SuperPoint)
- Matcher model (LightGlue)

### Pipeline Settings

| Parameter | Description | Default |
|-----------|-------------|---------|
| Max Features | Maximum keypoints per image | 2048 |
| Match Threshold | Feature matching threshold | 0.7 |
| Depth Scale | Depth estimation scale | 1.0 |
| RANSAC Iterations | Pose estimation iterations | 1000 |

### Camera Intrinsics

For accurate reconstruction:
- Focal length (pixels)
- Principal point (cx, cy)
- Distortion coefficients

If unknown, calibration can be estimated from images.

---

## Export Options

### Point Cloud

| Format | Extension | Description |
|--------|-----------|-------------|
| PLY | .ply | Stanford format |
| XYZ | .xyz | Simple text format |
| OBJ | .obj | Wavefront with points |

### Mesh

Generated from point cloud:
- Poisson reconstruction
- Ball pivoting
- Marching cubes

### Camera Path

Export camera trajectory:
- JSON format
- COLMAP format
- OpenCV format

---

## Recommended Workflow

### For Best Results

1. **Image Acquisition**
   - Overlap: 60-80% between images
   - Coverage: Multiple angles
   - Lighting: Consistent illumination
   - Focus: Sharp, in-focus images

2. **Processing**
   - Start with default settings
   - Adjust based on results
   - Enable GPU for speed

3. **Post-Processing**
   - Clean outliers
   - Scale to real dimensions
   - Apply georeferencing

---

## Performance Benchmarks

### Processing Speed

| Hardware | Images | Time |
|----------|--------|------|
| CPU (16 cores) | 50 | 5 min |
| GPU (RTX 3080) | 50 | 1 min |
| CPU (16 cores) | 200 | 30 min |
| GPU (RTX 3080) | 200 | 5 min |

### Quality Metrics

| Metric | Description |
|--------|-------------|
| Reprojection Error | < 1 pixel ideal |
| Point Density | Points per m² |
| Coverage | % of scene covered |

---

## Troubleshooting

### Poor Reconstruction

**Solutions:**
- Increase image overlap
- Use more images
- Improve lighting consistency
- Check for motion blur

### Failed Matching

**Solutions:**
- Lower match threshold
- Use more features
- Check image quality
- Ensure sufficient texture

### Out of Memory

**Solutions:**
- Reduce image resolution
- Process in batches
- Disable GPU (use CPU)
- Limit max features

### Models Not Loading

**Solutions:**
- Verify model file paths
- Check ONNX file integrity
- Update ONNX Runtime
- Try different execution provider

---

## Limitations

### Current Limitations

- Textureless surfaces difficult
- Reflective/transparent objects
- Moving objects in scene
- Large scale scenes require tiling

### Future Developments

Planned enhancements:
- Dense reconstruction
- Textured mesh output
- Loop closure
- Real-time SLAM
- Multi-sensor fusion

---

## References

### Depth Estimation
- Ranftl et al. (2020): Towards Robust Monocular Depth Estimation

### Feature Detection
- DeTone et al. (2018): SuperPoint

### Feature Matching
- Lindenberger et al. (2023): LightGlue

---

## Related Pages

- [CT Imaging and Segmentation](CT-Imaging-and-Segmentation) - 3D imaging
- [User Guide](User-Guide) - Application documentation
- [Home](Home) - Wiki home page
