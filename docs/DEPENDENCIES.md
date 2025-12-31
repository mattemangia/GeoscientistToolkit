# Dependencies & Optional Components

This document centralizes **mandatory** and **optional** requirements for the Geoscientist's Toolkit ecosystem, including GPU acceleration, GIS, and AI model support. It also provides OS-specific installation hints.

## Required (Build/Run)

| Component | Minimum Version | Notes |
| --- | --- | --- |
| .NET SDK | 8.0 | Required to build all projects (`GeoscientistToolkit.sln`). |
| .NET Runtime | 8.0 | Required to run the desktop app and NodeEndpoint. |
| GPU Drivers | OpenGL 3.3+ or Vulkan | Rendering backend for 3D visualization. |
| RAM | 8 GB (16 GB+ recommended) | Large datasets and GPU workflows benefit from more memory. |
| Disk | 2 GB (10 GB+ recommended) | Datasets and model caches can be large. |

### OS Install Hints

**Windows**
- Install the .NET 8 SDK from <https://dotnet.microsoft.com/> (or `winget install Microsoft.DotNet.SDK.8`).

**macOS**
- `brew install --cask dotnet-sdk` (installs the .NET 8 SDK).

**Linux (Ubuntu/Debian)**
- `sudo apt-get update && sudo apt-get install dotnet-sdk-8.0`

## Optional (Feature-Dependent)

| Component | Minimum Version | Used For |
| --- | --- | --- |
| OpenCL Runtime/Drivers | 1.2+ | GPU-accelerated simulation kernels and CT processing. |
| GDAL | 3.x | GIS workflows, raster/shape conversions. |
| ONNX Runtime | 1.16+ | AI inference (SAM2, MicroSAM, GroundingDINO). |
| CUDA (optional) | 11.8+ | GPU acceleration for compatible ONNX builds. |
| Vulkan SDK (optional) | 1.2+ | Additional rendering backend support on Linux. |

### OpenCL
- **Windows**: Install GPU vendor drivers (NVIDIA/AMD/Intel) with OpenCL support.
- **Linux**: Install vendor OpenCL packages (e.g., `nvidia-opencl-icd`, `intel-opencl-icd`, `ocl-icd-libopencl1`).
- **macOS**: Apple provides OpenCL 1.2+ in system frameworks (deprecated but available on many macOS versions).

### GDAL (GIS workflows)
- **Windows**: `choco install gdal` or use OSGeo4W.
- **macOS**: `brew install gdal`
- **Linux**: `sudo apt-get install gdal-bin libgdal-dev`

### ONNX Runtime & AI Models
- **Runtime**: If you enable AI segmentation features, install ONNX Runtime (or use a bundled runtime in distribution builds).
- **Models**: The repository stores sample models under `ONNX/`. For production, keep models in a shared, versioned location and reference them through configuration or the model manager UI.

### Vulkan (optional)
- **Linux**: `sudo apt-get install libvulkan1 vulkan-tools`
- **Windows**: Install latest GPU drivers (Vulkan is typically included).

## Validation Checklist

1. `dotnet --version` returns `8.x`.
2. `dotnet build GeoscientistToolkit.sln` completes without errors.
3. `OpenCL` and `Vulkan` (if needed) are visible via vendor tools (`clinfo`, `vulkaninfo`).
4. AI models load correctly from the `ONNX/` directory when enabled.
5. Optional: run diagnostics with `dotnet run -- --ai-diagnostic` or `dotnet run -- --gui-diagnostic`.
