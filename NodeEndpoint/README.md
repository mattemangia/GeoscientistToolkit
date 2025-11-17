# GeoscientistToolkit Node Endpoint

A REST API server that exposes the GeoscientistToolkit's simulation and CT processing capabilities through HTTP endpoints with keepalive connections for distributed computing.

## Overview

This node endpoint server provides:
- **HTTP REST API** for submitting simulations and CT operations
- **Keepalive connections** ensuring the node manager always sees active endpoints
- **Distributed computing** leveraging the existing NodeManager architecture
- **Async job processing** with result retrieval endpoints
- **Swagger/OpenAPI documentation** for easy integration

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

## Security Notes

This is a development/internal endpoint. For production use, consider adding:
- Authentication/Authorization (JWT, API keys)
- HTTPS/TLS encryption
- Rate limiting
- Input validation and sanitization
- Network security (firewall, VPN)

## License

Part of the GeoscientistToolkit project.
