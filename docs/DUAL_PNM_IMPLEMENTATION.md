# Dual Pore Network Model (DPNM) Implementation

## Overview

This document describes the implementation of Dual Pore Network Models in GeoscientistToolkit, based on the dual porosity approach from research by FOUBERT, DE BOEVER, and others for carbonate rock characterization.

## Scientific Background

### What is a Dual Pore Network Model?

A Dual Pore Network Model (DPNM) integrates pore space characterization at two distinct scales:

1. **Macro-pores**: Resolved from CT (Computed Tomography) scans at the micrometer scale
2. **Micro-pores**: Resolved from SEM (Scanning Electron Microscopy) images at the nanometer scale

This dual-scale approach is particularly important for carbonate rocks and other materials with bimodal pore size distributions, where a single-scale characterization would miss critical features.

### Key Research References

- **De Boever et al. (2012)**: "Quantification and prediction of the 3D pore network evolution in carbonate reservoir rocks" - Oil Gas Sci. Technol., 67(1), 161–178
- **Bauer et al. (2012)**: "Improving the estimations of petrophysical transport behavior of carbonate rocks using a dual pore network approach combined with computed microtomography" - Transp. Porous Med. 94(2), 505–524
- **De Boever et al. (2016)**: "Multiscale approach to (micro)porosity quantification in continental spring carbonate facies" - Geochem. Geophys. Geosyst.

### Coupling Modes

The implementation supports three coupling modes:

1. **Parallel**: Macropores and micropores conduct in parallel (separate flow paths)
   - Formula: `k_eff = k_macro + α * k_micro`
   - Use case: Independent macro and micro pore networks

2. **Series**: Micropores are embedded within macropores (series flow)
   - Formula: `1/k_eff = (1-α)/k_macro + α/k_micro`
   - Use case: Microporous cement within macropores

3. **Mass Transfer**: Advanced coupling with explicit mass exchange between scales
   - Formula: Weighted average with mass transfer coefficients
   - Use case: Complex dual-porosity systems with significant cross-scale transport

## Implementation Architecture

### Core Classes

#### 1. `DualPNMDataset` (`Data/PNM/DualPNMDataset.cs`)

Main dataset class that extends `PNMDataset` to include dual porosity features:

```csharp
public class DualPNMDataset : PNMDataset
{
    // Macro-scale network (inherited from PNMDataset)
    // Pores, Throats, VoxelSize, ImageWidth/Height/Depth

    // Micro-scale networks
    public List<MicroPoreNetwork> MicroNetworks { get; set; }
    public DualPNMCoupling Coupling { get; set; }

    // Source tracking
    public string CTDatasetPath { get; set; }
    public List<string> SEMImagePaths { get; set; }
}
```

Key features:
- Stores both macro and micro pore networks
- Calculates combined properties based on coupling mode
- Serializes/deserializes with DTOs
- Supports all standard PNM operations

#### 2. `MicroPoreNetwork` (`Data/PNM/DualPNMDataset.cs`)

Represents a micro-pore network associated with a specific macro-pore:

```csharp
public class MicroPoreNetwork
{
    public int MacroPoreID { get; set; }
    public List<Pore> MicroPores { get; set; }
    public List<Throat> MicroThroats { get; set; }
    public float MicroPorosity { get; set; }
    public float MicroPermeability { get; set; }
    public string SEMImagePath { get; set; }
    public float SEMPixelSize { get; set; }
}
```

#### 3. `DualPNMCoupling` (`Data/PNM/DualPNMDataset.cs`)

Manages the coupling between macro and micro scales:

```csharp
public class DualPNMCoupling
{
    public Dictionary<int, MicroPoreNetwork> MacroToMicroMap { get; set; }
    public float TotalMicroPorosity { get; set; }
    public float EffectiveMacroPermeability { get; set; }
    public float EffectiveMicroPermeability { get; set; }
    public float CombinedPermeability { get; set; }
    public DualPorosityCouplingMode CouplingMode { get; set; }
}
```

#### 4. `DualPNMGenerator` (`Analysis/PNM/DualPNMGenerator.cs`)

Interactive workflow tool for creating dual PNM datasets:

```csharp
public class DualPNMGeneratorTool
{
    // Step-by-step workflow:
    // 1. Select CT dataset
    // 2. Generate macro-PNM from CT
    // 3. Select SEM images
    // 4. Calibrate SEM scales
    // 5. Associate SEM images with macro-pores
    // 6. Generate micro-PNM from SEM
    // 7. Configure coupling mode
    // 8. Calculate combined properties
}
```

#### 5. `DualPNMSimulations` (`Analysis/PNM/DualPNMSimulations.cs`)

Simulation extensions for dual PNM:

- `CalculateDualPermeability()`: Calculates effective permeability at both scales
- `RunDualReactiveTransport()`: Reactive transport with dual porosity
- `CalculateDualDiffusivity()`: Molecular diffusivity accounting for both scales

### Data Transfer Objects (DTOs)

Located in `Data/DatasetDTO.cs`:

