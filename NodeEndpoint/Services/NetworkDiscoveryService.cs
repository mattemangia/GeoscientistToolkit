using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;

namespace GeoscientistToolkit.NodeEndpoint.Services;

/// <summary>
/// Cross-platform network discovery service for automatic node connection
/// </summary>
public class NetworkDiscoveryService
{
    private const int DISCOVERY_PORT = 9877; // UDP broadcast port
    private const int BROADCAST_INTERVAL_MS = 5000; // Broadcast every 5 seconds
    private UdpClient? _broadcastClient;
    private UdpClient? _listenClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly string _nodeEndpointAddress;
    private readonly int _nodeEndpointPort;
    private readonly int _nodeManagerPort;
    private readonly Dictionary<string, DiscoveryMessage> _discoveredNodes = new();

    public NetworkDiscoveryService(string nodeEndpointAddress, int nodeEndpointPort, int nodeManagerPort)
    {
        _nodeEndpointAddress = nodeEndpointAddress;
        _nodeEndpointPort = nodeEndpointPort;
        _nodeManagerPort = nodeManagerPort;
    }

    /// <summary>
    /// Get the local IP address for the current machine (cross-platform)
    /// </summary>
    public static string GetLocalIPAddress()
    {
        try
        {
            // Get all network interfaces
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                            ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            foreach (var ni in interfaces)
            {
                var properties = ni.GetIPProperties();
                var addresses = properties.UnicastAddresses
                    .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                               !IPAddress.IsLoopback(ua.Address))
                    .Select(ua => ua.Address)
                    .ToList();

                if (addresses.Any())
                {
                    // Prefer addresses in common private ranges
                    var preferred = addresses.FirstOrDefault(a =>
                        a.ToString().StartsWith("192.168.") ||
                        a.ToString().StartsWith("10.") ||
                        a.ToString().StartsWith("172."));

                    return (preferred ?? addresses.First()).ToString();
                }
            }

            // Fallback: connect to external address to determine local IP
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    /// <summary>
    /// Start broadcasting this node's availability on the network
    /// </summary>
    public void StartBroadcasting()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        Task.Run(async () =>
        {
            try
            {
                _broadcastClient = new UdpClient();
                _broadcastClient.EnableBroadcast = true;

                var localIp = GetLocalIPAddress();
                var message = new DiscoveryMessage
                {
                    NodeType = "NodeEndpoint",
                    HostName = Environment.MachineName,
                    IPAddress = localIp,
                    HttpPort = _nodeEndpointPort,
                    NodeManagerPort = _nodeManagerPort,
                    Platform = GetPlatform(),
                    Timestamp = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(message);
                var data = Encoding.UTF8.GetBytes(json);

                Console.WriteLine($"[NetworkDiscovery] Broadcasting on {localIp}:{DISCOVERY_PORT}");
                Console.WriteLine($"[NetworkDiscovery] HTTP API: http://{localIp}:{_nodeEndpointPort}");
                Console.WriteLine($"[NetworkDiscovery] NodeManager: {localIp}:{_nodeManagerPort}");

                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // Broadcast to subnet
                    var endpoint = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);
                    await _broadcastClient.SendAsync(data, data.Length, endpoint);

                    await Task.Delay(BROADCAST_INTERVAL_MS, _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkDiscovery] Broadcast error: {ex.Message}");
            }
        }, _cancellationTokenSource.Token);
    }

    /// <summary>
    /// Listen for other nodes broadcasting on the network
    /// </summary>
    public void StartListening(Action<DiscoveryMessage> onNodeDiscovered)
    {
        Task.Run(async () =>
        {
            try
            {
                _listenClient = new UdpClient(DISCOVERY_PORT);
                _listenClient.EnableBroadcast = true;

                Console.WriteLine($"[NetworkDiscovery] Listening for nodes on port {DISCOVERY_PORT}");

                while (_cancellationTokenSource?.Token.IsCancellationRequested != true)
                {
                    var result = await _listenClient.ReceiveAsync();
                    var json = Encoding.UTF8.GetString(result.Buffer);

                    try
                    {
                        var message = JsonSerializer.Deserialize<DiscoveryMessage>(json);
                        if (message != null)
                        {
                            // Don't discover ourselves
                            if (message.HostName != Environment.MachineName)
                            {
                                // Track discovered node
                                var nodeKey = $"{message.IPAddress}:{message.HttpPort}";
                                lock (_discoveredNodes)
                                {
                                    _discoveredNodes[nodeKey] = message;
                                }

                                Console.WriteLine($"[NetworkDiscovery] Discovered {message.NodeType} at {message.IPAddress}:{message.HttpPort}");
                                onNodeDiscovered(message);
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed messages
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                Console.WriteLine($"[NetworkDiscovery] Port {DISCOVERY_PORT} already in use. Discovery disabled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkDiscovery] Listen error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Stop broadcasting and listening
    /// </summary>
    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _broadcastClient?.Close();
        _listenClient?.Close();
    }

    /// <summary>
    /// Get list of discovered nodes
    /// </summary>
    public List<DiscoveryMessage> GetDiscoveredNodes()
    {
        lock (_discoveredNodes)
        {
            // Clean up stale nodes (not seen in last 30 seconds)
            var staleKeys = _discoveredNodes
                .Where(kvp => (DateTime.UtcNow - kvp.Value.Timestamp).TotalSeconds > 30)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                _discoveredNodes.Remove(key);
            }

            return new List<DiscoveryMessage>(_discoveredNodes.Values);
        }
    }

    private static string GetPlatform()
    {
        if (OperatingSystem.IsWindows())
            return "Windows";
        if (OperatingSystem.IsMacOS())
            return "macOS";
        if (OperatingSystem.IsLinux())
            return "Linux";
        return "Unknown";
    }
}

public class DiscoveryMessage
{
    public required string NodeType { get; set; }
    public required string HostName { get; set; }
    public required string IPAddress { get; set; }
    public int HttpPort { get; set; }
    public int NodeManagerPort { get; set; }
    public required string Platform { get; set; }
    public DateTime Timestamp { get; set; }
}
