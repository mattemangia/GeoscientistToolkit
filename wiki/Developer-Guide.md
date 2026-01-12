# Developer Guide

Guide for developers extending and contributing to Geoscientist's Toolkit.

---

## Overview

This guide covers:
- Architecture overview
- Adding new dataset types
- Creating analysis modules
- GeoScript command development
- UI extension
- Testing and validation

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
│   ├── Borehole/             # Well log data
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
├── NodeEndpoint/              # Network service
├── ONNX/                      # Model assets and runtime helpers
├── OpenCL/                    # GPU kernels
├── Scripting/                 # GeoScript language runtime + commands
├── Settings/                  # Configuration
├── Shaders/                   # Graphics shaders
├── Tests/                     # Unit/integration tests
├── Tools/                     # Cross-dataset integration
├── UI/                        # ImGui user interface components
├── Util/                      # Utilities
├── VerificationTests/         # Verification test harness
├── docs/                      # End-user documentation
└── wiki/                      # Developer documentation
```

### Key Design Patterns

| Pattern | Usage | Example |
|---------|-------|---------|
| Singleton | Project state | `ProjectManager` |
| Factory | UI component creation | `DatasetUIFactory` |
| Strategy | Dataset handling | `IDatasetViewer` |
| Observer | Update notifications | Dataset change events |

### Technology Stack

| Component | Technology |
|-----------|-----------|
| UI Framework | ImGui.NET |
| Graphics | Veldrid |
| 3D Rendering | OpenGL/Vulkan/DirectX |
| AI/ML | ONNX Runtime |
| Scientific | MathNet.Numerics |
| GIS | GDAL, NetTopologySuite |
| GPU Compute | Silk.NET.OpenCL |

---

## Adding Dataset Types

### Step 1: Define Dataset Class

Create new class in `Data/` directory:

```csharp
// Data/MyData/MyDataset.cs
public class MyDataset : Dataset
{
    public MyDataset(string name, string filePath) : base(name, filePath)
    {
        Type = DatasetType.MyData;
    }

    // Dataset-specific properties
    public int Width { get; set; }
    public int Height { get; set; }
    public float[] Values { get; set; }

    public override long GetSizeInBytes()
    {
        return Values?.Length * sizeof(float) ?? 0;
    }

    public override void Load()
    {
        // Load from file
    }

    public override void Unload()
    {
        Values = null;
    }
}
```

### Step 2: Add DatasetType Enum

Update `Data/Dataset.cs`:

```csharp
public enum DatasetType
{
    // ... existing types ...
    MyData,  // Add new type
}
```

### Step 3: Create Viewer

```csharp
// UI/Viewers/MyDataViewer.cs
public class MyDataViewer : IDatasetViewer
{
    private MyDataset _dataset;

    public MyDataViewer(MyDataset dataset)
    {
        _dataset = dataset;
    }

    public void Draw()
    {
        // ImGui rendering code
        ImGui.Text($"Dataset: {_dataset.Name}");
        ImGui.Text($"Size: {_dataset.Width} x {_dataset.Height}");

        // Custom visualization
    }
}
```

### Step 4: Register in Factory

Update `UI/DatasetUIFactory.cs`:

```csharp
public static IDatasetViewer CreateViewer(Dataset dataset)
{
    switch (dataset.Type)
    {
        // ... existing cases ...
        case DatasetType.MyData:
            return new MyDataViewer(dataset as MyDataset);
    }
}
```

### Step 5: Add File Loader

```csharp
// Data/MyData/MyDataLoader.cs
public static class MyDataLoader
{
    public static MyDataset Load(string path)
    {
        // Parse file and create dataset
        var dataset = new MyDataset(Path.GetFileName(path), path);
        // ... load data ...
        return dataset;
    }
}
```

---

## Creating Analysis Modules

### Module Structure

```
Analysis/MyAnalysis/
├── MyAnalysisParameters.cs   # Input configuration
├── MyAnalysisResults.cs      # Output data
├── MyAnalysisCPU.cs          # CPU implementation
├── MyAnalysisGPU.cs          # GPU implementation (optional)
└── MyAnalysisUI.cs           # ImGui interface
```

### Parameters Class

```csharp
public class MyAnalysisParameters
{
    public float Temperature { get; set; } = 25.0f;
    public float Pressure { get; set; } = 1.0f;
    public int Iterations { get; set; } = 1000;
    public bool UseGPU { get; set; } = false;
}
```

### Results Class

```csharp
public class MyAnalysisResults
{
    public float[,,] OutputField { get; set; }
    public float FinalValue { get; set; }
    public TimeSpan ComputeTime { get; set; }
    public string Status { get; set; }
}
```

### Solver Implementation

```csharp
public class MyAnalysisCPU
{
    public MyAnalysisResults Run(Dataset input, MyAnalysisParameters parameters)
    {
        var results = new MyAnalysisResults();
        var stopwatch = Stopwatch.StartNew();

        // Analysis implementation
        for (int i = 0; i < parameters.Iterations; i++)
        {
            // Compute step
        }

        stopwatch.Stop();
        results.ComputeTime = stopwatch.Elapsed;
        results.Status = "Completed";

        return results;
    }
}
```

### UI Integration

```csharp
public class MyAnalysisUI : IDatasetTool
{
    private MyAnalysisParameters _params = new();

