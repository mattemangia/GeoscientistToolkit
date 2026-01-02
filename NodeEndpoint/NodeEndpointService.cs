using GeoscientistToolkit.Network;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.NodeEndpoint.Services;

namespace GeoscientistToolkit.NodeEndpoint;

/// <summary>
/// Service that manages the NodeManager lifecycle and configuration
/// </summary>
public class NodeEndpointService
{
    private readonly NodeManager _nodeManager;
    private readonly JobTracker _jobTracker;
    private readonly JobExecutor _jobExecutor; // Add JobExecutor
    private bool _initialized = false;

    public NodeEndpointService(JobTracker jobTracker)
    {
        _nodeManager = NodeManager.Instance;
        _jobTracker = jobTracker;
        _jobExecutor = new JobExecutor(_nodeManager); // Initialize JobExecutor

        // Subscribe to NodeManager events
        _nodeManager.JobReceived += OnJobReceived;
        _nodeManager.JobCompleted += OnJobCompleted;
        _nodeManager.NodeConnected += OnNodeConnected;
        _nodeManager.NodeDisconnected += OnNodeDisconnected;
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        Console.WriteLine("Initializing NodeEndpointService...");

        // Load settings - create default if none exist
        var settings = SettingsManager.Instance.Settings;
        if (settings.NodeManager == null)
        {
            settings.NodeManager = new NodeManagerSettings
            {
                EnableNodeManager = true,
                Role = NodeRole.Hybrid, // Can act as both host and worker
                NodeName = Environment.MachineName + "_Endpoint",
                ServerPort = 9876,
                HostAddress = "localhost",
                HeartbeatInterval = 30, // 30 seconds
                MaxReconnectAttempts = 5,
                UseNodesForSimulators = true,
                UseGpuForJobs = true
            };
        }
        else
        {
            // Ensure it's enabled for endpoint mode
            settings.NodeManager.EnableNodeManager = true;
        }

        // Start NodeManager
        try
        {
            _nodeManager.Start();
            Console.WriteLine($"NodeManager started in {settings.NodeManager.Role.ToString()} mode");
            Console.WriteLine($"Server port: {settings.NodeManager.ServerPort.ToString()}");
            Console.WriteLine($"Node name: {settings.NodeManager.NodeName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start NodeManager: {ex.Message}");
        }

        _initialized = true;
    }

    private void OnJobReceived(JobMessage job)
    {
        Console.WriteLine($"Job received: {job.JobId} - Type: {job.JobType}");
        _jobTracker.RegisterJob(job);

        // Execute the job using JobExecutor
        // We run it asynchronously to avoid blocking the network thread
        _ = _jobExecutor.ExecuteJob(job);
    }

    private void OnJobCompleted(JobResultMessage result)
    {
        Console.WriteLine($"Job completed: {result.JobId} - Status: {result.Status}");
        _jobTracker.UpdateJobResult(result);
    }

    private void OnNodeConnected(NodeInfo node)
    {
        Console.WriteLine($"Node connected: {node.NodeId} - {node.NodeName}");
        Console.WriteLine($"  CPU Cores: {node.Capabilities.CpuCores}");
        Console.WriteLine($"  Memory: {node.Capabilities.TotalMemoryMb} MB");
        Console.WriteLine($"  GPU: {(node.Capabilities.HasGpu ? node.Capabilities.GpuName : "None")}");
    }

    private void OnNodeDisconnected(NodeInfo node)
    {
        Console.WriteLine($"Node disconnected: {node.NodeId} - {node.NodeName}");
    }

    public NodeManager GetNodeManager() => _nodeManager;
    public JobTracker GetJobTracker() => _jobTracker;
}
