using System;
using Gtk;
using GeoscientistToolkit.Settings;
using GeoscientistToolkit.GTK.UI;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.GTK
{
    public class MainWindow : Window
    {
        public MainWindow() : base("Geoscientist Toolkit (GTK)")
        {
            SetDefaultSize(1200, 800);
            SetPosition(WindowPosition.Center);

            // Dark Theme
            var settings = SettingsManager.Instance.Settings;
            if (settings != null)
            {
                settings.GtkApplicationPreferDarkTheme = true;
                Gtk.Settings.Default.ApplicationPreferDarkTheme = true;
            }

            DeleteEvent += delegate { Application.Quit(); };

            // Main Layout
            var vbox = new Box(Orientation.Vertical, 0);
            Add(vbox);

            // Menu Bar
            var menuBar = new MenuBar();
            vbox.PackStart(menuBar, false, false, 0);

            // File Menu
            var fileMenu = new Menu();
            var fileMenuItem = new MenuItem("File");
            fileMenuItem.Submenu = fileMenu;
            menuBar.Append(fileMenuItem);

            var quitItem = new MenuItem("Quit");
            quitItem.Activated += (sender, e) => Application.Quit();
            fileMenu.Append(quitItem);

            // Borehole Menu
            var boreholeMenu = new Menu();
            var boreholeMenuItem = new MenuItem("Borehole");
            boreholeMenuItem.Submenu = boreholeMenu;
            menuBar.Append(boreholeMenuItem);

            var newBoreholeItem = new MenuItem("New Dataset...");
            newBoreholeItem.Activated += (s, e) => {
                var ds = new BoreholeDataset("New Borehole", "");
                ds.TotalDepth = 200;
                ds.AddLithologyUnit(new LithologyUnit { Name="Top Soil", DepthFrom=0, DepthTo=10, Color=new System.Numerics.Vector4(0.6f, 0.5f, 0.2f, 1) });
                ds.AddLithologyUnit(new LithologyUnit { Name="Sandstone", DepthFrom=10, DepthTo=150, Color=new System.Numerics.Vector4(0.9f, 0.8f, 0.4f, 1) });
                new BoreholeWindow(ds).Show();
            };
            boreholeMenu.Append(newBoreholeItem);

            // Simulation Menu
            var simMenu = new Menu();
            var simMenuItem = new MenuItem("Simulation");
            simMenuItem.Submenu = simMenu;
            menuBar.Append(simMenuItem);

            var physicoChemItem = new MenuItem("PhysicoChem...");
            physicoChemItem.Activated += (s, e) => {
                var ds = new PhysicoChemDataset("New Simulation");
                ds.GenerateMesh(20);
                ds.InitializeState();
                new PhysicoChemWindow(ds).Show();
            };
            simMenu.Append(physicoChemItem);

            // Notebook (Tabs) for content
            var notebook = new Notebook();
            vbox.PackStart(notebook, true, true, 0);

            var welcomeLabel = new Label("Welcome to GeoscientistToolkit GTK Edition\nUse the menu to open tools.");
            welcomeLabel.Justify = Justification.Center;
            notebook.AppendPage(welcomeLabel, new Label("Home"));

            ShowAll();
        }
    }
}
