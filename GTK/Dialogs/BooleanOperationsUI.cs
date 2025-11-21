using System;
using System.Collections.Generic;
using System.Linq;
using Gtk;
using GeoscientistToolkit.Data.Mesh3D;

namespace GeoscientistToolkit.GtkUI.Dialogs
{
    public class BooleanOperationsUI : Dialog
    {
        public Mesh3DDataset? TargetMesh { get; private set; }
        public Mesh3DDataset? ToolMesh { get; private set; }
        public int OperationIndex => _opCombo.Active;

        private readonly ComboBoxText _targetCombo;
        private readonly ComboBoxText _toolCombo;
        private readonly ComboBoxText _opCombo;
        private readonly List<Mesh3DDataset> _meshes;

        public BooleanOperationsUI(Window parent, List<Mesh3DDataset> meshes) : base("Boolean Operations", parent, DialogFlags.Modal)
        {
            _meshes = meshes;
            SetDefaultSize(400, 300);
            BorderWidth = 8;

            var box = new VBox(false, 8);
            box.PackStart(new Label("Select Operation") { Xalign = 0 }, false, false, 0);

            _opCombo = new ComboBoxText();
            _opCombo.AppendText("Union");
            _opCombo.AppendText("Subtract (Target - Tool)");
            _opCombo.Active = 0;
            box.PackStart(_opCombo, false, false, 0);

            box.PackStart(new Label("Target Mesh:"), false, false, 0);
            _targetCombo = new ComboBoxText();
            foreach (var m in _meshes) _targetCombo.AppendText(m.Name);
            if (_meshes.Count > 0) _targetCombo.Active = 0;
            box.PackStart(_targetCombo, false, false, 0);

            box.PackStart(new Label("Tool Mesh:"), false, false, 0);
            _toolCombo = new ComboBoxText();
            foreach (var m in _meshes) _toolCombo.AppendText(m.Name);
            if (_meshes.Count > 1) _toolCombo.Active = 1;
            else if (_meshes.Count > 0) _toolCombo.Active = 0;
            box.PackStart(_toolCombo, false, false, 0);

            ContentArea.PackStart(box, true, true, 0);

            AddButton("Cancel", ResponseType.Cancel);
            AddButton("Execute", ResponseType.Ok);

            ShowAll();
        }

        protected override void OnResponse(ResponseType response_id)
        {
            if (response_id == ResponseType.Ok)
            {
                if (_targetCombo.Active != -1) TargetMesh = _meshes[_targetCombo.Active];
                if (_toolCombo.Active != -1) ToolMesh = _meshes[_toolCombo.Active];
            }
            base.OnResponse(response_id);
        }
    }
}
