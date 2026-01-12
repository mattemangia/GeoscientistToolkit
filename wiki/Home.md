<div align="center">

![Geoscientist's Toolkit Logo](../image.png)

# Geoscientist's Toolkit

**A comprehensive desktop application for geoscientific data analysis, visualization, and simulation**

**Current Version: 1.0.0**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=.net)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-lightgrey)](#installation)
[![License](https://img.shields.io/badge/license-MIT-green)](../LICENSE)

</div>

---

## Welcome to the Wiki

This wiki contains comprehensive documentation for the Geoscientist's Toolkit application. Whether you're a new user getting started or an experienced researcher looking for advanced features, you'll find the information you need here.

### Quick Navigation

| Getting Started | Core Features | Advanced Topics |
|----------------|---------------|-----------------|
| [Getting Started](Getting-Started.md) | [User Guide](User-Guide.md) | [GeoScript Manual](GeoScript-Manual.md) |
| [Installation](Installation.md) | [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md) | [API Reference](API-Reference.md) |
| | [Seismic Analysis](Seismic-Analysis.md) | [NodeEndpoint](NodeEndpoint.md) |
| | [Geothermal Simulation](Geothermal-Simulation.md) | [Developer Guide](Developer-Guide.md) |
| | [Simulation Modules](Simulation-Modules.md) | |
| | [2D Geology & Restoration](2D-Geology-and-Restoration.md) | |
| | [Stratigraphy Correlation](Stratigraphy-Correlation.md) | |

---

## Overview

Geoscientist's Toolkit is an advanced, cross-platform desktop application built with C# and .NET 8.0 that provides an integrated environment for working with diverse geophysical and geochemical datasets. From pore-scale (micrometers) to basin-scale (kilometers) analysis, it combines real-time 3D visualization, GPU-accelerated simulations, and a domain-specific scripting language (GeoScript) to enable sophisticated workflows across multiple geoscience disciplines.

---

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
| **CT Scans** | DICOM, TIFF, .ctstack | AI-powered segmentation (SAM2, MicroSAM), 3D reconstruction |
| **Seismic Data** | SEG-Y | Trace analysis, wiggle/variable area displays, well correlation |
| **Well Logs** | LAS, .bhb | Lithology editing, synthetic seismic, geothermal simulation |
| **2D Geology Profiles** | .2dgeo | Cross-section editing, structural restoration, slope stability prep |
| **3D Meshes** | OBJ, STL | Import/export, surface extraction from CT |
| **GIS Data** | Shapefile, GeoJSON | Subsurface mapping, geographic integration |
| **Images** | PNG, JPG, BMP, TIFF | Standard image processing and analysis |
| **Tabular** | CSV, Excel | Data plotting, statistical analysis |
| **Pore Networks** | Custom | Flow simulation, permeability calculation |

### Advanced Analysis Modules

#### CT Imaging & Segmentation
- Interactive 3D/2D/slice viewers with multi-planar reconstruction
- Manual segmentation tools (brush, lasso, magic wand)
- AI-powered segmentation using ONNX models (SAM2, MicroSAM, Grounding DINO)
- Material definition and interpolation between slices
- Export to 3D meshes via Marching Cubes/Surface Nets

**Learn more:** [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md)

#### Seismic Analysis
- SEG-Y file loading and comprehensive visualization
- Multiple display modes: wiggle trace, variable area, color maps
- Borehole-Seismic Integration with synthetic seismograms
- Well-to-seismic tie with correlation analysis

**Learn more:** [Seismic Analysis](Seismic-Analysis.md)

#### Physical Property Simulations
- **Thermal Conductivity** - Finite element heat transfer analysis ([Thermal Conductivity](Thermal-Conductivity.md))
- **Acoustic Properties** - P-wave and S-wave velocity simulation ([Acoustic Simulation](Acoustic-Simulation.md))
- **NMR Simulation** - T2 relaxation time calculation ([NMR Simulation](NMR-Simulation.md))
- **Geomechanical Analysis** - Stress/strain, Mohr circles, failure prediction ([Geomechanical Simulation](Geomechanical-Simulation.md))
- **Slope Stability (3D/2D)** - DEM-based block simulation
- **Pore Network Modeling** - Permeability, tortuosity, flow simulation
- **Geothermal Simulation** - Heat transfer and fluid flow in boreholes

**Learn more:** [Simulation Modules](Simulation-Modules.md) | [Geothermal Simulation](Geothermal-Simulation.md) | [Slope Stability](Slope-Stability.md) | [Pore Network Modeling](Pore-Network-Modeling.md)

#### Structural Geology & Correlation
- **2D Geology & Restoration** - Cross-section drafting, fault/formation editing, interactive restoration ([2D Geology & Restoration](2D-Geology-and-Restoration.md))
- **Stratigraphy Correlation** - Compare regional/chronostratigraphic charts and export correlation tables ([Stratigraphy Correlation](Stratigraphy-Correlation.md))

#### Thermodynamics & Geochemistry
- Extended compound library (60+ species)
- GeoScript thermo commands for phase calculations
- Equilibrium workflow with Gibbs energy minimization

**Learn more:** [Thermodynamics and Geochemistry](Thermodynamics-and-Geochemistry.md) | [PhysicoChem Reactors](PhysicoChem-Reactors.md)

### Performance Features
- GPU-accelerated simulations using OpenCL
- Multi-threaded processing for large datasets
- Efficient memory management for CT stacks
- Real-time rendering at 60+ FPS

---

## Quick Start

### 1. Install
- **Cross-platform installer** (recommended): Use the TUI wizard
- **Build from source**: Clone and run `dotnet build`

See [Installation](Installation.md) for detailed instructions.

### 2. Create Your First Project
1. Launch the application
2. Go to `File → New Project`
3. Choose a location and name for your `.gtp` project file

### 3. Import Data
From `File → Import`, select your data format:
- CT/Volumes: DICOM, TIFF stack, `.ctstack`
- Seismic: SEG-Y
- Well logs: LAS, `.bhb`
- Meshes: OBJ, STL
- Images: PNG, JPG, BMP, TIFF
- Tables: CSV, Excel

See [Getting Started](Getting-Started.md) for a complete walkthrough.

---

## GeoScript Language

Geoscientist's Toolkit includes **GeoScript**, a domain-specific scripting language for automating workflows:

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

**Learn more:** [GeoScript Manual](GeoScript-Manual.md) | [GeoScript Image Operations](GeoScript-Image-Operations.md)

---

## Technology Stack

| Component | Technology | Purpose |
|-----------|-----------|---------|
| **UI Framework** | ImGui.NET | Immediate-mode GUI |
| **Graphics** | Veldrid | Cross-platform graphics |
| **3D Rendering** | OpenGL 3.3 / Vulkan / DirectX 11 | Hardware-accelerated visualization |
| **AI/ML** | ONNX Runtime | Model inference for segmentation |
| **Scientific Computing** | MathNet.Numerics | Linear algebra, statistics |
| **GIS** | GDAL, NetTopologySuite | Geospatial data handling |
| **GPU Compute** | Silk.NET.OpenCL | Parallel computation |

---

## Documentation Categories

### Getting Started
- [Getting Started](Getting-Started.md) - Linear onboarding path
- [Installation](Installation.md) - System requirements and setup

### User Guides
- [User Guide](User-Guide.md) - Comprehensive application guide
- [GeoScript Manual](GeoScript-Manual.md) - Complete scripting reference
- [GeoScript Image Operations](GeoScript-Image-Operations.md) - Image processing pipelines

### Analysis Modules
- [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md) - 3D imaging and AI segmentation
- [Seismic Analysis](Seismic-Analysis.md) - Seismic data and earthquake simulation
- [Geothermal Simulation](Geothermal-Simulation.md) - Heat transfer and BHE modeling
- [Pore Network Modeling](Pore-Network-Modeling.md) - PNM generation and flow simulation
- [Slope Stability](Slope-Stability.md) - DEM-based stability analysis
- [2D Geology & Restoration](2D-Geology-and-Restoration.md) - Cross-section workflows and restoration
- [Stratigraphy Correlation](Stratigraphy-Correlation.md) - Stratigraphic chart correlation viewer
- [Photogrammetry](Photogrammetry.md) - 3D reconstruction from images
- [Hydrological Analysis](Hydrological-Analysis.md) - Flow routing and rainfall simulation
- [Acoustic Simulation](Acoustic-Simulation.md) - CT-scale wave propagation
- [NMR Simulation](NMR-Simulation.md) - T2/T1 spectra and pore-size estimation
- [Thermal Conductivity](Thermal-Conductivity.md) - Heat flow in segmented CT volumes
- [Geomechanical Simulation](Geomechanical-Simulation.md) - Stress/strain and failure envelopes

### Simulations
- [Thermodynamics and Geochemistry](Thermodynamics-and-Geochemistry.md) - Phase diagrams and reactions
- [PhysicoChem Reactors](PhysicoChem-Reactors.md) - Multiphysics reactor simulation
- [Multiphase Flow](Multiphase-Flow.md) - Water/steam/NCG EOS models
- [Simulation Modules](Simulation-Modules.md) - Full simulation index

### Developer Resources
- [API Reference](API-Reference.md) - External automation API
- [NodeEndpoint](NodeEndpoint.md) - Distributed computing service
- [Developer Guide](Developer-Guide.md) - Architecture and extension points

### Quality Assurance
- [Verification and Testing](Verification-and-Testing.md) - Test cases and benchmarks

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

---

## Contributing

We welcome contributions! See the [Contributing Guidelines](https://github.com/mattemangia/GeoscientistToolkit#contributing) for details.

---

## Support & Community

- **Documentation**: Browse this wiki
- **Bug Reports**: [GitHub Issues](https://github.com/mattemangia/GeoscientistToolkit/issues)
- **Feature Requests**: [GitHub Discussions](https://github.com/mattemangia/GeoscientistToolkit/discussions)
- **Contact**: matteo.mangia@gmail.com

---

## Citation

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

## License

This project is licensed under the MIT License.

**Author:** Matteo Mangiagalli

**Institution:** Universita degli Studi di Urbino Carlo Bo

**Email:** m.mangiagalli@campus.uniurb.it

---

<div align="center">

**Built for the geoscience community**

[Getting Started](Getting-Started.md) | [User Guide](User-Guide.md) | [GeoScript Manual](GeoScript-Manual.md)

</div>
