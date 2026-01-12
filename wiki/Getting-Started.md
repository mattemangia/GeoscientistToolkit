# Getting Started

This page provides a step-by-step onboarding flow to get you from installation to results quickly.

---

## 1. Install

Choose one of the following installation methods:

### Cross-Platform Installer (Recommended)

The TUI wizard downloads and updates the toolkit automatically.

- **Windows**: Run `InstallerWizard.exe`
- **Linux**: Run `./InstallerWizard`
- **macOS**: Run `./InstallerWizard` (Intel/Apple Silicon)

See [Installation](Installation.md) for detailed instructions.

### Build from Source

For development or local customization:

```bash
git clone https://github.com/mattemangia/GeoscientistToolkit.git
cd GeoscientistToolkit
dotnet build
dotnet run
```

### Core Requirements

| Requirement | Specification |
|-------------|---------------|
| Runtime | .NET 8 SDK/Runtime |
| GPU | OpenGL 3.3+ or Vulkan for 3D visualization |
| RAM | 8-16 GB (16+ recommended for large CT stacks) |
| Disk | 2 GB minimum, 10 GB+ for large datasets |

---

## 2. Create Your First Project

1. **Launch the application**
   ```bash
   ./GeoscientistToolkit
   ```

2. **Create a new project**
   - Go to `File → New Project`
   - Choose a folder and project name (`.gtp` extension)

3. **Understanding Projects**
   - Projects are saved as `.gtp` files (zipped archives)
   - Contains all datasets, materials, and analysis configurations
   - Supports auto-save functionality

---

## 3. Import a Dataset

From `File → Import`, choose your data type:

| Data Type | Formats | Common Use |
|-----------|---------|------------|
| **CT / Volumes** | DICOM, TIFF stack, `.ctstack` | Rock core analysis, pore characterization |
| **Seismic** | SEG-Y | Subsurface imaging, structural interpretation |
| **Well Logs** | LAS, `.bhb` | Lithology analysis, well correlation |
| **Meshes** | OBJ, STL | 3D visualization, geometric modeling |
| **Images** | PNG, JPG, BMP, TIFF | Thin section analysis, texture mapping |
| **Tables** | CSV, Excel | Analytical results, time series |

### Import Tips
- For CT stacks, select the folder containing TIFF/DICOM files
- For SEG-Y files, the parser automatically detects byte order
- LAS files are parsed with standard curve mnemonics

---

## 4. Run an Analysis

### Example: CT Segmentation (Quick Start)

1. **Open a CT stack** from the Datasets panel
2. **Define materials** in `Tools → Segmentation → Material Manager`
3. **Segment** using:
   - Brush Tool (`B`) - Paint regions
   - Lasso Tool (`L`) - Select irregular areas
   - Magic Wand (`W`) - Threshold-based selection
   - AI Models - Automatic segmentation
4. **Export mesh** in `Tools → Export → Mesh Extraction`

**More details:** [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md)

### Example: Seismic + Borehole Integration

1. **Load a SEG-Y file** from `File → Import`
2. **Load a borehole** with Vp and Density data
3. **Generate synthetic seismic** in `Borehole Tools → Seismic`
4. **Tie wells** in `Seismic Tools → Borehole Integration`

**More details:** [Seismic Analysis](Seismic-Analysis.md)

### Example: Geothermal Simulation

1. **Load borehole dataset** with temperature/lithology
2. **Configure simulation** in `Tools → Analysis → Geothermal`
   - Set mesh resolution
   - Define injection/production temperatures
   - Choose flow configuration
3. **Run simulation**
4. **Visualize** temperature distribution and flow paths

**More details:** [Geothermal Simulation](Geothermal-Simulation.md)

---

## 5. Produce Outputs and Export

### Export Options

| Output Type | Formats | How to Export |
|-------------|---------|---------------|
| **3D Meshes** | OBJ, STL | Right-click dataset → Export |
| **Images** | PNG, JPG, TIFF | `File → Export` or screenshot button |
| **Tables** | CSV, Excel | Right-click table → Export |
| **Projects** | `.gtp` | `File → Save Project` |

### Working with Results

- **Screenshots**: Use the camera button in 3D viewers for high-resolution captures
- **Animation**: Export frame sequences for time-dependent simulations
- **Reports**: Export analysis results to CSV for further processing

---

## 6. Find the Right Documentation

### By Topic

| Topic | Guide |
|-------|-------|
| Complete user guide | [User Guide](User-Guide.md) |
| GeoScript scripting | [GeoScript Manual](GeoScript-Manual.md) |
| Image processing pipelines | [GeoScript Image Operations](GeoScript-Image-Operations.md) |
| AI segmentation | [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md) |
| Thermodynamics & geochemistry | [Thermodynamics and Geochemistry](Thermodynamics-and-Geochemistry.md) |
| Pore network modeling | [Pore Network Modeling](Pore-Network-Modeling.md) |
| API automation | [API Reference](API-Reference.md) |
| All documentation | [Home](Home.md) |

### By Workflow

| Workflow | Relevant Pages |
|----------|----------------|
| CT analysis & segmentation | [CT Imaging and Segmentation](CT-Imaging-and-Segmentation.md), [User Guide](User-Guide.md) |
| Seismic interpretation | [Seismic Analysis](Seismic-Analysis.md) |
| Geothermal modeling | [Geothermal Simulation](Geothermal-Simulation.md) |
| Reactive transport | [PhysicoChem Reactors](PhysicoChem-Reactors.md), [Pore Network Modeling](Pore-Network-Modeling.md) |
| Slope stability | [Slope Stability](Slope-Stability.md) |
| 3D reconstruction | [Photogrammetry](Photogrammetry.md) |
| Batch processing | [GeoScript Manual](GeoScript-Manual.md), [API Reference](API-Reference.md) |
| Distributed computing | [NodeEndpoint](NodeEndpoint.md) |

---

## Sample Workflows

### CT Segmentation Workflow

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

### Seismic-Borehole Integration

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

### Geothermal Simulation

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

## Tips for Success

1. **Start small**: Test workflows on small datasets before processing large volumes
2. **Save frequently**: Use `Ctrl+S` to save your project often
3. **Use GeoScript**: Automate repetitive tasks with scripts
4. **Check logs**: The log panel shows warnings and errors
5. **GPU acceleration**: Enable OpenCL in settings for faster simulations

---

## Next Steps

- [User Guide](User-Guide.md) - Complete application documentation
- [GeoScript Manual](GeoScript-Manual.md) - Learn to automate workflows
- [Installation](Installation.md) - Detailed setup instructions

---

**Need help?** Open an issue on [GitHub](https://github.com/mattemangia/GeoscientistToolkit/issues) or contact m.mangiagalli@campus.uniurb.it
