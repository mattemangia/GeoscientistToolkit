// GeoscientistToolkit/Analysis/Filtering/FilterUI.cs

using System.Diagnostics;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Business;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;
using ImGuiNET;
using Silk.NET.Core.Native;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Filtering;

public enum FilterType
{
    Gaussian,
    Median,
    Mean,
    NonLocalMeans,
    EdgeSobel,
    EdgeCanny,
    UnsharpMask,
    Bilateral
}

/// <summary>
///     Provides the UI panel and filtering logic with GPU acceleration and real-time preview.
/// </summary>
public class FilterUI : IDisposable
{
    private const int PREVIEW_UPDATE_DELAY_MS = 300;
    private readonly GpuProcessor _gpuProcessor;
    private float _cannyHighThreshold = 150f;
    private float _cannyLowThreshold = 50f;
    private bool _isGeneratingPreview;

    private bool _isProcessing;
    private int _kernelSize = 3;
    private DateTime _lastPreviewUpdate = DateTime.MinValue;
    private float _nlmPatchRadius = 3;
    private float _nlmSearchRadius = 7;
    private byte[] _previewMask;
    private bool _process3D;
    private float _progress;
    private FilterType _selectedFilter = FilterType.Gaussian;

    // Preview functionality
    private bool _showPreview;
    private float _sigma = 1.0f;
    private float _sigmaColor = 20.0f;
    private string _statusMessage = "Ready";
    private float _unsharpAmount = 0.5f;
    private bool _useGpu = true;

    public FilterUI()
    {
        try
        {
            _gpuProcessor = new GpuProcessor();
            if (!_gpuProcessor.IsInitialized)
            {
                _useGpu = false;
                _statusMessage = "GPU (OpenCL) not available. Using CPU.";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"[FilterTool] Failed to initialize GPU processor: {ex.Message}");
            _gpuProcessor = null;
            _useGpu = false;
            _statusMessage = "GPU initialization failed. Using CPU.";
        }
    }

    public void Dispose()
    {
        ClearPreview();
        _gpuProcessor?.Dispose();
    }

    public void DrawPanel(CtImageStackDataset dataset)
    {
        ImGui.Text("Advanced Filter Tools for Volume Data");
        ImGui.Separator();

        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("Filter Type", GetFilterDisplayName(_selectedFilter)))
        {
            foreach (FilterType type in Enum.GetValues(typeof(FilterType)))
                if (ImGui.Selectable(GetFilterDisplayName(type), type == _selectedFilter))
                {
                    _selectedFilter = type;
                    ClearPreview();
                }

            ImGui.EndCombo();
        }

        if (_gpuProcessor != null && _gpuProcessor.IsInitialized)
        {
            ImGui.Checkbox("Use GPU (OpenCL)", ref _useGpu);
        }
        else
        {
            ImGui.TextDisabled("GPU (OpenCL) not available");
            _useGpu = false;
        }

        ImGui.SameLine();
        ImGui.Checkbox("Process as 3D", ref _process3D);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Apply filter in 3D (e.g., 3x3x3 kernel) vs. 2D per slice.");

        ImGui.Separator();
        ImGui.Text("Parameters");

        var paramsChanged = DrawFilterParameters();

        ImGui.Separator();

        // Preview controls
        if (ImGui.Checkbox("Show Preview", ref _showPreview))
        {
            if (_showPreview)
                _ = UpdatePreviewAsync(dataset);
            else
                ClearPreview();
        }

        if (_showPreview)
        {
            ImGui.SameLine();
            if (_isGeneratingPreview)
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Generating...");
            else
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Preview Active");

            if (paramsChanged && (DateTime.Now - _lastPreviewUpdate).TotalMilliseconds > PREVIEW_UPDATE_DELAY_MS)
                _ = UpdatePreviewAsync(dataset);
        }

        ImGui.Separator();

        if (_isProcessing) ImGui.BeginDisabled();
        if (ImGui.Button("Apply Filter", new Vector2(-1, 0)))
        {
            ClearPreview();
            _isProcessing = true;
            _progress = 0f;
            _statusMessage = "Starting...";
            _ = Task.Run(() => ApplyFilter(dataset));
        }

        if (_isProcessing) ImGui.EndDisabled();

