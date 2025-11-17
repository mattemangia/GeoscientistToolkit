// GeoscientistToolkit/Settings/Settings.cs

using System.Text.Json;

namespace GeoscientistToolkit.Settings;

/// <summary>
///     Main settings container that holds all application settings
/// </summary>
public class AppSettings
{
    public AppearanceSettings Appearance { get; set; } = new();
    public HardwareSettings Hardware { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public AddInSettings AddIns { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
    public NetworkSettings Network { get; set; } = new();
    public FileAssociationSettings FileAssociations { get; set; } = new();
    public BackupSettings Backup { get; set; } = new();
    public PhotogrammetrySettings Photogrammetry { get; set; } = new();
    public GISSettings GIS { get; set; } = new();
    public NodeManagerSettings NodeManager { get; set; } = new();
    public OllamaSettings Ollama { get; set; } = new();

    /// <summary>
    ///     Creates a new instance with default values
    /// </summary>
    public static AppSettings CreateDefaults()
    {
        return new AppSettings();
    }

    /// <summary>
    ///     Creates a deep copy of the settings
    /// </summary>
    public AppSettings Clone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<AppSettings>(json);
    }
}

/// <summary>
///     Appearance-related settings
/// </summary>
public class AppearanceSettings
{
    public string Theme { get; set; } = "Dark";
    public float UIScale { get; set; } = 1.0f;
    public string FontFamily { get; set; } = "Default";
    public int FontSize { get; set; } = 13;
    public bool ShowToolTips { get; set; } = true;
    public bool AnimateWindows { get; set; } = true;
    public float AnimationSpeed { get; set; } = 1.0f;
    public bool ShowWelcomeOnStartup { get; set; } = false;
    public string ColorScheme { get; set; } = "Blue";
    public bool UseSystemTitleBar { get; set; } = false;
    public int MaxRecentProjects { get; set; } = 10;
}

/// <summary>
///     Hardware-related settings
/// </summary>
public class HardwareSettings
{
    public string ComputeGPU { get; set; } = "Auto";
    public string VisualizationGPU { get; set; } = "Auto";
    public bool EnableMultiThreading { get; set; } = true;
    public int MaxThreadCount { get; set; } = -1; // -1 means auto
    public bool UseHardwareAcceleration { get; set; } = true;
    public string PreferredGraphicsBackend { get; set; } = "Auto";
    public int TextureMemoryLimit { get; set; } = 2048; // MB
    public bool EnableVSync { get; set; } = true;
    public int TargetFrameRate { get; set; } = 60;
    public bool EnableMultiGPUParallelization { get; set; } = false; // Enable multi-GPU compute when multiple GPUs available
}

/// <summary>
///     Logging-related settings
/// </summary>
public class LoggingSettings
{
    public string LogFilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GeoscientistToolkit", "Logs");

    public LogLevel MinimumLogLevel { get; set; } = LogLevel.Information;
    public bool EnableFileLogging { get; set; } = true;
    public bool EnableConsoleLogging { get; set; } = false;
    public int MaxLogFileSize { get; set; } = 10; // MB
    public int MaxLogFiles { get; set; } = 5;
    public bool IncludeTimestamp { get; set; } = true;
    public bool IncludeThreadId { get; set; } = false;
    public string LogFilePattern { get; set; } = "gt-{date}.log";
}

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

/// <summary>
///     Add-in/Plugin settings
/// </summary>
public class AddInSettings
{
    public List<AddInInfo> InstalledAddIns { get; set; } = new();
    public bool AutoUpdateAddIns { get; set; } = true;
    public bool LoadAddInsOnStartup { get; set; } = true;

    public string AddInDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GeoscientistToolkit", "AddIns");
}

public class AddInInfo
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public bool Enabled { get; set; }
    public string Author { get; set; }
    public string Description { get; set; }
    public DateTime InstalledDate { get; set; }
}

/// <summary>
///     Performance-related settings
/// </summary>
public class PerformanceSettings
{
    public int TextureCacheSize { get; set; } = 512; // MB
    public int UndoHistorySize { get; set; } = 50;
    public int MaxDatasetPreviewSize { get; set; } = 100; // MB
    public bool EnableLazyLoading { get; set; } = true;
    public bool EnableDataCompression { get; set; } = true;
    public int AutoSaveInterval { get; set; } = 5; // minutes, 0 = disabled
    public bool EnableMemoryOptimization { get; set; } = true;
    public int MemoryWarningThreshold { get; set; } = 90; // percentage
    public bool PreloadCommonResources { get; set; } = true;
}

/// <summary>
///     Network-related settings
/// </summary>
public class NetworkSettings
{
    public bool UseProxy { get; set; } = false;
    public string ProxyAddress { get; set; } = "";
    public int ProxyPort { get; set; } = 8080;
    public bool CheckForUpdates { get; set; } = true;
    public int UpdateCheckInterval { get; set; } = 7; // days
    public bool DownloadUpdatesAutomatically { get; set; } = false;
    public int ConnectionTimeout { get; set; } = 30; // seconds
    public bool EnableTelemetry { get; set; } = false;
}

/// <summary>
///     File association settings
/// </summary>
public class FileAssociationSettings
{
    public bool AssociateGTPFiles { get; set; } = true;
    public bool AssociateDICOMFiles { get; set; } = false;
    public bool AssociateTIFFFiles { get; set; } = false;
    public List<string> CustomAssociations { get; set; } = new();
    public string DefaultImportFormat { get; set; } = "Auto";
    public bool AutoLoadLastProject { get; set; } = false;
    public List<string> RecentProjects { get; set; } = new();
}

