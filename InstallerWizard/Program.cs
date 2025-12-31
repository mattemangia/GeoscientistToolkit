using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.Installer.Services;
using GeoscientistToolkit.Installer.Utilities;

namespace GeoscientistToolkit.Installer;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var settings = InstallerSettingsLoader.Load();
            var manifestLoader = new ManifestLoader(settings);
            var updateService = new UpdateService(manifestLoader, settings);
            var planBuilder = new InstallPlanBuilder();
            var archiveInstaller = new ArchiveInstallService();
            var uiMode = ParseUiMode(args);

            if (uiMode == UiMode.Terminal)
            {
                RunTerminal(settings, manifestLoader, updateService, planBuilder, archiveInstaller);
                return 0;
            }

            try
            {
                var imGuiWizard = new InstallerImGuiApp(settings, manifestLoader, updateService, planBuilder, archiveInstaller);
                imGuiWizard.Run();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to start ImGui installer: {ex.Message}");
                if (uiMode == UiMode.ImGui)
                {
                    throw;
                }
                RunTerminal(settings, manifestLoader, updateService, planBuilder, archiveInstaller);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Critical error: {ex.Message}\n{ex}");
            return 1;
        }
    }

    private static void RunTerminal(
        InstallerSettings settings,
        ManifestLoader manifestLoader,
        UpdateService updateService,
        InstallPlanBuilder planBuilder,
        ArchiveInstallService archiveInstaller)
    {
        var wizard = new InstallerWizardApp(settings, manifestLoader, updateService, planBuilder, archiveInstaller);
        wizard.Run();
    }

    private static UiMode ParseUiMode(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--terminal", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--tui", StringComparison.OrdinalIgnoreCase))
            {
                return UiMode.Terminal;
            }

            if (arg.Equals("--imgui", StringComparison.OrdinalIgnoreCase))
            {
                return UiMode.ImGui;
            }

            if (arg.StartsWith("--ui=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg.Split('=', 2)[1];
                return ParseUiValue(value);
            }
        }

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--ui", StringComparison.OrdinalIgnoreCase))
            {
                return ParseUiValue(args[i + 1]);
            }
        }

        return UiMode.Auto;
    }

    private static UiMode ParseUiValue(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "terminal" => UiMode.Terminal,
            "tui" => UiMode.Terminal,
            "imgui" => UiMode.ImGui,
            _ => UiMode.Auto
        };
    }

    private enum UiMode
    {
        Auto,
        ImGui,
        Terminal
    }
}
