using GeoscientistToolkit.InstallerPackager.Models;

namespace GeoscientistToolkit.InstallerPackager.Utilities;

internal static class CommandLineParser
{
    public static CommandLineOptions Parse(string[] args)
    {
        var platforms = new List<string>();
        string? configuration = null;
        string? outputDirectory = null;
        string? version = null;
        string? packageBaseUrl = null;
        bool showHelp = false;
        bool interactive = false;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;

                case "-i":
                case "--interactive":
                    interactive = true;
                    break;

                case "-p":
                case "--platform":
                    if (i + 1 < args.Length)
                    {
                        platforms.Add(args[++i]);
                    }
                    else
                    {
                        throw new ArgumentException($"Missing value for {arg}");
                    }
                    break;

                case "-c":
                case "--config":
                case "--configuration":
                    if (i + 1 < args.Length)
                    {
                        configuration = args[++i];
                    }
                    else
                    {
                        throw new ArgumentException($"Missing value for {arg}");
                    }
                    break;

                case "-o":
                case "--output":
                    if (i + 1 < args.Length)
                    {
                        outputDirectory = args[++i];
                    }
                    else
                    {
                        throw new ArgumentException($"Missing value for {arg}");
                    }
                    break;

                case "-v":
                case "--version":
                    if (i + 1 < args.Length)
                    {
                        version = args[++i];
                    }
                    else
                    {
                        throw new ArgumentException($"Missing value for {arg}");
                    }
                    break;

                case "-u":
                case "--url":
                case "--base-url":
                    if (i + 1 < args.Length)
                    {
                        packageBaseUrl = args[++i];
                    }
                    else
                    {
                        throw new ArgumentException($"Missing value for {arg}");
                    }
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new CommandLineOptions
        {
            Platforms = platforms,
            Configuration = configuration,
            OutputDirectory = outputDirectory,
            Version = version,
            PackageBaseUrl = packageBaseUrl,
            ShowHelp = showHelp,
            Interactive = interactive
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine("GeoscientistToolkit InstallerPackager");
        Console.WriteLine();
        Console.WriteLine("USAGE:");
        Console.WriteLine("  dotnet run [options]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS:");
        Console.WriteLine("  -h, --help                 Show this help message");
        Console.WriteLine("  -i, --interactive          Launch interactive TUI (graphical terminal interface)");
        Console.WriteLine("  -p, --platform <RID>       Build for specific platform (can be specified multiple times)");
        Console.WriteLine("                             Examples: win-x64, linux-x64, osx-x64, osx-arm64");
        Console.WriteLine("  -c, --config <CONFIG>      Build configuration (Debug or Release)");
        Console.WriteLine("  -o, --output <PATH>        Output directory for packages");
        Console.WriteLine("  -v, --version <VERSION>    Version number for the manifest");
        Console.WriteLine("  -u, --url <URL>            Base URL for package downloads");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES:");
        Console.WriteLine("  dotnet run --interactive                      # Launch TUI for interactive building");
        Console.WriteLine("  dotnet run                                    # Build all platforms from manifest");
        Console.WriteLine("  dotnet run -p win-x64                         # Build only Windows x64");
        Console.WriteLine("  dotnet run -p win-x64 -p linux-x64            # Build Windows and Linux");
        Console.WriteLine("  dotnet run -c Debug -v 1.2.3                  # Debug build with version 1.2.3");
        Console.WriteLine("  dotnet run -o ./dist -u https://example.com   # Custom output and URL");
        Console.WriteLine();
        Console.WriteLine("CONFIGURATION:");
        Console.WriteLine("  Settings are loaded from 'packager-settings.json' and can be overridden");
        Console.WriteLine("  by command-line options. The manifest is auto-created if it doesn't exist.");
    }
}
