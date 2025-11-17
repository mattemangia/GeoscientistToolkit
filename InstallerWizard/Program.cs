using GeoscientistToolkit.Installer.Models;
using GeoscientistToolkit.Installer.Services;
using GeoscientistToolkit.Installer.Utilities;
using Terminal.Gui;

namespace GeoscientistToolkit.Installer;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var settings = InstallerSettingsLoader.Load();
            var manifestLoader = new ManifestLoader(settings);
            var updateService = new UpdateService(manifestLoader, settings);
            var planBuilder = new InstallPlanBuilder();
            var archiveInstaller = new ArchiveInstallService();
            var wizard = new InstallerWizardApp(settings, manifestLoader, updateService, planBuilder, archiveInstaller);
            wizard.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Errore critico: {ex.Message}\n{ex}");
            return 1;
        }
    }
}
