// GeoscientistToolkit/Util/Logger.cs
// A simple static logger for application-wide message logging.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace GeoscientistToolkit.Util
{
    public static class Logger
    {
        private static readonly List<string> _logMessages = new List<string>();
        private static readonly string _logFilePath = "log.nfo";
        private static readonly object _lock = new object();

        static Logger()
        {
            // Clear the log file on startup
            File.WriteAllText(_logFilePath, string.Empty);
        }

        /// <summary>
        /// Logs a message to the in-memory list and the log file.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="methodName">The calling method, automatically populated.</param>
        public static void Log(string message, [CallerMemberName] string methodName = "")
        {
            lock (_lock)
            {
                string formattedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] [{methodName}]: {message}";
                _logMessages.Add(formattedMessage);
                try
                {
                    File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    _logMessages.Add($"[ERROR] Failed to write to log file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets a read-only list of all current log messages.
        /// </summary>
        public static IReadOnlyList<string> GetMessages()
        {
            return _logMessages;
        }

        /// <summary>
        /// Clears all messages from memory and the log file.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _logMessages.Clear();
                File.WriteAllText(_logFilePath, string.Empty);
                Log("Log cleared.");
            }
        }
    }
}