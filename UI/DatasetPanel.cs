// GeoscientistToolkit/UI/DatasetPanel.cs (Fixed Unicode Issue)

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.AcousticVolume;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.GIS;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Data.Pnm;
using GeoscientistToolkit.Data.Table;
using GeoscientistToolkit.UI.AcousticVolume;
using ImGuiNET;

// Added for PNM

namespace GeoscientistToolkit.UI;

public class DatasetPanel : BasePanel
{
    private readonly AcousticToCtConverterDialog _acousticToCtConverterDialog = new();

    private readonly MetadataEditor _metadataEditor = new();

    // Multi-selection state
    private readonly HashSet<Dataset> _selectedDatasets = new();
    private Dataset _lastSelectedDataset;
    private Action<Dataset> _onDatasetSelected;
    private Action _onImportClicked;
    private List<Dataset> _orderedDatasets = new(); // To maintain order for shift-selection
    private string _searchFilter = "";

    public DatasetPanel() : base("Datasets", new Vector2(250, 400))
    {
    }

    public void Submit(ref bool pOpen, Action<Dataset> onDatasetSelected, Action onImportClicked)
    {
        _onDatasetSelected = onDatasetSelected;
        _onImportClicked = onImportClicked;
        base.Submit(ref pOpen);
    }

    protected override void DrawContent()
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##Search", "Search datasets...", ref _searchFilter, 256);
        ImGui.Separator();

        var datasets = ProjectManager.Instance.LoadedDatasets;

        // Update ordered datasets list for shift-selection
        _orderedDatasets = datasets
            .Where(d => string.IsNullOrEmpty(_searchFilter) ||
                        d.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Type)
            .ThenBy(d => d.Name)
            .ToList();

        if (datasets.Count == 0)
        {
            // --- Show empty state UI ---
            var windowWidth = ImGui.GetWindowSize().X;
            var textWidth = ImGui.CalcTextSize("No datasets loaded").X;
            ImGui.SetCursorPosX((windowWidth - textWidth) * 0.5f);
            ImGui.TextDisabled("No datasets loaded");

            ImGui.Spacing();

            var buttonText = "Import Data";
            var buttonWidth = ImGui.CalcTextSize(buttonText).X + ImGui.GetStyle().FramePadding.X * 2;
            ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);

