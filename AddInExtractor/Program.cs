using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace GeoscientistToolkit.AddInExtractor;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Extract and compile GeoscientistToolkit add-ins");

        var sourceOption = new Option<string>(
            new[] { "--source", "-s" },
            "Source directory containing add-in source files");

        var outputOption = new Option<string>(
            new[] { "--output", "-o" },
            "Output directory for compiled add-ins");

        var coreAssemblyOption = new Option<string>(
            new[] { "--core", "-c" },
            "Path to GeoscientistToolkit.dll");

        var mainAssemblyOption = new Option<string>(
            new[] { "--main", "-m" },
            "Path to GeoscientistToolkit.exe (optional, for access to concrete types)");

        var verboseOption = new Option<bool>(
            new[] { "--verbose", "-v" },
            "Enable verbose output");

        var diagnosticsOption = new Option<bool>(
            new[] { "--diagnostics", "-d" },
            "Run diagnostics only (don't compile)");

        rootCommand.AddOption(sourceOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(coreAssemblyOption);
        rootCommand.AddOption(mainAssemblyOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(diagnosticsOption);

        rootCommand.SetHandler(async (source, output, core, main, verbose, diagnostics) =>
        {
            var extractor = new AddInExtractor(verbose);

            if (diagnostics)
            {
                // Run diagnostics mode
                RunDiagnostics(core ?? extractor.FindCoreAssembly());
                return;
            }

            // Use current directory as a fallback for source if not provided.
            var sourceDir = string.IsNullOrEmpty(source) ? Directory.GetCurrentDirectory() : source;

            // Use a default output relative to the main project's build output if not provided.
            var outputDir = string.IsNullOrEmpty(output)
                ? Path.Combine("bin", "Release", "net8.0", "AddIns")
                : output;

            await ExtractAddIns(sourceDir, outputDir, core, main, verbose);
        }, sourceOption, outputOption, coreAssemblyOption, mainAssemblyOption, verboseOption, diagnosticsOption);

        return await rootCommand.InvokeAsync(args);
    }

    private static void RunDiagnostics(string? coreAssemblyPath)
    {
        Console.WriteLine("Running AddInExtractor Diagnostics");
        Console.WriteLine("==================================");
        Console.WriteLine($"Operating System: {GetOSDescription()}");
        Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");

        if (string.IsNullOrEmpty(coreAssemblyPath))
        {
            Console.WriteLine("Error: No core assembly path provided and could not find it automatically.");
            return;
        }

        if (!File.Exists(coreAssemblyPath))
        {
            Console.WriteLine($"Error: Core assembly not found at: {coreAssemblyPath}");
            return;
        }

        Console.WriteLine($"\nCore Assembly: {coreAssemblyPath}");

        try
        {
            var assembly = Assembly.LoadFrom(coreAssemblyPath);
            Console.WriteLine($"Loaded: {assembly.GetName().Name} v{assembly.GetName().Version}");

            var allTypes = assembly.GetExportedTypes();
            Console.WriteLine($"\nTotal exported types: {allTypes.Length}");

            // Group by namespace
            var namespaces = allTypes.GroupBy(t => t.Namespace ?? "<no namespace>")
                .OrderBy(g => g.Key)
                .ToList();

            Console.WriteLine($"\nNamespaces ({namespaces.Count}):");
            foreach (var ns in namespaces) Console.WriteLine($"  {ns.Key} ({ns.Count()} types)");

            // Check for specific AddIn types
            Console.WriteLine("\nAddIn-related types:");
            var addInInterface = allTypes.FirstOrDefault(t => t.Name == "IAddIn");
            Console.WriteLine($"  IAddIn: {addInInterface?.FullName ?? "NOT FOUND"}");

            var addInTool = allTypes.FirstOrDefault(t => t.Name == "AddInTool");
            Console.WriteLine($"  AddInTool: {addInTool?.FullName ?? "NOT FOUND"}");

            var dataset = allTypes.FirstOrDefault(t => t.Name == "Dataset");
            Console.WriteLine($"  Dataset: {dataset?.FullName ?? "NOT FOUND"}");

            var logger = allTypes.FirstOrDefault(t => t.Name == "Logger");
            Console.WriteLine($"  Logger: {logger?.FullName ?? "NOT FOUND"}");

            // List all types in GeoscientistToolkit.AddIns namespace
            var addInTypes = allTypes.Where(t => t.Namespace == "GeoscientistToolkit.AddIns").ToList();
            if (addInTypes.Count > 0)
            {
                Console.WriteLine($"\nTypes in GeoscientistToolkit.AddIns ({addInTypes.Count}):");
                foreach (var type in addInTypes) Console.WriteLine($"  - {type.Name}");
            }
            else
            {
                Console.WriteLine("\nWARNING: No types found in GeoscientistToolkit.AddIns namespace!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError loading assembly: {ex.Message}");
            Console.WriteLine($"Exception type: {ex.GetType().Name}");
            if (ex.InnerException != null) Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
        }
    }

    private static string GetOSDescription()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"Windows {Environment.OSVersion.Version}";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "Linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "macOS";
        return Environment.OSVersion.ToString();
    }

    private static async Task ExtractAddIns(string sourceDir, string outputDir, string? coreAssemblyPath,
        string? mainAssemblyPath, bool verbose)
    {
        var extractor = new AddInExtractor(verbose);

        if (string.IsNullOrEmpty(coreAssemblyPath))
        {
            coreAssemblyPath = extractor.FindCoreAssembly();
            if (coreAssemblyPath == null)
            {
                Console.Error.WriteLine(
                    "Error: Could not find GeoscientistToolkit.dll. Please specify its path with the --core option.");
                Environment.Exit(1);
            }
        }

        if (!File.Exists(coreAssemblyPath))
        {
            Console.Error.WriteLine($"Error: Core assembly not found: {coreAssemblyPath}");
            Environment.Exit(1);
        }

        await extractor.ExtractAndCompileAsync(sourceDir, outputDir, coreAssemblyPath, mainAssemblyPath);
    }
}

