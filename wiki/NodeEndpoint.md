# NodeEndpoint

<div align="center">
<img src="../NodeEndpoint.png" alt="NodeEndpoint" width="400"/>
</div>

Documentation for the NodeEndpoint network service for distributed computing.

---

## Overview

NodeEndpoint provides:
- Advanced Terminal UI (TUI)
- HTTP REST API on port 8500
- Configuration management
- Job monitoring
- Performance statistics
- Distributed computing support

---

## Features

### Terminal UI

Interactive dashboard with tabs:
- **Dashboard**: System overview
- **Jobs**: Running and queued jobs
- **Logs**: Application logs
- **Statistics**: Performance metrics
- **Nodes**: Connected compute nodes
- **Benchmark**: Performance testing

### REST API

HTTP endpoints for:
- Node management
- Job submission
- Simulation execution
- CT filtering and processing
- PNM operations

---

## Getting Started

### Building

```bash
cd NodeEndpoint
dotnet build
```

### Running

```bash
dotnet run
# Or
./NodeEndpoint
```

The service starts on port 8500 by default.

### Production Deployment

#### Systemd Service (Linux)

Create `/etc/systemd/system/nodeendpoint.service`:

```ini
[Unit]
Description=GeoscientistToolkit NodeEndpoint
After=network.target

[Service]
Type=simple
ExecStart=/path/to/NodeEndpoint
WorkingDirectory=/path/to/
Restart=always
User=nodeuser

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl enable nodeendpoint
sudo systemctl start nodeendpoint
```

#### Reverse Proxy (Nginx)

```nginx
server {
    listen 80;
    server_name nodeendpoint.example.com;

    location / {
        proxy_pass http://localhost:8500;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
    }
}
```

---

## API Endpoints

### Node Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/nodes` | List all nodes |
| GET | `/api/nodes/{id}` | Get node details |
| POST | `/api/nodes` | Register new node |
| DELETE | `/api/nodes/{id}` | Remove node |
| GET | `/api/nodes/{id}/status` | Get node status |

### Job Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/jobs` | List all jobs |
| GET | `/api/jobs/{id}` | Get job details |
| POST | `/api/jobs` | Submit new job |
| DELETE | `/api/jobs/{id}` | Cancel job |
| GET | `/api/jobs/{id}/status` | Get job status |
| GET | `/api/jobs/{id}/result` | Get job result |

### Simulation Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/simulate/geomechanical` | Run geomechanical simulation |
| POST | `/api/simulate/acoustic` | Run acoustic simulation |
| POST | `/api/simulate/geothermal` | Run geothermal simulation |
| POST | `/api/simulate/seismic` | Run seismic simulation |
| POST | `/api/simulate/nmr` | Run NMR simulation |
| POST | `/api/simulate/triaxial` | Run triaxial test |

### CT Processing Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/ct/filter` | Apply filter to CT stack |
| POST | `/api/ct/segment` | Segment CT volume |
| POST | `/api/ct/extract-mesh` | Extract mesh from CT |
| POST | `/api/ct/calculate-porosity` | Calculate porosity |

### PNM Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/pnm/generate` | Generate PNM from CT |
| POST | `/api/pnm/permeability` | Calculate permeability |
| POST | `/api/pnm/drainage` | Run drainage simulation |

---

## Example Usage

### List Nodes

```bash
curl http://localhost:8500/api/nodes
```

Response:
```json
{
  "nodes": [
    {
      "id": "node-001",
      "name": "Compute-1",
      "address": "192.168.1.10",
      "status": "online",
      "cpuCores": 16,
      "memory": 32768,
      "gpuAvailable": true
    }
  ]
}
```

### Submit Geothermal Job

```bash
curl -X POST http://localhost:8500/api/simulate/geothermal \
  -H "Content-Type: application/json" \
  -d '{
    "boreholeDepth": 100,
    "inletTemperature": 5,
    "flowRate": 2.0,
    "duration": 365,
    "timeStep": 1
  }'
```

