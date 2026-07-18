// GAIA/Data/CtImageStack/CtImageStackExportTool.cs

using System.Numerics;
using GAIA.Data.VolumeData;
using GAIA.UI;
using GAIA.UI.Interfaces;
using GAIA.UI.Utils;
using GAIA.Util;
using ImGuiNET;
using StbImageWriteSharp;

namespace GAIA.Data.CtImageStack;

/// <summary>
///     Export tool for CT image stacks. Writes the grayscale (BW) volume as 8-bit
///     single-channel TIFF/PNG slices, and the label volume as material-colored
///     slice images. Uses StbImageWriteSharp and SimpleTiffWriter only.
/// </summary>
public class CtImageStackExportTool : IDatasetTools
{
    private const int ModeGrayscale = 0;
    private const int ModeLabels = 1;

    private static readonly string[] FormatNames = { "TIFF (.tif)", "PNG (.png)" };
    private static readonly string[] FormatExtensions = { ".tif", ".png" };

    private readonly ImGuiExportFileDialog _folderDialog;
    private readonly ProgressBarDialog _progressDialog;

    private bool _applyWindowLevel;
    private string _baseFileName = "slice";
    private CtImageStackDataset _currentDataset;
    private int _exportMode = ModeGrayscale;
    private volatile bool _isExporting;
    private string _lastResult = "";
    private bool _lastResultIsError;
    private string _outputFolder = "";
    private int _selectedFormat; // 0=TIFF, 1=PNG
    private float _windowLevel = 128;
    private float _windowWidth = 255;

    public CtImageStackExportTool()
    {
        _folderDialog = new ImGuiExportFileDialog("CtStackExportToolFolder", "Select Export Folder");
        _folderDialog.SetExtensions(new ImGuiExportFileDialog.ExtensionOption[] { });
        _progressDialog = new ProgressBarDialog("Exporting Image Stack");
    }

