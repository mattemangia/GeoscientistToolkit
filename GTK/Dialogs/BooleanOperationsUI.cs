using System;
using Gtk;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class BooleanOperationsUI : Dialog
    {
        public BooleanOperationsUI(Window parent) : base("Boolean Operations", parent, DialogFlags.Modal)
        {
            SetDefaultSize(400, 300);
            BorderWidth = 8;

            var box = new VBox(false, 8);
            box.PackStart(new Label("Select Operation") { Xalign = 0 }, false, false, 0);

            var opCombo = new ComboBoxText();
            opCombo.AppendText("Union");
            opCombo.AppendText("Difference");
            opCombo.AppendText("Intersection");
            opCombo.Active = 0;
            box.PackStart(opCombo, false, false, 0);

            box.PackStart(new Label("Target Mesh:"), false, false, 0);
            var targetEntry = new Entry { PlaceholderText = "Select target mesh..." };
            box.PackStart(targetEntry, false, false, 0);

            box.PackStart(new Label("Tool Mesh:"), false, false, 0);
            var toolEntry = new Entry { PlaceholderText = "Select tool mesh..." };
            box.PackStart(toolEntry, false, false, 0);

            ContentArea.PackStart(box, true, true, 0);

            AddButton("Cancel", ResponseType.Cancel);
            AddButton("Execute", ResponseType.Ok);

            ShowAll();
        }
    }
}