        if (_isProcessing)
        {
            ImGui.ProgressBar(_progress, new Vector2(-1, 0), $"{_progress * 100:F1}%");
            ImGui.TextWrapped(_statusMessage);
        }
        else
        {
            ImGui.Text(_statusMessage);
        }
    }

    private bool DrawFilterParameters()
    {
        var changed = false;

        switch (_selectedFilter)
        {
            case FilterType.Gaussian:
            case FilterType.Mean:
                changed |= DrawKernelSizeSelector();
                if (_selectedFilter == FilterType.Gaussian)
                {
                    ImGui.SetNextItemWidth(-1);
                    changed |= ImGui.SliderFloat("Sigma (Blur)", ref _sigma, 0.1f, 10.0f, "%.2f");
                }

                break;

            case FilterType.Median:
                changed |= DrawKernelSizeSelector();
                break;

            case FilterType.Bilateral:
                changed |= DrawKernelSizeSelector();
                ImGui.SetNextItemWidth(-1);
                changed |= ImGui.SliderFloat("Sigma (Space)", ref _sigma, 0.1f, 10.0f, "%.2f");
                ImGui.SetNextItemWidth(-1);
                changed |= ImGui.SliderFloat("Sigma (Color)", ref _sigmaColor, 1.0f, 255.0f, "%.1f");
                break;

            case FilterType.NonLocalMeans:
                ImGui.SetNextItemWidth(-1);
                changed |= ImGui.SliderFloat("Filter Strength (h)", ref _sigma, 0.1f, 30.0f, "%.1f");
                ImGui.SetNextItemWidth(-1);
                changed |= ImGui.SliderFloat("Search Radius", ref _nlmSearchRadius, 3, 15, "%.0f");
                ImGui.SetNextItemWidth(-1);
                changed |= ImGui.SliderFloat("Patch Radius", ref _nlmPatchRadius, 1, 7, "%.0f");
                break;

            case FilterType.EdgeSobel:
                ImGui.Text("Uses Sobel operator (3x3 kernel)");
                break;

            case FilterType.EdgeCanny:
                ImGui.SetNextItemWidth(-1);
                changed |= ImGui.SliderFloat("Low Threshold", ref _cannyLowThreshold, 0f, 255f, "%.1f");
                ImGui.SetNextItemWidth(-1);
                changed |= ImGui.SliderFloat("High Threshold", ref _cannyHighThreshold, 0f, 255f, "%.1f");
                break;

            case FilterType.UnsharpMask:
                changed |= DrawKernelSizeSelector();
                ImGui.SetNextItemWidth(-1);
                changed |= ImGui.SliderFloat("Blur Sigma", ref _sigma, 0.1f, 10.0f, "%.2f");
                ImGui.SetNextItemWidth(-1);
                changed |= ImGui.SliderFloat("Amount", ref _unsharpAmount, 0.1f, 3.0f, "%.2f");
                break;
        }

        return changed;
    }

    private bool DrawKernelSizeSelector()
    {
        int[] kernelSizes = { 3, 5, 7, 9, 11 };
        var kernelLabels = kernelSizes.Select(s => $"{s}x{s}" + (_process3D ? $"x{s}" : "")).ToArray();
        var currentKernelIndex = Array.IndexOf(kernelSizes, _kernelSize);
        if (currentKernelIndex == -1) currentKernelIndex = 0;

        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("Kernel Size", ref currentKernelIndex, kernelLabels, kernelLabels.Length))
        {
            _kernelSize = kernelSizes[currentKernelIndex];
            return true;
        }

        return false;
    }

    private string GetFilterDisplayName(FilterType filter)
    {
        return filter switch
        {
            FilterType.Gaussian => "Gaussian Blur",
            FilterType.Median => "Median Filter",
            FilterType.Mean => "Mean (Box) Filter",
            FilterType.NonLocalMeans => "Non-Local Means Denoising",
            FilterType.EdgeSobel => "Edge Detection (Sobel)",
            FilterType.EdgeCanny => "Edge Detection (Canny)",
            FilterType.UnsharpMask => "Unsharp Masking",
            FilterType.Bilateral => "Bilateral Filter",
            _ => filter.ToString()
        };
    }

    private async Task UpdatePreviewAsync(CtImageStackDataset dataset)
    {
        if (_isGeneratingPreview || dataset.VolumeData == null) return;

        _isGeneratingPreview = true;

        try
        {
            await Task.Run(() =>
            {
                var width = dataset.Width;
                var height = dataset.Height;
                var depth = dataset.Depth;

                // Generate preview for current slice + adjacent slices
                var previewDepth = Math.Min(5, depth);
                var startZ = Math.Max(0, depth / 2 - previewDepth / 2);

                var previewData = new byte[width * height * previewDepth];
                for (var z = 0; z < previewDepth; z++)
                {
                    var slice = new byte[width * height];
                    dataset.VolumeData.ReadSliceZ(startZ + z, slice);
                    Buffer.BlockCopy(slice, 0, previewData, z * width * height, slice.Length);
                }

                byte[] resultData;
                if (_useGpu && _gpuProcessor != null && _gpuProcessor.IsInitialized)
                {
                    resultData = _gpuProcessor.RunFilter(previewData, width, height, previewDepth, _selectedFilter,
                        _kernelSize, _sigma, _sigmaColor, _nlmSearchRadius, _nlmPatchRadius, _unsharpAmount,
                        _cannyLowThreshold, _cannyHighThreshold, false, _ => { }, _ => { });

                    if (resultData == null)
                    {
                        resultData = new byte[previewData.Length];
                        ApplyFilterCPUToSubVolume(previewData, resultData, width, height, previewDepth);
                    }
                }
                else
                {
                    resultData = new byte[previewData.Length];
                    ApplyFilterCPUToSubVolume(previewData, resultData, width, height, previewDepth);
                }

                // Create full volume preview mask showing difference
                _previewMask = new byte[width * height * depth];
                var midSlice = previewDepth / 2;
                Buffer.BlockCopy(resultData, midSlice * width * height, _previewMask,
                    (startZ + midSlice) * width * height, width * height);
            });

            CtImageStackTools.Update3DPreviewFromExternal(dataset, _previewMask, new Vector4(0, 1, 1, 0.5f));
            _lastPreviewUpdate = DateTime.Now;
        }
        finally
        {
            _isGeneratingPreview = false;
        }
    }

    private void ClearPreview()
    {
        _previewMask = null;
        _showPreview = false;
    }

    private void ApplyFilterCPUToSubVolume(byte[] source, byte[] result, int width, int height, int depth)
    {
        switch (_selectedFilter)
        {
            case FilterType.Gaussian: ApplyGaussianCPU(source, result, width, height, depth); break;
            case FilterType.Median: ApplyMedianCPU(source, result, width, height, depth); break;
            case FilterType.Mean: ApplyMeanCPU(source, result, width, height, depth); break;
            case FilterType.Bilateral: ApplyBilateralCPU(source, result, width, height, depth); break;
            case FilterType.NonLocalMeans: ApplyNonLocalMeansCPU(source, result, width, height, depth); break;
            case FilterType.EdgeSobel: ApplySobelCPU(source, result, width, height, depth); break;
            case FilterType.EdgeCanny: ApplyCannyCPU(source, result, width, height, depth); break;
            case FilterType.UnsharpMask: ApplyUnsharpMaskCPU(source, result, width, height, depth); break;
        }
    }

    private async Task ApplyFilter(CtImageStackDataset dataset)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (dataset.VolumeData == null)
            {
                _statusMessage = "Error: Volume data not loaded.";
                return;
            }

            _statusMessage = "Reading source volume data...";
            await Task.Delay(10);
            var sourceData = dataset.VolumeData.GetAllData();
            byte[] resultData;

            var width = dataset.Width;
            var height = dataset.Height;
            var depth = dataset.Depth;

            if (_useGpu && _gpuProcessor != null && _gpuProcessor.IsInitialized)
            {
                _statusMessage = "Processing on GPU...";
                resultData = _gpuProcessor.RunFilter(sourceData, width, height, depth, _selectedFilter, _kernelSize,
                    _sigma, _sigmaColor, _nlmSearchRadius, _nlmPatchRadius, _unsharpAmount, _cannyLowThreshold,
                    _cannyHighThreshold, _process3D, p => _progress = p, s => _statusMessage = s);

                if (resultData == null)
                {
                    _statusMessage = "GPU not available for this config. Processing on CPU...";
                    resultData = new byte[sourceData.Length];
                    ApplyFilterCPU(sourceData, resultData, width, height, depth);
                }
            }
            else
            {
                _statusMessage = "Processing on CPU...";
                resultData = new byte[sourceData.Length];
                ApplyFilterCPU(sourceData, resultData, width, height, depth);
            }

            _statusMessage = "Writing results back to volume...";
            await Task.Delay(10);
            for (var z = 0; z < depth; z++)
            {
                var sliceBuffer = new byte[width * height];
                Buffer.BlockCopy(resultData, z * width * height, sliceBuffer, 0, sliceBuffer.Length);
                dataset.VolumeData.WriteSliceZ(z, sliceBuffer);
                _progress = (float)(z + 1) / depth;
            }

            stopwatch.Stop();
            _statusMessage = $"Completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds.";
            ProjectManager.Instance.NotifyDatasetDataChanged(dataset);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _statusMessage = $"Error: {ex.Message}";
            Logger.LogError($"[FilterTool] ApplyFilter failed: {ex}");
        }
        finally
        {
            _isProcessing = false;
            _progress = 0f;
        }
    }

    private void ApplyFilterCPU(byte[] source, byte[] result, int width, int height, int depth)
    {
        switch (_selectedFilter)
        {
            case FilterType.Gaussian: ApplyGaussianCPU(source, result, width, height, depth); break;
            case FilterType.Median: ApplyMedianCPU(source, result, width, height, depth); break;
            case FilterType.Mean: ApplyMeanCPU(source, result, width, height, depth); break;
            case FilterType.Bilateral: ApplyBilateralCPU(source, result, width, height, depth); break;
            case FilterType.NonLocalMeans: ApplyNonLocalMeansCPU(source, result, width, height, depth); break;
            case FilterType.EdgeSobel: ApplySobelCPU(source, result, width, height, depth); break;
            case FilterType.EdgeCanny: ApplyCannyCPU(source, result, width, height, depth); break;
            case FilterType.UnsharpMask: ApplyUnsharpMaskCPU(source, result, width, height, depth); break;
        }
    }

    private void ApplyGaussianCPU(byte[] source, byte[] result, int width, int height, int depth)
    {
        var R = _kernelSize / 2;
        var kernel = CreateGaussianKernel3D(_kernelSize, _sigma, _process3D);
        Parallel.For(0, depth, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                float sum = 0, weightSum = 0;
                var zStart = _process3D ? Math.Max(0, z - R) : z;
                var zEnd = _process3D ? Math.Min(depth - 1, z + R) : z;
                for (var kz = zStart; kz <= zEnd; kz++)
                {
                    var dz = _process3D ? kz - z + R : R;
                    for (var ky = Math.Max(0, y - R); ky <= Math.Min(height - 1, y + R); ky++)
                    {
                        var dy = ky - y + R;
                        for (var kx = Math.Max(0, x - R); kx <= Math.Min(width - 1, x + R); kx++)
                        {
                            var dx = kx - x + R;
                            var weight = kernel[dz, dy, dx];
                            var idx = (long)kz * width * height + (long)ky * width + kx;
                            sum += source[idx] * weight;
                            weightSum += weight;
                        }
                    }
                }

                var centerIdx = (long)z * width * height + (long)y * width + x;
                result[centerIdx] = (byte)Math.Clamp(sum / weightSum, 0, 255);
            }

            _progress = (float)(z + 1) / depth;
        });
    }

    private void ApplyMedianCPU(byte[] source, byte[] result, int width, int height, int depth)
    {
        var R = _kernelSize / 2;
        Parallel.For(0, depth, z =>
        {
            var neighbors = new List<byte>(_kernelSize * _kernelSize * (_process3D ? _kernelSize : 1));
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                neighbors.Clear();
                var zStart = _process3D ? Math.Max(0, z - R) : z;
                var zEnd = _process3D ? Math.Min(depth - 1, z + R) : z;
                for (var kz = zStart; kz <= zEnd; kz++)
                for (var ky = Math.Max(0, y - R); ky <= Math.Min(height - 1, y + R); ky++)
                for (var kx = Math.Max(0, x - R); kx <= Math.Min(width - 1, x + R); kx++)
                {
                    var idx = (long)kz * width * height + (long)ky * width + kx;
                    neighbors.Add(source[idx]);
                }

                neighbors.Sort();
                var centerIdx = (long)z * width * height + (long)y * width + x;
                result[centerIdx] = neighbors[neighbors.Count / 2];
            }

            _progress = (float)(z + 1) / depth;
        });
    }

    private void ApplyMeanCPU(byte[] source, byte[] result, int width, int height, int depth)
    {
        var R = _kernelSize / 2;
        Parallel.For(0, depth, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                float sum = 0;
                var count = 0;
                var zStart = _process3D ? Math.Max(0, z - R) : z;
                var zEnd = _process3D ? Math.Min(depth - 1, z + R) : z;
                for (var kz = zStart; kz <= zEnd; kz++)
                for (var ky = Math.Max(0, y - R); ky <= Math.Min(height - 1, y + R); ky++)
                for (var kx = Math.Max(0, x - R); kx <= Math.Min(width - 1, x + R); kx++)
                {
                    var idx = (long)kz * width * height + (long)ky * width + kx;
                    sum += source[idx];
                    count++;
                }

                var centerIdx = (long)z * width * height + (long)y * width + x;
                result[centerIdx] = (byte)Math.Clamp(sum / count, 0, 255);
            }

            _progress = (float)(z + 1) / depth;
        });
    }

    private void ApplyBilateralCPU(byte[] source, byte[] result, int width, int height, int depth)
    {
        var R = _kernelSize / 2;
        Parallel.For(0, depth, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var centerIdx = (long)z * width * height + (long)y * width + x;
                var centerVal = source[centerIdx];
                float totalWeight = 0, weightedSum = 0;
                var zStart = _process3D ? Math.Max(0, z - R) : z;
                var zEnd = _process3D ? Math.Min(depth - 1, z + R) : z;
                for (var kz = zStart; kz <= zEnd; kz++)
                for (var ky = Math.Max(0, y - R); ky <= Math.Min(height - 1, y + R); ky++)
                for (var kx = Math.Max(0, x - R); kx <= Math.Min(width - 1, x + R); kx++)
                {
                    var kernelIdx = (long)kz * width * height + (long)ky * width + kx;
                    var neighborVal = source[kernelIdx];
                    float distSq = (kx - x) * (kx - x) + (ky - y) * (ky - y) + (_process3D ? (kz - z) * (kz - z) : 0);
                    float colorDist = Math.Abs(neighborVal - centerVal);
                    var spatialWeight = (float)Math.Exp(-distSq / (2 * _sigma * _sigma));
                    var colorWeight = (float)Math.Exp(-colorDist * colorDist / (2 * _sigmaColor * _sigmaColor));
                    var weight = spatialWeight * colorWeight;
                    weightedSum += neighborVal * weight;
                    totalWeight += weight;
                }

                result[centerIdx] = (byte)Math.Clamp(weightedSum / totalWeight, 0, 255);
            }

            _progress = (float)(z + 1) / depth;
        });
    }

    private void ApplyNonLocalMeansCPU(byte[] source, byte[] result, int width, int height, int depth)
    {
        int searchRadius = (int)_nlmSearchRadius, patchRadius = (int)_nlmPatchRadius;
        float h = _sigma, h2 = h * h;
        Parallel.For(0, depth, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                float totalWeight = 0, weightedSum = 0;
                var centerIdx = (long)z * width * height + (long)y * width + x;
                var szMin = _process3D ? Math.Max(0, z - searchRadius) : z;
                var szMax = _process3D ? Math.Min(depth - 1, z + searchRadius) : z;
                for (var sz = szMin; sz <= szMax; sz++)
                for (var sy = Math.Max(0, y - searchRadius); sy <= Math.Min(height - 1, y + searchRadius); sy++)
                for (var sx = Math.Max(0, x - searchRadius); sx <= Math.Min(width - 1, x + searchRadius); sx++)
                {
                    float patchDist = 0;
                    var patchCount = 0;
                    var pzMin = _process3D ? Math.Max(0, -patchRadius) : 0;
                    var pzMax = _process3D ? Math.Min(depth - 1 - Math.Max(z, sz), patchRadius) : 0;
                    for (var pz = pzMin; pz <= pzMax; pz++)
                    for (var py = Math.Max(-patchRadius, -Math.Min(y, sy));
                         py <= Math.Min(patchRadius, Math.Min(height - 1 - y, height - 1 - sy));
                         py++)
                    for (var px = Math.Max(-patchRadius, -Math.Min(x, sx));
                         px <= Math.Min(patchRadius, Math.Min(width - 1 - x, width - 1 - sx));
                         px++)
                    {
                        var idx1 = (long)(z + pz) * width * height + (long)(y + py) * width + (x + px);
                        var idx2 = (long)(sz + pz) * width * height + (long)(sy + py) * width + (sx + px);
                        float diff = source[idx1] - source[idx2];
                        patchDist += diff * diff;
                        patchCount++;
                    }

                    patchDist /= patchCount;
                    var weight = (float)Math.Exp(-Math.Max(patchDist - 2 * h2, 0) / h2);
                    var searchIdx = (long)sz * width * height + (long)sy * width + sx;
                    weightedSum += source[searchIdx] * weight;
                    totalWeight += weight;
                }

                result[centerIdx] = (byte)Math.Clamp(weightedSum / totalWeight, 0, 255);
            }

            _progress = (float)(z + 1) / depth;
        });
    }

    private void ApplySobelCPU(byte[] source, byte[] result, int width, int height, int depth)
    {
        int[,] sobelX = { { -1, 0, 1 }, { -2, 0, 2 }, { -1, 0, 1 } };
        int[,] sobelY = { { -1, -2, -1 }, { 0, 0, 0 }, { 1, 2, 1 } };
        Parallel.For(0, depth, z =>
        {
            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                float gx = 0, gy = 0, gz = 0;
                for (var ky = -1; ky <= 1; ky++)
                for (var kx = -1; kx <= 1; kx++)
                {
                    var idx = (long)z * width * height + (long)(y + ky) * width + (x + kx);
                    var val = source[idx];
                    gx += val * sobelX[ky + 1, kx + 1];
                    gy += val * sobelY[ky + 1, kx + 1];
                }

                if (_process3D && z > 0 && z < depth - 1)
                    for (var ky = -1; ky <= 1; ky++)
                    for (var kx = -1; kx <= 1; kx++)
                    {
                        var idxPrev = (long)(z - 1) * width * height + (long)(y + ky) * width + (x + kx);
                        var idxNext = (long)(z + 1) * width * height + (long)(y + ky) * width + (x + kx);
                        gz += (source[idxNext] - source[idxPrev]) * sobelX[ky + 1, kx + 1];
                    }

                var mag = (float)Math.Sqrt(gx * gx + gy * gy + gz * gz);
                result[(long)z * width * height + (long)y * width + x] = (byte)Math.Clamp(mag, 0, 255);
            }

            _progress = (float)(z + 1) / depth;
        });
    }

    private void ApplyCannyCPU(byte[] source, byte[] result, int width, int height, int depth)
    {
        var blurred = new byte[source.Length];
        ApplyGaussianCPU(source, blurred, width, height, depth);
        var gradients = new byte[source.Length];
        var gradX = new float[depth, height, width];
        var gradY = new float[depth, height, width];
        int[,] sobelX = { { -1, 0, 1 }, { -2, 0, 2 }, { -1, 0, 1 } };
        int[,] sobelY = { { -1, -2, -1 }, { 0, 0, 0 }, { 1, 2, 1 } };
        Parallel.For(0, depth, z =>
        {
            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                float gx = 0, gy = 0;
                for (var ky = -1; ky <= 1; ky++)
                for (var kx = -1; kx <= 1; kx++)
                {
                    var idx = (long)z * width * height + (long)(y + ky) * width + (x + kx);
                    gx += blurred[idx] * sobelX[ky + 1, kx + 1];
                    gy += blurred[idx] * sobelY[ky + 1, kx + 1];
                }

                gradX[z, y, x] = gx;
                gradY[z, y, x] = gy;
                gradients[(long)z * width * height + (long)y * width + x] =
                    (byte)Math.Clamp((float)Math.Sqrt(gx * gx + gy * gy), 0, 255);
            }
        });
        var suppressed = new byte[source.Length];
        Parallel.For(0, depth, z =>
        {
            for (var y = 1; y < height - 1; y++)
            for (var x = 1; x < width - 1; x++)
            {
                var idx = (long)z * width * height + (long)y * width + x;
                var angle = (float)Math.Atan2(gradY[z, y, x], gradX[z, y, x]) * 180f / (float)Math.PI;
                if (angle < 0) angle += 180;
                byte q = 255, r = 255;
                if ((angle >= 0 && angle < 22.5) || (angle >= 157.5 && angle <= 180))
                {
                    q = gradients[idx + 1];
                    r = gradients[idx - 1];
                }
                else if (angle >= 22.5 && angle < 67.5)
                {
                    q = gradients[idx - width + 1];
                    r = gradients[idx + width - 1];
                }
                else if (angle >= 67.5 && angle < 112.5)
                {
                    q = gradients[idx - width];
                    r = gradients[idx + width];
                }
                else if (angle >= 112.5 && angle < 157.5)
                {
                    q = gradients[idx - width - 1];
                    r = gradients[idx + width + 1];
                }

                suppressed[idx] = gradients[idx] >= q && gradients[idx] >= r ? gradients[idx] : (byte)0;
            }
        });
        Parallel.For(0, depth, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var idx = (long)z * width * height + (long)y * width + x;
                if (suppressed[idx] >= _cannyHighThreshold)
                {
                    result[idx] = 255;
                }
                else if (suppressed[idx] >= _cannyLowThreshold)
                {
                    var strong = false;
                    for (var ky = -1; ky <= 1 && !strong; ky++)
                    for (var kx = -1; kx <= 1 && !strong; kx++)
                    {
                        if (kx == 0 && ky == 0) continue;
                        int nx = x + kx, ny = y + ky;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height &&
                            suppressed[(long)z * width * height + (long)ny * width + nx] >= _cannyHighThreshold)
                            strong = true;
                    }

                    result[idx] = strong ? (byte)255 : (byte)0;
                }
                else
                {
                    result[idx] = 0;
                }
            }

            _progress = (float)(z + 1) / depth;
        });
    }

    private void ApplyUnsharpMaskCPU(byte[] source, byte[] result, int width, int height, int depth)
    {
        var blurred = new byte[source.Length];
        ApplyGaussianCPU(source, blurred, width, height, depth);
        Parallel.For(0, (long)width * height * depth, i =>
        {
            float original = source[i];
            float blur = blurred[i];
            var sharpened = original + _unsharpAmount * (original - blur);
            result[i] = (byte)Math.Clamp(sharpened, 0, 255);
        });
    }

    private float[,,] CreateGaussianKernel3D(int size, float sigma, bool is3D)
    {
        var kernel = new float[is3D ? size : 1, size, size];
        var R = size / 2;
        float sum = 0;
        for (var z = 0; z < (is3D ? size : 1); z++)
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dz = is3D ? z - R : 0;
            var dy = y - R;
            var dx = x - R;
            float distSq = dx * dx + dy * dy + dz * dz;
            kernel[z, y, x] = (float)Math.Exp(-distSq / (2 * sigma * sigma));
            sum += kernel[z, y, x];
        }

        for (var z = 0; z < (is3D ? size : 1); z++)
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            kernel[z, y, x] /= sum;
        return kernel;
    }
}

