// GeoscientistToolkit/UI/DatasetViewPanel.cs
// This class represents a detachable panel for viewing a single dataset.
// For complex datasets like CT scans, it can contain multiple viewports (e.g., 3D, X, Y, Z).

using GeoscientistToolkit.Data;
using ImGuiNET;

namespace GeoscientistToolkit.UI
{
    public class DatasetViewPanel
    {
        public Dataset Dataset { get; }

        public DatasetViewPanel(Dataset dataset)
        {
            Dataset = dataset;
        }

        public void Submit(ref bool pOpen)
        {
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(400, 300), ImGuiCond.FirstUseEver);
            if (ImGui.Begin(Dataset.Name, ref pOpen, ImGuiWindowFlags.NoCollapse))
            {
                if (ImGui.BeginPopupContextItem())
                {
                    if (ImGui.MenuItem("Close")) { pOpen = false; }
                    ImGui.EndPopup();
                }

                // For CT datasets, we'd implement the 2x2 viewport layout here.
                if (Dataset.Type == DatasetType.CtImageStack)
                {
                    SubmitCtViewport();
                }
                else
                {
                    ImGui.Text($"This is the view for {Dataset.Name}");
                    ImGui.Text("Rendering for this dataset type is not yet implemented.");
                }
            }
            ImGui.End();
        }

        private void SubmitCtViewport()
        {
            // This is a placeholder for the complex 4-viewport rendering.
            // A real implementation would use ImGui's child windows or tables for layout
            // and a 3D rendering library to draw in them.
            ImGui.Text("CT Viewport for: " + Dataset.Name);

            var contentRegion = ImGui.GetContentRegionAvail();
            var halfWidth = contentRegion.X / 2.0f - 5;
            var halfHeight = contentRegion.Y / 2.0f - 5;
            
            if (ImGui.BeginTable("ct_viewports", 2))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Button("3D View", new System.Numerics.Vector2(halfWidth, halfHeight));

                ImGui.TableNextColumn();
                ImGui.Button("X-Axis View", new System.Numerics.Vector2(halfWidth, halfHeight));

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Button("Y-Axis View", new System.Numerics.Vector2(halfWidth, halfHeight));

                ImGui.TableNextColumn();
                ImGui.Button("Z-Axis View", new System.Numerics.Vector2(halfWidth, halfHeight));

                ImGui.EndTable();
            }
        }
    }
}