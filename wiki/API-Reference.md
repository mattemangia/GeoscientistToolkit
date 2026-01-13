# API Reference

Documentation for the Geoscientist's Toolkit API for external automation and integration.

---

## Overview

The API provides:
- Verification simulation access
- Dataset loaders
- GeoScript execution
- Acoustic 3D velocity simulation
- Seismic cube construction and export
- External tool integration

---

## Getting Started

### Referencing the API

Add reference to the API DLL in your C# project:

```xml
<ItemGroup>
  <Reference Include="GeoscientistToolkit.Api">
    <HintPath>path/to/Api.dll</HintPath>
  </Reference>
</ItemGroup>
```

### Basic Usage

```csharp
using GeoscientistToolkit.Api;

// Initialize API
var api = new GtkApi();

// Load a dataset
var ctData = api.LoadCtStack("path/to/data.ctstack");

// Run analysis
var results = api.CalculatePorosity(ctData, "Pore");

// Export results
api.ExportResults(results, "output.csv");
```

---

## Core Classes

### GtkApi

Main entry point for API operations.

```csharp
public class GtkApi
{
    // Dataset loading
    public CtImageStackDataset LoadCtStack(string path);
    public BoreholeDataset LoadBorehole(string path);
    public SeismicDataset LoadSeismic(string path);
    public TableDataset LoadTable(string path);
    public GISDataset LoadGIS(string path);

    // Analysis
    public PorosityResult CalculatePorosity(CtImageStackDataset ct, string material);
    public PermeabilityResult CalculatePermeability(PNMDataset pnm, string direction);
    public GeothermalResult RunGeothermalSimulation(GeothermalParameters params);

    // GeoScript
    public Dataset ExecuteGeoScript(string script, Dataset input);

    // Export
    public void ExportResults(object results, string path, string format = "csv");
}
```

### SeismicCubeApi

Utilities for creating and exporting seismic cubes.

```csharp
public class SeismicCubeApi
{
    public SeismicCubeDataset CreateCube(string name, string surveyName = "", string projectName = "");
    public void AddLine(SeismicCubeDataset cube, SeismicDataset line, LineGeometry geometry);
    public void AddLineFromHeaders(SeismicCubeDataset cube, SeismicDataset line);
    public void AddPerpendicularLine(SeismicCubeDataset cube, SeismicDataset line, string baseLineId, int traceIndex);
    public void DetectIntersections(SeismicCubeDataset cube);
    public void ApplyNormalization(SeismicCubeDataset cube);
    public void BuildVolume(SeismicCubeDataset cube, int inlineCount, int crosslineCount, int sampleCount, float inlineSpacing, float crosslineSpacing, float sampleInterval);
    public Task ExportAsync(SeismicCubeDataset cube, string outputPath, SeismicCubeExportOptions options = null, IProgress<(float progress, string message)> progress = null);
    public Task<SeismicCubeDataset> ImportAsync(string inputPath, IProgress<(float progress, string message)> progress = null);
    public SubsurfaceGISDataset ExportToSubsurfaceGis(SeismicCubeDataset cube, string name);
    public GISRasterLayer ExportTimeSlice(SeismicCubeDataset cube, float timeMs, string name);
}
```

### LoaderApi

Dataset loaders for common file formats.

```csharp
public class LoaderApi
{
    public Task<Dataset> LoadSeismicAsync(string filePath, IProgress<(float progress, string message)> progress = null);
    public Task<Dataset> LoadSeismicCubeAsync(string filePath, IProgress<(float progress, string message)> progress = null);
    public Task<Dataset> LoadSubsurfaceGisAsync(string filePath, IProgress<(float progress, string message)> progress = null);
    public Task<Dataset> LoadTableAsync(string filePath, IProgress<(float progress, string message)> progress = null);
    // Additional loaders available for CT, PNM, acoustic volumes, and more.
}
```

### Dataset Classes

All datasets inherit from the base `Dataset` class:

```csharp
public abstract class Dataset
{
    public string Name { get; set; }
    public DatasetType Type { get; }
    public string FilePath { get; set; }
    public DatasetMetadata Metadata { get; set; }

    public abstract long GetSizeInBytes();
    public abstract void Load();
    public abstract void Unload();
}
```

---

## Dataset Types

### CtImageStackDataset

3D volumetric CT data.

