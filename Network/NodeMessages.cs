// GeoscientistToolkit/Network/NodeMessages.cs

using System.Text.Json.Serialization;

namespace GeoscientistToolkit.Network;

/// <summary>
///     Base class for all network messages
/// </summary>
public abstract class NodeMessage
{
    [JsonPropertyName("type")]
    public string MessageType { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("senderId")]
    public string SenderId { get; set; }
}

/// <summary>
///     Message sent by worker nodes to register with the host
/// </summary>
public class RegisterNodeMessage : NodeMessage
{
    public RegisterNodeMessage()
    {
        MessageType = "RegisterNode";
    }

    [JsonPropertyName("nodeName")]
    public string NodeName { get; set; }

    [JsonPropertyName("capabilities")]
    public NodeCapabilities Capabilities { get; set; }
}

/// <summary>
///     Message sent by host to acknowledge node registration
/// </summary>
public class RegisterAckMessage : NodeMessage
{
    public RegisterAckMessage()
    {
        MessageType = "RegisterAck";
    }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("nodeId")]
    public string NodeId { get; set; }
}

/// <summary>
///     Heartbeat message to check node status
/// </summary>
public class HeartbeatMessage : NodeMessage
{
    public HeartbeatMessage()
    {
        MessageType = "Heartbeat";
    }

    [JsonPropertyName("status")]
    public NodeStatus Status { get; set; }

    [JsonPropertyName("cpuUsage")]
    public float CpuUsage { get; set; }

    [JsonPropertyName("memoryUsage")]
    public float MemoryUsage { get; set; }

    [JsonPropertyName("activeJobs")]
    public int ActiveJobs { get; set; }
}

/// <summary>
///     Job submission message sent from host to worker
/// </summary>
public class JobMessage : NodeMessage
{
    public JobMessage()
    {
        MessageType = "Job";
    }

    [JsonPropertyName("jobId")]
    public string JobId { get; set; }

    [JsonPropertyName("jobType")]
    public string JobType { get; set; }

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    [JsonPropertyName("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = new();

    [JsonPropertyName("requiresGpu")]
    public bool RequiresGpu { get; set; }
}

/// <summary>
///     Job result message sent from worker to host
/// </summary>
public class JobResultMessage : NodeMessage
{
    public JobResultMessage()
    {
        MessageType = "JobResult";
    }

    [JsonPropertyName("jobId")]
    public string JobId { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("results")]
    public Dictionary<string, object> Results { get; set; } = new();

    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; set; }

    [JsonPropertyName("executionTimeMs")]
    public long ExecutionTimeMs { get; set; }
}

/// <summary>
///     Node disconnection message
/// </summary>
public class DisconnectMessage : NodeMessage
{
    public DisconnectMessage()
    {
        MessageType = "Disconnect";
    }

    [JsonPropertyName("reason")]
    public string Reason { get; set; }
}

/// <summary>
///     Node capabilities information
/// </summary>
public class NodeCapabilities
{
    [JsonPropertyName("cpuCores")]
    public int CpuCores { get; set; }

    [JsonPropertyName("totalMemoryMb")]
    public long TotalMemoryMb { get; set; }

    [JsonPropertyName("hasGpu")]
    public bool HasGpu { get; set; }

    [JsonPropertyName("gpuName")]
    public string GpuName { get; set; }

    [JsonPropertyName("supportedJobTypes")]
    public List<string> SupportedJobTypes { get; set; } = new();

    [JsonPropertyName("operatingSystem")]
    public string OperatingSystem { get; set; }
}

/// <summary>
///     Node status enumeration
/// </summary>
public enum NodeStatus
{
    Idle,
    Busy,
    Error,
    Disconnected
}
