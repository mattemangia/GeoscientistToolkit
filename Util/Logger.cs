// GeoscientistToolkit/Util/Logger.cs

using System.Collections.Concurrent;
using System.Text;
using GeoscientistToolkit.Settings;

// Using the shared LogLevel enum

namespace GeoscientistToolkit.Util;

// The LogEntry class is now defined here and uses the correct enum
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; } // This is now Settings.LogLevel
    public string Message { get; set; }
    public int ThreadId { get; set; }
}

public static class Logger
{
    private static readonly ConcurrentQueue<LogEntry> _logEntries = new();
    private static readonly int MaxEntries = 1000;
    private static StreamWriter _logWriter;
    private static readonly object _fileLock = new();
    private static bool _isInitialized;
    private static LoggingSettings _settings;

    public static event Action<LogEntry> OnLogAdded;

    /// <summary>
    ///     Initializes the logger with specific settings. Must be called after settings are loaded.
    /// </summary>
    public static void Initialize(LoggingSettings settings)
    {
        lock (_fileLock)
        {
            _settings = settings;

            // Dispose previous writer if it exists
            _logWriter?.Dispose();
            _logWriter = null;

            if (_settings.EnableFileLogging)
                try
                {
                    var logDirectory = _settings.LogFilePath;
                    var logFileName = _settings.LogFilePattern.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"));
                    var logFilePath = Path.Combine(logDirectory, logFileName);

                    Directory.CreateDirectory(logDirectory);

                    var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    _logWriter = new StreamWriter(fileStream) { AutoFlush = true };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FATAL: Could not initialize file logger. {ex.Message}");
                    _logWriter = null;
                }

            // Register a handler to close the writer on exit
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit; // Remove old handler to prevent duplicates
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            _isInitialized = true;
        }
    }

    private static void OnProcessExit(object sender, EventArgs e)
    {
        _logWriter?.Dispose();
    }

    // The default LogLevel is now correctly typed as Settings.LogLevel
    public static void Log(string message, LogLevel level = LogLevel.Information)
    {
        // The comparison now works because both operands are of type Settings.LogLevel
        if (!_isInitialized || level < _settings.MinimumLogLevel) return;

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            ThreadId = Thread.CurrentThread.ManagedThreadId
        };

        _logEntries.Enqueue(entry);

        var logString = BuildLogString(entry);

        // Write to the log file in a thread-safe manner.
        if (_settings.EnableFileLogging && _logWriter != null)
            lock (_fileLock)
            {
                try
                {
                    _logWriter.WriteLine(logString);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: Failed to write to log file. {ex.Message}");
                }
            }

        // Write to console if enabled
        if (_settings.EnableConsoleLogging) Console.WriteLine(logString);

        // Keep in-memory log size manageable.
        while (_logEntries.Count > MaxEntries) _logEntries.TryDequeue(out _);

        OnLogAdded?.Invoke(entry);
    }

    private static string BuildLogString(LogEntry entry)
    {
        var sb = new StringBuilder();
        if (_settings.IncludeTimestamp) sb.Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
        if (_settings.IncludeThreadId) sb.Append($"[Thread:{entry.ThreadId}] ");
        sb.Append($"[{entry.Level.ToString().ToUpper()}] {entry.Message}");
        return sb.ToString();
    }

    public static void LogError(string message)
    {
        Log(message, LogLevel.Error);
    }

    public static void LogWarning(string message)
    {
        Log(message, LogLevel.Warning);
    }

    public static void LogDebug(string message)
    {
        Log(message, LogLevel.Debug);
    }


    public static IEnumerable<LogEntry> GetEntries()
    {
        return _logEntries;
    }

    public static void Clear()
    {
        while (_logEntries.TryDequeue(out _))
        {
        }
    }
}