using Gtk;

namespace GeoscientistToolkit.GtkUI;

/// <summary>
/// Minimal splash screen that displays while the GTK edition wires up
/// settings, project defaults, and the default reactor dataset.
/// </summary>
public sealed class SplashWindow : Window
{
    public SplashWindow() : base("Geoscientist's Toolkit - Reactor (GTK) Loading…")
    {
        Decorated = false;
        Resizable = false;
        SetDefaultSize(420, 200);
        SetPosition(WindowPosition.CenterAlways);

        var layout = new VBox(false, 12)
        {
            BorderWidth = 16
        };

        var logo = new Image(GtkResourceLoader.LoadLogoPixbuf(320, 152));

        var title = new Label("<big><b>Geoscientist's Toolkit - Reactor (GTK)</b></big>")
        {
            UseMarkup = true,
            Xalign = 0.5f
        };
        var subtitle = new Label("Preparazione di mesh, materiali e nodi…")
        {
            Xalign = 0.5f
        };

        var spinner = new Spinner();
        spinner.Start();
        spinner.WidthRequest = 48;
        spinner.HeightRequest = 48;

        layout.PackStart(logo, false, false, 0);
        layout.PackStart(title, false, false, 0);
        layout.PackStart(subtitle, false, false, 0);
        layout.PackStart(spinner, true, true, 0);

        Add(layout);
        ShowAll();
    }
}
