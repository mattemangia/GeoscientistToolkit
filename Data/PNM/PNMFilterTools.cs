// GeoscientistToolkit/UI/PNMFilterTools.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public sealed class PNMFilterTools : IDatasetTools
{
    private readonly ImGuiExportFileDialog _exportPoresCsvDialog;
    private readonly ImGuiExportFileDialog _exportThroatsCsvDialog;
    private float _minArea, _maxArea;
    private int _minConn, _maxConn;
    private float _minPoreRad, _maxPoreRad;
    private float _minThroatRad, _maxThroatRad;
    private float _minVolPhys, _maxVolPhys;
    private float _minVolVox, _maxVolVox;

    // UI state
    private bool _usePhysicalUnits = true; // µm-based sliders

    public PNMFilterTools()
    {
        _exportPoresCsvDialog = new ImGuiExportFileDialog("ExportPoresCsvDialog", "Export Pores CSV");
        _exportPoresCsvDialog.SetExtensions((".csv", "CSV (Comma-separated values)"));

        _exportThroatsCsvDialog = new ImGuiExportFileDialog("ExportThroatsCsvDialog", "Export Throats CSV");
        _exportThroatsCsvDialog.SetExtensions((".csv", "CSV (Comma-separated values)"));
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not PNMDataset pnm)
        {
            ImGui.TextDisabled("PNM Filter Tools require a PNM dataset.");
            return;
        }

        ImGui.Text("PNM Filter & Export");
        ImGui.Separator();

        DrawFilterSection(pnm);
        ImGui.Separator();
        DrawExportSection(pnm);

        // Handle CSV dialogs
        if (_exportPoresCsvDialog.Submit())
            try
            {
                pnm.ExportPoresCsv(_exportPoresCsvDialog.SelectedPath);
                Logger.Log($"[PNMFilterTools] Exported pores CSV to '{_exportPoresCsvDialog.SelectedPath}'");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PNMFilterTools] Failed to export pores CSV: {ex.Message}");
            }

        if (_exportThroatsCsvDialog.Submit())
            try
            {
                pnm.ExportThroatsCsv(_exportThroatsCsvDialog.SelectedPath);
                Logger.Log($"[PNMFilterTools] Exported throats CSV to '{_exportThroatsCsvDialog.SelectedPath}'");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[PNMFilterTools] Failed to export throats CSV: {ex.Message}");
            }
    }

    private void DrawFilterSection(PNMDataset pnm)
    {
        if (ImGui.CollapsingHeader("Filter Visualised Pores/Throats", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // Units toggle
            ImGui.Checkbox("Use physical units (µm / µm² / µm³)", ref _usePhysicalUnits);
            ImGui.Separator();

            var voxelSize = Math.Max(pnm.VoxelSize, 1e-6f); // prevent divide-by-zero

            // Suggested ranges from current visible stats
            var radMin = pnm.MinPoreRadius;
            var radMax = pnm.MaxPoreRadius;
            var throatMin = pnm.MinThroatRadius;
            var throatMax = pnm.MaxThroatRadius;

            // Convert to µm for sliders if requested
            var radMinDisp = _usePhysicalUnits ? radMin * voxelSize : radMin;
            var radMaxDisp = _usePhysicalUnits ? radMax * voxelSize : radMax;

            var throatMinDisp = _usePhysicalUnits ? throatMin * voxelSize : throatMin;
            var throatMaxDisp = _usePhysicalUnits ? throatMax * voxelSize : throatMax;

            // Initialize UI ranges lazily
            if (_maxPoreRad <= 0f)
            {
                _minPoreRad = radMinDisp;
                _maxPoreRad = radMaxDisp;
            }

            if (_maxThroatRad <= 0f)
            {
                _minThroatRad = throatMinDisp;
                _maxThroatRad = throatMaxDisp;
            }

            // Pore radius
            ImGui.Text("Pore radius");
            ImGui.SetNextItemWidth(350);
            ImGui.DragFloatRange2("##PoreRadius", ref _minPoreRad, ref _maxPoreRad, 0.1f, 0, float.MaxValue,
                _usePhysicalUnits ? "min: %.3f µm" : "min: %.3f vox",
                _usePhysicalUnits ? "max: %.3f µm" : "max: %.3f vox");

            // Pore area (display in µm² when physical)
            ImGui.Text("Pore area");
            ImGui.SetNextItemWidth(350);
            ImGui.DragFloatRange2("##PoreArea", ref _minArea, ref _maxArea, 0.1f, 0, float.MaxValue,
                _usePhysicalUnits ? "min: %.3f µm²" : "min: %.3f vox²",
                _usePhysicalUnits ? "max: %.3f µm²" : "max: %.3f vox²");

            // Pore volume (voxel and physical)
            ImGui.Text("Pore volume (voxels)");
            ImGui.SetNextItemWidth(350);
            ImGui.DragFloatRange2("##PoreVolVox", ref _minVolVox, ref _maxVolVox, 0.1f, 0, float.MaxValue,
                "min: %.3f", "max: %.3f");

            ImGui.Text("Pore volume (physical)");
            ImGui.SetNextItemWidth(350);
            ImGui.DragFloatRange2("##PoreVolPhys", ref _minVolPhys, ref _maxVolPhys, 0.1f, 0, float.MaxValue,
                "min: %.3f µm³", "max: %.3f µm³");

            // Connections
            ImGui.Text("Connections");
            ImGui.SetNextItemWidth(350);
            ImGui.DragIntRange2("##Connections", ref _minConn, ref _maxConn, 1, 0, int.MaxValue, "min: %d", "max: %d");

            ImGui.Separator();

            // Throat radius filter
            ImGui.Text("Throat radius");
            ImGui.SetNextItemWidth(350);
            ImGui.DragFloatRange2("##ThroatRadius", ref _minThroatRad, ref _maxThroatRad, 0.1f, 0, float.MaxValue,
                _usePhysicalUnits ? "min: %.3f µm" : "min: %.3f vox",
                _usePhysicalUnits ? "max: %.3f µm" : "max: %.3f vox");

            ImGui.Spacing();
            if (ImGui.Button("Apply Filter", new Vector2(150, 0)))
            {
                var c = new PoreFilterCriteria();

                // Convert displayed physical back to voxel for pores if needed
                Func<float, float> fromDisplayToVox = _usePhysicalUnits
                    ? v => v / voxelSize
                    : v => v;

                // Pore radius
                if (_minPoreRad > 0) c.MinPoreRadius = fromDisplayToVox(_minPoreRad);
                if (_maxPoreRad > 0) c.MaxPoreRadius = fromDisplayToVox(_maxPoreRad);

                // Area
                if (_minArea > 0) c.MinPoreArea = _usePhysicalUnits ? _minArea / (voxelSize * voxelSize) : _minArea;
                if (_maxArea > 0) c.MaxPoreArea = _usePhysicalUnits ? _maxArea / (voxelSize * voxelSize) : _maxArea;

                // Volumes
                if (_minVolVox > 0) c.MinPoreVolumeVox = _minVolVox;
                if (_maxVolVox > 0) c.MaxPoreVolumeVox = _maxVolVox;

                if (_minVolPhys > 0) c.MinPoreVolumePhys = _minVolPhys;
                if (_maxVolPhys > 0) c.MaxPoreVolumePhys = _maxVolPhys;

                // Connections
                if (_minConn > 0) c.MinConnections = _minConn;
                if (_maxConn > 0) c.MaxConnections = _maxConn;

                // Throat radius
                if (_minThroatRad > 0)
                    c.MinThroatRadius = _usePhysicalUnits ? _minThroatRad / voxelSize : _minThroatRad;
                if (_maxThroatRad > 0)
                    c.MaxThroatRadius = _usePhysicalUnits ? _maxThroatRad / voxelSize : _maxThroatRad;

                pnm.ApplyFilter(c);
                Logger.Log("[PNMFilterTools] Filter applied.");
            }

            ImGui.SameLine();
            if (ImGui.Button("Reset", new Vector2(120, 0)))
            {
                _minPoreRad = _maxPoreRad = 0;
                _minArea = _maxArea = 0;
                _minVolVox = _maxVolVox = 0;
                _minVolPhys = _maxVolPhys = 0;
                _minConn = _maxConn = 0;
                _minThroatRad = _maxThroatRad = 0;
                pnm.ClearFilter();
                Logger.Log("[PNMFilterTools] Filter reset.");
            }

            ImGui.Unindent();
        }
    }

    private void DrawExportSection(PNMDataset pnm)
    {
        if (ImGui.CollapsingHeader("Export Pores/Throats", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            // Tables
            if (ImGui.Button("Create Pores Table Dataset", new Vector2(-1, 0)))
            {
                var tbl = pnm.BuildPoresTableDataset($"{pnm.Name}_Pores");
                ProjectManager.Instance.AddDataset(tbl);
                Logger.Log($"[PNMFilterTools] Created table dataset '{tbl.Name}' for pores.");
            }

            if (ImGui.Button("Create Throats Table Dataset", new Vector2(-1, 0)))
            {
                var tbl = pnm.BuildThroatsTableDataset($"{pnm.Name}_Throats");
                ProjectManager.Instance.AddDataset(tbl);
                Logger.Log($"[PNMFilterTools] Created table dataset '{tbl.Name}' for throats.");
            }

            ImGui.Separator();

            // CSV
            if (ImGui.Button("Export Pores as CSV...", new Vector2(-1, 0)))
                _exportPoresCsvDialog.Open($"{pnm.Name}_Pores");
            if (ImGui.Button("Export Throats as CSV...", new Vector2(-1, 0)))
                _exportThroatsCsvDialog.Open($"{pnm.Name}_Throats");

            ImGui.TextDisabled("Tip: tables can be sorted & re-exported using Table Tools and Table Viewer.");

            ImGui.Unindent();
        }
    }
}