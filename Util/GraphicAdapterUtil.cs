// GeoscientistToolkit/Util/GraphicsAdapterUtil.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
#if WINDOWS
using System.Management;
#endif

namespace GeoscientistToolkit.Util
{
    /// <summary>
    /// A multiplatform utility to enumerate the names of graphics adapters installed in the system.
    /// </summary>
    public static class GraphicsAdapterUtil
    {
        public static List<string> GetGpuList()
        {
            var gpuList = new List<string>();
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
#if WINDOWS
                    var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        gpuList.Add(mo["Name"]?.ToString() ?? "Unknown Video Controller");
                    }
#else
                    gpuList.Add("WMI not available on this platform");
#endif
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var output = ExecuteCommand("lspci", "| grep -i 'vga\\|3d\\|2d'");
                    gpuList.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Split(':').Length > 2 ? line.Split(':')[2].Trim() : line.Trim()));
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var output = ExecuteCommand("system_profiler", "SPDisplaysDataType");
                    gpuList.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Where(line => line.Trim().StartsWith("Chipset Model:"))
                        .Select(line => line.Split(':').Length > 1 ? line.Split(':')[1].Trim() : line.Trim()));
                }
            }
            catch(Exception ex) 
            {
                Logger.LogError($"Could not enumerate GPUs: {ex.Message}");
            }

            if (!gpuList.Any())
            {
                gpuList.Add("Could not enumerate GPUs");
            }

            return gpuList;
        }

        private static string ExecuteCommand(string command, string args)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                }
            };

            // On non-Windows, commands with pipes need to be run through a shell
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && args.Contains('|'))
            {
                process.StartInfo.FileName = "/bin/bash";
                process.StartInfo.Arguments = $"-c \"{command} {args}\"";
            }

            try
            {
                process.Start();
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return result;
            }
            catch 
            {
                return string.Empty; 
            }
        }
    }
}