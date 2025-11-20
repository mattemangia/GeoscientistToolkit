using System;
using Gtk;

namespace GeoscientistToolkit.GTK
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.Init();
            var app = new Application("org.GeoscientistToolkit.GTK", GLib.ApplicationFlags.None);
            app.Register(GLib.Cancellable.Current);

            var win = new MainWindow();
            app.AddWindow(win);

            win.Show();
            Application.Run();
        }
    }
}
