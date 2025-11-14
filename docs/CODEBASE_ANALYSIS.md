# GeoscientistToolkit - Comprehensive Codebase Analysis

## Executive Summary

The GeoscientistToolkit is a comprehensive C# (.NET 8.0) desktop application for geoscientific data analysis and visualization. It integrates GIS, borehole, seismic, geomechanical, geothermal, and petrological analysis capabilities. The application uses an extensible architecture with support for multi-threaded simulations, GPU acceleration (OpenCL/Vulkan), and a custom scripting language (GeoScript).

**Total Codebase**: 375 C# files across 14 major modules

---

## 1. Architecture Overview

### 1.1 Technology Stack

- **Framework**: .NET 8.0
- **Graphics**: Veldrid 4.9.0 (Cross-platform graphics abstraction)
- **UI**: ImGui.NET 1.90.8.1 with ImGui docking support
- **GIS**: GDAL 3.11.3 + NetTopologySuite 2.6.0 + ProjNET 2.0.0
- **Numerics**: MathNet.Numerics 6.0.0-beta2
- **File I/O**: TIFF (BitMiracle.LibTiff.NET), Excel (ClosedXML), Image (StbImageSharp, SkiaSharp)
- **GPU Compute**: Silk.NET.OpenCL 2.21.0
- **Deployment**: Cross-platform (Windows, macOS, Linux)

### 1.2 Core Architecture Pattern

```
Program.cs (Entry Point)
    ↓
Application.cs (Graphics/Window Management + ImGui Controller)
    ↓
MainWindow.cs (UI Layout & Event Dispatch)
    ├── DatasetPanel (Project tree view)
    ├── PropertiesPanel (Metadata editor)
    ├── ToolsPanel (Category-based tools)
    ├── Various Viewers (Dataset-specific visualization)
    └── Various Dialogs (Import, Export, Settings)
    
ProjectManager.cs (Singleton - Project State)
    ├── LoadedDatasets (List<Dataset>)
    ├── ProjectMetadata
    └── Undo/Redo Support
    
Business Layer (Geoscientific Domain Logic)
    ├── CompoundLibrary.cs (Thermodynamic compounds)
    ├── GeoScript.cs (Custom scripting engine)
    ├── MaterialLibrary.cs (Rock/mineral properties)
    └── Petrology/Stratigraphies (Rock classification)

Data Layer (Data Models & Serialization)
    ├── Dataset.cs (Abstract base)
    ├── DatasetDTO.cs (Serialization models)
    ├── GIS/ (Geographic data)
    ├── Borehole/ (Well log data)
    ├── CtImageStack/ (3D CT imaging)
    ├── Mesh3D/ (3D geometry)
    └── Table/ (Tabular data)

Analysis Layer (Computational Simulations)
    ├── Geomechanics/ (Stress/strain, failure analysis)
    ├── Geothermal/ (Heat flow, borehole simulation)
    ├── Thermodynamic/ (Phase equilibria, reactions)
    ├── NMR/ (Nuclear magnetic resonance)
    ├── Acoustic/ (Seismic wave propagation)
    └── Other analysis modules
```

---

## 2. Dataset System & Data Model

### 2.1 Dataset Type Hierarchy

```csharp
public enum DatasetType
{
    CtImageStack,          // 3D CT scan volumes
    CtBinaryFile,          // Binary volume data
    MicroXrf,              // Micro X-ray fluorescence
    PointCloud,            // LiDAR/point data
    Mesh,                  // 3D mesh geometry
    SingleImage,           // 2D images
    Group,                 // Container for datasets
    Mesh3D,                // 3D surface meshes
    Table,                 // Tabular data (CSV, Excel)
    GIS,                   // Geographic layers (vector/raster)
    AcousticVolume,        // Seismic volume data
    PNM,                   // Pore network models
    Borehole,              // Well log data
    TwoDGeology,           // 2D geological cross-sections
    SubsurfaceGIS          // 3D subsurface interpolation
}
```

### 2.2 Base Dataset Class

Located: `/Data/Dataset.cs`

```csharp
public abstract class Dataset
{
    public string Name { get; set; }
    public string FilePath { get; set; }
    public DatasetType Type { get; protected set; }
    public DateTime DateCreated { get; set; }
    public DateTime DateModified { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public DatasetMetadata DatasetMetadata { get; set; }  // Rich metadata
    
    // Abstract methods
    public abstract long GetSizeInBytes();
    public abstract void Load();
    public abstract void Unload();
}
```

