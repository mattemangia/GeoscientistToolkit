// GeoscientistToolkit/Program.cs
// This file contains the main entry point for the GeoscientistToolkit application.
// It is responsible for creating and running the main application instance.

namespace GeoscientistToolkit;

public static class Program
{
    public static string StartingProjectPath { get; private set; }

    public static void Main(string[] args)
    {
        if (args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
                            || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        {
            ShowHelp();
            return;
        }

        var diagnosticOptions = ParseDiagnosticOptions(args);
        if (diagnosticOptions != null)
        {
            var diagnosticApp = new UI.Diagnostics.DiagnosticApp(diagnosticOptions);
            diagnosticApp.Run();
            return;
        }

        // Check for file path in command line arguments
        if (args.Length > 0 && args[0].EndsWith(".gtp", StringComparison.OrdinalIgnoreCase))
            StartingProjectPath = args[0];

        var app = new Application();
        app.Run();
    }

    private static void ShowHelp()
    {
        Console.WriteLine("GeoscientistToolkit command line options:");
        Console.WriteLine("  --help, -h                Show this help message.");
        Console.WriteLine("  --ai-diagnostic           Run AI diagnostics.");
        Console.WriteLine("  --gui-diagnostic          Run GUI diagnostics.");
        Console.WriteLine("  --test[=filters]          Run tests (optional comma-separated filters).");
        Console.WriteLine("  <path>.gtp                Open a project file on launch.");
    }

    private static UI.Diagnostics.DiagnosticOptions ParseDiagnosticOptions(string[] args)
    {
        var runAiDiagnostic = args.Any(arg => arg.Equals("--ai-diagnostic", StringComparison.OrdinalIgnoreCase));
        var runGuiDiagnostic = args.Any(arg => arg.Equals("--gui-diagnostic", StringComparison.OrdinalIgnoreCase));
        var testArgument = args.FirstOrDefault(arg => arg.StartsWith("--test", StringComparison.OrdinalIgnoreCase));

        if (!runAiDiagnostic && !runGuiDiagnostic && testArgument == null)
            return null;

        var testFilters = Array.Empty<string>();
        var runTests = testArgument != null;

        if (testArgument != null)
        {
            var value = string.Empty;
            if (testArgument.Contains('='))
            {
                var parts = testArgument.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                value = parts.Length > 1 ? parts[1] : string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(value) && !value.Equals("all", StringComparison.OrdinalIgnoreCase))
                testFilters = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return new UI.Diagnostics.DiagnosticOptions
        {
            RunAiDiagnostic = runAiDiagnostic,
            RunGuiDiagnostic = runGuiDiagnostic,
            RunTests = runTests,
            TestFilters = testFilters
        };
    }
}
