# Geoscientist's Toolkit

<div align="center">
  <img src="image.png" alt="Geoscientist's Toolkit Logo" width="400"/>

  **A comprehensive desktop application for geoscientific data analysis, visualization, and simulation**

  **Current GTK Version: 1.0.0**

  [![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=.net)](https://dotnet.microsoft.com/)
  [![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)](#installation)
  [![License](https://img.shields.io/badge/license-MIT-green)](#license)
</div>

---

## Overview

Geoscientist's Toolkit is an advanced, cross-platform desktop application built with C# and .NET 8.0 that provides an integrated environment for working with diverse geophysical and geochemical datasets. From pore-scale (micrometers) to basin-scale (kilometers) analysis, it combines real-time 3D visualization, GPU-accelerated simulations, and a domain-specific scripting language (GeoScript) to enable sophisticated workflows across multiple geoscience disciplines.

## Key Features

### 🌍 Multi-Scale Analysis
- **Pore-scale**: Micro-CT imaging, pore network modeling, NMR simulation
- **Core-scale**: Rock physics, acoustic properties, thermal conductivity
- **Well-scale**: Borehole/well log analysis, geothermal simulation
- **Reservoir-scale**: Seismic analysis, 3D geological modeling
- **Basin-scale**: Regional mapping, GIS integration

### 📊 Comprehensive Data Support

| Data Type | Formats | Key Capabilities |
|-----------|---------|------------------|
| **CT Scans** | DICOM, TIFF, .ctstack | AI-powered segmentation (SAM2, MicroSAM), 3D reconstruction, material analysis |
| **Seismic Data** | SEG-Y | Trace analysis, wiggle/variable area displays, well-to-seismic correlation |
| **Well Logs** | LAS, .bhb | Lithology editing, synthetic seismic generation, geothermal simulation |
| **3D Meshes** | OBJ, STL | Import/export, surface extraction from CT, visualization |
| **GIS Data** | Shapefile, GeoJSON | Subsurface mapping, geographic integration |
| **Images** | PNG, JPG, BMP, TIFF | Standard image processing and analysis |
| **Tabular** | CSV, Excel | Data plotting, statistical analysis |
| **Pore Networks** | Custom | Flow simulation, permeability calculation, tortuosity |

### 🔬 Advanced Analysis Modules

#### CT Imaging & Segmentation
- 🖼️ Interactive 3D/2D/slice viewers with multi-planar reconstruction
- 🖌️ Manual segmentation tools (brush, lasso, magic wand)
- 🤖 AI-powered segmentation using ONNX models:
  - **SAM2** (Segment Anything Model v2)
  - **MicroSAM** (optimized for microscopy)
  - **Grounding DINO** (text-prompted segmentation)
- 📐 Material definition and interpolation between slices
- 🔄 Export to 3D meshes via Marching Cubes/Surface Nets

#### Seismic Analysis
- 📡 SEG-Y file loading and comprehensive visualization
- 📊 Multiple display modes: wiggle trace, variable area, color maps
- 🔗 **Borehole-Seismic Integration**:
  - Generate synthetic seismograms from well logs
  - Create pseudo-boreholes from seismic traces
  - Well-to-seismic tie with correlation analysis
  - Interactive correlation visualization

#### Physical Property Simulations
- 🌡️ **Thermal Conductivity** - Finite element heat transfer analysis
- 🔊 **Acoustic Properties** - P-wave and S-wave velocity simulation
- 🧲 **NMR Simulation** - T2 relaxation time calculation
- 🪨 **Geomechanical Analysis** - Stress/strain, Mohr circles, failure prediction
- 💧 **Pore Network Modeling** - Permeability, tortuosity, flow simulation
- 🧬 **Dual Pore Network Modeling** - Macro–micro pore coupling from CT + SEM with parallel/series/mass-transfer modes, micro-network extraction, and coupled permeability/reactive transport simulations
- ♨️ **Geothermal Simulation** - Heat transfer and fluid flow in boreholes

#### Thermodynamics & Geochemistry
- 🧪 **Extended Compound Library** - 60+ additional silicates, carbonates, sulfates, chlorides, oxides, aqueous ions, and gases from Holland & Powell, PHREEQC, Robie & Hemingway, and SUPCRT92 datasets
- 🧮 **GeoScript Thermo Commands** - `CALCULATE_PHASES` for phase-separated outputs, `CALCULATE_CARBONATE_ALKALINITY` for pH/alkalinity-driven carbonate speciation, and an enhanced `REACT` command with phase grouping and mineral identification
- 🔁 **Equilibrium Workflow** - Gibbs energy minimization with element conservation, activity coefficients, and temperature-dependent constants for robust equilibrium calculations

#### 3D Visualization
- 🎮 Hardware-accelerated rendering via Veldrid (OpenGL/Vulkan/DirectX)
- 🔄 Real-time manipulation and interaction
- 👀 Multiple viewport support with docking
- 🪟 Pop-out window capability
- 📸 High-resolution screenshot capture

### 🚀 Performance Features
- GPU-accelerated simulations using OpenCL
- Multi-threaded processing for large datasets
- Efficient memory management for CT stacks
- Real-time rendering at 60+ FPS

### 🛰️ Earthquake & GIS Integration
- 3D earthquake simulation with corrected stress-tensor propagation
- Generation of subsurface GIS volumes (PGA/PGV/PGD, stress, fracture density) from simulations
- Depth-slice layers, per-location time series extraction, and 3D geology construction from 2D map selections with layered models

### 🧭 Structural Geology & Restoration
- Interactive 2D cross-section restoration with real-time overlays, opacity controls, and fault rendering
- Slider-driven flexural slip unfolding/deformation with percentage controls to compare present-day vs restored geometry
- Overlay indicators and visibility toggles to declutter original formations during restoration previews

### ⚙️ PHYSICOCHEM Multiphysics Reactors
- Dataset builder for complex reactors (box/sphere/cylinder/cone/custom) with 2D-to-3D extrusion and revolutions
- Comprehensive boundary conditions, force fields (gravity, vortex, centrifugal), nucleation sites, and reactive transport coupling
- Parameter sweeps with sensitivity analysis and optional geothermal coupling for TOUGH-like scenarios

### 🧪 GeoScript Dataset Pipelines
- Pipeline syntax (`|>`) for chaining dataset/image operations
- Built-in filters (Gaussian, median, Sobel/Canny, bilateral, NLM, unsharp) and thresholding
- Brightness/contrast, grayscale/inversion, and mask operations directly in scripts

---

## Installation

### Prerequisites
- **Runtime**: .NET 8.0 SDK or later
- **OS**: Windows 10/11, Linux (x64), or macOS (ARM64/x86_64)
- **GPU**: OpenGL 3.3+ or Vulkan support (for 3D visualization)
- **RAM**: 8 GB minimum, 16 GB+ recommended
- **Disk**: 2 GB minimum, 10 GB+ recommended for large datasets

### Quick Start

#### Installer multi-piattaforma
Gli eseguibili del wizard TUI `InstallerWizard` permettono di installare e aggiornare automaticamente il toolkit su Windows, Linux e macOS (Intel/Apple Silicon). Il tool `InstallerPackager` comprime i build `dotnet publish`, include i modelli ONNX e il server Node endpoint e aggiorna il manifest per l'auto-update. Tutte le istruzioni pratiche sono descritte in [docs/installers.md](docs/installers.md).

#### Clone and Build
```bash
git clone https://github.com/mattemangia/GeoscientistToolkit.git
cd GeoscientistToolkit
dotnet build
dotnet run
```

#### Pre-built Releases
Download platform-specific releases from the [Releases](../../releases) page.

No installation required - just extract and run!

---

## Quick Start Guide

### Creating Your First Project

1. **Launch the application**
   ```bash
   ./GeoscientistToolkit
   ```

2. **Create a new project**
   - Go to `File → New Project`
   - Choose a location and name for your `.gtp` project file

3. **Import data**
   - `File → Import` and select your data format:
     - CT: DICOM, TIFF stack, or .ctstack
     - Seismic: SEG-Y files
     - Borehole: LAS or .bhb files
     - Mesh: OBJ or STL files
     - Images: PNG, JPG, BMP, TIFF

### Sample Workflows

#### 1. CT Segmentation Workflow

```
Load CT dataset
    ↓
Define materials (Tools → Segmentation → Material Manager)
    ↓
Segment using:
  • Brush Tool (B) - Paint regions
  • Lasso Tool (L) - Select irregular areas
  • Magic Wand (W) - Threshold-based selection
  • AI Models - Automatic segmentation
    ↓
Assign selections to materials
    ↓
Export to 3D mesh (Tools → Export → Mesh Extraction)
```

#### 2. Seismic-Borehole Integration

```
Load seismic dataset (SEG-Y)
    ↓
Load borehole with Vp and Density data
    ↓
Generate synthetic seismic (Borehole Tools → Seismic)
  • Set dominant frequency (20-40 Hz typical)
    ↓
Perform well tie (Seismic Tools → Borehole Integration)
  • Auto-correlate with seismic traces
  • View correlation heatmap
    ↓
Interpret results
```

#### 3. Geothermal Simulation

```
Load borehole dataset with temperature/lithology
    ↓
Configure simulation (Tools → Analysis → Geothermal)
  • Set mesh resolution
  • Define injection/production temps
  • Choose flow configuration
    ↓
Run simulation
    ↓
Visualize temperature distribution and flow paths
```

---

## Architecture

### Project Structure

```
GeoscientistToolkit/
├── Analysis/                  # Simulation and analysis engines
│   ├── Geothermal/           # Heat transfer simulations
│   ├── AcousticSimulation/   # Wave propagation
│   ├── NMR/                  # NMR relaxation modeling
│   ├── MaterialManager/      # Material properties database
│   └── ThermalConductivity/  # FEM heat solver
├── Business/                  # Project management, serialization
├── Data/                      # Dataset classes
│   ├── Borehole/             # Well log data structures
│   ├── CtImageStack/         # CT scan management
│   ├── Seismic/              # Seismic data handling
│   ├── Mesh3D/               # 3D mesh operations
│   └── GIS/                  # Geographic information
├── Tools/                     # Cross-dataset integration
│   ├── BoreholeSeismic/      # Well-seismic correlation
│   └── CTMesh/               # CT to mesh conversion
├── UI/                        # User interface components
│   ├── Viewers/              # 3D/2D visualization widgets
│   ├── Tools/                # Dataset-specific tool panels
│   ├── Properties/           # Property editors
│   ├── MainWindow.cs         # Main application window
│   └── SplashScreen.cs       # Startup splash screen
├── NodeEndpoint/              # Network service for distributed computing
│   ├── Program.cs            # HTTP endpoint (port 8500)
│   ├── TuiManager.cs         # Terminal UI for monitoring
│   └── Services/             # Network discovery
├── Util/                      # Utilities (logging, managers)
├── Settings/                  # Application configuration
├── Shaders/                   # GLSL/HLSL shader programs
└── docs/                      # Documentation
```

### Technology Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **UI Framework** | ImGui.NET | Immediate-mode GUI for responsive interface |
| **Graphics** | Veldrid | Cross-platform graphics abstraction |
| **3D Rendering** | OpenGL 3.3 / Vulkan / DirectX 11 | Hardware-accelerated visualization |
| **AI/ML** | ONNX Runtime | Model inference for segmentation |
| **Image Processing** | StbImageSharp, SkiaSharp | Image loading and manipulation |
| **Scientific Computing** | MathNet.Numerics | Linear algebra, statistics |
| **File Formats** | Custom parsers | SEG-Y, LAS, DICOM, OBJ, STL |
| **GIS** | GDAL, NetTopologySuite | Geospatial data handling |
| **GPU Compute** | Silk.NET.OpenCL | Parallel computation |
| **Networking** | ASP.NET Core | Distributed computing support |

---

## Keyboard Shortcuts

### Global Commands
| Shortcut | Action |
|----------|--------|
| `F11` | Toggle fullscreen |
| `Ctrl+S` | Save project |
| `Ctrl+O` | Open project |
| `Ctrl+N` | New project |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |

### CT Viewer
| Shortcut | Tool/Action |
|----------|-------------|
| `B` | Brush tool |
| `L` | Lasso tool |
| `W` | Magic wand |
| `Esc` | Deselect tool |
| `Mouse Wheel` | Zoom in/out |
| `Middle Mouse` | Pan view |
| `Right Mouse` | Rotate (3D view) |

### Seismic Viewer
| Shortcut | Action |
|----------|--------|
| `+` / `-` | Adjust gain |
| `C` | Cycle color maps |
| `Mouse Drag` | Select trace range |

---

## Documentation

For detailed documentation, see:

- [**GUIDE.md**](GUIDE.md) - Comprehensive user guide
- [**Earthquake Simulation**](docs/EARTHQUAKE_SIM_QUICKSTART.md) - Earthquake modeling quickstart
- [**AI Segmentation Guide**](docs/AI_SEGMENTATION_GUIDE.md) - Using AI models
- [**Petrology System**](docs/PETROLOGY_SYSTEM.md) - Rock classification
- [**Thermodynamics**](docs/THERMODYNAMICS_ENHANCEMENTS.md) - Phase diagrams and reactions
- [**ONNX Models**](docs/ONNX_MODELS.md) - Setting up AI models
- [**Codebase Analysis**](docs/CODEBASE_ANALYSIS.md) - Developer documentation

---

## GeoScript Language

Geoscientist's Toolkit includes **GeoScript**, a domain-specific scripting language for automating workflows:

```geoscript
// Load and process CT data
ct = LoadCTStack("sample.ctstack")
ct.ApplyFilter("gaussian", sigma=2.0)

// Segment using threshold
mask = ct.Threshold(min=100, max=255)
material = CreateMaterial("Quartz", density=2.65)
ct.AssignMaterial(mask, material)

// Extract mesh and export
mesh = ct.ExtractMesh(material, algorithm="MarchingCubes")
mesh.Export("output.obj")
```

See the [GeoScript documentation](GUIDE.md#geoscript-scripting-language) for complete reference.

---

## Network Features

The **NodeEndpoint** component provides distributed computing capabilities:

- **HTTP API** on port `8500` for remote job submission
- **Network discovery** for finding other nodes
- **Terminal UI** (TUI) for monitoring CPU, memory, and network status
- **Job distribution** for parallel processing

Launch NodeEndpoint separately:
```bash
cd NodeEndpoint
dotnet run
```

Access the TUI by running NodeEndpoint in a terminal.

---

## Contributing

We welcome contributions! Here's how to get started:

1. **Fork the repository**
2. **Create a feature branch**
   ```bash
   git checkout -b feature/amazing-feature
   ```
3. **Commit your changes**
   ```bash
   git commit -m "Add amazing feature"
   ```
4. **Push to your fork**
   ```bash
   git push origin feature/amazing-feature
   ```
5. **Open a Pull Request**

### Development Guidelines
- Follow existing code style and conventions
- Add unit tests for new features
- Update documentation as needed
- Use descriptive commit messages
- Ensure all tests pass before submitting PR

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## Authors

**Matteo Mangiagalli**
- Email: m.mangiagalli@campus.uniurb.it

### Citation

If you use Geoscientist's Toolkit in your research, please cite:

```bibtex
@software{geoscientist_toolkit,
  title = {Geoscientist's Toolkit: Integrated Geoscientific Analysis Platform},
  author = {Mangia, Matteo},
  year = {2025},
  url = {https://github.com/mattemangia/GeoscientistToolkit}
}
```

---

## Acknowledgments

This project builds upon excellent open-source libraries:

- **ImGui.NET** by mellinoe - Immediate-mode UI framework
- **Veldrid** - Cross-platform graphics library
- **StbImageSharp** - Image loading
- **SkiaSharp** - 2D graphics rendering
- **MathNet.Numerics** - Scientific computing
- **GDAL** - Geospatial data abstraction
- **ONNX Runtime** - AI model inference
- **SAM2 & MicroSAM** teams - Segmentation models
- **Terminal.Gui** - Terminal user interface

---

## Support & Community

- 📖 **Documentation**: See [GUIDE.md](GUIDE.md) and `/docs` directory
- 🐛 **Bug Reports**: [GitHub Issues](../../issues)
- 💡 **Feature Requests**: [GitHub Discussions](../../discussions)
- 📧 **Contact**: matteo.mangia@gmail.com

---

## Screenshots

<div align="center">
  <i>Coming soon: Screenshots of CT segmentation, seismic analysis, and 3D visualization</i>
</div>

---

## Version History

**Current Version**: 1.0.0

See [CHANGELOG.md](CHANGELOG.md) for release notes and version history.

---

<div align="center">

**Built with ❤️ for the geoscience community**

[Documentation](GUIDE.md) • [Report Bug](../../issues) • [Request Feature](../../discussions)

</div>