**Key Features**:
- Lazy loading support (Load/Unload methods)
- Rich metadata tracking (sample name, location, collection date, depth, size)
- Serialization support via DatasetDTO pattern
- Parent-child dataset relationships

### 2.3 GIS Dataset Structure

Located: `/Data/GIS/GISDataset.cs`

**Layers**:
- **Vector Layers**: Points, Lines, Polygons with feature properties
- **Raster Layers**: Float 2D grids (elevation, satellite imagery)
- **Basemap Support**: GeoTIFF, TileServer, WMS

```csharp
public class GISDataset : Dataset
{
    public List<GISLayer> Layers { get; set; }
    public GISProjection Projection { get; set; }
    public BoundingBox Bounds { get; set; }
    public Vector2 Center { get; set; }
    public BasemapType BasemapType { get; set; }
    public GISTag Tags { get; set; }  // Powerful tagging system
    public Dictionary<string, object> GISMetadata { get; set; }
}

public class GISLayer
{
    public string Name { get; set; }
    public LayerType Type { get; set; }  // Vector or Raster
    public List<GISFeature> Features { get; set; }
    public bool IsVisible { get; set; }
    public bool IsEditable { get; set; }
    public Vector4 Color { get; set; }
    public float LineWidth { get; set; }
    public float PointSize { get; set; }
    public Dictionary<string, object> Properties { get; set; }
}

public class GISFeature
{
    public FeatureType Type { get; set; }  // Point, Line, Polygon, Multi-*
    public List<Vector2> Coordinates { get; set; }
    public Dictionary<string, object> Properties { get; set; }
    public string Id { get; set; }
}
```

**Supported Formats**:
- Shapefile (.shp) - via NetTopologySuite
- GeoJSON (.geojson)
- KML/KMZ - Google Earth format
- GeoTIFF (.tif/.tiff) - via GDAL

**GIS Tags System**:
- Powerful enum flags system for metadata classification
- Automatic tag inference from file format
- Operations are filtered by tags (e.g., "Intersection" only works on VectorData)

### 2.4 Borehole Dataset

Located: `/Data/Borehole/BoreholeDataset.cs`

**Key Data Structures**:
- **LithologyUnit**: Depth interval with rock type, contact type, color, description
- **ParameterTrack**: Log curves with depth-value pairs (porosity, permeability, etc.)
- **ParameterSource**: Links log values to source datasets (NMR, CT, acoustic)
- **ContactType Enum**: Sharp, Erosive, Gradational, Conformable, Unconformity, Faulted, Intrusive, Indistinct

**Multi-dataset Integration**:
- Links boreholes to CT image stacks at specific depths
- Associates NMR results with depth intervals
- Ties acoustic data to lithological units
- Supports parameter interpolation from multiple sources

### 2.5 Subsurface 3D Model

Located: `/Data/GIS/SubsurfaceGISDataset.cs`

Extends GISDataset with 3D interpolation capabilities:

```csharp
public class SubsurfaceGISDataset : GISDataset
{
    // 3D regular grid
    public Vector3 GridOrigin { get; set; }
    public Vector3 GridSize { get; set; }
    public Vector3 VoxelSize { get; set; }
    public int GridResolutionX { get; set; }  // e.g., 50
    public int GridResolutionY { get; set; }  // e.g., 50
    public int GridResolutionZ { get; set; }  // e.g., 100
    
    // Voxel grid content
    public List<SubsurfaceVoxel> VoxelGrid { get; set; }
    public List<SubsurfaceLayerBoundary> LayerBoundaries { get; set; }
    
    // Interpolation settings
    public InterpolationMethod Method { get; set; }  // IDW, Kriging, etc.
    public float InterpolationRadius { get; set; }
    public float IDWPower { get; set; }
}

public class SubsurfaceVoxel
{
    public Vector3 Position { get; set; }
    public string LithologyType { get; set; }
    public Dictionary<string, float> Parameters { get; set; }  // Temperature, porosity, etc.
    public float Confidence { get; set; }  // Based on distance to boreholes
}
```

---

## 3. GIS Integration & Spatial Analysis

### 3.1 GIS Tools & Operations

