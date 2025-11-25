// GeoscientistToolkit/GTK/Dialogs/BoreholeLegend.cs

using GeoscientistToolkit.Data.Borehole;
using Gtk;
using System.Linq;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class BoreholeLegend : Window
    {
        private Grid _grid;

        public BoreholeLegend(BoreholeDataset dataset) : base(WindowType.Toplevel)
        {
            Title = "Borehole Legend";
            SetDefaultSize(320, 240);

            _grid = new Grid { ColumnSpacing = 10, RowSpacing = 5, Margin = 10 };
            Add(_grid);

            UpdateDataset(dataset);
        }

        public void UpdateDataset(BoreholeDataset dataset)
        {
            foreach (var child in _grid.Children)
                _grid.Remove(child);

            var visibleTracks = dataset.ParameterTracks.Values.Where(t => t.IsVisible).ToList();
            if (visibleTracks.Any())
            {
                _grid.Attach(new Label("<b>Tracks</b>") { UseMarkup = true, Halign = Align.Start }, 0, 0, 2, 1);
                for (var i = 0; i < visibleTracks.Count; i++)
                {
                    var track = visibleTracks[i];
                    var colorBox = new DrawingArea();
                    colorBox.SetSizeRequest(20, 12);
                    colorBox.Drawn += (s, a) => {
                        var cr = a.Cr;
                        cr.SetSourceRGBA(track.Color.X, track.Color.Y, track.Color.Z, track.Color.W);
                        cr.Rectangle(0, 0, 20, 12);
                        cr.Fill();
                    };
                    _grid.Attach(colorBox, 0, i + 1, 1, 1);
                    var label = string.IsNullOrWhiteSpace(track.Unit) ? track.Name : $"{track.Name} [{track.Unit}]";
                    _grid.Attach(new Label(label) { Halign = Align.Start }, 1, i + 1, 1, 1);
                }
            }

            var lithoTypes = dataset.LithologyUnits.Select(u => u.LithologyType).Distinct().ToList();
            if (lithoTypes.Any())
            {
                var offset = visibleTracks.Count + 1;
                _grid.Attach(new Label("<b>Lithologies</b>") { UseMarkup = true, Halign = Align.Start }, 0, offset, 2, 1);
                for (var i = 0; i < lithoTypes.Count; i++)
                {
                    var lithoType = lithoTypes[i];
                    var unit = dataset.LithologyUnits.First(u => u.LithologyType == lithoType);
                    var colorBox = new DrawingArea();
                    colorBox.SetSizeRequest(20, 12);
                    colorBox.Drawn += (s, a) => {
                        var cr = a.Cr;
                        cr.SetSourceRGBA(unit.Color.X, unit.Color.Y, unit.Color.Z, unit.Color.W);
                        cr.Rectangle(0, 0, 20, 12);
                        cr.Fill();
                    };
                    _grid.Attach(colorBox, 0, offset + i + 1, 1, 1);
                    _grid.Attach(new Label(lithoType) { Halign = Align.Start }, 1, offset + i + 1, 1, 1);
                }
            }
        }
    }
}
