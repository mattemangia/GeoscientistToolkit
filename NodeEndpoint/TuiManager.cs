using Terminal.Gui;
using GeoscientistToolkit.Network;
using System.Diagnostics;
using System.Text;
using System.Net.NetworkInformation;

namespace GeoscientistToolkit.NodeEndpoint;

public class TuiManager
{
    private readonly NodeManager _nodeManager;
    private readonly Services.NetworkDiscoveryService _networkDiscovery;
    private readonly int _httpPort;
    private readonly int _nodeManagerPort;
    private readonly string _localIp;

    private Window _mainWindow = null!;
    private ListView _connectionsListView = null!;
    private Label _statusLabel = null!;
    private Label _cpuBenchmarkLabel = null!;
    private Label _platformLabel = null!;
    private Label _httpApiLabel = null!;
    private Label _nodeManagerLabel = null!;
    private Label _uptimeLabel = null!;
    private FrameView _connectionsFrame = null!;
    private FrameView _statusFrame = null!;
    private FrameView _benchmarkFrame = null!;
    private ProgressBar _cpuUsageBar = null!;
    private Label _cpuUsageLabel = null!;
    private Label _keepaliveIndicator = null!;
    private Label _txIndicator = null!;
    private Label _rxIndicator = null!;

    private DateTime _startTime;
    private System.Timers.Timer? _updateTimer;
    private bool _isRunning;
    private float _currentCpuUsage;
    private long _lastBytesSent;
    private long _lastBytesReceived;
    private bool _txActive;
    private bool _rxActive;
    private int _keepaliveCounter;