Located: `/Data/GIS/GISTools.cs`, `/Data/GIS/GISOperations.cs`

**Categorized Tools**:
1. **Scripting** - GeoScript Editor for automated workflows
2. **Layers** - Layer Manager (visibility, styling, properties)
3. **Properties & Tags** - Projection info, descriptive metadata
4. **Operations** - Spatial analysis (buffering, intersection, union, etc.)
5. **Export** - Shapefile, GeoTIFF generation

**Spatial Operations** (via NetTopologySuite):
- Buffer analysis
- Intersection & Union
- Polygon offsetting
- Convex hull generation
- Spatial indexing
- Coordinate system transformations (ProjNET)

### 3.2 Coordinate System Support

Located: `/Data/GIS/CoordinateConverter.cs`

- EPSG code support (4326 for WGS84, etc.)
- Datum transformations
- Projection support (Geographic, Projected)
- Automated coordinate conversion between layers

### 3.3 Geological Mapping

Located: `/Data/GIS/GeologicalMapping.cs`, `/Data/GIS/GeologicalMappingViewer.cs`

**GeologicalFeature** (extends GISFeature):
- Structural measurements: Strike, Dip, Dip Direction
- Kinematic data: Plunge, Trend
- Lithological classification: Formation, Lithology code, Age code
- Fault attributes: Displacement, Movement sense
- Quality flags: Inferred, Covered

**Visualization**:
- Symbol patterns for lithologies
- Structural trend stereonets
- Thickness display
- Contact type visualization

---

## 4. Simulation Systems

### 4.1 Geomechanical Simulation

Located: `/Analysis/Geomechanics/`

**Files**: 16 files, ~2,000+ LOC for GeomechanicalSimulationCPU.cs alone

**Capabilities**:
- Finite element method (FEM) solver on voxel grids
- Stress tensor calculation (σxx, σyy, σzz, σxy, σxz, σyz)
- Strain field computation
- Principal stress calculation
- Failure analysis (Mohr-Coulomb, Drucker-Prager, Hoek-Brown, Griffith criteria)
- Damage evolution modeling
- Plastic strain tracking

**Key Parameters**:
```csharp
public class GeomechanicalParameters
{
    // Material properties
    public float YoungModulus { get; set; } = 30000f;  // MPa
    public float PoissonRatio { get; set; } = 0.25f;
    public float Cohesion { get; set; } = 10f;
    public float FrictionAngle { get; set; } = 30f;
    public float TensileStrength { get; set; } = 5f;
    public float Density { get; set; } = 2700f;
    
    // Loading conditions
    public LoadingMode LoadingMode { get; set; }  // Uniaxial, Biaxial, Triaxial, Custom
    public float Sigma1 { get; set; } = 100f;  // Max principal stress
    public float Sigma2 { get; set; } = 50f;   // Intermediate
    public float Sigma3 { get; set; } = 20f;   // Confining pressure
    
    // Pore pressure
    public bool UsePorePressure { get; set; }
    public float PorePressure { get; set; } = 10f;
    public float BiotCoefficient { get; set; } = 0.8f;
    
    // Computational settings
    public bool UseGPU { get; set; } = true;
    public int MaxIterations { get; set; } = 1000;
    public float Tolerance { get; set; } = 1e-4f;
    
    // Damage evolution
    public bool EnableDamageEvolution { get; set; }
    public float DamageThreshold { get; set; } = 0.001f;
    public float DamageCriticalStrain { get; set; } = 0.01f;
}
```

**Output**:
```csharp
public class GeomechanicalResults
{
    // Stress/strain fields
    public float[,,] StressXX { get; set; }
    public float[,,] StrainXX { get; set; }
    // ... (6 components each)
    
    // Derived quantities
    public float[,,] Sigma1 { get; set; }
    public float[,,] FailureIndex { get; set; }  // 0-1, 1 = failure
    public byte[,,] DamageField { get; set; }
    public bool[,,] FractureField { get; set; }
    
    // Mohr circle analysis
    public List<MohrCircleData> MohrCircles { get; set; }
    
    // Statistics
    public float FailedVoxelPercentage { get; set; }
    public int TotalVoxels { get; set; }
    public int FailedVoxels { get; set; }
    public TimeSpan ComputationTime { get; set; }
}
```

