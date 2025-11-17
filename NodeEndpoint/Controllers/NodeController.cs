using Microsoft.AspNetCore.Mvc;
using GeoscientistToolkit.Network;

namespace GeoscientistToolkit.NodeEndpoint.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NodeController : ControllerBase
{
    private readonly NodeManager _nodeManager;

    public NodeController(NodeManager nodeManager)
    {
        _nodeManager = nodeManager;
    }

    /// <summary>
    /// Get all connected nodes with keepalive status
    /// </summary>
    [HttpGet]
    public IActionResult GetNodes()
    {
        var nodes = _nodeManager.GetNodes().Select(n => new
        {
            nodeId = n.NodeId,
            nodeName = n.NodeName,
            status = n.Status.ToString(),
            capabilities = new
            {
                cpuCores = n.Capabilities.CpuCores,
                totalMemoryMb = n.Capabilities.TotalMemoryMb,
                hasGpu = n.Capabilities.HasGpu,
                gpuName = n.Capabilities.GpuName,
                supportedJobTypes = n.Capabilities.SupportedJobTypes,
                operatingSystem = n.Capabilities.OperatingSystem
            },
            performance = new
            {
                cpuUsagePercent = n.CpuUsagePercent,
                memoryUsagePercent = n.MemoryUsagePercent,
                activeJobs = n.ActiveJobs,
                loadScore = n.GetLoadScore()
            },
            lastHeartbeat = n.LastHeartbeat,
            isAlive = (DateTime.UtcNow - n.LastHeartbeat).TotalSeconds < 60 // Consider alive if heartbeat within 60 seconds
        });

        return Ok(new
        {
            totalNodes = nodes.Count(),
            activeNodes = nodes.Count(n => n.isAlive),
            nodes
        });
    }

    /// <summary>
    /// Get a specific node by ID
    /// </summary>
    [HttpGet("{nodeId}")]
    public IActionResult GetNode(string nodeId)
    {
        var node = _nodeManager.GetNodes().FirstOrDefault(n => n.NodeId == nodeId);
        if (node == null)
        {
            return NotFound(new { error = "Node not found" });
        }

        return Ok(new
        {
            nodeId = node.NodeId,
            nodeName = node.NodeName,
            status = node.Status.ToString(),
            capabilities = new
            {
                cpuCores = node.Capabilities.CpuCores,
                totalMemoryMb = node.Capabilities.TotalMemoryMb,
                hasGpu = node.Capabilities.HasGpu,
                gpuName = node.Capabilities.GpuName,
                supportedJobTypes = node.Capabilities.SupportedJobTypes,
                operatingSystem = node.Capabilities.OperatingSystem
            },
            performance = new
            {
                cpuUsagePercent = node.CpuUsagePercent,
                memoryUsagePercent = node.MemoryUsagePercent,
                activeJobs = node.ActiveJobs,
                loadScore = node.GetLoadScore()
            },
            lastHeartbeat = node.LastHeartbeat,
            isAlive = (DateTime.UtcNow - node.LastHeartbeat).TotalSeconds < 60
        });
    }

    /// <summary>
    /// Get node manager status
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var settings = GeoscientistToolkit.Settings.SettingsManager.Instance.Settings.NodeManager;
        var nodes = _nodeManager.GetNodes();

        return Ok(new
        {
            status = _nodeManager.Status.ToString(),
            role = settings?.Role.ToString() ?? "Unknown",
            nodeName = settings?.NodeName ?? "Unknown",
            serverPort = settings?.ServerPort ?? 0,
            heartbeatInterval = settings?.HeartbeatInterval ?? 0,
            enabledForSimulations = settings?.UseNodesForSimulators ?? false,
            gpuEnabled = settings?.UseGpuForJobs ?? false,
            totalNodes = nodes.Count(),
            activeNodes = nodes.Count(n => (DateTime.UtcNow - n.LastHeartbeat).TotalSeconds < 60),
            totalActiveJobs = nodes.Sum(n => n.ActiveJobs),
            uptime = _nodeManager.Status == NodeStatus.Connected || _nodeManager.Status == NodeStatus.Hosting
                ? "Active"
                : "Inactive"
        });
    }

    /// <summary>
    /// Keepalive endpoint - returns 200 OK if server is responsive
    /// </summary>
    [HttpGet("keepalive")]
    public IActionResult Keepalive()
    {
        return Ok(new
        {
            alive = true,
            timestamp = DateTime.UtcNow,
            status = _nodeManager.Status.ToString(),
            nodeCount = _nodeManager.GetNodes().Count()
        });
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        var isHealthy = _nodeManager.Status == NodeStatus.Connected ||
                       _nodeManager.Status == NodeStatus.Hosting ||
                       _nodeManager.Status == NodeStatus.Hybrid;

        if (!isHealthy)
        {
            return StatusCode(503, new
            {
                healthy = false,
                status = _nodeManager.Status.ToString(),
                message = "Node manager is not active"
            });
        }

        return Ok(new
        {
            healthy = true,
            status = _nodeManager.Status.ToString(),
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get best available node for a job type
    /// </summary>
    [HttpGet("best")]
    public IActionResult GetBestNode([FromQuery] string jobType = "")
    {
        var nodes = _nodeManager.GetNodes()
            .Where(n => (DateTime.UtcNow - n.LastHeartbeat).TotalSeconds < 60)
            .OrderBy(n => n.GetLoadScore())
            .ToList();

        if (!nodes.Any())
        {
            return NotFound(new { error = "No available nodes" });
        }

        var bestNode = nodes.First();
        return Ok(new
        {
            nodeId = bestNode.NodeId,
            nodeName = bestNode.NodeName,
            loadScore = bestNode.GetLoadScore(),
            capabilities = new
            {
                cpuCores = bestNode.Capabilities.CpuCores,
                totalMemoryMb = bestNode.Capabilities.TotalMemoryMb,
                hasGpu = bestNode.Capabilities.HasGpu
            }
        });
    }
}
