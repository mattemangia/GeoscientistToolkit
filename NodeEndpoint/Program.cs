using GeoscientistToolkit.Network;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net;

namespace GeoscientistToolkit.NodeEndpoint;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure Kestrel for keepalive connections
        // NOTE: Port binding is configured in appsettings.json under Kestrel:Endpoints
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Enable keepalive
            options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
            options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);

            // Configure for production
            options.Limits.MaxConcurrentConnections = 100;
            options.Limits.MaxConcurrentUpgradedConnections = 100;
            options.Limits.MaxRequestBodySize = 1_073_741_824; // 1GB for large CT volumes
        });

        // Add services to the container
        builder.Services.AddControllers()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
            });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new() {
                Title = "GeoscientistToolkit Node Endpoint",
                Version = "v1",
                Description = "REST API for distributed simulations and CT operations"
            });
        });

        // Get configuration for shared storage
        var sharedStoragePath = builder.Configuration.GetValue<string>("SharedStorage:Path");
        if (sharedStoragePath == "auto" || string.IsNullOrEmpty(sharedStoragePath))
        {
            sharedStoragePath = null; // Will use platform-specific defaults
        }

        // Register NodeManager as singleton
        builder.Services.AddSingleton<NodeManager>(sp => NodeManager.Instance);

        // Register endpoint services
        builder.Services.AddSingleton<NodeEndpointService>();
        builder.Services.AddSingleton<JobTracker>();
        builder.Services.AddSingleton<Services.DataReferenceService>(sp =>
            new Services.DataReferenceService(sharedStoragePath));
        builder.Services.AddSingleton<Services.JobPartitioner>();
        builder.Services.AddSingleton<Services.NetworkDiscoveryService>(sp =>
        {
            // Get the HTTP port from configuration (appsettings.json)
            var httpPort = builder.Configuration.GetValue<int>("HttpPort", 8500);
            var nodeManagerPort = builder.Configuration.GetValue<int>("NodeManager:ServerPort", 9876);
            var localIp = Services.NetworkDiscoveryService.GetLocalIPAddress();
            return new Services.NetworkDiscoveryService($"http://{localIp}", httpPort, nodeManagerPort);
        });

        // Add CORS for cross-origin requests
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        var app = builder.Build();

        // Configure the HTTP request pipeline
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors("AllowAll");
        app.UseAuthorization();
        app.MapControllers();

        // Initialize services
        var nodeManager = app.Services.GetRequiredService<NodeManager>();
        var nodeEndpointService = app.Services.GetRequiredService<NodeEndpointService>();
        var networkDiscovery = app.Services.GetRequiredService<Services.NetworkDiscoveryService>();

        // Load settings before accessing them
        GeoscientistToolkit.Settings.SettingsManager.Instance.LoadSettings();

        // Auto-detect local IP if needed
        var localIp = Services.NetworkDiscoveryService.GetLocalIPAddress();
        var nodeManagerSettings = GeoscientistToolkit.Settings.SettingsManager.Instance.Settings.NodeManager;

        if (nodeManagerSettings != null && (nodeManagerSettings.HostAddress == "auto" || nodeManagerSettings.HostAddress == "localhost"))
        {
            nodeManagerSettings.HostAddress = localIp;
        }

        var httpPort = builder.Configuration.GetValue<int>("HttpPort", 8500);
        var nodeManagerPort = builder.Configuration.GetValue<int>("NodeManager:ServerPort", 9876);

        Console.WriteLine("=== GeoscientistToolkit Node Endpoint Server ===");
        Console.WriteLine($"Platform: {(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsLinux() ? "Linux" : "Unknown")}");
        Console.WriteLine($"Local IP: {localIp}");
        Console.WriteLine($"HTTP API: http://{localIp}:{httpPort}");
        Console.WriteLine($"NodeManager: {localIp}:{nodeManagerPort}");
        Console.WriteLine($"Keepalive timeout: 10 minutes");
        Console.WriteLine($"");

        // Start NodeManager
        nodeEndpointService.Initialize();
        Console.WriteLine($"Node Manager status: {nodeManager.Status}");

        // Start network discovery
        var discoveryEnabled = builder.Configuration.GetValue<bool>("NetworkDiscovery:Enabled", true);
        if (discoveryEnabled)
        {
            Console.WriteLine("Starting network discovery...");
            networkDiscovery.StartBroadcasting();
            networkDiscovery.StartListening((node) =>
            {
                Console.WriteLine($"[Discovery] Found {node.NodeType} at {node.IPAddress}:{node.HttpPort} ({node.Platform})");
            });
            Console.WriteLine("Network discovery enabled - other nodes on the network can find this endpoint automatically");
        }

        Console.WriteLine("");
        Console.WriteLine("Ready to accept connections!");
        Console.WriteLine($"Swagger UI: http://localhost:{httpPort}/swagger");
        Console.WriteLine("");
        Console.WriteLine("Starting Terminal UI...");
        Console.WriteLine("");

        // Run the web app in a background thread
        var appTask = Task.Run(() => app.Run());

        // Give the web app a moment to fully start
        Thread.Sleep(1000);

        // Get the JobTracker service
        var jobTracker = app.Services.GetRequiredService<JobTracker>();

        // Run the TUI on the main thread
        var tuiManager = new TuiManager(
            nodeManager,
            networkDiscovery,
            jobTracker,
            httpPort,
            nodeManagerPort,
            localIp
        );

        tuiManager.Run();

        // Wait for the app to finish (if the TUI exits)
        appTask.Wait();
    }
}
