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
    public bool ShowWelcomeOnStartup { get; set; } = true;
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