**Solver Implementations**:
- **GeomechanicalSimulationCPU.cs**: ~2,000 lines - Multi-threaded FEM solver with SIMD acceleration
- **GeomechanicalSimulationGPU.cs**: ~3,000+ lines - OpenCL/Vulkan GPU implementation
- **Specialized variants**: Plasticity module, Damage module, Fluid-geothermal coupling

### 4.2 Geothermal Simulation

Located: `/Analysis/Geothermal/`

**Files**: 12 files, comprehensive heat flow and borehole analysis

**Capabilities**:
- Borehole heat exchanger simulation (U-tube, Coaxial)
- 3D heat diffusion solving
- Groundwater flow coupling
- Thermal breakthrough analysis
- Multi-borehole coupled simulations
- OpenCL GPU acceleration option

**Key Parameters**:
```csharp
public class GeothermalSimulationOptions
{
    // Heat exchanger design
    public HeatExchangerType HeatExchangerType { get; set; }  // UTube or Coaxial
    public double PipeInnerDiameter { get; set; } = 0.032;
    public double PipeOuterDiameter { get; set; } = 0.040;
    public double PipeSpacing { get; set; } = 0.080;
    
    // Fluid properties
    public double FluidMassFlowRate { get; set; } = 0.5;
    public double FluidInletTemperature { get; set; } = 278.15;  // Kelvin
    public double FluidSpecificHeat { get; set; } = 4186;
    
    // Domain setup
    public Vector3 GroundwaterVelocity { get; set; } = new(0, 0, 0);
    public double DomainRadius { get; set; } = 50;
    public int RadialGridPoints { get; set; } = 50;
    public int AngularGridPoints { get; set; } = 36;
    public int VerticalGridPoints { get; set; } = 100;
    
    // Simulation time
    public double SimulationTime { get; set; } = 31536000;  // 1 year in seconds
    public double TimeStep { get; set; } = 3600 * 6;  // 6 hours
}
```

**Solver Implementations**:
- **GeothermalSimulationSolver.cs**: ~3,500 lines - Main thermal FEM solver
- **GeothermalOpenCLSolver.cs**: GPU-accelerated version
- **BTESOpenCLSolver.cs**: Specialized for borehole thermal energy storage

**Visualization**:
- 3D isosurface rendering (temperature contours)
- Streamline visualization for flow
- Cross-section viewers

### 4.3 Thermodynamic Simulation

Located: `/Analysis/Thermodynamic/`

**Files**: 10+ files for phase equilibria, reactions, and mineral assemblages

**Key Components**:
- **CompoundLibrary.cs**: 60+ thermodynamic compounds with Gibbs energy data
- **ThermodynamicSolver.cs**: Phase equilibrium calculations
- **ReactionGenerator.cs**: Chemical reaction equations
- **PhaseDiagramGenerator.cs**: Auto-generated phase diagrams
- **ActivityCoefficientCalculator.cs**: Non-ideal solution modeling
- **WaterProperties.cs**: Temperature/pressure dependent water properties

**GeoScript Integration**:
- `FRACTIONATE_MAGMA` - Rayleigh fractionation modeling
- `CALCULATE_PHASES` - Phase separation (solid/aqueous/gas)
- `REACT` - Chemical reaction equilibrium

### 4.4 Acoustic/Seismic Analysis

Located: `/Analysis/AcousticSimulation/`

- Velocity model analysis
- Wave propagation simulation
- Frequency domain analysis
- Time-frequency representations

### 4.5 Other Analysis Modules

- **NMR Simulation** - Nuclear magnetic resonance T1/T2 relaxation
- **ThermalConductivity** - Rock thermal property estimation
- **PNM** - Pore network modeling and permeability
- **MaterialStatistics** - Material composition analysis

---

## 5. Application Architecture - MainWindow

Located: `/UI/MainWindow.cs`

**UI Layout Strategy**:
- ImGui docking system for flexible window management
- Main viewport area for visualization
- Persistent panels: Datasets, Properties, Tools, Log
- Pop-out viewers for different dataset types
- Modal dialogs for complex operations

**Key Responsibilities**:
1. **Project Management**: New, Open, Save projects
2. **Dataset Lifecycle**: Add/remove datasets, track modifications
3. **Undo/Redo**: Via ProjectManager and UndoManager
4. **Tool Dispatch**: Routes dataset to appropriate analysis tool
5. **Visualization Pipeline**: Manages multiple simultaneous viewers
6. **Auto-save & Backup**: Periodic project persistence
7. **Modal Dialogs**: Import data, export results, shape file creation

