using System;
using System.Linq;
using System.Collections.Generic;
using Gtk;
using GeoscientistToolkit.Data.PhysicoChem;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class HeatExchangerConfigDialog : Dialog
    {
        public ReactorObject? CreatedObject { get; private set; }

        private readonly Entry _nameEntry;
        private readonly ComboBoxText _typeSelector;
        private readonly ComboBoxText _materialSelector;
        private readonly SpinButton _posX, _posY, _posZ;
        private readonly SpinButton _sizeX, _sizeY, _sizeZ; // Or Radius/Height
        private readonly CheckButton _isCylinderCheck;

        private readonly List<MaterialProperties> _availableMaterials;

        public HeatExchangerConfigDialog(Window parent, List<MaterialProperties> materials)
            : base("Configure Heat Exchanger / Object", parent, DialogFlags.Modal)
        {
            _availableMaterials = materials;
            SetDefaultSize(400, 500);
            BorderWidth = 8;

            var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8, BorderWidth = 10 };

            // Name
            grid.Attach(new Label("Name:"), 0, 0, 1, 1);
            _nameEntry = new Entry("Heat Exchanger 1");
            grid.Attach(_nameEntry, 1, 0, 1, 1);

            // Type
            grid.Attach(new Label("Type:"), 0, 1, 1, 1);
            _typeSelector = new ComboBoxText();
            _typeSelector.AppendText("HeatExchanger");
            _typeSelector.AppendText("Baffle");
            _typeSelector.AppendText("Obstacle");
            _typeSelector.Active = 0;
            grid.Attach(_typeSelector, 1, 1, 1, 1);

            // Material
            grid.Attach(new Label("Material:"), 0, 2, 1, 1);
            _materialSelector = new ComboBoxText();
            foreach (var mat in _availableMaterials)
            {
                _materialSelector.AppendText(mat.MaterialID);
            }
            if (_availableMaterials.Count > 0) _materialSelector.Active = 0;
            grid.Attach(_materialSelector, 1, 2, 1, 1);

            // Geometry
            grid.Attach(new Separator(Orientation.Horizontal), 0, 3, 2, 1);
            grid.Attach(new Label("Geometry"), 0, 4, 2, 1);

            _isCylinderCheck = new CheckButton("Is Cylinder?");
            _isCylinderCheck.Toggled += UpdateGeometryInputs;
            grid.Attach(_isCylinderCheck, 1, 4, 1, 1);

            // Position
            grid.Attach(new Label("Position (X, Y, Z):"), 0, 5, 1, 1);
            _posX = new SpinButton(-1000, 1000, 0.1);
            _posY = new SpinButton(-1000, 1000, 0.1);
            _posZ = new SpinButton(-1000, 1000, 0.1);
            var posBox = new HBox(false, 2);
            posBox.PackStart(_posX, true, true, 0);
            posBox.PackStart(_posY, true, true, 0);
            posBox.PackStart(_posZ, true, true, 0);
            grid.Attach(posBox, 1, 5, 1, 1);

            // Dimensions
            grid.Attach(new Label("Dimensions:"), 0, 6, 1, 1);
            _sizeX = new SpinButton(0.1, 1000, 0.1) { Value = 1.0 };
            _sizeY = new SpinButton(0.1, 1000, 0.1) { Value = 1.0 };
            _sizeZ = new SpinButton(0.1, 1000, 0.1) { Value = 1.0 };

            var sizeBox = new HBox(false, 2);
            sizeBox.PackStart(_sizeX, true, true, 0);
            sizeBox.PackStart(_sizeY, true, true, 0);
            sizeBox.PackStart(_sizeZ, true, true, 0);
            grid.Attach(sizeBox, 1, 6, 1, 1);

            ContentArea.PackStart(grid, true, true, 0);

            AddButton("Cancel", ResponseType.Cancel);
            AddButton("Create", ResponseType.Ok);

            ShowAll();
            UpdateGeometryInputs(this, EventArgs.Empty);
        }

        private void UpdateGeometryInputs(object? sender, EventArgs e)
        {
            if (_isCylinderCheck.Active)
            {
                _sizeX.TooltipText = "Radius";
                _sizeY.Sensitive = false; // Only Radius needed
                _sizeZ.TooltipText = "Height";
            }
            else
            {
                _sizeX.TooltipText = "Size X";
                _sizeY.Sensitive = true;
                _sizeZ.TooltipText = "Size Z";
            }
        }

        protected override void OnResponse(ResponseType response_id)
        {
            if (response_id == ResponseType.Ok)
            {
                CreatedObject = new ReactorObject
                {
                    Name = _nameEntry.Text,
                    Type = _typeSelector.ActiveText,
                    MaterialID = _materialSelector.ActiveText ?? "Default",
                    Center = (_posX.Value, _posY.Value, _posZ.Value),
                    IsCylinder = _isCylinderCheck.Active
                };

                if (CreatedObject.IsCylinder)
                {
                    CreatedObject.Radius = _sizeX.Value;
                    CreatedObject.Height = _sizeZ.Value;
                }
                else
                {
                    CreatedObject.Size = (_sizeX.Value, _sizeY.Value, _sizeZ.Value);
                }
            }
            base.OnResponse(response_id);
        }
    }
}
