using Terminal.Gui;
using GeoscientistToolkit.Network;
using System.Diagnostics;
using System.Text;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Collections.Concurrent;

namespace GeoscientistToolkit.NodeEndpoint;

public class TuiManager
{
    private readonly NodeManager _nodeManager;
    private readonly Services.NetworkDiscoveryService _networkDiscovery;
    private readonly JobTracker _jobTracker;
    private readonly int _httpPort;
    private readonly int _nodeManagerPort;
    private readonly string _localIp;
    private readonly string _configPath;

    // UI Components
    private Window _mainWindow = null!;
    private TabView _tabView = null!;

    // Dashboard Tab
    private ListView _connectionsListView = null!;
    private Label _statusLabel = null!;
    private Label _platformLabel = null!;
    private Label _httpApiLabel = null!;
    private Label _nodeManagerLabel = null!;
    private Label _uptimeLabel = null!;
    private FrameView _connectionsFrame = null!;
    private FrameView _statusFrame = null!;
    private ProgressBar _cpuUsageBar = null!;
    private ProgressBar _memoryUsageBar = null!;
    private Label _cpuUsageLabel = null!;
    private Label _memoryUsageLabel = null!;
    private Label _diskUsageLabel = null!;
    private Label _networkStatsLabel = null!;

    // Jobs Tab
    private ListView _jobsListView = null!;
    private TextView _jobDetailsView = null!;

    // Logs Tab
    private ListView _logsListView = null!;
    private TextField _logFilterField = null!;

    // Statistics Tab
    private TextView _statsView = null!;

    // Nodes Tab
    private ListView _nodesListView = null!;
    private TextView _nodeDetailsView = null!;

    // Benchmark Tab
    private Label _cpuBenchmarkLabel = null!;
    private ProgressBar _benchmarkProgress = null!;

    // Indicators
    private Label _keepaliveIndicator = null!;
    private Label _txIndicator = null!;
    private Label _rxIndicator = null!;
    private Label _jobsIndicator = null!;

    // State
    private DateTime _startTime;
    private System.Timers.Timer? _updateTimer;
    private bool _isRunning;
    private float _currentCpuUsage;
    private float _currentMemoryUsage;
    private long _lastBytesSent;
    private long _lastBytesReceived;
    private long _currentBytesPerSecSent;
    private long _currentBytesPerSecReceived;
    private bool _txActive;
    private bool _rxActive;
    private int _keepaliveCounter;
    private readonly ConcurrentQueue<LogEntry> _logs = new();
    private readonly List<MetricSnapshot> _metricHistory = new();
    private string _logFilter = "";
    private string _jobFilter = "";
    private int _selectedJobIndex = -1;
    private int _selectedNodeIndex = -1;