**Viewer Types**:
- `DatasetViewPanel` - Generic dataset viewer
- `CtVolume3DViewer` - 3D volumetric visualization
- `GISViewer` - Map-based visualization with layers
- `BoreholeViewer` - Well log display
- `TableViewer` - Tabular data display
- `Mesh3DViewer` - 3D geometry rendering
- `ThumbnailViewerPanel` - Image galleries

**Windows**:
- `SystemInfoWindow` - GPU/CPU/memory info
- `SettingsWindow` - Application preferences
- `MaterialLibraryWindow` - Rock/mineral database
- `StratigraphyCorrelationViewer` - Well correlation
- `GeoScriptTerminalWindow` - Script execution
- `Volume3DDebugWindow` - Shader/rendering debug

---

## 6. Business Logic Layer

Located: `/Business/`

### 6.1 ProjectManager (Singleton)

```csharp
public class ProjectManager
{
    public List<Dataset> LoadedDatasets { get; }
    public string ProjectName { get; set; }
    public string ProjectPath { get; set; }
    public bool HasUnsavedChanges { get; set; }
    public ProjectMetadata ProjectMetadata { get; set; }
    
    // Events
    public event Action<Dataset> DatasetRemoved;
    public event Action<Dataset> DatasetDataChanged;
}
```

**Responsibilities**:
- Central project state management
- Dataset lifecycle tracking
- Save/load project serialization
- Undo/redo coordination

### 6.2 GeoScript Engine

Located: `/Business/GeoScript.cs` (700+ lines)

**Architecture**:
- Custom parser with AST (Abstract Syntax Tree)
- Command registry pattern for extensibility
- Pipeline operator support (`|>`)
- Context-based execution with available datasets

**Command Categories**:
- Data transformation (SELECT, WHERE, GROUPBY)
- Geospatial operations (BUFFER, INTERSECT, UNION)
- Analysis (REACT, FRACTIONATE_MAGMA, CALCULATE_PHASES)
- Visualization (PLOT, HISTOGRAM)

**Example Usage**:
```geoscript
FRACTIONATE_MAGMA TYPE 'Basaltic' TEMP_RANGE 700-1200 C STEPS 50 FRACTIONAL
ELEMENTS 'Ni,Rb,Sr,La,Yb'
```

### 6.3 CompoundLibrary

Located: `/Business/CompoundLibrary.cs` + `/CompoundLibraryExtensions.cs`

**Database**:
- 60+ thermodynamic compounds with:
  - Chemical formula
  - Molar mass
  - Gibbs free energy data (ΔG°f)
  - Enthalpy (ΔH°f)
  - Entropy (S°)
  - Heat capacity coefficients (a, b, c, d)
  - Temperature/pressure dependence

**Sources**:
- Holland & Powell (2011) - Silicate minerals
- Robie & Hemingway (1995) - Carbonates, oxides, sulfates
- PHREEQC database - Aqueous species
- SUPCRT92 - Gas species

**User-defined Compounds**: Can add custom thermodynamic data

### 6.4 MaterialLibrary

Located: `/Business/MaterialLibrary.cs`

- Rock types with properties (density, elasticity, conductivity)
- Mineral assemblages
- Thermal and mechanical property database
- Integration with petrology system

### 6.5 Petrology System

Located: `/Business/Petrology/`

**Capabilities**:
- Mineral composition modeling
- Bowen's reaction series (automatic from thermodynamics)
- Partition coefficients database for trace elements
- Fractional vs. equilibrium crystallization

---

## 7. Data Loaders & Serialization

Located: `/Data/Loaders/` (13 loader classes)

### 7.1 Supported Input Formats

| Format | Class | Notes |
|--------|-------|-------|
| CT Image Stack | LASLoader, CtStackLoaderWrapper | 3D volumetric data |
| TIFF/Image | ImageLoader | Single 2D images |
| Shapefile | GISLoader | Vector GIS data |
| GeoJSON | GISLoader | Geographic features |
| KML/KMZ | GISLoader | Google Earth format |
| GeoTIFF | GISLoader | Raster GIS data |
| Borehole Binary | BoreholeBinaryLoader | Well log data |
| LAS Point Cloud | LASLoader | LiDAR data |
| Table/CSV | TableLoader | Tabular data |
| Mesh 3D | Mesh3DLoader | OBJ, STL format |
| 2D Geology | TwoDGeologyLoader | Cross-section data |
| Subsurface GIS | SubsurfaceGISLoader | 3D interpolated model |

