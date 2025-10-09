// GeoscientistToolkit/Data/CtImageStack/StackRegistration.cs

using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Data.CtImageStack;

/// <summary>
///     Alignment direction for stack registration
/// </summary>
public enum RegistrationAlignment
{
    AlongX, // Stacks aligned horizontally (side by side)
    AlongY, // Stacks aligned vertically (top and bottom)
    AlongZ // Stacks aligned depth-wise (front and back)
}

/// <summary>
///     Registration method
/// </summary>
public enum RegistrationMethod
{
    CPU_SIMD,
    OpenCL_GPU
}

/// <summary>
///     Performs image registration of CT stacks using normalized cross-correlation
/// </summary>
public class StackRegistration
{
    private readonly RegistrationMethod _method;
    private CL _cl;
    private nint _context;
    private nint _device;
    private nint _queue;

    public StackRegistration(RegistrationMethod method = RegistrationMethod.CPU_SIMD)
    {
        _method = method;

        if (_method == RegistrationMethod.OpenCL_GPU) InitializeOpenCL();
    }

    /// <summary>
    ///     Registers two CT stacks and creates a combined volume
    /// </summary>
    public async Task<ChunkedVolume> RegisterStacksAsync(
        CtImageStackDataset dataset1,
        CtImageStackDataset dataset2,
        RegistrationAlignment alignment,
        int maxShift = 50,
        IProgress<(float progress, string status)> progress = null,
        CancellationToken cancellationToken = default)
    {
        Logger.Log($"[StackRegistration] Starting registration: {dataset1.Name} + {dataset2.Name}");
        Logger.Log($"[StackRegistration] Alignment: {alignment}, Method: {_method}");

        // Ensure both datasets are loaded
        if (dataset1.VolumeData == null) dataset1.Load();
        if (dataset2.VolumeData == null) dataset2.Load();

        var vol1 = dataset1.VolumeData;
        var vol2 = dataset2.VolumeData;

        // Validate dimensions
        ValidateDimensions(vol1, vol2, alignment);

        // Calculate output dimensions
        var (outWidth, outHeight, outDepth) = CalculateOutputDimensions(vol1, vol2, alignment);
        Logger.Log($"[StackRegistration] Output dimensions: {outWidth}x{outHeight}x{outDepth}");

        // Create output volume
        var outputVolume = new ChunkedVolume(outWidth, outHeight, outDepth);

        // Perform registration based on alignment
        switch (alignment)
        {
            case RegistrationAlignment.AlongZ:
                await RegisterAlongZAsync(vol1, vol2, outputVolume, maxShift, progress, cancellationToken);
                break;
            case RegistrationAlignment.AlongX:
                await RegisterAlongXAsync(vol1, vol2, outputVolume, maxShift, progress, cancellationToken);
                break;
            case RegistrationAlignment.AlongY:
                await RegisterAlongYAsync(vol1, vol2, outputVolume, maxShift, progress, cancellationToken);
                break;
        }

        Logger.Log("[StackRegistration] Registration completed successfully");
        return outputVolume;
    }

    private void ValidateDimensions(ChunkedVolume vol1, ChunkedVolume vol2, RegistrationAlignment alignment)
    {
        switch (alignment)
        {
            case RegistrationAlignment.AlongZ:
                if (vol1.Width != vol2.Width || vol1.Height != vol2.Height)
                    throw new ArgumentException("For Z-alignment, volumes must have same Width and Height");
                break;
            case RegistrationAlignment.AlongX:
                if (vol1.Height != vol2.Height || vol1.Depth != vol2.Depth)
                    throw new ArgumentException("For X-alignment, volumes must have same Height and Depth");
                break;
            case RegistrationAlignment.AlongY:
                if (vol1.Width != vol2.Width || vol1.Depth != vol2.Depth)
                    throw new ArgumentException("For Y-alignment, volumes must have same Width and Depth");
                break;
        }
    }