public class AddInExtractor
{
    private readonly bool _verbose;

    public AddInExtractor(bool verbose = false)
    {
        _verbose = verbose;
    }

    public string? FindCoreAssembly()
    {
        // Try common locations based on a .NET 8 target
        var searchPaths = new[]
        {
            Path.Combine("bin", "Release", "net8.0", "GeoscientistToolkit.dll"),
            Path.Combine("bin", "Debug", "net8.0", "GeoscientistToolkit.dll"),
            Path.Combine("..", "GeoscientistToolkit", "bin", "Release", "net8.0", "GeoscientistToolkit.dll"),
            Path.Combine("..", "GeoscientistToolkit", "bin", "Debug", "net8.0", "GeoscientistToolkit.dll"),
            "GeoscientistToolkit.dll"
        };

        foreach (var path in searchPaths)
            if (File.Exists(path))
            {
                Log($"Found core assembly at: {path}");
                return Path.GetFullPath(path);
            }

        return null;
    }

    public async Task ExtractAndCompileAsync(string sourceDir, string outputDir, string coreAssemblyPath,
        string? mainAssemblyPath)
    {
        Console.WriteLine("GeoscientistToolkit Add-In Extractor");
        Console.WriteLine($"Platform: {GetPlatformIdentifier()}");
        Console.WriteLine($"Source: {Path.GetFullPath(sourceDir)}");
        Console.WriteLine($"Output: {Path.GetFullPath(outputDir)}");
        Console.WriteLine($"Core Assembly: {Path.GetFullPath(coreAssemblyPath)}");
        if (!string.IsNullOrEmpty(mainAssemblyPath))
            Console.WriteLine($"Main Assembly: {Path.GetFullPath(mainAssemblyPath)}");
        Console.WriteLine();

        // Verify core assembly exists and is valid
        if (!File.Exists(coreAssemblyPath))
        {
            Console.Error.WriteLine($"Error: Core assembly not found at: {coreAssemblyPath}");
            return;
        }

        try
        {
            // Try to load the assembly to verify it's valid
            var testLoad = Assembly.LoadFrom(coreAssemblyPath);
            Console.WriteLine(
                $"Core assembly loaded successfully: {testLoad.GetName().Name} v{testLoad.GetName().Version}");
            var coreTypes = testLoad.GetExportedTypes();
            var addInTypes = coreTypes.Where(t => t.Namespace?.StartsWith("GeoscientistToolkit.AddIns") == true)
                .ToList();
            Console.WriteLine($"Found {addInTypes.Count} types in GeoscientistToolkit.AddIns namespace");
            if (_verbose && addInTypes.Count > 0)
                foreach (var type in addInTypes.Take(5))
                    Console.WriteLine($"  - {type.FullName}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to load core assembly: {ex.Message}");
            return;
        }

        if (!Directory.Exists(sourceDir))
        {
            Console.Error.WriteLine($"Error: Source directory not found: {sourceDir}");
            return;
        }

        Directory.CreateDirectory(outputDir);

        // Verify native dependencies
        var appDirectory = Path.GetDirectoryName(coreAssemblyPath)!;
        VerifyNativeDependencies(appDirectory);

        var matcher = new Matcher();
        matcher.AddInclude("**/*.cs");
        matcher.AddExclude("**/obj/**");
        matcher.AddExclude("**/bin/**");

        var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(sourceDir)));
        var sourceFiles = result.Files.Select(f => Path.Combine(sourceDir, f.Path)).ToList();

        if (sourceFiles.Count == 0)
        {
            Console.WriteLine("No add-in source files found.");
            return;
        }

        Console.WriteLine($"\nFound {sourceFiles.Count} add-in source file(s):");
        foreach (var file in sourceFiles) Console.WriteLine($"  - {Path.GetFileName(file)}");
        Console.WriteLine();

        // Load all necessary references
        var references = await LoadAllReferencesAsync(coreAssemblyPath, mainAssemblyPath);

        // Verify critical references for the main packages
        VerifyCriticalReferences(references, appDirectory);

        Console.WriteLine($"Loaded {references.Count} assembly references for compilation.");

        // Copy native libraries to output directory
        Console.WriteLine("\nCopying native libraries...");
        CopyNativeLibraries(appDirectory, outputDir);

        var tasks = sourceFiles.Select(file => CompileAddInAsync(file, outputDir, references)).ToList();
        await Task.WhenAll(tasks);

        Console.WriteLine();
        Console.WriteLine("Add-in extraction complete!");

        // Display platform library information if verbose
        if (_verbose)
        {
            Console.WriteLine("\nPlatform Library Mappings:");
            var mappings = GetPlatformLibraryMappings();
            var platformName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS" : "Unknown";

            foreach (var lib in mappings)
                if (lib.Value.ContainsKey(platformName))
                    Console.WriteLine($"  {lib.Key}: {lib.Value[platformName]}");
        }
    }

    private string GetPlatformIdentifier()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        if (string.IsNullOrEmpty(rid))
        {
            // Fallback to constructing RID manually
            var os = "";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                os = "win";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                os = "linux";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                os = "osx";

            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "arm64",
                _ => RuntimeInformation.ProcessArchitecture.ToString().ToLower()
            };

            rid = $"{os}-{arch}";
        }

        return rid;
    }

    private async Task<List<MetadataReference>> LoadAllReferencesAsync(string coreAssemblyPath,
        string? mainAssemblyPath)
    {
        var references = new List<MetadataReference>();
        var referencedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // CRITICAL: Add the core assembly FIRST
        try
        {
            references.Add(MetadataReference.CreateFromFile(coreAssemblyPath));
            referencedAssemblies.Add(Path.GetFileName(coreAssemblyPath));
            Log($"Added CORE assembly: {Path.GetFileName(coreAssemblyPath)}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CRITICAL ERROR: Failed to add core assembly reference: {ex.Message}");
            throw;
        }

        // Get the runtime directory for .NET Core/.NET 5+ assemblies
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        Log($"Runtime directory: {runtimeDirectory}");

        // Add core .NET assemblies
        var coreAssemblies = new[]
        {
            "System.Runtime.dll",
            "System.Runtime.Extensions.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Console.dll",
            "System.IO.dll",
            "System.Threading.dll",
            "System.Text.RegularExpressions.dll",
            "System.Diagnostics.Debug.dll",
            "System.Private.CoreLib.dll",
            "netstandard.dll",
            "System.ObjectModel.dll",
            "System.ComponentModel.dll",
            "System.ComponentModel.Primitives.dll",
            "System.ComponentModel.TypeConverter.dll",
            "System.Drawing.Primitives.dll",
            "System.Globalization.dll",
            "System.Resources.ResourceManager.dll",
            "System.Runtime.InteropServices.dll",
            "System.Memory.dll",
            "System.Numerics.Vectors.dll",
            "System.Runtime.CompilerServices.Unsafe.dll"
        };

        foreach (var assembly in coreAssemblies)
        {
            var path = Path.Combine(runtimeDirectory, assembly);
            if (File.Exists(path) && referencedAssemblies.Add(assembly))
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                    Log($"Added core reference: {assembly}");
                }
                catch (Exception ex)
                {
                    Log($"Warning: Could not load {assembly}: {ex.Message}");
                }
        }

        // Load assemblies from the application directory
        var appDirectory = Path.GetDirectoryName(coreAssemblyPath)!;
        Log($"Scanning application directory: {appDirectory}");

        // Get all DLL files, including from subdirectories (for runtime-specific libraries)
        var searchDirectories = new List<string> { appDirectory };

        // Add runtime-specific directories if they exist
        var runtimesDir = Path.Combine(appDirectory, "runtimes");
        if (Directory.Exists(runtimesDir))
        {
            // Get current platform RID
            var currentRid = GetPlatformIdentifier();

            // Build list of possible runtime identifiers to check
            var possibleRids = new List<string> { currentRid };

            // Add generic platform RIDs based on current OS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                possibleRids.AddRange(new[] { "win-x64", "win-x86", "win" });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                possibleRids.AddRange(new[] { "linux-x64", "linux-arm64", "linux-arm", "linux" });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                possibleRids.AddRange(new[] { "osx-x64", "osx-arm64", "osx" });

            // Add any-rid fallback
            possibleRids.Add("any");

            foreach (var rid in possibleRids.Distinct())
            {
                var ridLibDir = Path.Combine(runtimesDir, rid, "lib", "net8.0");
                if (Directory.Exists(ridLibDir))
                {
                    searchDirectories.Add(ridLibDir);
                    Log($"Added runtime directory: {ridLibDir}");
                }

                // Also check for native subdirectory
                var ridNativeDir = Path.Combine(runtimesDir, rid, "native");
                if (Directory.Exists(ridNativeDir)) Log($"Found native runtime directory: {ridNativeDir}");
            }
        }

        // Scan all directories for assemblies
        foreach (var directory in searchDirectories)
        {
            var assemblies = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
            foreach (var file in assemblies)
            {
                var fileName = Path.GetFileName(file);

                // Skip native libraries and duplicates (including the core assembly we already added)
                if (IsNativeLibrary(fileName) || referencedAssemblies.Contains(fileName))
                    continue;

                if (referencedAssemblies.Add(fileName))
                    try
                    {
                        references.Add(MetadataReference.CreateFromFile(file));
                        Log($"Added app reference: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        // This is expected for native libraries
                        Log($"Info: Could not load '{fileName}' as managed assembly (might be native): {ex.Message}");
                    }
            }
        }

        // Add main assembly reference if provided
        if (!string.IsNullOrEmpty(mainAssemblyPath) && File.Exists(mainAssemblyPath))
        {
            var fileName = Path.GetFileName(mainAssemblyPath);
            if (referencedAssemblies.Add(fileName))
            {
                references.Add(MetadataReference.CreateFromFile(mainAssemblyPath));
                Log($"Added main executable reference: {fileName}");
            }
        }

        // Try to resolve any missing System assemblies
        var systemAssemblies = new[]
        {
            typeof(object).Assembly.Location, // System.Private.CoreLib
            typeof(Console).Assembly.Location, // System.Console
            typeof(IEnumerable<>).Assembly.Location, // System.Collections
            typeof(Enumerable).Assembly.Location, // System.Linq
            typeof(StringBuilder).Assembly.Location, // System.Runtime
            typeof(File).Assembly.Location, // System.IO.FileSystem
            typeof(Debug).Assembly.Location, // System.Diagnostics.Debug
            typeof(Color).Assembly.Location, // System.Drawing.Primitives
            typeof(Vector2).Assembly.Location // System.Numerics
        };

        foreach (var assembly in systemAssemblies.Where(a => !string.IsNullOrEmpty(a)))
        {
            var fileName = Path.GetFileName(assembly);
            if (referencedAssemblies.Add(fileName))
                try
                {
                    references.Add(MetadataReference.CreateFromFile(assembly));
                    Log($"Added system reference: {fileName}");
                }
                catch (Exception ex)
                {
                    Log($"Warning: Could not load system assembly '{fileName}': {ex.Message}");
                }
        }

        // Log summary
        Log($"Total references loaded: {references.Count}");

        // In verbose mode, list loaded assemblies
        if (_verbose)
        {
            var loadedAssemblies = new List<string>();
            foreach (var reference in references)
                if (reference is PortableExecutableReference peRef && peRef.FilePath != null)
                    loadedAssemblies.Add(Path.GetFileName(peRef.FilePath));

            Log($"Loaded assemblies ({loadedAssemblies.Count}):");
            foreach (var assembly in loadedAssemblies.OrderBy(a => a).Take(20)) Log($"  - {assembly}");
            if (loadedAssemblies.Count > 20) Log($"  ... and {loadedAssemblies.Count - 20} more");
        }

        return references;
    }

    private bool IsNativeLibrary(string fileName)
    {
        var lowerFileName = fileName.ToLowerInvariant();

        // Check file extensions first (most reliable)
        if (lowerFileName.EndsWith(".so") || // Linux shared libraries
            lowerFileName.EndsWith(".so.0") || // Versioned Linux libraries  
            lowerFileName.EndsWith(".so.1") ||
            lowerFileName.EndsWith(".so.2") ||
            lowerFileName.Contains(".so.") || // Any versioned .so file
            lowerFileName.EndsWith(".dylib") || // macOS dynamic libraries
            lowerFileName.EndsWith(".a")) // Static libraries
            return true;

        // For Windows DLLs, we need to check if it's a native or managed DLL
        if (lowerFileName.EndsWith(".dll"))
        {
            // Known native library patterns
            var nativePatterns = new[]
            {
                // Graphics/Rendering
                "sdl2", "vulkan", "d3dcompiler", "d3d11", "d3d12", "dxgi",
                "opengl32", "gdi32", "libmoltenvk", "metal",

                // Media libraries
                "avcodec", "avformat", "avutil", "swscale", "swresample",

                // Image processing
                "libskiasharp", "libharfbuzzsharp", "freetype", "libpng",
                "libjpeg", "libtiff", "zlib", "harfbuzz",

                // Platform/System
                "kernel32", "user32", "advapi32", "shell32", "ole32",
                "msvcr", "msvcp", "vcruntime", "api-ms-win", "ucrtbase",

                // Audio
                "openal", "openal32", "soft_oal",

                // Compute
                "opencl", "cuda", "cudart",

                // Database
                "e_sqlite3", "sqlite3", "libe_sqlite3",

                // ImGui
                "cimgui",

                // Other
                "glfw", "glfw3", "native"
            };

            // Check if it matches any native pattern
            if (nativePatterns.Any(pattern => lowerFileName.Contains(pattern))) return true;

            // Additional check: files starting with "lib" are often native on Unix-like systems
            if (lowerFileName.StartsWith("lib") && !lowerFileName.Contains(".net")) return true;
        }

        return false;
    }

    private void VerifyCriticalReferences(List<MetadataReference> references, string appDirectory)
    {
        // List of critical assemblies from your NuGet packages
        // Platform-specific assemblies are marked with comments
        var criticalAssemblies = new Dictionary<string, string[]>
        {
            // Cross-platform managed assemblies (always .dll on all platforms)
            ["all"] = new[]
            {
                "ImGui.NET.dll",
                "Veldrid.dll",
                "Veldrid.ImGui.dll",
                "Veldrid.SPIRV.dll",
                "Veldrid.StartupUtilities.dll",
                "SkiaSharp.dll",
                "SkiaSharp.HarfBuzz.dll",
                "StbImageSharp.dll",
                "StbImageWriteSharp.dll",
                "TinyDialogsNet.dll",
                "BitMiracle.LibTiff.NET.dll",
                "Silk.NET.OpenCL.dll",
                "Silk.NET.Core.dll",
                "Silk.NET.Maths.dll"
            },
            // Windows-specific managed assemblies
            ["win"] = new[]
            {
                "System.Management.dll",
                "Veldrid.SDL2.dll"
            },
            // macOS-specific managed assemblies
            ["osx"] = new[]
            {
                "Veldrid.MetalBindings.dll",
                "Veldrid.SDL2.dll"
            },
            // Linux-specific managed assemblies
            ["linux"] = new[]
            {
                "Veldrid.SDL2.dll"
            }
        };

        var loadedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Extract file names from references
        foreach (var reference in references)
            if (reference is PortableExecutableReference peRef && peRef.FilePath != null)
                loadedFiles.Add(Path.GetFileName(peRef.FilePath));

        var missingAssemblies = new List<string>();

        // Check cross-platform assemblies
        foreach (var assembly in criticalAssemblies["all"])
            if (!loadedFiles.Contains(assembly))
                TryLoadMissingAssembly(assembly, appDirectory, references, loadedFiles, missingAssemblies);

        // Check platform-specific assemblies
        var platformKey = "";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            platformKey = "win";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            platformKey = "osx";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            platformKey = "linux";

        if (!string.IsNullOrEmpty(platformKey) && criticalAssemblies.ContainsKey(platformKey))
            foreach (var assembly in criticalAssemblies[platformKey])
                if (!loadedFiles.Contains(assembly))
                    TryLoadMissingAssembly(assembly, appDirectory, references, loadedFiles, missingAssemblies);

        if (missingAssemblies.Count > 0)
        {
            Console.WriteLine("Warning: Some expected assemblies were not found:");
            foreach (var assembly in missingAssemblies) Console.WriteLine($"  - {assembly}");
            Console.WriteLine("Add-ins may not compile if they reference types from these assemblies.");
        }
    }

    private void TryLoadMissingAssembly(string assembly, string appDirectory, List<MetadataReference> references,
        HashSet<string> loadedFiles, List<string> missingAssemblies)
    {
        // Build search paths based on current platform
        var searchPaths = new List<string>
        {
            Path.Combine(appDirectory, assembly)
        };

        // Add platform-specific runtime paths
        var currentRid = GetPlatformIdentifier();
        searchPaths.Add(Path.Combine(appDirectory, "runtimes", currentRid, "lib", "net8.0", assembly));

        // Add generic platform paths
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            searchPaths.Add(Path.Combine(appDirectory, "runtimes", "win-x64", "lib", "net8.0", assembly));
            searchPaths.Add(Path.Combine(appDirectory, "runtimes", "win", "lib", "net8.0", assembly));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            searchPaths.Add(Path.Combine(appDirectory, "runtimes", "linux-x64", "lib", "net8.0", assembly));
            searchPaths.Add(Path.Combine(appDirectory, "runtimes", "linux", "lib", "net8.0", assembly));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            searchPaths.Add(Path.Combine(appDirectory, "runtimes", "osx-x64", "lib", "net8.0", assembly));
            searchPaths.Add(Path.Combine(appDirectory, "runtimes", "osx-arm64", "lib", "net8.0", assembly));
            searchPaths.Add(Path.Combine(appDirectory, "runtimes", "osx", "lib", "net8.0", assembly));
        }

        // Add "any" RID fallback
        searchPaths.Add(Path.Combine(appDirectory, "runtimes", "any", "lib", "net8.0", assembly));

        var found = false;
        foreach (var path in searchPaths)
            if (File.Exists(path))
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                    loadedFiles.Add(assembly);
                    Log($"Added critical reference: {assembly}");
                    found = true;
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Warning: Could not load critical assembly '{assembly}': {ex.Message}");
                }

        if (!found) missingAssemblies.Add(assembly);
    }

    private void CopyNativeLibraries(string sourceDir, string outputDir)
    {
        // This method copies platform-specific native libraries to the output
        var runtimesDir = Path.Combine(sourceDir, "runtimes");
        if (!Directory.Exists(runtimesDir))
        {
            Log("No runtimes directory found, skipping native library copy");
            return;
        }

        var currentRid = GetPlatformIdentifier();
        var nativeSearchPaths = new List<string>();

        // Build list of runtime paths to check for native libraries
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            nativeSearchPaths.AddRange(new[]
            {
                Path.Combine(runtimesDir, "win-x64", "native"),
                Path.Combine(runtimesDir, "win-x86", "native"),
                Path.Combine(runtimesDir, "win", "native")
            });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            nativeSearchPaths.AddRange(new[]
            {
                Path.Combine(runtimesDir, "linux-x64", "native"),
                Path.Combine(runtimesDir, "linux-arm64", "native"),
                Path.Combine(runtimesDir, "linux-musl-x64", "native"), // Alpine Linux
                Path.Combine(runtimesDir, "linux", "native")
            });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            nativeSearchPaths.AddRange(new[]
            {
                Path.Combine(runtimesDir, "osx-x64", "native"),
                Path.Combine(runtimesDir, "osx-arm64", "native"),
                Path.Combine(runtimesDir, "osx", "native")
            });

        // Copy native libraries to output
        foreach (var nativePath in nativeSearchPaths.Where(Directory.Exists))
        {
            var files = Directory.GetFiles(nativePath);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(outputDir, fileName);

                try
                {
                    File.Copy(file, destPath, true);
                    Log($"Copied native library: {fileName}");

                    // On Unix-like systems, ensure the library is executable
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) MakeFileExecutable(destPath);
                }
                catch (Exception ex)
                {
                    Log($"Warning: Could not copy native library {fileName}: {ex.Message}");
                }
            }
        }
    }

    private void MakeFileExecutable(string filePath)
    {
        // Only works on Unix-like systems (Linux, macOS)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                Log($"chmod returned non-zero exit code for {Path.GetFileName(filePath)}: {error}");
            }
        }
        catch (Exception ex)
        {
            // If chmod is not available, continue without setting permissions
            Log($"Could not set executable permissions for {Path.GetFileName(filePath)}: {ex.Message}");
        }
    }

    private void VerifyNativeDependencies(string appDirectory)
    {
        Console.WriteLine("\nVerifying native dependencies for current platform...");

        var requiredNativeLibs = new Dictionary<OSPlatform, string[]>
        {
            [OSPlatform.Windows] = new[]
            {
                "SDL2.dll", "vulkan-1.dll", "libSkiaSharp.dll",
                "libHarfBuzzSharp.dll", "cimgui.dll", "e_sqlite3.dll"
            },
            [OSPlatform.Linux] = new[]
            {
                "libSDL2-2.0.so.0", "libvulkan.so.1", "libSkiaSharp.so",
                "libHarfBuzzSharp.so", "cimgui.so", "libe_sqlite3.so"
            },
            [OSPlatform.OSX] = new[]
            {
                "libSDL2.dylib", "libMoltenVK.dylib", "libSkiaSharp.dylib",
                "libHarfBuzzSharp.dylib", "cimgui.dylib", "libe_sqlite3.dylib"
            }
        };

        var currentPlatform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? OSPlatform.Windows :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? OSPlatform.Linux :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? OSPlatform.OSX :
            OSPlatform.Create("Unknown");

        if (!requiredNativeLibs.ContainsKey(currentPlatform))
        {
            Console.WriteLine("Warning: Unknown platform, cannot verify native dependencies");
            return;
        }

        var missingLibs = new List<string>();
        var foundLibs = new List<string>();

        foreach (var lib in requiredNativeLibs[currentPlatform])
        {
            var searchPaths = new[]
            {
                Path.Combine(appDirectory, lib),
                Path.Combine(appDirectory, "runtimes", GetPlatformIdentifier(), "native", lib),
                Path.Combine(appDirectory, "native", lib)
            };

            var found = searchPaths.Any(File.Exists);

            if (found)
                foundLibs.Add(lib);
            else
                missingLibs.Add(lib);
        }

        if (foundLibs.Count > 0)
        {
            Console.WriteLine($"Found {foundLibs.Count} native dependencies:");
            foreach (var lib in foundLibs)
                Console.WriteLine($"  ✓ {lib}");
        }

        if (missingLibs.Count > 0)
        {
            Console.WriteLine($"\nWarning: Missing {missingLibs.Count} native dependencies:");
            foreach (var lib in missingLibs)
                Console.WriteLine($"  ✗ {lib}");
            Console.WriteLine("\nThese libraries may be provided by the system or GPU drivers.");
            Console.WriteLine("Or they may be in different locations. The application may still work.");
        }
    }

    private Dictionary<string, Dictionary<string, string>> GetPlatformLibraryMappings()
    {
        return new Dictionary<string, Dictionary<string, string>>
        {
            ["Veldrid"] = new()
            {
                ["Windows"] = "Veldrid.dll (managed) + vulkan-1.dll, d3dcompiler_47.dll (native)",
                ["Linux"] = "Veldrid.dll (managed) + libvulkan.so.1 (native)",
                ["macOS"] = "Veldrid.dll (managed) + libMoltenVK.dylib (native)"
            },
            ["SkiaSharp"] = new()
            {
                ["Windows"] = "SkiaSharp.dll (managed) + libSkiaSharp.dll (native)",
                ["Linux"] = "SkiaSharp.dll (managed) + libSkiaSharp.so (native)",
                ["macOS"] = "SkiaSharp.dll (managed) + libSkiaSharp.dylib (native)"
            },
            ["ImGui.NET"] = new()
            {
                ["Windows"] = "ImGui.NET.dll (managed) + cimgui.dll (native)",
                ["Linux"] = "ImGui.NET.dll (managed) + cimgui.so (native)",
                ["macOS"] = "ImGui.NET.dll (managed) + cimgui.dylib (native)"
            },
            ["SDL2"] = new()
            {
                ["Windows"] = "Veldrid.SDL2.dll (managed) + SDL2.dll (native)",
                ["Linux"] = "Veldrid.SDL2.dll (managed) + libSDL2-2.0.so.0 (native)",
                ["macOS"] = "Veldrid.SDL2.dll (managed) + libSDL2.dylib (native)"
            },
            ["Silk.NET.OpenCL"] = new()
            {
                ["Windows"] = "Silk.NET.OpenCL.dll (managed) + OpenCL.dll (from GPU driver)",
                ["Linux"] = "Silk.NET.OpenCL.dll (managed) + libOpenCL.so (from GPU driver)",
                ["macOS"] = "Silk.NET.OpenCL.dll (managed) + OpenCL.framework (system)"
            }
        };
    }

    private async Task CompileAddInAsync(string sourceFile, string outputDir, List<MetadataReference> references)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourceFile);
        var outputPath = Path.Combine(outputDir, $"{fileName}.dll");

        Console.Write($"Compiling {fileName}... ");

        try
        {
            var sourceCode = await File.ReadAllTextAsync(sourceFile);
            var parseOptions =
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion
                    .CSharp11); // .NET 8 supports up to C# 12, but 11 is safer
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, parseOptions, sourceFile, Encoding.UTF8);

            var compilationOptions = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                platform: Platform.AnyCpu,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Disable); // Match csproj setting

            var compilation = CSharpCompilation.Create(
                fileName,
                new[] { syntaxTree },
                references,
                compilationOptions);

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (emitResult.Success)
            {
                ms.Seek(0, SeekOrigin.Begin);
                await File.WriteAllBytesAsync(outputPath, ms.ToArray());
                Console.WriteLine("✓");
                Log($"Successfully compiled to: {outputPath}");
            }
            else
            {
                Console.WriteLine("✗");
                Console.Error.WriteLine($"\n  Compilation failed for {fileName}:");

                var errors = emitResult.Diagnostics
                    .Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error)
                    .Take(20); // Limit error output

                foreach (var diagnostic in errors)
                {
                    var pos = diagnostic.Location.GetLineSpan();
                    Console.Error.WriteLine(
                        $"    {pos.Path}({pos.StartLinePosition.Line + 1},{pos.StartLinePosition.Character + 1}): error {diagnostic.Id}: {diagnostic.GetMessage()}");
                }

                var totalErrors =
                    emitResult.Diagnostics.Count(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error);
                if (totalErrors > 20) Console.Error.WriteLine($"    ... and {totalErrors - 20} more errors");

                Console.Error.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("✗");
            Console.Error.WriteLine($"  An unexpected error occurred: {ex.Message}");
            if (_verbose) Console.Error.WriteLine($"  Stack trace: {ex.StackTrace}");
        }
    }

    private void Log(string message)
    {
        if (_verbose) Console.WriteLine($"[DEBUG] {message}");
    }
}

