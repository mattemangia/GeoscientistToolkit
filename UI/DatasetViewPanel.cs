// GeoscientistToolkit/UI/DatasetViewPanel.cs
// This panel is the generic window container for any dataset viewer.
// It implements IDisposable to ensure its viewer's resources are cleaned up.

using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
    public class DatasetViewPanel : IDisposable
    {
        public Dataset Dataset { get; }
        private readonly IDatasetViewer _viewer;

        private bool _isPoppedOut;
        private float _zoom = 1.0f;
        private Vector2 _pan = Vector2.Zero;

        public DatasetViewPanel(Dataset dataset)
        {
            Dataset = dataset;
            // The factory creates the correct viewer (e.g., ImageViewer) for the dataset.
            _viewer = DatasetUIFactory.CreateViewer(dataset);
        }

        /// <summary>
        /// Draws the panel's UI for the current frame.
        /// </summary>
        public void Submit(ref bool pOpen)
        {
            ImGui.SetNextWindowSize(new Vector2(800, 600), ImGuiCond.FirstUseEver);

            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoCollapse;
            if (_isPoppedOut)
                windowFlags |= ImGuiWindowFlags.NoDocking;

            // Use the dataset's name for the window title.
            if (!ImGui.Begin(Dataset.Name, ref pOpen, windowFlags))
            {
                ImGui.End();
                return;
            }

            DrawPopOutButton();
            DrawToolbar();
            ImGui.Separator();

            // The main content rendering is delegated to the specific viewer instance.
            _viewer.DrawContent(ref _zoom, ref _pan);

            DrawStatusBar();
            ImGui.End();
        }

        /// <summary>
        /// Disposes of the underlying viewer's resources (e.g., Veldrid textures).
        /// </summary>
        public void Dispose()
        {
            // The null-conditional operator ?. ensures no error occurs if _viewer is null.
            _viewer?.Dispose();
        }

        #region UI Drawing Helpers

        private void DrawToolbar()
        {
            // Delegate viewer-specific controls (e.g., 3D/X/Y/Z buttons for CT scans).
            _viewer.DrawToolbarControls();

            // Generic zoom and pan controls, common to all viewers.
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

        private void DrawPopOutButton()
        {
            const float padding = 5.0f;
            var buttonSize = new Vector2(20, 20);
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            var buttonPos = new Vector2(windowPos.X + windowSize.X - buttonSize.X - padding - 25, windowPos.Y + padding);
            
            var originalCursor = ImGui.GetCursorPos();
            ImGui.SetCursorScreenPos(buttonPos);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0.30f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.59f, 0.98f, 0.50f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.26f, 0.59f, 0.98f, 0.70f));

            if (ImGui.Button(_isPoppedOut ? "<<" : "[]", buttonSize))
            {
                _isPoppedOut = !_isPoppedOut;
                if (_isPoppedOut)
                {
                    var vp = ImGui.GetMainViewport();
                    ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
                    ImGui.SetNextWindowSize(new Vector2(1024, 768));
                }
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_isPoppedOut ? "Dock window" : "Pop-out window");
            }

            ImGui.PopStyleColor(3);
            ImGui.SetCursorPos(originalCursor);
        }

        #endregion
    }
}