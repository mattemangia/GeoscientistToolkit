// GeoscientistToolkit/Data/CtImageStack/CtImageStackViewer.cs
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackViewer : IDatasetViewer
    {
        private int _selectedView; // 0 = 3D, 1 = X, 2 = Y, 3 = Z, 4 = Quad layout

        public void DrawToolbarControls()
        {
            string[] viewNames = { "3D", "X", "Y", "Z", "All" };
            for (int i = 0; i < viewNames.Length; ++i)
            {
                if (i > 0) ImGui.SameLine();

                bool selected = _selectedView == i;
                if (selected)
                    ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);

                if (ImGui.Button(viewNames[i], new Vector2(40, 0)))
                    _selectedView = i;

                if (selected)
                    ImGui.PopStyleColor();
            }

            ImGui.SameLine();
            ImGui.TextUnformatted("|");
            ImGui.SameLine();
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            if (_selectedView == 4)
            {
                SubmitCtQuadViewport(ref zoom, ref pan);
            }
            else
            {
                SubmitSingleViewport(ref zoom, ref pan);
            }
        }
        
        private void SubmitSingleViewport(ref float zoom, ref Vector2 pan)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            if (ImGui.BeginChild("Viewport", ImGui.GetContentRegionAvail(), ImGuiChildFlags.Border, ImGuiWindowFlags.NoScrollbar))
            {
                 // Placeholder drawing logic
                ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + ImGui.GetContentRegionAvail(), 0xFF302020);
                ImGui.Text("  Single Viewport (CT)");
            }
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }

        private void SubmitCtQuadViewport(ref float zoom, ref Vector2 pan)
        {
            // Placeholder drawing logic
            ImGui.Text("Quad Viewport (CT)");
        }

        /// <summary>
        /// Fulfills the IDisposable contract from the interface. No resources to clean up yet.
        /// </summary>
        public void Dispose()
        {
            // Nothing to dispose of yet for this viewer.
        }
    }
}