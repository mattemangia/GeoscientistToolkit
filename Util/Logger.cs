// GeoscientistToolkit/Util/Logger.cs
using System.Collections.Concurrent;

namespace GeoscientistToolkit.Util
{
    public static class Logger
    {
        private static readonly ConcurrentQueue<LogEntry> _logEntries = new();
        private static readonly int MaxEntries = 1000;

        public static event Action<LogEntry> OnLogAdded;

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };

            _logEntries.Enqueue(entry);

            // Keep log size manageable
            while (_logEntries.Count > MaxEntries)
            {
                _logEntries.TryDequeue(out _);
            }

            OnLogAdded?.Invoke(entry);
        }

        public static void LogError(string message) => Log(message, LogLevel.Error);
        public static void LogWarning(string message) => Log(message, LogLevel.Warning);

        public static IEnumerable<LogEntry> GetEntries() => _logEntries;

        public static void Clear()
        {
            while (_logEntries.TryDequeue(out _)) { }
        }
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
    }
}