// GeoscientistToolkit/Data/CtImageStack/CtImageStackViewer.cs
// Viewer for CT image stacks with similar scale bar functionality

using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.Data.CtImageStack
{
    public class CtImageStackViewer : IDatasetViewer
    {
        private int _currentSlice = 0;
        private int _viewMode = 0; // 0=Axial, 1=Coronal, 2=Sagittal
        
        public void DrawToolbarControls()
        {
            // View mode selection
            string[] modes = { "Axial", "Coronal", "Sagittal" };
            ImGui.SetNextItemWidth(100);
            ImGui.Combo("##ViewMode", ref _viewMode, modes, modes.Length);
            
            ImGui.SameLine();
            ImGui.Text($"Slice: {_currentSlice}");
        }

        public void DrawContent(ref float zoom, ref Vector2 pan)
        {
            var io = ImGui.GetIO();
            var canvasPos = ImGui.GetCursorScreenPos();
            var canvasSize = ImGui.GetContentRegionAvail();
            var dl = ImGui.GetWindowDrawList();

            // Create invisible button for mouse interaction
            ImGui.InvisibleButton("ct_canvas", canvasSize);
            bool isHovered = ImGui.IsItemHovered();

            // Handle mouse wheel zoom
            if (isHovered && io.MouseWheel != 0)
            {
                float zoomDelta = io.MouseWheel * 0.1f;
                float newZoom = Math.Clamp(zoom + zoomDelta * zoom, 0.1f, 10.0f);
                
                // Zoom towards mouse position
                if (newZoom != zoom)
                {
                    Vector2 mousePos = io.MousePos - canvasPos - canvasSize * 0.5f;
                    pan -= mousePos * (newZoom / zoom - 1.0f);
                    zoom = newZoom;
                }
            }

            // Handle slice scrolling with Ctrl+MouseWheel
            if (isHovered && io.MouseWheel != 0 && io.KeyCtrl)
            {
                _currentSlice += (int)io.MouseWheel;
                _currentSlice = Math.Max(0, _currentSlice);
            }

            // Handle panning with middle mouse button
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Middle))
            {
                pan += io.MouseDelta;
            }

            // Draw background
            dl.AddRectFilled(canvasPos, canvasPos + canvasSize, 0xFF202020);
            
            // Placeholder for CT rendering
            string text = "CT Stack Viewer\n(Not yet implemented)\n\nUse mouse wheel to zoom\nCtrl+wheel to change slice\nMiddle button to pan";
            var textSize = ImGui.CalcTextSize(text);
            var textPos = canvasPos + (canvasSize - textSize) * 0.5f;
            dl.AddText(textPos, 0xFFFFFFFF, text);
        }

        public void Dispose()
        {
            // Clean up any resources when the viewer is closed
        }
    }
}