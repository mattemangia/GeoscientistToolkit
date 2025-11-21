using Gtk;
using GtkApplication = Gtk.Application;

namespace GeoscientistToolkit.Gtk;

public static class Program
{
    public static void Main(string[] args)
    {
        GtkApplication.Init();

        var app = new GtkToolkitApp();
        app.Run(args);

        GtkApplication.Run();
    }
}
