using GeoscientistToolkit.Business;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;
using Gtk;
using GtkApplication = Gtk.Application;

namespace GeoscientistToolkit.GtkUI;

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
        ApplyDarkTheme();

        var splash = new SplashWindow();
        splash.ShowAll();
        while (GtkApplication.EventsPending())
            GtkApplication.RunIteration();

        var projectManager = ProjectManager.Instance;
        projectManager.NewProject();

        EnsureDefaultReactor(projectManager);

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

        GLib.Timeout.Add(1200, () =>
        {
            splash.Destroy();
            _window.ShowAll();
            return false;
        });
    }

    private static void ApplyDarkTheme()
    {
        var css = new CssProvider();
        css.LoadFromData(@"
            * { color: #e8edf7; }
            window, notebook, paned, toolbar, treeview, textview, entry, button, frame, drawingarea {
                background: #0f1116;
            }
            treeview header button { background: #1b1f2a; color: #e8edf7; }
            button { background: #1f2533; border-radius: 4px; }
            frame { border-color: #2c3445; }
            scale trough { background: #1b1f2a; }
            scale slider { background: #3b82f6; }
        ");

        StyleContext.AddProviderForScreen(
            Gdk.Screen.Default,
            css,
            Gtk.StyleProviderPriority.Application);
    }

    private static void EnsureDefaultReactor(ProjectManager projectManager)
    {
        try
        {
            if (projectManager.LoadedDatasets.OfType<Data.PhysicoChem.PhysicoChemDataset>().Any()) return;
            var reactor = Data.PhysicoChem.MultiphysicsExamples.CreateExothermicReactor(3.5, 6.0);
            reactor.Name = "Default Exothermic Reactor";
            reactor.Description += " (preset GTK)";
            projectManager.AddDataset(reactor);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Impossibile creare il reattore di default: {ex.Message}");
        }
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
