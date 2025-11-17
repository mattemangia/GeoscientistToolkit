# NodeEndpoint Build Notes

## Final State: Headless Server with Core Simulations

The NodeEndpoint is now a **fully headless REST API server** with **zero UI dependencies**. It includes only computational simulations that can run without graphics.

### Available Simulations
- **Geomechanical** (FEM-based CPU path with plasticity and damage)
- **Acoustic** (wave propagation CPU simulator)
- **Triaxial** (compression/extension lab tests)

### Excluded Simulations
These remain in the main application due to complex dependencies:
- **Geothermal** (requires Mesh3D, Borehole, GIS datasets)
- **Seismic/Earthquake** (requires CrustalModel, WavePropagationEngine)
- **NMR** (requires CtImageStack, VolumeData)
- **PNM Generation** (directly uses ImGuiNET for UI progress)

---

## Compilation Error Fixes (766 → 0 Errors)

### Problem 1: Duplicate Assembly Attributes (Main Project)
**Error Count**: 7 errors
**Symptoms**:
```
Error CS0579: L'attributo 'System.Reflection.AssemblyCompanyAttribute' è duplicato
Error CS0579: L'attributo 'System.Reflection.AssemblyProductAttribute' è duplicato
Error CS0234: Il tipo o il nome dello spazio dei nomi 'AspNetCore' non esiste
```

