// GeoscientistToolkit/Data/CtImageStack/CtImageStackExportDialog.cs

using System.Numerics;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI;
using GeoscientistToolkit.UI.Utils;
using GeoscientistToolkit.Util;
using ImGuiNET;
using StbImageWriteSharp;

namespace GeoscientistToolkit.Data.CtImageStack;

public class CtImageStackExportDialog
{
    private static readonly string[] FormatNames = { "PNG", "TIF", "BMP", "JPEG" };
    private static readonly string[] FormatExtensions = { ".png", ".tif", ".bmp", ".jpg" };
    private readonly ImGuiExportFileDialog _folderDialog;
    private readonly ProgressBarDialog _progressDialog;
    private bool _applyWindowLevel = true;

    // Export settings
    private string _baseFileName = "slice";
    private Dataset _dataset;
    private bool _exportLabels;
    private bool _exportOverlay;
    private bool _isOpen;
    private int _jpegQuality = 90;
    private ILabelVolumeData _labelData;
    private List<Material> _materials;
    private string _selectedFolder = "";
    private int _selectedFormat; // 0=PNG, 1=TIF, 2=BMP, 3=JPEG
    private IGrayscaleVolumeData _volumeData;
    private int _width, _height, _depth;
    private float _windowLevel = 128;
    private float _windowWidth = 255;

