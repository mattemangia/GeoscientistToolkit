// GeoscientistToolkit/UI/Windows/NodeManagerWindow.cs

using System.Numerics;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI.Windows;

/// <summary>
///     Window for managing the distributed computing node manager
/// </summary>
public class NodeManagerWindow
{
    private bool _isOpen;
    private string _statusMessage = "Not running";
    private List<NodeInfo> _nodes = new();
    private List<JobMessage> _activeJobs = new();
    private float _refreshTimer;
    private const float RefreshInterval = 1.0f; // seconds

    // Test job submission
    private string _testJobName = "Test Job";
    private bool _testJobRequiresGpu;

    // Callback for opening settings window
    public Action OnOpenSettings { get; set; }

    public NodeManagerWindow()
    {
        // Subscribe to node manager events
        NodeManager.Instance.NodeConnected += OnNodeConnected;
        NodeManager.Instance.NodeDisconnected += OnNodeDisconnected;
        NodeManager.Instance.StatusChanged += OnStatusChanged;
        NodeManager.Instance.JobReceived += OnJobReceived;
        NodeManager.Instance.JobCompleted += OnJobCompleted;
    }

    public void Show()
    {
        _isOpen = true;
    }

    public void Submit(float deltaTime)
    {
        if (!_isOpen)
            return;

        // Update data periodically
        _refreshTimer += deltaTime;
        if (_refreshTimer >= RefreshInterval)
        {
            _refreshTimer = 0;
            RefreshData();
        }

        var settings = SettingsManager.Instance.Settings.NodeManager;

        ImGui.SetNextWindowSize(new Vector2(900, 600), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Node Manager", ref _isOpen, ImGuiWindowFlags.None))
        {
            // Top controls
            DrawControlPanel(settings);

            ImGui.Separator();

            // Status bar
            DrawStatusBar();

            ImGui.Separator();

            // Main content tabs
            if (ImGui.BeginTabBar("NodeManagerTabs"))
            {
                if (ImGui.BeginTabItem("Connected Nodes"))
                {
                    DrawNodesTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Active Jobs"))
                {
                    DrawJobsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Statistics"))
                {
                    DrawStatsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Test"))
                {
                    DrawTestTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }

    private void DrawControlPanel(NodeManagerSettings settings)
    {
        var isRunning = NodeManager.Instance.IsRunning;

        // Start/Stop button
        if (isRunning)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
            if (ImGui.Button("Stop Node Manager", new Vector2(150, 30)))
            {
                try
                {
                    NodeManager.Instance.Stop();
                    Logger.Log("Node Manager stopped by user");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to stop Node Manager: {ex.Message}");
                }
            }
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.8f, 0.2f, 1.0f));
            if (ImGui.Button("Start Node Manager", new Vector2(150, 30)))
            {
                try
                {
                    NodeManager.Instance.Start();
                    Logger.Log("Node Manager started by user");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to start Node Manager: {ex.Message}");
                }
            }
            ImGui.PopStyleColor();
        }

        ImGui.SameLine();

        // Role indicator
        ImGui.Text($"Role: {settings.Role}");
        ImGui.SameLine();

        // Port/Host info
        if (settings.Role == NodeRole.Host || settings.Role == NodeRole.Hybrid)
        {
            ImGui.Text($"| Port: {settings.ServerPort}");
        }
        else
        {
            ImGui.Text($"| Host: {settings.HostAddress}:{settings.ServerPort}");
        }

        ImGui.SameLine();

        // Settings button
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 120);
        if (ImGui.Button("Settings", new Vector2(100, 30)))
        {
            OnOpenSettings?.Invoke();
        }
    }

    private void DrawStatusBar()
    {
        var settings = SettingsManager.Instance.Settings.NodeManager;
        var isRunning = NodeManager.Instance.IsRunning;

        // Status indicator
        var statusColor = isRunning ? new Vector4(0.2f, 0.8f, 0.2f, 1.0f) : new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
        ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
        ImGui.Text(isRunning ? "●" : "○");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.Text($"Status: {_statusMessage}");

        ImGui.SameLine();
        ImGui.Text($"| Node ID: {NodeManager.Instance.NodeId[..8]}...");

        if (isRunning && (settings.Role == NodeRole.Host || settings.Role == NodeRole.Hybrid))
        {
            ImGui.SameLine();
            ImGui.Text($"| Connected Nodes: {_nodes.Count}");

            ImGui.SameLine();
            ImGui.Text($"| Active Jobs: {_activeJobs.Count}");
        }
    }

    private void DrawNodesTab()
    {
        var settings = SettingsManager.Instance.Settings.NodeManager;

        if (settings.Role == NodeRole.Worker)
        {
            ImGui.TextWrapped("Node information is only available in Host or Hybrid mode.");
            return;
        }

        if (_nodes.Count == 0)
        {
            ImGui.TextWrapped("No nodes connected. Start the node manager and configure worker nodes to connect to this host.");
            return;
        }

        // Table of connected nodes
        if (ImGui.BeginTable("NodesTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("IP Address", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("CPU %", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Memory %", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Jobs", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("GPU", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var node in _nodes)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(node.NodeName);

                ImGui.TableNextColumn();
                ImGui.Text(node.IpAddress);

                ImGui.TableNextColumn();
                var statusColor = node.Status switch
                {
                    NodeStatus.Idle => new Vector4(0.2f, 0.8f, 0.2f, 1.0f),
                    NodeStatus.Busy => new Vector4(0.8f, 0.6f, 0.2f, 1.0f),
                    NodeStatus.Error => new Vector4(0.8f, 0.2f, 0.2f, 1.0f),
                    _ => new Vector4(0.6f, 0.6f, 0.6f, 1.0f)
                };
                ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
                ImGui.Text(node.Status.ToString());
                ImGui.PopStyleColor();

                ImGui.TableNextColumn();
                ImGui.Text($"{node.CpuUsage:F1}%");

                ImGui.TableNextColumn();
                ImGui.Text($"{node.MemoryUsage:F1}%");

                ImGui.TableNextColumn();
                ImGui.Text(node.ActiveJobs.ToString());

                ImGui.TableNextColumn();
                ImGui.Text(node.Capabilities?.GpuName ?? "None");

                // Tooltip with more details
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Node Details:");
                    ImGui.Separator();
                    ImGui.Text($"Node ID: {node.NodeId}");
                    ImGui.Text($"Connected: {node.ConnectedAt:g}");
                    ImGui.Text($"Last Heartbeat: {node.LastHeartbeat:T}");
                    if (node.Capabilities != null)
                    {
                        ImGui.Text($"CPU Cores: {node.Capabilities.CpuCores}");
                        ImGui.Text($"Total Memory: {node.Capabilities.TotalMemoryMb} MB");
                        ImGui.Text($"OS: {node.Capabilities.OperatingSystem}");
                    }
                    ImGui.EndTooltip();
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawJobsTab()
    {
        if (_activeJobs.Count == 0)
        {
            ImGui.TextWrapped("No active jobs.");
            return;
        }

        // Table of active jobs
        if (ImGui.BeginTable("JobsTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Job ID", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Priority", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("GPU Required", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Submitted", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var job in _activeJobs)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(job.JobId[..8] + "...");

                ImGui.TableNextColumn();
                ImGui.Text(job.JobType);

                ImGui.TableNextColumn();
                ImGui.Text(job.Priority.ToString());

                ImGui.TableNextColumn();
                ImGui.Text(job.RequiresGpu ? "Yes" : "No");

                ImGui.TableNextColumn();
                ImGui.Text(job.Timestamp.ToLocalTime().ToString("T"));

                // Tooltip with job details
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"Job Details:");
                    ImGui.Separator();
                    ImGui.Text($"Full ID: {job.JobId}");
                    ImGui.Text($"Sender: {job.SenderId}");
                    ImGui.Text($"Parameters: {job.Parameters.Count}");
                    ImGui.EndTooltip();
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawStatsTab()
    {
        var settings = SettingsManager.Instance.Settings.NodeManager;

        ImGui.Text("Node Manager Statistics");
        ImGui.Separator();

        ImGui.Text($"Role: {settings.Role}");
        ImGui.Text($"Node Name: {settings.NodeName}");
        ImGui.Text($"Running: {(NodeManager.Instance.IsRunning ? "Yes" : "No")}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Configuration");
        ImGui.Separator();

        ImGui.Text($"Server Port: {settings.ServerPort}");
        ImGui.Text($"Host Address: {settings.HostAddress}");
        ImGui.Text($"Max Concurrent Jobs: {settings.MaxConcurrentJobs}");
        ImGui.Text($"Use GPU for Jobs: {(settings.UseGpuForJobs ? "Yes" : "No")}");
        ImGui.Text($"Heartbeat Interval: {settings.HeartbeatInterval} seconds");
        ImGui.Text($"Connection Timeout: {settings.ConnectionTimeout} seconds");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Resources");
        ImGui.Separator();

        ImGui.Text($"Max Memory Usage: {settings.MaxMemoryUsagePercent}%");
        ImGui.Text($"Max CPU Usage: {settings.MaxCpuUsagePercent}%");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Network");
        ImGui.Separator();

        if (settings.Role == NodeRole.Host || settings.Role == NodeRole.Hybrid)
        {
            ImGui.Text($"Total Nodes Connected: {_nodes.Count}");
            ImGui.Text($"Active Nodes: {_nodes.Count(n => n.Status == NodeStatus.Idle || n.Status == NodeStatus.Busy)}");
            ImGui.Text($"Total Jobs: {_activeJobs.Count}");
        }
        else
        {
            ImGui.Text($"Connection Status: {_statusMessage}");
        }
    }

    private void DrawTestTab()
    {
        var settings = SettingsManager.Instance.Settings.NodeManager;

        ImGui.TextWrapped("Use this tab to test job submission and execution.");
        ImGui.Separator();

        if (settings.Role == NodeRole.Worker)
        {
            ImGui.TextWrapped("Job submission is only available in Host or Hybrid mode.");
            return;
        }

        if (_nodes.Count == 0)
        {
            ImGui.TextWrapped("No worker nodes connected. Cannot submit test jobs.");
            return;
        }

        ImGui.Text("Submit Test Job");
        ImGui.Separator();

        ImGui.InputText("Job Name", ref _testJobName, 256);
        ImGui.Checkbox("Requires GPU", ref _testJobRequiresGpu);

        if (ImGui.Button("Submit Test Job", new Vector2(150, 30)))
        {
            var job = new JobMessage
            {
                JobId = Guid.NewGuid().ToString(),
                JobType = "Test",
                Priority = 0,
                RequiresGpu = _testJobRequiresGpu,
                Parameters = new Dictionary<string, object>
                {
                    { "name", _testJobName },
                    { "timestamp", DateTime.UtcNow.ToString() }
                }
            };

            if (NodeManager.Instance.SubmitJob(job))
            {
                Logger.Log($"Test job submitted: {job.JobId}");
            }
            else
            {
                Logger.LogError("Failed to submit test job");
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Recent Job Results");
        ImGui.Separator();

        ImGui.TextWrapped("Job results will appear here when completed.");
    }

    private void RefreshData()
    {
        var settings = SettingsManager.Instance.Settings.NodeManager;

        if (NodeManager.Instance.IsRunning)
        {
            if (settings.Role == NodeRole.Host || settings.Role == NodeRole.Hybrid)
            {
                _nodes = NodeManager.Instance.GetConnectedNodes();
                _activeJobs = NodeManager.Instance.GetActiveJobs();
            }
        }
        else
        {
            _nodes.Clear();
            _activeJobs.Clear();
        }
    }

    // Event handlers
    private void OnNodeConnected(NodeInfo node)
    {
        _statusMessage = $"Node connected: {node.NodeName}";
        RefreshData();
    }

    private void OnNodeDisconnected(NodeInfo node)
    {
        _statusMessage = $"Node disconnected: {node.NodeName}";
        RefreshData();
    }

    private void OnStatusChanged(string status)
    {
        _statusMessage = status;
    }

    private void OnJobReceived(JobMessage job)
    {
        _statusMessage = $"Job received: {job.JobType}";
        RefreshData();
    }

    private void OnJobCompleted(JobResultMessage result)
    {
        _statusMessage = $"Job completed: {result.JobId[..8]}... ({(result.Success ? "Success" : "Failed")})";
        RefreshData();
    }
}
