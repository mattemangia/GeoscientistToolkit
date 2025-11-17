# GeoscientistToolkit Node Endpoint

A production-ready REST API server that exposes the GeoscientistToolkit's simulation and CT processing capabilities through HTTP endpoints with an advanced Terminal User Interface (TUI) for monitoring and management.

## Overview

This node endpoint server provides:
- **Advanced Terminal UI (TUI)** - Production-ready monitoring dashboard with real-time metrics
- **HTTP REST API** for submitting simulations and CT operations
- **Configuration Management** - Live editing of settings with JSON validation
- **Job Monitoring** - Real-time tracking of job queue and execution
- **Performance Statistics** - Historical metrics with export capabilities
- **Keepalive connections** ensuring the node manager always sees active endpoints
- **Distributed computing** leveraging the existing NodeManager architecture
- **Async job processing** with result retrieval endpoints
- **Swagger/OpenAPI documentation** for easy integration

## Terminal User Interface (TUI)

The node endpoint includes a comprehensive TUI built with Terminal.Gui that provides:

### Dashboard Tab
- Real-time system information (platform, uptime, API endpoints)
- CPU and memory usage with progress bars
- Disk usage monitoring
- Network statistics (send/receive rates, total bandwidth)
- Active connections and discovered nodes
- Live network activity indicators (TX/RX with bandwidth display)

### Jobs Tab
- Job queue visualization with status icons (⧗ pending, ▶ running, ✓ completed, ✗ failed)
- Detailed job information view
- Job execution timing and duration
- JSON-formatted job details and results

### Logs Tab
- Real-time log viewer with up to 10,000 entries
- Live filtering/searching capabilities
- Color-coded log levels
- Export functionality

### Statistics Tab
- Historical performance metrics (last hour)
- CPU usage statistics (current, average, min, max)
- Memory usage trends
- Network activity totals
- Connection and job statistics
- Export to JSON

### Nodes Tab
- Connected nodes list with status
- Detailed node information (capabilities, resources, uptime)
- CPU cores, memory, GPU information
- Support job types per node

### Benchmark Tab
- CPU benchmark tool (matrix multiplication)
- Performance rating (GFLOPS calculation)
- Progress bar with real-time updates
- System information display

### Menu System

**File Menu:**
- Export Logs - Save logs to timestamped text file
- Export Statistics - Save metrics to JSON
- Export Configuration - Backup current settings
- Quit - Exit application

**View Menu:**
- Quick navigation to all tabs
- Refresh All - Update all data displays

**Tools Menu:**
- Run CPU Benchmark - Performance testing
- Clear Logs - Reset log viewer
- Clear Completed Jobs - Clean up job tracker
- Test Network Discovery - Send discovery broadcast
- Collect Garbage - Force GC

**Configuration Menu:**
- Edit Configuration - Live JSON editor with validation
- Reload Configuration - Restart with new settings
- Network Discovery Settings
- NodeManager Settings
- HTTP/API Settings
- Shared Storage Settings

**Services Menu:**
- Start/Stop Network Discovery
- Connect to Node - Manual node connection
- Disconnect Node - Remove node connection

**Help Menu:**
- Keyboard Shortcuts - Quick reference
- System Info - Detailed system information
- About - Version and feature information

### Keyboard Shortcuts

- **F1** - Show help
- **F5** - Run CPU benchmark
- **Ctrl+Q** - Quit application
- **Ctrl+R** - Refresh all data
- **Ctrl+E** - Edit configuration
- **Ctrl+F** - Focus log filter
- **Tab/Shift+Tab** - Navigate between tabs
- **Arrow Keys** - Navigate lists
- **Enter** - Select item

### Bottom Status Bar

- **KEEPALIVE** - Blinks to show application is running
- **TX** - Network transmit indicator (shows KB/s when active)
- **RX** - Network receive indicator (shows KB/s when active)
- **JOBS: X/Y** - Active jobs / total jobs (yellow when jobs are running)

### Configuration Editor

The TUI includes a built-in configuration editor with:
- Full JSON editing capabilities
- Syntax validation before saving
- Automatic backup creation
- Real-time error reporting
- Restart prompt for applying changes

## Architecture

The NodeEndpoint project is a nested ASP.NET Core Web API that:
1. References simulation files from the parent GeoscientistToolkit project (no duplication)
2. Wraps the NodeManager with HTTP endpoints
3. Provides job tracking and result retrieval
4. Supports long polling for async job completion

## Project Structure

