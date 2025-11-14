# GeoscientistToolkit

A comprehensive desktop application for geoscientific data analysis, visualization, and simulation. Built with C# and .NET, featuring advanced 3D visualization, AI-powered analysis, and multi-physics simulations.

## Features

### Data Types Supported

- **CT Scans** - Micro-CT and CT imaging with segmentation, material analysis
- **Seismic Data** - SEG-Y format, trace analysis, well-to-seismic correlation
- **Borehole/Well Logs** - LAS format, lithology, parameter tracks
- **3D Meshes** - OBJ/STL import/export, surface extraction
- **GIS Data** - Geographic information systems, subsurface mapping
- **Images** - Standard image formats with analysis tools
- **Tables** - CSV/Excel data with plotting and analysis
- **Pore Network Models** - Flow simulation, permeability analysis
- **2D Geology** - Cross-sections, geological mapping
- **Acoustic Volumes** - Seismic wave propagation data

### Core Capabilities

#### CT Imaging & Segmentation
- Interactive 3D/2D/slice viewers with multi-planar reconstruction
- Manual segmentation tools (brush, lasso, magic wand)
- AI-powered segmentation (SAM2, MicroSAM, Grounding DINO)
- Material definition and assignment
- Interpolation between slices
- Export to 3D meshes (Marching Cubes/Surface Nets)

#### Seismic Analysis
- SEG-Y file loading and visualization
- Wiggle trace, variable area, and color map displays
- Line package management
- **Borehole-Seismic Integration**:
  - Generate synthetic seismic from well logs
  - Create pseudo-boreholes from seismic traces
  - Well-to-seismic tie with correlation analysis
  - Interactive correlation visualization

#### Borehole/Well Log Analysis
- LAS file import/export
- Lithology editor with contact types
- Parameter track visualization
- Geothermal simulation
- Synthetic seismic generation
- Well tie to seismic sections
- Cross-section visualization

#### Physical Property Simulations
- **Thermal Conductivity** - Finite element analysis on CT data
- **Acoustic Properties** - P-wave and S-wave velocity simulation
- **NMR Simulation** - T2 relaxation time calculation
- **Geomechanical Analysis** - Stress/strain, Mohr circles, failure prediction
- **Pore Network Modeling** - Permeability, tortuosity, flow simulation
- **Geothermal Simulation** - Heat transfer, fluid flow in boreholes

#### 3D Visualization
- Hardware-accelerated OpenGL/Vulkan rendering via Veldrid
- Real-time manipulation and interaction
- Multiple viewport support
- Pop-out window capability
- Screenshot capture

## Installation

### Prerequisites
- .NET 8.0 SDK or later
- Windows 10/11, Linux, or macOS
- GPU with OpenGL 3.3+ or Vulkan support (for 3D visualization)

### Building from Source

```bash
git clone https://github.com/yourusername/GeoscientistToolkit.git
cd GeoscientistToolkit
dotnet build
dotnet run
```

## Quick Start

### Loading Data

1. **File → New Project** or **Open Project**
2. **File → Import** and select your data format:
   - CT: DICOM, TIFF stack, or .ctstack
   - Seismic: SEG-Y files
   - Borehole: LAS or .bhb files
   - Mesh: OBJ or STL files
   - Images: PNG, JPG, BMP, TIFF

### Basic Workflows

#### CT Segmentation Workflow

1. Load CT dataset
2. **Tools → Segmentation → Material Manager** - Define materials
3. Use segmentation tools:
   - **Brush Tool** (B) - Paint regions
   - **Lasso Tool** (L) - Select irregular regions
   - **Magic Wand** (W) - Threshold-based selection
4. **Actions → Add Selections to Material**
5. **Tools → Export → Mesh Extraction** - Generate 3D model

#### Seismic-Borehole Integration

1. Load seismic dataset (SEG-Y)
2. Load borehole with Vp and Density data
3. **Borehole Tools → Seismic → Generate Synthetic**
   - Adjust dominant frequency (typically 20-40 Hz)
   - Creates synthetic seismic trace
4. **Seismic Tools → Borehole Integration → Well Tie**
   - Select borehole and seismic
   - Find best trace correlation
   - View correlation plot

#### Geothermal Simulation

1. Load borehole dataset
2. **Tools → Analysis → Geothermal Simulation**
3. Configure:
   - Mesh resolution
   - Injection/production temperatures
   - Flow configuration
4. Run simulation
5. View temperature distribution and flow paths

## Project Structure

