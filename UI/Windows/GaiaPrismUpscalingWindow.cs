using System.Numerics;
using GAIA.Business;
using GAIA.Data.Borehole;
using GAIA.Data.Pnm;
using GAIA.Interop.GaiaPrism;
using GAIA.UI.Utils;
using GAIA.Util;
using ImGuiNET;

namespace GAIA.UI.Windows;

/// <summary>
///     Tool window for the GAIA ↔ PRISM upscaling bridge: exports boreholes + pore networks as an
///     upscaling GPEX package (with per-interval PNM assignments), and imports PRISM well packages
///     back into the project as borehole datasets.
/// </summary>
public class GaiaPrismUpscalingWindow
{
    private sealed class AssignmentRow
    {
        public int PnmIndex;
        public int WellIndex; // 0 = all wells, otherwise index+1 into selected boreholes
        public float DepthFrom;
        public float DepthTo = 10f;
        public float Weight = 1f;
    }

    private readonly ImGuiExportFileDialog _exportDialog = new("GaiaPrismUpscalingExport", "Export Upscaling Package");
    private readonly ImGuiFileDialog _importDialog = new("GaiaPrismUpscalingImport", FileDialogType.OpenFile, "Open Upscaling Package");
    private readonly List<AssignmentRow> _assignments = new();
    private readonly HashSet<string> _selectedBoreholes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectedPnms = new(StringComparer.Ordinal);
    private readonly List<string> _log = new();
    private string _projectId = "";
    private bool _isVisible;
    private UpscalingGpexImporter.ImportedUpscalingPackage _imported;
    private string _importedPath = "";

    public GaiaPrismUpscalingWindow()
    {
        _exportDialog.SetExtensions((".gpex", "GAIA-PRISM exchange package"));
    }

    public void Show()
    {
        _isVisible = true;
    }

