// GeoscientistToolkit/UI/StackRegistrationDialog.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.UI;

/// <summary>
///     Dialog for registering two CT stacks into a single combined volume
/// </summary>
public class StackRegistrationDialog
{
    // File export
    private readonly ImGuiExportFileDialog _exportDialog = new(
        "StackRegistrationExport", "Export Registered Stack");

    private List<CtImageStackDataset> _availableDatasets = new();
    private CancellationTokenSource _cancellationTokenSource;
    private CtImageStackDataset _dataset1;
    private CtImageStackDataset _dataset2;
    private DatasetGroup _group;
    private bool _isOpen;

    // Progress tracking
    private bool _isProcessing;
    private bool _loadDirectly = true;
    private int _maxShift = 50;
    private bool _needsDatasetSelection;
    private string _outputFormat = "PNG";
    private float _progress;
    private string _progressStatus = "";
    private ChunkedVolume _resultVolume;
    private bool _saveToFile;

    // Configuration
    private RegistrationAlignment _selectedAlignment = RegistrationAlignment.AlongZ;
    private int _selectedDataset1Index;
    private int _selectedDataset2Index = 1;
    private RegistrationMethod _selectedMethod = RegistrationMethod.CPU_SIMD;

    public void Open(DatasetGroup group)
    {
        if (group.Datasets.Count < 2)
        {
            Logger.LogError("[StackRegistrationDialog] Group must contain at least 2 datasets");
            return;
        }

        var ctDatasets = group.Datasets.OfType<CtImageStackDataset>().ToList();
        if (ctDatasets.Count < 2)
        {
            Logger.LogError("[StackRegistrationDialog] Group must contain at least 2 CT Image Stacks");
            return;
        }

        _group = group;
        _availableDatasets = ctDatasets;
        _isOpen = true;
        _isProcessing = false;
        _resultVolume = null;

        // If exactly 2 datasets, use them directly
        if (ctDatasets.Count == 2)
        {
            _needsDatasetSelection = false;
            _dataset1 = ctDatasets[0];
            _dataset2 = ctDatasets[1];
            _selectedDataset1Index = 0;
            _selectedDataset2Index = 1;
            Logger.Log($"[StackRegistrationDialog] Opened for {_dataset1.Name} + {_dataset2.Name}");
        }
        else
        {
            // More than 2 datasets - need user selection
            _needsDatasetSelection = true;
            _selectedDataset1Index = 0;
            _selectedDataset2Index = 1;
            _dataset1 = null;
            _dataset2 = null;
            Logger.Log($"[StackRegistrationDialog] Opened with {ctDatasets.Count} datasets - user selection required");
        }
    }