```csharp
public class CtImageStackDataset : Dataset
{
    // Properties
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public float VoxelSize { get; set; }
    public byte[,,] VoxelData { get; }

    // Materials
    public List<Material> Materials { get; }
    public void AssignMaterial(int x, int y, int z, Material material);
    public Material GetMaterial(int x, int y, int z);

    // Operations
    public CtImageStackDataset Crop(int x, int y, int z, int w, int h, int d);
    public ImageDataset ExtractSlice(int index, Axis axis);
    public PNMDataset GeneratePNM(string poreMaterial);
    public Mesh3DDataset ExtractMesh(Material material, string algorithm);
}
```

### BoreholeDataset

Well log data.

```csharp
public class BoreholeDataset : Dataset
{
    public List<LithologyInterval> Lithologies { get; }
    public List<LogCurve> Curves { get; }
    public float TopDepth { get; }
    public float BottomDepth { get; }

    public float GetValue(string curveName, float depth);
    public void AddLithology(float top, float bottom, string name);
    public SeismicDataset GenerateSynthetic(float frequency);
}
```

### PNMDataset

Pore network model.

```csharp
public class PNMDataset : Dataset
{
    public List<Pore> Pores { get; }
    public List<Throat> Throats { get; }

    public PermeabilityResult CalculatePermeability(string direction);
    public DrainageResult SimulateDrainage(float contactAngle, float ift);
    public PNMDataset ExtractLargestCluster();
    public PNMStatistics GetStatistics();
}
```

---

## Analysis Functions

### Porosity Calculation

```csharp
var api = new GtkApi();
var ct = api.LoadCtStack("core.ctstack");

// Calculate porosity for 'Pore' material
var result = api.CalculatePorosity(ct, "Pore");

Console.WriteLine($"Porosity: {result.Porosity:P2}");
Console.WriteLine($"Pore Volume: {result.PoreVolume} mm³");
Console.WriteLine($"Total Volume: {result.TotalVolume} mm³");
```

### Permeability Calculation

```csharp
var pnm = api.LoadPNM("network.pnm");

// Calculate permeability in all directions
var result = api.CalculatePermeability(pnm, "all");

Console.WriteLine($"kx: {result.Kx:E3} m²");
Console.WriteLine($"ky: {result.Ky:E3} m²");
Console.WriteLine($"kz: {result.Kz:E3} m²");
```

### Geothermal Simulation

```csharp
var params = new GeothermalParameters
{
    BoreholeDepth = 100,
    InletTemperature = 5,
    FlowRate = 2.0,
    SimulationTime = TimeSpan.FromDays(365),
    TimeStep = TimeSpan.FromHours(1)
};

var result = api.RunGeothermalSimulation(params);

foreach (var point in result.TemperatureHistory)
{
    Console.WriteLine($"{point.Time}: {point.OutletTemp:F1}°C");
}
```

### Acoustic Simulation

```csharp
var velocityModel = api.LoadCtStack("velocity.ctstack");

var params = new AcousticParameters
{
    SourcePosition = new Vector3(50, 50, 0),
    Frequency = 30,  // Hz
    Duration = 1.0   // seconds
};

var result = api.RunAcousticSimulation(velocityModel, params);
api.ExportResults(result, "acoustic_output.segy", "segy");
```

---

## GeoScript Execution

### Running Scripts

```csharp
var api = new GtkApi();
var inputData = api.LoadTable("data.csv");

string script = @"
    SELECT WHERE 'Temperature' > 25 |>
    CALCULATE 'TempF' = 'Temperature' * 1.8 + 32 |>
    SORTBY 'TempF' DESC |>
    TAKE 10
";

var result = api.ExecuteGeoScript(script, inputData);
api.ExportResults(result, "filtered.csv");
```

### Script Files

```csharp
string scriptPath = "process.geoscript";
var result = api.ExecuteGeoScriptFile(scriptPath, inputData);
```

---

## Verification Simulations

### Running Verification Tests

```csharp
var verifier = new VerificationRunner();

// Run all verification tests
var results = verifier.RunAll();

// Run specific test
var beierResult = verifier.RunTest("Beier_TRT");

// Generate report
var report = verifier.GenerateReport(results);
Console.WriteLine(report);
```

### Available Verification Cases

| Case ID | Description | Module |
|---------|-------------|--------|
| Beier_TRT | Thermal response test | Geothermal |
| Lauwerier | Analytical heat transport | Geothermal |
| TOUGH2_1D | 1D flow comparison | Geothermal |
| PhreeqC_Calcite | Calcite equilibrium | Geochemistry |
| RocFall_Basic | Basic rockfall | Slope Stability |

