// GeoscientistToolkit/UI/PropertiesPanel.cs (Updated to inherit from BasePanel and handle image export)
using GeoscientistToolkit.Data;
using GeoscientistToolkit.UI.Interfaces;
using ImGuiNET;
using System.Numerics;
using GeoscientistToolkit.Data.Image; // Added for ImageDataset and ImageExportDialog
using GeoscientistToolkit.Util; // Added for Logger

namespace GeoscientistToolkit.UI
{
    public class PropertiesPanel : BasePanel
    {
        private Dataset _selectedDataset;
        private readonly ImageExportDialog _imageExportDialog; // Added to handle image export

        public PropertiesPanel() : base("Properties", new Vector2(300, 400))
        {
            _imageExportDialog = new ImageExportDialog(); // Initialize the dialog
        }

        public void Submit(ref bool pOpen, Dataset selectedDataset)
        {
            _selectedDataset = selectedDataset;
            base.Submit(ref pOpen);
            
            // Submit the dialog every frame. It will only draw when it's open.
            _imageExportDialog.Submit();
        }

        protected override void DrawContent()
        {
            if (_selectedDataset != null)
            {
                // --- Header ---
                ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]);
                ImGui.Text(_selectedDataset.Name);
                ImGui.PopFont();
                ImGui.Separator();

                // --- General Properties ---
                if (ImGui.CollapsingHeader("General", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();
                    DrawProperty("Type", _selectedDataset.Type.ToString());
                    DrawProperty("Path", _selectedDataset.FilePath, true);
                    DrawProperty("Created", _selectedDataset.DateCreated.ToString("g"));
                    DrawProperty("Modified", _selectedDataset.DateModified.ToString("g"));
                    DrawProperty("Size", FormatFileSize(_selectedDataset.GetSizeInBytes()));
                    ImGui.Unindent();
                }

                // --- Type-Specific Properties ---
                IDatasetPropertiesRenderer propertiesRenderer = DatasetUIFactory.CreatePropertiesRenderer(_selectedDataset);
                propertiesRenderer.Draw(_selectedDataset);

                // --- Actions ---
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("Reload", new Vector2(-1, 0)))
                {
                    _selectedDataset.Unload();
                    _selectedDataset.Load();
                }

                if (ImGui.Button("Export...", new Vector2(-1, 0)))
                {
                    // FIX: Implemented export functionality for ImageDataset
                    if (_selectedDataset is ImageDataset imageDataset)
                    {
                        _imageExportDialog.Open(imageDataset);
                    }
                    else
                    {
                        // Optionally, handle cases where the dataset type is not exportable from this panel.
                        Logger.Log($"Export from this panel is not implemented for dataset type: {_selectedDataset.Type}");
                    }
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
        }

        #region Public Static Helpers

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

        public static string FormatNumber(long number)
        {
            return number.ToString("N0");
        }

        #endregion
    }
}