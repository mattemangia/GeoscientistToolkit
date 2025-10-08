// GeoscientistToolkit/UI/MetadataEditor.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

public class MetadataEditor
{
    private readonly string[] _sizeUnits = { "nm", "µm", "mm", "cm", "m", "km" };
    private string _collectionDateStr = "";
    private string _collector = "";
    private Dataset _dataset;
    private string _depthStr = "";
    private bool _isOpen;
    private string _latitudeStr = "";
    private string _locationName = "";
    private string _longitudeStr = "";

    // Custom fields
    private string _newFieldKey = "";
    private string _newFieldValue = "";
    private string _notes = "";

    // Form fields
    private string _sampleName = "";
    private Vector3 _size = Vector3.Zero;
    private string _sizeUnit = "mm";
    private DatasetMetadata _tempMetadata;

    public void Open(Dataset dataset)
    {
        _dataset = dataset;
        _tempMetadata = dataset.DatasetMetadata.Clone();
        LoadFromMetadata();
        _isOpen = true;
    }

    private void LoadFromMetadata()
    {
        _sampleName = _tempMetadata.SampleName ?? "";
        _locationName = _tempMetadata.LocationName ?? "";
        _latitudeStr = _tempMetadata.Latitude?.ToString() ?? "";
        _longitudeStr = _tempMetadata.Longitude?.ToString() ?? "";
        _depthStr = _tempMetadata.Depth?.ToString() ?? "";
        _size = _tempMetadata.Size ?? Vector3.Zero;
        _sizeUnit = _tempMetadata.SizeUnit ?? "mm";
        _notes = _tempMetadata.Notes ?? "";
        _collectionDateStr = _tempMetadata.CollectionDate?.ToString("yyyy-MM-dd") ?? "";
        _collector = _tempMetadata.Collector ?? "";
    }

    public void Submit()
    {
        if (!_isOpen || _dataset == null) return;

        ImGui.SetNextWindowSize(new Vector2(600, 700), ImGuiCond.FirstUseEver);
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

        if (ImGui.Begin($"Edit Metadata: {_dataset.Name}###MetadataEditor", ref _isOpen))
        {
            // Basic Information
            if (ImGui.CollapsingHeader("Basic Information", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                ImGui.Text("Sample Name:");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##SampleName", ref _sampleName, 256);

                ImGui.Text("Dataset Type:");
                ImGui.TextDisabled(_dataset.Type.ToString());

                ImGui.Text("Collector:");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##Collector", ref _collector, 256);

                ImGui.Text("Collection Date (YYYY-MM-DD):");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##CollectionDate", ref _collectionDateStr, 32);

                ImGui.Unindent();
            }

            // Location Information
            if (ImGui.CollapsingHeader("Location Information", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                ImGui.Text("Location Name:");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##LocationName", ref _locationName, 256);

                ImGui.Text("Latitude (decimal degrees, WGS 84):");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##Latitude", ref _latitudeStr, 32);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Range: -90 to 90");

                ImGui.Text("Longitude (decimal degrees, WGS 84):");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##Longitude", ref _longitudeStr, 32);
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Range: -180 to 180");

                ImGui.Text("Depth (meters):");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##Depth", ref _depthStr, 32);

                ImGui.Unindent();
            }

            // Physical Dimensions
            if (ImGui.CollapsingHeader("Physical Dimensions", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                ImGui.Text("Size (X, Y, Z):");
                ImGui.DragFloat3("##Size", ref _size, 0.1f, 0.0f, float.MaxValue, "%.3f");

                ImGui.Text("Unit:");
                ImGui.SetNextItemWidth(100);
                if (ImGui.BeginCombo("##SizeUnit", _sizeUnit))
                {
                    foreach (var unit in _sizeUnits)
                        if (ImGui.Selectable(unit, unit == _sizeUnit))
                            _sizeUnit = unit;
                    ImGui.EndCombo();
                }

                ImGui.Unindent();
            }

            // Notes
            if (ImGui.CollapsingHeader("Notes", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                ImGui.InputTextMultiline("##Notes", ref _notes, 1024, new Vector2(-1, 100));
                ImGui.Unindent();
            }

            // Custom Fields
            if (ImGui.CollapsingHeader("Custom Fields"))
            {
                ImGui.Indent();

                // Display existing custom fields
                foreach (var kvp in _tempMetadata.CustomFields)
                {
                    ImGui.Text($"{kvp.Key}: {kvp.Value}");
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Remove##{kvp.Key}"))
                    {
                        _tempMetadata.CustomFields.Remove(kvp.Key);
                        break;
                    }
                }

                ImGui.Separator();

                // Add new custom field
                ImGui.Text("Add Custom Field:");
                ImGui.SetNextItemWidth(150);
                ImGui.InputText("##NewFieldKey", ref _newFieldKey, 64);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200);
                ImGui.InputText("##NewFieldValue", ref _newFieldValue, 256);
                ImGui.SameLine();
                if (ImGui.Button("Add") && !string.IsNullOrWhiteSpace(_newFieldKey))
                {
                    _tempMetadata.CustomFields[_newFieldKey] = _newFieldValue;
                    _newFieldKey = "";
                    _newFieldValue = "";
                }

                ImGui.Unindent();
            }

            ImGui.Separator();

            // Buttons
            if (ImGui.Button("Save", new Vector2(100, 0)))
            {
                SaveMetadata();
                _isOpen = false;
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(100, 0))) _isOpen = false;

            ImGui.End();
        }
    }

    private void SaveMetadata()
    {
        _tempMetadata.SampleName = _sampleName;
        _tempMetadata.LocationName = _locationName;

        // Parse latitude
        if (double.TryParse(_latitudeStr, out var lat) && lat >= -90 && lat <= 90)
            _tempMetadata.Latitude = lat;
        else
            _tempMetadata.Latitude = null;

        // Parse longitude
        if (double.TryParse(_longitudeStr, out var lon) && lon >= -180 && lon <= 180)
            _tempMetadata.Longitude = lon;
        else
            _tempMetadata.Longitude = null;

        // Parse depth
        if (double.TryParse(_depthStr, out var depth))
            _tempMetadata.Depth = depth;
        else
            _tempMetadata.Depth = null;

        // Save size
        if (_size != Vector3.Zero)
            _tempMetadata.Size = _size;
        else
            _tempMetadata.Size = null;

        _tempMetadata.SizeUnit = _sizeUnit;
        _tempMetadata.Notes = _notes;
        _tempMetadata.Collector = _collector;

        // Parse collection date
        if (DateTime.TryParse(_collectionDateStr, out var collDate))
            _tempMetadata.CollectionDate = collDate;
        else
            _tempMetadata.CollectionDate = null;

        // Apply to dataset
        _dataset.DatasetMetadata = _tempMetadata;

        // Mark project as having unsaved changes
        ProjectManager.Instance.HasUnsavedChanges = true;

        Logger.Log($"Updated metadata for dataset: {_dataset.Name}");
    }
}