### 7.2 Serialization Architecture

**DTO Pattern** (Data Transfer Objects):
- `Dataset` → `DatasetDTO` (for JSON serialization)
- All datasets implement `ISerializableDataset`
- Project files (.gtp) use JSON compression

**Project Format**:
- ZIP file containing metadata + serialized datasets
- Auto-recovery from crashes
- Versioning support for backward compatibility

---

## 8. UI Framework & Visualization

### 8.1 Graphics Stack

- **Veldrid**: Cross-platform graphics abstraction (D3D11, Metal, Vulkan)
- **ImGui.NET**: Immediate mode UI with docking
- **SkiaSharp**: Advanced 2D rendering
- **Metal/D3D/Vulkan bindings**: Platform-specific optimization

### 8.2 Rendering Implementations

- **D3D11MeshRenderer.cs** - Windows 3D geometry
- **MetalMeshRenderer.cs** - macOS Metal API
- **VulkanMeshRenderer.cs** - Linux/high-performance
- **CtVolume3DViewer.cs** - Volume ray casting
- **GeothermalVisualization3D.cs** - Isosurface + streamlines

### 8.3 UI Tool System

Located: `/UI/` (30+ files)

**Tool Categories**:
- **LayerManagerTool** - GIS layer control
- **GeoScriptEditorTool** - Script development
- **ExportTool** - Multi-format export
- **AnalysisTool** - Runs simulation workflows

**Dialog System**:
- `ImportDataModal` - Drag-drop or file browser
- `ProgressBarDialog` - Long operation feedback
- `ImGuiFileDialog` - File selection
- `ShapeFileCreationDialog` - Shapefile export wizard

---

## 9. Key Extensibility Points for Earthquake Simulator

### 9.1 Data Structures Already Available

1. **SubsurfaceGISDataset** - 3D voxel grid with lithology and parameters
2. **GeomechanicalResults** - Stress/strain/failure tensors
3. **GISDataset** - Geographic layer support for epicenter/fault mapping
4. **TableDataset** - Seismic event catalog storage

### 9.2 Simulation Framework Patterns

All simulations follow this pattern:

```csharp
public class EarthquakeSimulation
{
    // 1. Parameters class (user inputs)
    public class EarthquakeSimulationParameters
    {
        // Fault geometry, depth, magnitude, etc.
    }
    
    // 2. Results class (outputs)
    public class EarthquakeSimulationResults
    {
        // Ground motion, stress drop, slip distribution, etc.
    }
    
    // 3. CPU solver
    public class EarthquakeSimulationCPU
    {
        public EarthquakeSimulationResults Simulate(
            byte[,,] labels, 
            IProgress<float> progress, 
            CancellationToken token);
    }
    
    // 4. GPU solver (optional)
    public class EarthquakeSimulationGPU
    {
        // OpenCL implementation
    }
    
    // 5. UI integration
    public class EarthquakeSimulationUI : IDatasetTools
    {
        // ImGui controls for parameter input
    }
}
```

### 9.3 Integration Points

1. **Analysis Module Location**: `/Analysis/EarthquakeSimulation/` (new)
2. **Data Type**: Add `Earthquake` to `DatasetType` enum
3. **Dataset Class**: Create `EarthquakeDataset : Dataset`
4. **GeoScript Commands**: Add via `GeoScriptExtensions`
5. **UI Tools**: Register via `DatasetUIFactory`
6. **Loaders**: Add format support to `Loaders/`

### 9.4 Existing Tools to Leverage

- **Geomechanical solver**: Stress/strain analysis
- **CoordinateConverter**: Lat/long to local coordinates
- **SubsurfaceGISDataset**: Fault geometry interpolation
- **Visualization3D**: Display results
- **TableDataset**: Event catalog

---

## 10. Build System & Deployment

### 10.1 Project File Structure