Response:
```json
{
  "jobId": "job-12345",
  "status": "queued",
  "estimatedTime": "00:15:00"
}
```

### Check Job Status

```bash
curl http://localhost:8500/api/jobs/job-12345/status
```

Response:
```json
{
  "jobId": "job-12345",
  "status": "running",
  "progress": 45,
  "currentStep": "Solving heat equation",
  "elapsed": "00:06:32"
}
```

### Get Job Result

```bash
curl http://localhost:8500/api/jobs/job-12345/result
```

---

## Keepalive Configuration

Configure heartbeat settings:

```json
{
  "keepalive": {
    "interval": 30,
    "timeout": 120,
    "retries": 3
  }
}
```

---

## Swagger Documentation

Access interactive API documentation at:
```
http://localhost:8500/swagger
```

Features:
- Endpoint listing
- Request/response schemas
- Try-it-out functionality
- Authentication setup

---

## Configuration

### Configuration File

`appsettings.json`:

```json
{
  "NodeEndpoint": {
    "Port": 8500,
    "MaxConcurrentJobs": 4,
    "EnableGPU": true,
    "LogLevel": "Information",
    "DataDirectory": "./data",
    "TempDirectory": "./temp"
  },
  "Network": {
    "DiscoveryEnabled": true,
    "DiscoveryPort": 8501,
    "BroadcastInterval": 60
  }
}
```

### Environment Variables

Override settings with environment variables:

```bash
export NODEENDPOINT_PORT=8500
export NODEENDPOINT_MAXJOBS=8
export NODEENDPOINT_ENABLEGPU=true
```

---

## Supported Job Types

| Job Type | Description |
|----------|-------------|
| `geomechanical` | Stress/strain analysis |
| `acoustic` | Wave propagation |
| `geothermal` | Heat transfer |
| `seismic` | Seismic processing |
| `nmr` | NMR relaxation |
| `triaxial` | Rock mechanics |
| `ct_filter` | CT image filtering |
| `ct_segment` | CT segmentation |
| `pnm_generate` | PNM extraction |
| `pnm_flow` | PNM flow simulation |

---

## Performance Notes

### Scalability

- Horizontal scaling via multiple nodes
- Automatic load balancing
- Job queue prioritization

### Resource Management

- CPU affinity configuration
- GPU memory management
- Disk I/O optimization

### Benchmarks

| Operation | Single Node | 4 Nodes |
|-----------|-------------|---------|
| Geothermal (100³) | 10 min | 3 min |
| Acoustic (200³) | 30 min | 8 min |
| PNM (50k pores) | 5 min | 2 min |

---

## Advanced Features

### Data References

Jobs can reference existing data:

```json
{
  "inputData": "@dataset:core_ct",
  "operation": "segment",
  "parameters": {...}
}
```

### Job Partitioning

Large jobs can be split across nodes:

```json
{
  "partition": {
    "strategy": "spatial",
    "chunks": 4,
    "overlap": 10
  }
}
```

---

## Security Notes

### Production Recommendations

1. Use reverse proxy with TLS
2. Enable authentication
3. Restrict network access
4. Validate input data
5. Monitor for anomalies

### Authentication

Enable API key authentication:

```json
{
  "Security": {
    "RequireApiKey": true,
    "ApiKeys": ["key1", "key2"]
  }
}
```

Use in requests:
```bash
curl -H "X-API-Key: key1" http://localhost:8500/api/nodes
```

---

## Integration with Main Application

### Connecting from GTK

1. Open **Tools → Network → Node Manager**
2. Add node endpoint URL
3. Configure credentials
4. Test connection

### Submitting Jobs from GTK

1. Select dataset in main application
2. Choose **Submit to Node** from context menu
3. Select target node
4. Configure job parameters
5. Monitor in Node Manager

---

## Related Pages

- [API Reference](API-Reference.md) - Direct API usage
- [Developer Guide](Developer-Guide.md) - Extension development
- [User Guide](User-Guide.md) - Application documentation
- [Home](Home.md) - Wiki home page