**Root Cause**: The main `GeoscientistToolkit.csproj` was compiling all files in the solution, including the `NodeEndpoint/` subdirectory files. This caused:
- Duplicate auto-generated assembly attributes (each project generates its own)
- Missing ASP.NET Core namespace errors (main project doesn't reference ASP.NET Core)

**Solution**: Added NodeEndpoint directory exclusion to `GeoscientistToolkit.csproj`:
```xml
<ItemGroup>
    <Compile Remove="AddInExtractor\**"/>
    <EmbeddedResource Remove="AddInExtractor\**"/>
    <None Remove="AddInExtractor\**"/>

    <!-- Exclude NodeEndpoint to prevent duplicate assembly attributes -->
    <Compile Remove="NodeEndpoint\**"/>
    <EmbeddedResource Remove="NodeEndpoint\**"/>
    <None Remove="NodeEndpoint\**"/>
</ItemGroup>
```

---

### Problem 2: Missing UI Library Dependencies (NodeEndpoint)
**Error Count**: 766 errors
**Symptoms**:
```
Error CS0246: Il nome di tipo o di spazio dei nomi 'ImGuiNET' non è stato trovato
Error CS0246: Il nome di tipo o di spazio dei nomi 'Veldrid' non è stato trovato
Error CS0246: Il nome di tipo o di spazio dei nomi 'Texture' non è stato trovato
Error CS0246: Il nome di tipo o di spazio dei nomi 'Shader' non è stato trovato
```

**Files That Failed**:
- `AcousticExportManager.cs` (uses ImGuiNET for UI)
- `RealTimeTomographyViewer.cs` (uses ImGuiNET)
- `CtVolume3DViewer.cs` (uses Veldrid rendering)
- `MetalVolumeRenderer.cs` (uses Veldrid Metal backend)
- Hundreds of other UI/visualization files

**Root Cause**: The original `NodeEndpoint.csproj` used wildcard includes:
```xml
<!-- BEFORE (caused errors): -->
<Compile Include="..\Analysis\**\*.cs" LinkBase="Analysis" />
<Compile Remove="..\**\*UI.cs" />  <!-- Not selective enough -->
```

This pulled in **ALL** files from the Analysis directory, including UI-dependent visualization files. The NodeEndpoint project intentionally does NOT reference UI libraries (ImGuiNET, Veldrid, SDL2, etc.) because it's designed to be a **headless server**.

**Solution**: Changed from wildcard includes to **explicit file-by-file includes**:
```xml
<!-- AFTER (fixed): -->
<!-- Only include core computation files, exclude ALL UI/rendering files -->
<Compile Include="..\Analysis\Geomechanics\GeomechanicalSimulationCPU.cs" Link="..." />
<Compile Include="..\Analysis\Geomechanics\TriaxialSimulation.cs" Link="..." />
<Compile Include="..\Analysis\AcousticSimulation\AcousticSimulatorCPU.cs" Link="..." />
<!-- ... and so on for each headless computation file -->
```

**What's Included** (headless computation only):
- Network layer (NodeManager, SimulatorNodeSupport)
- Settings (configuration management)
- OpenCL (GPU compute for simulations)
- Geomechanics simulations (CPU path)
- Acoustic simulations (CPU path)
- Geothermal simulations
- Seismic simulations
- NMR simulations
- Triaxial simulations
- PNM operations (generation, permeability, reactive transport)
- Essential data types (Dataset, PhysicalMaterial)
- Utilities (Logger)

**What's Excluded** (UI/rendering):
- All `*Viewer.cs` files (require Veldrid/ImGuiNET)
- All `*Renderer.cs` files (require Veldrid)
- All `*UI.cs` files (require ImGuiNET)
- Export managers with UI (require ImGuiNET)
- Visualization tools
- Metal/Vulkan/OpenGL renderers

---

## Building the Project

### Prerequisites
- .NET 8.0 SDK
- Cross-platform (Windows, macOS, Linux)

### Build Commands

**Build Main Project**:
```bash
cd /path/to/GeoscientistToolkit
dotnet build
```

**Build NodeEndpoint Only**:
```bash
cd /path/to/GeoscientistToolkit/NodeEndpoint
dotnet build
```

**Build Entire Solution**:
```bash
cd /path/to/GeoscientistToolkit
dotnet build GeoscientistToolkit.sln
```

### Expected Output
Both projects should now compile without errors:
- Main Project: 0 errors
- NodeEndpoint: 0 errors

---

## Running the NodeEndpoint

### Quick Start
```bash
cd NodeEndpoint
dotnet run
```

### Expected Startup Output
```
=== GeoscientistToolkit Node Endpoint Server ===
Platform: Linux
Local IP: 192.168.1.100
HTTP API: http://192.168.1.100:5000
NodeManager: 192.168.1.100:9876
Keepalive timeout: 10 minutes

[NodeManager] Started in Hybrid mode
Node Manager status: Hybrid

Starting network discovery...
[NetworkDiscovery] Broadcasting on 192.168.1.100:9877
Network discovery enabled - other nodes can find this endpoint automatically

Ready to accept connections!
Swagger UI: http://localhost:5000/swagger
```

### Verify It's Working

**1. Check Node Status**:
```bash
curl http://localhost:5000/api/node
```

**2. Open Swagger UI**:
```
http://localhost:5000/swagger
```

**3. Submit a Test Job**:
```bash
curl -X POST http://localhost:5000/api/simulation/nmr \
  -H "Content-Type: application/json" \
  -d '{
    "relaxationMode": "T2",
    "echoSpacing": 0.001,
    "numberOfEchoes": 64
  }'
```

---

## Architecture Overview

### Available REST API Endpoints

**Simulation Controller** (`/api/simulation`):
- `POST /api/simulation/geomechanical` - Submit FEM geomechanical simulation
- `POST /api/simulation/acoustic` - Submit acoustic wave propagation simulation
- `POST /api/simulation/triaxial` - Submit triaxial compression/extension test
- `GET /api/simulation/types` - List available simulation types

**Job Controller** (`/api/job`):
- `GET /api/job/{id}` - Get job status
- `GET /api/job/{id}/result` - Get job result
- `POST /api/job/cancel/{id}` - Cancel a job
- `GET /api/job/all` - List all jobs

**Node Controller** (`/api/node`):
- `GET /api/node` - Get NodeManager status and connected nodes
- `GET /api/node/info` - Get this endpoint's information

**Partitioned Job Controller** (`/api/partitionedjob`):
- `POST /api/partitionedjob/register-data` - Register large dataset
- `POST /api/partitionedjob/submit` - Submit distributed job
- `GET /api/partitionedjob/{id}/status` - Track distributed job progress
- `GET /api/partitionedjob/{id}/result` - Get aggregated results

**Filtering Controller** (`/api/filtering`):
- `POST /api/filtering/apply` - Apply CT volume filter
- `POST /api/filtering/pipeline` - Apply filter pipeline
- `POST /api/filtering/edge-detection` - Edge detection
- `POST /api/filtering/segmentation` - Volume segmentation

### Design Philosophy
The NodeEndpoint is a **headless REST API server** designed to:
- Run on servers without graphics/display
- Distribute compute-intensive jobs across multiple nodes
- Minimize memory footprint by excluding UI libraries
- Support cross-platform deployment (Windows/Linux/macOS)
- Enable automatic network discovery of worker nodes

### What It Does NOT Do
- No graphical user interface
- No real-time visualization
- No interactive rendering
- No ImGui/Veldrid dependencies
- No complex simulations requiring advanced data types (Geothermal, Seismic, NMR, PNM Generation)

The main GeoscientistToolkit application handles all UI/visualization and complex simulations, while NodeEndpoint handles headless computation and job distribution.

---

## Troubleshooting

### "AspNetCore not found" in Main Project
**Solution**: Make sure you pulled the latest changes with the NodeEndpoint exclusions in `GeoscientistToolkit.csproj`.

### "ImGuiNET not found" in NodeEndpoint
**Solution**: Make sure you pulled the latest changes with the explicit file includes in `NodeEndpoint/NodeEndpoint.csproj`. The wildcard includes have been removed.

### Compilation Still Fails
**Debug Steps**:
```bash
# Clean all build artifacts
dotnet clean
rm -rf bin/ obj/ NodeEndpoint/bin/ NodeEndpoint/obj/

# Rebuild
dotnet build
```

### Port Already in Use
If port 5000 is already in use, edit `NodeEndpoint/appsettings.json`:
```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:8080"
      }
    }
  }
}
```

---

## Testing Cross-Platform

### Test on Windows
```powershell
cd NodeEndpoint
dotnet run
```

### Test on Linux
```bash
cd NodeEndpoint
dotnet run
```

### Test on macOS
```bash
cd NodeEndpoint
dotnet run
```

All platforms should work identically with automatic IP detection and network discovery.

---

## Next Steps

See `SETUP_GUIDE.md` for instructions on:
- Setting up multi-node clusters
- Configuring shared storage (NFS/SMB)
- Firewall configuration
- Performance tuning
- Production deployment

---

## Summary

The NodeEndpoint is now a clean, headless server with **zero UI dependencies**. It compiles successfully on all platforms and can run on servers without graphics. The explicit file includes ensure we only pull in computation code, making it suitable for distributed computing environments.