```
GeoscientistToolkit/
├── Analysis/           # Simulation and analysis engines
│   ├── Geothermal/    # Heat transfer simulations
│   ├── AcousticSimulation/
│   ├── NMR/
│   ├── MaterialManager/
│   └── ThermalConductivity/
├── Business/          # Project management, serialization
├── Data/             # Dataset classes
│   ├── Borehole/
│   ├── CtImageStack/
│   ├── Seismic/
│   ├── Mesh3D/
│   ├── GIS/
│   └── ...
├── Tools/            # Cross-dataset integration tools
│   ├── BoreholeSeismic/  # Well-seismic integration
│   └── CTMesh/           # CT to mesh conversion
├── UI/               # User interface components
│   ├── Viewers/      # 3D/2D visualization
│   ├── Tools/        # Dataset-specific tools
│   └── Properties/   # Property panels
├── Util/             # Utilities (logging, managers)
├── Shaders/          # GLSL/HLSL shaders
├── Settings/         # Application configuration
├── docs/             # Documentation
└── samples/          # Sample data files
```

## Key Technologies

- **UI Framework**: ImGui.NET (immediate mode GUI)
- **Graphics**: Veldrid (cross-platform graphics abstraction)
- **3D Rendering**: OpenGL 3.3 / Vulkan
- **AI Integration**: ONNX Runtime (SAM2, MicroSAM, Grounding DINO)
- **Image Processing**: StbImageSharp, ImageSharp
- **File Formats**: SEG-Y, LAS, DICOM, OBJ, STL
- **Physics Simulation**: Custom finite element solvers

## Documentation

- [Earthquake Simulation Guide](docs/EARTHQUAKE_SIM_QUICKSTART.md)
- [AI Segmentation Guide](docs/AI_SEGMENTATION_GUIDE.md)
- [Petrology System](docs/PETROLOGY_SYSTEM.md)
- [Thermodynamics Enhancements](docs/THERMODYNAMICS_ENHANCEMENTS.md)
- [Codebase Analysis](docs/CODEBASE_ANALYSIS.md)
- [ONNX AI Models](docs/ONNX_MODELS.md)

## Features In Detail

### Borehole-Seismic Integration

**Generate Synthetic Seismic**:
- Calculates acoustic impedance from Vp and density
- Computes reflectivity coefficients
- Generates Ricker wavelet
- Applies convolution model: `seismic = reflectivity ⊗ wavelet`

**Create Pseudo-Boreholes**:
- Extract seismic amplitude at trace location
- Convert time → depth using velocity model
- Generate amplitude and envelope curves
- Useful for seismic-to-geology interpretation

**Well Tie Analysis**:
- Cross-correlates synthetic with real seismic
- Finds best matching trace
- Visualizes correlation across all traces
- Helps locate well position in seismic section

### CT Segmentation Tools

**Interactive Tools**:
- Real-time preview during selection
- Undo/redo support
- Multi-slice interpolation
- Batch operations

**AI Segmentation**:
- **SAM2** - Segment Anything Model v2
- **MicroSAM** - Optimized for microscopy
- **Grounding DINO** - Text-prompted segmentation
- Requires ONNX models (see docs/ONNX_MODELS.md)

### Physical Simulations

**Thermal Conductivity**:
- Finite element heat transfer
- Material-specific conductivity
- Steady-state and transient analysis

**Acoustic Simulation**:
- Wave propagation through heterogeneous media
- P-wave and S-wave velocities
- Elastic moduli calculation

**Geomechanical**:
- Stress-strain analysis
- Mohr circle visualization
- Failure prediction (Mohr-Coulomb, Drucker-Prager)
- Hydraulic fracturing simulation

## Keyboard Shortcuts

### Global
- `F11` - Toggle fullscreen
- `Ctrl+S` - Save project
- `Ctrl+O` - Open project
- `Ctrl+N` - New project

### CT Viewer
- `B` - Brush tool
- `L` - Lasso tool
- `W` - Magic wand
- `Esc` - Deselect tool
- Mouse wheel - Zoom
- Middle mouse - Pan
- Right mouse - Rotate (3D view)

### Seismic Viewer
- `+/-` - Adjust gain
- `C` - Cycle color maps
- Mouse drag - Select trace range

## Contributing

Contributions are welcome! Please ensure:
- Code follows existing style
- Add tests for new features
- Update documentation
- Use descriptive commit messages

## License

[Add license information]

## Authors

[Add author information]

## Acknowledgments

- ImGui.NET by mellinoe
- Veldrid graphics library
- StbImageSharp
- SAM2, MicroSAM teams

## Support

For issues, questions, or feature requests:
- GitHub Issues: [repository URL]
- Documentation: `/docs` directory

---

**Version**: 1.0
**Last Updated**: 2025-11-14