- `DualPNMDatasetDTO`: Serialization for dual PNM datasets
- `MicroPoreNetworkDTO`: Serialization for micro-networks
- `DualPNMCouplingDTO`: Serialization for coupling information

### Loader

`DualPNMLoader` (`Data/Loaders/DualPNMLoader.cs`):

- Loads dual PNM datasets from JSON files
- Supports `.dualpnm.json` and `.json` extensions
- Automatically deserializes both macro and micro networks

## Workflow

### Creating a Dual PNM from CT and SEM Data

1. **Load CT Data**:
   - Import CT image stack
   - Segment materials of interest

2. **Generate Macro-PNM**:
   - Use PNM Generator tool on CT data
   - Select material ID, generation mode, neighborhood type
   - Extract macro-scale pore network

3. **Load SEM Images**:
   - Import SEM images as single image datasets
   - These should be high-resolution images of micro-porosity

4. **Calibrate SEM Scales**:
   - Use scale calibration tool
   - Draw line on SEM scale bar
   - Enter known distance and unit
   - Calculates pixel size in micrometers

5. **Associate SEM with Macro-Pores**:
   - Specify which macro-pore each SEM image represents
   - This links micro-networks to macro-pores

6. **Generate Micro-PNM**:
   - Extract pore networks from SEM images
   - Calculate micro-scale properties (porosity, permeability)

7. **Configure Coupling**:
   - Select coupling mode (Parallel/Series/Mass Transfer)
   - Calculate combined properties
   - Finalize dual PNM dataset

8. **Export and Analyze**:
   - Export to JSON for archival
   - Run simulations (permeability, reactive transport, diffusivity)
   - Visualize in PNM Viewer

## Scale Calibration

The scale calibration tool (`Data/Image/ScaleCalibrationTool.cs`) enables accurate measurement from SEM images:

### Features:
- Interactive line drawing on images
- Unit conversion (nm, µm, mm, cm, m, km)
- Automatic pixel size calculation
- Stored with image dataset

### Usage:
1. Click "Calibrate" button on SEM image
2. Click two points on the scale bar
3. Enter known distance (e.g., "10" µm)
4. Select unit
5. Pixel size is automatically calculated and stored

## Simulations

### Permeability Calculation

```csharp
DualPNMSimulations.CalculateDualPermeability(
    dualPNM,
    PNMPermeabilityMethod.NavierStokes,
    pressureDiffPa: 100000.0f,
    viscosityPas: 0.001f
);
```

Results:
- Macro-permeability (from macro-network)
- Micro-permeability (averaged from micro-networks)
- Combined permeability (based on coupling mode)

### Reactive Transport

```csharp
var options = new PNMReactiveTransportOptions
{
    TotalTime = 3600.0f,
    TimeStep = 1.0f,
    // ... other parameters
};

DualPNMSimulations.RunDualReactiveTransport(dualPNM, options);
```

Features:
- Accounts for micro-porosity storage
- Adjusts time step for mass transfer
- Corrects dispersivity for dual porosity

### Diffusivity

```csharp
var diffOptions = new DiffusivityOptions
{
    BulkDiffusivity = 2.0e-9f,
    NumberOfWalkers = 1000,
    NumberOfSteps = 10000
};

var results = DualPNMSimulations.CalculateDualDiffusivity(dualPNM, diffOptions);
```

Results:
- Effective diffusivity (dual porosity corrected)
- Formation factor
- Transport tortuosity

## UI Integration

### Import Menu

The dual PNM import option has been added to the import modal:

- Menu item: "Dual Pore Network Model (Dual PNM)"
- File types: `.dualpnm.json`, `.json`
- Automatic detection of dual PNM format

### Generator Tool

Access via: *(To be integrated in a future update)*

The `DualPNMGeneratorTool` provides a step-by-step wizard for creating dual PNM datasets.

## File Format

Dual PNM datasets are saved as JSON files with the following structure:

```json
{
  "typeName": "DualPNMDataset",
  "name": "MyDualPNM",
  "voxelSize": 5.0,
  "pores": [...],
  "throats": [...],
  "microNetworks": [
    {
      "macroPoreID": 42,
      "microPores": [...],
      "microThroats": [...],
      "microPorosity": 0.15,
      "microPermeability": 0.1,
      "semPixelSize": 0.05,
      "semImagePath": "path/to/sem/image.tif"
    }
  ],
  "coupling": {
    "totalMicroPorosity": 0.08,
    "effectiveMacroPermeability": 125.5,
    "effectiveMicroPermeability": 0.12,
    "combinedPermeability": 133.7,
    "couplingMode": "Parallel"
  },
  "ctDatasetPath": "path/to/ct/dataset",
  "semImagePaths": [...]
}
```

## Best Practices

### When to Use Dual PNM

Use dual PNM when:
- Working with carbonate rocks or other bimodal pore systems
- CT resolution is insufficient to resolve all relevant porosity
- Micro-porosity significantly affects transport properties
- SEM images are available for micro-scale characterization

### Scale Selection