    private (int width, int height, int depth) CalculateOutputDimensions(
        ChunkedVolume vol1, ChunkedVolume vol2, RegistrationAlignment alignment)
    {
        return alignment switch
        {
            RegistrationAlignment.AlongZ => (vol1.Width, vol1.Height, vol1.Depth + vol2.Depth),
            RegistrationAlignment.AlongX => (vol1.Width + vol2.Width, vol1.Height, vol1.Depth),
            RegistrationAlignment.AlongY => (vol1.Width, vol1.Height + vol2.Height, vol1.Depth),
            _ => throw new ArgumentException("Invalid alignment")
        };
    }

    private async Task RegisterAlongZAsync(
        ChunkedVolume vol1, ChunkedVolume vol2, ChunkedVolume output,
        int maxShift, IProgress<(float, string)> progress, CancellationToken ct)
    {
        var width = vol1.Width;
        var height = vol1.Height;

        // Copy first volume as-is
        progress?.Report((0.1f, "Copying first volume..."));
        await Task.Run(() =>
        {
            for (var z = 0; z < vol1.Depth; z++)
            {
                if (ct.IsCancellationRequested) return;

                var slice = new byte[width * height];
                vol1.ReadSliceZ(z, slice);
                output.WriteSliceZ(z, slice);

                if (z % 10 == 0)
                    progress?.Report((0.1f + 0.2f * z / vol1.Depth, $"Copying slice {z}/{vol1.Depth}"));
            }
        }, ct);

        // Find optimal alignment using overlap region
        progress?.Report((0.3f, "Computing optimal alignment..."));

        var overlapSize = Math.Min(20, Math.Min(vol1.Depth, vol2.Depth) / 2);
        var lastSliceVol1 = new byte[width * height];
        var firstSliceVol2 = new byte[width * height];

        // Get average of last slices from vol1
        var avgSlice1 = new float[width * height];
        for (var z = vol1.Depth - overlapSize; z < vol1.Depth; z++)
        {
            vol1.ReadSliceZ(z, lastSliceVol1);
            for (var i = 0; i < lastSliceVol1.Length; i++)
                avgSlice1[i] += lastSliceVol1[i];
        }

        for (var i = 0; i < avgSlice1.Length; i++)
            avgSlice1[i] /= overlapSize;

        // Get average of first slices from vol2
        var avgSlice2 = new float[width * height];
        for (var z = 0; z < overlapSize; z++)
        {
            vol2.ReadSliceZ(z, firstSliceVol2);
            for (var i = 0; i < firstSliceVol2.Length; i++)
                avgSlice2[i] += firstSliceVol2[i];
        }

        for (var i = 0; i < avgSlice2.Length; i++)
            avgSlice2[i] /= overlapSize;

        // Find best XY offset
        var (bestOffsetX, bestOffsetY) = await FindBestOffset2DAsync(
            avgSlice1, avgSlice2, width, height, maxShift, ct);

        Logger.Log($"[StackRegistration] Optimal offset: ({bestOffsetX}, {bestOffsetY})");

        // Copy second volume with offset
        progress?.Report((0.5f, "Copying second volume with alignment..."));
        await Task.Run(() =>
        {
            for (var z = 0; z < vol2.Depth; z++)
            {
                if (ct.IsCancellationRequested) return;

                var slice = new byte[width * height];
                vol2.ReadSliceZ(z, slice);

                var shiftedSlice = ApplyOffset2D(slice, width, height, bestOffsetX, bestOffsetY);
                output.WriteSliceZ(vol1.Depth + z, shiftedSlice);

                if (z % 10 == 0)
                    progress?.Report((0.5f + 0.5f * z / vol2.Depth,
                        $"Registering slice {z}/{vol2.Depth}"));
            }
        }, ct);

        progress?.Report((1.0f, "Registration complete!"));
    }

