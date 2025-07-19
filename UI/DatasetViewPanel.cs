// GeoscientistToolkit/UI/DatasetViewPanel.cs (Updated to inherit from BasePanel)
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
    public class DatasetViewPanel : BasePanel
    {
        public Dataset Dataset { get; }
        private readonly IDatasetViewer _viewer;
        
        private float _zoom = 1.0f;
        private Vector2 _pan = Vector2.Zero;

        public DatasetViewPanel(Dataset dataset) : base(dataset.Name, new Vector2(800, 600))
        {
            Dataset = dataset;
            _viewer = DatasetUIFactory.CreateViewer(dataset);
        }
        
        public void Submit(ref bool pOpen)
        {
            base.Submit(ref pOpen);
        }
        
        protected override void DrawContent()
        {
            DrawToolbar();
            ImGui.Separator();
            
            _viewer.DrawContent(ref _zoom, ref _pan);
            
            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            _viewer.DrawToolbarControls();

            if (ImGui.Button("-", new Vector2(25, 0))) _zoom = Math.Max(0.1f, _zoom - 0.1f);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.SliderFloat("##zoom", ref _zoom, 0.1f, 5.0f, "%.1fx");
            ImGui.SameLine();
            if (ImGui.Button("+", new Vector2(25, 0))) _zoom = Math.Min(5.0f, _zoom + 0.1f);
            ImGui.SameLine();
            if (ImGui.Button("Fit", new Vector2(40, 0)))
            {
                _zoom = 1.0f;
                _pan = Vector2.Zero;
            }
        }

        private void DrawStatusBar()
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Dataset: {Dataset.Name} | Type: {Dataset.Type} | Zoom: {_zoom:F1}×");

            if (Dataset is CtImageStackDataset ct && ct.Width > 0)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"| Size: {ct.Width}×{ct.Height}×{ct.Depth}");
            }
        }

        public override void Dispose()
        {
            _viewer?.Dispose();
            base.Dispose();
        }
    }
}