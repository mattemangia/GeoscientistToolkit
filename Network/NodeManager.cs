// GeoscientistToolkit/Network/NodeManager.cs

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Network;

/// <summary>
///     Manages the distributed computing node network
///     Can run as a Host (distributes jobs), Worker (executes jobs), or Hybrid (both)
/// </summary>
public class NodeManager
{
    private static NodeManager _instance;
    private static readonly object _instanceLock = new object();

    private readonly object _nodesLock = new object();
    private readonly Dictionary<string, NodeInfo> _connectedNodes = new();
    private readonly Dictionary<string, JobMessage> _activeJobs = new();

    private TcpListener _server;
    private TcpClient _clientConnection;
    private CancellationTokenSource _cancellationTokenSource;
    private Thread _serverThread;
    private Thread _clientThread;
    private Thread _heartbeatThread;

    private NodeManager()
    {
        NodeId = Guid.NewGuid().ToString();
    }

    public static NodeManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new NodeManager();
                }
            }
            return _instance;
        }
    }

    public string NodeId { get; }
    public bool IsRunning { get; private set; }
    public NodeStatus CurrentStatus { get; private set; } = NodeStatus.Idle;
    public int ConnectedNodeCount => _connectedNodes.Count;

    // Events
    public event Action<NodeInfo> NodeConnected;
    public event Action<NodeInfo> NodeDisconnected;
    public event Action<JobMessage> JobReceived;
    public event Action<JobResultMessage> JobCompleted;
    public event Action<string> StatusChanged;

    /// <summary>
    ///     Starts the node manager based on settings
    /// </summary>
    public void Start()
    {
        if (IsRunning)
        {
            Logger.LogWarning("NodeManager is already running");
            return;
        }

        var settings = SettingsManager.Instance.Settings.NodeManager;
        if (!settings.EnableNodeManager)
        {
            Logger.LogWarning("NodeManager is disabled in settings");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        IsRunning = true;

        try
        {
            switch (settings.Role)
            {
                case NodeRole.Host:
                    StartHost(settings.ServerPort);
                    break;

                case NodeRole.Worker:
                    StartWorker(settings.HostAddress, settings.ServerPort);
                    break;

                case NodeRole.Hybrid:
                    StartHost(settings.ServerPort);
                    // Note: Hybrid mode runs as host and can accept jobs locally
                    break;
            }

            // Start heartbeat thread for all roles
            _heartbeatThread = new Thread(HeartbeatLoop);
            _heartbeatThread.IsBackground = true;
            _heartbeatThread.Start();

            Logger.Log($"NodeManager started in {settings.Role} mode");
            StatusChanged?.Invoke($"Started in {settings.Role} mode");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to start NodeManager: {ex.Message}");
            Stop();
            throw;
        }
    }

    /// <summary>
    ///     Stops the node manager
    /// </summary>
    public void Stop()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        _cancellationTokenSource?.Cancel();

        // Stop server
        _server?.Stop();

        // Disconnect client
        _clientConnection?.Close();

        // Wait for threads to finish
        _serverThread?.Join(1000);
        _clientThread?.Join(1000);
        _heartbeatThread?.Join(1000);

        // Clear connections
        lock (_nodesLock)
        {
            foreach (var node in _connectedNodes.Values)
            {
                node.Connection?.Close();
            }
            _connectedNodes.Clear();
        }

        _activeJobs.Clear();

        Logger.Log("NodeManager stopped");
        StatusChanged?.Invoke("Stopped");
    }

    /// <summary>
    ///     Starts the host server to accept worker connections
    /// </summary>
    private void StartHost(int port)
    {
        _server = new TcpListener(IPAddress.Any, port);
        _server.Start();

        _serverThread = new Thread(AcceptClientsLoop);
        _serverThread.IsBackground = true;
        _serverThread.Start();

        Logger.Log($"Host server started on port {port}");
    }

    /// <summary>
    ///     Starts the worker client to connect to host
    /// </summary>
    private void StartWorker(string hostAddress, int port)
    {
        _clientThread = new Thread(() => ConnectToHost(hostAddress, port));
        _clientThread.IsBackground = true;
        _clientThread.Start();
    }

    /// <summary>
    ///     Accept incoming client connections (Host mode)
    /// </summary>
    private void AcceptClientsLoop()
    {
        while (IsRunning && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (_server.Pending())
                {
                    var client = _server.AcceptTcpClient();
                    Logger.Log($"New connection from {client.Client.RemoteEndPoint}");

                    // Handle client in separate thread
                    var clientThread = new Thread(() => HandleClient(client));
                    clientThread.IsBackground = true;
                    clientThread.Start();
                }

                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                if (IsRunning)
                {
                    Logger.LogError($"Error accepting client: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    ///     Connect to host server (Worker mode)
    /// </summary>
    private void ConnectToHost(string hostAddress, int port)
    {
        var settings = SettingsManager.Instance.Settings.NodeManager;
        var attempts = 0;

        while (IsRunning && attempts < settings.MaxReconnectAttempts)
        {
            try
            {
                _clientConnection = new TcpClient();
                _clientConnection.Connect(hostAddress, port);

                Logger.Log($"Connected to host at {hostAddress}:{port}");
                StatusChanged?.Invoke($"Connected to {hostAddress}:{port}");

                // Send registration message
                var registerMsg = new RegisterNodeMessage
                {
                    SenderId = NodeId,
                    NodeName = settings.NodeName,
                    Capabilities = GetNodeCapabilities()
                };

                SendMessage(_clientConnection, registerMsg);

                // Handle messages from host
                HandleServerMessages(_clientConnection);

                break;
            }
            catch (Exception ex)
            {
                attempts++;
                Logger.LogError($"Failed to connect to host (attempt {attempts}): {ex.Message}");

                if (attempts < settings.MaxReconnectAttempts)
                {
                    Thread.Sleep(5000); // Wait before retry
                }
            }
        }

        if (attempts >= settings.MaxReconnectAttempts)
        {
            Logger.LogError("Failed to connect to host after maximum attempts");
            StatusChanged?.Invoke("Connection failed");
        }
    }

    /// <summary>
    ///     Handle messages from a client node (Host mode)
    /// </summary>
    private void HandleClient(TcpClient client)
    {
        NodeInfo nodeInfo = null;

        try
        {
            var stream = client.GetStream();
            var buffer = new byte[8192];

            while (IsRunning && client.Connected)
            {
                if (stream.DataAvailable)
                {
                    var bytesRead = stream.Read(buffer, 0, buffer.Length);
                    var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    var baseMsg = JsonSerializer.Deserialize<NodeMessage>(json);

                    switch (baseMsg.MessageType)
                    {
                        case "RegisterNode":
                            var registerMsg = JsonSerializer.Deserialize<RegisterNodeMessage>(json);
                            nodeInfo = RegisterNode(registerMsg, client);
                            break;

                        case "Heartbeat":
                            var heartbeatMsg = JsonSerializer.Deserialize<HeartbeatMessage>(json);
                            UpdateNodeHeartbeat(heartbeatMsg);
                            break;

                        case "JobResult":
                            var resultMsg = JsonSerializer.Deserialize<JobResultMessage>(json);
                            HandleJobResult(resultMsg);
                            break;

                        case "Disconnect":
                            Logger.Log($"Node {nodeInfo?.NodeName} requested disconnect");
                            return;
                    }
                }

                Thread.Sleep(50);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error handling client: {ex.Message}");
        }
        finally
        {
            if (nodeInfo != null)
            {
                UnregisterNode(nodeInfo);
            }
            client.Close();
        }
    }

    /// <summary>
    ///     Handle messages from host server (Worker mode)
    /// </summary>
    private void HandleServerMessages(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[8192];

            while (IsRunning && client.Connected)
            {
                if (stream.DataAvailable)
                {
                    var bytesRead = stream.Read(buffer, 0, buffer.Length);
                    var json = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    var baseMsg = JsonSerializer.Deserialize<NodeMessage>(json);

                    switch (baseMsg.MessageType)
                    {
                        case "RegisterAck":
                            var ackMsg = JsonSerializer.Deserialize<RegisterAckMessage>(json);
                            HandleRegisterAck(ackMsg);
                            break;

                        case "Job":
                            var jobMsg = JsonSerializer.Deserialize<JobMessage>(json);
                            HandleJob(jobMsg);
                            break;
                    }
                }

                Thread.Sleep(50);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error handling server messages: {ex.Message}");
            StatusChanged?.Invoke("Disconnected from host");
        }
    }

    /// <summary>
    ///     Register a new node (Host mode)
    /// </summary>
    private NodeInfo RegisterNode(RegisterNodeMessage msg, TcpClient client)
    {
        var nodeInfo = new NodeInfo
        {
            NodeId = msg.SenderId,
            NodeName = msg.NodeName,
            IpAddress = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString(),
            Capabilities = msg.Capabilities,
            Connection = client,
            ConnectedAt = DateTime.UtcNow,
            LastHeartbeat = DateTime.UtcNow
        };

        lock (_nodesLock)
        {
            _connectedNodes[nodeInfo.NodeId] = nodeInfo;
        }

        // Send acknowledgment
        var ack = new RegisterAckMessage
        {
            SenderId = NodeId,
            Success = true,
            Message = "Registration successful",
            NodeId = nodeInfo.NodeId
        };

        SendMessage(client, ack);

        Logger.Log($"Node registered: {nodeInfo.NodeName} ({nodeInfo.IpAddress})");
        NodeConnected?.Invoke(nodeInfo);

        return nodeInfo;
    }

    /// <summary>
    ///     Unregister a node (Host mode)
    /// </summary>
    private void UnregisterNode(NodeInfo nodeInfo)
    {
        lock (_nodesLock)
        {
            _connectedNodes.Remove(nodeInfo.NodeId);
        }

        Logger.Log($"Node disconnected: {nodeInfo.NodeName}");
        NodeDisconnected?.Invoke(nodeInfo);
    }

    /// <summary>
    ///     Update node heartbeat information (Host mode)
    /// </summary>
    private void UpdateNodeHeartbeat(HeartbeatMessage msg)
    {
        lock (_nodesLock)
        {
            if (_connectedNodes.TryGetValue(msg.SenderId, out var node))
            {
                node.LastHeartbeat = DateTime.UtcNow;
                node.Status = msg.Status;
                node.CpuUsage = msg.CpuUsage;
                node.MemoryUsage = msg.MemoryUsage;
                node.ActiveJobs = msg.ActiveJobs;
            }
        }
    }

    /// <summary>
    ///     Handle registration acknowledgment (Worker mode)
    /// </summary>
    private void HandleRegisterAck(RegisterAckMessage msg)
    {
        if (msg.Success)
        {
            Logger.Log($"Successfully registered with host");
            CurrentStatus = NodeStatus.Idle;
        }
        else
        {
            Logger.LogError($"Registration failed: {msg.Message}");
        }
    }

    /// <summary>
    ///     Handle job assignment (Worker mode)
    /// </summary>
    private void HandleJob(JobMessage msg)
    {
        Logger.Log($"Received job: {msg.JobId} (Type: {msg.JobType})");
        CurrentStatus = NodeStatus.Busy;

        JobReceived?.Invoke(msg);

        // Job execution will be handled by external code via JobReceived event
        // For now, we just acknowledge receipt
    }

    /// <summary>
    ///     Handle job result (Host mode)
    /// </summary>
    private void HandleJobResult(JobResultMessage msg)
    {
        Logger.Log($"Job completed: {msg.JobId} - Success: {msg.Success}");

        lock (_activeJobs)
        {
            _activeJobs.Remove(msg.JobId);
        }

        JobCompleted?.Invoke(msg);
    }

    /// <summary>
    ///     Heartbeat loop to send periodic status updates
    /// </summary>
    private void HeartbeatLoop()
    {
        var settings = SettingsManager.Instance.Settings.NodeManager;

        while (IsRunning && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // Worker mode: send heartbeat to host
                if (settings.Role == NodeRole.Worker && _clientConnection?.Connected == true)
                {
                    var heartbeat = new HeartbeatMessage
                    {
                        SenderId = NodeId,
                        Status = CurrentStatus,
                        CpuUsage = GetCpuUsage(),
                        MemoryUsage = GetMemoryUsage(),
                        ActiveJobs = _activeJobs.Count
                    };

                    SendMessage(_clientConnection, heartbeat);
                }

                // Host mode: check for dead nodes
                if (settings.Role == NodeRole.Host || settings.Role == NodeRole.Hybrid)
                {
                    CheckNodeHealth();
                }

                Thread.Sleep(settings.HeartbeatInterval * 1000);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Heartbeat error: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     Check health of connected nodes and remove dead ones
    /// </summary>
    private void CheckNodeHealth()
    {
        var settings = SettingsManager.Instance.Settings.NodeManager;
        var deadNodes = new List<NodeInfo>();

        lock (_nodesLock)
        {
            foreach (var node in _connectedNodes.Values)
            {
                if (!node.IsAlive(settings.HeartbeatInterval))
                {
                    deadNodes.Add(node);
                }
            }
        }

        foreach (var node in deadNodes)
        {
            Logger.LogWarning($"Node {node.NodeName} is not responding, removing...");
            UnregisterNode(node);
        }
    }

    /// <summary>
    ///     Submit a job to the best available worker node
    /// </summary>
    public bool SubmitJob(JobMessage job)
    {
        lock (_nodesLock)
        {
            // Find best node (lowest load score)
            var bestNode = _connectedNodes.Values
                .Where(n => n.Status == NodeStatus.Idle || n.Status == NodeStatus.Busy)
                .Where(n => !job.RequiresGpu || n.Capabilities.HasGpu)
                .OrderBy(n => n.GetLoadScore())
                .FirstOrDefault();

            if (bestNode == null)
            {
                Logger.LogWarning("No available nodes to execute job");
                return false;
            }

            job.SenderId = NodeId;
            SendMessage(bestNode.Connection, job);

            lock (_activeJobs)
            {
                _activeJobs[job.JobId] = job;
            }

            Logger.Log($"Job {job.JobId} submitted to node {bestNode.NodeName}");
            return true;
        }
    }

    /// <summary>
    ///     Send job result back to host (Worker mode)
    /// </summary>
    public void SendJobResult(JobResultMessage result)
    {
        if (_clientConnection?.Connected == true)
        {
            result.SenderId = NodeId;
            SendMessage(_clientConnection, result);
            CurrentStatus = NodeStatus.Idle;
        }
    }

    /// <summary>
    ///     Send a message to a TCP client
    /// </summary>
    private void SendMessage(TcpClient client, NodeMessage message)
    {
        try
        {
            var json = JsonSerializer.Serialize(message, message.GetType());
            var data = Encoding.UTF8.GetBytes(json);

            var stream = client.GetStream();
            stream.Write(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to send message: {ex.Message}");
        }
    }

    /// <summary>
    ///     Get current node capabilities
    /// </summary>
    private NodeCapabilities GetNodeCapabilities()
    {
        var settings = SettingsManager.Instance.Settings.NodeManager;

        return new NodeCapabilities
        {
            CpuCores = Environment.ProcessorCount,
            TotalMemoryMb = GetTotalMemoryMb(),
            HasGpu = settings.UseGpuForJobs && HasGpuSupport(),
            GpuName = GetGpuName(),
            SupportedJobTypes = new List<string> { "Simulation", "Analysis", "Computation" },
            OperatingSystem = Environment.OSVersion.ToString()
        };
    }

    /// <summary>
    ///     Get total system memory in MB
    /// </summary>
    private long GetTotalMemoryMb()
    {
        try
        {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            return gcMemoryInfo.TotalAvailableMemoryBytes / (1024 * 1024);
        }
        catch
        {
            return 8192; // Default fallback
        }
    }

    /// <summary>
    ///     Check if GPU support is available
    /// </summary>
    private bool HasGpuSupport()
    {
        try
        {
            var devices = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetAvailableDevices();
            return devices.Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Get GPU name
    /// </summary>
    private string GetGpuName()
    {
        try
        {
            var devices = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetAvailableDevices();
            return devices.FirstOrDefault()?.Name ?? "None";
        }
        catch
        {
            return "None";
        }
    }

    /// <summary>
    ///     Get current CPU usage percentage
    /// </summary>
    private float GetCpuUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;

            Thread.Sleep(100);

            var endTime = DateTime.UtcNow;
            var endCpuUsage = process.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;

            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            return (float)(cpuUsageTotal * 100);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    ///     Get current memory usage percentage
    /// </summary>
    private float GetMemoryUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var totalMemory = GetTotalMemoryMb() * 1024 * 1024;
            return (float)(process.WorkingSet64 / (double)totalMemory * 100);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    ///     Get list of connected nodes (Host mode)
    /// </summary>
    public List<NodeInfo> GetConnectedNodes()
    {
        lock (_nodesLock)
        {
            return new List<NodeInfo>(_connectedNodes.Values);
        }
    }

    /// <summary>
    ///     Get list of active jobs
    /// </summary>
    public List<JobMessage> GetActiveJobs()
    {
        lock (_activeJobs)
        {
            return new List<JobMessage>(_activeJobs.Values);
        }
    }
}