```
NodeEndpoint/
├── NodeEndpoint.csproj          # Project file (references parent simulation files)
├── Program.cs                   # Entry point with Kestrel configuration
├── appsettings.json            # Configuration (ports, keepalive, etc.)
├── NodeEndpointService.cs      # NodeManager lifecycle management
├── JobTracker.cs               # Job submission and result tracking
└── Controllers/
    ├── SimulationController.cs  # Simulation endpoints
    ├── FilteringController.cs   # CT filtering/processing endpoints
    ├── JobController.cs        # Job management and result retrieval
    └── NodeController.cs       # Node status and health endpoints
```

## Building and Running

### Build

```bash
cd NodeEndpoint
dotnet build
```

### Run

```bash
dotnet run
```

The server will start on `http://localhost:5000` with:
- Keepalive timeout: 10 minutes
- Request timeout: 5 minutes
- Max concurrent connections: 100
- Max request body size: 1 GB (for large CT volumes)

## API Endpoints

### Node Management

#### GET /api/node
Get all connected nodes with keepalive status

#### GET /api/node/{nodeId}
Get specific node information

#### GET /api/node/status
Get NodeManager status

#### GET /api/node/keepalive
Keepalive endpoint - returns 200 OK if server is responsive

#### GET /api/node/health
Health check endpoint

### Job Management

#### GET /api/job
Get all jobs (optional ?status=pending|completed)

#### GET /api/job/{jobId}
Get job status

#### GET /api/job/{jobId}/result
Get job result (returns 202 Accepted if still running)

#### GET /api/job/{jobId}/wait?timeoutSeconds=300
Long polling - wait for job completion (default 5 minutes)

#### POST /api/job
Submit custom job

#### DELETE /api/job/{jobId}
Cancel job

### Simulations

#### POST /api/simulation/geomechanical
Submit geomechanical simulation (FEM with plasticity/damage)

```json
{
  "meshFile": "/path/to/mesh.msh",
  "materialProperties": {
    "youngModulus": 50e9,
    "poissonRatio": 0.25
  },
  "enablePlasticity": true,
  "enableDamage": false,
  "timeSteps": 100,
  "outputPath": "/path/to/output"
}
```

#### POST /api/simulation/acoustic
Submit acoustic simulation

```json
{
  "meshFile": "/path/to/mesh.msh",
  "frequency": 1000.0,
  "sourcePosition": [0, 0, 0],
  "useGPU": true,
  "outputPath": "/path/to/output"
}
```

#### POST /api/simulation/geothermal
Submit geothermal reservoir simulation

```json
{
  "meshFile": "/path/to/mesh.msh",
  "simulationTime": 86400.0,
  "timeStepSize": 60.0,
  "multiBoreholeMode": false,
  "outputPath": "/path/to/output"
}
```

#### POST /api/simulation/seismic
Submit earthquake/seismic simulation

```json
{
  "faultFriction": 0.6,
  "slipRate": 0.001,
  "duration": 100.0,
  "outputPath": "/path/to/output"
}
```

#### POST /api/simulation/nmr
Submit NMR pore-scale simulation

```json
{
  "echoTime": 0.001,
  "numberOfEchoes": 1000,
  "useOpenCL": true,
  "outputPath": "/path/to/output"
}
```

#### GET /api/simulation/types
Get available simulation types

### CT Filtering & Processing

#### POST /api/filtering/apply
Apply filter to CT volume

```json
{
  "volumePath": "/path/to/volume.raw",
  "filterType": "Gaussian",
  "kernelSize": 5,
  "sigma": 1.5,
  "iterations": 1,
  "useGPU": true,
  "is3D": true,
  "outputPath": "/path/to/filtered.raw"
}
```

#### POST /api/filtering/pipeline
Apply multiple filters in sequence

```json
{
  "volumePath": "/path/to/volume.raw",
  "filters": [
    { "filterType": "Gaussian", "kernelSize": 3, "sigma": 1.0 },
    { "filterType": "UnsharpMask", "kernelSize": 5, "sigma": 2.0 }
  ],
  "useGPU": true,
  "outputPath": "/path/to/filtered.raw"
}
```

#### POST /api/filtering/edge-detection
Apply edge detection

```json
{
  "volumePath": "/path/to/volume.raw",
  "method": "Canny",
  "threshold1": 100,
  "threshold2": 200,
  "useGPU": true,
  "outputPath": "/path/to/edges.raw"
}
```

#### POST /api/filtering/segmentation
Apply segmentation

```json
{
  "volumePath": "/path/to/volume.raw",
  "method": "Threshold",
  "thresholdValue": 128,
  "minSize": 100,
  "useGPU": true,
  "outputPath": "/path/to/segmented.raw"
}
```

