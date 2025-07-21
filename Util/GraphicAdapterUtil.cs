// GeoscientistToolkit/Util/GraphicsAdapterUtil.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

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
                    // Try WMI approach first
                    try
                    {
                        gpuList.AddRange(GetWindowsGpusViaWmi());
                    }
                    catch
                    {
                        // If WMI fails, try alternative methods
                        gpuList.AddRange(GetWindowsGpusViaProcess());
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Use -E for extended regex and proper quoting
                    var output = ExecuteCommand("lspci", "-v | grep -E -i \"(vga|3d|display)\"");
                    gpuList.AddRange(output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => 
                        {
                            // lspci format: "00:02.0 VGA compatible controller: Intel Corporation HD Graphics 620 (rev 02)"
                            // Extract the device description after the last colon
                            var colonIndex = line.LastIndexOf(':');
                            return colonIndex > 0 ? line.Substring(colonIndex + 1).Trim() : line.Trim();
                        })
                        .Where(name => !string.IsNullOrWhiteSpace(name)));
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
                // If we couldn't find any GPUs, add some common ones
                gpuList.Add("Default GPU");
            }

            return gpuList;
        }

        private static List<string> GetWindowsGpusViaWmi()
        {
            var gpuList = new List<string>();
            
            // Use dynamic loading to avoid compile-time dependency on System.Management
            try
            {
                var assembly = System.Reflection.Assembly.Load("System.Management");
                if (assembly != null)
                {
                    var searcherType = assembly.GetType("System.Management.ManagementObjectSearcher");
                    var searcher = Activator.CreateInstance(searcherType, new object[] { "SELECT * FROM Win32_VideoController" });
                    
                    var getMethod = searcherType.GetMethod("Get", Type.EmptyTypes);
                    var results = getMethod.Invoke(searcher, null);
                    
                    foreach (var mo in (System.Collections.IEnumerable)results)
                    {
                        var nameProperty = mo.GetType().GetProperty("Item");
                        var name = nameProperty.GetValue(mo, new object[] { "Name" })?.ToString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            gpuList.Add(name);
                        }
                    }
                }
            }
            catch
            {
                // WMI not available, will fall back to process method
            }
            
            return gpuList;
        }

        private static List<string> GetWindowsGpusViaProcess()
        {
            var gpuList = new List<string>();
            
            try
            {
                // Try using WMIC command line tool
                var output = ExecuteCommand("wmic", "path win32_VideoController get name");
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Skip(1) // Skip header
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line));
                
                gpuList.AddRange(lines);
            }
            catch
            {
                // If WMIC fails, try DirectX diagnostic
                try
                {
                    var output = ExecuteCommand("dxdiag", "/t dxdiag_output.txt");
                    System.Threading.Thread.Sleep(2000); // Give dxdiag time to write
                    if (System.IO.File.Exists("dxdiag_output.txt"))
                    {
                        var content = System.IO.File.ReadAllText("dxdiag_output.txt");
                        System.IO.File.Delete("dxdiag_output.txt");
                        
                        // Parse display devices from dxdiag output
                        var displaySections = content.Split(new[] { "Display Devices" }, StringSplitOptions.None);
                        if (displaySections.Length > 1)
                        {
                            var lines = displaySections[1].Split('\n');
                            foreach (var line in lines)
                            {
                                if (line.Trim().StartsWith("Card name:"))
                                {
                                    gpuList.Add(line.Substring(line.IndexOf(':') + 1).Trim());
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // All methods failed
                }
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