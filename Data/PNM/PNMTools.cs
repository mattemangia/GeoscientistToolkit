// GeoscientistToolkit/UI/PNMTools.cs
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using System.Numerics;

namespace GeoscientistToolkit.UI
{
    public class PNMTools : IDatasetTools
    {
        private readonly ImGuiExportFileDialog _exportDialog;

        public PNMTools()
        {
            _exportDialog = new ImGuiExportFileDialog("ExportPNMDialog", "Export PNM");
            _exportDialog.SetExtensions((".pnm.json", "PNM JSON File"));
        }

        public void Draw(Dataset dataset)
        {
            if (dataset is not PNMDataset pnm) return;

            ImGui.Text("PNM Tools");
            ImGui.Separator();

            if (ImGui.CollapsingHeader("Analysis & Calculation", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.TextWrapped("Tools for calculating permeability and other network properties from a segmented CT volume will be available here in a future update.");
                ImGui.BeginDisabled();
                ImGui.Button("Calculate Permeability...", new Vector2(-1, 0));
                ImGui.EndDisabled();
            }

            ImGui.Spacing();

            if (ImGui.CollapsingHeader("Export", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Text("Export the current pore network model to a file.");
                if (ImGui.Button("Export as JSON...", new Vector2(-1, 0)))
                {
                    _exportDialog.Open(pnm.Name);
                }
            }

            // Handle the export dialog
            if (_exportDialog.Submit())
            {
                try
                {
                    pnm.ExportToJson(_exportDialog.SelectedPath);
                    Logger.Log($"[PNMTools] Successfully exported PNM dataset to '{_exportDialog.SelectedPath}'");
                }
                catch (System.Exception ex)
                {
                    Logger.LogError($"[PNMTools] Failed to export PNM dataset: {ex.Message}");
                }
            }
        }
    }
}