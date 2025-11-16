// GeoscientistToolkit/Analysis/AmbientOcclusionSegmentation/AmbientOcclusionTool.cs

using System.Numerics;
using GeoscientistToolkit.Data;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.UI.Interfaces;
using GeoscientistToolkit.Util;
using ImGuiNET;

namespace GeoscientistToolkit.Analysis.AmbientOcclusionSegmentation;

/// <summary>
/// UI Tool for ambient occlusion-based cavity and pore segmentation
/// </summary>
public class AmbientOcclusionTool : IDatasetTools, IDisposable
{
    private AmbientOcclusionSegmentation _processor;
    private AmbientOcclusionSettings _settings = new();
    private AmbientOcclusionResult _currentResult;
    private AmbientOcclusionPreview _preview = new();

    private CancellationTokenSource _cts;
    private Task _computeTask;
    private bool _isProcessing;
    private string _statusMessage = "Ready";

    // UI state
    private int _selectedMaterialIndex = 1;
    private bool _autoUpdate = false;
    private float _lastUpdateTime = 0;
    private const float AutoUpdateDelay = 1.0f; // 1 second delay for auto-update

    // Binarization
    private ThresholdMethod _thresholdMethod = ThresholdMethod.Otsu;
    private int[] _histogram;
    private HistogramStats _histogramStats;
    private bool _showHistogram = false;

    // Source material mode
    private bool _useExistingMaterial = false;
    private int _sourceMaterialIndex = 0;

    private CtImageStackDataset _lastDataset;

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _processor?.Dispose();

