// GAIA.GeoGenesis/Logger.cs
//
// Self-contained static logger for the GeoGenesis thermodynamic simulator. Replaces the
// GeoscientistToolkit.Util.Logger the engine was originally written against, so that
// GAIA.GeoGenesis carries no dependency on the Geoscientist's Toolkit code base.
//
// The API surface (Log / LogWarning / LogError) is kept identical to what the ported solvers
// expect. By default messages go to the console; hosts (PRISM, CLI, tests) may attach their
// own sink via OnLogAdded or redirect to a file with SetLogFile().

using System.Collections.Concurrent;

namespace GAIA.GeoGenesis;

public enum GeoGenesisLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public sealed class GeoGenesisLogEntry
{
    public DateTime Timestamp { get; init; }
    public GeoGenesisLogLevel Level { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
///     Lightweight, thread-safe logger used throughout the GeoGenesis engine. All members are
///     static so the ported solver code can call <c>Logger.Log(...)</c> without dependency
///     injection, exactly as in the original module.
/// </summary>
public static class Logger
{
    private static readonly object _sync = new();
    private static StreamWriter? _file;

    /// <summary>When false (default in tests) console output is suppressed but sinks still fire.</summary>
    public static bool EchoToConsole { get; set; } = true;

    /// <summary>Minimum level that is emitted. Messages below this are dropped.</summary>
    public static GeoGenesisLogLevel MinimumLevel { get; set; } = GeoGenesisLogLevel.Info;

    /// <summary>Raised for every accepted log entry so a host can route it into its own UI/log.</summary>
    public static event Action<GeoGenesisLogEntry>? OnLogAdded;

    public static void SetLogFile(string path)
    {
        lock (_sync)
        {
            _file?.Flush();
            _file?.Dispose();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            _file = new StreamWriter(path, append: true) { AutoFlush = true };
        }
    }

    public static void Log(string message) => Emit(GeoGenesisLogLevel.Info, message);
    public static void LogDebug(string message) => Emit(GeoGenesisLogLevel.Debug, message);
    public static void LogWarning(string message) => Emit(GeoGenesisLogLevel.Warning, message);
    public static void LogError(string message) => Emit(GeoGenesisLogLevel.Error, message);
    public static void LogError(string message, Exception ex) => Emit(GeoGenesisLogLevel.Error, $"{message}: {ex}");

    private static void Emit(GeoGenesisLogLevel level, string message)
    {
        if (level < MinimumLevel) return;
        var entry = new GeoGenesisLogEntry { Timestamp = DateTime.Now, Level = level, Message = message };
        var line = $"[{entry.Timestamp:HH:mm:ss}] {level.ToString().ToUpperInvariant(),-7} {message}";

        lock (_sync)
        {
            if (EchoToConsole)
            {
                if (level == GeoGenesisLogLevel.Error) Console.Error.WriteLine(line);
                else Console.WriteLine(line);
            }
            _file?.WriteLine(line);
        }

        OnLogAdded?.Invoke(entry);
    }
}