/// <summary>
///     Backup and recovery settings
/// </summary>
public class BackupSettings
{
    public bool EnableAutoBackup { get; set; } = true;
    public int BackupInterval { get; set; } = 30; // minutes

    public string BackupDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GeoscientistToolkit", "Backups");

    public int MaxBackupCount { get; set; } = 10;
    public bool CompressBackups { get; set; } = true;
    public bool BackupOnProjectClose { get; set; } = true;
    public bool EnableCrashRecovery { get; set; } = true;
}

/// <summary>
///     Photogrammetry-related settings
/// </summary>
public class PhotogrammetrySettings
{
    // Model paths
    public string DepthModelPath { get; set; } = "";
    public string SuperPointModelPath { get; set; } = "";
    public string LightGlueModelPath { get; set; } = "";

    // Model directory
    public string ModelsDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GeoscientistToolkit", "Models", "Photogrammetry");

    // Pipeline settings
    public bool UseGpuAcceleration { get; set; } = false;
    public int DepthModelType { get; set; } = 0; // 0=MiDaS Small, 1=DPT Small, 2=ZoeDepth
    public int KeyframeInterval { get; set; } = 10;
    public int TargetWidth { get; set; } = 640;
    public int TargetHeight { get; set; } = 480;

    // Camera intrinsics
    public float FocalLengthX { get; set; } = 500;
    public float FocalLengthY { get; set; } = 500;
    public float PrincipalPointX { get; set; } = 320;
    public float PrincipalPointY { get; set; } = 240;

    // Export settings
    public string DefaultExportFormat { get; set; } = "PLY"; // PLY, XYZ, OBJ
    public bool ExportTexturedMesh { get; set; } = true;
    public bool ExportCameraPath { get; set; } = true;

    // Model download URLs
    public string MidasSmallUrl { get; set; } = "https://github.com/PINTO0309/PINTO_model_zoo/raw/main/142_midas/01_float32/midas_v21_small_256.onnx";
    public string SuperPointUrl { get; set; } = "https://github.com/PINTO0309/PINTO_model_zoo/raw/main/144_SuperPoint/superpoint.onnx";
    public string LightGlueUrl { get; set; } = ""; // To be filled with actual URL when available

    // Advanced settings
    public float ConfidenceThreshold { get; set; } = 0.015f;
    public float MatchingRatioThreshold { get; set; } = 0.8f;
    public double ReprojectionThreshold { get; set; } = 1.0;
    public int MinMatchesForPose { get; set; } = 8;

    // Memory management
    public bool EnableMemoryManagement { get; set; } = true;
    public int MemoryThresholdMB { get; set; } = 2048; // 2 GB default
    public int MaxKeyframesInMemory { get; set; } = 50;
}

/// <summary>
///     GIS-related settings
/// </summary>
public class GISSettings
{
    // Basemap settings
    public bool EnableOnlineBasemaps { get; set; } = true;
    public string DefaultBasemapProvider { get; set; } = "esri_imagery"; // Satellite imagery by default
    public int DefaultTileZoom { get; set; } = 5;
    public int MaxTileCacheSize { get; set; } = 500; // MB
    public bool AutoLoadBasemaps { get; set; } = true;

    // Available basemap types (for quick access)
    public string SatelliteProvider { get; set; } = "esri_imagery";
    public string TopographicProvider { get; set; } = "opentopomap";
    public string ElevationProvider { get; set; } = "esri_hillshade";
    public string PhysicalProvider { get; set; } = "stamen_terrain";

    // Tile cache directory
    public string TileCacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GeoscientistToolkit", "TileCache");

    // Display settings
    public bool ShowAttribution { get; set; } = true;
    public bool ShowGridByDefault { get; set; } = true;
    public bool ShowScaleBarByDefault { get; set; } = true;
    public bool ShowNorthArrowByDefault { get; set; } = true;
    public bool ShowCoordinatesByDefault { get; set; } = true;
}

/// <summary>
///     Node Manager settings for distributed computing
/// </summary>
public class NodeManagerSettings
{
    // Node role configuration
    public bool EnableNodeManager { get; set; } = false;
    public NodeRole Role { get; set; } = NodeRole.Worker;

    // Network settings
    public string NodeName { get; set; } = Environment.MachineName;
    public int ServerPort { get; set; } = 9876;
    public string HostAddress { get; set; } = "localhost";

    // Connection settings
    public int ConnectionTimeout { get; set; } = 30; // seconds
    public int HeartbeatInterval { get; set; } = 10; // seconds
    public int MaxReconnectAttempts { get; set; } = 3;

    // Performance settings
    public int MaxConcurrentJobs { get; set; } = Environment.ProcessorCount;
    public bool UseGpuForJobs { get; set; } = true;

    // Auto-start settings
    public bool AutoStartOnLaunch { get; set; } = false;
    public bool AutoConnectToHost { get; set; } = false;

    // Simulator integration
    public bool UseNodesForSimulators { get; set; } = false;

    // Resource limits
    public int MaxMemoryUsagePercent { get; set; } = 80;
    public int MaxCpuUsagePercent { get; set; } = 90;
}

/// <summary>
///     Node role in the distributed computing network
/// </summary>
public enum NodeRole
{
    /// <summary>
    /// Host node that distributes work to workers
    /// </summary>
    Host,

    /// <summary>
    /// Worker node that executes jobs
    /// </summary>
    Worker,

    /// <summary>
    /// Hybrid node that can act as both host and worker
    /// </summary>
    Hybrid
}

/// <summary>
///     Ollama LLM integration settings
/// </summary>
public class OllamaSettings
{
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string SelectedModel { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 300; // 5 minutes default for long report generation
    public int MaxTokens { get; set; } = 4096;
    public float Temperature { get; set; } = 0.7f;
}