/// <summary>
///     Encapsulates all OpenCL related logic for GPU processing with complete filter implementations.
/// </summary>
internal unsafe class GpuProcessor : IDisposable
{
    private readonly Dictionary<string, nint> _kernels = new();
    private readonly Dictionary<string, nint> _programs = new();
    private CL _cl;
    private nint _context;
    private nint _device;
    private bool _disposed;
    private nint _queue;

    public GpuProcessor()
    {
        try
        {
            Initialize();
            IsInitialized = true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"[GpuProcessor] OpenCL initialization failed: {ex.Message}");
            IsInitialized = false;
        }
    }

    public bool IsInitialized { get; }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var kernel in _kernels.Values)
            if (kernel != nint.Zero)
                _cl?.ReleaseKernel(kernel);
        foreach (var program in _programs.Values)
            if (program != nint.Zero)
                _cl?.ReleaseProgram(program);
        if (_queue != nint.Zero) _cl?.ReleaseCommandQueue(_queue);
        if (_context != nint.Zero) _cl?.ReleaseContext(_context);
        _disposed = true;
    }

    private void Initialize()
    {
        _cl = CL.GetApi();

        // Use centralized device manager to get the device from settings
        _device = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetComputeDevice();

        if (_device == 0)
            throw new DllNotFoundException("No OpenCL device available from OpenCLDeviceManager.");

        // Get device info from the centralized manager
        var deviceInfo = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetDeviceInfo();
        Logger.Log($"[GpuProcessor/FilterUI] Using device: {deviceInfo.Name} ({deviceInfo.Vendor})");

        // Context
        _context = _cl.CreateContext(null, 1, in _device, null, null, out var err);
        CheckError(err, "CreateContext");

        // Command queue
        _queue = _cl.CreateCommandQueue(_context, _device, (CommandQueueProperties)0, out err);
        CheckError(err, "CreateCommandQueue");

        // Compile all kernels
        CompileKernel(GaussianKernelSource, "gaussian_2d");
        CompileKernel(MedianKernelSource, "median_2d");
        CompileKernel(MeanKernelSource, "mean_2d");
        CompileKernel(BilateralKernelSource, "bilateral_2d");
        CompileKernel(SobelKernelSource, "sobel_2d");
        CompileKernel(CannyKernelSource, "canny_gradient");
        CompileKernel(UnsharpKernelSource, "unsharp_2d");
        CompileKernel(NonLocalMeans2DKernelSource, "nlm_2d");
        CompileKernel(NonLocalMeans3DKernelSource, "nlm_3d");
    }

    private void CompileKernel(string source, string kernelName)
    {
        var err = 0;

        var srcPtr = (byte*)SilkMarshal.StringToPtr(source, NativeStringEncoding.UTF8);
        try
        {
            var ppSrc = &srcPtr;
            var program = _cl.CreateProgramWithSource(_context, 1, ppSrc, null, &err);
            CheckError(err, $"CreateProgramWithSource ({kernelName})");

            err = _cl.BuildProgram(program, 1, in _device, "", null, null);
            if (err != (int)CLEnum.Success)
            {
                nuint logSize = 0;
                _cl.GetProgramBuildInfo(program, _device, ProgramBuildInfo.BuildLog, 0, null, &logSize);

                var logBytes = new byte[(int)logSize];
                fixed (byte* pLog = logBytes)
                {
                    _cl.GetProgramBuildInfo(program, _device, ProgramBuildInfo.BuildLog, logSize, pLog, null);
                }

                Logger.LogError($"[GpuProcessor] Build Error for {kernelName}: {Encoding.UTF8.GetString(logBytes)}");
                CheckError(err, $"BuildProgram ({kernelName})");
            }

            var kernel = _cl.CreateKernel(program, kernelName, out err);
            CheckError(err, $"CreateKernel ({kernelName})");

            _programs[kernelName] = program;
            _kernels[kernelName] = kernel;
        }
        finally
        {
            SilkMarshal.Free((nint)srcPtr);
        }
    }

    public byte[]? RunFilter(byte[] sourceData, int width, int height, int depth,
        FilterType filter, int kernelSize, float sigma, float sigmaColor,
        float nlmSearchRadius, float nlmPatchRadius,
        float unsharpAmount, float cannyLowThreshold, float cannyHighThreshold,
        bool is3D, Action<float> onProgress, Action<string> onStatus)
    {
        if (!IsGpuAcceleratedFilter(filter) || (is3D && !SupportsGpu3D(filter)))
        {
            if (!IsGpuAcceleratedFilter(filter))
                onStatus($"Filter ({GetFilterName(filter)}) not GPU-accelerated. Using CPU.");
            else
                onStatus($"Filter ({GetFilterName(filter)}, 3D={is3D}) not GPU-accelerated in 3D mode. Using CPU.");
            Logger.LogWarning($"[GpuProcessor] Filter ({filter}, 3D={is3D}) not GPU-accelerated. Falling back to CPU.");
            return null;
        }

        var resultData = new byte[sourceData.Length];

        switch (filter)
        {
            case FilterType.Gaussian:
                ProcessGaussianGPU(sourceData, resultData, width, height, depth, kernelSize, sigma, onProgress,
                    onStatus);
                break;
            case FilterType.Median:
                ProcessMedianGPU(sourceData, resultData, width, height, depth, kernelSize, onProgress, onStatus);
                break;
            case FilterType.Mean:
                ProcessMeanGPU(sourceData, resultData, width, height, depth, kernelSize, onProgress, onStatus);
                break;
            case FilterType.Bilateral:
                ProcessBilateralGPU(sourceData, resultData, width, height, depth, kernelSize, sigma, sigmaColor,
                    onProgress, onStatus);
                break;
            case FilterType.EdgeSobel:
                ProcessSobelGPU(sourceData, resultData, width, height, depth, onProgress, onStatus);
                break;
            case FilterType.EdgeCanny:
                ProcessCannyGPU(sourceData, resultData, width, height, depth, cannyLowThreshold, cannyHighThreshold,
                    onProgress, onStatus);
                break;
            case FilterType.UnsharpMask:
                ProcessUnsharpGPU(sourceData, resultData, width, height, depth, kernelSize, sigma, unsharpAmount,
                    onProgress, onStatus);
                break;
            case FilterType.NonLocalMeans:
                if (is3D)
                    ProcessNonLocalMeans3DGPU(sourceData, resultData, width, height, depth, nlmSearchRadius,
                        nlmPatchRadius, sigma, onProgress, onStatus);
                else
                    ProcessNonLocalMeans2DGPU(sourceData, resultData, width, height, depth, nlmSearchRadius,
                        nlmPatchRadius, sigma, onProgress, onStatus);
                break;
            default:
                return null;
        }

        return resultData;
    }

    private static bool IsGpuAcceleratedFilter(FilterType filter)
    {
        return filter is FilterType.Gaussian or FilterType.Median or FilterType.Mean
            or FilterType.Bilateral or FilterType.EdgeSobel or FilterType.EdgeCanny
            or FilterType.UnsharpMask or FilterType.NonLocalMeans;
    }

    private static bool SupportsGpu3D(FilterType filter)
    {
        return filter is FilterType.NonLocalMeans;
    }

    private static string GetFilterName(FilterType filter)
    {
        return filter switch
        {
            FilterType.Gaussian => "Gaussian",
            FilterType.Median => "Median",
            FilterType.Mean => "Mean",
            FilterType.NonLocalMeans => "Non-Local Means",
            FilterType.EdgeSobel => "Sobel Edge",
            FilterType.EdgeCanny => "Canny Edge",
            FilterType.UnsharpMask => "Unsharp Mask",
            FilterType.Bilateral => "Bilateral",
            _ => filter.ToString()
        };
    }

    private void ProcessGaussianGPU(byte[] sourceData, byte[] resultData, int width, int height, int depth,
        int kernelSize, float sigma, Action<float> onProgress, Action<string> onStatus)
    {
        onStatus("Running Gaussian Filter on GPU...");
        var kernel = _kernels["gaussian_2d"];
        var err = 0;
        var radius = kernelSize / 2;

        var kernelWeights = CreateGaussianKernel(kernelSize, sigma);
        fixed (float* pWeights = kernelWeights)
        {
            var clKernelWeights = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(sizeof(float) * kernelWeights.Length), pWeights, &err);
            CheckError(err, "CreateBuffer (kernel_weights)");

            ProcessSlicesGPU(sourceData, resultData, width, height, depth, kernel,
                new[] { clKernelWeights }, new[] { radius }, onProgress, onStatus);

            _cl.ReleaseMemObject(clKernelWeights);
        }
    }

    private void ProcessMedianGPU(byte[] sourceData, byte[] resultData, int width, int height, int depth,
        int kernelSize, Action<float> onProgress, Action<string> onStatus)
    {
        onStatus("Running Median Filter on GPU...");
        var kernel = _kernels["median_2d"];
        var radius = kernelSize / 2;
        ProcessSlicesGPU(sourceData, resultData, width, height, depth, kernel,
            Array.Empty<nint>(), new[] { radius }, onProgress, onStatus);
    }

    private void ProcessMeanGPU(byte[] sourceData, byte[] resultData, int width, int height, int depth,
        int kernelSize, Action<float> onProgress, Action<string> onStatus)
    {
        onStatus("Running Mean Filter on GPU...");
        var kernel = _kernels["mean_2d"];
        var radius = kernelSize / 2;
        ProcessSlicesGPU(sourceData, resultData, width, height, depth, kernel,
            Array.Empty<nint>(), new[] { radius }, onProgress, onStatus);
    }

    private void ProcessBilateralGPU(byte[] sourceData, byte[] resultData, int width, int height, int depth,
        int kernelSize, float sigma, float sigmaColor, Action<float> onProgress, Action<string> onStatus)
    {
        onStatus("Running Bilateral Filter on GPU...");
        var kernel = _kernels["bilateral_2d"];
        var err = 0;
        var radius = kernelSize / 2;

        // Pack parameters into a buffer
        var parameters = new[] { sigma, sigmaColor };
        fixed (float* pParams = parameters)
        {
            var clParams = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(sizeof(float) * parameters.Length), pParams, &err);
            CheckError(err, "CreateBuffer (bilateral_params)");

            ProcessSlicesGPU(sourceData, resultData, width, height, depth, kernel,
                new[] { clParams }, new[] { radius }, onProgress, onStatus);

            _cl.ReleaseMemObject(clParams);
        }
    }

    private void ProcessSobelGPU(byte[] sourceData, byte[] resultData, int width, int height, int depth,
        Action<float> onProgress, Action<string> onStatus)
    {
        onStatus("Running Sobel Edge Detection on GPU...");
        var kernel = _kernels["sobel_2d"];
        ProcessSlicesGPU(sourceData, resultData, width, height, depth, kernel,
            Array.Empty<nint>(), Array.Empty<int>(), onProgress, onStatus);
    }

    private void ProcessCannyGPU(byte[] sourceData, byte[] resultData, int width, int height, int depth,
        float lowThreshold, float highThreshold, Action<float> onProgress, Action<string> onStatus)
    {
        onStatus("Running Canny Edge Detection on GPU...");
        var kernel = _kernels["canny_gradient"];
        var err = 0;

        var thresholds = new[] { lowThreshold / 255f, highThreshold / 255f };
        fixed (float* pThresh = thresholds)
        {
            var clThresh = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(sizeof(float) * thresholds.Length), pThresh, &err);
            CheckError(err, "CreateBuffer (canny_thresholds)");

            ProcessSlicesGPU(sourceData, resultData, width, height, depth, kernel,
                new[] { clThresh }, Array.Empty<int>(), onProgress, onStatus);

            _cl.ReleaseMemObject(clThresh);
        }
    }

    private void ProcessUnsharpGPU(byte[] sourceData, byte[] resultData, int width, int height, int depth,
        int kernelSize, float sigma, float amount, Action<float> onProgress, Action<string> onStatus)
    {
        onStatus("Running Unsharp Mask on GPU...");
        var kernel = _kernels["unsharp_2d"];
        var err = 0;
        var radius = kernelSize / 2;

        var kernelWeights = CreateGaussianKernel(kernelSize, sigma);
        fixed (float* pWeights = kernelWeights)
        {
            var clWeights = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(sizeof(float) * kernelWeights.Length), pWeights, &err);
            CheckError(err, "CreateBuffer (unsharp_weights)");

            var parameters = new[] { amount };
            fixed (float* pParams = parameters)
            {
                var clParams = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                    (nuint)(sizeof(float) * parameters.Length), pParams, &err);
                CheckError(err, "CreateBuffer (unsharp_params)");

                ProcessSlicesGPU(sourceData, resultData, width, height, depth, kernel,
                    new[] { clWeights, clParams }, new[] { radius }, onProgress, onStatus);

                _cl.ReleaseMemObject(clParams);
            }

            _cl.ReleaseMemObject(clWeights);
        }
    }

    private void ProcessNonLocalMeans2DGPU(byte[] sourceData, byte[] resultData, int width, int height, int depth,
        float searchRadius, float patchRadius, float h, Action<float> onProgress, Action<string> onStatus)
    {
        onStatus("Running Non-Local Means (2D) on GPU...");
        var kernel = _kernels["nlm_2d"];
        var err = 0;

        var sr = (int)searchRadius;
        var pr = (int)patchRadius;

        var imageFormat = new ImageFormat(ChannelOrder.R, ChannelType.UnormInt8);

        ImageDesc imageDesc = default;
        imageDesc.ImageType = MemObjectType.Image2D;
        imageDesc.ImageWidth = (nuint)width;
        imageDesc.ImageHeight = (nuint)height;
        imageDesc.ImageDepth = 0;
        imageDesc.ImageArraySize = 0;
        imageDesc.ImageRowPitch = 0;
        imageDesc.ImageSlicePitch = 0;
        imageDesc.NumMipLevels = 0;
        imageDesc.NumSamples = 0;

        var clInputImage = _cl.CreateImage(_context, MemFlags.ReadOnly, &imageFormat, &imageDesc, null, &err);
        CheckError(err, "CreateImage (input)");
        var clOutputImage = _cl.CreateImage(_context, MemFlags.WriteOnly, &imageFormat, &imageDesc, null, &err);
        CheckError(err, "CreateImage (output)");

        nuint[] origin = { 0, 0, 0 };
        nuint[] region = { (nuint)width, (nuint)height, 1 };

        for (var z = 0; z < depth; z++)
        {
            onStatus($"Processing NLM slice {z + 1}/{depth}");

            var slice = new byte[width * height];
            Buffer.BlockCopy(sourceData, z * width * height, slice, 0, slice.Length);

            fixed (void* pSlice = slice)
            fixed (nuint* pOrigin = origin)
            fixed (nuint* pRegion = region)
            {
                err = _cl.EnqueueWriteImage(_queue, clInputImage, true, pOrigin, pRegion,
                    (nuint)(width * sizeof(byte)), 0, pSlice, 0, null, null);
                CheckError(err, "EnqueueWriteImage");
            }

            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &clInputImage);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &clOutputImage);
            _cl.SetKernelArg(kernel, 2, sizeof(int), &sr);
            _cl.SetKernelArg(kernel, 3, sizeof(int), &pr);
            _cl.SetKernelArg(kernel, 4, sizeof(float), &h);
            _cl.SetKernelArg(kernel, 5, sizeof(int), &width);
            _cl.SetKernelArg(kernel, 6, sizeof(int), &height);

            nuint[] gws = { (nuint)width, (nuint)height };
            fixed (nuint* pGws = gws)
            {
                err = _cl.EnqueueNdrangeKernel(_queue, kernel, 2, null, pGws, null, 0, null, null);
            }

            CheckError(err, "EnqueueNdrangeKernel");

            fixed (void* pResultSlice = slice)
            fixed (nuint* pOrigin = origin)
            fixed (nuint* pRegion = region)
            {
                err = _cl.EnqueueReadImage(_queue, clOutputImage, true, pOrigin, pRegion,
                    (nuint)(width * sizeof(byte)), 0, pResultSlice, 0, null, null);
                CheckError(err, "EnqueueReadImage");
            }

            Buffer.BlockCopy(slice, 0, resultData, z * width * height, slice.Length);
            onProgress((float)(z + 1) / depth);
        }

        _cl.ReleaseMemObject(clInputImage);
        _cl.ReleaseMemObject(clOutputImage);
        _cl.Finish(_queue);
    }

    private void ProcessNonLocalMeans3DGPU(byte[] sourceData, byte[] resultData, int width, int height, int depth,
        float searchRadius, float patchRadius, float h, Action<float> onProgress, Action<string> onStatus)
    {
        onStatus("Running Non-Local Means (3D) on GPU...");
        var kernel = _kernels["nlm_3d"];
        var err = 0;

        var sr = (int)searchRadius;
        var pr = (int)patchRadius;

        fixed (byte* pSourceData = sourceData)
        {
            var clSrc = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(sourceData.Length * sizeof(byte)), pSourceData, &err);
            CheckError(err, "CreateBuffer (source)");

            var clDst = _cl.CreateBuffer(_context, MemFlags.WriteOnly,
                (nuint)(resultData.Length * sizeof(byte)), null, &err);
            CheckError(err, "CreateBuffer (dest)");

            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &clSrc);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &clDst);
            _cl.SetKernelArg(kernel, 2, sizeof(int), &width);
            _cl.SetKernelArg(kernel, 3, sizeof(int), &height);
            _cl.SetKernelArg(kernel, 4, sizeof(int), &depth);
            _cl.SetKernelArg(kernel, 5, sizeof(int), &sr);
            _cl.SetKernelArg(kernel, 6, sizeof(int), &pr);
            _cl.SetKernelArg(kernel, 7, sizeof(float), &h);

            var chunk = Math.Max(1, Math.Min(16, depth / 4));
            for (var z0 = 0; z0 < depth; z0 += chunk)
            {
                var z1 = Math.Min(z0 + chunk, depth);
                nuint[] gws = { (nuint)width, (nuint)height, (nuint)(z1 - z0) };
                nuint[] offs = { 0, 0, (nuint)z0 };
                fixed (nuint* pGws = gws)
                fixed (nuint* pOffs = offs)
                {
                    err = _cl.EnqueueNdrangeKernel(_queue, kernel, 3, pOffs, pGws, null, 0, null, null);
                }

                CheckError(err, "EnqueueNdrangeKernel");
                _cl.Finish(_queue);
                onStatus($"Processing NLM 3D chunks {z0}-{z1}/{depth}");
                onProgress((float)z1 / depth);
            }

            fixed (byte* pResult = resultData)
            {
                err = _cl.EnqueueReadBuffer(_queue, clDst, true, 0, (nuint)(resultData.Length * sizeof(byte)), pResult,
                    0, null, null);
                CheckError(err, "EnqueueReadBuffer");
            }

            _cl.ReleaseMemObject(clSrc);
            _cl.ReleaseMemObject(clDst);
            _cl.Finish(_queue);
        }
    }

    private void ProcessSlicesGPU(byte[] sourceData, byte[] resultData, int width, int height, int depth,
        nint kernel, nint[] extraBuffers, int[] intParams,
        Action<float> onProgress, Action<string> onStatus)
    {
        var err = 0;

        var imageFormat = new ImageFormat(ChannelOrder.R, ChannelType.UnormInt8);

        ImageDesc imageDesc = default;
        imageDesc.ImageType = MemObjectType.Image2D;
        imageDesc.ImageWidth = (nuint)width;
        imageDesc.ImageHeight = (nuint)height;
        imageDesc.ImageDepth = 0;
        imageDesc.ImageArraySize = 0;
        imageDesc.ImageRowPitch = 0;
        imageDesc.ImageSlicePitch = 0;
        imageDesc.NumMipLevels = 0;
        imageDesc.NumSamples = 0;

        var clInputImage = _cl.CreateImage(_context, MemFlags.ReadOnly, &imageFormat, &imageDesc, null, &err);
        CheckError(err, "CreateImage (input)");
        var clOutputImage = _cl.CreateImage(_context, MemFlags.WriteOnly, &imageFormat, &imageDesc, null, &err);
        CheckError(err, "CreateImage (output)");

        nuint[] origin = { 0, 0, 0 };
        nuint[] region = { (nuint)width, (nuint)height, 1 };

        for (var z = 0; z < depth; z++)
        {
            onStatus($"Processing slice {z + 1}/{depth}");

            var slice = new byte[width * height];
            Buffer.BlockCopy(sourceData, z * width * height, slice, 0, slice.Length);

            fixed (void* pSlice = slice)
            fixed (nuint* pOrigin = origin)
            fixed (nuint* pRegion = region)
            {
                err = _cl.EnqueueWriteImage(_queue, clInputImage, true, pOrigin, pRegion,
                    (nuint)(width * sizeof(byte)), 0, pSlice, 0, null, null);
                CheckError(err, "EnqueueWriteImage");
            }

            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &clInputImage);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &clOutputImage);

            for (var i = 0; i < extraBuffers.Length; i++)
            {
                var buf = extraBuffers[i];
                _cl.SetKernelArg(kernel, (uint)(2 + i), (nuint)sizeof(nint), &buf);
            }

            for (var i = 0; i < intParams.Length; i++)
            {
                var v = intParams[i];
                _cl.SetKernelArg(kernel, (uint)(2 + extraBuffers.Length + i), sizeof(int), &v);
            }

            nuint[] gws = { (nuint)width, (nuint)height };
            fixed (nuint* pGws = gws)
            {
                err = _cl.EnqueueNdrangeKernel(_queue, kernel, 2, null, pGws, null, 0, null, null);
            }

            CheckError(err, "EnqueueNdrangeKernel");

            fixed (void* pResultSlice = slice)
            fixed (nuint* pOrigin = origin)
            fixed (nuint* pRegion = region)
            {
                err = _cl.EnqueueReadImage(_queue, clOutputImage, true, pOrigin, pRegion,
                    (nuint)(width * sizeof(byte)), 0, pResultSlice, 0, null, null);
                CheckError(err, "EnqueueReadImage");
            }

            Buffer.BlockCopy(slice, 0, resultData, z * width * height, slice.Length);
            onProgress((float)(z + 1) / depth);
        }

        _cl.ReleaseMemObject(clInputImage);
        _cl.ReleaseMemObject(clOutputImage);
        _cl.Finish(_queue);
    }

    private static float[] CreateGaussianKernel(int size, float sigma)
    {
        var k = new float[size * size];
        var r = size / 2;
        float sum = 0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            int dx = x - r, dy = y - r;
            float d2 = dx * dx + dy * dy;
            var v = (float)Math.Exp(-d2 / (2 * sigma * sigma));
            k[y * size + x] = v;
            sum += v;
        }

        for (var i = 0; i < k.Length; i++) k[i] /= sum;
        return k;
    }

    private void CheckError(int err, string op)
    {
        if (err != (int)CLEnum.Success)
            throw new Exception($"OpenCL Error on {op}: {(CLEnum)err}");
    }

    #region OpenCL Kernel Sources

    private const string GaussianKernelSource = @"
        __constant sampler_t sampler = CLK_NORMALIZED_COORDS_FALSE | CLK_ADDRESS_CLAMP_TO_EDGE | CLK_FILTER_NEAREST;
        __kernel void gaussian_2d(__read_only image2d_t src,__write_only image2d_t dst,__constant float* kernel_weights,int kernel_radius){
            int2 pos=(int2)(get_global_id(0),get_global_id(1));
            float4 sum=(float4)(0.0f);
            int kernel_dim=kernel_radius*2+1;
            for(int j=-kernel_radius;j<=kernel_radius;++j){
                for(int i=-kernel_radius;i<=kernel_radius;++i){
                    float w=kernel_weights[(j+kernel_radius)*kernel_dim+(i+kernel_radius)];
                    sum+=read_imagef(src,sampler,pos+(int2)(i,j))*w;
                }
            }
            write_imagef(dst,pos,sum);
        }";

    private const string MedianKernelSource = @"
        __constant sampler_t sampler = CLK_NORMALIZED_COORDS_FALSE | CLK_ADDRESS_CLAMP_TO_EDGE | CLK_FILTER_NEAREST;
        __kernel void median_2d(__read_only image2d_t src,__write_only image2d_t dst,int kernel_radius){
            int2 pos=(int2)(get_global_id(0),get_global_id(1));
            int ks=kernel_radius*2+1; int N=ks*ks; float values[121]; int c=0;
            for(int j=-kernel_radius;j<=kernel_radius;++j) for(int i=-kernel_radius;i<=kernel_radius;++i){
                values[c++]=read_imagef(src,sampler,pos+(int2)(i,j)).x;
            }
            for(int i=0;i<N-1;i++) for(int j=0;j<N-i-1;j++){ if(values[j]>values[j+1]){ float t=values[j]; values[j]=values[j+1]; values[j+1]=t; } }
            float m=values[N/2]; write_imagef(dst,pos,(float4)(m,m,0.0f,1.0f));
        }";

    private const string MeanKernelSource = @"
        __constant sampler_t sampler = CLK_NORMALIZED_COORDS_FALSE | CLK_ADDRESS_CLAMP_TO_EDGE | CLK_FILTER_NEAREST;
        __kernel void mean_2d(__read_only image2d_t src,__write_only image2d_t dst,int kernel_radius){
            int2 pos=(int2)(get_global_id(0),get_global_id(1));
            float sum=0.0f; int count=0;
            for(int j=-kernel_radius;j<=kernel_radius;++j){
                for(int i=-kernel_radius;i<=kernel_radius;++i){
                    sum+=read_imagef(src,sampler,pos+(int2)(i,j)).x;
                    count++;
                }
            }
            float mean=sum/(float)count;
            write_imagef(dst,pos,(float4)(mean,mean,mean,1.0f));
        }";

    private const string BilateralKernelSource = @"
        __constant sampler_t sampler = CLK_NORMALIZED_COORDS_FALSE | CLK_ADDRESS_CLAMP_TO_EDGE | CLK_FILTER_NEAREST;
        __kernel void bilateral_2d(__read_only image2d_t src,__write_only image2d_t dst,__constant float* params,int kernel_radius){
            int2 pos=(int2)(get_global_id(0),get_global_id(1));
            float sigma_space=params[0], sigma_color=params[1];
            float center_val=read_imagef(src,sampler,pos).x;
            float weighted_sum=0.0f, total_weight=0.0f;
            for(int j=-kernel_radius;j<=kernel_radius;++j){
                for(int i=-kernel_radius;i<=kernel_radius;++i){
                    float neighbor_val=read_imagef(src,sampler,pos+(int2)(i,j)).x;
                    float dist_sq=(float)(i*i+j*j);
                    float color_dist=fabs(neighbor_val-center_val);
                    float spatial_weight=exp(-dist_sq/(2.0f*sigma_space*sigma_space));
                    float color_weight=exp(-color_dist*color_dist/(2.0f*sigma_color*sigma_color));
                    float weight=spatial_weight*color_weight;
                    weighted_sum+=neighbor_val*weight;
                    total_weight+=weight;
                }
            }
            float result=weighted_sum/total_weight;
            write_imagef(dst,pos,(float4)(result,result,result,1.0f));
        }";

    private const string SobelKernelSource = @"
        __constant sampler_t sampler = CLK_NORMALIZED_COORDS_FALSE | CLK_ADDRESS_CLAMP_TO_EDGE | CLK_FILTER_NEAREST;
        __kernel void sobel_2d(__read_only image2d_t src,__write_only image2d_t dst){
            int2 p=(int2)(get_global_id(0),get_global_id(1));
            float gx=0.0f,gy=0.0f;
            gx+=-1*read_imagef(src,sampler,p+(int2)(-1,-1)).x; gx+= 1*read_imagef(src,sampler,p+(int2)( 1,-1)).x;
            gx+=-2*read_imagef(src,sampler,p+(int2)(-1, 0)).x; gx+= 2*read_imagef(src,sampler,p+(int2)( 1, 0)).x;
            gx+=-1*read_imagef(src,sampler,p+(int2)(-1, 1)).x; gx+= 1*read_imagef(src,sampler,p+(int2)( 1, 1)).x;
            gy+=-1*read_imagef(src,sampler,p+(int2)(-1,-1)).x; gy+=-2*read_imagef(src,sampler,p+(int2)( 0,-1)).x; gy+=-1*read_imagef(src,sampler,p+(int2)( 1,-1)).x;
            gy+= 1*read_imagef(src,sampler,p+(int2)(-1, 1)).x; gy+= 2*read_imagef(src,sampler,p+(int2)( 0, 1)).x; gy+= 1*read_imagef(src,sampler,p+(int2)( 1, 1)).x;
            float mag=clamp(sqrt(gx*gx+gy*gy)/255.0f,0.0f,1.0f);
            write_imagef(dst,p,(float4)(mag,mag,mag,1.0f));
        }";

    private const string CannyKernelSource = @"
        __constant sampler_t sampler = CLK_NORMALIZED_COORDS_FALSE | CLK_ADDRESS_CLAMP_TO_EDGE | CLK_FILTER_NEAREST;
        __kernel void canny_gradient(__read_only image2d_t src,__write_only image2d_t dst,__constant float* thresholds){
            int2 p=(int2)(get_global_id(0),get_global_id(1));
            float gx=0.0f,gy=0.0f;
            gx+=-1*read_imagef(src,sampler,p+(int2)(-1,-1)).x; gx+= 1*read_imagef(src,sampler,p+(int2)( 1,-1)).x;
            gx+=-2*read_imagef(src,sampler,p+(int2)(-1, 0)).x; gx+= 2*read_imagef(src,sampler,p+(int2)( 1, 0)).x;
            gx+=-1*read_imagef(src,sampler,p+(int2)(-1, 1)).x; gx+= 1*read_imagef(src,sampler,p+(int2)( 1, 1)).x;
            gy+=-1*read_imagef(src,sampler,p+(int2)(-1,-1)).x; gy+=-2*read_imagef(src,sampler,p+(int2)( 0,-1)).x; gy+=-1*read_imagef(src,sampler,p+(int2)( 1,-1)).x;
            gy+= 1*read_imagef(src,sampler,p+(int2)(-1, 1)).x; gy+= 2*read_imagef(src,sampler,p+(int2)( 0, 1)).x; gy+= 1*read_imagef(src,sampler,p+(int2)( 1, 1)).x;
            float mag=sqrt(gx*gx+gy*gy)/255.0f;
            float low_thresh=thresholds[0], high_thresh=thresholds[1];
            float result = mag > high_thresh ? 1.0f : (mag > low_thresh ? 0.5f : 0.0f);
            write_imagef(dst,p,(float4)(result,result,result,1.0f));
        }";

    private const string UnsharpKernelSource = @"
        __constant sampler_t sampler = CLK_NORMALIZED_COORDS_FALSE | CLK_ADDRESS_CLAMP_TO_EDGE | CLK_FILTER_NEAREST;
        __kernel void unsharp_2d(__read_only image2d_t src,__write_only image2d_t dst,__constant float* kernel_weights,__constant float* params,int kernel_radius){
            int2 pos=(int2)(get_global_id(0),get_global_id(1));
            float amount=params[0];
            float original=read_imagef(src,sampler,pos).x;
            float4 blurred=(float4)(0.0f);
            int kernel_dim=kernel_radius*2+1;
            for(int j=-kernel_radius;j<=kernel_radius;++j){
                for(int i=-kernel_radius;i<=kernel_radius;++i){
                    float w=kernel_weights[(j+kernel_radius)*kernel_dim+(i+kernel_radius)];
                    blurred+=read_imagef(src,sampler,pos+(int2)(i,j))*w;
                }
            }
            float sharpened=original+amount*(original-blurred.x);
            sharpened=clamp(sharpened,0.0f,1.0f);
            write_imagef(dst,pos,(float4)(sharpened,sharpened,sharpened,1.0f));
        }";

    private const string NonLocalMeans2DKernelSource = @"
        __constant sampler_t sampler = CLK_NORMALIZED_COORDS_FALSE | CLK_ADDRESS_CLAMP_TO_EDGE | CLK_FILTER_NEAREST;
        __kernel void nlm_2d(__read_only image2d_t src,__write_only image2d_t dst,int sr,int pr,float h,int width,int height){
            int2 pos=(int2)(get_global_id(0),get_global_id(1));
            if(pos.x>=width||pos.y>=height) return;
            float h2=h*h, tw=0.0f, ws=0.0f;
            for(int sy=-sr;sy<=sr;sy++) for(int sx=-sr;sx<=sr;sx++){
                int2 q=pos+(int2)(sx,sy); float pd=0.0f; int pc=0;
                for(int py=-pr;py<=pr;py++) for(int px=-pr;px<=pr;px++){
                    float d=read_imagef(src,sampler,pos+(int2)(px,py)).x - read_imagef(src,sampler,q+(int2)(px,py)).x;
                    pd+=d*d; pc++;
                }
                pd/= (float)pc;
                float w=exp(-max(pd-2.0f*h2,0.0f)/h2);
                ws += read_imagef(src,sampler,q).x * w; tw += w;
            }
            float r=ws/tw; write_imagef(dst,pos,(float4)(r,r,r,1.0f));
        }";

    private const string NonLocalMeans3DKernelSource = @"
        __kernel void nlm_3d(__global uchar* src,__global uchar* dst,int W,int H,int D,int sr,int pr,float h){
            int x=get_global_id(0), y=get_global_id(1), z=get_global_id(2); if(x>=W||y>=H||z>=D) return;
            float h2=h*h, tw=0.0f, ws=0.0f;
            long c=(long)z*W*H + (long)y*W + x;
            const int M=7; float cp[M*M*M]; int as=2*pr+1;
            int k=0;
            for(int pz=-pr;pz<=pr;pz++){
                int cz=z+pz;
                for(int py=-pr;py<=pr;py++){
                    int cy=y+py;
                    for(int px=-pr;px<=pr;px++){
                        int cx=x+px;
                        if(cz>=0&&cz<D&&cy>=0&&cy<H&&cx>=0&&cx<W){
                            long idx=(long)cz*W*H + (long)cy*W + cx;
                            cp[k]=(float)src[idx];
                        } else cp[k]=0.0f;
                        k++;
                    }
                }
            }
            int z0=max(0,z-sr), z1=min(D-1,z+sr), y0=max(0,y-sr), y1=min(H-1,y+sr), x0=max(0,x-sr), x1=min(W-1,x+sr);
            for(int zz=z0;zz<=z1;zz++) for(int yy=y0;yy<=y1;yy++) for(int xx=x0;xx<=x1;xx++){
                float pd=0.0f; k=0;
                for(int pz=-pr;pz<=pr;pz++){
                    int nz=zz+pz;
                    for(int py=-pr;py<=pr;py++){
                        int ny=yy+py;
                        for(int px=-pr;px<=pr;px++){
                            int nx=xx+px;
                            if(nz>=0&&nz<D&&ny>=0&&ny<H&&nx>=0&&nx<W){
                                long idx=(long)nz*W*H + (long)ny*W + nx;
                                float diff=cp[k] - (float)src[idx];
                                pd+=diff*diff;
                            }
                            k++;
                        }
                    }
                }
                pd/= (float)(as*as*as);
                float w=exp(-max(pd-2.0f*h2,0.0f)/h2);
                long q=(long)zz*W*H + (long)yy*W + (long)xx;
                ws+=(float)src[q]*w; tw+=w;
            }
            dst[c]=(uchar)clamp(tw>0.0f? ws/tw : src[c],0.0f,255.0f);
        }";

    #endregion
}