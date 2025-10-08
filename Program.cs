// GeoscientistToolkit/Program.cs
// This file contains the main entry point for the GeoscientistToolkit application.
// It is responsible for creating and running the main application instance.

namespace GeoscientistToolkit;

public static class Program
{
    public static string StartingProjectPath { get; private set; }

    public static void Main(string[] args)
    {
        // Check for file path in command line arguments
        if (args.Length > 0 && args[0].EndsWith(".gtp", StringComparison.OrdinalIgnoreCase))
            StartingProjectPath = args[0];

        var app = new Application();
        app.Run();
    }
}