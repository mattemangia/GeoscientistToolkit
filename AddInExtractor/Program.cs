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

            rootCommand.AddOption(sourceOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(coreAssemblyOption);
            rootCommand.AddOption(mainAssemblyOption);
            rootCommand.AddOption(verboseOption);

            rootCommand.SetHandler(async (source, output, core, main, verbose) =>
            {
                // Use current directory as a fallback for source if not provided.
                var sourceDir = string.IsNullOrEmpty(source) ? Directory.GetCurrentDirectory() : source;
                
                // Use a default output relative to the main project's build output if not provided.
                var outputDir = string.IsNullOrEmpty(output) ? Path.Combine("bin", "Release", "net8.0", "AddIns") : output;

                await ExtractAddIns(sourceDir, outputDir, core, main, verbose);
            }, sourceOption, outputOption, coreAssemblyOption, mainAssemblyOption, verboseOption);

            return await rootCommand.InvokeAsync(args);
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
            Console.WriteLine();

            if (!Directory.Exists(sourceDir))
            {
                Console.Error.WriteLine($"Error: Source directory not found: {sourceDir}");
                return; // Don't exit, just inform that no add-ins can be built.
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

            // --- KEY CHANGE: Dynamically load all dependencies ---
            var references = LoadAllReferencesFrom(Path.GetDirectoryName(coreAssemblyPath)!);

            // Add main assembly reference separately if provided and it exists
            if (!string.IsNullOrEmpty(mainAssemblyPath) && File.Exists(mainAssemblyPath))
            {
                references.Add(MetadataReference.CreateFromFile(mainAssemblyPath));
                Log($"Added main executable reference: {Path.GetFileName(mainAssemblyPath)}");
            }
            
            Console.WriteLine($"Loaded {references.Count} assembly references for compilation.");

            var tasks = sourceFiles.Select(file => CompileAddInAsync(file, outputDir, references)).ToList();
            await Task.WhenAll(tasks);

            Console.WriteLine();
            Console.WriteLine("Add-in extraction complete!");
        }

        private List<MetadataReference> LoadAllReferencesFrom(string searchDir)
        {
            Log($"Scanning for reference assemblies in: {searchDir}");
            var references = new List<MetadataReference>();
            
            // This is the core of the new logic: find ALL DLLs in the main app's output directory.
            // This includes framework DLLs, NuGet package DLLs, and the main app's DLL.
            var assemblyFiles = Directory.GetFiles(searchDir, "*.dll");

            foreach (var file in assemblyFiles)
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(file));
                    Log($"Added reference: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    // It's possible some native DLLs can't be loaded as metadata, so we just log and skip.
                    Log($"Warning: Could not load reference '{Path.GetFileName(file)}'. This is often normal for native libraries. Reason: {ex.Message}");
                }
            }
            return references;
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
                }
                else
                {
                    Console.WriteLine("✗");
                    Console.Error.WriteLine($"\n  Compilation failed for {fileName}:");
                    foreach (var diagnostic in emitResult.Diagnostics.Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error))
                    {
                        var pos = diagnostic.Location.GetLineSpan();
                        Console.Error.WriteLine($"    {pos.Path}({pos.StartLinePosition.Line + 1},{pos.StartLinePosition.Character + 1}): error {diagnostic.Id}: {diagnostic.GetMessage()}");
                    }
                    Console.Error.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("✗");
                Console.Error.WriteLine($"  An unexpected error occurred: {ex.Message}");
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