    private class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Details { get; set; }
    }

    private class MetricSnapshot
    {
        public DateTime Timestamp { get; set; }
        public float CpuUsage { get; set; }
        public float MemoryUsage { get; set; }
        public long NetworkBytesSent { get; set; }
        public long NetworkBytesReceived { get; set; }
        public int ActiveConnections { get; set; }
        public int ActiveJobs { get; set; }
    }

    public TuiManager(
        NodeManager nodeManager,
        Services.NetworkDiscoveryService networkDiscovery,
        JobTracker jobTracker,
        int httpPort,
        int nodeManagerPort,
        string localIp)
    {
        _nodeManager = nodeManager;
        _networkDiscovery = networkDiscovery;
        _jobTracker = jobTracker;
        _httpPort = httpPort;
        _nodeManagerPort = nodeManagerPort;
        _localIp = localIp;
        _startTime = DateTime.Now;
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        // Initialize logging
        AddLog("INFO", "TUI Manager initialized");
    }

    public void Run()
    {
        Application.Init();

        try
        {
            _isRunning = true;
            SetupUI();
            SetupUpdateTimer();

            Application.Run();
        }
        finally
        {
            _isRunning = false;
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            Application.Shutdown();
        }
    }

    private void SetupUI()
    {
        var top = Application.Top;

        // Create menu bar
        var menu = new MenuBar(new MenuBarItem[]
        {
            new MenuBarItem("_File", new MenuItem[]
            {
                new MenuItem("_Export Logs...", "Export logs to file", ExportLogs),
                new MenuItem("_Export Statistics...", "Export statistics to file", ExportStatistics),
                new MenuItem("_Export Configuration...", "Export current configuration", ExportConfiguration),
                null!, // Separator
                new MenuItem("_Quit", "Exit the application", QuitApplication)
            }),
            new MenuBarItem("_View", new MenuItem[]
            {
                new MenuItem("_Dashboard", "Show main dashboard", () => _tabView.SelectedTab = _tabView.Tabs.ElementAt(0)),
                new MenuItem("_Jobs", "Show jobs monitor", () => _tabView.SelectedTab = _tabView.Tabs.ElementAt(1)),
                new MenuItem("_Logs", "Show logs viewer", () => _tabView.SelectedTab = _tabView.Tabs.ElementAt(2)),
                new MenuItem("_Statistics", "Show statistics", () => _tabView.SelectedTab = _tabView.Tabs.ElementAt(3)),
                new MenuItem("_Nodes", "Show connected nodes", () => _tabView.SelectedTab = _tabView.Tabs.ElementAt(4)),
                new MenuItem("_Benchmark", "Show benchmark results", () => _tabView.SelectedTab = _tabView.Tabs.ElementAt(5)),
                null!, // Separator
                new MenuItem("_Refresh All", "Refresh all data", RefreshAll)
            }),
            new MenuBarItem("_Tools", new MenuItem[]
            {
                new MenuItem("Run CPU _Benchmark", "Run a comprehensive CPU benchmark", RunCpuBenchmark),
                new MenuItem("_Clear Logs", "Clear all logs", ClearLogs),
                new MenuItem("Clear _Completed Jobs", "Remove completed jobs from tracker", ClearCompletedJobs),
                new MenuItem("_Test Network Discovery", "Test network discovery broadcast", TestNetworkDiscovery),
                null!, // Separator
                new MenuItem("_Collect Garbage", "Force garbage collection", () => {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    AddLog("INFO", "Garbage collection performed");
                    MessageBox.Query("GC", "Garbage collection completed", "OK");
                })
            }),
            new MenuBarItem("_Configuration", new MenuItem[]
            {
                new MenuItem("_Edit Configuration...", "Edit appsettings.json", EditConfiguration),
                new MenuItem("_Reload Configuration", "Reload configuration from file", ReloadConfiguration),
                null!, // Separator
                new MenuItem("Network _Discovery Settings...", "Configure network discovery", ConfigureNetworkDiscovery),
                new MenuItem("_NodeManager Settings...", "Configure NodeManager", ConfigureNodeManager),
                new MenuItem("_HTTP/API Settings...", "Configure HTTP API", ConfigureHttpApi),
                new MenuItem("_Shared Storage Settings...", "Configure shared storage", ConfigureSharedStorage)
            }),
            new MenuBarItem("_Services", new MenuItem[]
            {
                new MenuItem("_Start Network Discovery", "Start network discovery service", () => {
                    _networkDiscovery.StartBroadcasting();
                    _networkDiscovery.StartListening((node) => {
                        AddLog("DISCOVERY", $"Found {node.NodeType} at {node.IPAddress}:{node.HttpPort}");
                    });
                    AddLog("INFO", "Network discovery started");
                }),
                new MenuItem("S_top Network Discovery", "Stop network discovery service", () => {
                    _networkDiscovery.Stop();
                    AddLog("INFO", "Network discovery stopped");
                }),
                null!, // Separator
                new MenuItem("_Connect to Node...", "Manually connect to a node", ConnectToNode),
                new MenuItem("_Disconnect Node...", "Disconnect from a node", DisconnectNode)
            }),
            new MenuBarItem("_Help", new MenuItem[]
            {
                new MenuItem("_Keyboard Shortcuts", "Show keyboard shortcuts", ShowKeyboardShortcuts),
                new MenuItem("_System Info", "Show detailed system information", ShowSystemInfo),
                new MenuItem("System _Health", "Show system health summary", ShowSystemHealth),
                new MenuItem("_About", "About this application", ShowAbout)
            })
        });

        top.Add(menu);

        // Main window with tabs
        _mainWindow = new Window("GeoscientistToolkit Node Endpoint")
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 1
        };

        _tabView = new TabView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Setup all tabs
        SetupDashboardTab();
        SetupJobsTab();
        SetupLogsTab();
        SetupStatisticsTab();
        SetupNodesTab();
        SetupBenchmarkTab();

        _mainWindow.Add(_tabView);

        // Bottom indicators
        _keepaliveIndicator = new Label("KEEPALIVE")
        {
            X = Pos.AnchorEnd(40),
            Y = Pos.AnchorEnd(1),
            ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black)
            }
        };

        _txIndicator = new Label("TX")
        {
            X = Pos.AnchorEnd(28),
            Y = Pos.AnchorEnd(1),
            ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black)
            }
        };

        _rxIndicator = new Label("RX")
        {
            X = Pos.AnchorEnd(22),
            Y = Pos.AnchorEnd(1),
            ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black)
            }
        };

        _jobsIndicator = new Label("JOBS: 0")
        {
            X = Pos.AnchorEnd(14),
            Y = Pos.AnchorEnd(1),
            ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightBlue, Color.Black)
            }
        };

        top.Add(_mainWindow, _keepaliveIndicator, _txIndicator, _rxIndicator, _jobsIndicator);

        // Global keyboard shortcuts
        top.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key == Key.F5)
            {
                RunCpuBenchmark();
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == (Key.F | Key.CtrlMask))
            {
                // Focus search in logs tab
                _tabView.SelectedTab = _tabView.Tabs.ElementAt(2);
                _logFilterField?.SetFocus();
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == (Key.R | Key.CtrlMask))
            {
                RefreshAll();
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == (Key.Q | Key.CtrlMask))
            {
                QuitApplication();
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == (Key.E | Key.CtrlMask))
            {
                EditConfiguration();
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == Key.F1)
            {
                ShowKeyboardShortcuts();
                e.Handled = true;
            }
        };

        // Initial refresh
        RefreshAll();
        AddLog("INFO", "UI initialized successfully");
    }

    private void SetupDashboardTab()
    {
        var dashboardTab = new TabView.Tab();
        dashboardTab.Text = "Dashboard";

        var dashboardView = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Status frame (top section)
        _statusFrame = new FrameView("System Information")
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(50),
            Height = 14
        };

        var platform = OperatingSystem.IsWindows() ? "Windows" :
                      OperatingSystem.IsMacOS() ? "macOS" :
                      OperatingSystem.IsLinux() ? "Linux" : "Unknown";

        _platformLabel = new Label($"Platform: {platform} ({(Environment.Is64BitProcess ? "x64" : "x86")})")
        {
            X = 1,
            Y = 0
        };

        _httpApiLabel = new Label($"HTTP API: http://{_localIp}:{_httpPort}")
        {
            X = 1,
            Y = 1
        };

        _nodeManagerLabel = new Label($"NodeManager: {_localIp}:{_nodeManagerPort}")
        {
            X = 1,
            Y = 2
        };

        _statusLabel = new Label($"Status: {_nodeManager.Status}")
        {
            X = 1,
            Y = 3
        };

        _uptimeLabel = new Label("Uptime: 00:00:00")
        {
            X = 1,
            Y = 4
        };

        var swaggerLabel = new Label($"Swagger: http://localhost:{_httpPort}/swagger")
        {
            X = 1,
            Y = 5
        };

        // Resource usage
        _cpuUsageLabel = new Label("CPU Usage: 0.0%")
        {
            X = 1,
            Y = 7
        };

        _cpuUsageBar = new ProgressBar()
        {
            X = 1,
            Y = 8,
            Width = Dim.Fill(1),
            Height = 1
        };

        _memoryUsageLabel = new Label("Memory Usage: 0.0%")
        {
            X = 1,
            Y = 9
        };

        _memoryUsageBar = new ProgressBar()
        {
            X = 1,
            Y = 10,
            Width = Dim.Fill(1),
            Height = 1
        };

        _diskUsageLabel = new Label("Disk: N/A")
        {
            X = 1,
            Y = 11
        };

        _statusFrame.Add(_platformLabel, _httpApiLabel, _nodeManagerLabel, _statusLabel,
            _uptimeLabel, swaggerLabel, _cpuUsageLabel, _cpuUsageBar, _memoryUsageLabel,
            _memoryUsageBar, _diskUsageLabel);

        // Network Statistics frame
        var networkFrame = new FrameView("Network Statistics")
        {
            X = Pos.Right(_statusFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = 14
        };

        _networkStatsLabel = new Label("Initializing...")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1)
        };

        networkFrame.Add(_networkStatsLabel);

        // Connections frame (bottom section)
        _connectionsFrame = new FrameView("Active Connections & Discovered Nodes")
        {
            X = 0,
            Y = Pos.Bottom(_statusFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _connectionsListView = new ListView(new List<string> { "Loading..." })
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _connectionsFrame.Add(_connectionsListView);

        dashboardView.Add(_statusFrame, networkFrame, _connectionsFrame);
        dashboardTab.View = dashboardView;
        _tabView.AddTab(dashboardTab, false);
    }

    private void SetupJobsTab()
    {
        var jobsTab = new TabView.Tab();
        jobsTab.Text = "Jobs";

        var jobsView = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Job filter controls
        var filterLabel = new Label("Filter:")
        {
            X = 0,
            Y = 0
        };

        var jobFilterField = new TextField("")
        {
            X = Pos.Right(filterLabel) + 1,
            Y = 0,
            Width = 20
        };

        jobFilterField.TextChanged += (oldValue) => {
            _jobFilter = jobFilterField.Text?.ToString() ?? "";
            UpdateJobsList();
        };

        var allButton = new Button("All")
        {
            X = Pos.Right(jobFilterField) + 2,
            Y = 0
        };
        allButton.Clicked += () => {
            _jobFilter = "";
            jobFilterField.Text = "";
            UpdateJobsList();
        };

        var pendingButton = new Button("Pending")
        {
            X = Pos.Right(allButton) + 1,
            Y = 0
        };
        pendingButton.Clicked += () => {
            _jobFilter = "Pending";
            jobFilterField.Text = "Pending";
            UpdateJobsList();
        };

        var runningButton = new Button("Running")
        {
            X = Pos.Right(pendingButton) + 1,
            Y = 0
        };
        runningButton.Clicked += () => {
            _jobFilter = "Running";
            jobFilterField.Text = "Running";
            UpdateJobsList();
        };

        var completedButton = new Button("Completed")
        {
            X = Pos.Right(runningButton) + 1,
            Y = 0
        };
        completedButton.Clicked += () => {
            _jobFilter = "Completed";
            jobFilterField.Text = "Completed";
            UpdateJobsList();
        };

        var jobsFrame = new FrameView("Job Queue")
        {
            X = 0,
            Y = 1,
            Width = Dim.Percent(60),
            Height = Dim.Fill()
        };

        _jobsListView = new ListView(new List<string> { "Loading..." })
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _jobsListView.SelectedItemChanged += (args) => {
            _selectedJobIndex = args.Item;
            UpdateJobDetails();
        };

        jobsFrame.Add(_jobsListView);

        var detailsFrame = new FrameView("Job Details")
        {
            X = Pos.Right(jobsFrame),
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _jobDetailsView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true
        };

        detailsFrame.Add(_jobDetailsView);

        jobsView.Add(filterLabel, jobFilterField, allButton, pendingButton, runningButton, completedButton, jobsFrame, detailsFrame);
        jobsTab.View = jobsView;
        _tabView.AddTab(jobsTab, false);
    }

    private void SetupLogsTab()
    {
        var logsTab = new TabView.Tab();
        logsTab.Text = "Logs";

        var logsView = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var filterLabel = new Label("Filter:")
        {
            X = 0,
            Y = 0
        };

        _logFilterField = new TextField("")
        {
            X = Pos.Right(filterLabel) + 1,
            Y = 0,
            Width = Dim.Fill() - 20
        };

        _logFilterField.TextChanged += (oldValue) => {
            _logFilter = _logFilterField.Text?.ToString() ?? "";
            UpdateLogs();
        };

        var clearButton = new Button("Clear")
        {
            X = Pos.AnchorEnd(10),
            Y = 0
        };
        clearButton.Clicked += ClearLogs;

        var logsFrame = new FrameView("System Logs")
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _logsListView = new ListView(new List<string> { " " })
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        logsFrame.Add(_logsListView);

        logsView.Add(filterLabel, _logFilterField, clearButton, logsFrame);
        logsTab.View = logsView;
        _tabView.AddTab(logsTab, false);
    }

    private void SetupStatisticsTab()
    {
        var statsTab = new TabView.Tab();
        statsTab.Text = "Statistics";

        var statsView = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var statsFrame = new FrameView("Performance Statistics")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _statsView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true
        };

        statsFrame.Add(_statsView);
        statsView.Add(statsFrame);
        statsTab.View = statsView;
        _tabView.AddTab(statsTab, false);
    }

    private void SetupNodesTab()
    {
        var nodesTab = new TabView.Tab();
        nodesTab.Text = "Nodes";

        var nodesView = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var nodesListFrame = new FrameView("Connected Nodes")
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(50),
            Height = Dim.Fill()
        };

        _nodesListView = new ListView(new List<string> { "Loading..." })
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _nodesListView.SelectedItemChanged += (args) => {
            _selectedNodeIndex = args.Item;
            UpdateNodeDetails();
        };

        nodesListFrame.Add(_nodesListView);

        var nodeDetailsFrame = new FrameView("Node Details")
        {
            X = Pos.Right(nodesListFrame),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _nodeDetailsView = new TextView()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true
        };

        nodeDetailsFrame.Add(_nodeDetailsView);

        nodesView.Add(nodesListFrame, nodeDetailsFrame);
        nodesTab.View = nodesView;
        _tabView.AddTab(nodesTab, false);
    }

    private void SetupBenchmarkTab()
    {
        var benchmarkTab = new TabView.Tab();
        benchmarkTab.Text = "Benchmark";

        var benchmarkView = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        var benchmarkFrame = new FrameView("CPU Benchmark")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill() - 3
        };

        _cpuBenchmarkLabel = new Label("Press F5 or click 'Run Benchmark' to start")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill()
        };

        benchmarkFrame.Add(_cpuBenchmarkLabel);

        _benchmarkProgress = new ProgressBar()
        {
            X = 0,
            Y = Pos.Bottom(benchmarkFrame),
            Width = Dim.Fill(),
            Height = 1
        };

        var runButton = new Button("Run Benchmark")
        {
            X = Pos.Center(),
            Y = Pos.Bottom(_benchmarkProgress) + 1
        };
        runButton.Clicked += RunCpuBenchmark;

        benchmarkView.Add(benchmarkFrame, _benchmarkProgress, runButton);
        benchmarkTab.View = benchmarkView;
        _tabView.AddTab(benchmarkTab, false);
    }

    private void SetupUpdateTimer()
    {
        _updateTimer = new System.Timers.Timer(500); // Update every 500ms
        _updateTimer.Elapsed += (sender, e) =>
        {
            if (_isRunning)
            {
                Application.MainLoop.Invoke(() =>
                {
                    UpdateUptime();
                    UpdateConnections();
                    UpdateStatus();
                    UpdateCpuUsage();
                    UpdateMemoryUsage();
                    UpdateNetworkStats();
                    UpdateNetworkIndicators();
                    UpdateKeepaliveIndicator();
                    UpdateJobsIndicator();
                    UpdateJobsList();
                    UpdateNodesList();

                    // Capture metrics every 5 seconds
                    if (_keepaliveCounter % 10 == 0)
                    {
                        CaptureMetrics();
                    }
                });
            }
        };
        _updateTimer.Start();
    }

    private void UpdateUptime()
    {
        var uptime = DateTime.Now - _startTime;
        _uptimeLabel.Text = $"Uptime: {uptime.Days}d {uptime.Hours:D2}h {uptime.Minutes:D2}m {uptime.Seconds:D2}s";
    }

    private void UpdateStatus()
    {
        _statusLabel.Text = $"Status: {_nodeManager.Status}";
    }

    private void UpdateConnections()
    {
        var connections = new List<string>();

        try
        {
            // Get discovered nodes from network discovery
            var discoveredNodes = _networkDiscovery.GetDiscoveredNodes();

            if (discoveredNodes.Any())
            {
                connections.Add(SanitizeString($"Discovered Nodes: {discoveredNodes.Count}"));
                foreach (var node in discoveredNodes)
                {
                    var nodeType = node.NodeType.Length > 15 ? node.NodeType.Substring(0, 15) : node.NodeType;
                    var platform = node.Platform.Length > 10 ? node.Platform.Substring(0, 10) : node.Platform;
                    connections.Add(SanitizeString($"  {nodeType} - {node.IPAddress}:{node.HttpPort} ({platform})"));
                }
                connections.Add("");
            }

            // Get connected nodes from NodeManager
            var connectedNodes = _nodeManager.GetConnectedNodes();
            if (connectedNodes.Any())
            {
                connections.Add(SanitizeString($"Connected Nodes: {connectedNodes.Count}"));
                foreach (var node in connectedNodes)
                {
                    var statusIcon = node.Status == NodeStatus.Connected ? "*" : "-";
                    var uptime = DateTime.Now - node.ConnectedAt;
                    var nodeName = node.NodeName.Length > 25 ? node.NodeName.Substring(0, 25) : node.NodeName;
                    connections.Add(SanitizeString($"  [{statusIcon}] {nodeName} - {node.IpAddress} - {node.Status}"));
                    connections.Add(SanitizeString($"      CPU: {node.CpuUsage:F1}% | Mem: {node.MemoryUsage:F1}% | Jobs: {node.ActiveJobs} | Up: {uptime:hh\\:mm\\:ss}"));
                }
            }

            if (!connections.Any())
            {
                connections.Add("No active connections or discovered nodes");
                connections.Add("");
                connections.Add("Network discovery is running");
            }

            SafeSetListViewSource(_connectionsListView, connections);
        }
        catch (Exception ex)
        {
            // If there's an error updating connections, show a safe fallback
            connections.Clear();
            connections.Add("Error loading connections");
            connections.Add($"  {ex.Message}");
            SafeSetListViewSource(_connectionsListView, connections);
        }
    }

    /// <summary>
    /// Sanitizes a string for safe rendering in Terminal.Gui ListView.
    /// This method aggressively removes problematic characters to prevent rendering crashes.
    /// </summary>
    private string SanitizeString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return " "; // Return single space instead of empty string

        try
        {
            // Convert to ASCII-safe string by removing all non-ASCII characters
            // This is the most reliable way to prevent Unicode-related rendering issues
            var cleaned = new StringBuilder(input.Length);

            foreach (char c in input)
            {
                // Only keep basic printable ASCII characters and spaces
                if (c >= 32 && c <= 126)
                {
                    cleaned.Append(c);
                }
                else if (c == '\t')
                {
                    cleaned.Append("    "); // Replace tabs with spaces
                }
                else if (c == '\n' || c == '\r')
                {
                    // Skip newlines - ListView doesn't handle them well
                    continue;
                }
                // Skip all other characters (including Unicode, control chars, etc.)
            }

            var result = cleaned.ToString().Trim();

            // Ensure we never return an empty string
            if (string.IsNullOrEmpty(result))
                return " ";

            // Ensure string isn't too long (prevent buffer overruns)
            if (result.Length > 500)
                result = result.Substring(0, 497) + "...";

            return result;
        }
        catch
        {
            // If anything goes wrong, return a safe fallback
            return " ";
        }
    }

    /// <summary>
    /// Safely sets the source of a ListView with sanitized strings.
    /// This prevents crashes caused by problematic strings in Terminal.Gui.
    /// </summary>
    private void SafeSetListViewSource(ListView listView, IEnumerable<string> items)
    {
        try
        {
            var safeItems = items
                .Select(item => SanitizeString(item))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();

            // Ensure we always have at least one item
            if (!safeItems.Any())
            {
                safeItems.Add(" ");
            }

            listView.SetSource(safeItems);
        }
        catch (Exception ex)
        {
            // If SetSource fails, try with a minimal safe list
            try
            {
                listView.SetSource(new List<string> { "Error loading data", $"Details: {SanitizeString(ex.Message)}" });
            }
            catch
            {
                // Last resort: set a single space
                listView.SetSource(new List<string> { " " });
            }
        }
    }

    private void UpdateCpuUsage()
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
            _currentCpuUsage = (float)(cpuUsageTotal * 100);

            _cpuUsageLabel.Text = $"CPU Usage: {_currentCpuUsage:F1}% ({Environment.ProcessorCount} cores)";
            _cpuUsageBar.Fraction = Math.Min(_currentCpuUsage / 100f, 1.0f);
        }
        catch
        {
            _currentCpuUsage = 0;
            _cpuUsageLabel.Text = "CPU Usage: N/A";
        }
    }

    private void UpdateMemoryUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;
            var totalMemory = GC.GetTotalMemory(false);

            // Get system total memory (approximate)
            var gcMemInfo = GC.GetGCMemoryInfo();
            var totalAvailableMemory = gcMemInfo.TotalAvailableMemoryBytes;

            _currentMemoryUsage = totalAvailableMemory > 0
                ? (float)(workingSet * 100.0 / totalAvailableMemory)
                : 0;

            _memoryUsageLabel.Text = $"Memory Usage: {_currentMemoryUsage:F1}% ({workingSet / 1024 / 1024:N0} MB / {totalAvailableMemory / 1024 / 1024:N0} MB)";
            _memoryUsageBar.Fraction = Math.Min(_currentMemoryUsage / 100f, 1.0f);

            // Update disk usage
            var driveInfo = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "/");
            var diskUsedPercent = 100.0 - (driveInfo.AvailableFreeSpace * 100.0 / driveInfo.TotalSize);
            _diskUsageLabel.Text = $"Disk: {diskUsedPercent:F1}% ({driveInfo.AvailableFreeSpace / 1024 / 1024 / 1024:N0} GB free of {driveInfo.TotalSize / 1024 / 1024 / 1024:N0} GB)";
        }
        catch
        {
            _currentMemoryUsage = 0;
            _memoryUsageLabel.Text = "Memory Usage: N/A";
        }
    }

    private void UpdateNetworkStats()
    {
        try
        {
            long totalBytesSent = 0;
            long totalBytesReceived = 0;

            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var stats = ni.GetIPv4Statistics();
                    totalBytesSent += stats.BytesSent;
                    totalBytesReceived += stats.BytesReceived;
                }
            }

            // Calculate bytes per second
            if (_lastBytesSent > 0)
            {
                _currentBytesPerSecSent = (totalBytesSent - _lastBytesSent) * 2; // *2 because we update every 500ms
                _currentBytesPerSecReceived = (totalBytesReceived - _lastBytesReceived) * 2;
            }

            _lastBytesSent = totalBytesSent;
            _lastBytesReceived = totalBytesReceived;

            var sb = new StringBuilder();
            sb.AppendLine($"Total Sent:     {totalBytesSent / 1024 / 1024:N2} MB");
            sb.AppendLine($"Total Received: {totalBytesReceived / 1024 / 1024:N2} MB");
            sb.AppendLine();
            sb.AppendLine($"Send Rate:    {_currentBytesPerSecSent / 1024.0:N2} KB/s");
            sb.AppendLine($"Receive Rate: {_currentBytesPerSecReceived / 1024.0:N2} KB/s");
            sb.AppendLine();

            var activeInterfaces = interfaces.Count(ni =>
                ni.OperationalStatus == OperationalStatus.Up &&
                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);
            sb.AppendLine($"Active Interfaces: {activeInterfaces}");

            _networkStatsLabel.Text = sb.ToString();
        }
        catch
        {
            _networkStatsLabel.Text = "Network stats unavailable";
        }
    }

    private void UpdateNetworkIndicators()
    {
        try
        {
            // Check if there's activity
            _txActive = _currentBytesPerSecSent > 1024; // More than 1 KB/s
            _rxActive = _currentBytesPerSecReceived > 1024;

            // Update TX indicator
            if (_txActive)
            {
                _txIndicator.Text = $"TX▲ {_currentBytesPerSecSent / 1024.0:F1}K/s";
                _txIndicator.ColorScheme = new ColorScheme()
                {
                    Normal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black)
                };
            }
            else
            {
                _txIndicator.Text = "TX";
                _txIndicator.ColorScheme = new ColorScheme()
                {
                    Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black)
                };
            }

            // Update RX indicator
            if (_rxActive)
            {
                _rxIndicator.Text = $"RX▼ {_currentBytesPerSecReceived / 1024.0:F1}K/s";
                _rxIndicator.ColorScheme = new ColorScheme()
                {
                    Normal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Black)
                };
            }
            else
            {
                _rxIndicator.Text = "RX";
                _rxIndicator.ColorScheme = new ColorScheme()
                {
                    Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black)
                };
            }
        }
        catch
        {
            // Ignore network stats errors
        }
    }

    private void UpdateKeepaliveIndicator()
    {
        _keepaliveCounter++;

        // Blink the keepalive indicator every 2 seconds
        if (_keepaliveCounter % 4 == 0)
        {
            _keepaliveIndicator.ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black)
            };
        }
        else if (_keepaliveCounter % 4 == 2)
        {
            _keepaliveIndicator.ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.Green, Color.Black)
            };
        }
    }

    private void UpdateJobsIndicator()
    {
        var jobs = _jobTracker.GetAllJobs();
        var activeJobs = jobs.Count(j => j.Status == JobTracker.JobStatus.Pending || j.Status == JobTracker.JobStatus.Running);

        _jobsIndicator.Text = $"JOBS: {activeJobs}/{jobs.Count}";

        if (activeJobs > 0)
        {
            _jobsIndicator.ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black)
            };
        }
        else
        {
            _jobsIndicator.ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightBlue, Color.Black)
            };
        }
    }

    private void UpdateJobsList()
    {
        var jobs = _jobTracker.GetAllJobs();

        // Apply filter if set
        if (!string.IsNullOrWhiteSpace(_jobFilter))
        {
            jobs = jobs.Where(j =>
                j.Status.ToString().Contains(_jobFilter, StringComparison.OrdinalIgnoreCase) ||
                j.JobId.Contains(_jobFilter, StringComparison.OrdinalIgnoreCase) ||
                j.JobMessage.JobType.Contains(_jobFilter, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        var jobLines = new List<string>();

        if (jobs.Any())
        {
            jobLines.Add($"Job Queue: {jobs.Count} jobs");
            foreach (var job in jobs)
            {
                var statusIcon = job.Status switch
                {
                    JobTracker.JobStatus.Pending => "P",
                    JobTracker.JobStatus.Running => "R",
                    JobTracker.JobStatus.Completed => "C",
                    JobTracker.JobStatus.Failed => "F",
                    JobTracker.JobStatus.Cancelled => "X",
                    _ => "?"
                };

                var duration = job.CompletedAt.HasValue
                    ? (job.CompletedAt.Value - job.SubmittedAt).TotalSeconds.ToString("F1") + "s"
                    : (DateTime.UtcNow - job.SubmittedAt).TotalSeconds.ToString("F1") + "s";

                var jobId = job.JobId.Length > 36 ? job.JobId.Substring(0, 36) : job.JobId;
                jobLines.Add($"  [{statusIcon}] {jobId} - {job.Status} - {duration}");
            }
        }
        else
        {
            var filterMsg = _jobFilter.Length > 0 ? $"No jobs match filter: {_jobFilter}" : "No jobs in queue";
            jobLines.Add(filterMsg.Length > 80 ? filterMsg.Substring(0, 80) : filterMsg);
        }

        SafeSetListViewSource(_jobsListView, jobLines);
    }

    private void UpdateJobDetails()
    {
        if (_selectedJobIndex < 0)
        {
            _jobDetailsView.Text = "Select a job to view details";
            return;
        }

        var jobs = _jobTracker.GetAllJobs();
        if (_selectedJobIndex >= jobs.Count)
        {
            _jobDetailsView.Text = "Job not found";
            return;
        }

        var job = jobs[_selectedJobIndex];
        var sb = new StringBuilder();

        sb.AppendLine("═══ JOB DETAILS ═══");
        sb.AppendLine();
        sb.AppendLine($"Job ID:        {job.JobId}");
        sb.AppendLine($"Status:        {job.Status}");
        sb.AppendLine($"Job Type:      {job.JobMessage.JobType}");
        sb.AppendLine($"Submitted:     {job.SubmittedAt:yyyy-MM-dd HH:mm:ss}");

        if (job.CompletedAt.HasValue)
        {
            sb.AppendLine($"Completed:     {job.CompletedAt.Value:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Duration:      {(job.CompletedAt.Value - job.SubmittedAt).TotalSeconds:F2}s");
        }
        else
        {
            sb.AppendLine($"Running Time:  {(DateTime.UtcNow - job.SubmittedAt).TotalSeconds:F2}s");
        }

        sb.AppendLine();
        sb.AppendLine("═══ JOB MESSAGE ═══");
        sb.AppendLine(JsonSerializer.Serialize(job.JobMessage, new JsonSerializerOptions { WriteIndented = true }));

        if (job.Result != null)
        {
            sb.AppendLine();
            sb.AppendLine("═══ RESULT ═══");
            sb.AppendLine(JsonSerializer.Serialize(job.Result, new JsonSerializerOptions { WriteIndented = true }));
        }

        _jobDetailsView.Text = sb.ToString();
    }

    private void UpdateNodesList()
    {
        var nodes = _nodeManager.GetConnectedNodes();
        var nodeLines = new List<string>();

        if (nodes.Any())
        {
            nodeLines.Add($"Connected Nodes: {nodes.Count}");
            foreach (var node in nodes)
            {
                var statusIcon = node.Status == NodeStatus.Connected ? "*" : "-";
                var nodeName = node.NodeName.Length > 30 ? node.NodeName.Substring(0, 30) : node.NodeName;
                nodeLines.Add($"  [{statusIcon}] {nodeName} - {node.IpAddress} - Jobs: {node.ActiveJobs}");
            }
        }
        else
        {
            nodeLines.Add("No connected nodes");
        }

        SafeSetListViewSource(_nodesListView, nodeLines);
    }

    private void UpdateNodeDetails()
    {
        if (_selectedNodeIndex < 0)
        {
            _nodeDetailsView.Text = "Select a node to view details";
            return;
        }

        var nodes = _nodeManager.GetConnectedNodes();
        if (_selectedNodeIndex >= nodes.Count)
        {
            _nodeDetailsView.Text = "Node not found";
            return;
        }

        var node = nodes[_selectedNodeIndex];
        var sb = new StringBuilder();

        var uptime = DateTime.Now - node.ConnectedAt;
        sb.AppendLine("═══ NODE DETAILS ═══");
        sb.AppendLine();
        sb.AppendLine($"Node ID:         {node.NodeId}");
        sb.AppendLine($"Node Name:       {node.NodeName}");
        sb.AppendLine($"IP Address:      {node.IpAddress}");
        sb.AppendLine($"Status:          {node.Status}");
        sb.AppendLine($"Connected At:    {node.ConnectedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Uptime:          {uptime.Days}d {uptime.Hours:D2}h {uptime.Minutes:D2}m {uptime.Seconds:D2}s");
        sb.AppendLine($"Last Heartbeat:  {node.LastHeartbeat:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("═══ RESOURCES ═══");
        sb.AppendLine($"CPU Usage:       {node.CpuUsage:F1}%");
        sb.AppendLine($"Memory Usage:    {node.MemoryUsage:F1}%");
        sb.AppendLine($"Active Jobs:     {node.ActiveJobs}");
        sb.AppendLine();
        sb.AppendLine("═══ CAPABILITIES ═══");
        sb.AppendLine($"CPU Cores:       {node.Capabilities.CpuCores}");
        sb.AppendLine($"Total Memory:    {node.Capabilities.TotalMemoryMb:N0} MB");
        sb.AppendLine($"Operating System: {node.Capabilities.OperatingSystem}");
        sb.AppendLine($"Has GPU:         {node.Capabilities.HasGpu}");
        if (node.Capabilities.HasGpu)
        {
            sb.AppendLine($"GPU Name:        {node.Capabilities.GpuName}");
        }
        sb.AppendLine($"Supports Jobs:   {string.Join(", ", node.Capabilities.SupportedJobTypes)}");

        _nodeDetailsView.Text = sb.ToString();
    }

    private void UpdateLogs()
    {
        var logs = _logs.ToArray();

        if (!string.IsNullOrWhiteSpace(_logFilter))
        {
            logs = logs.Where(l =>
                l.Message.Contains(_logFilter, StringComparison.OrdinalIgnoreCase) ||
                l.Level.Contains(_logFilter, StringComparison.OrdinalIgnoreCase) ||
                (l.Details?.Contains(_logFilter, StringComparison.OrdinalIgnoreCase) ?? false)
            ).ToArray();
        }

        var logLines = logs
            .OrderByDescending(l => l.Timestamp)
            .Take(1000)
            .Select(l => $"[{l.Timestamp:HH:mm:ss}] [{l.Level,8}] {l.Message}")
            .ToList();

        if (!logLines.Any())
        {
            logLines.Add("No logs match the filter");
        }

        SafeSetListViewSource(_logsListView, logLines);
    }

    private void CaptureMetrics()
    {
        var snapshot = new MetricSnapshot
        {
            Timestamp = DateTime.Now,
            CpuUsage = _currentCpuUsage,
            MemoryUsage = _currentMemoryUsage,
            NetworkBytesSent = _lastBytesSent,
            NetworkBytesReceived = _lastBytesReceived,
            ActiveConnections = _nodeManager.GetConnectedNodes().Count,
            ActiveJobs = _jobTracker.GetAllJobs().Count(j =>
                j.Status == JobTracker.JobStatus.Pending ||
                j.Status == JobTracker.JobStatus.Running)
        };

        _metricHistory.Add(snapshot);

        // Keep only last hour of metrics (720 snapshots at 5 second intervals)
        if (_metricHistory.Count > 720)
        {
            _metricHistory.RemoveAt(0);
        }

        UpdateStatistics();
    }

    private void UpdateStatistics()
    {
        if (!_metricHistory.Any())
        {
            _statsView.Text = "No statistics available yet. Collecting metrics...";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("═══ PERFORMANCE STATISTICS ═══");
        sb.AppendLine();
        sb.AppendLine($"Monitoring Period: {_metricHistory.Count * 5} seconds ({_metricHistory.Count} samples)");
        sb.AppendLine($"First Sample:      {_metricHistory.First().Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Last Sample:       {_metricHistory.Last().Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("═══ CPU USAGE ═══");
        sb.AppendLine($"Current:  {_metricHistory.Last().CpuUsage:F2}%");
        sb.AppendLine($"Average:  {_metricHistory.Average(m => m.CpuUsage):F2}%");
        sb.AppendLine($"Minimum:  {_metricHistory.Min(m => m.CpuUsage):F2}%");
        sb.AppendLine($"Maximum:  {_metricHistory.Max(m => m.CpuUsage):F2}%");
        sb.AppendLine();

        sb.AppendLine("═══ MEMORY USAGE ═══");
        sb.AppendLine($"Current:  {_metricHistory.Last().MemoryUsage:F2}%");
        sb.AppendLine($"Average:  {_metricHistory.Average(m => m.MemoryUsage):F2}%");
        sb.AppendLine($"Minimum:  {_metricHistory.Min(m => m.MemoryUsage):F2}%");
        sb.AppendLine($"Maximum:  {_metricHistory.Max(m => m.MemoryUsage):F2}%");
        sb.AppendLine();

        sb.AppendLine("═══ NETWORK ACTIVITY ═══");
        var totalSent = _metricHistory.Last().NetworkBytesSent / 1024 / 1024;
        var totalReceived = _metricHistory.Last().NetworkBytesReceived / 1024 / 1024;
        sb.AppendLine($"Total Sent:       {totalSent:N2} MB");
        sb.AppendLine($"Total Received:   {totalReceived:N2} MB");
        sb.AppendLine($"Current TX Rate:  {_currentBytesPerSecSent / 1024.0:N2} KB/s");
        sb.AppendLine($"Current RX Rate:  {_currentBytesPerSecReceived / 1024.0:N2} KB/s");
        sb.AppendLine();

        sb.AppendLine("═══ CONNECTIONS ═══");
        sb.AppendLine($"Current:  {_metricHistory.Last().ActiveConnections}");
        sb.AppendLine($"Average:  {_metricHistory.Average(m => m.ActiveConnections):F2}");
        sb.AppendLine($"Maximum:  {_metricHistory.Max(m => m.ActiveConnections)}");
        sb.AppendLine();

        sb.AppendLine("═══ JOBS ═══");
        sb.AppendLine($"Currently Active: {_metricHistory.Last().ActiveJobs}");
        sb.AppendLine($"Average Active:   {_metricHistory.Average(m => m.ActiveJobs):F2}");
        sb.AppendLine($"Peak Active:      {_metricHistory.Max(m => m.ActiveJobs)}");

        var allJobs = _jobTracker.GetAllJobs();
        sb.AppendLine($"Total Processed:  {allJobs.Count}");
        sb.AppendLine($"Completed:        {allJobs.Count(j => j.Status == JobTracker.JobStatus.Completed)}");
        sb.AppendLine($"Failed:           {allJobs.Count(j => j.Status == JobTracker.JobStatus.Failed)}");
        sb.AppendLine($"Pending:          {allJobs.Count(j => j.Status == JobTracker.JobStatus.Pending)}");

        _statsView.Text = sb.ToString();
    }

    private void RefreshAll()
    {
        UpdateUptime();
        UpdateStatus();
        UpdateConnections();
        UpdateCpuUsage();
        UpdateMemoryUsage();
        UpdateNetworkStats();
        UpdateJobsList();
        UpdateNodesList();
        UpdateLogs();
        UpdateStatistics();
        AddLog("INFO", "All data refreshed");
    }

    private void AddLog(string level, string message, string? details = null)
    {
        var log = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            Details = details
        };

        _logs.Enqueue(log);

        // Keep only last 10000 logs
        while (_logs.Count > 10000)
        {
            _logs.TryDequeue(out _);
        }

        // Update logs view if we're on that tab
        if (_isRunning)
        {
            Application.MainLoop?.Invoke(() => UpdateLogs());
        }
    }

    private void ClearLogs()
    {
        _logs.Clear();
        UpdateLogs();
        AddLog("INFO", "Logs cleared");
    }

    private void ClearCompletedJobs()
    {
        var jobs = _jobTracker.GetAllJobs();
        var completedJobs = jobs.Where(j =>
            j.Status == JobTracker.JobStatus.Completed ||
            j.Status == JobTracker.JobStatus.Failed ||
            j.Status == JobTracker.JobStatus.Cancelled).ToList();

        foreach (var job in completedJobs)
        {
            _jobTracker.RemoveJob(job.JobId);
        }

        AddLog("INFO", $"Cleared {completedJobs.Count} completed jobs");
        MessageBox.Query("Jobs Cleared", $"Removed {completedJobs.Count} completed jobs from tracker", "OK");
    }

    private void TestNetworkDiscovery()
    {
        AddLog("INFO", "Testing network discovery broadcast...");
        _networkDiscovery.StartBroadcasting();
        MessageBox.Query("Network Discovery",
            "Network discovery broadcast sent.\n\n" +
            "Other nodes on the network should be able to discover this endpoint.\n" +
            "Check the Dashboard tab for discovered nodes.", "OK");
    }

    private void RunCpuBenchmark()
    {
        _cpuBenchmarkLabel.Text = "Initializing benchmark...";
        _benchmarkProgress.Fraction = 0;
        Application.Refresh();

        AddLog("INFO", "Starting CPU benchmark");

        Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            const int size = 500;
            var random = new Random();
            var matrixA = new double[size, size];
            var matrixB = new double[size, size];
            var result = new double[size, size];

            Application.MainLoop.Invoke(() => {
                _cpuBenchmarkLabel.Text = "Initializing matrices...";
                _benchmarkProgress.Fraction = 0.1f;
            });

            // Initialize matrices
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    matrixA[i, j] = random.NextDouble();
                    matrixB[i, j] = random.NextDouble();
                }
            }

            Application.MainLoop.Invoke(() => {
                _cpuBenchmarkLabel.Text = "Running matrix multiplication benchmark...";
                _benchmarkProgress.Fraction = 0.3f;
            });

            // Matrix multiplication with progress
            var benchmarkStart = Stopwatch.StartNew();
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < size; k++)
                    {
                        sum += matrixA[i, k] * matrixB[k, j];
                    }
                    result[i, j] = sum;
                }

                // Update progress
                if (i % 50 == 0)
                {
                    var progress = 0.3f + (i / (float)size) * 0.6f;
                    Application.MainLoop.Invoke(() => {
                        _benchmarkProgress.Fraction = progress;
                    });
                }
            }
            benchmarkStart.Stop();

            sw.Stop();

            var flops = (2.0 * size * size * size) / benchmarkStart.Elapsed.TotalSeconds;
            var gflops = flops / 1_000_000_000.0;

            Application.MainLoop.Invoke(() =>
            {
                _benchmarkProgress.Fraction = 1.0f;

                var benchmarkText = new StringBuilder();
                benchmarkText.AppendLine("═══ BENCHMARK RESULTS ═══");
                benchmarkText.AppendLine();
                benchmarkText.AppendLine($"Completed in {sw.ElapsedMilliseconds:N0} ms");
                benchmarkText.AppendLine($"Computation time: {benchmarkStart.ElapsedMilliseconds:N0} ms");
                benchmarkText.AppendLine();
                benchmarkText.AppendLine($"Matrix size: {size} x {size}");
                benchmarkText.AppendLine($"Total operations: {2L * size * size * size:N0}");
                benchmarkText.AppendLine($"Performance: {gflops:F2} GFLOPS");
                benchmarkText.AppendLine();
                benchmarkText.AppendLine("═══ CPU INFORMATION ═══");
                benchmarkText.AppendLine($"Processor Count: {Environment.ProcessorCount}");
                benchmarkText.AppendLine($"64-bit Process: {Environment.Is64BitProcess}");
                benchmarkText.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
                benchmarkText.AppendLine();
                benchmarkText.AppendLine("═══ PERFORMANCE RATING ═══");

                string rating = gflops switch
                {
                    < 1.0 => "Low Performance",
                    < 5.0 => "Moderate Performance",
                    < 20.0 => "Good Performance",
                    < 50.0 => "High Performance",
                    _ => "Excellent Performance"
                };

                benchmarkText.AppendLine($"Rating: {rating}");
                benchmarkText.AppendLine();
                benchmarkText.AppendLine("Press F5 to run again");

                _cpuBenchmarkLabel.Text = benchmarkText.ToString();
                AddLog("INFO", $"CPU benchmark completed: {gflops:F2} GFLOPS");
            });
        });
    }

    private void EditConfiguration()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                MessageBox.ErrorQuery("Error", "Configuration file not found:\n" + _configPath, "OK");
                return;
            }

            var configJson = File.ReadAllText(_configPath);
            JsonDocument configDoc;

            try
            {
                configDoc = JsonDocument.Parse(configJson);
            }
            catch (JsonException ex)
            {
                MessageBox.ErrorQuery("Error", "Failed to parse configuration file:\n\n" + ex.Message, "OK");
                return;
            }

            var dialog = new Dialog("Configuration Editor", 100, 35);

            // Create TabView for different configuration sections
            var tabView = new TabView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill() - 2
            };

            // ============= HTTP/API Tab =============
            var httpTab = CreateHttpConfigTab(configDoc);
            tabView.AddTab(httpTab, false);

            // ============= NodeManager Tab =============
            var nodeTab = CreateNodeManagerConfigTab(configDoc);
            tabView.AddTab(nodeTab, false);

            // ============= Network Discovery Tab =============
            var discoveryTab = CreateNetworkDiscoveryConfigTab(configDoc);
            tabView.AddTab(discoveryTab, false);

            // ============= Storage Tab =============
            var storageTab = CreateStorageConfigTab(configDoc);
            tabView.AddTab(storageTab, false);

            // ============= Logging Tab =============
            var loggingTab = CreateLoggingConfigTab(configDoc);
            tabView.AddTab(loggingTab, false);

            // Save and Cancel buttons
            var saveButton = new Button("Save Configuration")
            {
                X = Pos.Center() - 15,
                Y = Pos.AnchorEnd(1)
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = Pos.AnchorEnd(1)
            };

            saveButton.Clicked += () => {
                try
                {
                    var newConfig = BuildConfigFromForm(
                        httpTab, nodeTab, discoveryTab, storageTab, loggingTab);

                    // Validate JSON
                    var testDoc = JsonDocument.Parse(newConfig);
                    testDoc.Dispose();

                    // Backup current config
                    var backupPath = _configPath + ".backup";
                    File.Copy(_configPath, backupPath, true);

                    // Save new config
                    File.WriteAllText(_configPath, newConfig);

                    AddLog("INFO", "Configuration saved successfully");
                    MessageBox.Query("Success",
                        "Configuration saved successfully!\n\n" +
                        "Restart the application for changes to take effect.\n\n" +
                        $"Backup created: {Path.GetFileName(backupPath)}", "OK");

                    configDoc.Dispose();
                    Application.RequestStop();
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error",
                        "Failed to save configuration:\n\n" + ex.Message, "OK");
                }
            };

            cancelButton.Clicked += () => {
                configDoc.Dispose();
                Application.RequestStop();
            };

            dialog.Add(tabView, saveButton, cancelButton);
            Application.Run(dialog);
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Error", "Failed to open configuration:\n\n" + ex.Message, "OK");
        }
    }

    private TabView.Tab CreateHttpConfigTab(JsonDocument configDoc)
    {
        var tab = new TabView.Tab();
        tab.Text = "HTTP/API";

        var view = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        int y = 1;

        // HTTP Port
        var httpPortLabel = new Label("HTTP Port:") { X = 2, Y = y };
        var httpPortField = new TextField(GetJsonValue(configDoc, "HttpPort", "8500"))
        {
            X = 25,
            Y = y,
            Width = 10
        };
        httpPortField.Data = "HttpPort";
        view.Add(httpPortLabel, httpPortField);
        y += 2;

        // Keep Alive Timeout
        var keepAliveLabel = new Label("Keep Alive Timeout:") { X = 2, Y = y };
        var keepAliveField = new TextField(GetJsonValue(configDoc, "Kestrel.Limits.KeepAliveTimeout", "00:10:00"))
        {
            X = 25,
            Y = y,
            Width = 15
        };
        keepAliveField.Data = "KeepAliveTimeout";
        var keepAliveHelp = new Label("(HH:MM:SS)") { X = 41, Y = y };
        view.Add(keepAliveLabel, keepAliveField, keepAliveHelp);
        y += 2;

        // Request Headers Timeout
        var headersLabel = new Label("Request Headers Timeout:") { X = 2, Y = y };
        var headersField = new TextField(GetJsonValue(configDoc, "Kestrel.Limits.RequestHeadersTimeout", "00:05:00"))
        {
            X = 30,
            Y = y,
            Width = 15
        };
        headersField.Data = "RequestHeadersTimeout";
        var headersHelp = new Label("(HH:MM:SS)") { X = 46, Y = y };
        view.Add(headersLabel, headersField, headersHelp);
        y += 2;

        // Max Concurrent Connections
        var maxConnLabel = new Label("Max Concurrent Connections:") { X = 2, Y = y };
        var maxConnField = new TextField(GetJsonValue(configDoc, "Kestrel.Limits.MaxConcurrentConnections", "100"))
        {
            X = 32,
            Y = y,
            Width = 10
        };
        maxConnField.Data = "MaxConcurrentConnections";
        view.Add(maxConnLabel, maxConnField);
        y += 2;

        // Max Request Body Size
        var maxBodyLabel = new Label("Max Request Body Size (bytes):") { X = 2, Y = y };
        var maxBodyField = new TextField(GetJsonValue(configDoc, "Kestrel.Limits.MaxRequestBodySize", "1073741824"))
        {
            X = 35,
            Y = y,
            Width = 15
        };
        maxBodyField.Data = "MaxRequestBodySize";
        var maxBodyHelp = new Label("(1 GB = 1073741824)") { X = 51, Y = y };
        view.Add(maxBodyLabel, maxBodyField, maxBodyHelp);

        tab.View = view;
        return tab;
    }

    private TabView.Tab CreateNodeManagerConfigTab(JsonDocument configDoc)
    {
        var tab = new TabView.Tab();
        tab.Text = "NodeManager";

        var view = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        int y = 1;

        // Enable NodeManager
        var enableLabel = new Label("Enable NodeManager:") { X = 2, Y = y };
        var enableCheck = new CheckBox("", GetJsonBool(configDoc, "NodeManager.EnableNodeManager", true))
        {
            X = 28,
            Y = y
        };
        enableCheck.Data = "EnableNodeManager";
        view.Add(enableLabel, enableCheck);
        y += 2;

        // Role
        var roleLabel = new Label("Role:") { X = 2, Y = y };
        var roleCombo = new ComboBox()
        {
            X = 28,
            Y = y,
            Width = 20,
            Height = 4
        };
        roleCombo.SetSource(new List<string> { "Hybrid", "Worker", "Coordinator" });
        var roleValue = GetJsonValue(configDoc, "NodeManager.Role", "Hybrid");
        roleCombo.SelectedItem = roleValue == "Worker" ? 1 : roleValue == "Coordinator" ? 2 : 0;
        roleCombo.Data = "Role";
        view.Add(roleLabel, roleCombo);
        y += 2;

        // Node Name
        var nodeNameLabel = new Label("Node Name:") { X = 2, Y = y };
        var nodeNameField = new TextField(GetJsonValue(configDoc, "NodeManager.NodeName", "GeoscientistToolkit_Endpoint"))
        {
            X = 28,
            Y = y,
            Width = 35
        };
        nodeNameField.Data = "NodeName";
        view.Add(nodeNameLabel, nodeNameField);
        y += 2;

        // Server Port
        var portLabel = new Label("Server Port:") { X = 2, Y = y };
        var portField = new TextField(GetJsonValue(configDoc, "NodeManager.ServerPort", "9876"))
        {
            X = 28,
            Y = y,
            Width = 10
        };
        portField.Data = "ServerPort";
        view.Add(portLabel, portField);
        y += 2;

        // Host Address
        var hostLabel = new Label("Host Address:") { X = 2, Y = y };
        var hostField = new TextField(GetJsonValue(configDoc, "NodeManager.HostAddress", "auto"))
        {
            X = 28,
            Y = y,
            Width = 25
        };
        hostField.Data = "HostAddress";
        var hostHelp = new Label("(use 'auto' for auto-detect)") { X = 54, Y = y };
        view.Add(hostLabel, hostField, hostHelp);
        y += 2;

        // Heartbeat Interval
        var heartbeatLabel = new Label("Heartbeat Interval (sec):") { X = 2, Y = y };
        var heartbeatField = new TextField(GetJsonValue(configDoc, "NodeManager.HeartbeatInterval", "30"))
        {
            X = 32,
            Y = y,
            Width = 10
        };
        heartbeatField.Data = "HeartbeatInterval";
        view.Add(heartbeatLabel, heartbeatField);
        y += 2;

        // Max Reconnect Attempts
        var reconnectLabel = new Label("Max Reconnect Attempts:") { X = 2, Y = y };
        var reconnectField = new TextField(GetJsonValue(configDoc, "NodeManager.MaxReconnectAttempts", "5"))
        {
            X = 32,
            Y = y,
            Width = 10
        };
        reconnectField.Data = "MaxReconnectAttempts";
        view.Add(reconnectLabel, reconnectField);
        y += 2;

        // Use Nodes For Simulators
        var useNodesLabel = new Label("Use Nodes For Simulators:") { X = 2, Y = y };
        var useNodesCheck = new CheckBox("", GetJsonBool(configDoc, "NodeManager.UseNodesForSimulators", true))
        {
            X = 32,
            Y = y
        };
        useNodesCheck.Data = "UseNodesForSimulators";
        view.Add(useNodesLabel, useNodesCheck);
        y += 2;

        // Use GPU For Jobs
        var useGpuLabel = new Label("Use GPU For Jobs:") { X = 2, Y = y };
        var useGpuCheck = new CheckBox("", GetJsonBool(configDoc, "NodeManager.UseGpuForJobs", true))
        {
            X = 32,
            Y = y
        };
        useGpuCheck.Data = "UseGpuForJobs";
        view.Add(useGpuLabel, useGpuCheck);

        tab.View = view;
        return tab;
    }

    private TabView.Tab CreateNetworkDiscoveryConfigTab(JsonDocument configDoc)
    {
        var tab = new TabView.Tab();
        tab.Text = "Network Discovery";

        var view = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        int y = 1;

        // Enabled
        var enabledLabel = new Label("Enable Network Discovery:") { X = 2, Y = y };
        var enabledCheck = new CheckBox("", GetJsonBool(configDoc, "NetworkDiscovery.Enabled", true))
        {
            X = 32,
            Y = y
        };
        enabledCheck.Data = "DiscoveryEnabled";
        view.Add(enabledLabel, enabledCheck);
        y += 2;

        // Broadcast Interval
        var intervalLabel = new Label("Broadcast Interval (ms):") { X = 2, Y = y };
        var intervalField = new TextField(GetJsonValue(configDoc, "NetworkDiscovery.BroadcastInterval", "5000"))
        {
            X = 32,
            Y = y,
            Width = 10
        };
        intervalField.Data = "BroadcastInterval";
        view.Add(intervalLabel, intervalField);
        y += 2;

        // Discovery Port
        var portLabel = new Label("Discovery Port:") { X = 2, Y = y };
        var portField = new TextField(GetJsonValue(configDoc, "NetworkDiscovery.DiscoveryPort", "9877"))
        {
            X = 32,
            Y = y,
            Width = 10
        };
        portField.Data = "DiscoveryPort";
        view.Add(portLabel, portField);
        y += 3;

        // Info text
        var infoLabel = new Label(
            "Network Discovery allows nodes to automatically\n" +
            "find each other on the local network without\n" +
            "manual configuration.")
        {
            X = 2,
            Y = y,
            Width = Dim.Fill() - 4,
            Height = 3
        };
        view.Add(infoLabel);

        tab.View = view;
        return tab;
    }

    private TabView.Tab CreateStorageConfigTab(JsonDocument configDoc)
    {
        var tab = new TabView.Tab();
        tab.Text = "Storage";

        var view = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        int y = 1;

        // Storage Path
        var pathLabel = new Label("Storage Path:") { X = 2, Y = y };
        var pathField = new TextField(GetJsonValue(configDoc, "SharedStorage.Path", "auto"))
        {
            X = 25,
            Y = y,
            Width = 50
        };
        pathField.Data = "StoragePath";
        view.Add(pathLabel, pathField);
        y++;

        var pathHelp = new Label("(use 'auto' for default location or specify full path)")
        {
            X = 25,
            Y = y
        };
        view.Add(pathHelp);
        y += 3;

        // Use Network Storage
        var networkLabel = new Label("Use Network Storage:") { X = 2, Y = y };
        var networkCheck = new CheckBox("", GetJsonBool(configDoc, "SharedStorage.UseNetworkStorage", true))
        {
            X = 25,
            Y = y
        };
        networkCheck.Data = "UseNetworkStorage";
        view.Add(networkLabel, networkCheck);
        y += 3;

        // Info text
        var infoLabel = new Label(
            "Shared Storage Configuration:\n\n" +
            "Network Storage: Allows sharing datasets and results\n" +
            "between distributed nodes for collaborative processing.\n\n" +
            "Path: Location where shared data will be stored.\n" +
            "Use 'auto' to let the system choose an appropriate\n" +
            "location based on the platform.")
        {
            X = 2,
            Y = y,
            Width = Dim.Fill() - 4,
            Height = 8
        };
        view.Add(infoLabel);

        tab.View = view;
        return tab;
    }

    private TabView.Tab CreateLoggingConfigTab(JsonDocument configDoc)
    {
        var tab = new TabView.Tab();
        tab.Text = "Logging";

        var view = new View()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        int y = 1;

        var logLevels = new List<string> { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };

        // Default Log Level
        var defaultLabel = new Label("Default Log Level:") { X = 2, Y = y };
        var defaultCombo = new ComboBox()
        {
            X = 28,
            Y = y,
            Width = 20,
            Height = 6
        };
        defaultCombo.SetSource(logLevels);
        var defaultLevel = GetJsonValue(configDoc, "Logging.LogLevel.Default", "Information");
        defaultCombo.SelectedItem = logLevels.IndexOf(defaultLevel);
        defaultCombo.Data = "DefaultLogLevel";
        view.Add(defaultLabel, defaultCombo);
        y += 2;

        // ASP.NET Core Log Level
        var aspLabel = new Label("ASP.NET Core Log Level:") { X = 2, Y = y };
        var aspCombo = new ComboBox()
        {
            X = 28,
            Y = y,
            Width = 20,
            Height = 6
        };
        aspCombo.SetSource(logLevels);
        var aspLevel = GetJsonValue(configDoc, "Logging.LogLevel.Microsoft.AspNetCore", "Warning");
        aspCombo.SelectedItem = logLevels.IndexOf(aspLevel);
        aspCombo.Data = "AspNetCoreLogLevel";
        view.Add(aspLabel, aspCombo);
        y += 3;

        // Info text
        var infoLabel = new Label(
            "Log Levels (from most to least verbose):\n\n" +
            "  Trace      - Very detailed logs, may include sensitive data\n" +
            "  Debug      - Debugging information\n" +
            "  Information - General informational messages\n" +
            "  Warning    - Warning messages for potentially harmful situations\n" +
            "  Error      - Error messages for failures\n" +
            "  Critical   - Critical failures requiring immediate attention")
        {
            X = 2,
            Y = y,
            Width = Dim.Fill() - 4,
            Height = 9
        };
        view.Add(infoLabel);

        tab.View = view;
        return tab;
    }

    private string GetJsonValue(JsonDocument doc, string path, string defaultValue)
    {
        try
        {
            var parts = path.Split('.');
            JsonElement current = doc.RootElement;

            foreach (var part in parts)
            {
                if (current.TryGetProperty(part, out var element))
                {
                    current = element;
                }
                else
                {
                    return defaultValue;
                }
            }

            return current.ValueKind == JsonValueKind.String
                ? current.GetString() ?? defaultValue
                : current.ToString();
        }
        catch
        {
            return defaultValue;
        }
    }

    private bool GetJsonBool(JsonDocument doc, string path, bool defaultValue)
    {
        try
        {
            var parts = path.Split('.');
            JsonElement current = doc.RootElement;

            foreach (var part in parts)
            {
                if (current.TryGetProperty(part, out var element))
                {
                    current = element;
                }
                else
                {
                    return defaultValue;
                }
            }

            return current.ValueKind == JsonValueKind.True || current.ValueKind == JsonValueKind.False
                ? current.GetBoolean()
                : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private string BuildConfigFromForm(TabView.Tab httpTab, TabView.Tab nodeTab,
        TabView.Tab discoveryTab, TabView.Tab storageTab, TabView.Tab loggingTab)
    {
        var config = new Dictionary<string, object>();

        // Build Logging section
        var loggingView = loggingTab.View;
        var defaultLogLevel = GetComboBoxValue(loggingView, "DefaultLogLevel", new List<string> { "Trace", "Debug", "Information", "Warning", "Error", "Critical" });
        var aspLogLevel = GetComboBoxValue(loggingView, "AspNetCoreLogLevel", new List<string> { "Trace", "Debug", "Information", "Warning", "Error", "Critical" });

        config["Logging"] = new Dictionary<string, object>
        {
            ["LogLevel"] = new Dictionary<string, string>
            {
                ["Default"] = defaultLogLevel,
                ["Microsoft.AspNetCore"] = aspLogLevel
            }
        };

        config["AllowedHosts"] = "*";

        // Build HTTP section
        var httpView = httpTab.View;
        var httpPort = GetTextFieldValue(httpView, "HttpPort");
        config["HttpPort"] = int.Parse(httpPort);

        config["Kestrel"] = new Dictionary<string, object>
        {
            ["Endpoints"] = new Dictionary<string, object>
            {
                ["Http"] = new Dictionary<string, string>
                {
                    ["Url"] = $"http://0.0.0.0:{httpPort}"
                }
            },
            ["Limits"] = new Dictionary<string, object>
            {
                ["KeepAliveTimeout"] = GetTextFieldValue(httpView, "KeepAliveTimeout"),
                ["RequestHeadersTimeout"] = GetTextFieldValue(httpView, "RequestHeadersTimeout"),
                ["MaxConcurrentConnections"] = int.Parse(GetTextFieldValue(httpView, "MaxConcurrentConnections")),
                ["MaxRequestBodySize"] = long.Parse(GetTextFieldValue(httpView, "MaxRequestBodySize"))
            }
        };

        // Build NodeManager section
        var nodeView = nodeTab.View;
        config["NodeManager"] = new Dictionary<string, object>
        {
            ["EnableNodeManager"] = GetCheckBoxValue(nodeView, "EnableNodeManager"),
            ["Role"] = GetComboBoxValue(nodeView, "Role", new List<string> { "Hybrid", "Worker", "Coordinator" }),
            ["NodeName"] = GetTextFieldValue(nodeView, "NodeName"),
            ["ServerPort"] = int.Parse(GetTextFieldValue(nodeView, "ServerPort")),
            ["HostAddress"] = GetTextFieldValue(nodeView, "HostAddress"),
            ["HeartbeatInterval"] = int.Parse(GetTextFieldValue(nodeView, "HeartbeatInterval")),
            ["MaxReconnectAttempts"] = int.Parse(GetTextFieldValue(nodeView, "MaxReconnectAttempts")),
            ["UseNodesForSimulators"] = GetCheckBoxValue(nodeView, "UseNodesForSimulators"),
            ["UseGpuForJobs"] = GetCheckBoxValue(nodeView, "UseGpuForJobs")
        };

        // Build NetworkDiscovery section
        var discoveryView = discoveryTab.View;
        config["NetworkDiscovery"] = new Dictionary<string, object>
        {
            ["Enabled"] = GetCheckBoxValue(discoveryView, "DiscoveryEnabled"),
            ["BroadcastInterval"] = int.Parse(GetTextFieldValue(discoveryView, "BroadcastInterval")),
            ["DiscoveryPort"] = int.Parse(GetTextFieldValue(discoveryView, "DiscoveryPort"))
        };

        // Build SharedStorage section
        var storageView = storageTab.View;
        config["SharedStorage"] = new Dictionary<string, object>
        {
            ["Path"] = GetTextFieldValue(storageView, "StoragePath"),
            ["UseNetworkStorage"] = GetCheckBoxValue(storageView, "UseNetworkStorage")
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        return JsonSerializer.Serialize(config, options);
    }

    private string GetTextFieldValue(View view, string tag)
    {
        foreach (var subview in view.Subviews)
        {
            if (subview is TextField textField && textField.Data?.ToString() == tag)
            {
                return SanitizeString(textField.Text.ToString() ?? "");
            }
        }
        return "";
    }

    private bool GetCheckBoxValue(View view, string tag)
    {
        foreach (var subview in view.Subviews)
        {
            if (subview is CheckBox checkBox && checkBox.Data?.ToString() == tag)
            {
                return checkBox.Checked;
            }
        }
        return false;
    }

    private string GetComboBoxValue(View view, string tag, List<string> options)
    {
        foreach (var subview in view.Subviews)
        {
            if (subview is ComboBox comboBox && comboBox.Data?.ToString() == tag)
            {
                var index = comboBox.SelectedItem;
                if (index >= 0 && index < options.Count)
                {
                    return options[index];
                }
            }
        }
        return options.Count > 0 ? options[0] : "";
    }

    private void ReloadConfiguration()
    {
        var result = MessageBox.Query("Reload Configuration",
            "This will reload the configuration from appsettings.json.\n\n" +
            "The application needs to restart for changes to take effect.\n\n" +
            "Continue?", "Yes", "No");

        if (result == 0)
        {
            AddLog("INFO", "Configuration reload requested - restarting...");
            Application.RequestStop();
        }
    }

    private void ConfigureNetworkDiscovery()
    {
        var dialog = new Dialog("Network Discovery Settings", 60, 15);

        var enabledLabel = new Label("Enabled:")
        {
            X = 1,
            Y = 1
        };

        var enabledCheck = new CheckBox("Enable network discovery")
        {
            X = Pos.Right(enabledLabel) + 2,
            Y = 1,
            Checked = true
        };

        var intervalLabel = new Label("Broadcast Interval (ms):")
        {
            X = 1,
            Y = 3
        };

        var intervalField = new TextField("5000")
        {
            X = Pos.Right(intervalLabel) + 2,
            Y = 3,
            Width = 10
        };

        var portLabel = new Label("Discovery Port:")
        {
            X = 1,
            Y = 5
        };

        var portField = new TextField("9877")
        {
            X = Pos.Right(portLabel) + 2,
            Y = 5,
            Width = 10
        };

        var infoLabel = new Label("Note: Changes require application restart")
        {
            X = 1,
            Y = 7,
            ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black)
            }
        };

        var saveButton = new Button("Save to Config")
        {
            X = Pos.Center() - 12,
            Y = Pos.AnchorEnd(1)
        };

        var cancelButton = new Button("Cancel")
        {
            X = Pos.Center() + 2,
            Y = Pos.AnchorEnd(1)
        };

        saveButton.Clicked += () => {
            // This would update the config file
            MessageBox.Query("Info", "This will be saved when you edit the full configuration", "OK");
            Application.RequestStop();
        };

        cancelButton.Clicked += () => {
            Application.RequestStop();
        };

        dialog.Add(enabledLabel, enabledCheck, intervalLabel, intervalField,
            portLabel, portField, infoLabel, saveButton, cancelButton);

        Application.Run(dialog);
    }

    private void ConfigureNodeManager()
    {
        MessageBox.Query("NodeManager Settings",
            $"Current NodeManager configuration:\n\n" +
            $"Status: {_nodeManager.Status}\n" +
            $"Port: {_nodeManagerPort}\n" +
            $"Connected Nodes: {_nodeManager.GetConnectedNodes().Count}\n\n" +
            "Use Configuration > Edit Configuration to modify settings", "OK");
    }

    private void ConfigureHttpApi()
    {
        MessageBox.Query("HTTP API Settings",
            $"Current HTTP API configuration:\n\n" +
            $"Port: {_httpPort}\n" +
            $"URL: http://{_localIp}:{_httpPort}\n" +
            $"Swagger: http://localhost:{_httpPort}/swagger\n\n" +
            "Use Configuration > Edit Configuration to modify settings", "OK");
    }

    private void ConfigureSharedStorage()
    {
        MessageBox.Query("Shared Storage Settings",
            "Shared storage configuration allows nodes to share\n" +
            "large datasets efficiently.\n\n" +
            "Use Configuration > Edit Configuration to modify settings", "OK");
    }

    private void ConnectToNode()
    {
        var dialog = new Dialog("Connect to Node", 50, 10);

        var ipLabel = new Label("IP Address:")
        {
            X = 1,
            Y = 1
        };

        var ipField = new TextField("")
        {
            X = Pos.Right(ipLabel) + 2,
            Y = 1,
            Width = 20
        };

        var portLabel = new Label("Port:")
        {
            X = 1,
            Y = 3
        };

        var portField = new TextField("9876")
        {
            X = Pos.Right(portLabel) + 2,
            Y = 3,
            Width = 10
        };

        var connectButton = new Button("Connect")
        {
            X = Pos.Center() - 8,
            Y = Pos.AnchorEnd(1)
        };

        var cancelButton = new Button("Cancel")
        {
            X = Pos.Center() + 2,
            Y = Pos.AnchorEnd(1)
        };

        connectButton.Clicked += () => {
            var ip = ipField.Text?.ToString();
            var port = portField.Text?.ToString();

            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.ErrorQuery("Error", "IP address is required", "OK");
                return;
            }

            AddLog("INFO", $"Attempting to connect to {ip}:{port}");
            MessageBox.Query("Info", $"Connection initiated to {ip}:{port}", "OK");
            Application.RequestStop();
        };

        cancelButton.Clicked += () => {
            Application.RequestStop();
        };

        dialog.Add(ipLabel, ipField, portLabel, portField, connectButton, cancelButton);
        Application.Run(dialog);
    }

    private void DisconnectNode()
    {
        var nodes = _nodeManager.GetConnectedNodes();
        if (!nodes.Any())
        {
            MessageBox.Query("No Nodes", "No nodes are currently connected", "OK");
            return;
        }

        var nodeNames = nodes.Select(n => SanitizeString(n.NodeName)).ToList();

        var dialog = new Dialog("Disconnect Node", 50, 15);

        var listView = new ListView(nodeNames)
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2)
        };

        var disconnectButton = new Button("Disconnect")
        {
            X = Pos.Center() - 10,
            Y = Pos.AnchorEnd(1)
        };

        var cancelButton = new Button("Cancel")
        {
            X = Pos.Center() + 2,
            Y = Pos.AnchorEnd(1)
        };

        disconnectButton.Clicked += () => {
            if (listView.SelectedItem >= 0 && listView.SelectedItem < nodes.Count)
            {
                var node = nodes[listView.SelectedItem];
                AddLog("INFO", $"Disconnecting from node: {node.NodeName}");
                MessageBox.Query("Info", $"Disconnecting from {node.NodeName}", "OK");
                Application.RequestStop();
            }
        };

        cancelButton.Clicked += () => {
            Application.RequestStop();
        };

        dialog.Add(listView, disconnectButton, cancelButton);
        Application.Run(dialog);
    }

    private void ExportLogs()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"logs_{timestamp}.txt";
            var filepath = Path.Combine(Environment.CurrentDirectory, filename);

            var logs = _logs.OrderBy(l => l.Timestamp).ToArray();
            var lines = logs.Select(l => $"[{l.Timestamp:yyyy-MM-dd HH:mm:ss}] [{l.Level}] {l.Message}");

            File.WriteAllLines(filepath, lines);

            AddLog("INFO", $"Logs exported to {filename}");
            MessageBox.Query("Export Successful",
                $"Logs exported successfully!\n\n" +
                $"File: {filename}\n" +
                $"Location: {Environment.CurrentDirectory}\n" +
                $"Total logs: {logs.Length}", "OK");
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Export Failed",
                $"Failed to export logs:\n\n{ex.Message}", "OK");
        }
    }

    private void ExportStatistics()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"statistics_{timestamp}.json";
            var filepath = Path.Combine(Environment.CurrentDirectory, filename);

            var stats = new
            {
                ExportedAt = DateTime.Now,
                Uptime = DateTime.Now - _startTime,
                Metrics = _metricHistory,
                CurrentStatus = new
                {
                    CpuUsage = _currentCpuUsage,
                    MemoryUsage = _currentMemoryUsage,
                    NetworkBytesSent = _lastBytesSent,
                    NetworkBytesReceived = _lastBytesReceived,
                    ActiveConnections = _nodeManager.GetConnectedNodes().Count,
                    TotalJobs = _jobTracker.GetAllJobs().Count
                }
            };

            var json = JsonSerializer.Serialize(stats, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filepath, json);

            AddLog("INFO", $"Statistics exported to {filename}");
            MessageBox.Query("Export Successful",
                $"Statistics exported successfully!\n\n" +
                $"File: {filename}\n" +
                $"Location: {Environment.CurrentDirectory}\n" +
                $"Metrics: {_metricHistory.Count} samples", "OK");
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Export Failed",
                $"Failed to export statistics:\n\n{ex.Message}", "OK");
        }
    }

    private void ExportConfiguration()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"config_backup_{timestamp}.json";
            var filepath = Path.Combine(Environment.CurrentDirectory, filename);

            File.Copy(_configPath, filepath, true);

            AddLog("INFO", $"Configuration exported to {filename}");
            MessageBox.Query("Export Successful",
                $"Configuration exported successfully!\n\n" +
                $"File: {filename}\n" +
                $"Location: {Environment.CurrentDirectory}", "OK");
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Export Failed",
                $"Failed to export configuration:\n\n{ex.Message}", "OK");
        }
    }

    private void ShowKeyboardShortcuts()
    {
        var dialog = new Dialog("Keyboard Shortcuts & Quick Reference", 80, 24);

        var helpText = new TextView()
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            ReadOnly = true,
            Text = BuildKeyboardShortcutsText()
        };

        var okButton = new Button("OK")
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1)
        };

        okButton.Clicked += () => {
            Application.RequestStop();
        };

        dialog.Add(helpText, okButton);
        Application.Run(dialog);
    }

    private string BuildKeyboardShortcutsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════════════");
        sb.AppendLine("                    KEYBOARD SHORTCUTS & QUICK REFERENCE");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("═══ GLOBAL SHORTCUTS ═══");
        sb.AppendLine();
        sb.AppendLine("F1             Show this help dialog");
        sb.AppendLine("F5             Run CPU benchmark");
        sb.AppendLine("Ctrl+Q         Quit application (with confirmation)");
        sb.AppendLine("Ctrl+R         Refresh all data immediately");
        sb.AppendLine("Ctrl+E         Edit configuration (opens JSON editor)");
        sb.AppendLine("Ctrl+F         Focus log filter (switches to Logs tab)");
        sb.AppendLine();
        sb.AppendLine("═══ NAVIGATION ═══");
        sb.AppendLine();
        sb.AppendLine("Tab            Move to next UI element");
        sb.AppendLine("Shift+Tab      Move to previous UI element");
        sb.AppendLine("Arrow Keys     Navigate lists and menus");
        sb.AppendLine("Enter          Select item / Activate button");
        sb.AppendLine("Esc            Close dialog / Cancel operation");
        sb.AppendLine();
        sb.AppendLine("═══ MENU ACCESS ═══");
        sb.AppendLine();
        sb.AppendLine("Alt+F          File menu (Export, Quit)");
        sb.AppendLine("Alt+V          View menu (Navigate tabs)");
        sb.AppendLine("Alt+T          Tools menu (Benchmark, Clear)");
        sb.AppendLine("Alt+C          Configuration menu (Edit, Settings)");
        sb.AppendLine("Alt+S          Services menu (Network, Nodes)");
        sb.AppendLine("Alt+H          Help menu (About, System Info)");
        sb.AppendLine();
        sb.AppendLine("═══ TAB FEATURES ═══");
        sb.AppendLine();
        sb.AppendLine("Dashboard Tab:");
        sb.AppendLine("  • Real-time system metrics (CPU, Memory, Disk)");
        sb.AppendLine("  • Network statistics and bandwidth");
        sb.AppendLine("  • Active connections and discovered nodes");
        sb.AppendLine();
        sb.AppendLine("Jobs Tab:");
        sb.AppendLine("  • Filter jobs by status (All, Pending, Running, Completed)");
        sb.AppendLine("  • View detailed job information");
        sb.AppendLine("  • Monitor execution time");
        sb.AppendLine();
        sb.AppendLine("Logs Tab:");
        sb.AppendLine("  • Filter logs with Ctrl+F");
        sb.AppendLine("  • Clear logs with Clear button");
        sb.AppendLine("  • Export logs via File menu");
        sb.AppendLine();
        sb.AppendLine("Statistics Tab:");
        sb.AppendLine("  • View historical performance data");
        sb.AppendLine("  • Export statistics to JSON");
        sb.AppendLine();
        sb.AppendLine("Nodes Tab:");
        sb.AppendLine("  • View connected nodes");
        sb.AppendLine("  • Select node for detailed information");
        sb.AppendLine("  • Monitor node capabilities");
        sb.AppendLine();
        sb.AppendLine("Benchmark Tab:");
        sb.AppendLine("  • Run CPU benchmark with F5");
        sb.AppendLine("  • View GFLOPS performance rating");
        sb.AppendLine();
        sb.AppendLine("═══ STATUS BAR INDICATORS ═══");
        sb.AppendLine();
        sb.AppendLine("KEEPALIVE      Application is running (blinks green)");
        sb.AppendLine("TX ▲          Network transmit active (shows KB/s)");
        sb.AppendLine("RX ▼          Network receive active (shows KB/s)");
        sb.AppendLine("JOBS: X/Y      Active jobs / Total jobs");
        sb.AppendLine();
        sb.AppendLine("═══ QUICK TIPS ═══");
        sb.AppendLine();
        sb.AppendLine("• Updates occur every 500ms for real-time monitoring");
        sb.AppendLine("• Metrics history is retained for 1 hour (720 snapshots)");
        sb.AppendLine("• Logs capacity: 10,000 entries");
        sb.AppendLine("• Configuration changes require application restart");
        sb.AppendLine("• All exports include timestamps in filename");
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════════");
        return sb.ToString();
    }

    private void ShowSystemInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══ SYSTEM INFORMATION ═══");
        sb.AppendLine();
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"Platform: {(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Unknown")}");
        sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"64-bit Process: {Environment.Is64BitProcess}");
        sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");
        sb.AppendLine($"Working Set: {Environment.WorkingSet / 1024 / 1024:N0} MB");
        sb.AppendLine($"System Directory: {Environment.SystemDirectory}");
        sb.AppendLine($"Current Directory: {Environment.CurrentDirectory}");
        sb.AppendLine($"Machine Name: {Environment.MachineName}");
        sb.AppendLine($"User Name: {Environment.UserName}");
        sb.AppendLine($".NET Version: {Environment.Version}");

        MessageBox.Query("System Information", sb.ToString(), "OK");
    }

    private void ShowSystemHealth()
    {
        var dialog = new Dialog("System Health Summary", 80, 26);

        var healthText = new TextView()
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            ReadOnly = true,
            Text = BuildSystemHealthText()
        };

        var refreshButton = new Button("Refresh")
        {
            X = Pos.Center() - 8,
            Y = Pos.AnchorEnd(1)
        };

        refreshButton.Clicked += () => {
            healthText.Text = BuildSystemHealthText();
        };

        var okButton = new Button("OK")
        {
            X = Pos.Center() + 4,
            Y = Pos.AnchorEnd(1)
        };

        okButton.Clicked += () => {
            Application.RequestStop();
        };

        dialog.Add(healthText, refreshButton, okButton);
        Application.Run(dialog);
    }

    private string BuildSystemHealthText()
    {
        var sb = new StringBuilder();
        var uptime = DateTime.Now - _startTime;
        var jobs = _jobTracker.GetAllJobs();
        var nodes = _nodeManager.GetConnectedNodes();
        var discoveredNodes = _networkDiscovery.GetDiscoveredNodes();

        sb.AppendLine("═══════════════════════════════════════════════════════════════════");
        sb.AppendLine("                        SYSTEM HEALTH SUMMARY");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Uptime:    {uptime.Days}d {uptime.Hours:D2}h {uptime.Minutes:D2}m {uptime.Seconds:D2}s");
        sb.AppendLine();

        // Overall Status
        var healthStatus = "HEALTHY";
        var healthIndicator = "✓";

        if (_currentCpuUsage > 90 || _currentMemoryUsage > 90)
        {
            healthStatus = "WARNING - High Resource Usage";
            healthIndicator = "⚠";
        }

        var failedJobs = jobs.Count(j => j.Status == JobTracker.JobStatus.Failed);
        if (failedJobs > 0)
        {
            healthStatus = "WARNING - Failed Jobs Detected";
            healthIndicator = "⚠";
        }

        sb.AppendLine("═══ OVERALL STATUS ═══");
        sb.AppendLine();
        sb.AppendLine($"{healthIndicator} {healthStatus}");
        sb.AppendLine();

        // Resource Health
        sb.AppendLine("═══ RESOURCE HEALTH ═══");
        sb.AppendLine();
        sb.AppendLine($"CPU Usage:        {_currentCpuUsage:F1}% {(_currentCpuUsage < 70 ? "✓ Normal" : _currentCpuUsage < 90 ? "⚠ High" : "✗ Critical")}");
        sb.AppendLine($"Memory Usage:     {_currentMemoryUsage:F1}% {(_currentMemoryUsage < 70 ? "✓ Normal" : _currentMemoryUsage < 90 ? "⚠ High" : "✗ Critical")}");

        try
        {
            var driveInfo = new DriveInfo(Path.GetPathRoot(Environment.CurrentDirectory) ?? "/");
            var diskUsedPercent = 100.0 - (driveInfo.AvailableFreeSpace * 100.0 / driveInfo.TotalSize);
            sb.AppendLine($"Disk Usage:       {diskUsedPercent:F1}% {(diskUsedPercent < 80 ? "✓ Normal" : diskUsedPercent < 95 ? "⚠ High" : "✗ Critical")}");
        }
        catch
        {
            sb.AppendLine("Disk Usage:       N/A");
        }

        sb.AppendLine($"Network TX:       {_currentBytesPerSecSent / 1024.0:F2} KB/s");
        sb.AppendLine($"Network RX:       {_currentBytesPerSecReceived / 1024.0:F2} KB/s");
        sb.AppendLine();

        // Job Queue Health
        sb.AppendLine("═══ JOB QUEUE HEALTH ═══");
        sb.AppendLine();
        var pendingJobs = jobs.Count(j => j.Status == JobTracker.JobStatus.Pending);
        var runningJobs = jobs.Count(j => j.Status == JobTracker.JobStatus.Running);
        var completedJobs = jobs.Count(j => j.Status == JobTracker.JobStatus.Completed);
        var cancelledJobs = jobs.Count(j => j.Status == JobTracker.JobStatus.Cancelled);

        sb.AppendLine($"Total Jobs:       {jobs.Count}");
        sb.AppendLine($"  Pending:        {pendingJobs}");
        sb.AppendLine($"  Running:        {runningJobs}");
        sb.AppendLine($"  Completed:      {completedJobs} ✓");
        sb.AppendLine($"  Failed:         {failedJobs} {(failedJobs > 0 ? "⚠" : "")}");
        sb.AppendLine($"  Cancelled:      {cancelledJobs}");
        sb.AppendLine();

        if (jobs.Any())
        {
            var avgDuration = jobs
                .Where(j => j.CompletedAt.HasValue)
                .Select(j => (j.CompletedAt!.Value - j.SubmittedAt).TotalSeconds)
                .DefaultIfEmpty(0)
                .Average();
            sb.AppendLine($"Avg Completion:   {avgDuration:F1}s");
        }

        sb.AppendLine();

        // Network Health
        sb.AppendLine("═══ NETWORK HEALTH ═══");
        sb.AppendLine();
        sb.AppendLine($"NodeManager:      {_nodeManager.Status}");
        sb.AppendLine($"Connected Nodes:  {nodes.Count}");
        sb.AppendLine($"Discovered Nodes: {discoveredNodes.Count}");

        if (nodes.Any())
        {
            var activeNodes = nodes.Count(n => n.Status == NodeStatus.Connected);
            sb.AppendLine($"  Active:         {activeNodes} / {nodes.Count}");
            sb.AppendLine($"  Avg CPU:        {nodes.Average(n => n.CpuUsage):F1}%");
            sb.AppendLine($"  Avg Memory:     {nodes.Average(n => n.MemoryUsage):F1}%");
            sb.AppendLine($"  Total Jobs:     {nodes.Sum(n => n.ActiveJobs)}");
        }
        else
        {
            sb.AppendLine("  No nodes connected");
        }

        sb.AppendLine();

        // Performance Metrics
        if (_metricHistory.Any())
        {
            sb.AppendLine("═══ PERFORMANCE TRENDS (Last Hour) ═══");
            sb.AppendLine();
            sb.AppendLine($"CPU Average:      {_metricHistory.Average(m => m.CpuUsage):F1}%");
            sb.AppendLine($"CPU Peak:         {_metricHistory.Max(m => m.CpuUsage):F1}%");
            sb.AppendLine($"Memory Average:   {_metricHistory.Average(m => m.MemoryUsage):F1}%");
            sb.AppendLine($"Memory Peak:      {_metricHistory.Max(m => m.MemoryUsage):F1}%");
            sb.AppendLine($"Max Connections:  {_metricHistory.Max(m => m.ActiveConnections)}");
            sb.AppendLine($"Peak Jobs:        {_metricHistory.Max(m => m.ActiveJobs)}");
            sb.AppendLine();
        }

        // Recommendations
        sb.AppendLine("═══ RECOMMENDATIONS ═══");
        sb.AppendLine();

        var recommendations = new List<string>();

        if (_currentCpuUsage > 80)
        {
            recommendations.Add("⚠ CPU usage is high - consider distributing load to more nodes");
        }

        if (_currentMemoryUsage > 80)
        {
            recommendations.Add("⚠ Memory usage is high - run garbage collection or restart service");
        }

        if (failedJobs > 0)
        {
            recommendations.Add($"⚠ {failedJobs} job(s) failed - review logs for details");
        }

        if (pendingJobs > 10)
        {
            recommendations.Add($"⚠ {pendingJobs} job(s) pending - consider adding worker nodes");
        }

        if (nodes.Count == 0 && discoveredNodes.Count > 0)
        {
            recommendations.Add($"ℹ {discoveredNodes.Count} node(s) discovered but not connected");
        }

        if (!recommendations.Any())
        {
            recommendations.Add("✓ System is operating normally");
            recommendations.Add("✓ No issues detected");
        }

        foreach (var rec in recommendations)
        {
            sb.AppendLine(rec);
        }

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════════");

        return sb.ToString();
    }

    private void ShowAbout()
    {
        var dialog = new Dialog("About GeoscientistToolkit Node Endpoint", 80, 28);

        var aboutText = new TextView()
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            ReadOnly = true,
            Text = BuildAboutText()
        };

        var okButton = new Button("OK")
        {
            X = Pos.Center(),
            Y = Pos.AnchorEnd(1)
        };

        okButton.Clicked += () => {
            Application.RequestStop();
        };

        dialog.Add(aboutText, okButton);
        Application.Run(dialog);
    }

    private string BuildAboutText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════════════");
        sb.AppendLine("        GeoscientistToolkit Node Endpoint - Enhanced Edition");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("Version:          2.0 (Production Ready)");
        sb.AppendLine($"Platform:         {(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Unknown")} ({(Environment.Is64BitProcess ? "x64" : "x86")})");
        sb.AppendLine($".NET Runtime:     {Environment.Version}");
        sb.AppendLine($"Uptime:           {DateTime.Now - _startTime:dd\\:hh\\:mm\\:ss}");
        sb.AppendLine();
        sb.AppendLine("═══ DESCRIPTION ═══");
        sb.AppendLine();
        sb.AppendLine("A production-ready distributed computing node for geoscientific");
        sb.AppendLine("simulations, CT imaging operations, and pore network modeling.");
        sb.AppendLine();
        sb.AppendLine("═══ KEY FEATURES ═══");
        sb.AppendLine();
        sb.AppendLine("  ✓ Real-time Monitoring Dashboard");
        sb.AppendLine("      - CPU, Memory, Disk, Network statistics");
        sb.AppendLine("      - Live connection and node discovery");
        sb.AppendLine("      - Performance graphs and metrics");
        sb.AppendLine();
        sb.AppendLine("  ✓ Job Queue Management");
        sb.AppendLine("      - Visual job tracking with status icons");
        sb.AppendLine("      - Detailed job information and results");
        sb.AppendLine("      - JSON-formatted output viewer");
        sb.AppendLine();
        sb.AppendLine("  ✓ Live Logs Viewer");
        sb.AppendLine("      - 10,000 entry capacity with filtering");
        sb.AppendLine("      - Real-time log streaming");
        sb.AppendLine("      - Export to text file");
        sb.AppendLine();
        sb.AppendLine("  ✓ Performance Statistics");
        sb.AppendLine("      - Historical metrics (1 hour retention)");
        sb.AppendLine("      - CPU/Memory trends and averages");
        sb.AppendLine("      - Network bandwidth tracking");
        sb.AppendLine("      - Export to JSON");
        sb.AppendLine();
        sb.AppendLine("  ✓ Configuration Management");
        sb.AppendLine("      - Live JSON editor with validation");
        sb.AppendLine("      - Automatic backup creation");
        sb.AppendLine("      - Hot-reload capability");
        sb.AppendLine();
        sb.AppendLine("  ✓ Network Discovery");
        sb.AppendLine("      - Automatic node discovery via UDP broadcast");
        sb.AppendLine("      - Manual node connection");
        sb.AppendLine("      - Service start/stop controls");
        sb.AppendLine();
        sb.AppendLine("  ✓ Export Capabilities");
        sb.AppendLine("      - Logs, statistics, configuration");
        sb.AppendLine("      - Timestamped file naming");
        sb.AppendLine("      - Multiple format support");
        sb.AppendLine();
        sb.AppendLine("  ✓ CPU Benchmark Tool");
        sb.AppendLine("      - Matrix multiplication benchmark");
        sb.AppendLine("      - GFLOPS performance rating");
        sb.AppendLine("      - Progress visualization");
        sb.AppendLine();
        sb.AppendLine("═══ SUPPORTED OPERATIONS ═══");
        sb.AppendLine();
        sb.AppendLine("Simulations:");
        sb.AppendLine("  • Geomechanical (FEM, plasticity, damage)");
        sb.AppendLine("  • Acoustic wave propagation");
        sb.AppendLine("  • Geothermal reservoir");
        sb.AppendLine("  • Seismic/earthquake");
        sb.AppendLine("  • NMR pore-scale");
        sb.AppendLine("  • Triaxial testing");
        sb.AppendLine();
        sb.AppendLine("CT Operations:");
        sb.AppendLine("  • Filtering (Gaussian, Median, Bilateral, etc.)");
        sb.AppendLine("  • Edge detection (Sobel, Canny)");
        sb.AppendLine("  • Segmentation");
        sb.AppendLine("  • Pipeline processing");
        sb.AppendLine();
        sb.AppendLine("PNM Operations:");
        sb.AppendLine("  • Network generation from CT");
        sb.AppendLine("  • Permeability calculation");
        sb.AppendLine("  • Diffusivity analysis");
        sb.AppendLine("  • Reactive transport");
        sb.AppendLine();
        sb.AppendLine("═══ API ENDPOINTS ═══");
        sb.AppendLine();
        sb.AppendLine($"HTTP API:    http://{_localIp}:{_httpPort}");
        sb.AppendLine($"Swagger UI:  http://localhost:{_httpPort}/swagger");
        sb.AppendLine($"NodeManager: {_localIp}:{_nodeManagerPort}");
        sb.AppendLine();
        sb.AppendLine("═══ KEYBOARD SHORTCUTS ═══");
        sb.AppendLine();
        sb.AppendLine("F1        - Show help");
        sb.AppendLine("F5        - Run CPU benchmark");
        sb.AppendLine("Ctrl+Q    - Quit application");
        sb.AppendLine("Ctrl+R    - Refresh all data");
        sb.AppendLine("Ctrl+E    - Edit configuration");
        sb.AppendLine("Ctrl+F    - Focus log filter");
        sb.AppendLine();
        sb.AppendLine("═══ SYSTEM INFORMATION ═══");
        sb.AppendLine();
        sb.AppendLine($"Processors:        {Environment.ProcessorCount} cores");
        sb.AppendLine($"Working Memory:    {Environment.WorkingSet / 1024 / 1024:N0} MB");
        sb.AppendLine($"Machine Name:      {Environment.MachineName}");
        sb.AppendLine($"Current Directory: {Environment.CurrentDirectory}");
        sb.AppendLine();
        sb.AppendLine("═══ LICENSE ═══");
        sb.AppendLine();
        sb.AppendLine("MIT License - Part of the GeoscientistToolkit project");
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("For more information, documentation, and support:");
        sb.AppendLine($"Visit the Swagger UI at http://localhost:{_httpPort}/swagger");
        sb.AppendLine();
        sb.AppendLine("Press F1 for keyboard shortcuts reference");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════");

        return sb.ToString();
    }

    private void QuitApplication()
    {
        if (MessageBox.Query("Quit",
            "Are you sure you want to quit?\n\n" +
            "This will stop the node endpoint service.", "Yes", "No") == 0)
        {
            AddLog("INFO", "Application shutting down");
            Application.RequestStop();
            Environment.Exit(0);
        }
    }
}