    public void Draw()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(600, 500), ImGuiCond.FirstUseEver);
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

        if (ImGui.Begin("Register CT Stacks", ref _isOpen, ImGuiWindowFlags.NoCollapse))
        {
            if (_needsDatasetSelection)
                DrawDatasetSelectionView();
            else if (_isProcessing)
                DrawProgressView();
            else if (_resultVolume != null)
                DrawResultView();
            else
                DrawConfigurationView();

            ImGui.End();
        }

        // Handle export dialog
        if (_exportDialog.Submit())
        {
            var exportPath = _exportDialog.SelectedPath;
            if (!string.IsNullOrEmpty(exportPath)) _ = ExportResultAsync(exportPath);
        }
    }

    private void DrawDatasetSelectionView()
    {
        ImGui.TextWrapped(
            $"This group contains {_availableDatasets.Count} CT datasets. Please select which two datasets you want to register.");
        ImGui.Separator();
        ImGui.Spacing();

        // Dataset 1 selection
        ImGui.Text("First Dataset:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##Dataset1", _availableDatasets[_selectedDataset1Index].Name))
        {
            for (var i = 0; i < _availableDatasets.Count; i++)
            {
                var isSelected = _selectedDataset1Index == i;
                if (ImGui.Selectable(_availableDatasets[i].Name, isSelected))
                {
                    _selectedDataset1Index = i;
                    // Ensure second dataset is different
                    if (_selectedDataset2Index == i) _selectedDataset2Index = (i + 1) % _availableDatasets.Count;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        // Show dataset 1 info
        var dataset1 = _availableDatasets[_selectedDataset1Index];
        ImGui.Indent();
        ImGui.TextDisabled($"Dimensions: {dataset1.Width}×{dataset1.Height}×{dataset1.Depth}");
        ImGui.TextDisabled($"Pixel Size: {dataset1.PixelSize} {dataset1.Unit}");
        ImGui.Unindent();
        ImGui.Spacing();

        // Dataset 2 selection
        ImGui.Text("Second Dataset:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##Dataset2", _availableDatasets[_selectedDataset2Index].Name))
        {
            for (var i = 0; i < _availableDatasets.Count; i++)
            {
                // Don't allow selecting the same dataset twice
                if (i == _selectedDataset1Index)
                {
                    ImGui.BeginDisabled();
                    ImGui.Selectable(_availableDatasets[i].Name, false);
                    ImGui.EndDisabled();
                    continue;
                }

                var isSelected = _selectedDataset2Index == i;
                if (ImGui.Selectable(_availableDatasets[i].Name, isSelected)) _selectedDataset2Index = i;
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        // Show dataset 2 info
        var dataset2 = _availableDatasets[_selectedDataset2Index];
        ImGui.Indent();
        ImGui.TextDisabled($"Dimensions: {dataset2.Width}×{dataset2.Height}×{dataset2.Depth}");
        ImGui.TextDisabled($"Pixel Size: {dataset2.PixelSize} {dataset2.Unit}");
        ImGui.Unindent();
        ImGui.Spacing();

        // Swap button
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - 150) * 0.5f);
        if (ImGui.Button("⇅ Swap Datasets", new Vector2(150, 0)))
        {
            var temp = _selectedDataset1Index;
            _selectedDataset1Index = _selectedDataset2Index;
            _selectedDataset2Index = temp;
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Warning if dimensions might not be compatible
        if (dataset1.Width != dataset2.Width || dataset1.Height != dataset2.Height)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.7f, 0.0f, 1.0f));
            ImGui.TextWrapped(
                "⚠ Warning: Datasets have different dimensions. Make sure to select the correct alignment direction.");
            ImGui.PopStyleColor();
            ImGui.Spacing();
        }

        // Action buttons
        var buttonWidth = 120f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var totalWidth = buttonWidth * 2 + spacing;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - totalWidth - ImGui.GetStyle().WindowPadding.X);

        if (ImGui.Button("Continue", new Vector2(buttonWidth, 0)))
        {
            _dataset1 = _availableDatasets[_selectedDataset1Index];
            _dataset2 = _availableDatasets[_selectedDataset2Index];
            _needsDatasetSelection = false;
            Logger.Log($"[StackRegistrationDialog] User selected: {_dataset1.Name} + {_dataset2.Name}");
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0))) _isOpen = false;
    }

    private void DrawConfigurationView()
    {
        ImGui.TextWrapped("Configure the registration parameters for combining two CT stacks into a single volume.");
        ImGui.Separator();
        ImGui.Spacing();

        // Dataset info
        ImGui.Text("Datasets:");
        ImGui.Indent();
        ImGui.BulletText($"Stack 1: {_dataset1.Name}");
        ImGui.Text($"  Dimensions: {_dataset1.Width}x{_dataset1.Height}x{_dataset1.Depth}");
        ImGui.BulletText($"Stack 2: {_dataset2.Name}");
        ImGui.Text($"  Dimensions: {_dataset2.Width}x{_dataset2.Height}x{_dataset2.Depth}");
        ImGui.Unindent();
        ImGui.Spacing();

        // Alignment selection
        ImGui.Text("Alignment Direction:");
        ImGui.Indent();
        if (ImGui.RadioButton("Along Z (Depth-wise)", _selectedAlignment == RegistrationAlignment.AlongZ))
            _selectedAlignment = RegistrationAlignment.AlongZ;
        ImGui.SameLine();
        HelpMarker("Stacks are aligned front-to-back. Requires same Width and Height.");

        if (ImGui.RadioButton("Along X (Horizontal)", _selectedAlignment == RegistrationAlignment.AlongX))
            _selectedAlignment = RegistrationAlignment.AlongX;
        ImGui.SameLine();
        HelpMarker("Stacks are aligned side-by-side. Requires same Height and Depth.");

        if (ImGui.RadioButton("Along Y (Vertical)", _selectedAlignment == RegistrationAlignment.AlongY))
            _selectedAlignment = RegistrationAlignment.AlongY;
        ImGui.SameLine();
        HelpMarker("Stacks are aligned top-to-bottom. Requires same Width and Depth.");
        ImGui.Unindent();
        ImGui.Spacing();

        // Method selection
        ImGui.Text("Registration Method:");
        ImGui.Indent();
        if (ImGui.RadioButton("CPU (SIMD Optimized)", _selectedMethod == RegistrationMethod.CPU_SIMD))
            _selectedMethod = RegistrationMethod.CPU_SIMD;
        ImGui.SameLine();
        HelpMarker("Uses CPU with SIMD acceleration (AVX2). Reliable and fast.");

        if (ImGui.RadioButton("GPU (OpenCL)", _selectedMethod == RegistrationMethod.OpenCL_GPU))
            _selectedMethod = RegistrationMethod.OpenCL_GPU;
        ImGui.SameLine();
        HelpMarker("Uses GPU acceleration via OpenCL. May be faster for large datasets.");
        ImGui.Unindent();
        ImGui.Spacing();

        // Advanced settings
        if (ImGui.CollapsingHeader("Advanced Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();

            ImGui.Text("Maximum Search Range (pixels):");
            ImGui.SetNextItemWidth(150);
            ImGui.SliderInt("##MaxShift", ref _maxShift, 10, 200);
            ImGui.SameLine();
            HelpMarker(
                "Maximum pixel offset to search for optimal alignment. Larger values take longer but may find better alignment.");

            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Output options
        ImGui.Text("Output Options:");
        ImGui.Indent();
        ImGui.Checkbox("Load result directly into workspace", ref _loadDirectly);
        ImGui.Checkbox("Save result to file", ref _saveToFile);

        if (_saveToFile)
        {
            ImGui.Indent();
            ImGui.Text("Export Format:");
            if (ImGui.RadioButton("PNG Slices", _outputFormat == "PNG"))
                _outputFormat = "PNG";
            ImGui.SameLine();
            if (ImGui.RadioButton("TIFF Slices", _outputFormat == "TIF"))
                _outputFormat = "TIF";
            ImGui.Unindent();
        }

        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Validate configuration
        var (isValid, errorMessage) = ValidateConfiguration();

        if (!isValid) ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), $"⚠ Warning: {errorMessage}");

        // Action buttons
        var buttonWidth = 120f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        // Calculate total width based on number of buttons
        var numButtons = _availableDatasets.Count > 2 ? 3 : 2; // Add "Back" if came from selection
        var totalWidth = buttonWidth * numButtons + spacing * (numButtons - 1);
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - totalWidth - ImGui.GetStyle().WindowPadding.X);

        // Back button (only if user came from dataset selection)
        if (_availableDatasets.Count > 2)
        {
            if (ImGui.Button("← Back", new Vector2(buttonWidth, 0)))
            {
                _needsDatasetSelection = true;
                _dataset1 = null;
                _dataset2 = null;
            }

            ImGui.SameLine();
        }

        ImGui.BeginDisabled(!isValid);
        if (ImGui.Button("Start Registration", new Vector2(buttonWidth, 0))) StartRegistration();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0))) _isOpen = false;
    }

    private void DrawProgressView()
    {
        ImGui.TextWrapped("Registration in progress...");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text(_progressStatus);
        ImGui.ProgressBar(_progress, new Vector2(-1, 0));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var buttonWidth = 120f;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - buttonWidth - ImGui.GetStyle().WindowPadding.X);

        if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0))) _cancellationTokenSource?.Cancel();
    }

    private void DrawResultView()
    {
        ImGui.TextWrapped("Registration completed successfully!");
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text("Result Volume:");
        ImGui.Indent();
        ImGui.BulletText($"Dimensions: {_resultVolume.Width}x{_resultVolume.Height}x{_resultVolume.Depth}");
        var sizeInMB = _resultVolume.Width * _resultVolume.Height * _resultVolume.Depth / (1024f * 1024f);
        ImGui.BulletText($"Size: {sizeInMB:F2} MB");
        ImGui.Unindent();
        ImGui.Spacing();

        ImGui.Separator();
        ImGui.Spacing();

        // Action buttons
        var buttonWidth = 150f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        if (_loadDirectly)
        {
            if (ImGui.Button("Load into Workspace", new Vector2(buttonWidth, 0))) LoadResultIntoWorkspace();
            ImGui.SameLine();
        }

        if (_saveToFile)
        {
            if (ImGui.Button("Export to File...", new Vector2(buttonWidth, 0))) OpenExportDialog();
            ImGui.SameLine();
        }

        if (ImGui.Button("Close", new Vector2(buttonWidth, 0)))
        {
            _isOpen = false;
            _resultVolume?.Dispose();
            _resultVolume = null;
        }
    }

    private (bool isValid, string errorMessage) ValidateConfiguration()
    {
        // Ensure datasets are loaded
        if (_dataset1.VolumeData == null)
            _dataset1.Load();
        if (_dataset2.VolumeData == null)
            _dataset2.Load();

        var vol1 = _dataset1.VolumeData;
        var vol2 = _dataset2.VolumeData;

        if (vol1 == null || vol2 == null)
            return (false, "Failed to load volume data");

        // Check dimension compatibility
        switch (_selectedAlignment)
        {
            case RegistrationAlignment.AlongZ:
                if (vol1.Width != vol2.Width || vol1.Height != vol2.Height)
                    return (false, "For Z-alignment, both stacks must have the same Width and Height");
                break;

            case RegistrationAlignment.AlongX:
                if (vol1.Height != vol2.Height || vol1.Depth != vol2.Depth)
                    return (false, "For X-alignment, both stacks must have the same Height and Depth");
                break;

            case RegistrationAlignment.AlongY:
                if (vol1.Width != vol2.Width || vol1.Depth != vol2.Depth)
                    return (false, "For Y-alignment, both stacks must have the same Width and Depth");
                break;
        }

        // Check output options
        if (!_loadDirectly && !_saveToFile)
            return (false, "Select at least one output option");

        return (true, string.Empty);
    }

    private async void StartRegistration()
    {
        _isProcessing = true;
        _progress = 0;
        _progressStatus = "Initializing...";
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var registration = new StackRegistration(_selectedMethod);

            var progressReporter = new Progress<(float progress, string status)>(update =>
            {
                _progress = update.progress;
                _progressStatus = update.status;
            });

            _resultVolume = await registration.RegisterStacksAsync(
                _dataset1,
                _dataset2,
                _selectedAlignment,
                _maxShift,
                progressReporter,
                _cancellationTokenSource.Token);

            _isProcessing = false;

            if (_resultVolume != null)
            {
                Logger.Log("[StackRegistrationDialog] Registration completed successfully");
            }
            else
            {
                Logger.LogError("[StackRegistrationDialog] Registration failed or was cancelled");
                _isOpen = false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[StackRegistrationDialog] Registration failed: {ex.Message}");
            _isProcessing = false;
            _isOpen = false;
        }
    }

    private void LoadResultIntoWorkspace()
    {
        try
        {
            var combinedName = $"{_dataset1.Name}+{_dataset2.Name}";

            // Create a temporary folder for the dataset
            var tempFolder = Path.Combine(Path.GetTempPath(), "GeoscientistToolkit", $"Registered_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempFolder);

            // Save the volume
            var volumePath = Path.Combine(tempFolder, $"{combinedName}.Volume.bin");
            _resultVolume.SaveAsBin(volumePath);

            // Create dataset
            var newDataset = new CtImageStackDataset(combinedName, tempFolder)
            {
                Width = _resultVolume.Width,
                Height = _resultVolume.Height,
                Depth = _resultVolume.Depth,
                PixelSize = _dataset1.PixelSize,
                SliceThickness = _dataset1.SliceThickness,
                Unit = _dataset1.Unit,
                VolumeData = _resultVolume
            };

            // Create empty labels
            var labelPath = Path.Combine(tempFolder, $"{combinedName}.Labels.bin");
            var labels = new ChunkedLabelVolume(
                _resultVolume.Width,
                _resultVolume.Height,
                _resultVolume.Depth,
                _resultVolume.ChunkDim,
                false,
                labelPath);
            labels.SaveAsBin(labelPath);
            labels.Dispose();

            // Add default material
            if (!newDataset.Materials.Any(m => m.ID == 0))
                newDataset.Materials.Add(new Material(0, "Exterior", new Vector4(0, 0, 0, 0)));

            // Save materials
            newDataset.SaveMaterials();

            // Remove original datasets and group
            ProjectManager.Instance.RemoveDataset(_group);

            // Add new dataset
            ProjectManager.Instance.AddDataset(newDataset);

            Logger.Log($"[StackRegistrationDialog] Loaded registered stack into workspace: {combinedName}");

            _isOpen = false;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[StackRegistrationDialog] Failed to load result: {ex.Message}");
        }
    }

    private void OpenExportDialog()
    {
        _exportDialog.SetExtensions(
            new ImGuiExportFileDialog.ExtensionOption("", "Image Stack Folder")
        );
        _exportDialog.Open($"{_dataset1.Name}+{_dataset2.Name}_Registered");
    }

    private async Task ExportResultAsync(string outputPath)
    {
        try
        {
            Logger.Log($"[StackRegistrationDialog] Exporting to: {outputPath}");

            // Create output directory
            var outputDir = Path.GetDirectoryName(outputPath);
            var baseName = Path.GetFileNameWithoutExtension(outputPath);
            var exportFolder = Path.Combine(outputDir, baseName);
            Directory.CreateDirectory(exportFolder);

            _isProcessing = true;
            _progress = 0;
            _progressStatus = "Exporting slices...";

            await Task.Run(() =>
            {
                for (var z = 0; z < _resultVolume.Depth; z++)
                {
                    if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                        break;

                    var slice = new byte[_resultVolume.Width * _resultVolume.Height];
                    _resultVolume.ReadSliceZ(z, slice);

                    var sliceFileName = $"{baseName}_{z:D4}.{_outputFormat.ToLower()}";
                    var slicePath = Path.Combine(exportFolder, sliceFileName);

                    ImageExporter.ExportGrayscaleSlice(slice, _resultVolume.Width, _resultVolume.Height, slicePath);

                    _progress = (float)(z + 1) / _resultVolume.Depth;
                    _progressStatus = $"Exporting slice {z + 1}/{_resultVolume.Depth}";
                }
            });

            _isProcessing = false;
            Logger.Log($"[StackRegistrationDialog] Export completed: {exportFolder}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[StackRegistrationDialog] Export failed: {ex.Message}");
            _isProcessing = false;
        }
    }

    private void HelpMarker(string text)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(400);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}