    public void Draw(Dataset dataset)
    {
        ImGui.Text("My Analysis");

        ImGui.SliderFloat("Temperature", ref _params.Temperature, 0, 100);
        ImGui.SliderFloat("Pressure", ref _params.Pressure, 0.1f, 10);
        ImGui.InputInt("Iterations", ref _params.Iterations);
        ImGui.Checkbox("Use GPU", ref _params.UseGPU);

        if (ImGui.Button("Run Analysis"))
        {
            var solver = new MyAnalysisCPU();
            var results = solver.Run(dataset, _params);
            // Handle results
        }
    }
}
```

---

## GeoScript Commands

### Creating a Command

```csharp
// Business/GeoScript/Commands/MyCommand.cs
public class MyCommand : IGeoScriptCommand
{
    public string Name => "MY_OPERATION";
    public string HelpText => "Performs my custom operation";
    public string Usage => "MY_OPERATION param1=value param2=value";

    public async Task<Dataset> ExecuteAsync(GeoScriptContext context, AstNode node)
    {
        // Parse parameters
        var param1 = node.GetParameter<float>("param1", 1.0f);
        var param2 = node.GetParameter<string>("param2", "default");

        // Get input dataset
        var input = context.InputDataset;

        // Perform operation
        var output = ProcessData(input, param1, param2);

        return output;
    }

    private Dataset ProcessData(Dataset input, float p1, string p2)
    {
        // Implementation
    }
}
```

### Registering Command

```csharp
// Business/GeoScript/OperationRegistry.cs
public void RegisterCommands()
{
    // ... existing registrations ...
    Register(new MyCommand(), DatasetType.Image, DatasetType.CtImageStack);
}
```

### Testing

```csharp
[TestMethod]
public async Task MyCommand_BasicOperation_ReturnsExpectedResult()
{
    var engine = new GeoScriptEngine();
    var input = CreateTestDataset();

    var result = await engine.ExecuteAsync(
        "MY_OPERATION param1=2.0 param2=test",
        input,
        new Dictionary<string, Dataset>()
    );

    Assert.IsNotNull(result);
    // Additional assertions
}
```

---

## UI Extension

### Custom Window

```csharp
public class MyWindow : IWindow
{
    private bool _isOpen = true;

    public void Draw()
    {
        if (!_isOpen) return;

        if (ImGui.Begin("My Window", ref _isOpen))
        {
            ImGui.Text("Window content");

            if (ImGui.Button("Do Something"))
            {
                // Action
            }
        }
        ImGui.End();
    }
}
```

### Menu Integration

```csharp
// In MainWindow.DrawMenuBar()
if (ImGui.BeginMenu("Tools"))
{
    if (ImGui.MenuItem("My Tool"))
    {
        _myWindow.Open();
    }
    ImGui.EndMenu();
}
```

### Custom Viewer

```csharp
public class MyViewer : IDatasetViewer
{
    private readonly MyDataset _dataset;
    private Texture _texture;

    public void Draw()
    {
        // Render to texture
        UpdateTexture();

        // Display in ImGui
        var size = ImGui.GetContentRegionAvail();
        ImGui.Image(_texture.Handle, size);

        // Handle input
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(0))
        {
            var pos = ImGui.GetMousePos();
            HandleClick(pos);
        }
    }
}
```

---

## Testing

### Unit Tests

```csharp
[TestClass]
public class MyAnalysisTests
{
    [TestMethod]
    public void MyAnalysis_ValidInput_ReturnsCorrectResult()
    {
        var input = CreateTestData();
        var params = new MyAnalysisParameters { Temperature = 25 };
        var solver = new MyAnalysisCPU();

        var result = solver.Run(input, params);

        Assert.AreEqual("Completed", result.Status);
        Assert.IsTrue(result.FinalValue > 0);
    }
}
```

### Verification Tests

Create verification test against known solutions:

```csharp
[TestClass]
public class MyAnalysisVerification
{
    [TestMethod]
    public void AnalyticalSolution_Comparison()
    {
        // Setup
        var input = CreateAnalyticalTestCase();
        var expected = CalculateAnalyticalSolution(input);

        // Run
        var solver = new MyAnalysisCPU();
        var result = solver.Run(input, new MyAnalysisParameters());

        // Compare
        var error = CalculateRelativeError(expected, result.FinalValue);
        Assert.IsTrue(error < 0.05, $"Error {error:P2} exceeds 5% threshold");
    }
}
```

### Integration Tests

```csharp
[TestClass]
public class IntegrationTests
{
    [TestMethod]
    public async Task FullWorkflow_LoadProcessExport()
    {
        // Load
        var dataset = MyDataLoader.Load("test_data.dat");

        // Process
        var engine = new GeoScriptEngine();
        var processed = await engine.ExecuteAsync(
            "MY_OPERATION param1=2.0",
            dataset,
            new Dictionary<string, Dataset>()
        );

        // Export
        var exporter = new DataExporter();
        exporter.Export(processed, "output.csv", "csv");

        // Verify
        Assert.IsTrue(File.Exists("output.csv"));
    }
}
```

---

## Contributing

### Development Workflow

1. Fork repository
2. Create feature branch
3. Implement changes
4. Write tests
5. Update documentation
6. Submit pull request

### Code Style

- Follow existing code conventions
- Use meaningful names
- Add XML documentation comments
- Keep methods focused and small

### Pull Request Guidelines

- Describe changes clearly
- Reference related issues
- Include tests for new features
- Update documentation as needed
- Ensure CI passes

---

## Related Pages

- [API Reference](API-Reference.md) - API documentation
- [Verification and Testing](Verification-and-Testing.md) - Test cases
- [GeoScript Manual](GeoScript-Manual.md) - Scripting reference
- [Home](Home.md) - Wiki home page