        // Unregister preview
        if (_lastDataset != null)
        {
            AmbientOcclusionIntegration.UnregisterPreview(_lastDataset);
        }
    }

    public void Draw(Dataset dataset)
    {
        if (dataset is not CtImageStackDataset ctDataset)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "This tool requires a CT Image Stack dataset.");
            return;
        }

        // Track dataset changes
        if (_lastDataset != ctDataset)
        {
            if (_lastDataset != null)
            {
                AmbientOcclusionIntegration.UnregisterPreview(_lastDataset);
            }
            _lastDataset = ctDataset;
        }

        // Initialize processor on first use
        if (_processor == null)
        {
            _processor = new AmbientOcclusionSegmentation();
        }

        DrawHeader();
        ImGui.Separator();

        DrawSettings(ctDataset);
        ImGui.Separator();

        DrawPreviewOptions();
        ImGui.Separator();

        DrawProcessingControls(ctDataset);
        ImGui.Separator();

        DrawStatus();

        if (_currentResult != null)
        {
            ImGui.Separator();
            DrawResults();
        }
    }

    private void DrawHeader()
    {
        ImGui.TextColored(new Vector4(0.2f, 0.8f, 1.0f, 1), "Ambient Occlusion Segmentation");
        ImGui.TextWrapped("Segment pores and cavities using ambient occlusion. " +
                         "This method works even when cavities have the same grayscale value as surrounding material.");

        if (ImGui.Button("?##help"))
        {
            ImGui.OpenPopup("ao_help");
        }
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1),
            "Based on: Baum & Titschack (2016) - ZIB Report ZR-16-17");

        if (ImGui.BeginPopup("ao_help"))
        {
            ImGui.Text("Ambient Occlusion Segmentation Help");
            ImGui.Separator();
            ImGui.TextWrapped("This tool casts rays from each voxel to compute ambient occlusion values.");
            ImGui.TextWrapped("• Pores/cavities have LOW AO (rays escape)");
            ImGui.TextWrapped("• Solid material has HIGH AO (rays blocked)");
            ImGui.Spacing();
            ImGui.TextWrapped("Parameters:");
            ImGui.BulletText("Ray Count: More rays = better accuracy, slower (64-256)");
            ImGui.BulletText("Ray Length: Longer = capture larger features (voxels)");
            ImGui.BulletText("Material Threshold: Grayscale value above = material");
            ImGui.BulletText("AO Threshold: Voxels below this are segmented as pores");
            ImGui.EndPopup();
        }
    }

    private void DrawSettings(CtImageStackDataset dataset)
    {
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Algorithm Parameters");

        // Ray count
        int rayCount = _settings.RayCount;
        if (ImGui.SliderInt("Ray Count", ref rayCount, 32, 512))
        {
            _settings.RayCount = rayCount;
            if (_autoUpdate) TriggerAutoUpdate();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Number of rays cast per voxel. More rays = better accuracy but slower.");

        // Ray length
        float rayLength = _settings.RayLength;
        float maxRayLength = Math.Min(dataset.Width, Math.Min(dataset.Height, dataset.Depth)) / 2f;
        if (ImGui.SliderFloat("Ray Length", ref rayLength, 5f, maxRayLength))
        {
            _settings.RayLength = rayLength;
            if (_autoUpdate) TriggerAutoUpdate();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Maximum ray length in voxels. Longer rays capture larger-scale features.");

        // Material threshold with auto-detection
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1), "Binarization");

        // Mode selection: Grayscale threshold or existing material
        bool useExisting = _useExistingMaterial;
        if (ImGui.Checkbox("Analyze cavities in existing material", ref useExisting))
        {
            _useExistingMaterial = useExisting;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Find empty spaces/pores within an already segmented material");

        if (_useExistingMaterial)
        {
            // Select source material
            ImGui.Text("Source Material:");
            ImGui.SameLine();

            if (dataset.Materials.Count > 0)
            {
                _sourceMaterialIndex = Math.Clamp(_sourceMaterialIndex, 0, dataset.Materials.Count - 1);
                var sourceMaterial = dataset.Materials[_sourceMaterialIndex];

                var color = sourceMaterial.Color;
                ImGui.ColorButton("##srccolor", color, ImGuiColorEditFlags.NoAlpha, new Vector2(20, 20));
                ImGui.SameLine();

                if (ImGui.BeginCombo("##srcmaterial", sourceMaterial.Name))
                {
                    for (int i = 0; i < dataset.Materials.Count; i++)
                    {
                        var mat = dataset.Materials[i];
                        bool isSelected = i == _sourceMaterialIndex;

                        ImGui.ColorButton($"##srccolor{i}", mat.Color, ImGuiColorEditFlags.NoAlpha, new Vector2(20, 20));
                        ImGui.SameLine();

                        if (ImGui.Selectable(mat.Name, isSelected))
                        {
                            _sourceMaterialIndex = i;
                        }

                        if (isSelected)
                            ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No materials available");
            }
        }
        else
        {
            // Grayscale threshold mode
            int materialThreshold = _settings.MaterialThreshold;
        if (ImGui.SliderInt("Material Threshold", ref materialThreshold, 0, 255))
        {
            _settings.MaterialThreshold = (byte)materialThreshold;
            if (_autoUpdate) TriggerAutoUpdate();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Grayscale threshold: voxels above this are considered material.");

        // Auto-detect threshold button
        ImGui.SameLine();
        if (ImGui.Button("Auto##threshold"))
        {
            AutoDetectThreshold(dataset);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Automatically detect optimal threshold");

        // Threshold method dropdown
        if (ImGui.BeginCombo("Method##threshold", _thresholdMethod.ToString()))
        {
            foreach (ThresholdMethod method in Enum.GetValues(typeof(ThresholdMethod)))
            {
                if (method == ThresholdMethod.Manual) continue;

                bool isSelected = _thresholdMethod == method;
                if (ImGui.Selectable(method.ToString(), isSelected))
                {
                    _thresholdMethod = method;
                }
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // Show histogram button
        ImGui.SameLine();
        if (ImGui.Button(_showHistogram ? "Hide Histogram" : "Show Histogram"))
        {
            _showHistogram = !_showHistogram;
            if (_showHistogram && _histogram == null)
            {
                ComputeHistogram(dataset);
            }
        }

        // Draw histogram if visible
        if (_showHistogram && _histogram != null)
        {
            DrawHistogram();
        }

        }

        ImGui.Spacing();

        // AO threshold for segmentation
        float aoThreshold = _settings.SegmentationThreshold;
        if (ImGui.SliderFloat("AO Threshold", ref aoThreshold, 0f, 1f))
        {
            _settings.SegmentationThreshold = aoThreshold;
            if (_currentResult != null)
            {
                // Re-threshold existing result
                UpdateSegmentationMask();
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("AO threshold for segmentation. Voxels with AO below this are segmented as pores.");

        // Target material selection
        ImGui.Text("Target Material:");
        ImGui.SameLine();

        if (dataset.Materials.Count > 0)
        {
            _selectedMaterialIndex = Math.Clamp(_selectedMaterialIndex, 0, dataset.Materials.Count - 1);
            var selectedMaterial = dataset.Materials[_selectedMaterialIndex];

            var color = selectedMaterial.Color;
            ImGui.ColorButton("##color", color, ImGuiColorEditFlags.NoAlpha, new Vector2(20, 20));
            ImGui.SameLine();

            if (ImGui.BeginCombo("##material", selectedMaterial.Name))
            {
                for (int i = 0; i < dataset.Materials.Count; i++)
                {
                    var mat = dataset.Materials[i];
                    bool isSelected = i == _selectedMaterialIndex;

                    ImGui.ColorButton($"##color{i}", mat.Color, ImGuiColorEditFlags.NoAlpha, new Vector2(20, 20));
                    ImGui.SameLine();

                    if (ImGui.Selectable(mat.Name, isSelected))
                    {
                        _selectedMaterialIndex = i;
                        _settings.TargetMaterialId = mat.ID;
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            _settings.TargetMaterialId = selectedMaterial.ID;
        }
        else
        {
            ImGui.TextColored(new Vector4(1, 0.5f, 0, 1), "No materials defined");
        }

        // Acceleration settings
        ImGui.Spacing();
        bool useGpu = _settings.UseGpu;
        if (ImGui.Checkbox("Use GPU Acceleration", ref useGpu))
        {
            _settings.UseGpu = useGpu;
        }

        var (statusMsg, statusColor) = _processor.GetAccelerationStatus();
        ImGui.SameLine();
        ImGui.TextColored(statusColor, statusMsg);

        if (!useGpu)
        {
            int cpuThreads = _settings.CpuThreads;
            if (ImGui.SliderInt("CPU Threads", ref cpuThreads, 1, Environment.ProcessorCount))
            {
                _settings.CpuThreads = cpuThreads;
            }
        }
    }

    private void DrawPreviewOptions()
    {
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Preview Options");

        bool showPreview = _preview.ShowPreview;
        if (ImGui.Checkbox("Show Preview Overlay", ref showPreview))
        {
            _preview.ShowPreview = showPreview;
            UpdatePreviewRegistration();
        }

        if (_preview.ShowPreview)
        {
            ImGui.Indent();

            bool showAo = _preview.ShowAoField;
            if (ImGui.Checkbox("Show AO Heatmap", ref showAo))
            {
                _preview.ShowAoField = showAo;
            }

            bool showMask = _preview.ShowSegmentationMask;
            if (ImGui.Checkbox("Show Segmentation Mask", ref showMask))
            {
                _preview.ShowSegmentationMask = showMask;
            }

            if (showMask)
            {
                Vector4 overlayColor = _preview.OverlayColor;
                if (ImGui.ColorEdit3("Overlay Color", ref overlayColor))
                {
                    _preview.OverlayColor = overlayColor;
                }

                float opacity = _preview.OverlayOpacity;
                if (ImGui.SliderFloat("Overlay Opacity", ref opacity, 0f, 1f))
                {
                    _preview.OverlayOpacity = opacity;
                }
            }

            bool showStats = _preview.ShowStatistics;
            if (ImGui.Checkbox("Show Statistics", ref showStats))
            {
                _preview.ShowStatistics = showStats;
            }

            ImGui.Unindent();
        }
    }

    private void DrawProcessingControls(CtImageStackDataset dataset)
    {
        ImGui.TextColored(new Vector4(1, 1, 0, 1), "Processing");

        bool canCompute = !_isProcessing && dataset.VolumeData != null;

        if (!canCompute)
            ImGui.BeginDisabled();

        if (ImGui.Button("Compute Ambient Occlusion", new Vector2(-1, 30)))
        {
            StartComputation(dataset);
        }

        if (!canCompute)
            ImGui.EndDisabled();

        if (_isProcessing)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                CancelComputation();
            }
        }

        // Auto-update option
        ImGui.Checkbox("Auto-update on parameter change", ref _autoUpdate);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Automatically recompute when parameters change (with 1s delay)");

        ImGui.Spacing();

        // Apply to labels button
        bool canApply = _currentResult != null && !_isProcessing && dataset.Materials.Count > 0;

        if (!canApply)
            ImGui.BeginDisabled();

        if (ImGui.Button("Apply to Labels", new Vector2(-1, 25)))
        {
            ApplySegmentation(dataset);
        }

        if (!canApply)
            ImGui.EndDisabled();
    }

    private void DrawStatus()
    {
        if (_isProcessing)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "Processing...");

            float progress = _processor.Progress;
            ImGui.ProgressBar(progress, new Vector2(-1, 0), $"{(int)(progress * 100)}%");

            string stage = _processor.CurrentStage;
            ImGui.Text($"Stage: {stage}");
        }
        else
        {
            var color = _statusMessage.Contains("Error") || _statusMessage.Contains("Failed")
                ? new Vector4(1, 0, 0, 1)
                : new Vector4(0, 1, 0, 1);

            ImGui.TextColored(color, $"Status: {_statusMessage}");
        }
    }

    private void DrawResults()
    {
        ImGui.TextColored(new Vector4(0, 1, 0, 1), "Results");

        var result = _currentResult;

        ImGui.Text($"Processing Time: {result.ProcessingTime:F2} seconds");
        ImGui.Text($"Throughput: {result.VoxelsPerSecond / 1_000_000.0:F2} M voxels/sec");
        ImGui.Text($"Acceleration: {result.AccelerationType}");

        // Calculate porosity
        if (result.SegmentationMask != null)
        {
            var mask = result.SegmentationMask;
            long totalVoxels = mask.GetLength(0) * mask.GetLength(1) * mask.GetLength(2);
            long poreVoxels = 0;

            for (int z = 0; z < mask.GetLength(2); z++)
            {
                for (int y = 0; y < mask.GetLength(1); y++)
                {
                    for (int x = 0; x < mask.GetLength(0); x++)
                    {
                        if (mask[x, y, z])
                            poreVoxels++;
                    }
                }
            }

            float porosity = (float)poreVoxels / totalVoxels * 100f;
            ImGui.Text($"Porosity: {porosity:F2}%");
            ImGui.Text($"Pore Voxels: {poreVoxels:N0} / {totalVoxels:N0}");
        }
    }

    private void StartComputation(CtImageStackDataset dataset)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _isProcessing = true;
        _statusMessage = "Computing...";

        // Update settings based on mode
        _settings.UseExistingMaterial = _useExistingMaterial;
        if (_useExistingMaterial && dataset.Materials.Count > 0)
        {
            _sourceMaterialIndex = Math.Clamp(_sourceMaterialIndex, 0, dataset.Materials.Count - 1);
            _settings.SourceMaterialId = dataset.Materials[_sourceMaterialIndex].ID;
        }

        var token = _cts.Token;

        _computeTask = Task.Run(() =>
        {
            try
            {
                var result = _processor.ComputeAmbientOcclusion(dataset, _settings, token);

                if (!token.IsCancellationRequested)
                {
                    _currentResult = result;
                    _preview.Result = result;
                    _statusMessage = "Computation completed successfully";

                    UpdatePreviewRegistration();

                    Logger.Log($"[AmbientOcclusion] Completed in {result.ProcessingTime:F2}s " +
                              $"({result.VoxelsPerSecond / 1_000_000.0:F2} Mvox/s) using {result.AccelerationType}");
                }
            }
            catch (Exception ex)
            {
                _statusMessage = $"Error: {ex.Message}";
                Logger.LogError($"[AmbientOcclusion] {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }, token);
    }

    private void CancelComputation()
    {
        _cts?.Cancel();
        _statusMessage = "Cancelled";
        _isProcessing = false;
    }

    private void ApplySegmentation(CtImageStackDataset dataset)
    {
        if (_currentResult == null)
            return;

        try
        {
            _processor.ApplySegmentation(dataset, _currentResult, _settings.TargetMaterialId);
            _statusMessage = "Segmentation applied to labels";
            Logger.Log($"[AmbientOcclusion] Applied segmentation to material ID {_settings.TargetMaterialId}");
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error applying: {ex.Message}";
            Logger.LogError($"[AmbientOcclusion] Failed to apply segmentation: {ex.Message}");
        }
    }

    private void UpdateSegmentationMask()
    {
        if (_currentResult?.AoField == null)
            return;

        var aoField = _currentResult.AoField;
        int width = aoField.GetLength(0);
        int height = aoField.GetLength(1);
        int depth = aoField.GetLength(2);

        var mask = new bool[width, height, depth];

        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    mask[x, y, z] = aoField[x, y, z] < _settings.SegmentationThreshold;
                }
            }
        }

        _currentResult.SegmentationMask = mask;
        _preview.Result = _currentResult;
    }

    private void UpdatePreviewRegistration()
    {
        if (_lastDataset == null)
            return;

        if (_preview.ShowPreview && _currentResult != null)
        {
            AmbientOcclusionIntegration.RegisterPreview(_lastDataset, _preview);
        }
        else
        {
            AmbientOcclusionIntegration.UnregisterPreview(_lastDataset);
        }
    }

    private void TriggerAutoUpdate()
    {
        _lastUpdateTime = (float)ImGui.GetTime();

        // Schedule update after delay
        Task.Delay(TimeSpan.FromSeconds(AutoUpdateDelay)).ContinueWith(_ =>
        {
            if ((float)ImGui.GetTime() - _lastUpdateTime >= AutoUpdateDelay && !_isProcessing && _lastDataset != null)
            {
                StartComputation(_lastDataset);
            }
        });
    }

    private void ComputeHistogram(CtImageStackDataset dataset)
    {
        try
        {
            _statusMessage = "Computing histogram...";
            int sampleRate = Math.Max(1, dataset.Depth / 100); // Sample for speed
            _histogram = BinarizationHelper.ComputeHistogram(dataset, sampleRate);
            _histogramStats = BinarizationHelper.ComputeHistogramStats(_histogram);
            _statusMessage = "Histogram computed";
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error computing histogram: {ex.Message}";
            Logger.LogError($"[AmbientOcclusion] {ex.Message}");
        }
    }

    private void AutoDetectThreshold(CtImageStackDataset dataset)
    {
        try
        {
            if (_histogram == null)
            {
                ComputeHistogram(dataset);
            }

            byte threshold = _thresholdMethod switch
            {
                ThresholdMethod.Otsu => BinarizationHelper.ComputeOtsuThreshold(_histogram),
                ThresholdMethod.Triangle => BinarizationHelper.ComputeTriangleThreshold(_histogram),
                ThresholdMethod.Mean => BinarizationHelper.ComputeMeanThreshold(_histogram),
                ThresholdMethod.Isodata => BinarizationHelper.ComputeIsodataThreshold(_histogram),
                ThresholdMethod.Percentile95 => BinarizationHelper.ComputePercentileThreshold(_histogram, 95),
                ThresholdMethod.Percentile99 => BinarizationHelper.ComputePercentileThreshold(_histogram, 99),
                _ => _settings.MaterialThreshold
            };

            _settings.MaterialThreshold = threshold;
            _statusMessage = $"Auto-detected threshold: {threshold} ({_thresholdMethod})";
            Logger.Log($"[AmbientOcclusion] Auto-detected threshold: {threshold} using {_thresholdMethod}");

            if (_autoUpdate)
            {
                TriggerAutoUpdate();
            }
        }
        catch (Exception ex)
        {
            _statusMessage = $"Error detecting threshold: {ex.Message}";
            Logger.LogError($"[AmbientOcclusion] {ex.Message}");
        }
    }

    private void DrawHistogram()
    {
        if (_histogram == null)
            return;

        var stats = _histogramStats;

        // Draw histogram stats
        ImGui.Indent();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "Histogram Statistics:");
        ImGui.Text($"Min: {stats.Min}, Max: {stats.Max}, Mean: {stats.Mean:F1}");
        ImGui.Text($"StdDev: {stats.StdDev:F1}, Mode: {stats.Mode}");

        // Normalize histogram for display
        int maxCount = 0;
        for (int i = 0; i < 256; i++)
        {
            if (_histogram[i] > maxCount)
                maxCount = _histogram[i];
        }

        if (maxCount > 0)
        {
            float[] histogramFloats = new float[256];
            for (int i = 0; i < 256; i++)
            {
                histogramFloats[i] = (float)_histogram[i] / maxCount;
            }

            // Draw histogram plot
            ImGui.PlotHistogram("##histogram", ref histogramFloats[0], 256, 0, null, 0f, 1f, new Vector2(-1, 80));

            // Draw current threshold line
            var drawList = ImGui.GetWindowDrawList();
            var plotPos = ImGui.GetItemRectMin();
            var plotSize = ImGui.GetItemRectSize();

            float thresholdX = plotPos.X + (_settings.MaterialThreshold / 255f) * plotSize.X;
            var lineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0, 0, 1));
            drawList.AddLine(
                new Vector2(thresholdX, plotPos.Y),
                new Vector2(thresholdX, plotPos.Y + plotSize.Y),
                lineColor, 2f);

            // Label
            ImGui.Text($"Threshold: {_settings.MaterialThreshold} (red line)");
        }

        ImGui.Unindent();
    }
}
