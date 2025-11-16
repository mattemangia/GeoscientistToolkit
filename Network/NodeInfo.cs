// GeoscientistToolkit/Network/NodeInfo.cs

using System.Net.Sockets;

namespace GeoscientistToolkit.Network;

/// <summary>
///     Represents information about a connected node in the network
/// </summary>
public class NodeInfo
{
    public string NodeId { get; set; }
    public string NodeName { get; set; }
    public string IpAddress { get; set; }
    public NodeStatus Status { get; set; } = NodeStatus.Idle;
    public NodeCapabilities Capabilities { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public float CpuUsage { get; set; }
    public float MemoryUsage { get; set; }
    public int ActiveJobs { get; set; }
    public TcpClient Connection { get; set; }

    /// <summary>
    ///     Checks if the node is considered alive based on last heartbeat
    /// </summary>
    public bool IsAlive(int heartbeatTimeoutSeconds)
    {
        return (DateTime.UtcNow - LastHeartbeat).TotalSeconds < heartbeatTimeoutSeconds * 2;
    }

    /// <summary>
    ///     Calculates the node's load score (0-100, lower is better)
    /// </summary>
    public float GetLoadScore()
    {
        return (CpuUsage * 0.5f + MemoryUsage * 0.3f + (ActiveJobs / (float)Capabilities.CpuCores) * 20f);
    }
}