    public CtImageStackExportDialog()
    {
        _folderDialog = new ImGuiExportFileDialog("CtStackExportDialog", "Select Export Folder");
        _folderDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption[] { });
        _progressDialog = new ProgressBarDialog("Exporting Image Stack");
    }

    public void Open(CtImageStackDataset dataset)
    {
        _dataset = dataset;
        _volumeData = dataset.VolumeData;
        _labelData = dataset.LabelData;
        _materials = dataset.Materials;
        _width = dataset.Width;
        _height = dataset.Height;
        _depth = dataset.Depth;
        InitializeExport();
    }

    public void Open(StreamingCtVolumeDataset dataset)
    {
        _dataset = dataset;

        // For StreamingCtVolumeDataset, we might need to get data from its editable partner
        if (dataset.EditablePartner != null)
        {
            _volumeData = dataset.EditablePartner.VolumeData;
            _labelData = dataset.EditablePartner.LabelData;
            _materials = dataset.EditablePartner.Materials;
            _width = dataset.EditablePartner.Width;
            _height = dataset.EditablePartner.Height;
            _depth = dataset.EditablePartner.Depth;
        }
        else
        {
            // If no editable partner, try to get directly from streaming dataset
            // This assumes StreamingCtVolumeDataset has these properties
            _volumeData = dataset.VolumeData;
            _labelData = dataset.LabelData;
            _materials = dataset.Materials ?? new List<Material>();
            _width = dataset.Width;
            _height = dataset.Height;
            _depth = dataset.Depth;
        }

        InitializeExport();
    }

    private void InitializeExport()
    {
        _isOpen = true;
        _selectedFolder = "";

        // Initialize window/level from dataset if possible
        _windowLevel = 128;
        _windowWidth = 255;

        // Generate default filename from dataset name
        _baseFileName = SanitizeFileName(_dataset.Name) + "_slice";
    }

    public void Submit()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new Vector2(500, 600), ImGuiCond.FirstUseEver);
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));

        if (ImGui.Begin("Export CT Stack###CtExportDialog", ref _isOpen))
        {
            DrawExportSettings();
            DrawButtons();
            ImGui.End();
        }

        // Handle folder selection dialog
        if (_folderDialog.Submit()) _selectedFolder = _folderDialog.CurrentDirectory;

        // Handle progress dialog
        _progressDialog.Submit();
    }

    private void DrawExportSettings()
    {
        ImGui.Text($"Dataset: {_dataset.Name}");
        ImGui.Text($"Dimensions: {_width} × {_height} × {_depth}");
        ImGui.Separator();

        // Base filename
        ImGui.Text("Base Filename:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##BaseFileName", ref _baseFileName, 256);
        ImGui.TextDisabled("Files will be named: " + _baseFileName + "_0001" + FormatExtensions[_selectedFormat]);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Format selection
        ImGui.Text("Image Format:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##Format", FormatNames[_selectedFormat]))
        {
            for (var i = 0; i < FormatNames.Length; i++)
                if (ImGui.Selectable(FormatNames[i], _selectedFormat == i))
                    _selectedFormat = i;

            ImGui.EndCombo();
        }

        // JPEG quality slider (only visible for JPEG)
        if (_selectedFormat == 3) // JPEG
        {
            ImGui.Text("JPEG Quality:");
            ImGui.SetNextItemWidth(-1);
            ImGui.SliderInt("##JpegQuality", ref _jpegQuality, 1, 100, "%d%%");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Export options - Simplified for clarity
        ImGui.Text("Export Mode:");

        // Radio buttons for export mode
        var exportMode = 0;
        if (!_exportLabels && !_exportOverlay) exportMode = 0;
        else if (_exportLabels && !_exportOverlay) exportMode = 1;
        else if (!_exportLabels && _exportOverlay) exportMode = 2;

        if (ImGui.RadioButton("Grayscale Volume Only", exportMode == 0))
        {
            _exportLabels = false;
            _exportOverlay = false;
        }

        if (_labelData != null && _materials != null && _materials.Count > 1)
        {
            if (ImGui.RadioButton("Label Volume as Material Colors", exportMode == 1))
            {
                _exportLabels = true;
                _exportOverlay = false;
            }

            if (ImGui.RadioButton("Grayscale with Label Overlay", exportMode == 2))
            {
                _exportLabels = false;
                _exportOverlay = true;
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.RadioButton("Label Volume as Material Colors", false);
            ImGui.RadioButton("Grayscale with Label Overlay", false);
            ImGui.EndDisabled();
            ImGui.TextDisabled("No label data available");
        }

        ImGui.Spacing();

        // Window/Level controls only for grayscale modes
        if (exportMode == 0 || exportMode == 2)
        {
            ImGui.Checkbox("Apply Window/Level", ref _applyWindowLevel);
            if (_applyWindowLevel)
            {
                ImGui.Indent();
                ImGui.SetNextItemWidth(150);
                ImGui.DragFloat("Window Level", ref _windowLevel, 1f, 0f, 255f);
                ImGui.SetNextItemWidth(150);
                ImGui.DragFloat("Window Width", ref _windowWidth, 1f, 1f, 255f);
                ImGui.Unindent();
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Preview
        ImGui.Text("Export Preview:");
        var previewSize = _depth * _width * _height * 4; // Approximate size
        ImGui.BulletText($"Number of images: {_depth}");
        ImGui.BulletText($"Image dimensions: {_width} × {_height}");
        ImGui.BulletText($"Estimated total size: ~{FormatFileSize(previewSize)}");

        if (exportMode == 1)
            ImGui.BulletText("Export type: Material-colored labels");
        else if (exportMode == 2)
            ImGui.BulletText("Export type: Grayscale with colored overlay");
        else
            ImGui.BulletText("Export type: Grayscale");

        ImGui.TextDisabled("Note: Actual file sizes may vary based on format and content.");
    }

    private void DrawButtons()
    {
        ImGui.Separator();

        // Show selected folder
        if (!string.IsNullOrEmpty(_selectedFolder))
        {
            ImGui.Text("Export to:");
            ImGui.TextWrapped(_selectedFolder);
            ImGui.Spacing();
        }

        float buttonWidth = 120;
        var totalButtonWidth = buttonWidth * 3 + ImGui.GetStyle().ItemSpacing.X * 2;
        var startX = (ImGui.GetContentRegionAvail().X - totalButtonWidth) * 0.5f;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + startX);

        if (ImGui.Button("Select Folder...", new Vector2(buttonWidth, 0))) _folderDialog.Open();

        ImGui.SameLine();

        var canExport = !string.IsNullOrEmpty(_selectedFolder) && Directory.Exists(_selectedFolder);
        if (!canExport) ImGui.BeginDisabled();

        if (ImGui.Button("Export", new Vector2(buttonWidth, 0)))
            _ = Task.Run(async () => await ExportStackAsync(_selectedFolder));

        if (!canExport) ImGui.EndDisabled();

        ImGui.SameLine();

        if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0))) _isOpen = false;
    }

    private async Task ExportStackAsync(string outputFolder)
    {
        try
        {
            _progressDialog.Open("Preparing export...");

            // Ensure volume data is loaded
            if (_volumeData == null)
            {
                _progressDialog.Update(0.0f, "Loading volume data...");
                await Task.Run(() => _dataset.Load());

                // Re-acquire references after loading
                if (_dataset is CtImageStackDataset ctDataset)
                {
                    _volumeData = ctDataset.VolumeData;
                    _labelData = ctDataset.LabelData;
                    _materials = ctDataset.Materials;
                }
                else if (_dataset is StreamingCtVolumeDataset streamingDataset)
                {
                    if (streamingDataset.EditablePartner != null)
                    {
                        _volumeData = streamingDataset.EditablePartner.VolumeData;
                        _labelData = streamingDataset.EditablePartner.LabelData;
                        _materials = streamingDataset.EditablePartner.Materials;
                    }
                    else
                    {
                        _volumeData = streamingDataset.VolumeData;
                        _labelData = streamingDataset.LabelData;
                        _materials = streamingDataset.Materials ?? new List<Material>();
                    }
                }
            }

            if (_volumeData == null)
            {
                Logger.LogError("[CtImageStackExportDialog] No volume data available for export");
                _progressDialog.Update(1.0f, "Error: No volume data available");
                await Task.Delay(2000);
                _progressDialog.Close();
                _isOpen = false;
                return;
            }

            var baseFilePath = Path.Combine(outputFolder, _baseFileName);
            var extension = FormatExtensions[_selectedFormat];

            Directory.CreateDirectory(outputFolder);

            var totalSlices = _depth;
            var processedSlices = 0;
            var progressLock = new object();

            var maxParallelism = Math.Min(Environment.ProcessorCount, 8);
            var semaphore = new SemaphoreSlim(maxParallelism);
            var tasks = new Task[totalSlices];

            for (var z = 0; z < totalSlices; z++)
            {
                if (_progressDialog.IsCancellationRequested)
                    break;

                var sliceIndex = z;
                tasks[z] = Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var sliceData = new byte[_width * _height];
                        var rgbaData = new byte[_width * _height * 4];
                        var labelData = _exportLabels || _exportOverlay ? new byte[_width * _height] : null;

                        string fileName;

                        if (_exportLabels && !_exportOverlay)
                        {
                            // Export labels only as colored images
                            if (_labelData != null)
                            {
                                _labelData.ReadSliceZ(sliceIndex, labelData);
                                ConvertLabelsToRGBA(labelData, rgbaData);
                                fileName = $"{baseFilePath}_labels_{sliceIndex + 1:D4}{extension}";
                            }
                            else
                            {
                                return;
                            }
                        }
                        else
                        {
                            // Read grayscale data
                            _volumeData.ReadSliceZ(sliceIndex, sliceData);

                            // Apply window/level if requested
                            if (_applyWindowLevel) ApplyWindowLevel(sliceData);

                            // Read label data if needed for overlay
                            if (_exportOverlay && labelData != null && _labelData != null)
                                _labelData.ReadSliceZ(sliceIndex, labelData);

                            // Convert to RGBA
                            ConvertToRGBA(sliceData, labelData, rgbaData, _exportOverlay);
                            fileName = $"{baseFilePath}_{sliceIndex + 1:D4}{extension}";
                        }

                        // Save the image
                        await SaveImageAsync(fileName, rgbaData, _width, _height, _selectedFormat, _jpegQuality);

                        // Update progress
                        lock (progressLock)
                        {
                            processedSlices++;
                            _progressDialog.Update((float)processedSlices / totalSlices,
                                $"Exporting slice {processedSlices} of {totalSlices}...");
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });
            }

            await Task.WhenAll(tasks.Where(t => t != null));

            _progressDialog.Update(1.0f, "Export complete!");
            await Task.Delay(500);

            Logger.Log($"[CtImageStackExportDialog] Successfully exported {totalSlices} slices to {outputFolder}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[CtImageStackExportDialog] Export failed: {ex.Message}");
            _progressDialog.Update(1.0f, $"Export failed: {ex.Message}");
            await Task.Delay(2000);
        }
        finally
        {
            _progressDialog.Close();
            _isOpen = false;
        }
    }

    private void ApplyWindowLevel(byte[] data)
    {
        var min = _windowLevel - _windowWidth / 2;
        var max = _windowLevel + _windowWidth / 2;

        for (var i = 0; i < data.Length; i++)
        {
            float value = data[i];
            value = (value - min) / (max - min) * 255;
            data[i] = (byte)Math.Clamp(value, 0, 255);
        }
    }

    private void ConvertToRGBA(byte[] grayscale, byte[] labels, byte[] rgba, bool applyOverlay)
    {
        for (var i = 0; i < grayscale.Length; i++)
        {
            var gray = grayscale[i];

            if (applyOverlay && labels != null && labels[i] > 0)
            {
                // Apply material color overlay
                var material = _materials?.Find(m => m.ID == labels[i]);
                if (material != null && material.IsVisible)
                {
                    var opacity = 0.5f; // Semi-transparent overlay
                    rgba[i * 4] = (byte)(gray * (1 - opacity) + material.Color.X * 255 * opacity);
                    rgba[i * 4 + 1] = (byte)(gray * (1 - opacity) + material.Color.Y * 255 * opacity);
                    rgba[i * 4 + 2] = (byte)(gray * (1 - opacity) + material.Color.Z * 255 * opacity);
                    rgba[i * 4 + 3] = 255;
                }
                else
                {
                    // No overlay for this material
                    rgba[i * 4] = gray;
                    rgba[i * 4 + 1] = gray;
                    rgba[i * 4 + 2] = gray;
                    rgba[i * 4 + 3] = 255;
                }
            }
            else
            {
                // Standard grayscale
                rgba[i * 4] = gray;
                rgba[i * 4 + 1] = gray;
                rgba[i * 4 + 2] = gray;
                rgba[i * 4 + 3] = 255;
            }
        }
    }

    private void ConvertLabelsToRGBA(byte[] labels, byte[] rgba)
    {
        for (var i = 0; i < labels.Length; i++)
        {
            var labelId = labels[i];

            if (labelId > 0)
            {
                var material = _materials?.Find(m => m.ID == labelId);
                if (material != null)
                {
                    rgba[i * 4] = (byte)(material.Color.X * 255);
                    rgba[i * 4 + 1] = (byte)(material.Color.Y * 255);
                    rgba[i * 4 + 2] = (byte)(material.Color.Z * 255);
                    rgba[i * 4 + 3] = 255;
                }
                else
                {
                    // Default color for unknown labels
                    rgba[i * 4] = 255;
                    rgba[i * 4 + 1] = 0;
                    rgba[i * 4 + 2] = 255;
                    rgba[i * 4 + 3] = 255;
                }
            }
            else
            {
                // Background (exterior)
                rgba[i * 4] = 0;
                rgba[i * 4 + 1] = 0;
                rgba[i * 4 + 2] = 0;
                rgba[i * 4 + 3] = 255;
            }
        }
    }

    private async Task SaveImageAsync(string filePath, byte[] rgbaData, int width, int height, int format,
        int jpegQuality)
    {
        await Task.Run(() =>
        {
            switch (format)
            {
                case 0: // PNG
                    using (var stream = File.Create(filePath))
                    {
                        var writer = new ImageWriter();
                        writer.WritePng(rgbaData, width, height, ColorComponents.RedGreenBlueAlpha, stream);
                    }

                    break;

                case 1: // TIF
                    SimpleTiffWriter.WriteTiff(filePath, rgbaData, width, height);
                    break;

                case 2: // BMP
                    using (var stream = File.Create(filePath))
                    {
                        var writer = new ImageWriter();
                        writer.WriteBmp(rgbaData, width, height, ColorComponents.RedGreenBlueAlpha, stream);
                    }

                    break;

                case 3: // JPEG
                    using (var stream = File.Create(filePath))
                    {
                        var writer = new ImageWriter();
                        writer.WriteJpg(rgbaData, width, height, ColorComponents.RedGreenBlueAlpha, stream,
                            jpegQuality);
                    }

                    break;
            }
        });
    }

    private string SanitizeFileName(string fileName)
    {
        // Remove invalid characters from filename
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = fileName;

        foreach (var c in invalid) sanitized = sanitized.Replace(c, '_');

        return sanitized;
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        var order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}