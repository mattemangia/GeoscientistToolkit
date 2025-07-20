// GeoscientistToolkit/Util/Logger.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace GeoscientistToolkit.Util
{
    public static class Logger
    {
        private static readonly ConcurrentQueue<LogEntry> _logEntries = new();
        private static readonly int MaxEntries = 1000;
        private static readonly StreamWriter _logWriter;
        private static readonly object _fileLock = new();

        public static event Action<LogEntry> OnLogAdded;

        /// <summary>
        /// Static constructor to initialize file logging.
        /// </summary>
        static Logger()
        {
            try
            {
                // Determine a platform-neutral path for the log file.
                string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string logDirectory = Path.Combine(appDataPath, "GeoscientistToolkit");
                string logFilePath = Path.Combine(logDirectory, "Log.nfo");

                // Ensure the directory exists.
                Directory.CreateDirectory(logDirectory);

                // Open the file stream. FileMode.Create overwrites the file if it exists.
                // This fulfills the requirement to rewrite the file at each application start.
                var fileStream = new FileStream(logFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                _logWriter = new StreamWriter(fileStream) { AutoFlush = true };

                // Register an event handler to close the writer when the application exits.
                AppDomain.CurrentDomain.ProcessExit += (s, e) => _logWriter?.Dispose();
            }
            catch (Exception ex)
            {
                // If file logging fails to initialize, log the error to the console.
                // The logger will still work in-memory.
                Console.WriteLine($"FATAL: Could not initialize file logger. {ex.Message}");
                _logWriter = null;
            }
        }

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };

            _logEntries.Enqueue(entry);

            // Write to the log file in a thread-safe manner.
            if (_logWriter != null)
            {
                lock (_fileLock)
                {
                    try
                    {
                        _logWriter.WriteLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}");
                    }
                    catch (Exception ex)
                    {
                        // Avoid crashing the app if a log write fails.
                        Console.WriteLine($"ERROR: Failed to write to log file. {ex.Message}");
                    }
                }
            }


            // Keep in-memory log size manageable.
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
            // This will clear the in-memory queue. The log file is cleared on startup.
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