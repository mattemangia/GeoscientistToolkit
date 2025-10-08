// GeoscientistToolkit/Analysis/ImageAdjustment/BrightnessContrastTool.cs

using System.Numerics;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.ImageAdjustment;

/// <summary>
///     Tool for adjusting brightness and contrast of CT image stacks with real-time preview.
/// </summary>
public class BrightnessContrastTool : IDatasetTools
{
    private bool _autoNormalize;
    private float _brightness; // Range: -100 to +100
    private int _cachedSliceIndex = -1;
    private int _cachedViewType = -1;
    private float _contrast = 1.0f; // Range: 0.1 to 3.0
    private CtImageStackDataset _currentDataset;

    // Histogram data
    private int[] _histogram;
    private bool _isProcessing;
    private float _maxValue = 255;
    private float _meanValue = 128;
    private float _minValue;

    // Cached original data for preview
    private byte[] _originalSliceData;
    private bool _previewEnabled = true;
    private bool _showHistogram;
    private float _stdDev = 50;

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ct) return;

        if (_currentDataset != ct)
        {
            _currentDataset = ct;
            _cachedSliceIndex = -1;
            UpdateHistogram(ct);
        }

        ImGui.SeparatorText("Brightness & Contrast Adjustment");

        // Live preview toggle
        var preview = _previewEnabled;
        if (ImGui.Checkbox("Live Preview", ref preview))
        {
            _previewEnabled = preview;
            if (!_previewEnabled)
                // Reset to original values
                ResetPreview(ct);
        }

        ImGui.Separator();

        // Brightness control
        ImGui.Text("Brightness:");
        var brightness = _brightness;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##Brightness", ref brightness, -100.0f, 100.0f, "%.1f"))
        {
            _brightness = brightness;
            if (_previewEnabled) ApplyPreview(ct);
        }

        // Contrast control
        ImGui.Text("Contrast:");
        var contrast = _contrast;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##Contrast", ref contrast, 0.1f, 3.0f, "%.2f"))
        {
            _contrast = contrast;
            if (_previewEnabled) ApplyPreview(ct);
        }

        ImGui.Separator();

        // Preset buttons
        ImGui.Text("Presets:");
        if (ImGui.Button("Reset", new Vector2(80, 0)))
        {
            _brightness = 0.0f;
            _contrast = 1.0f;
            if (_previewEnabled) ApplyPreview(ct);
        }

        ImGui.SameLine();
        if (ImGui.Button("Brighten", new Vector2(80, 0)))
        {
            _brightness = 20.0f;
            _contrast = 1.2f;
            if (_previewEnabled) ApplyPreview(ct);
        }

        ImGui.SameLine();
        if (ImGui.Button("Darken", new Vector2(80, 0)))
        {
            _brightness = -20.0f;
            _contrast = 1.2f;
            if (_previewEnabled) ApplyPreview(ct);
        }

        if (ImGui.Button("High Contrast", new Vector2(100, 0)))
        {
            _brightness = 0.0f;
            _contrast = 1.5f;
            if (_previewEnabled) ApplyPreview(ct);
        }

        ImGui.SameLine();
        if (ImGui.Button("Low Contrast", new Vector2(100, 0)))
        {
            _brightness = 0.0f;
            _contrast = 0.7f;
            if (_previewEnabled) ApplyPreview(ct);
        }

        ImGui.Separator();

        // Auto-normalize option
        if (ImGui.Checkbox("Auto-Normalize", ref _autoNormalize))
            if (_autoNormalize)
            {
                CalculateAutoLevels(ct);
                if (_previewEnabled) ApplyPreview(ct);
            }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Automatically adjusts brightness and contrast to use full dynamic range");

        // Histogram display
        if (ImGui.Checkbox("Show Histogram", ref _showHistogram))
            if (_showHistogram && _histogram == null)
                UpdateHistogram(ct);

        if (_showHistogram && _histogram != null) DrawHistogram();

        ImGui.Separator();

        // Apply buttons
        if (_isProcessing)
        {
            ImGui.BeginDisabled();
            ImGui.Button("Processing...", new Vector2(-1, 30));
            ImGui.EndDisabled();
        }
        else
        {
            if (_previewEnabled) ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1), "Preview Active");

            if (ImGui.Button("Apply to Dataset", new Vector2(-1, 30))) _ = ApplyToDatasetAsync(ct);

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Permanently applies the brightness/contrast adjustments to the entire dataset");
        }

        // Statistics
        ImGui.Separator();
        ImGui.Text("Statistics:");
        ImGui.Text($"  Min: {_minValue:F1}");
        ImGui.Text($"  Max: {_maxValue:F1}");
        ImGui.Text($"  Mean: {_meanValue:F1}");
        ImGui.Text($"  Std Dev: {_stdDev:F1}");
    }

    private void ApplyPreview(CtImageStackDataset ct)
    {
        // This notifies the viewers to update
        ProjectManager.Instance.NotifyDatasetDataChanged(ct);
    }

    private void ResetPreview(CtImageStackDataset ct)
    {
        _brightness = 0.0f;
        _contrast = 1.0f;
        ProjectManager.Instance.NotifyDatasetDataChanged(ct);
    }

    private void UpdateHistogram(CtImageStackDataset ct)
    {
        if (ct.VolumeData == null) return;

        _histogram = new int[256];
        long totalPixels = 0;
        double sum = 0;
        double sumSquared = 0;
        _minValue = 255;
        _maxValue = 0;

        // Sample every 10th slice for performance
        var step = Math.Max(1, ct.Depth / 10);
        for (var z = 0; z < ct.Depth; z += step)
        {
            var slice = new byte[ct.Width * ct.Height];
            ct.VolumeData.ReadSliceZ(z, slice);

            foreach (var value in slice)
            {
                _histogram[value]++;
                totalPixels++;
                sum += value;
                sumSquared += value * value;
                if (value < _minValue) _minValue = value;
                if (value > _maxValue) _maxValue = value;
            }
        }

        if (totalPixels > 0)
        {
            _meanValue = (float)(sum / totalPixels);
            var variance = sumSquared / totalPixels - _meanValue * _meanValue;
            _stdDev = (float)Math.Sqrt(Math.Max(0, variance));
        }
    }

    private void DrawHistogram()
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, 100);

        // Background
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1)));

        if (_histogram != null)
        {
            // Find max count for scaling
            var maxCount = 0;
            for (var i = 0; i < 256; i++)
                if (_histogram[i] > maxCount)
                    maxCount = _histogram[i];

            if (maxCount > 0)
            {
                // Draw histogram bars
                var barWidth = size.X / 256.0f;
                for (var i = 0; i < 256; i++)
                {
                    var height = _histogram[i] / (float)maxCount * size.Y;
                    var barPos = pos + new Vector2(i * barWidth, size.Y - height);
                    var barEnd = barPos + new Vector2(barWidth, height);

                    // Apply brightness/contrast to preview color
                    var adjustedValue = ApplyAdjustment(i / 255.0f);
                    var color = ImGui.GetColorU32(new Vector4(adjustedValue, adjustedValue, adjustedValue, 1));

                    drawList.AddRectFilled(barPos, barEnd, color);
                }

                // Draw mean line
                var meanX = pos.X + _meanValue / 255.0f * size.X;
                drawList.AddLine(new Vector2(meanX, pos.Y), new Vector2(meanX, pos.Y + size.Y),
                    ImGui.GetColorU32(new Vector4(1, 0.5f, 0, 1)), 2.0f);
            }
        }

        // Border
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1)));

        ImGui.Dummy(size);
    }

    private void CalculateAutoLevels(CtImageStackDataset ct)
    {
        if (_histogram == null) UpdateHistogram(ct);

        // Find 1% and 99% percentiles
        long totalPixels = 0;
        foreach (var count in _histogram) totalPixels += count;

        var threshold1 = totalPixels / 100; // 1%
        var threshold99 = totalPixels * 99 / 100; // 99%

        int min = 0, max = 255;
        long cumulative = 0;

        for (var i = 0; i < 256; i++)
        {
            cumulative += _histogram[i];
            if (cumulative >= threshold1)
            {
                min = i;
                break;
            }
        }

        cumulative = 0;
        for (var i = 255; i >= 0; i--)
        {
            cumulative += _histogram[i];
            if (cumulative >= totalPixels - threshold99)
            {
                max = i;
                break;
            }
        }

        // Calculate brightness and contrast to map [min, max] to [0, 255]
        float range = max - min;
        if (range > 0)
        {
            _contrast = 255.0f / range;
            _brightness = -min * _contrast;
        }

        Logger.Log(
            $"[BrightnessContrast] Auto-levels: min={min}, max={max}, brightness={_brightness:F1}, contrast={_contrast:F2}");
    }

    private float ApplyAdjustment(float value)
    {
        // Apply contrast and brightness
        var adjusted = (value - 0.5f) * _contrast + 0.5f + _brightness / 255.0f;
        return Math.Clamp(adjusted, 0.0f, 1.0f);
    }

    /// <summary>
    ///     Gets the adjusted value for a single byte (used by viewers)
    /// </summary>
    public static byte GetAdjustedValue(byte original, float brightness, float contrast)
    {
        var normalized = original / 255.0f;
        var adjusted = (normalized - 0.5f) * contrast + 0.5f + brightness / 255.0f;
        return (byte)Math.Clamp(adjusted * 255.0f, 0.0f, 255.0f);
    }

    private async Task ApplyToDatasetAsync(CtImageStackDataset ct)
    {
        if (_isProcessing || ct.VolumeData == null) return;

        _isProcessing = true;
        _previewEnabled = false; // Disable preview during processing

        try
        {
            Logger.Log(
                $"[BrightnessContrast] Applying adjustments: brightness={_brightness:F1}, contrast={_contrast:F2}");

            await Task.Run(() =>
            {
                for (var z = 0; z < ct.Depth; z++)
                {
                    var slice = new byte[ct.Width * ct.Height];
                    ct.VolumeData.ReadSliceZ(z, slice);

                    // Apply adjustments
                    for (var i = 0; i < slice.Length; i++)
                        slice[i] = GetAdjustedValue(slice[i], _brightness, _contrast);

                    ct.VolumeData.WriteSliceZ(z, slice);
                }
            });

            // Save the modified volume
            if (ct.VolumeData is ChunkedVolume chunkedVolume)
            {
                var volumePath = Path.Combine(
                    Path.GetDirectoryName(ct.FilePath),
                    Path.GetFileNameWithoutExtension(ct.FilePath) + ".Volume.bin"
                );
                chunkedVolume.SaveAsBin(volumePath);
                Logger.Log($"[BrightnessContrast] Saved adjusted volume to {volumePath}");
            }

            // Reset adjustment values
            _brightness = 0.0f;
            _contrast = 1.0f;

            // Update histogram
            UpdateHistogram(ct);

            // Notify of changes
            ProjectManager.Instance.NotifyDatasetDataChanged(ct);
            ProjectManager.Instance.HasUnsavedChanges = true;
        }
        finally
        {
            _isProcessing = false;
        }
    }
}

/// <summary>
///     Helper class for viewers to get current brightness/contrast settings
/// </summary>
public static class BrightnessContrastHelper
{
    private static readonly Dictionary<CtImageStackDataset, (float brightness, float contrast)> _settings = new();

    public static void UpdateSettings(CtImageStackDataset dataset, float brightness, float contrast)
    {
        _settings[dataset] = (brightness, contrast);
    }

    public static (float brightness, float contrast) GetSettings(CtImageStackDataset dataset)
    {
        return _settings.TryGetValue(dataset, out var settings) ? settings : (0.0f, 1.0f);
    }

    public static byte AdjustPixel(byte value, CtImageStackDataset dataset)
    {
        var (brightness, contrast) = GetSettings(dataset);
        if (Math.Abs(brightness) < 0.01f && Math.Abs(contrast - 1.0f) < 0.01f)
            return value;

        return BrightnessContrastTool.GetAdjustedValue(value, brightness, contrast);
    }
}