- **GeoscientistToolkit.csproj** - Main application
- **AddInExtractor.csproj** - Plugin build system
- **Folder Structure**:
  - `/AddIns/Development/` - Plugin source
  - `/AddIns/CTSimulation/` - Example plugins
  - `/Shaders/` - GLSL shader code
  - `/Settings/` - Configuration schemas

### 10.2 Build Targets

```
Runtime Identifiers:
- osx-arm64 (Apple Silicon)
- osx-x64 (Intel macOS)
- win-x64 (Windows 64-bit)
- linux-x64 (Linux 64-bit)
```

### 10.3 Key Compilation Features

- Unsafe code blocks enabled (for pointer operations)
- Implicit usings enabled
- Null-safety disabled for compatibility
- Shader variant system for multi-backend support
- AddIn extraction in Release mode

---

## 11. Development Patterns & Best Practices

### 11.1 Logging

Located: `/Util/Logger.cs`

```csharp
Logger.Log(message);
Logger.LogWarning(message);
Logger.LogError(message);
```

### 11.2 Memory Management

- Explicit disposal for large arrays via `ArrayWrapper<T>`
- Streaming for huge datasets (`StreamingCtVolumeDataset`)
- GPU offloading support (`EnableOffloading` flag)

### 11.3 Multi-threading

- Thread-safe dataset operations
- CancellationToken support for long operations
- IProgress<float> for UI feedback
- Lock-based synchronization on shared state

### 11.4 Error Handling

- Try-catch in top-level (Application.cs, MainWindow.cs)
- Detailed error logging
- User-friendly error messages via UI
- Graceful degradation when features unavailable

---

## 12. Documentation & Examples

### 12.1 Markdown Docs

- `PETROLOGY_SYSTEM.md` - Fractional crystallization theory & usage
- `THERMODYNAMICS_ENHANCEMENTS.md` - Phase equilibria & new compounds

### 12.2 Example Projects

- `LASExample/` - Sample borehole data
- Built-in material library with 100+ rocks/minerals

---

## 13. Summary: Key Statistics

| Metric | Value |
|--------|-------|
| Total C# Files | 375 |
| Major Modules | 14 |
| Analysis Modules | 8+ |
| Supported Dataset Types | 14 |
| GIS Formats Supported | 5+ |
| Thermodynamic Compounds | 60+ |
| GPU Backends | 3 (D3D11, Metal, Vulkan) |
| Physics Solvers | 3+ (Geomechanics, Geothermal, Acoustic) |
| Programming Languages | C#, GLSL, OpenCL |

---

## 14. Recommendations for Earthquake Simulator Implementation

### 14.1 Core Components Needed

1. **SeismicStressState** - Track stress on faults
2. **FaultGeometry** - Fault plane definition + rupture segments
3. **EarthquakeHypocenter** - Nucleation point with moment release
4. **RuptureModel** - Shear crack dynamics (3D crack propagation)
5. **GroundMotionModel** - Attenuation relations (e.g., Akkar et al.)
6. **CatalogAnalysis** - Gutenberg-Richter relations, foreshock/aftershock

### 14.2 Data Integration

- Use **SubsurfaceGISDataset** for fault geometry
- Leverage **GeomechanicalResults** for stress fields
- Store events in **TableDataset** as earthquake catalog
- Visualize on **GISDataset** with epicenter/fault layers

### 14.3 Computation Strategy

- CPU solver: SIMD-optimized wave equation (2D/3D)
- GPU solver: OpenCL Lax-Wendroff or ADER-DG schemes
- Support both deterministic scenarios and stochastic catalogs

### 14.4 UI Integration Points

- **MainWindow**: Register earthquake viewer
- **GISTools**: Add seismic risk map tool
- **TableViewer**: Catalog browser
- **GeoScript**: Commands like `SIMULATE_EARTHQUAKE`, `COMPUTE_HAZARD`

---

## 15. Technology References

### Core References
- NetTopologySuite: https://github.com/NetTopologySuite/NetTopologySuite
- Veldrid: https://veldrid.dev/
- GDAL: https://gdal.org/
- ImGui: https://github.com/ocornut/imgui

### Geoscientific References
- Holland & Powell (2011) - Thermodynamic database
- Hoek & Brown (2019) - Rock failure criteria
- Gutenberg & Richter (1956) - Seismology fundamentals
- Bowen - Magmatic crystallization series

---

