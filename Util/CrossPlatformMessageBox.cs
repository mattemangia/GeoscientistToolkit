// GeoscientistToolkit/Util/CrossPlatformMessageBox.cs
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GeoscientistToolkit.Util
{
    public static class CrossPlatformMessageBox
    {
        public static void Show(string message, string title, MessageBoxType type = MessageBoxType.Information)
        {
            // Try native message box first
            if (TryShowNative(message, title, type))
                return;
            
            // Fallback to console
            Console.WriteLine($"[{type}] {title}: {message}");
            
            // Also log it
            switch (type)
            {
                case MessageBoxType.Error:
                    Logger.LogError($"{title}: {message}");
                    break;
                case MessageBoxType.Warning:
                    Logger.LogWarning($"{title}: {message}");
                    break;
                default:
                    Logger.Log($"{title}: {message}");
                    break;
            }
        }
        
        private static bool TryShowNative(string message, string title, MessageBoxType type)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Use reflection to avoid compile-time dependency on Windows Forms
                    var assemblyName = "System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
                    var assembly = System.Reflection.Assembly.Load(assemblyName);
                    var messageBoxType = assembly.GetType("System.Windows.Forms.MessageBox");
                    var showMethod = messageBoxType.GetMethod("Show", new[] { typeof(string), typeof(string) });
                    showMethod.Invoke(null, new object[] { message, title });
                    return true;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Try zenity (common on Linux desktops)
                    var iconType = type switch
                    {
                        MessageBoxType.Error => "error",
                        MessageBoxType.Warning => "warning",
                        _ => "info"
                    };
                    
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "zenity",
                        Arguments = $"--{iconType} --title=\"{EscapeShellArg(title)}\" --text=\"{EscapeShellArg(message)}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(startInfo);
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // Use osascript on macOS
                    var script = $"display dialog \"{EscapeAppleScript(message)}\" with title \"{EscapeAppleScript(title)}\" buttons {{\"OK\"}} default button \"OK\"";
                    
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "osascript",
                        Arguments = $"-e '{script}'",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var process = Process.Start(startInfo);
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
            }
            catch
            {
                // Ignore and fall back to console
            }
            
            return false;
        }
        
        private static string EscapeShellArg(string arg)
        {
            return arg.Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
        }
        
        private static string EscapeAppleScript(string text)
        {
            return text.Replace("\"", "\\\"").Replace("\\", "\\\\");
        }
    }
    
    public enum MessageBoxType
    {
        Information,
        Warning,
        Error
    }
}