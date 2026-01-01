# Real-time Photogrammetry Pipeline

Complete real-time photogrammetry system for GeoscientistToolkit with automatic ONNX model management and advanced export.

## Key Features

- **Depth estimation**: Support for ONNX models (MiDaS, DPT, ZoeDepth)
- **Keypoint detection**: SuperPoint with deep learning descriptors
- **Feature matching**: LightGlue for fast and accurate matching (with brute-force fallback)
- **Depth-aware RANSAC**: Pose estimation with depth constraints
- **2.5D Keyframes**: Keyframe system with PnP and bundle adjustment
- **Video acquisition**: Webcam and video file support
- **Georeferencing**: GCP system with altitude refinement
- **GPU acceleration**: CUDA support for ONNX inference

## Requirements

### NuGet Packages (already included in .csproj)
- Microsoft.ML.OnnxRuntime (>=1.19.2)
- Microsoft.ML.OnnxRuntime.Gpu (>=1.19.2) - for GPU
- OpenCvSharp4 (>=4.10.0)
- MathNet.Numerics (already present)

### ONNX Models

ONNX models must be downloaded separately:

#### 1. Depth Estimation

**MiDaS Small** (recommended for real-time):
- Download: https://github.com/isl-org/MiDaS/releases
- File: `midas_v21_small_256.onnx` or similar
- Input size: 384x384

**ZoeDepth** (for metric-aware depth):
- Download: https://github.com/isl-org/ZoeDepth
- Convert to ONNX using Python script
- Input size: 512x384

#### 2. Keypoint Detection

**SuperPoint**:
- Download: https://github.com/magicleap/SuperPointPretrainedNetwork
- Convert to ONNX or use already converted ports:
  - https://github.com/PINTO0309/PINTO_model_zoo (SuperPoint ONNX)
- Input: Grayscale 1CHW

#### 3. Feature Matching

**LightGlue** (optional):
- Download: https://github.com/cvg/LightGlue
- Convert to ONNX
- Input: Descriptor pairs

**Note**: If LightGlue is not available, the system automatically uses brute-force matching.

## Quick Start (New!)

### Method 1: Automatic Download (Recommended)

1. **Open Settings**:
   ```
   Menu: Edit → Settings → Photogrammetry
   ```

2. **Automatic Model Download**:
   - Click "Download MiDaS Small (Depth)" - automatically downloads depth model (~20 MB)
   - Click "Download SuperPoint" - automatically downloads keypoint detector (~5 MB)
   - (Optional) "Download LightGlue" if available

3. **Configure Pipeline**:
   - Enable GPU if available (requires CUDA)
   - Set target resolution (640x480 recommended)
   - Configure camera intrinsics (or use auto-estimation)
   - Click "Apply" to save

4. **Start Photogrammetry**:
   ```
   Menu: Tools → Real-time Photogrammetry
   Tab Configuration → Initialize Pipeline
   Tab Capture → Start Capture
   ```

### Method 2: Manual Selection

1. **Download models** manually (see `ModelDownloadGuide.md`)

2. **Configure paths in Settings**:
   ```
   Edit → Settings → Photogrammetry
   ```
   - Use the "Browse..." button to select each ONNX model
   - Set the Models Directory folder

3. **Save and initialize** the pipeline

## Advanced Configuration

### Settings → Photogrammetry

Photogrammetry settings are fully integrated into the configuration system:

#### **Model Paths**
- `Depth Model`: Path to ONNX model for depth estimation
- `SuperPoint Model`: Path to model for keypoint detection
- `LightGlue Model`: (Optional) Path to matcher
- `Models Directory`: Default folder for models

#### **Pipeline Settings**
- `Use GPU Acceleration`: Enable CUDA if available
- `Depth Model Type`: MiDaS Small / DPT Small / ZoeDepth
- `Keyframe Interval`: Create keyframe every N frames (1-30)
- `Target Width/Height`: Processing resolution (320-1920 / 240-1080)

#### **Camera Intrinsics**
- `Focal Length X/Y`: Focal length in pixels
- `Principal Point X/Y`: Optical center
- "Auto-estimate" button: Calculate from resolution

#### **Export Settings**
- `Default Export Format`: PLY / XYZ / OBJ
- `Export Textured Mesh`: Include texture (when available)
- `Export Camera Path`: Export camera trajectory

### Configuring the Pipeline from RT Photogrammetry Window

From the "Real-time Photogrammetry" window:

1. **Tab Configuration**:
   - Enter paths to ONNX models
   - Select depth model type
   - Enable GPU if available
   - Set target resolution (640x480 recommended for real-time)
   - Configure camera intrinsics (or use auto-estimation)
   - Click "Initialize Pipeline"

2. **Tab Capture**:
   - Select video source:
     - **Webcam**: Choose from list of detected cameras
     - **Video File**: Use "Browse..." to select file (.mp4, .avi, .mov, etc.)
   - Click "Start Capture"
   - Live display of frame and depth map
   - System will process frames in real-time

3. **Tab Keyframes**:
   - Table with all created keyframes
   - Information: Frame ID, Timestamp, Number of 3D points
   - "Perform Bundle Adjustment" button for refinement

