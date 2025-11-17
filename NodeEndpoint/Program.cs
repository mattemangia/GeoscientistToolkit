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
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Enable keepalive
            options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
            options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);

            // Configure for production
            options.Limits.MaxConcurrentConnections = 100;
            options.Limits.MaxConcurrentUpgradedConnections = 100;
            options.Limits.MaxRequestBodySize = 1_073_741_824; // 1GB for large CT volumes

            // Listen on all interfaces
            options.Listen(IPAddress.Any, 5000, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            });
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

        // Register NodeManager as singleton
        builder.Services.AddSingleton<NodeManager>(sp => NodeManager.Instance);

        // Register endpoint services
        builder.Services.AddSingleton<NodeEndpointService>();
        builder.Services.AddSingleton<JobTracker>();

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

        // Initialize NodeManager
        var nodeManager = app.Services.GetRequiredService<NodeManager>();
        var nodeEndpointService = app.Services.GetRequiredService<NodeEndpointService>();

        Console.WriteLine("=== GeoscientistToolkit Node Endpoint Server ===");
        Console.WriteLine($"Starting on port 5000 with keepalive enabled");
        Console.WriteLine($"Keepalive timeout: 10 minutes");
        Console.WriteLine($"Node Manager status: {nodeManager.Status}");

        // Start NodeManager if not already started
        nodeEndpointService.Initialize();

        app.Run();
    }
}
