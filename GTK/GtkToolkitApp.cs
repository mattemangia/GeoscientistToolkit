using GeoscientistToolkit.Business;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;
using Gtk;
using GtkApplication = Gtk.Application;

namespace GeoscientistToolkit.Gtk;

/// <summary>
/// Boots the GTK front-end while reusing the core GeoscientistToolkit runtime
/// (datasets, simulations, node manager and settings) so that the mesh and
/// distributed computing stacks behave identically to the ImGui edition.
/// </summary>
public sealed class GtkToolkitApp
{
    private MainGtkWindow? _window;

    public void Run(string[] args)
    {
        SettingsManager.Instance.LoadSettings();

        var projectManager = ProjectManager.Instance;
        projectManager.NewProject();

        var projectPath = args?.FirstOrDefault(a => a.EndsWith(".gtp", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            try
            {
                projectManager.LoadProject(projectPath);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Impossibile caricare il progetto '{projectPath}': {ex.Message}");
            }
        }

        TryStartNodeManager();

        _window = new MainGtkWindow(projectManager, SettingsManager.Instance, NodeManager.Instance);
        _window.DeleteEvent += (_, _) => Shutdown();
        _window.ShowAll();
    }

    private void TryStartNodeManager()
    {
        try
        {
            var settings = SettingsManager.Instance.Settings.NodeManager;
            if (settings.EnableNodeManager)
                NodeManager.Instance.Start();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Errore durante l'avvio del NodeManager: {ex.Message}");
        }
    }

    private void Shutdown()
    {
        try
        {
            NodeManager.Instance.Stop();
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Errore durante la chiusura del NodeManager: {ex.Message}");
        }

        GtkApplication.Quit();
    }
}
