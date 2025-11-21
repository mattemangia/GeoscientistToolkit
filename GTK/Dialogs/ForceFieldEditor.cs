using System;
using Gtk;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class ForceFieldEditor : Dialog
    {
        public ForceFieldEditor(Window parent) : base("Force Field Editor", parent, DialogFlags.Modal)
        {
            SetDefaultSize(500, 400);
            BorderWidth = 8;

            var content = new VBox(false, 6);
            content.PackStart(new Label("Edit Force Fields") { Xalign = 0 }, false, false, 0);

            // Placeholder for force field list and editor
            var tree = new TreeView();
            content.PackStart(tree, true, true, 0);

            ContentArea.PackStart(content, true, true, 0);

            AddButton("Close", ResponseType.Close);

            ShowAll();
        }
    }
}