            if (ImGui.Button(buttonText)) _onImportClicked?.Invoke();
        }
        else
        {
            // Check for clicks outside any dataset to clear selection
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered() &&
                !ImGui.IsAnyItemHovered())
                _selectedDatasets.Clear();

            // --- Group datasets by type ---
            var datasetsByType = _orderedDatasets
                .GroupBy(d => d.Type)
                .OrderBy(g => g.Key.ToString());

            if (!datasetsByType.Any())
                ImGui.TextDisabled("No datasets match the search");
            else
                foreach (var group in datasetsByType)
                {
                    var icon = GetIconForDatasetType(group.Key);
                    var headerText = $"{icon} {group.Key}";

                    ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.26f, 0.59f, 0.98f, 0.31f));
                    if (ImGui.TreeNodeEx(headerText, ImGuiTreeNodeFlags.DefaultOpen))
                    {
                        ImGui.PopStyleColor();

                        foreach (var dataset in group.OrderBy(d => d.Name)) DrawDatasetItem(dataset);
                        ImGui.TreePop();
                    }
                    else
                    {
                        ImGui.PopStyleColor();
                    }
                }
        }

        _acousticToCtConverterDialog.Draw();
        _metadataEditor.Submit();
        ImGui.Separator();
        ImGui.TextDisabled($"{datasets.Count} dataset(s) loaded");
        if (_selectedDatasets.Count > 1) ImGui.TextDisabled($"{_selectedDatasets.Count} selected");
    }

    private void DrawDatasetItem(Dataset dataset, int indentLevel = 0)
    {
        ImGui.PushID(dataset.GetHashCode());

        // Apply indentation for grouped items
        if (indentLevel > 0) ImGui.Indent(20f * indentLevel);

        var isSelected = _selectedDatasets.Contains(dataset);

        // Change color if dataset is missing
        if (dataset.IsMissing) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.4f, 0.4f, 1.0f));

        // Check if dataset has metadata
        var hasMetadata = !string.IsNullOrEmpty(dataset.DatasetMetadata?.SampleName) ||
                          !string.IsNullOrEmpty(dataset.DatasetMetadata?.LocationName) ||
                          dataset.DatasetMetadata?.Latitude.HasValue == true ||
                          dataset.DatasetMetadata?.Longitude.HasValue == true;

        var displayName = dataset.Name;
        if (hasMetadata)
            // Use simple text indicator instead of Unicode emoji
            displayName = "[M] " + displayName; // [M] for Metadata

        // Handle selection
        if (ImGui.Selectable(displayName, isSelected)) HandleDatasetSelection(dataset);

        if (dataset.IsMissing) ImGui.PopStyleColor();

        // Show enhanced tooltip with metadata
        if (ImGui.IsItemHovered()) ShowDatasetTooltipWithMetadata(dataset);

        // Context menu
        if (ImGui.BeginPopupContextItem())
        {
            // If right-clicking on an item that is not selected, clear 
            // the current selection and select only this one.
            if (!isSelected)
            {
                _selectedDatasets.Clear();
                _selectedDatasets.Add(dataset);
                _lastSelectedDataset = dataset;
            }

            DrawContextMenuWithMetadata(dataset);
            ImGui.EndPopup();
        }

        // If this is a group, draw its children
        if (dataset is DatasetGroup group)
            foreach (var child in group.Datasets)
                DrawDatasetItem(child, indentLevel + 1);

        if (indentLevel > 0) ImGui.Unindent(20f * indentLevel);

        ImGui.PopID();
    }

    private void ShowDatasetTooltipWithMetadata(Dataset dataset)
    {
        ImGui.BeginTooltip();

        if (dataset.IsMissing)
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), "Source file or directory not found!");

        ImGui.TextUnformatted($"Name: {dataset.Name}");
        ImGui.TextUnformatted($"Type: {dataset.Type}");
        ImGui.TextUnformatted($"Path: {dataset.FilePath}");

        // Show metadata if available
        var meta = dataset.DatasetMetadata;
        if (meta != null)
        {
            var hasAnyMetadata = false;

            if (!string.IsNullOrEmpty(meta.SampleName))
            {
                if (!hasAnyMetadata)
                {
                    ImGui.Separator();
                    ImGui.Text("Metadata:");
                    hasAnyMetadata = true;
                }

                ImGui.TextUnformatted($"  Sample: {meta.SampleName}");
            }

            if (!string.IsNullOrEmpty(meta.LocationName))
            {
                if (!hasAnyMetadata)
                {
                    ImGui.Separator();
                    ImGui.Text("Metadata:");
                    hasAnyMetadata = true;
                }

                ImGui.TextUnformatted($"  Location: {meta.LocationName}");
            }

            if (meta.Latitude.HasValue && meta.Longitude.HasValue)
            {
                if (!hasAnyMetadata)
                {
                    ImGui.Separator();
                    ImGui.Text("Metadata:");
                    hasAnyMetadata = true;
                }

                ImGui.TextUnformatted($"  Coordinates: {meta.Latitude:F6}°, {meta.Longitude:F6}°");
            }

            if (meta.Depth.HasValue)
            {
                if (!hasAnyMetadata)
                {
                    ImGui.Separator();
                    ImGui.Text("Metadata:");
                    hasAnyMetadata = true;
                }

                ImGui.TextUnformatted($"  Depth: {meta.Depth:F2} m");
            }
        }

        // Show type-specific information
        if (dataset is CtImageStackDataset ctDataset)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Dimensions: {ctDataset.Width}x{ctDataset.Height}x{ctDataset.Depth}");
            ImGui.TextUnformatted($"Binning: {ctDataset.BinningSize}");
            ImGui.TextUnformatted($"Pixel Size: {ctDataset.PixelSize} {ctDataset.Unit}");
            ImGui.TextUnformatted($"Materials: {ctDataset.Materials?.Count ?? 0}");
        }
        else if (dataset is StreamingCtVolumeDataset streamingCt)
        {
            ImGui.Separator();
            ImGui.TextUnformatted(
                $"Full Size: {streamingCt.FullWidth}x{streamingCt.FullHeight}x{streamingCt.FullDepth}");
            ImGui.TextUnformatted($"LOD Levels: {streamingCt.LodCount}");
            ImGui.TextUnformatted($"Brick Size: {streamingCt.BrickSize}");
            if (streamingCt.EditablePartner != null)
                ImGui.TextUnformatted($"Editable Partner: {streamingCt.EditablePartner.Name}");
        }
        else if (dataset is Mesh3DDataset mesh3D)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Format: {mesh3D.FileFormat}");
            ImGui.TextUnformatted($"Vertices: {mesh3D.VertexCount:N0}");
            ImGui.TextUnformatted($"Faces: {mesh3D.FaceCount:N0}");
            if (mesh3D.Scale != 1.0f) ImGui.TextUnformatted($"Scale: {mesh3D.Scale:F2}x");
        }
        else if (dataset is TableDataset table)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Format: {table.SourceFormat}");
            ImGui.TextUnformatted($"Rows: {table.RowCount:N0}");
            ImGui.TextUnformatted($"Columns: {table.ColumnCount}");
            if (!string.IsNullOrEmpty(table.Delimiter)) ImGui.TextUnformatted($"Delimiter: '{table.Delimiter}'");
        }
        else if (dataset is GISDataset gis)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Layers: {gis.Layers.Count}");
            ImGui.TextUnformatted($"Projection: {gis.Projection?.Name ?? "Unknown"}");
            ImGui.TextUnformatted($"Basemap: {gis.BasemapType}");
            var totalFeatures = gis.Layers.Sum(l => l.Features.Count);
            ImGui.TextUnformatted($"Total Features: {totalFeatures:N0}");
        }
        else if (dataset is AcousticVolumeDataset acoustic)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"P-Wave Velocity: {acoustic.PWaveVelocity:F1} m/s");
            ImGui.TextUnformatted($"S-Wave Velocity: {acoustic.SWaveVelocity:F1} m/s");
            ImGui.TextUnformatted($"Vp/Vs Ratio: {acoustic.VpVsRatio:F2}");
            ImGui.TextUnformatted($"Time Steps: {acoustic.TimeSteps}");
            if (acoustic.TimeSeriesSnapshots?.Count > 0)
                ImGui.TextUnformatted($"Time Series: {acoustic.TimeSeriesSnapshots.Count} snapshots");
        }
        else if (dataset is PNMDataset pnm)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Pores: {pnm.Pores.Count:N0}");
            ImGui.TextUnformatted($"Throats: {pnm.Throats.Count:N0}");
            ImGui.TextUnformatted($"Darcy Permeability: {pnm.DarcyPermeability:F3} mD");
        }
        else if (dataset is DatasetGroup group)
        {
            ImGui.Separator();
            ImGui.TextUnformatted($"Contains {group.Datasets.Count} datasets:");
            foreach (var child in group.Datasets) ImGui.TextUnformatted($"  • {child.Name}");
        }

        ImGui.EndTooltip();
    }

    private void DrawContextMenuWithMetadata(Dataset dataset)
    {
        // View option
        if (ImGui.MenuItem("View", null, false, !(dataset is DatasetGroup) && !dataset.IsMissing))
            _onDatasetSelected?.Invoke(dataset);

        // Edit Metadata option
        ImGui.Separator();
        if (ImGui.MenuItem("Edit Metadata...", null, false, !(dataset is DatasetGroup))) _metadataEditor.Open(dataset);

        // Group-specific options
        if (dataset is DatasetGroup group)
        {
            if (ImGui.MenuItem("View Thumbnails"))
                // Signal to open thumbnail viewer
                OnOpenThumbnailViewer?.Invoke(group);

            if (ImGui.MenuItem("Ungroup")) UngroupDataset(group);
        }

        if (dataset is AcousticVolumeDataset acousticDataset)
        {
            ImGui.Separator();
            if (ImGui.MenuItem("Convert Velocity Field To Greyscale Dataset"))
                _acousticToCtConverterDialog.Open(acousticDataset);
        }

        // Multi-selection grouping
        if (_selectedDatasets.Count > 1 && _selectedDatasets.Contains(dataset))
            if (ImGui.MenuItem("Group Selected"))
                CreateGroup();

        ImGui.Separator();

        // Close/Remove option - now acts on all selected items
        if (ImGui.MenuItem("Close"))
        {
            var itemsToClose = _selectedDatasets.ToList();

            foreach (var item in itemsToClose)
            {
                if (item is DatasetGroup grp)
                    // Also remove all datasets within the group
                    foreach (var child in grp.Datasets.ToList())
                        ProjectManager.Instance.RemoveDataset(child);

                ProjectManager.Instance.RemoveDataset(item);
            }

            _selectedDatasets.Clear();
        }
    }

    private void HandleDatasetSelection(Dataset dataset)
    {
        if (dataset.IsMissing)
            // Optionally prevent interaction with missing datasets
            return;

        var ctrlHeld = ImGui.GetIO().KeyCtrl;
        var shiftHeld = ImGui.GetIO().KeyShift;

        if (ctrlHeld)
        {
            // Toggle selection
            if (_selectedDatasets.Contains(dataset))
                _selectedDatasets.Remove(dataset);
            else
                _selectedDatasets.Add(dataset);
        }
        else if (shiftHeld && _lastSelectedDataset != null)
        {
            // Range selection
            var startIdx = _orderedDatasets.IndexOf(_lastSelectedDataset);
            var endIdx = _orderedDatasets.IndexOf(dataset);

            if (startIdx != -1 && endIdx != -1)
            {
                var minIdx = Math.Min(startIdx, endIdx);
                var maxIdx = Math.Max(startIdx, endIdx);

                for (var i = minIdx; i <= maxIdx; i++)
                    if (!_orderedDatasets[i].IsMissing) // Don't select missing items in range
                        _selectedDatasets.Add(_orderedDatasets[i]);
            }
        }
        else
        {
            // Single selection
            _selectedDatasets.Clear();
            _selectedDatasets.Add(dataset);
            _onDatasetSelected?.Invoke(dataset);
        }

        _lastSelectedDataset = dataset;
    }

    // Add this event at the top of the class with other fields
    public event Action<DatasetGroup> OnOpenThumbnailViewer;

    private void CreateGroup()
    {
        if (_selectedDatasets.Count < 2) return;

        // Create group name
        var groupName = $"Group {ProjectManager.Instance.LoadedDatasets.Count(d => d is DatasetGroup) + 1}";

        // Create the group
        var group = new DatasetGroup(groupName, _selectedDatasets.ToList());

        // Remove individual datasets from the project
        foreach (var dataset in _selectedDatasets) ProjectManager.Instance.RemoveDataset(dataset);

        // Add the group
        ProjectManager.Instance.AddDataset(group);

        // Clear selection
        _selectedDatasets.Clear();
    }

    private void UngroupDataset(DatasetGroup group)
    {
        // Add all datasets back to the project
        foreach (var dataset in group.Datasets) ProjectManager.Instance.AddDataset(dataset);

        // Remove the group
        ProjectManager.Instance.RemoveDataset(group);
        _selectedDatasets.Remove(group);
    }

    private string GetIconForDatasetType(DatasetType type)
    {
        return type switch
        {
            DatasetType.CtImageStack => "[STACK]",
            DatasetType.CtBinaryFile => "[BIN]",
            DatasetType.MicroXrf => "[XRF]",
            DatasetType.PointCloud => "[PCD]",
            DatasetType.Mesh => "[MESH]",
            DatasetType.SingleImage => "[IMG]",
            DatasetType.Group => "[GROUP]",
            DatasetType.Mesh3D => "[3D]",
            DatasetType.Table => "[TABLE]",
            DatasetType.GIS => "[GIS]",
            DatasetType.AcousticVolume => "[ACOUSTIC]",
            DatasetType.PNM => "[PNM]", // Added for PNM
            _ => "[DATA]"
        };
    }
}