- **Macro-scale (CT)**: Typically 1-100 µm voxel size
- **Micro-scale (SEM)**: Typically 10-500 nm pixel size
- Ensure at least 1 order of magnitude separation between scales

### Coupling Mode Selection

- **Parallel**: When macro and micro pores are well-connected and independent
- **Series**: When micro-pores are within macro-pore walls (e.g., porous cement)
- **Mass Transfer**: When significant exchange occurs between scales

### Computational Considerations

- Dual PNM datasets are larger than standard PNM
- Micro-network generation can be time-consuming for many SEM images
- Simulations may take longer due to dual-scale coupling
- Consider starting with a subset of macro-pores for initial testing

## Limitations and Future Work

### Current Limitations

1. **Micro-PNM Generation**: Currently uses simplified placeholder generation. Full implementation would:
   - Segment SEM images
   - Extract actual pore network from segmentation
   - Calculate realistic micro-properties

2. **Visualization**: PNMViewer does not yet distinguish macro vs micro networks visually

3. **Mass Transfer**: Advanced mass transfer coupling is simplified. Future versions could include:
   - Explicit shape factors
   - Time-dependent exchange terms
   - Multi-rate mass transfer

### Future Enhancements

- [ ] Full SEM segmentation and network extraction
- [ ] Multi-resolution visualization in PNMViewer
- [ ] Advanced mass transfer models
- [ ] GPU acceleration for dual-scale simulations
- [ ] Automated SEM-to-macro-pore association using spatial correlation
- [ ] Support for more than 2 scales (hierarchical PNM)

## Example Use Cases

### 1. Carbonate Reservoir Characterization

**Scenario**: Characterizing a carbonate reservoir with both vuggy macro-porosity and microporous matrix.

**Workflow**:
1. µCT scan at 10 µm resolution captures vugs
2. SEM images at 100 nm resolution capture microporosity
3. Generate dual PNM
4. Use Series coupling (micropores in matrix between vugs)
5. Calculate effective permeability for flow simulation

**Result**: More accurate permeability prediction than single-scale approach.

### 2. Tight Sandstone Analysis

**Scenario**: Analyzing tight sandstone with nanopores and micropores.

**Workflow**:
1. CT scan shows larger pores (> 1 µm)
2. FIB-SEM reveals nanopore network
3. Generate dual PNM with Mass Transfer coupling
4. Run reactive transport to study mineral precipitation
5. Assess impact on permeability evolution

**Result**: Understanding of how micro-scale reactions affect macro-scale flow.

### 3. Shale Gas Characterization

**Scenario**: Characterizing organic-rich shale with organic and inorganic porosity.

**Workflow**:
1. CT identifies fractures and larger pores
2. High-res SEM captures organic matter nanopores
3. Dual PNM with Parallel coupling
4. Calculate dual diffusivity
5. Model gas diffusion and flow

**Result**: Improved gas production forecasting.

## Code Examples

### Creating a Dual PNM Programmatically

```csharp
// Create dual PNM dataset
var dualPNM = new DualPNMDataset("MyDualPNM", "output.dualpnm.json");

// Copy macro-PNM data from existing PNM
// (typically generated from CT data)
CopyMacroPNMToDual(macroPNM, dualPNM);

// Add micro-networks
foreach (var semImage in semImages)
{
    var microNet = GenerateMicroPNMFromSEM(semImage);
    dualPNM.AddMicroNetwork(semImage.MacroPoreID, microNet);
}

// Configure coupling
dualPNM.Coupling.CouplingMode = DualPorosityCouplingMode.Series;

// Calculate combined properties
dualPNM.CalculateCombinedProperties();

// Export
dualPNM.ExportToJson("output.dualpnm.json");

// Add to project
ProjectManager.Instance.AddDataset(dualPNM);
```

### Running Simulations

```csharp
// Load dual PNM
var loader = new DualPNMLoader();
var dualPNM = loader.Load("mydual.dualpnm.json") as DualPNMDataset;

// Calculate permeability
DualPNMSimulations.CalculateDualPermeability(
    dualPNM,
    PNMPermeabilityMethod.NavierStokes
);

Console.WriteLine($"Combined permeability: {dualPNM.Coupling.CombinedPermeability:F3} mD");

// Run reactive transport
var rtOptions = new PNMReactiveTransportOptions
{
    TotalTime = 3600.0f,
    TimeStep = 1.0f,
    InletPressurePa = 200000.0f,
    OutletPressurePa = 100000.0f
};

DualPNMSimulations.RunDualReactiveTransport(dualPNM, rtOptions);

// Get statistics
Console.WriteLine(dualPNM.GetStatisticsReport());
```

## Summary

The Dual PNM implementation provides a comprehensive framework for multi-scale pore network modeling, enabling accurate characterization and simulation of complex porous media. By integrating CT and SEM data, it captures both macro and micro-scale features critical for understanding transport properties in carbonate rocks, tight sandstones, shales, and other materials with bimodal porosity distributions.

## Contact & Support

For questions, issues, or contributions related to the dual PNM implementation, please refer to the main GeoscientistToolkit documentation.