#### GET /api/filtering/types
Get available filter types

## Keepalive Configuration

The server is configured with keepalive to ensure persistent connections:

- **Keepalive Timeout**: 10 minutes (configurable in appsettings.json)
- **Request Headers Timeout**: 5 minutes
- **Heartbeat Interval**: 30 seconds (NodeManager sends status updates)

This ensures that the NodeManager always sees the endpoints as active and can route jobs appropriately.

## Example Usage

### Submit a simulation and wait for results

```bash
# Submit job
curl -X POST http://localhost:5000/api/simulation/geomechanical \
  -H "Content-Type: application/json" \
  -d '{
    "meshFile": "/path/to/mesh.msh",
    "materialProperties": {"youngModulus": 50e9},
    "timeSteps": 100
  }'

# Response: {"jobId": "abc123...", "message": "...", "status": "pending"}

# Check status
curl http://localhost:5000/api/job/abc123

# Wait for completion (long polling)
curl http://localhost:5000/api/job/abc123/wait?timeoutSeconds=600

# Get result
curl http://localhost:5000/api/job/abc123/result
```

### Apply CT filter

```bash
curl -X POST http://localhost:5000/api/filtering/apply \
  -H "Content-Type: application/json" \
  -d '{
    "volumePath": "/data/ct_scan.raw",
    "filterType": "Gaussian",
    "kernelSize": 5,
    "sigma": 1.5,
    "useGPU": true
  }'
```

### Check node health

```bash
# Keepalive check
curl http://localhost:5000/api/node/keepalive

# Full health check
curl http://localhost:5000/api/node/health

# Get all connected nodes
curl http://localhost:5000/api/node
```

## Swagger Documentation

When running in development mode, Swagger UI is available at:
- http://localhost:5000/swagger

This provides interactive API documentation and testing.

## Configuration

Edit `appsettings.json` to configure:

```json
{
  "Kestrel": {
    "Limits": {
      "KeepAliveTimeout": "00:10:00",  // Keepalive timeout
      "MaxConcurrentConnections": 100   // Max concurrent connections
    }
  },
  "NodeManager": {
    "Role": "Hybrid",                   // Host, Worker, or Hybrid
    "ServerPort": 9876,                 // NodeManager internal port
    "HeartbeatInterval": 30,            // Seconds between heartbeats
    "UseNodesForSimulators": true,      // Enable distributed computing
    "UseGpuForJobs": true              // Enable GPU acceleration
  }
}
```

## Integration with Main Application

The NodeEndpoint references the parent project's simulation files without copying:

```xml
<Compile Include="..\Analysis\**\*.cs" LinkBase="Analysis" />
<Compile Include="..\Network\**\*.cs" LinkBase="Network" />
```

This means:
- No code duplication
- Changes to simulations automatically reflected in the endpoint
- Single source of truth for simulation logic

## Supported Job Types

### Simulations (CPU Path)
- **GeomechanicalSimulation** - FEM solver with plasticity and damage
- **AcousticSimulation** - Wave propagation (CPU/GPU)
- **GeothermalSimulation** - Thermal reservoir simulation
- **SeismicSimulation** - Earthquake and fault mechanics
- **NMRSimulation** - Pore-scale NMR (CPU/OpenCL)
- **TriaxialSimulation** - Triaxial compression/extension test with failure analysis

