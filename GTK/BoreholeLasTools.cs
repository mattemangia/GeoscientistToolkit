// GeoscientistToolkit/GTK/BoreholeLasTools.cs

using System.IO;
using GeoscientistToolkit.Data.Borehole;
using GeoscientistToolkit.Data.Loaders;
using GeoscientistToolkit.Util;
using Gtk;
using System.Linq;

namespace GeoscientistToolkit.Gtk
{
    public static class BoreholeLasTools
    {
        public static void ImportFromLas(Window parent, BoreholeDataset dataset)
        {
            var dialog = new FileChooserDialog("Import LAS File", parent, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "Open", ResponseType.Accept);
            var filter = new FileFilter();
            filter.AddPattern("*.las");
            filter.Name = "LAS Files";
            dialog.AddFilter(filter);

            if (dialog.Run() == (int)ResponseType.Accept)
            {
                var loader = new LASLoader();
                loader.LoadFromFile(dialog.Filename, null);
                var loadedDataset = loader.GetDataset();
                if (loadedDataset is BoreholeDataset borehole)
                {
                    dataset.LithologyUnits = borehole.LithologyUnits;
                    dataset.ParameterTracks = borehole.ParameterTracks;
                    dataset.TotalDepth = borehole.TotalDepth;
                    Logger.Log($"Successfully imported LAS file: {dialog.Filename}");
                }
            }
            dialog.Destroy();
        }

        public static void ExportToLas(Window parent, BoreholeDataset dataset, double step)
        {
            var dialog = new FileChooserDialog("Export to LAS File", parent, FileChooserAction.Save, "Cancel", ResponseType.Cancel, "Save", ResponseType.Accept);
            dialog.CurrentName = $"{dataset.Name}.las";

            if (dialog.Run() == (int)ResponseType.Accept)
            {
                try
                {
                    using (var writer = new StreamWriter(dialog.Filename))
                    {
                        // Minimal LAS 2.0 export
                        writer.WriteLine("~Version Information");
                        writer.WriteLine(" VERS.      2.0: ");
                        writer.WriteLine(" WRAP.      NO: ");
                        writer.WriteLine("~Well Information");
                        var startDepth = dataset.LithologyUnits.Any() ? dataset.LithologyUnits.Min(u => u.DepthFrom) : 0;
                        writer.WriteLine($" STRT.M      {startDepth:F4}");
                        writer.WriteLine($" STOP.M      {dataset.TotalDepth:F4}");
                        writer.WriteLine($" STEP.M      {step:F4}: ");
                        writer.WriteLine($" NULL.      -999.25: ");
                        writer.WriteLine($" WELL.      {dataset.WellName}: ");
                        writer.WriteLine("~Curve Information");
                        writer.WriteLine(" DEPT.M         : Depth");
                        foreach (var track in dataset.ParameterTracks.Values)
                            writer.WriteLine($" {track.Name}.{track.Unit}      : {track.Name}");

                        writer.WriteLine("~A");

                        for (var depth = startDepth; depth <= dataset.TotalDepth; depth += (float)step)
                        {
                            writer.Write($"{depth:F4}".PadRight(15));
                            foreach (var track in dataset.ParameterTracks.Values)
                            {
                                var value = dataset.GetParameterValueAtDepth(track.Name, depth) ?? -999.25f;
                                writer.Write($"{value:F4}".PadRight(15));
                            }
                            writer.WriteLine();
                        }
                    }
                    Logger.Log($"Exported borehole data to LAS: {dialog.Filename}");
                }
                catch (System.Exception ex)
                {
                    Logger.LogError($"Failed to export to LAS: {ex.Message}");
                }
            }
            dialog.Destroy();
        }
    }
}