4. **Tab Georeferencing**:
   - **Add GCP**:
     - Point name
     - Local position (x,y,z) in reconstruction system
     - World position (E,N,Alt) in real coordinates (UTM, lat/lon, etc.)
     - Accuracy in meters
   - **GCP Management**:
     - Table with all GCPs
     - Checkbox to enable/disable
     - "Remove" button to delete
   - **Compute Transform**:
     - Requires minimum 3 active GCPs
     - "Refine with Altitude" checkbox for vertical accuracy
     - Shows results: GCPs used, RMS error, scale, translation, rotation

5. **Tab Statistics**:
   - Total processed frames
   - Average processing time
   - Current FPS
   - Processing time graph
   - Total keyframes and GCPs

## Export

### Menu File → Export

All exports support automatic georeferencing if ≥3 GCPs are available:

#### **Export Point Cloud**
Available formats:
- **PLY (Polygon File Format)**:
  - ASCII format
  - Includes XYZ coordinates and RGB colors
  - Compatible with MeshLab, CloudCompare, Blender

- **XYZ (Simple Text)**:
  - Simple text format
  - One line per point: `X Y Z R G B`
  - Easy import in GIS software

- **OBJ (Wavefront)**:
  - Vertices only (point cloud)
  - Separate .mtl file for colors
  - Compatible with all 3D software

**How to export**:
1. Menu File → Export Point Cloud...
2. Choose file name and format (.ply, .xyz, .obj)
3. System automatically exports all 3D points from keyframes
4. If GCPs available, applies georeferencing

#### **Export Mesh**
- Currently exports as point cloud in OBJ format
- Note: TSDF fusion for dense mesh not yet implemented
- Future roadmap: marching cubes, texturing

#### **Export Camera Path**
- Exports complete camera trajectory
- Format: `FrameID X Y Z QuatX QuatY QuatZ QuatW Timestamp`
- Useful for:
  - Path visualization
  - Import in animation software
  - Camera movement analysis

## Recommended Workflow

### Real-time Pipeline (monocular)

1. **Pre-processing**:
   - Undistort (if distortion parameters available)
   - Resize to target resolution
   - Normalization

2. **Depth Estimation**:
   - Depth model inference (MiDaS/ZoeDepth)
   - Output: relative depth map per frame

3. **Keypoint & Matching**:
   - **Option A**: SuperPoint + LightGlue (sparse, fast)
   - **Option B**: ORB features (fallback, slower but no models needed)

4. **Depth-Aware RANSAC**:
   - Essential matrix estimation with RANSAC
   - Scale alignment using depth map
   - Outlier filtering with depth constraints

5. **2.5D Keyframes**:
   - Keyframe creation every N frames
   - Save keypoints with depth (3D sparse)
   - PnP+RANSAC for tracking against keyframes
   - Bundle adjustment in background

6. **Georeferencing**:
   - GCP collection (minimum 3 points)
   - Similarity transformation computation (7 parameters)
   - Optional altitude refinement

## Performance

### Typical processing times (CPU i7, resolution 640x480)

- MiDaS Small: ~100-200 ms/frame
- SuperPoint: ~50-100 ms/frame
- LightGlue: ~30-50 ms/frame
- RANSAC + Pose: ~10-20 ms/frame
- **Total**: ~200-400 ms/frame (~2-5 FPS)

### With GPU (NVIDIA RTX 3060)

- MiDaS Small: ~10-20 ms/frame
- SuperPoint: ~5-10 ms/frame
- LightGlue: ~5-10 ms/frame
- **Total**: ~30-50 ms/frame (~20-30 FPS)

## Troubleshooting

### "Failed to load model"
- Verify that the ONNX file path is correct
- Verify that the model is in correct ONNX format
- Check logs for details

### "CUDA not available"
- Install CUDA Toolkit (11.x or 12.x)
- Install updated NVIDIA drivers
- System will automatically use CPU if CUDA is not available

### "Not enough matches"
- Lower the confidence threshold for keypoints
- Verify that the scene has sufficient texture
- Try different camera exposure settings

### Low frame rate
- Reduce target resolution
- Disable heavy models (use MiDaS Small instead of ZoeDepth)
- Enable GPU acceleration
- Increase keyframe interval

## Limitations

1. **Ambiguous scale**: Monocular reconstruction has arbitrary scale. Use GCPs for absolute scale.
2. **Texture needed**: Scenes without texture (white walls) produce few keypoints.
3. **Slow movement**: For best results, move camera slowly.
4. **Relative depth**: MiDaS produces relative depth, not metric. ZoeDepth is more accurate but slower.

## Future Developments

- [ ] Dense reconstruction with TSDF fusion
- [ ] Real-time meshing with marching cubes
- [ ] Automatic texturing
- [ ] Loop closure detection
- [ ] Export PLY/OBJ point cloud
- [ ] Multi-camera support
- [ ] IMU fusion for more robust pose

## References

- MiDaS: https://github.com/isl-org/MiDaS
- ZoeDepth: https://github.com/isl-org/ZoeDepth
- SuperPoint: https://github.com/magicleap/SuperPointPretrainedNetwork
- LightGlue: https://github.com/cvg/LightGlue
- OpenCV: https://opencv.org/
- ONNX Runtime: https://onnxruntime.ai/