    public void Draw(Dataset dataset)
    {
        var ctDataset = dataset as CtImageStackDataset ?? (dataset as StreamingCtVolumeDataset)?.EditablePartner;
        if (ctDataset == null)
        {
            ImGui.TextDisabled("Image stack export is available for CT Image Stack datasets.");
            return;
        }

        if (!ReferenceEquals(ctDataset, _currentDataset))
        {
            _currentDataset = ctDataset;
            _baseFileName = SanitizeFileName(ctDataset.Name);
            _lastResult = "";
        }

        ImGui.Text($"Dataset: {ctDataset.Name}");
        ImGui.Text($"Dimensions: {ctDataset.Width} × {ctDataset.Height} × {ctDataset.Depth}");
        ImGui.Separator();
        ImGui.Spacing();

        var segmentedMaterials = ctDataset.Materials?.Where(m => m.ID != 0).ToList();
        var hasLabels = ctDataset.LabelData != null && segmentedMaterials is { Count: > 0 };

        ImGui.SeparatorText("Content");

        ImGui.RadioButton("Grayscale (BW) CT slices", ref _exportMode, ModeGrayscale);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Export the raw CT volume as 8-bit grayscale slice images.");

        if (!hasLabels) ImGui.BeginDisabled();
        ImGui.RadioButton("Label stack (material colors)", ref _exportMode, ModeLabels);
        if (!hasLabels)
        {
            ImGui.EndDisabled();
            ImGui.TextDisabled("No label data available. Segment materials first.");
            _exportMode = ModeGrayscale;
        }
        else if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Export segmented labels as colored slice images using material colors.");
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Format");

        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##ExportFormat", ref _selectedFormat, FormatNames, FormatNames.Length);

        if (_exportMode == ModeGrayscale)
        {
            ImGui.Spacing();
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
        ImGui.SeparatorText("Output");

        ImGui.Text("Base Filename:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##ExportBaseFileName", ref _baseFileName, 256);

        var suffix = _exportMode == ModeLabels ? "_labels" : "";
        ImGui.TextDisabled($"Files: {_baseFileName}{suffix}_0001{FormatExtensions[_selectedFormat]} …");

        ImGui.Spacing();
        if (ImGui.Button("Select Output Folder...", new Vector2(-1, 0))) _folderDialog.Open();

        if (!string.IsNullOrEmpty(_outputFolder))
        {
            ImGui.Text("Export to:");
            ImGui.TextWrapped(_outputFolder);
        }
        else
        {
            ImGui.TextDisabled("No output folder selected.");
        }

        ImGui.Spacing();

        var canExport = !_isExporting && !string.IsNullOrEmpty(_outputFolder) && Directory.Exists(_outputFolder) &&
                        !string.IsNullOrWhiteSpace(_baseFileName);
        if (!canExport) ImGui.BeginDisabled();

        var buttonLabel = _exportMode == ModeLabels
            ? $"Export Label Stack ({ctDataset.Depth} slices)"
            : $"Export Grayscale Stack ({ctDataset.Depth} slices)";
        if (ImGui.Button(buttonLabel, new Vector2(-1, 0)))
        {
            var mode = _exportMode;
            var format = _selectedFormat;
            var folder = _outputFolder;
            var baseName = SanitizeFileName(_baseFileName);
            var applyWindowLevel = _applyWindowLevel;
            var windowLevel = _windowLevel;
            var windowWidth = _windowWidth;
            var target = ctDataset;

            _isExporting = true;
            _lastResult = "";
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExportStackAsync(target, mode, format, folder, baseName,
                        applyWindowLevel, windowLevel, windowWidth);
                }
                finally
                {
                    _isExporting = false;
                }
            });
        }

        if (!canExport) ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(_lastResult))
        {
            var color = _lastResultIsError ? new Vector4(1f, .35f, .35f, 1f) : new Vector4(0.3f, 1f, 0.3f, 1f);
            ImGui.TextColored(color, _lastResult);
        }

        // Handle folder selection and progress dialogs
        if (_folderDialog.Submit()) _outputFolder = _folderDialog.CurrentDirectory;
        _progressDialog.Submit();
    }

    private async Task ExportStackAsync(CtImageStackDataset dataset, int mode, int format, string outputFolder,
        string baseFileName, bool applyWindowLevel, float windowLevel, float windowWidth)
    {
        try
        {
            _progressDialog.Open("Preparing export...");

            if (dataset.VolumeData == null)
            {
                _progressDialog.Update(0.0f, "Loading volume data...");
                await Task.Run(() => dataset.Load());
            }

            IGrayscaleVolumeData volumeData = dataset.VolumeData;
            ILabelVolumeData labelData = dataset.LabelData;

            if (volumeData == null)
            {
                SetError("No volume data available for export.");
                return;
            }

            if (mode == ModeLabels && labelData == null)
            {
                SetError("No label data available for export.");
                return;
            }

            var width = volumeData.Width;
            var height = volumeData.Height;
            var depth = volumeData.Depth;

            // Flat lookup table so the per-pixel color mapping avoids a list search.
            var colorLut = mode == ModeLabels ? BuildMaterialColorLut(dataset.Materials) : null;

            Directory.CreateDirectory(outputFolder);
            var baseFilePath = Path.Combine(outputFolder, baseFileName);
            var extension = FormatExtensions[format];

            var processedSlices = 0;
            var maxParallelism = Math.Min(Environment.ProcessorCount, 8);
            var semaphore = new SemaphoreSlim(maxParallelism);
            var tasks = new List<Task>(depth);

            for (var z = 0; z < depth; z++)
            {
                if (_progressDialog.IsCancellationRequested) break;

                var sliceIndex = z;
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        if (_progressDialog.IsCancellationRequested) return;

                        if (mode == ModeLabels)
                        {
                            var labels = new byte[width * height];
                            labelData.ReadSliceZ(sliceIndex, labels);

                            var rgba = new byte[width * height * 4];
                            ConvertLabelsToRgba(labels, rgba, colorLut);

                            var fileName = $"{baseFilePath}_labels_{sliceIndex + 1:D4}{extension}";
                            SaveRgbaImage(fileName, rgba, width, height, format);
                        }
                        else
                        {
                            var gray = new byte[width * height];
                            volumeData.ReadSliceZ(sliceIndex, gray);

                            if (applyWindowLevel) ApplyWindowLevel(gray, windowLevel, windowWidth);

                            var fileName = $"{baseFilePath}_{sliceIndex + 1:D4}{extension}";
                            SaveGrayscaleImage(fileName, gray, width, height, format);
                        }

                        var done = Interlocked.Increment(ref processedSlices);
                        _progressDialog.Update((float)done / depth, $"Exporting slice {done} of {depth}...");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            if (_progressDialog.IsCancellationRequested)
            {
                _lastResult = $"Export cancelled after {processedSlices} of {depth} slices.";
                _lastResultIsError = false;
                Logger.Log($"[CtImageStackExportTool] Export cancelled after {processedSlices}/{depth} slices");
            }
            else
            {
                _progressDialog.Update(1.0f, "Export complete!");
                await Task.Delay(500);
                _lastResult = $"Exported {processedSlices} slices to {outputFolder}";
                _lastResultIsError = false;
                Logger.Log($"[CtImageStackExportTool] Successfully exported {processedSlices} slices to {outputFolder}");
            }
        }
        catch (Exception ex)
        {
            SetError($"Export failed: {ex.Message}");
            Logger.LogError($"[CtImageStackExportTool] Export failed: {ex}");
        }
        finally
        {
            _progressDialog.Close();
        }
    }

    private void SetError(string message)
    {
        _lastResult = message;
        _lastResultIsError = true;
    }

    private static void ApplyWindowLevel(byte[] data, float level, float width)
    {
        var min = level - width / 2;
        var max = level + width / 2;

        for (var i = 0; i < data.Length; i++)
        {
            float value = data[i];
            value = (value - min) / (max - min) * 255;
            data[i] = (byte)Math.Clamp(value, 0, 255);
        }
    }

    private static byte[] BuildMaterialColorLut(List<Material> materials)
    {
        // Index = label ID * 4 (RGBA). Unknown labels get magenta, background stays black.
        var lut = new byte[256 * 4];
        for (var id = 1; id < 256; id++)
        {
            lut[id * 4] = 255;
            lut[id * 4 + 2] = 255;
            lut[id * 4 + 3] = 255;
        }

        if (materials != null)
            foreach (var material in materials)
            {
                if (material.ID == 0) continue;
                lut[material.ID * 4] = (byte)(material.Color.X * 255);
                lut[material.ID * 4 + 1] = (byte)(material.Color.Y * 255);
                lut[material.ID * 4 + 2] = (byte)(material.Color.Z * 255);
                lut[material.ID * 4 + 3] = 255;
            }

        lut[3] = 255; // Opaque black background for label ID 0
        return lut;
    }

    private static void ConvertLabelsToRgba(byte[] labels, byte[] rgba, byte[] colorLut)
    {
        for (var i = 0; i < labels.Length; i++)
        {
            var lutIndex = labels[i] * 4;
            rgba[i * 4] = colorLut[lutIndex];
            rgba[i * 4 + 1] = colorLut[lutIndex + 1];
            rgba[i * 4 + 2] = colorLut[lutIndex + 2];
            rgba[i * 4 + 3] = colorLut[lutIndex + 3];
        }
    }

    private static void SaveGrayscaleImage(string filePath, byte[] grayData, int width, int height, int format)
    {
        switch (format)
        {
            case 0: // TIFF
                SimpleTiffWriter.WriteTiffGrayscale8(filePath, grayData, width, height);
                break;

            case 1: // PNG
                using (var stream = File.Create(filePath))
                {
                    new ImageWriter().WritePng(grayData, width, height, ColorComponents.Grey, stream);
                }

                break;
        }
    }

    private static void SaveRgbaImage(string filePath, byte[] rgbaData, int width, int height, int format)
    {
        switch (format)
        {
            case 0: // TIFF
                SimpleTiffWriter.WriteTiff(filePath, rgbaData, width, height);
                break;

            case 1: // PNG
                using (var stream = File.Create(filePath))
                {
                    new ImageWriter().WritePng(rgbaData, width, height, ColorComponents.RedGreenBlueAlpha, stream);
                }

                break;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = fileName ?? "";

        foreach (var c in invalid) sanitized = sanitized.Replace(c, '_');

        return sanitized;
    }
}