    public TuiManager(
        NodeManager nodeManager,
        Services.NetworkDiscoveryService networkDiscovery,
        int httpPort,
        int nodeManagerPort,
        string localIp)
    {
        _nodeManager = nodeManager;
        _networkDiscovery = networkDiscovery;
        _httpPort = httpPort;
        _nodeManagerPort = nodeManagerPort;
        _localIp = localIp;
        _startTime = DateTime.Now;
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
                new MenuItem("_Quit", "Exit the application", () =>
                {
                    if (MessageBox.Query("Quit", "Are you sure you want to quit?", "Yes", "No") == 0)
                    {
                        Application.RequestStop();
                    }
                })
            }),
            new MenuBarItem("_Tools", new MenuItem[]
            {
                new MenuItem("Run CPU _Benchmark", "Run a fast CPU benchmark", RunCpuBenchmark),
                new MenuItem("_Refresh", "Refresh all data", RefreshData)
            }),
            new MenuBarItem("_Configuration", new MenuItem[]
            {
                new MenuItem("_HTTP Port", $"Current: {_httpPort}", () =>
                {
                    MessageBox.Query("HTTP Port", $"HTTP API Port: {_httpPort}\n\nTo change this port, edit appsettings.json", "OK");
                }),
                new MenuItem("_NodeManager Port", $"Current: {_nodeManagerPort}", () =>
                {
                    MessageBox.Query("NodeManager Port", $"NodeManager Port: {_nodeManagerPort}\n\nTo change this port, edit appsettings.json", "OK");
                }),
                new MenuItem("_Network Discovery", "Configure network discovery", () =>
                {
                    MessageBox.Query("Network Discovery", "Network Discovery is enabled.\n\nTo configure, edit appsettings.json", "OK");
                })
            }),
            new MenuBarItem("_Help", new MenuItem[]
            {
                new MenuItem("_About", "About this application", () =>
                {
                    MessageBox.Query("About",
                        "GeoscientistToolkit Node Endpoint\n" +
                        "Version 1.0\n\n" +
                        "Distributed computing node for\n" +
                        "geoscientific simulations and CT operations\n\n" +
                        $"Swagger UI: http://localhost:{_httpPort}/swagger", "OK");
                })
            })
        });

        top.Add(menu);

        // Main window
        _mainWindow = new Window("GeoscientistToolkit Node Endpoint")
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        // Status frame (top section)
        _statusFrame = new FrameView("System Information")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 10
        };

        var platform = OperatingSystem.IsWindows() ? "Windows" :
                      OperatingSystem.IsMacOS() ? "macOS" :
                      OperatingSystem.IsLinux() ? "Linux" : "Unknown";

        _platformLabel = new Label($"Platform: {platform}")
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

        var swaggerLabel = new Label($"Swagger UI: http://localhost:{_httpPort}/swagger")
        {
            X = 1,
            Y = 5
        };

        // CPU Usage bar
        _cpuUsageLabel = new Label("CPU Usage: 0.0%")
        {
            X = 1,
            Y = 6
        };

        _cpuUsageBar = new ProgressBar()
        {
            X = 1,
            Y = 7,
            Width = Dim.Percent(80),
            Height = 1
        };

        _statusFrame.Add(_platformLabel, _httpApiLabel, _nodeManagerLabel, _statusLabel, _uptimeLabel, swaggerLabel, _cpuUsageLabel, _cpuUsageBar);

        // Connections frame (middle section)
        _connectionsFrame = new FrameView("Active Connections")
        {
            X = 0,
            Y = Pos.Bottom(_statusFrame),
            Width = Dim.Fill(),
            Height = Dim.Percent(50)
        };

        _connectionsListView = new ListView(new List<string> { "No active connections" })
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _connectionsFrame.Add(_connectionsListView);

        // Benchmark frame (bottom section)
        _benchmarkFrame = new FrameView("CPU Benchmark")
        {
            X = 0,
            Y = Pos.Bottom(_connectionsFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _cpuBenchmarkLabel = new Label("Press F5 or use Tools > Run CPU Benchmark to run a benchmark")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _benchmarkFrame.Add(_cpuBenchmarkLabel);

        _mainWindow.Add(_statusFrame, _connectionsFrame, _benchmarkFrame);

        // Bottom right corner indicators (added to top level, not mainWindow)
        _keepaliveIndicator = new Label("KEEPALIVE")
        {
            X = Pos.AnchorEnd(30),
            Y = Pos.AnchorEnd(1),
            ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black)
            }
        };

        _txIndicator = new Label("TX")
        {
            X = Pos.AnchorEnd(18),
            Y = Pos.AnchorEnd(1),
            ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black)
            }
        };

        _rxIndicator = new Label("RX")
        {
            X = Pos.AnchorEnd(12),
            Y = Pos.AnchorEnd(1),
            ColorScheme = new ColorScheme()
            {
                Normal = Terminal.Gui.Attribute.Make(Color.Gray, Color.Black)
            }
        };

        top.Add(_mainWindow, _keepaliveIndicator, _txIndicator, _rxIndicator);

        // Add keyboard shortcuts
        top.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key == Key.F5)
            {
                RunCpuBenchmark();
                e.Handled = true;
            }
            else if (e.KeyEvent.Key == (Key.Q | Key.CtrlMask))
            {
                if (MessageBox.Query("Quit", "Are you sure you want to quit?", "Yes", "No") == 0)
                {
                    Application.RequestStop();
                }
                e.Handled = true;
            }
        };

        // Initial refresh
        RefreshData();
    }

    private void SetupUpdateTimer()
    {
        _updateTimer = new System.Timers.Timer(500); // Update every 500ms for smoother indicators
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
                    UpdateNetworkIndicators();
                    UpdateKeepaliveIndicator();
                });
            }
        };
        _updateTimer.Start();
    }

    private void UpdateUptime()
    {
        var uptime = DateTime.Now - _startTime;
        _uptimeLabel.Text = $"Uptime: {uptime:hh\\:mm\\:ss}";
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
            connections.Add($"=== Discovered Nodes ({discoveredNodes.Count}) ===");
            foreach (var node in discoveredNodes)
            {
                connections.Add($"  {node.NodeType} - {node.IPAddress}:{node.HttpPort} ({node.Platform})");
            }
            connections.Add("");
        }

        // Get connected nodes from NodeManager
        var connectedNodes = _nodeManager.GetConnectedNodes();
        if (connectedNodes.Any())
        {
            connections.Add($"=== Connected Nodes ({connectedNodes.Count}) ===");
            foreach (var node in connectedNodes)
            {
                var statusInfo = $"CPU: {node.CpuUsage:F1}% | Mem: {node.MemoryUsage:F1}% | Jobs: {node.ActiveJobs}";
                connections.Add($"  {node.NodeName} - {node.IpAddress} [{node.Status}]");
                connections.Add($"    {statusInfo}");
            }
        }

        if (!connections.Any())
        {
            connections.Add("No active connections");
        }

        _connectionsListView.SetSource(connections);
    }

    private void RefreshData()
    {
        UpdateUptime();
        UpdateStatus();
        UpdateConnections();
        UpdateCpuUsage();
        UpdateNetworkIndicators();
        UpdateKeepaliveIndicator();
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

            _cpuUsageLabel.Text = $"CPU Usage: {_currentCpuUsage:F1}%";
            _cpuUsageBar.Fraction = Math.Min(_currentCpuUsage / 100f, 1.0f);
        }
        catch
        {
            _currentCpuUsage = 0;
            _cpuUsageLabel.Text = "CPU Usage: N/A";
        }
    }

    private void UpdateNetworkIndicators()
    {
        try
        {
            long totalBytesSent = 0;
            long totalBytesReceived = 0;

            // Get network statistics from all interfaces
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

            // Check if there's activity since last check
            if (_lastBytesSent > 0)
            {
                _txActive = totalBytesSent > _lastBytesSent;
                _rxActive = totalBytesReceived > _lastBytesReceived;
            }

            _lastBytesSent = totalBytesSent;
            _lastBytesReceived = totalBytesReceived;

            // Update TX indicator
            if (_txActive)
            {
                _txIndicator.Text = "TX▲";
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
                _rxIndicator.Text = "RX▼";
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

            // Reset activity flags (will be set again on next check if still active)
            _txActive = false;
            _rxActive = false;
        }
        catch
        {
            // Ignore network stats errors
        }
    }

    private void UpdateKeepaliveIndicator()
    {
        _keepaliveCounter++;

        // Blink the keepalive indicator every 2 seconds (4 updates at 500ms)
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

    private void RunCpuBenchmark()
    {
        _cpuBenchmarkLabel.Text = "Running benchmark...";
        Application.Refresh();

        Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();

            // Simple CPU benchmark: matrix multiplication
            const int size = 500;
            var random = new Random();
            var matrixA = new double[size, size];
            var matrixB = new double[size, size];
            var result = new double[size, size];

            // Initialize matrices
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    matrixA[i, j] = random.NextDouble();
                    matrixB[i, j] = random.NextDouble();
                }
            }

            // Matrix multiplication
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
            }

            sw.Stop();

            var flops = (2.0 * size * size * size) / sw.Elapsed.TotalSeconds;
            var gflops = flops / 1_000_000_000.0;

            Application.MainLoop.Invoke(() =>
            {
                var benchmarkText = new StringBuilder();
                benchmarkText.AppendLine($"Benchmark completed in {sw.ElapsedMilliseconds:N0} ms");
                benchmarkText.AppendLine($"Matrix size: {size}x{size}");
                benchmarkText.AppendLine($"Operations: {2L * size * size * size:N0}");
                benchmarkText.AppendLine($"Performance: {gflops:F2} GFLOPS");
                benchmarkText.AppendLine($"");
                benchmarkText.AppendLine($"CPU Info:");
                benchmarkText.AppendLine($"  Processor Count: {Environment.ProcessorCount}");
                benchmarkText.AppendLine($"  64-bit Process: {Environment.Is64BitProcess}");
                benchmarkText.AppendLine($"");
                benchmarkText.AppendLine($"Press F5 to run again");

                _cpuBenchmarkLabel.Text = benchmarkText.ToString();
            });
        });
    }
}
