// GeoscientistToolkit/UI/PropertiesPanel.cs
// Renders the properties panel. This panel is generic and delegates the drawing
// of type-specific properties to a dedicated renderer class obtained from a factory.

using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
    public class PropertiesPanel
    {
        private bool _isPoppedOut;

        /// <summary>
        /// Submits the UI for the Properties panel for the current frame.
        /// </summary>
        /// <param name="pOpen">A reference to a boolean that controls the panel's visibility.</param>
        /// <param name="selectedDataset">The currently selected dataset, or null if none is selected.</param>
        public void Submit(ref bool pOpen, Dataset selectedDataset)
        {
            ImGui.SetNextWindowSize(new Vector2(300, 400), ImGuiCond.FirstUseEver);

            // Allow the window to be moved outside the main viewport when popped out.
            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.None;
            if (_isPoppedOut)
            {
                windowFlags |= ImGuiWindowFlags.NoDocking;
            }

            if (!ImGui.Begin("Properties", ref pOpen, windowFlags))
            {
                ImGui.End();
                return;
            }

            DrawPopOutButton();

            if (selectedDataset != null)
            {
                // --- Header ---
                ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]);
                ImGui.Text(selectedDataset.Name);
                ImGui.PopFont();
                ImGui.Separator();

                // --- General Properties (Common to all datasets) ---
                if (ImGui.CollapsingHeader("General", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    DrawProperty("Type", selectedDataset.Type.ToString());
                    DrawProperty("Path", selectedDataset.FilePath, true);
                    DrawProperty("Created", selectedDataset.DateCreated.ToString("g"));
                    DrawProperty("Modified", selectedDataset.DateModified.ToString("g"));
                    DrawProperty("Size", FormatFileSize(selectedDataset.GetSizeInBytes()));
                    ImGui.Unindent();
                }

                // --- Type-Specific Properties (Delegated to a renderer) ---
                IDatasetPropertiesRenderer propertiesRenderer = DatasetUIFactory.CreatePropertiesRenderer(selectedDataset);
                propertiesRenderer.Draw(selectedDataset);

                // --- Actions ---
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("Reload", new Vector2(-1, 0)))
                {
                    selectedDataset.Unload();
                    selectedDataset.Load();
                }

                if (ImGui.Button("Export...", new Vector2(-1, 0)))
                {
                    // TODO: Implement export functionality.
                }
            }
            else
            {
                // --- No Dataset Selected State ---
                var windowSize = ImGui.GetWindowSize();
                var text = "No dataset selected";
                var textSize = ImGui.CalcTextSize(text);
                ImGui.SetCursorPos(new Vector2((windowSize.X - textSize.X) * 0.5f, (windowSize.Y - textSize.Y) * 0.5f));
                ImGui.TextDisabled(text);
            }

            ImGui.End();
        }

        private void DrawPopOutButton()
        {
            const float padding = 5.0f;
            var buttonSize = new Vector2(20, 20);

            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            // Position button in the title bar area, leaving space for the standard close button.
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
                    // When popping out, set a default position outside the main viewport.
                    var mainVp = ImGui.GetMainViewport();
                    ImGui.SetNextWindowPos(new Vector2(mainVp.Pos.X + mainVp.Size.X + 10, mainVp.Pos.Y + 100));
                }
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(_isPoppedOut ? "Dock window" : "Pop out to a separate window");
            }

            ImGui.PopStyleColor(3);
            ImGui.SetCursorPos(originalCursor);
        }

        #region Public Static Helpers

        /// <summary>
        /// A standardized helper to draw a label-value pair. Can be used by any property renderer.
        /// </summary>
        public static void DrawProperty(string label, string value, bool isSelectable = false)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1.0f));
            ImGui.TextUnformatted($"{label}:");
            ImGui.PopStyleColor();
            ImGui.SameLine();

            if (isSelectable)
            {
                ImGui.PushItemWidth(-1);
                ImGui.InputText($"##{label}", ref value, (uint)value.Length + 1, ImGuiInputTextFlags.ReadOnly);
                ImGui.PopItemWidth();
            }
            else
            {
                ImGui.TextUnformatted(value);
            }
        }

        /// <summary>
        /// Formats a file size in bytes into a human-readable string (KB, MB, GB).
        /// </summary>
        public static string FormatFileSize(long bytes)
        {
            if (bytes < 0) return "N/A";
            if (bytes == 0) return "0 B";
            
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Formats a number with thousands separators.
        /// </summary>
        public static string FormatNumber(long number)
        {
            return number.ToString("N0");
        }

        #endregion
    }
}