---

## Export Functions

### Supported Formats

| Format | Extension | Description |
|--------|-----------|-------------|
| CSV | .csv | Comma-separated values |
| Excel | .xlsx | Microsoft Excel |
| JSON | .json | JavaScript Object Notation |
| XML | .xml | Extensible Markup Language |
| SEG-Y | .segy | Seismic data |
| LAS | .las | Well log data |
| OBJ | .obj | 3D mesh |
| STL | .stl | 3D mesh |

### Export Examples

```csharp
// Export to CSV
api.ExportResults(results, "output.csv", "csv");

// Export to Excel
api.ExportResults(results, "output.xlsx", "excel");

// Export mesh
api.ExportMesh(mesh, "output.obj", "obj");

// Export with options
api.ExportResults(results, "output.csv", "csv", new ExportOptions
{
    IncludeHeaders = true,
    DecimalPlaces = 4,
    Delimiter = ','
});
```

---

## Error Handling

### Exception Types

```csharp
try
{
    var ct = api.LoadCtStack("nonexistent.ctstack");
}
catch (DatasetNotFoundException ex)
{
    Console.WriteLine($"File not found: {ex.Path}");
}
catch (InvalidDatasetException ex)
{
    Console.WriteLine($"Invalid data: {ex.Message}");
}
catch (SimulationException ex)
{
    Console.WriteLine($"Simulation failed: {ex.Message}");
}
catch (GtkApiException ex)
{
    Console.WriteLine($"API error: {ex.Message}");
}
```

### Validation

```csharp
// Validate before processing
if (!api.ValidateDataset(ct))
{
    var errors = api.GetValidationErrors(ct);
    foreach (var error in errors)
    {
        Console.WriteLine($"Validation error: {error}");
    }
}
```

---

## Documentation Output

The API generates XML documentation files:

```
Api/
├── Api.dll
├── Api.xml          # XML documentation
└── Api.deps.json
```

Use with IntelliSense in Visual Studio for auto-completion and inline documentation.

## Geomechanical Simulation API

### Custom Gravity in REST API

The 2D geomechanical simulation endpoint supports custom gravity through the `Geomech2DSimulationRequest`:

```json
POST /api/simulation/geomech2d
{
    "meshJson": "...",
    "materialsJson": "...",
    "applyGravity": true,
    "gravityX": 0,
    "gravityY": -1.62,
    "gravityPreset": "moon"
}
```

**Gravity Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `gravityX` | float | X component of gravity (m/s²). Default: 0 |
| `gravityY` | float | Y component of gravity (m/s²). Default: -9.81 |
| `gravityPreset` | string | Planetary preset: earth, moon, mars, venus, jupiter, saturn, mercury |

**Planetary Presets:**

| Preset | Gravity (m/s²) |
|--------|---------------|
| earth | 9.81 |
| moon | 1.62 |
| mars | 3.72 |
| venus | 8.87 |
| jupiter | 24.79 |
| saturn | 10.44 |
| mercury | 3.70 |

### C# API for Custom Gravity

```csharp
// TwoDGeologyDataset gravity methods
dataset.SetGravity(0, -1.62f);           // Set gravity vector
dataset.SetGravityMagnitude(1.62f);       // Set magnitude (downward)
Vector2 gravity = dataset.GetGravity();   // Get current gravity

// Material gravity constant (affects UnitWeight, HydraulicConductivity)
GeomechanicalMaterial2D.GravityConstant = 1.62;

// Slope stability gravity
parameters.SetGravityMagnitude(1.62f);
parameters.Gravity = new Vector3(0, 0, -1.62f);
parameters.UseCustomGravityDirection = true;
```

### Material Assignment API

```csharp
// Assign material from library to formation
dataset.AssignMaterialToFormation("Sandstone Layer", "Sandstone (quartz-rich, dense)");

// Auto-assign all formations based on name matching
int assigned = dataset.AutoAssignMaterialsFromLibrary();

// Validate material assignments
List<string> issues = dataset.ValidateMaterialAssignments();

// Get available library materials
List<string> materials = dataset.GetAvailableLibraryMaterials();
```

---

## Related Pages

- [NodeEndpoint](NodeEndpoint.md) - REST API for distributed computing
- [GeoScript Manual](GeoScript-Manual.md) - Scripting language reference
- [Developer Guide](Developer-Guide.md) - Extension development
- [Home](Home.md) - Wiki home page