// Helper class for file globbing
public class DirectoryInfoWrapper : DirectoryInfoBase
{
    private readonly DirectoryInfo _directoryInfo;

    public DirectoryInfoWrapper(DirectoryInfo directoryInfo)
    {
        _directoryInfo = directoryInfo;
    }

    public override string FullName => _directoryInfo.FullName;
    public override string Name => _directoryInfo.Name;

    public override DirectoryInfoBase? ParentDirectory => _directoryInfo.Parent != null
        ? new DirectoryInfoWrapper(_directoryInfo.Parent)
        : null;

    public override IEnumerable<FileSystemInfoBase> EnumerateFileSystemInfos()
    {
        try
        {
            return _directoryInfo.EnumerateFileSystemInfos().Select<FileSystemInfo, FileSystemInfoBase>(info =>
            {
                if (info is DirectoryInfo dir)
                    return new DirectoryInfoWrapper(dir);
                return new FileInfoWrapper((FileInfo)info);
            });
        }
        catch (DirectoryNotFoundException)
        {
            return Enumerable.Empty<FileSystemInfoBase>();
        }
    }

    public override DirectoryInfoBase GetDirectory(string path)
    {
        var fullPath = Path.Combine(FullName, path);
        if (Directory.Exists(fullPath)) return new DirectoryInfoWrapper(new DirectoryInfo(fullPath));
        return null!;
    }

    public override FileInfoBase GetFile(string path)
    {
        return new FileInfoWrapper(new FileInfo(Path.Combine(FullName, path)));
    }
}

public class FileInfoWrapper : FileInfoBase
{
    private readonly FileInfo _fileInfo;

    public FileInfoWrapper(FileInfo fileInfo)
    {
        _fileInfo = fileInfo;
    }

    public override string FullName => _fileInfo.FullName;
    public override string Name => _fileInfo.Name;
    public override DirectoryInfoBase ParentDirectory => new DirectoryInfoWrapper(_fileInfo.Directory!);
}