### PNM Operations
- **PNMGeneration** - Generate pore network model from CT scan
- **PermeabilityCalculation** - Calculate absolute permeability (Darcy's law)
- **DiffusivityCalculation** - Calculate molecular diffusivity (random walk)
- **PNMReactiveTransport** - Reactive transport with mineral dissolution/precipitation

### CT Operations
- **CTFiltering** - Apply filters (Gaussian, Median, Bilateral, etc.)
- **CTFilteringPipeline** - Multi-step filtering
- **CTEdgeDetection** - Sobel/Canny edge detection
- **CTSegmentation** - Threshold-based segmentation

All operations support GPU acceleration where available and automatically fall back to CPU if GPU is not available.

## Performance

- Async job processing prevents blocking
- Job cleanup after 1 hour for completed jobs
- Thread-safe job tracking with ConcurrentDictionary
- Efficient node load balancing via GetLoadScore()

## Advanced Features

### Data References (Efficient Large Dataset Handling)

Instead of transmitting large CT volumes or meshes over the network, use data references:

1. **Register Data** (one time):
```bash
curl -X POST http://localhost:5000/api/partitionedjob/register-data \
  -H "Content-Type: application/json" \
  -d '{
    "filePath": "/data/ct_scan_10GB.raw",
    "dataType": "CTVolume",
    "width": 2000,
    "height": 2000,
    "depth": 2000,
    "copyToSharedStorage": true
  }'
# Returns: {"referenceId": "a1b2c3d4e5f6", "sharedPath": "/tmp/GTK_SharedData/..."}
```

2. **Use Reference in Jobs**:
```bash
curl -X POST http://localhost:5000/api/partitionedjob/submit \
  -H "Content-Type: application/json" \
  -d '{
    "jobType": "CTFiltering",
    "dataReferenceId": "a1b2c3d4e5f6",
    "partitionStrategy": "SpatialZ",
    "partitionCount": 8,
    "parameters": {
      "filterType": "Gaussian",
      "kernelSize": 5
    }
  }'
```

**Benefits:**
- No network transmission of large files
- Shared storage accessible by all nodes
- Automatic cleanup of old references

### Job Partitioning (Multi-Node Parallelism)

Large jobs are automatically split across multiple worker nodes:

**Partitioning Strategies:**
- `SpatialZ`: Split along Z axis (depth slices) - best for volumetric CT data
- `SpatialXY`: Split in XY plane (tiles) - best for 2D image processing
- `SpatialOctree`: Octree-based spatial partitioning - best for irregular geometries
- `Temporal`: Split by time steps - best for time-series simulations
- `Random`: Random distribution - best for independent samples

**Result Aggregation:**
- `Concatenate`: Join results sequentially (e.g., volume slices)
- `Merge`: Merge results (e.g., simulation timesteps)
- `Sum`: Sum numerical results
- `Average`: Average numerical results
- `Custom`: Return raw results for custom aggregation

**Example - Process 10GB CT Volume Across 8 Nodes:**
```bash
# 1. Register the large CT volume
curl -X POST http://localhost:5000/api/partitionedjob/register-data \
  -d '{
    "filePath": "/data/large_ct.raw",
    "dataType": "CTVolume",
    "width": 2048, "height": 2048, "depth": 2048
  }'
# Returns: {"referenceId": "abc123"}

# 2. Submit partitioned filtering job
curl -X POST http://localhost:5000/api/partitionedjob/submit \
  -d '{
    "jobType": "CTFiltering",
    "dataReferenceId": "abc123",
    "partitionStrategy": "SpatialZ",
    "partitionCount": 8,
    "aggregationStrategy": "Concatenate",
    "parameters": {
      "filterType": "Gaussian",
      "sigma": 1.5
    }
  }'
# Returns: {"parentJobId": "xyz789"}

# 3. Check progress
curl http://localhost:5000/api/partitionedjob/xyz789/status
# Returns: {"completedPartitions": 5, "totalPartitions": 8, "progress": 62.5}

# 4. Get aggregated result
curl http://localhost:5000/api/partitionedjob/xyz789/result
```

**Performance Gains:**
- 8 nodes = ~8x speedup for embarrassingly parallel jobs
- Automatic load balancing (2x oversubscription)
- Fault tolerance (individual partitions can be retried)

### PNM Endpoints

#### Generate PNM from CT
```bash
curl -X POST http://localhost:5000/api/pnm/generate \
  -d '{
    "ctVolumePath": "/data/ct_scan.raw",
    "materialId": 1,
    "neighborhood": "N26",
    "generationMode": "Conservative",
    "enforceInletOutletConnectivity": true,
    "flowAxis": "Z"
  }'
```

#### Calculate Permeability
```bash
curl -X POST http://localhost:5000/api/pnm/permeability \
  -d '{
    "pnmDatasetPath": "/data/pnm.json",
    "method": "DirectSolver",
    "flowAxis": "Z",
    "pressureDifference_Pa": 100000,
    "fluidViscosity_Pas": 0.001
  }'
```

### Triaxial Simulation Endpoint

```bash
curl -X POST http://localhost:5000/api/simulation/triaxial \
  -d '{
    "sampleHeight": 0.1,
    "sampleDiameter": 0.05,
    "confiningPressure_MPa": 10.0,
    "loadingMode": "StrainControlled",
    "axialStrainRate_per_s": 1e-5,
    "maxAxialStrain_percent": 5.0,
    "drainageCondition": "Drained"
  }'
```

## Security Notes

This is a development/internal endpoint. For production use, consider adding:
- Authentication/Authorization (JWT, API keys)
- HTTPS/TLS encryption
- Rate limiting
- Input validation and sanitization
- Network security (firewall, VPN)

## License

Part of the GeoscientistToolkit project.