    private async Task RegisterAlongXAsync(
        ChunkedVolume vol1, ChunkedVolume vol2, ChunkedVolume output,
        int maxShift, IProgress<(float, string)> progress, CancellationToken ct)
    {
        var height = vol1.Height;
        var depth = vol1.Depth;

        progress?.Report((0.1f, "Analyzing overlap region..."));

        // Get overlap slices for alignment (YZ plane)
        var overlapSize = Math.Min(20, Math.Min(vol1.Width, vol2.Width) / 2);
        var avgSlice1 = new float[height * depth];
        var avgSlice2 = new float[height * depth];

        // Average last YZ slices of vol1
        for (var x = vol1.Width - overlapSize; x < vol1.Width; x++)
        for (var z = 0; z < depth; z++)
        for (var y = 0; y < height; y++)
            avgSlice1[z * height + y] += vol1[x, y, z];

        for (var i = 0; i < avgSlice1.Length; i++)
            avgSlice1[i] /= overlapSize;

        // Average first YZ slices of vol2
        for (var x = 0; x < overlapSize; x++)
        for (var z = 0; z < depth; z++)
        for (var y = 0; y < height; y++)
            avgSlice2[z * height + y] += vol2[x, y, z];

        for (var i = 0; i < avgSlice2.Length; i++)
            avgSlice2[i] /= overlapSize;

        // Find best YZ offset
        var (bestOffsetY, bestOffsetZ) = await FindBestOffset2DAsync(
            avgSlice1, avgSlice2, height, depth, maxShift, ct);

        Logger.Log($"[StackRegistration] Optimal offset: (Y={bestOffsetY}, Z={bestOffsetZ})");

        // Combine volumes
        progress?.Report((0.5f, "Combining volumes..."));
        await Task.Run(() =>
        {
            for (var z = 0; z < depth; z++)
            {
                if (ct.IsCancellationRequested) return;

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < vol1.Width; x++) output[x, y, z] = vol1[x, y, z];

                    for (var x = 0; x < vol2.Width; x++)
                    {
                        var srcY = y - bestOffsetY;
                        var srcZ = z - bestOffsetZ;

                        if (srcY >= 0 && srcY < vol2.Height && srcZ >= 0 && srcZ < vol2.Depth)
                            output[vol1.Width + x, y, z] = vol2[x, srcY, srcZ];
                    }
                }

                if (z % 10 == 0)
                    progress?.Report((0.5f + 0.5f * z / depth, $"Processing slice {z}/{depth}"));
            }
        }, ct);

        progress?.Report((1.0f, "Registration complete!"));
    }

    private async Task RegisterAlongYAsync(
        ChunkedVolume vol1, ChunkedVolume vol2, ChunkedVolume output,
        int maxShift, IProgress<(float, string)> progress, CancellationToken ct)
    {
        var width = vol1.Width;
        var depth = vol1.Depth;

        progress?.Report((0.1f, "Analyzing overlap region..."));

        // Get overlap slices for alignment (XZ plane)
        var overlapSize = Math.Min(20, Math.Min(vol1.Height, vol2.Height) / 2);
        var avgSlice1 = new float[width * depth];
        var avgSlice2 = new float[width * depth];

        // Average last XZ slices of vol1
        for (var y = vol1.Height - overlapSize; y < vol1.Height; y++)
        for (var z = 0; z < depth; z++)
        for (var x = 0; x < width; x++)
            avgSlice1[z * width + x] += vol1[x, y, z];

        for (var i = 0; i < avgSlice1.Length; i++)
            avgSlice1[i] /= overlapSize;

        // Average first XZ slices of vol2
        for (var y = 0; y < overlapSize; y++)
        for (var z = 0; z < depth; z++)
        for (var x = 0; x < width; x++)
            avgSlice2[z * width + x] += vol2[x, y, z];

        for (var i = 0; i < avgSlice2.Length; i++)
            avgSlice2[i] /= overlapSize;

        // Find best XZ offset
        var (bestOffsetX, bestOffsetZ) = await FindBestOffset2DAsync(
            avgSlice1, avgSlice2, width, depth, maxShift, ct);

        Logger.Log($"[StackRegistration] Optimal offset: (X={bestOffsetX}, Z={bestOffsetZ})");

        // Combine volumes
        progress?.Report((0.5f, "Combining volumes..."));
        await Task.Run(() =>
        {
            for (var z = 0; z < depth; z++)
            {
                if (ct.IsCancellationRequested) return;

                for (var y = 0; y < vol1.Height; y++)
                for (var x = 0; x < width; x++)
                    output[x, y, z] = vol1[x, y, z];

                for (var y = 0; y < vol2.Height; y++)
                for (var x = 0; x < width; x++)
                {
                    var srcX = x - bestOffsetX;
                    var srcZ = z - bestOffsetZ;

                    if (srcX >= 0 && srcX < vol2.Width && srcZ >= 0 && srcZ < vol2.Depth)
                        output[x, vol1.Height + y, z] = vol2[srcX, y, srcZ];
                }

                if (z % 10 == 0)
                    progress?.Report((0.5f + 0.5f * z / depth, $"Processing slice {z}/{depth}"));
            }
        }, ct);

        progress?.Report((1.0f, "Registration complete!"));
    }

    private async Task<(int offsetX, int offsetY)> FindBestOffset2DAsync(
        float[] slice1, float[] slice2, int width, int height, int maxShift, CancellationToken ct)
    {
        if (_method == RegistrationMethod.OpenCL_GPU && _context != nint.Zero)
            return await FindBestOffsetOpenCLAsync(slice1, slice2, width, height, maxShift, ct);

        return await FindBestOffsetCPUAsync(slice1, slice2, width, height, maxShift, ct);
    }

    private async Task<(int offsetX, int offsetY)> FindBestOffsetCPUAsync(
        float[] slice1, float[] slice2, int width, int height, int maxShift, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var bestScore = float.MinValue;
            var bestOffsetX = 0;
            var bestOffsetY = 0;

            // Search in a grid around the center
            for (var dy = -maxShift; dy <= maxShift; dy += 2)
            {
                if (ct.IsCancellationRequested) break;

                for (var dx = -maxShift; dx <= maxShift; dx += 2)
                {
                    var score = ComputeNCCSIMD(slice1, slice2, width, height, dx, dy);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestOffsetX = dx;
                        bestOffsetY = dy;
                    }
                }
            }

            // Refine search around best position
            for (var dy = bestOffsetY - 2; dy <= bestOffsetY + 2; dy++)
            {
                if (ct.IsCancellationRequested) break;

                for (var dx = bestOffsetX - 2; dx <= bestOffsetX + 2; dx++)
                {
                    if (Math.Abs(dx - bestOffsetX) > 2 || Math.Abs(dy - bestOffsetY) > 2)
                        continue;

                    var score = ComputeNCCSIMD(slice1, slice2, width, height, dx, dy);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestOffsetX = dx;
                        bestOffsetY = dy;
                    }
                }
            }

            Logger.Log($"[StackRegistration] Best NCC score: {bestScore:F4}");
            return (bestOffsetX, bestOffsetY);
        }, ct);
    }

    private unsafe float ComputeNCCSIMD(float[] img1, float[] img2, int width, int height, int dx, int dy)
    {
        // Determine overlap region
        var x1Start = Math.Max(0, dx);
        var x1End = Math.Min(width, width + dx);
        var y1Start = Math.Max(0, dy);
        var y1End = Math.Min(height, height + dy);

        var x2Start = Math.Max(0, -dx);
        var y2Start = Math.Max(0, -dy);

        var overlapWidth = x1End - x1Start;
        var overlapHeight = y1End - y1Start;

        if (overlapWidth <= 0 || overlapHeight <= 0)
            return float.MinValue;

        fixed (float* pImg1 = img1, pImg2 = img2)
        {
            // Compute means using SIMD
            double sum1 = 0, sum2 = 0;
            var count = 0;

            for (var y = 0; y < overlapHeight; y++)
            {
                var idx1 = (y1Start + y) * width + x1Start;
                var idx2 = (y2Start + y) * width + x2Start;

                var i = 0;

                // SIMD processing for AVX2 (8 floats at a time)
                if (Avx2.IsSupported && overlapWidth >= 8)
                {
                    var vSum1 = Vector256<float>.Zero;
                    var vSum2 = Vector256<float>.Zero;

                    for (; i <= overlapWidth - 8; i += 8)
                    {
                        var v1 = Avx.LoadVector256(pImg1 + idx1 + i);
                        var v2 = Avx.LoadVector256(pImg2 + idx2 + i);

                        vSum1 = Avx.Add(vSum1, v1);
                        vSum2 = Avx.Add(vSum2, v2);
                    }

                    // Horizontal sum
                    for (var j = 0; j < 8; j++)
                    {
                        sum1 += vSum1.GetElement(j);
                        sum2 += vSum2.GetElement(j);
                    }
                }
                // Fallback to Vector<float> (4 floats) if AVX2 not available
                else if (Vector.IsHardwareAccelerated && overlapWidth >= Vector<float>.Count)
                {
                    var vSum1 = Vector<float>.Zero;
                    var vSum2 = Vector<float>.Zero;

                    for (; i <= overlapWidth - Vector<float>.Count; i += Vector<float>.Count)
                    {
                        var v1 = new Vector<float>(img1, idx1 + i);
                        var v2 = new Vector<float>(img2, idx2 + i);

                        vSum1 += v1;
                        vSum2 += v2;
                    }

                    for (var j = 0; j < Vector<float>.Count; j++)
                    {
                        sum1 += vSum1[j];
                        sum2 += vSum2[j];
                    }
                }

                // Process remaining elements
                for (; i < overlapWidth; i++)
                {
                    sum1 += pImg1[idx1 + i];
                    sum2 += pImg2[idx2 + i];
                }

                count += overlapWidth;
            }

            if (count == 0) return float.MinValue;

            var mean1 = (float)(sum1 / count);
            var mean2 = (float)(sum2 / count);

            // Compute NCC
            double numerator = 0;
            double denom1 = 0, denom2 = 0;

            for (var y = 0; y < overlapHeight; y++)
            {
                var idx1 = (y1Start + y) * width + x1Start;
                var idx2 = (y2Start + y) * width + x2Start;

                var i = 0;

                if (Avx2.IsSupported && overlapWidth >= 8)
                {
                    var vMean1 = Vector256.Create(mean1);
                    var vMean2 = Vector256.Create(mean2);
                    var vNum = Vector256<float>.Zero;
                    var vDen1 = Vector256<float>.Zero;
                    var vDen2 = Vector256<float>.Zero;

                    for (; i <= overlapWidth - 8; i += 8)
                    {
                        var v1 = Avx.LoadVector256(pImg1 + idx1 + i);
                        var v2 = Avx.LoadVector256(pImg2 + idx2 + i);

                        var diff1 = Avx.Subtract(v1, vMean1);
                        var diff2 = Avx.Subtract(v2, vMean2);

                        vNum = Avx.Add(vNum, Avx.Multiply(diff1, diff2));
                        vDen1 = Avx.Add(vDen1, Avx.Multiply(diff1, diff1));
                        vDen2 = Avx.Add(vDen2, Avx.Multiply(diff2, diff2));
                    }

                    for (var j = 0; j < 8; j++)
                    {
                        numerator += vNum.GetElement(j);
                        denom1 += vDen1.GetElement(j);
                        denom2 += vDen2.GetElement(j);
                    }
                }
                else if (Vector.IsHardwareAccelerated && overlapWidth >= Vector<float>.Count)
                {
                    var vMean1 = new Vector<float>(mean1);
                    var vMean2 = new Vector<float>(mean2);
                    var vNum = Vector<float>.Zero;
                    var vDen1 = Vector<float>.Zero;
                    var vDen2 = Vector<float>.Zero;

                    for (; i <= overlapWidth - Vector<float>.Count; i += Vector<float>.Count)
                    {
                        var v1 = new Vector<float>(img1, idx1 + i);
                        var v2 = new Vector<float>(img2, idx2 + i);

                        var diff1 = v1 - vMean1;
                        var diff2 = v2 - vMean2;

                        vNum += diff1 * diff2;
                        vDen1 += diff1 * diff1;
                        vDen2 += diff2 * diff2;
                    }

                    for (var j = 0; j < Vector<float>.Count; j++)
                    {
                        numerator += vNum[j];
                        denom1 += vDen1[j];
                        denom2 += vDen2[j];
                    }
                }

                for (; i < overlapWidth; i++)
                {
                    var diff1 = pImg1[idx1 + i] - mean1;
                    var diff2 = pImg2[idx2 + i] - mean2;

                    numerator += diff1 * diff2;
                    denom1 += diff1 * diff1;
                    denom2 += diff2 * diff2;
                }
            }

            var denominator = Math.Sqrt(denom1 * denom2);
            if (denominator < 1e-10) return 0;

            return (float)(numerator / denominator);
        }
    }

    private byte[] ApplyOffset2D(byte[] slice, int width, int height, int dx, int dy)
    {
        var result = new byte[width * height];

        Parallel.For(0, height, y =>
        {
            var srcY = y - dy;
            if (srcY < 0 || srcY >= height) return;

            for (var x = 0; x < width; x++)
            {
                var srcX = x - dx;
                if (srcX < 0 || srcX >= width) continue;

                result[y * width + x] = slice[srcY * width + srcX];
            }
        });

        return result;
    }

    private unsafe void InitializeOpenCL()
    {
        try
        {
            _cl = CL.GetApi();

            // Get platform
            uint numPlatforms;
            _cl.GetPlatformIDs(0, null, &numPlatforms);
            if (numPlatforms == 0)
            {
                Logger.LogWarning("[StackRegistration] No OpenCL platforms found. Falling back to CPU.");
                return;
            }

            var platforms = new nint[numPlatforms];
            fixed (nint* pPlatforms = platforms)
            {
                _cl.GetPlatformIDs(numPlatforms, pPlatforms, null);
            }

            // Get GPU device
            uint numDevices;
            // FIX: Removed the incorrect (ulong) cast. Pass the enum directly.
            _cl.GetDeviceIDs(platforms[0], DeviceType.Gpu, 0, null, &numDevices);
            if (numDevices == 0)
            {
                Logger.LogWarning("[StackRegistration] No GPU devices found. Falling back to CPU.");
                return;
            }

            var devices = new nint[numDevices];
            fixed (nint* pDevices = devices)
            {
                // FIX: Removed the incorrect (ulong) cast here as well.
                _cl.GetDeviceIDs(platforms[0], DeviceType.Gpu, numDevices, pDevices, null);
            }

            _device = devices[0];

            // Create context
            int error;
            // FIX: The address of _device must be taken inside a 'fixed' block to prevent the GC from moving it.
            fixed (nint* pDevice = &_device)
            {
                _context = _cl.CreateContext(null, 1, pDevice, null, null, &error);
            }

            if (error != 0)
            {
                Logger.LogError($"[StackRegistration] Failed to create OpenCL context: {error}");
                return;
            }

            // Create command queue
            // FIX: Explicitly cast '0' to CommandQueueProperties to resolve the ambiguous method call.
            _queue = _cl.CreateCommandQueue(_context, _device, (CommandQueueProperties)0, &error);
            if (error != 0)
            {
                Logger.LogError($"[StackRegistration] Failed to create command queue: {error}");
                return;
            }

            Logger.Log("[StackRegistration] OpenCL initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[StackRegistration] OpenCL initialization failed: {ex.Message}");
            _context = nint.Zero;
        }
    }

    private async Task<(int offsetX, int offsetY)> FindBestOffsetOpenCLAsync(
        float[] slice1, float[] slice2, int width, int height, int maxShift, CancellationToken ct)
    {
        // Fallback to CPU if OpenCL not initialized
        if (_context == nint.Zero)
            return await FindBestOffsetCPUAsync(slice1, slice2, width, height, maxShift, ct);

        try
        {
            // For simplicity, we'll use CPU implementation
            // Full OpenCL implementation would require kernel compilation and buffer management
            Logger.Log("[StackRegistration] OpenCL registration not fully implemented, using CPU");
            return await FindBestOffsetCPUAsync(slice1, slice2, width, height, maxShift, ct);
        }
        catch (Exception ex)
        {
            Logger.LogError($"[StackRegistration] OpenCL processing failed: {ex.Message}");
            return await FindBestOffsetCPUAsync(slice1, slice2, width, height, maxShift, ct);
        }
    }

    public void Dispose()
    {
        if (_queue != nint.Zero)
        {
            _cl?.ReleaseCommandQueue(_queue);
            _queue = nint.Zero;
        }

        if (_context != nint.Zero)
        {
            _cl?.ReleaseContext(_context);
            _context = nint.Zero;
        }
    }
}