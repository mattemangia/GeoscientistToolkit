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

## Start Here

Looking for a linear onboarding path? Start with [START_HERE.md](START_HERE.md) for a step-by-step flow (installation → project → dataset → output) that links to the most relevant guides. For a complete feature-to-documentation map, see [docs/FUNCTIONAL_OVERVIEW.md](docs/FUNCTIONAL_OVERVIEW.md).

## Key Features

### Multi-Scale Analysis
- **Pore-scale**: Micro-CT imaging, pore network modeling, NMR simulation
- **Core-scale**: Rock physics, acoustic properties, thermal conductivity
- **Well-scale**: Borehole/well log analysis, geothermal simulation
- **Reservoir-scale**: Seismic analysis, 3D geological modeling
- **Basin-scale**: Regional mapping, GIS integration

### Comprehensive Data Support

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

### Advanced Analysis Modules

#### CT Imaging & Segmentation
- Interactive 3D/2D/slice viewers with multi-planar reconstruction
- Manual segmentation tools (brush, lasso, magic wand)
- AI-powered segmentation using ONNX models:
  - **SAM2** (Segment Anything Model v2)
  - **MicroSAM** (optimized for microscopy)
  - **Grounding DINO** (text-prompted segmentation)
- Material definition and interpolation between slices
- Export to 3D meshes via Marching Cubes/Surface Nets

#### Seismic Analysis
- SEG-Y file loading and comprehensive visualization
- Multiple display modes: wiggle trace, variable area, color maps
- **Borehole-Seismic Integration**:
  - Generate synthetic seismograms from well logs
  - Create pseudo-boreholes from seismic traces
  - Well-to-seismic tie with correlation analysis
  - Interactive correlation visualization

#### Physical Property Simulations
- **Thermal Conductivity** - Finite element heat transfer analysis
- **Acoustic Properties** - P-wave and S-wave velocity simulation
- **NMR Simulation** - T2 relaxation time calculation
- **Geomechanical Analysis** - Stress/strain, Mohr circles, failure prediction
- **Slope Stability (3D/2D)** - DEM-based block simulation and 2D cross-section stability analysis ([docs](docs/SLOPE_STABILITY_SIMULATION.md))
- **Pore Network Modeling** - Permeability, tortuosity, flow simulation
- **Dual Pore Network Modeling** - Macro–micro pore coupling from CT + SEM with parallel/series/mass-transfer modes, micro-network extraction, and coupled permeability/reactive transport simulations
- **Geothermal Simulation** - Heat transfer and fluid flow in boreholes

#### Thermodynamics & Geochemistry
- **Extended Compound Library** - 60+ additional silicates, carbonates, sulfates, chlorides, oxides, aqueous ions, and gases from Holland & Powell, PHREEQC, Robie & Hemingway, and SUPCRT92 datasets
- **GeoScript Thermo Commands** - `CALCULATE_PHASES` for phase-separated outputs, `CALCULATE_CARBONATE_ALKALINITY` for pH/alkalinity-driven carbonate speciation, and an enhanced `REACT` command with phase grouping and mineral identification
- **Equilibrium Workflow** - Gibbs energy minimization with element conservation, activity coefficients, and temperature-dependent constants for robust equilibrium calculations

#### 3D Visualization
- Hardware-accelerated rendering via Veldrid (OpenGL/Vulkan/DirectX)
- Real-time manipulation and interaction
- Multiple viewport support with docking
- Pop-out window capability
- High-resolution screenshot capture

### Performance Features
- GPU-accelerated simulations using OpenCL
- Multi-threaded processing for large datasets
- Efficient memory management for CT stacks
- Real-time rendering at 60+ FPS

### Earthquake & GIS Integration
- 3D earthquake simulation with corrected stress-tensor propagation
- Generation of subsurface GIS volumes (PGA/PGV/PGD, stress, fracture density) from simulations
- Depth-slice layers, per-location time series extraction, and 3D geology construction from 2D map selections with layered models

### Structural Geology & Restoration
- Interactive 2D cross-section restoration with real-time overlays, opacity controls, and fault rendering
- Slider-driven flexural slip unfolding/deformation with percentage controls to compare present-day vs restored geometry
- Overlay indicators and visibility toggles to declutter original formations during restoration previews

### PHYSICOCHEM Multiphysics Reactors
- Dataset builder for complex reactors (box/sphere/cylinder/cone/custom) with 2D-to-3D extrusion and revolutions
- Comprehensive boundary conditions, force fields (gravity, vortex, centrifugal), nucleation sites, and reactive transport coupling
- Parameter sweeps with sensitivity analysis and optional geothermal coupling for TOUGH-like scenarios

