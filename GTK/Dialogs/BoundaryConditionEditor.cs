using System;
using System.Collections.Generic;
using Gtk;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class BoundaryConditionEditor : Dialog
    {
        public BoundaryCondition CreatedBC { get; private set; }

        private ComboBoxText _typeCombo;
        private SpinButton _valueInput;
        private ComboBoxText _varCombo;
        private ComboBoxText _locationCombo;

        public BoundaryConditionEditor(Window parent) : base("Boundary Condition Editor", parent, DialogFlags.Modal)
        {
            SetDefaultSize(500, 400);
            BorderWidth = 8;

            var content = new VBox(false, 6);
            content.PackStart(new Label("Create New Boundary Condition") { Xalign = 0 }, false, false, 0);

            var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10, BorderWidth = 10 };

            grid.Attach(new Label("Type:"), 0, 0, 1, 1);
            _typeCombo = new ComboBoxText();
            foreach (var t in Enum.GetNames(typeof(BoundaryType))) _typeCombo.AppendText(t);
            _typeCombo.Active = 0;
            grid.Attach(_typeCombo, 1, 0, 1, 1);

            grid.Attach(new Label("Location:"), 0, 1, 1, 1);
            _locationCombo = new ComboBoxText();
            foreach (var l in Enum.GetNames(typeof(BoundaryLocation))) _locationCombo.AppendText(l);
            _locationCombo.Active = 0;
            grid.Attach(_locationCombo, 1, 1, 1, 1);

            grid.Attach(new Label("Variable:"), 0, 2, 1, 1);
            _varCombo = new ComboBoxText();
            foreach (var v in Enum.GetNames(typeof(BoundaryVariable))) _varCombo.AppendText(v);
            _varCombo.Active = 0;
            grid.Attach(_varCombo, 1, 2, 1, 1);

            grid.Attach(new Label("Value:"), 0, 3, 1, 1);
            _valueInput = new SpinButton(-1e6, 1e6, 1);
            grid.Attach(_valueInput, 1, 3, 1, 1);

            content.PackStart(grid, true, true, 0);
            ContentArea.PackStart(content, true, true, 0);

            AddButton("Cancel", ResponseType.Cancel);
            AddButton("Apply", ResponseType.Ok);

            ShowAll();
        }

        protected override void OnResponse(ResponseType response_id)
        {
            if (response_id == ResponseType.Ok)
            {
                CreatedBC = new BoundaryCondition
                {
                    Name = $"BC_{DateTime.Now.Ticks}",
                    Type = Enum.Parse<BoundaryType>(_typeCombo.ActiveText),
                    Location = Enum.Parse<BoundaryLocation>(_locationCombo.ActiveText),
                    Variable = Enum.Parse<BoundaryVariable>(_varCombo.ActiveText),
                    Value = _valueInput.Value
                };
            }
            base.OnResponse(response_id);
        }
    }
}
