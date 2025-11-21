using System;
using System.Linq;
using System.Collections.Generic;
using Gtk;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class ForceFieldEditor : Dialog
    {
        private readonly List<ForceField> _forces;
        private readonly ListStore _store;
        private readonly TreeView _treeView;
        private readonly ComboBoxText _typeCombo;
        private readonly Entry _nameEntry;

        public ForceFieldEditor(Window parent, List<ForceField> forces) : base("Force Field Editor", parent, DialogFlags.Modal)
        {
            _forces = forces;
            SetDefaultSize(600, 400);
            BorderWidth = 8;

            var mainBox = new HBox(false, 8);

            // Left: List
            var leftBox = new VBox(false, 4);
            _store = new ListStore(typeof(string), typeof(string), typeof(ForceField));
            _treeView = new TreeView(_store);
            _treeView.AppendColumn("Name", new CellRendererText(), "text", 0);
            _treeView.AppendColumn("Type", new CellRendererText(), "text", 1);
            _treeView.Selection.Changed += OnSelectionChanged;

            var scroller = new ScrolledWindow();
            scroller.Add(_treeView);
            leftBox.PackStart(scroller, true, true, 0);

            var btnBox = new HBox(false, 4);
            var addBtn = new Button("Add");
            addBtn.Clicked += OnAdd;
            var removeBtn = new Button("Remove");
            removeBtn.Clicked += OnRemove;
            btnBox.PackStart(addBtn, true, true, 0);
            btnBox.PackStart(removeBtn, true, true, 0);
            leftBox.PackStart(btnBox, false, false, 0);

            mainBox.PackStart(leftBox, true, true, 0);

            // Right: Details (Simplified)
            var rightBox = new VBox(false, 4);

            var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            grid.Attach(new Label("Name:") { Xalign = 0 }, 0, 0, 1, 1);
            _nameEntry = new Entry();
            grid.Attach(_nameEntry, 1, 0, 1, 1);

            grid.Attach(new Label("Type:") { Xalign = 0 }, 0, 1, 1, 1);
            _typeCombo = new ComboBoxText();
            foreach (var t in Enum.GetNames(typeof(ForceType)))
                _typeCombo.AppendText(t);
            grid.Attach(_typeCombo, 1, 1, 1, 1);

            // Note: Full property editing for all Force types is complex.
            // We stick to basic ID/Type for this editor in this iteration.
            rightBox.PackStart(grid, false, false, 0);

            var saveBtn = new Button("Update Selected");
            saveBtn.Clicked += OnUpdate;
            rightBox.PackStart(saveBtn, false, false, 0);

            mainBox.PackStart(rightBox, true, true, 0);

            ContentArea.PackStart(mainBox, true, true, 0);
            AddButton("Close", ResponseType.Close);

            RefreshList();
            ShowAll();
        }

        private void RefreshList()
        {
            _store.Clear();
            foreach (var f in _forces)
            {
                _store.AppendValues(f.Name, f.Type.ToString(), f);
            }
        }

        private void OnAdd(object? sender, EventArgs e)
        {
            var f = new ForceField("New Force", ForceType.Gravity);
            _forces.Add(f);
            RefreshList();
        }

        private void OnRemove(object? sender, EventArgs e)
        {
            if (_treeView.Selection.GetSelected(out TreeIter iter))
            {
                var f = (ForceField)_store.GetValue(iter, 2);
                _forces.Remove(f);
                RefreshList();
            }
        }

        private void OnSelectionChanged(object? sender, EventArgs e)
        {
            if (_treeView.Selection.GetSelected(out TreeIter iter))
            {
                var f = (ForceField)_store.GetValue(iter, 2);
                _nameEntry.Text = f.Name;

                // Find type index
                int i = 0;
                foreach (var t in Enum.GetNames(typeof(ForceType)))
                {
                    if (t == f.Type.ToString())
                    {
                        _typeCombo.Active = i;
                        break;
                    }
                    i++;
                }
            }
        }

        private void OnUpdate(object? sender, EventArgs e)
        {
            if (_treeView.Selection.GetSelected(out TreeIter iter))
            {
                var f = (ForceField)_store.GetValue(iter, 2);
                f.Name = _nameEntry.Text;
                if (Enum.TryParse<ForceType>(_typeCombo.ActiveText, out var type))
                {
                    f.Type = type;
                }
                RefreshList();
            }
        }
    }
}