### GeoScript Dataset Pipelines
- Pipeline syntax (`|>`) for chaining dataset/image operations
- Built-in filters (Gaussian, median, Sobel/Canny, bilateral, NLM, unsharp) and thresholding
- Brightness/contrast, grayscale/inversion, and mask operations directly in scripts

---

## Installation

### Prerequisites
- **Build system**: Install the .NET 8.0 SDK or later (used for compilation)
- **Runtime**: .NET 8.0 runtime (included with the SDK)
- **OS**: Windows 10/11, Linux (x64), or macOS (ARM64/x86_64)
- **GPU**: OpenGL 3.3+ or Vulkan support (for 3D visualization)
- **RAM**: 8 GB minimum, 16 GB+ recommended
- **Disk**: 2 GB minimum, 10 GB+ recommended for large datasets
- **Optional components**: OpenCL, ONNX Runtime, GDAL, and AI models are supported but not mandatory. See [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md) for versions and OS-specific install notes.

### Quick Start

#### Cross-platform installer
The `InstallerWizard` TUI binaries install and update the toolkit on Windows, Linux, and macOS (Intel/Apple Silicon). The `InstallerPackager` tool bundles `dotnet publish` outputs, includes ONNX models and the NodeEndpoint server, and updates the auto-update manifest. Step-by-step instructions are available in [docs/installers.md](docs/installers.md).

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

### CI/CD

This repository uses GitHub Actions for build validation. See `.github/workflows/ci.yml` for the .NET 8 build pipeline.

### Diagnostic CLI

The ImGui executable supports diagnostic flags for validating AI models, renderer/GPU setup, and automated tests without launching the full UI:

```bash
dotnet run -- --ai-diagnostic
dotnet run -- --gui-diagnostic
dotnet run -- --test=all
dotnet run -- --test=test1,test2
```

Diagnostics open a full-screen log window with **Cancel** and **Close** controls. Errors are highlighted in red.

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
├── AddIns/                    # Plugin framework + sample add-ins
├── Analysis/                  # Simulation and analysis engines
│   ├── AcousticSimulation/   # Wave propagation
│   ├── AmbientOcclusionSegmentation/ # CT segmentation helpers
│   ├── Geomechanics/         # Stress/strain analysis
│   ├── Geothermal/           # Heat transfer simulations
│   ├── Hydrological/         # Flow and hydrology solvers
│   ├── ImageAdjustement/     # Image enhancement pipelines
│   ├── MaterialStatistics/   # Material metrics
│   ├── Materials/            # Material property analysis
│   ├── Multiphase/           # Multiphase flow modeling
│   ├── NMR/                  # NMR relaxation modeling
│   ├── ParticleSeparator/    # Particle separation tools
│   ├── Photogrammetry/       # Image-to-geometry workflows
│   ├── PhysicoChem/          # Physicochemical simulations
│   ├── PNM/                  # Pore network modeling
│   ├── RockCoreExtractor/    # Core extraction utilities
│   ├── Seismology/           # Seismic analysis
│   ├── SlopeStability/       # Slope stability simulation
│   ├── TextureClassification/ # Texture ML pipelines
│   ├── ThermalConductivity/  # FEM heat solver
│   ├── Thermodynamic/        # Thermodynamics engines
│   └── Transform/            # Geometry transforms
├── Api/                       # Public API contracts and helpers
├── Business/                  # Project management, serialization
├── Data/                      # Dataset classes
│   ├── AcousticVolume/       # Acoustic datasets
│   ├── Borehole/             # Well log data structures
│   ├── CrustalModels/        # Crustal model datasets
│   ├── CtImageStack/         # CT scan management
│   ├── GIS/                  # Geographic information
│   ├── Image/                # Image datasets
│   ├── Loaders/              # File loaders/importers
│   ├── Materials/            # Material datasets
│   ├── Media/                # Media assets
│   ├── Mesh3D/               # 3D mesh operations
│   ├── Nerf/                 # Neural radiance field data
│   ├── PNM/                  # Pore network data
│   ├── PhysicoChem/          # Physicochemical datasets
│   ├── Seismic/              # Seismic data handling
│   ├── Table/                # Tabular datasets
│   ├── Text/                 # Text datasets
│   ├── TwoDGeology/          # 2D geology datasets
│   └── VolumeData/           # Volume datasets
├── Documentation/             # Specialized domain docs
├── Examples/                  # Sample workflows and assets
├── GTK/                       # GTK-based UI frontend
├── InstallerPackager/         # Build-time packaging tool
├── InstallerWizard/           # Cross-platform installer TUI
├── Network/                   # Distributed node discovery/messaging
├── NodeEndpoint/              # Network service for distributed computing
│   ├── Program.cs            # HTTP endpoint (port 8500)
│   ├── TuiManager.cs         # Terminal UI for monitoring
│   └── Services/             # Network discovery
├── ONNX/                      # Model assets and runtime helpers
├── OpenCL/                    # GPU kernels
├── Scripting/                 # GeoScript language runtime + commands
├── Settings/                  # Application configuration
├── Shaders/                   # GLSL/HLSL shader programs
├── Tests/                     # Unit/integration tests
├── Tools/                     # Cross-dataset integration tools
├── UI/                        # ImGui user interface components
├── Util/                      # Utilities (logging, managers)
├── VerificationTests/         # Verification test harness
├── docs/                      # End-user documentation
└── wiki/                      # Developer documentation
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