    public void Draw()
    {
        if (!_isVisible) return;
        ImGui.SetNextWindowSize(new Vector2(900, 640), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("GAIA ↔ PRISM Upscaling", ref _isVisible))
        {
            if (ImGui.BeginTabBar("UpscalingTabs"))
            {
                if (ImGui.BeginTabItem("Export to PRISM"))
                {
                    DrawExportTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Import from PRISM"))
                {
                    DrawImportTab();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            DrawLog();
        }

        ImGui.End();
        SubmitDialogs();
    }

    // ----------------------------------------------------------------- export

    private List<BoreholeDataset> LoadedBoreholes() =>
        ProjectManager.Instance.LoadedDatasets.OfType<BoreholeDataset>().ToList();

    private List<PNMDataset> LoadedPnms() =>
        ProjectManager.Instance.LoadedDatasets.OfType<PNMDataset>().ToList();

    private void DrawExportTab()
    {
        var boreholes = LoadedBoreholes();
        var pnms = LoadedPnms();

        ImGui.TextWrapped("Select the wells and characterised pore networks to export. Assign each " +
                          "pore network to the depth interval it represents; intervals without measured " +
                          "parameters are filled by upscaling the assigned networks.");
        ImGui.Spacing();

        var available = ImGui.GetContentRegionAvail();
        var listHeight = MathF.Max(140f, available.Y * 0.32f);

        ImGui.BeginChild("WellList", new Vector2(available.X * 0.5f - 4, listHeight), ImGuiChildFlags.Border);
        ImGui.SeparatorText($"Wells ({boreholes.Count})");
        if (boreholes.Count == 0) ImGui.TextDisabled("No borehole datasets in the project.");
        foreach (var borehole in boreholes)
        {
            var selected = _selectedBoreholes.Contains(borehole.Name);
            if (ImGui.Checkbox($"{borehole.Name}##bh", ref selected))
            {
                if (selected) _selectedBoreholes.Add(borehole.Name);
                else _selectedBoreholes.Remove(borehole.Name);
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{borehole.LithologyUnits.Count} lithology units, TD {borehole.TotalDepth:0.##} m");
        }

        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("PnmList", new Vector2(0, listHeight), ImGuiChildFlags.Border);
        ImGui.SeparatorText($"Pore networks ({pnms.Count})");
        if (pnms.Count == 0) ImGui.TextDisabled("No PNM datasets in the project.");
        foreach (var pnm in pnms)
        {
            var selected = _selectedPnms.Contains(pnm.Name);
            if (ImGui.Checkbox($"{pnm.Name}##pnm", ref selected))
            {
                if (selected) _selectedPnms.Add(pnm.Name);
                else _selectedPnms.Remove(pnm.Name);
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{pnm.Pores.Count} pores, k(Darcy) {pnm.DarcyPermeability:0.###} mD, " +
                                 $"voxel {pnm.VoxelSize:0.##} µm");
        }

        ImGui.EndChild();

        var selectedPnms = pnms.Where(p => _selectedPnms.Contains(p.Name)).ToList();
        var selectedBoreholes = boreholes.Where(b => _selectedBoreholes.Contains(b.Name)).ToList();

        ImGui.SeparatorText("PNM → well interval assignments");
        ImGui.TextDisabled("Networks already linked to lithology units via 'Import Parameters from Dataset' are attached automatically.");
        if (ImGui.Button("+ Add assignment") && selectedPnms.Count > 0) _assignments.Add(new AssignmentRow());
        if (selectedPnms.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(select at least one pore network first)");
        }

        int? removeIndex = null;
        if (_assignments.Count > 0 && ImGui.BeginTable("Assignments", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Pore network", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("Well", ImGuiTableColumnFlags.WidthStretch, 2f);
            ImGui.TableSetupColumn("From (m)", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("To (m)", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("Weight", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##rm", ImGuiTableColumnFlags.WidthFixed, 28f);
            ImGui.TableHeadersRow();

            var pnmNames = selectedPnms.Select(p => p.Name).ToArray();
            var wellNames = new[] { "All wells" }.Concat(selectedBoreholes.Select(WellDisplayName)).ToArray();
            for (var i = 0; i < _assignments.Count; i++)
            {
                var row = _assignments[i];
                row.PnmIndex = Math.Clamp(row.PnmIndex, 0, Math.Max(0, pnmNames.Length - 1));
                row.WellIndex = Math.Clamp(row.WellIndex, 0, Math.Max(0, wellNames.Length - 1));
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.Combo($"##pnm{i}", ref row.PnmIndex, pnmNames, pnmNames.Length);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.Combo($"##well{i}", ref row.WellIndex, wellNames, wellNames.Length);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputFloat($"##from{i}", ref row.DepthFrom, 0, 0, "%.2f");
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputFloat($"##to{i}", ref row.DepthTo, 0, 0, "%.2f");
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputFloat($"##w{i}", ref row.Weight, 0, 0, "%.2f");
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"X##rm{i}")) removeIndex = i;
            }

            ImGui.EndTable();
        }

        if (removeIndex is { } toRemove) _assignments.RemoveAt(toRemove);

        ImGui.Spacing();
        if (string.IsNullOrWhiteSpace(_projectId)) _projectId = ProjectManager.Instance.ProjectName ?? "gaia-project";
        ImGui.SetNextItemWidth(280);
        ImGui.InputText("Project ID", ref _projectId, 128);

        var canExport = selectedBoreholes.Count > 0;
        if (!canExport) ImGui.BeginDisabled();
        if (ImGui.Button("Export .gpex package...", new Vector2(220, 0)))
            _exportDialog.Open($"{SanitizeFileName(_projectId)}-upscaling.gpex");
        if (!canExport)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled("Select at least one well.");
        }
    }

    private static string WellDisplayName(BoreholeDataset borehole) =>
        string.IsNullOrWhiteSpace(borehole.WellName) ? borehole.Name : borehole.WellName;

    private void RunExport(string path)
    {
        try
        {
            var boreholes = LoadedBoreholes().Where(b => _selectedBoreholes.Contains(b.Name)).ToList();
            var pnms = LoadedPnms().Where(p => _selectedPnms.Contains(p.Name)).ToList();
            var assignments = new List<UpscalingGpexExporter.PnmWellAssignment>();
            foreach (var row in _assignments)
            {
                if (row.PnmIndex >= pnms.Count) continue;
                string wellName = null;
                if (row.WellIndex > 0 && row.WellIndex - 1 < boreholes.Count)
                    wellName = WellDisplayName(boreholes[row.WellIndex - 1]);
                assignments.Add(new UpscalingGpexExporter.PnmWellAssignment(
                    pnms[row.PnmIndex], row.DepthFrom, row.DepthTo, row.Weight, wellName));
            }

            var manifest = UpscalingGpexExporter.Export(path, _projectId, boreholes, pnms, assignments);
            Log($"Exported {boreholes.Count} well(s) and {pnms.Count} pore network(s) to {path}");
            foreach (var message in manifest.Validation)
                Log($"  {message.Severity}: {message.Code}: {message.Message}");
            Logger.Log($"[GaiaPrismUpscaling] Exported upscaling package: {path}");
        }
        catch (Exception ex)
        {
            Log("Export failed: " + ex.Message);
            Logger.LogError($"[GaiaPrismUpscaling] Export failed: {ex}");
        }
    }

    // ----------------------------------------------------------------- import

    private void DrawImportTab()
    {
        ImGui.TextWrapped("Load a PRISM → GAIA upscaling package (.gpex) to preview its wells and add " +
                          "them to the project as borehole datasets for pore-scale downscaling.");
        ImGui.Spacing();
        if (ImGui.Button("Open .gpex package...", new Vector2(220, 0)))
            _importDialog.Open(null, new[] { ".gpex" });

        if (_imported == null) return;

        ImGui.SameLine();
        ImGui.TextDisabled(_importedPath);
        ImGui.Separator();
        var manifest = _imported.Manifest;
        ImGui.Text($"Exchange {manifest.ExchangeId}");
        ImGui.Text($"Direction: {manifest.Direction}   Producer: {manifest.Producer} {manifest.ProducerVersion}");
        ImGui.Text($"Wells: {_imported.Wells.Wells.Count}   Pore networks: {_imported.PnmSummaries.Count}");
        foreach (var message in _imported.Validation.Messages)
            ImGui.TextColored(message.Severity == ValidationSeverity.Warning
                    ? new Vector4(0.95f, 0.75f, 0.25f, 1f)
                    : new Vector4(0.8f, 0.8f, 0.8f, 1f),
                $"{message.Severity}: {message.Code}: {message.Message}");

        ImGui.Spacing();
        ImGui.BeginChild("ImportPreview", new Vector2(0, MathF.Max(160f, ImGui.GetContentRegionAvail().Y - 40)),
            ImGuiChildFlags.Border);
        foreach (var well in _imported.Wells.Wells)
        {
            var zone = IntervalUpscaler.UpscaleLayers(well.Intervals);
            if (!ImGui.TreeNode($"{well.Name} — {well.Intervals.Count} intervals, {zone.ThicknessMetres:0.##} m##{well.Id}"))
                continue;
            if (zone.PorosityFraction is { } phi)
                ImGui.Text($"Zone upscale: φ {phi:0.###}   kh {zone.HorizontalPermeabilityMilliDarcy ?? 0:0.###} mD   kv {zone.VerticalPermeabilityMilliDarcy ?? 0:0.###} mD");
            if (ImGui.BeginTable($"Intervals##{well.Id}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("From (m)");
                ImGui.TableSetupColumn("To (m)");
                ImGui.TableSetupColumn("Lithology");
                ImGui.TableSetupColumn("φ (-)");
                ImGui.TableSetupColumn("k (mD)");
                ImGui.TableHeadersRow();
                foreach (var interval in well.Intervals.OrderBy(i => i.TopDepthMetres))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{interval.TopDepthMetres:0.##}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{interval.BottomDepthMetres:0.##}");
                    ImGui.TableNextColumn();
                    ImGui.Text(interval.Lithology ?? "-");
                    ImGui.TableNextColumn();
                    ImGui.Text(IntervalUpscaler.ScalarProperty(interval, UpscalingPropertyNames.Porosity)?.ToString("0.###") ?? "-");
                    ImGui.TableNextColumn();
                    ImGui.Text(IntervalUpscaler.PermeabilityMilliDarcy(interval)?.ToString("0.###") ?? "-");
                }

                ImGui.EndTable();
            }

            ImGui.TreePop();
        }

        ImGui.EndChild();

        if (ImGui.Button("Add wells to project", new Vector2(220, 0))) AddImportedWells();
    }

    private void LoadPackage(string path)
    {
        try
        {
            _imported = UpscalingGpexImporter.Read(path);
            _importedPath = path;
            Log($"Loaded {Path.GetFileName(path)}: {_imported.Wells.Wells.Count} well(s), {_imported.PnmSummaries.Count} pore network(s).");
        }
        catch (Exception ex)
        {
            _imported = null;
            Log("Load failed: " + ex.Message);
            Logger.LogError($"[GaiaPrismUpscaling] Package load failed: {ex}");
        }
    }

    private void AddImportedWells()
    {
        if (_imported == null) return;
        var targetDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Boreholes");
        try
        {
            Directory.CreateDirectory(targetDirectory);
            foreach (var well in _imported.Wells.Wells)
            {
                var filePath = Path.Combine(targetDirectory, $"{SanitizeFileName(well.Name)}.borehole");
                var borehole = UpscalingGpexImporter.ToBoreholeDataset(well, filePath);
                ProjectManager.Instance.AddDataset(borehole);
                Log($"Added borehole dataset '{borehole.Name}' ({borehole.LithologyUnits.Count} units).");
            }

            Logger.Log($"[GaiaPrismUpscaling] Imported {_imported.Wells.Wells.Count} well(s) from {_importedPath}");
        }
        catch (Exception ex)
        {
            Log("Import failed: " + ex.Message);
            Logger.LogError($"[GaiaPrismUpscaling] Import failed: {ex}");
        }
    }

    // ------------------------------------------------------------------ misc

    private void SubmitDialogs()
    {
        if (_exportDialog.Submit()) RunExport(_exportDialog.SelectedPath);
        if (_importDialog.Submit()) LoadPackage(_importDialog.SelectedPath);
    }

    private void DrawLog()
    {
        ImGui.SeparatorText("Activity");
        ImGui.BeginChild("UpscalingLog", new Vector2(0, 0), ImGuiChildFlags.Border);
        foreach (var line in _log) ImGui.TextWrapped(line);
        ImGui.EndChild();
    }

    private void Log(string message)
    {
        _log.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        if (_log.Count > 200) _log.RemoveAt(0);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "well" : cleaned;
    }
}
