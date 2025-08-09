using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace GeoscientistToolkit.AddInExtractor
{
    class Program
    {
        static async Task<int> Main(string[] args)
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
                var outputDir = string.IsNullOrEmpty(output) ? Path.Combine("bin", "Release", "net8.0", "AddIns") : output;

                await ExtractAddIns(sourceDir, outputDir, core, main, verbose);
            }, sourceOption, outputOption, coreAssemblyOption, mainAssemblyOption, verboseOption, diagnosticsOption);

            return await rootCommand.InvokeAsync(args);
        }

        static void RunDiagnostics(string? coreAssemblyPath)
        {
            Console.WriteLine("Running AddInExtractor Diagnostics");
            Console.WriteLine("==================================");

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
                var assembly = System.Reflection.Assembly.LoadFrom(coreAssemblyPath);
                Console.WriteLine($"Loaded: {assembly.GetName().Name} v{assembly.GetName().Version}");

                var allTypes = assembly.GetExportedTypes();
                Console.WriteLine($"\nTotal exported types: {allTypes.Length}");

                // Group by namespace
                var namespaces = allTypes.GroupBy(t => t.Namespace ?? "<no namespace>")
                    .OrderBy(g => g.Key)
                    .ToList();

                Console.WriteLine($"\nNamespaces ({namespaces.Count}):");
                foreach (var ns in namespaces)
                {
                    Console.WriteLine($"  {ns.Key} ({ns.Count()} types)");
                }

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
                    foreach (var type in addInTypes)
                    {
                        Console.WriteLine($"  - {type.Name}");
                    }
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
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        static async Task ExtractAddIns(string sourceDir, string outputDir, string? coreAssemblyPath, string? mainAssemblyPath, bool verbose)
        {
            var extractor = new AddInExtractor(verbose);

            if (string.IsNullOrEmpty(coreAssemblyPath))
            {
                coreAssemblyPath = extractor.FindCoreAssembly();
                if (coreAssemblyPath == null)
                {
                    Console.Error.WriteLine("Error: Could not find GeoscientistToolkit.dll. Please specify its path with the --core option.");
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
            {
                if (File.Exists(path))
                {
                    Log($"Found core assembly at: {path}");
                    return Path.GetFullPath(path);
                }
            }
            return null;
        }

        public async Task ExtractAndCompileAsync(string sourceDir, string outputDir, string coreAssemblyPath, string? mainAssemblyPath)
        {
            Console.WriteLine("GeoscientistToolkit Add-In Extractor");
            Console.WriteLine($"Platform: {RuntimeInformation.RuntimeIdentifier}");
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
                var testLoad = System.Reflection.Assembly.LoadFrom(coreAssemblyPath);
                Console.WriteLine($"Core assembly loaded successfully: {testLoad.GetName().Name} v{testLoad.GetName().Version}");
                var coreTypes = testLoad.GetExportedTypes();
                var addInTypes = coreTypes.Where(t => t.Namespace?.StartsWith("GeoscientistToolkit.AddIns") == true).ToList();
                Console.WriteLine($"Found {addInTypes.Count} types in GeoscientistToolkit.AddIns namespace");
                if (_verbose && addInTypes.Count > 0)
                {
                    foreach (var type in addInTypes.Take(5))
                    {
                        Console.WriteLine($"  - {type.FullName}");
                    }
                }
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

            Console.WriteLine($"Found {sourceFiles.Count} add-in source file(s):");
            foreach (var file in sourceFiles)
            {
                Console.WriteLine($"  - {Path.GetFileName(file)}");
            }
            Console.WriteLine();

            // Load all necessary references
            var references = await LoadAllReferencesAsync(coreAssemblyPath, mainAssemblyPath);

            // Verify critical references for the main packages
            VerifyCriticalReferences(references, Path.GetDirectoryName(coreAssemblyPath)!);

            Console.WriteLine($"Loaded {references.Count} assembly references for compilation.");

            var tasks = sourceFiles.Select(file => CompileAddInAsync(file, outputDir, references)).ToList();
            await Task.WhenAll(tasks);

            Console.WriteLine();
            Console.WriteLine("Add-in extraction complete!");
        }

        private async Task<List<MetadataReference>> LoadAllReferencesAsync(string coreAssemblyPath, string? mainAssemblyPath)
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
                {
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
                // Add native runtime directories for current platform
                var currentRid = RuntimeInformation.RuntimeIdentifier;
                var possibleRids = new[] { currentRid, "win-x64", "linux-x64", "osx-x64", "osx-arm64" };

                foreach (var rid in possibleRids)
                {
                    var ridLibDir = Path.Combine(runtimesDir, rid, "lib", "net8.0");
                    if (Directory.Exists(ridLibDir))
                    {
                        searchDirectories.Add(ridLibDir);
                        Log($"Added runtime directory: {ridLibDir}");
                    }
                }
            }

            // Scan all directories for assemblies
            foreach (var directory in searchDirectories)
            {
                var assemblies = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories);
                foreach (var file in assemblies)
                {
                    var fileName = Path.GetFileName(file);

                    // Skip native libraries and duplicates (including the core assembly we already added)
                    if (IsNativeLibrary(fileName) || referencedAssemblies.Contains(fileName))
                        continue;

                    if (referencedAssemblies.Add(fileName))
                    {
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
                typeof(object).Assembly.Location,                    // System.Private.CoreLib
                typeof(Console).Assembly.Location,                   // System.Console
                typeof(IEnumerable<>).Assembly.Location,            // System.Collections
                typeof(System.Linq.Enumerable).Assembly.Location,   // System.Linq
                typeof(System.Text.StringBuilder).Assembly.Location, // System.Runtime
                typeof(System.IO.File).Assembly.Location,           // System.IO.FileSystem
                typeof(System.Diagnostics.Debug).Assembly.Location, // System.Diagnostics.Debug
                typeof(System.Drawing.Color).Assembly.Location,     // System.Drawing.Primitives
                typeof(System.Numerics.Vector2).Assembly.Location   // System.Numerics
            };

            foreach (var assembly in systemAssemblies.Where(a => !string.IsNullOrEmpty(a)))
            {
                var fileName = Path.GetFileName(assembly);
                if (referencedAssemblies.Add(fileName))
                {
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
            }

            // Log summary
            Log($"Total references loaded: {references.Count}");

            // In verbose mode, list loaded assemblies
            if (_verbose)
            {
                var loadedAssemblies = new List<string>();
                foreach (var reference in references)
                {
                    if (reference is PortableExecutableReference peRef && peRef.FilePath != null)
                    {
                        loadedAssemblies.Add(Path.GetFileName(peRef.FilePath));
                    }
                }

                Log($"Loaded assemblies ({loadedAssemblies.Count}):");
                foreach (var assembly in loadedAssemblies.OrderBy(a => a).Take(20))
                {
                    Log($"  - {assembly}");
                }
                if (loadedAssemblies.Count > 20)
                {
                    Log($"  ... and {loadedAssemblies.Count - 20} more");
                }
            }

            return references;
        }

        private bool IsNativeLibrary(string fileName)
        {
            // Common patterns for native libraries
            var nativePatterns = new[]
            {
                "native", "libskia", "libHarfBuzzSharp", "SDL2", "vulkan", "d3d",
                "opengl", "metal", "avcodec", "avformat", "avutil", "swscale",
                "e_sqlite3", "glfw", "freetype", "png", "jpeg", "tiff"
            };

            var lowerFileName = fileName.ToLowerInvariant();
            return nativePatterns.Any(pattern => lowerFileName.Contains(pattern)) ||
                   lowerFileName.EndsWith(".so") ||
                   lowerFileName.EndsWith(".dylib") ||
                   (lowerFileName.EndsWith(".dll") && !lowerFileName.Contains(".net"));
        }

        private void VerifyCriticalReferences(List<MetadataReference> references, string appDirectory)
        {
            // List of critical assemblies from your NuGet packages
            var criticalAssemblies = new[]
            {
                "ImGui.NET.dll",
                "ImGui.NET-Docking.dll",
                "Veldrid.dll",
                "Veldrid.ImGui.dll",
                "Veldrid.MetalBindings.dll",
                "Veldrid.SDL2.dll",
                "Veldrid.SPIRV.dll",
                "Veldrid.StartupUtilities.dll",
                "SkiaSharp.dll",
                "SkiaSharp.HarfBuzz.dll",
                "StbImageSharp.dll",
                "StbImageWriteSharp.dll",
                "TinyDialogsNet.dll",
                "BitMiracle.LibTiff.NET.dll",
                "System.Management.dll",
                "Silk.NET.OpenCL.dll" // Added for GPU Compute
            };

            var loadedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Extract file names from references
            foreach (var reference in references)
            {
                if (reference is PortableExecutableReference peRef && peRef.FilePath != null)
                {
                    loadedFiles.Add(Path.GetFileName(peRef.FilePath));
                }
            }

            var missingAssemblies = new List<string>();

            foreach (var assembly in criticalAssemblies)
            {
                if (!loadedFiles.Contains(assembly))
                {
                    // Try to find and load the missing assembly
                    var possiblePaths = new[]
                    {
                        Path.Combine(appDirectory, assembly),
                        Path.Combine(appDirectory, "runtimes", "win-x64", "lib", "net8.0", assembly),
                        Path.Combine(appDirectory, "runtimes", "linux-x64", "lib", "net8.0", assembly),
                        Path.Combine(appDirectory, "runtimes", "osx-x64", "lib", "net8.0", assembly),
                        Path.Combine(appDirectory, "runtimes", "osx-arm64", "lib", "net8.0", assembly)
                    };

                    bool found = false;
                    foreach (var path in possiblePaths)
                    {
                        if (File.Exists(path))
                        {
                            try
                            {
                                references.Add(MetadataReference.CreateFromFile(path));
                                Log($"Added critical reference: {assembly}");
                                found = true;
                                break;
                            }
                            catch (Exception ex)
                            {
                                Log($"Warning: Could not load critical assembly '{assembly}': {ex.Message}");
                            }
                        }
                    }

                    if (!found)
                    {
                        missingAssemblies.Add(assembly);
                    }
                }
            }

            if (missingAssemblies.Count > 0)
            {
                Console.WriteLine($"Warning: Some expected assemblies were not found:");
                foreach (var assembly in missingAssemblies)
                {
                    Console.WriteLine($"  - {assembly}");
                }
                Console.WriteLine("Add-ins may not compile if they reference types from these assemblies.");
            }
        }

        private async Task CompileAddInAsync(string sourceFile, string outputDir, List<MetadataReference> references)
        {
            var fileName = Path.GetFileNameWithoutExtension(sourceFile);
            var outputPath = Path.Combine(outputDir, $"{fileName}.dll");

            Console.Write($"Compiling {fileName}... ");

            try
            {
                var sourceCode = await File.ReadAllTextAsync(sourceFile);
                var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
                var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, parseOptions, sourceFile, System.Text.Encoding.UTF8);

                var compilationOptions = new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    platform: Platform.AnyCpu,
                    allowUnsafe: true,
                    nullableContextOptions: NullableContextOptions.Disable); // Match csproj setting

                var compilation = CSharpCompilation.Create(
                    fileName,
                    syntaxTrees: new[] { syntaxTree },
                    references: references,
                    options: compilationOptions);

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
                        Console.Error.WriteLine($"    {pos.Path}({pos.StartLinePosition.Line + 1},{pos.StartLinePosition.Character + 1}): error {diagnostic.Id}: {diagnostic.GetMessage()}");
                    }

                    var totalErrors = emitResult.Diagnostics.Count(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error);
                    if (totalErrors > 20)
                    {
                        Console.Error.WriteLine($"    ... and {totalErrors - 20} more errors");
                    }

                    Console.Error.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("✗");
                Console.Error.WriteLine($"  An unexpected error occurred: {ex.Message}");
                if (_verbose)
                {
                    Console.Error.WriteLine($"  Stack trace: {ex.StackTrace}");
                }
            }
        }

        private void Log(string message)
        {
            if (_verbose)
            {
                Console.WriteLine($"[DEBUG] {message}");
            }
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
                    else
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
            string fullPath = Path.Combine(FullName, path);
            if (Directory.Exists(fullPath))
            {
                return new DirectoryInfoWrapper(new DirectoryInfo(fullPath));
            }
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
}