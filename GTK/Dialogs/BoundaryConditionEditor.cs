using System;
using Gtk;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class BoundaryConditionEditor : Dialog
    {
        public BoundaryConditionEditor(Window parent) : base("Boundary Condition Editor", parent, DialogFlags.Modal)
        {
            SetDefaultSize(500, 400);
            BorderWidth = 8;

            var content = new VBox(false, 6);
            content.PackStart(new Label("Configure Boundary Conditions for selected cells/faces") { Xalign = 0 }, false, false, 0);

            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10, BorderWidth = 10 };

            grid.Attach(new Label("Type:"), 0, 0, 1, 1);
            var typeCombo = new ComboBoxText();
            typeCombo.AppendText("Dirichlet (Fixed Value)");
            typeCombo.AppendText("Neumann (Flux)");
            typeCombo.AppendText("Robin (Mixed)");
            typeCombo.Active = 0;
            grid.Attach(typeCombo, 1, 0, 1, 1);

            grid.Attach(new Label("Value:"), 0, 1, 1, 1);
            var valueInput = new SpinButton(-1e6, 1e6, 1);
            grid.Attach(valueInput, 1, 1, 1, 1);

            grid.Attach(new Label("Variable:"), 0, 2, 1, 1);
            var varCombo = new ComboBoxText();
            varCombo.AppendText("Temperature");
            varCombo.AppendText("Pressure");
            varCombo.Active = 0;
            grid.Attach(varCombo, 1, 2, 1, 1);

            content.PackStart(grid, true, true, 0);
            ContentArea.PackStart(content, true, true, 0);

            AddButton("Cancel", ResponseType.Cancel);
            AddButton("Apply", ResponseType.Ok);

            ShowAll();
        }
    }
}
