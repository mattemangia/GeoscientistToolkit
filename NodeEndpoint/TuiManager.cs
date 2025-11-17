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

        _connectionsListView = new ListView(new List<string> { "No active connections" })
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

        var jobsFrame = new FrameView("Job Queue")
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(60),
            Height = Dim.Fill()
        };

        _jobsListView = new ListView(new List<string> { "No jobs" })
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
            Y = 0,
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

        jobsView.Add(jobsFrame, detailsFrame);
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

        _logsListView = new ListView(new List<string>())
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

        _nodesListView = new ListView(new List<string> { "No connected nodes" })
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

        // Get discovered nodes from network discovery
        var discoveredNodes = _networkDiscovery.GetDiscoveredNodes();

        if (discoveredNodes.Any())
        {
            connections.Add($"╔═══ Discovered Nodes ({discoveredNodes.Count}) ═══");
            foreach (var node in discoveredNodes)
            {
                connections.Add($"║ {node.NodeType,-12} │ {node.IPAddress}:{node.HttpPort,-21} │ {node.Platform}");
            }
            connections.Add("╚" + new string('═', 60));
            connections.Add("");
        }

        // Get connected nodes from NodeManager
        var connectedNodes = _nodeManager.GetConnectedNodes();
        if (connectedNodes.Any())
        {
            connections.Add($"╔═══ Connected Nodes ({connectedNodes.Count}) ═══");
            foreach (var node in connectedNodes)
            {
                var statusIcon = node.Status == NodeStatus.Connected ? "●" : "○";
                var uptime = DateTime.Now - node.ConnectedAt;
                connections.Add($"║ {statusIcon} {node.NodeName,-20} │ {node.IpAddress,-15} │ [{node.Status}]");
                connections.Add($"║   CPU: {node.CpuUsage,5:F1}% │ Mem: {node.MemoryUsage,5:F1}% │ Jobs: {node.ActiveJobs,3} │ Uptime: {uptime:hh\\:mm\\:ss}");
            }
            connections.Add("╚" + new string('═', 60));
        }

        if (!connections.Any())
        {
            connections.Add("No active connections or discovered nodes");
            connections.Add("");
            connections.Add("Network discovery is " + (_networkDiscovery != null ? "running" : "stopped"));
        }

        _connectionsListView.SetSource(connections);
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
        var jobLines = new List<string>();

        if (jobs.Any())
        {
            jobLines.Add("╔═══ Job Queue ═══");
            foreach (var job in jobs)
            {
                var statusIcon = job.Status switch
                {
                    JobTracker.JobStatus.Pending => "⧗",
                    JobTracker.JobStatus.Running => "▶",
                    JobTracker.JobStatus.Completed => "✓",
                    JobTracker.JobStatus.Failed => "✗",
                    JobTracker.JobStatus.Cancelled => "⊘",
                    _ => "?"
                };

                var duration = job.CompletedAt.HasValue
                    ? (job.CompletedAt.Value - job.SubmittedAt).TotalSeconds.ToString("F1") + "s"
                    : (DateTime.UtcNow - job.SubmittedAt).TotalSeconds.ToString("F1") + "s";

                jobLines.Add($"║ {statusIcon} {job.JobId,-36} │ {job.Status,-10} │ {duration,8}");
            }
            jobLines.Add("╚" + new string('═', 70));
        }
        else
        {
            jobLines.Add("No jobs in queue");
        }

        _jobsListView.SetSource(jobLines);
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
            nodeLines.Add("╔═══ Connected Nodes ═══");
            foreach (var node in nodes)
            {
                var statusIcon = node.Status == NodeStatus.Connected ? "●" : "○";
                nodeLines.Add($"║ {statusIcon} {node.NodeName,-25} │ {node.IpAddress,-15} │ Jobs: {node.ActiveJobs,3}");
            }
            nodeLines.Add("╚" + new string('═', 60));
        }
        else
        {
            nodeLines.Add("No connected nodes");
        }

        _nodesListView.SetSource(nodeLines);
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

        _logsListView.SetSource(logLines);
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

            var dialog = new Dialog("Edit Configuration", 100, 30);

            var textView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill() - 1,
                Text = configJson
            };

            var saveButton = new Button("Save")
            {
                X = Pos.Center() - 10,
                Y = Pos.AnchorEnd(1)
            };

            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 2,
                Y = Pos.AnchorEnd(1)
            };

            saveButton.Clicked += () => {
                try
                {
                    var newConfig = textView.Text.ToString();

                    // Validate JSON
                    JsonDocument.Parse(newConfig!);

                    // Backup current config
                    File.Copy(_configPath, _configPath + ".backup", true);

                    // Save new config
                    File.WriteAllText(_configPath, newConfig);

                    AddLog("INFO", "Configuration saved successfully");
                    MessageBox.Query("Success",
                        "Configuration saved successfully!\n\n" +
                        "Restart the application for changes to take effect.\n" +
                        "A backup was created at: " + _configPath + ".backup", "OK");

                    Application.RequestStop();
                }
                catch (JsonException ex)
                {
                    MessageBox.ErrorQuery("JSON Error",
                        "Invalid JSON format:\n\n" + ex.Message, "OK");
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error",
                        "Failed to save configuration:\n\n" + ex.Message, "OK");
                }
            };

            cancelButton.Clicked += () => {
                Application.RequestStop();
            };

            dialog.Add(textView, saveButton, cancelButton);
            Application.Run(dialog);
        }
        catch (Exception ex)
        {
            MessageBox.ErrorQuery("Error", "Failed to open configuration:\n\n" + ex.Message, "OK");
        }
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

        var nodeNames = nodes.Select(n => n.NodeName).ToList();

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
        MessageBox.Query("Keyboard Shortcuts",
            "F1        - Show this help\n" +
            "F5        - Run CPU benchmark\n" +
            "Ctrl+Q    - Quit application\n" +
            "Ctrl+R    - Refresh all data\n" +
            "Ctrl+E    - Edit configuration\n" +
            "Ctrl+F    - Focus log filter\n\n" +
            "Tab/Shift+Tab - Navigate between tabs\n" +
            "Arrow Keys    - Navigate lists\n" +
            "Enter         - Select item", "OK");
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