### Wiki

**[Visit the Project Wiki](wiki/Home.md)** for comprehensive documentation organized by topic:

| Category | Contents |
|----------|----------|
| **Getting Started** | [Getting Started](wiki/Getting-Started.md), [Installation](wiki/Installation.md) |
| **User Guides** | [User Guide](wiki/User-Guide.md), [GeoScript Manual](wiki/GeoScript-Manual.md), [GeoScript Image Operations](wiki/GeoScript-Image-Operations.md) |
| **Analysis Modules** | [CT Imaging](wiki/CT-Imaging-and-Segmentation.md), [Seismic](wiki/Seismic-Analysis.md), [Geothermal](wiki/Geothermal-Simulation.md), [PNM](wiki/Pore-Network-Modeling.md), [Slope Stability](wiki/Slope-Stability.md), [Photogrammetry](wiki/Photogrammetry.md) |
| **Simulations** | [Thermodynamics](wiki/Thermodynamics-and-Geochemistry.md), [PhysicoChem Reactors](wiki/PhysicoChem-Reactors.md) |
| **Developer** | [API Reference](wiki/API-Reference.md), [NodeEndpoint](wiki/NodeEndpoint.md), [Developer Guide](wiki/Developer-Guide.md) |
| **Quality** | [Verification & Testing](wiki/Verification-and-Testing.md) |

### Additional Resources

- [**GUIDE.md**](GUIDE.md) - Comprehensive user guide
- [**START_HERE.md**](START_HERE.md) - Quick onboarding path
- [**Earthquake Simulation**](docs/EARTHQUAKE_SIM_QUICKSTART.md) - Earthquake modeling quickstart
- [**AI Segmentation Guide**](docs/AI_SEGMENTATION_GUIDE.md) - Using AI models
- [**Petrology System**](docs/PETROLOGY_SYSTEM.md) - Rock classification
- [**Thermodynamics**](docs/THERMODYNAMICS_ENHANCEMENTS.md) - Phase diagrams and reactions
- [**ONNX Models**](docs/ONNX_MODELS.md) - Setting up AI models
- [**Testing Case Studies**](docs/TESTING_CASE_STUDIES.md) - Validation datasets and standards
- [**Codebase Analysis**](docs/CODEBASE_ANALYSIS.md) - Developer documentation

---

## GeoScript Language

Geoscientist's Toolkit includes **GeoScript**, a domain-specific scripting language for automating workflows:
> **Note:** GeoScript in this project is an internal language for Geoscientist's Toolkit and is not affiliated with https://geoscript.net or any of its components.

```geoscript
// Load a CT stack and set it as the active dataset
LOAD "sample.ctstack" AS "CT Volume" TYPE=CtImageStack
USE @'CT Volume'

// Define a material and segment using a threshold
CT_ADD_MATERIAL name='Quartz' color=230,230,230
CT_FILTER3D type=gaussian size=5 |> CT_SEGMENT method=threshold min=100 max=255 material=1

// Review porosity for the segmented material
CT_ANALYZE_POROSITY void_material=1
```

See the [GeoScript documentation](GUIDE.md#geoscript-scripting-language) for complete reference.

---

## Network Features

<div align="center">
<img src="NodeEndpoint.png" alt="NodeEndpoint Logo" width="400"/>
</div>

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
  year = {2026},
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

- **Documentation**: See [GUIDE.md](GUIDE.md) and `/docs` directory
- **Bug Reports**: [GitHub Issues](../../issues)
- **Feature Requests**: [GitHub Discussions](../../discussions)
- **Contact**: m.mangiagalli@campus.uniurb.it

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

**Built for the geoscience community**

[Documentation](GUIDE.md) • [Report Bug](../../issues) • [Request Feature](../../discussions)

</div>
