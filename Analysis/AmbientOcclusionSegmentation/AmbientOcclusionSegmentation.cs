// GAIA/Analysis/AmbientOcclusionSegmentation/AmbientOcclusionSegmentation.cs
//
// ================================================================================================
// Ambient Occlusion Segmentation for CT Image Stacks
// ================================================================================================
// Based on: "Cavity and Pore Segmentation in 3D Images with Ambient Occlusion"
// Authors: D. Baum, J. Titschack
// ZIB Report: ZR-16-17 (2016)
//
// This implementation provides GPU-accelerated (OpenCL) and SIMD CPU fallback for computing
// ambient occlusion fields in 3D volumetric data to segment pores and cavities that cannot
// be distinguished from surrounding material by grayscale values alone.
//
// KEY CONCEPT:
// - For every VOID voxel (background / not the analyzed material) rays are cast in all
//   directions and the "openness" is the fraction of rays that escape without hitting material.
// - Enclosed pores/cavities have LOW openness (rays are blocked by the surrounding material).
// - Open exterior space has HIGH openness (rays leave the volume unobstructed).
// - Material voxels are excluded from segmentation (openness fixed at 1.0).
// - Segmentation keeps void voxels whose openness falls below the threshold.
// ================================================================================================

using System.Diagnostics;
using System.Numerics;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using GAIA.Data.CtImageStack;
using GAIA.Util;
using Silk.NET.OpenCL;

namespace GAIA.Analysis.AmbientOcclusionSegmentation;

/// <summary>
/// Settings for ambient occlusion segmentation
/// </summary>
public class AmbientOcclusionSettings
{
    /// <summary>
    /// Number of rays to cast per voxel (default: 128)
    /// More rays = better accuracy but slower
    /// Typical range: 64-256
    /// </summary>
    public int RayCount { get; set; } = 128;

    /// <summary>
    /// Maximum ray length in voxels (default: 50)
    /// Longer rays capture larger-scale features
    /// </summary>
    public float RayLength { get; set; } = 50.0f;

    /// <summary>
    /// Material intensity threshold (0-255)
    /// Voxels above this are considered material
    /// (Only used when UseExistingMaterial = false)
    /// </summary>
    public byte MaterialThreshold { get; set; } = 128;

    /// <summary>
    /// Use existing material labels instead of grayscale threshold
    /// </summary>
    public bool UseExistingMaterial { get; set; } = false;

    /// <summary>
    /// Source material ID to analyze (when UseExistingMaterial = true)
    /// Finds cavities within this material
    /// </summary>
    public byte SourceMaterialId { get; set; } = 1;

    /// <summary>
    /// Openness threshold for segmentation (0.0-1.0)
    /// Void voxels whose openness is below this are segmented as pores/cavities
    /// Typical range: 0.5-0.95
    /// </summary>
    public float SegmentationThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Target material ID for segmented pores
    /// </summary>
    public byte TargetMaterialId { get; set; } = 1;

    /// <summary>
    /// Processing region (null = entire volume)
    /// </summary>
    public Region3D? Region { get; set; } = null;

    /// <summary>
    /// Use GPU acceleration if available
    /// </summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>
    /// Number of CPU threads for fallback
    /// </summary>
    public int CpuThreads { get; set; } = Environment.ProcessorCount;
    public int MaxWorkingSetMB { get; set; } = 256;
}

/// <summary>
/// 3D region for processing subset of volume
/// </summary>
public struct Region3D
{
    public int MinX, MinY, MinZ;
    public int MaxX, MaxY, MaxZ;

    public int Width => MaxX - MinX;
    public int Height => MaxY - MinY;
    public int Depth => MaxZ - MinZ;
}

/// <summary>
/// Result of ambient occlusion computation
/// </summary>
public class AmbientOcclusionResult
{
    /// <summary>
    /// Openness values (0.0-1.0) for each voxel: the fraction of rays escaping without
    /// hitting material. Material voxels are fixed at 1.0 so they are never segmented.
    /// </summary>
    public float[,,] AoField { get; set; }

    /// <summary>
    /// Binary segmentation mask (true = pore/cavity)
    /// </summary>
    public bool[,,] SegmentationMask { get; set; }

    /// <summary>
    /// Processing time in seconds
    /// </summary>
    public double ProcessingTime { get; set; }

    /// <summary>
    /// Throughput in voxels per second
    /// </summary>
    public long VoxelsPerSecond { get; set; }

    /// <summary>
    /// Acceleration type used (GPU, SIMD, CPU)
    /// </summary>
    public string AccelerationType { get; set; }
    public Region3D ProcessingRegion { get; set; }
    public int SamplingStep { get; set; } = 1;
    public int DatasetWidth { get; set; }
    public int DatasetHeight { get; set; }
    public int DatasetDepth { get; set; }
}

/// <summary>
/// Main class for ambient occlusion segmentation
/// </summary>
public class AmbientOcclusionSegmentation : IDisposable
{
    private readonly object _statsLock = new object();
    private float _progress;
    private string _currentStage = "";
    private readonly bool _gpuAvailable;

    // OpenCL resources
    private CL _cl;
    private nint _context;
    private nint _commandQueue;
    private nint _program;
    private nint _computeAoKernel;
    private nint _thresholdKernel;

    // Ray direction table (precomputed)
    private Vector3[] _rayDirections;

    public AmbientOcclusionSegmentation()
    {
        try
        {
            InitializeGpu();
            _gpuAvailable = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[AmbientOcclusionSegmentation] GPU initialization failed: {ex.Message}. Falling back to CPU.");
            _gpuAvailable = false;
        }
    }

    public float Progress
    {
        get { lock (_statsLock) { return _progress; } }
        set { lock (_statsLock) { _progress = value; } }
    }

    public string CurrentStage
    {
        get { lock (_statsLock) { return _currentStage; } }
        set { lock (_statsLock) { _currentStage = value; } }
    }

    public (string Message, Vector4 Color) GetAccelerationStatus()
    {
        var simdAvailable = Vector.IsHardwareAccelerated;
        if (_gpuAvailable) return ("GPU Available", new Vector4(0, 1, 0, 1));
        if (simdAvailable) return ("SIMD Available", new Vector4(0.5f, 1, 0, 1));
        return ("Multi-threaded CPU", new Vector4(1, 1, 0, 1));
    }

    /// <summary>
    /// Compute ambient occlusion field for the dataset
    /// </summary>
    public AmbientOcclusionResult ComputeAmbientOcclusion(
        CtImageStackDataset dataset,
        AmbientOcclusionSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (dataset?.VolumeData == null)
            throw new ArgumentException("Dataset or VolumeData is null");

        var stopwatch = Stopwatch.StartNew();
        Progress = 0f;
        CurrentStage = "Initializing";

        // Determine processing region
        var region = settings.Region ?? new Region3D
        {
            MinX = 0, MinY = 0, MinZ = 0,
            MaxX = dataset.Width,
            MaxY = dataset.Height,
            MaxZ = dataset.Depth
        };
        // GPU mode temporarily holds input, output, transfer and managed 3D arrays; budget
        // conservatively at 16 bytes per sampled voxel to avoid approaching OOM.
        var maximumWorkingVoxels = Math.Max(1L, (long)settings.MaxWorkingSetMB * 1024 * 1024 / 16);
        var regionVoxels = (long)region.Width * region.Height * region.Depth;
        var samplingStep = regionVoxels <= maximumWorkingVoxels ? 1 :
            Math.Max(1, (int)Math.Ceiling(Math.Pow(regionVoxels / (double)maximumWorkingVoxels, 1.0 / 3)));
        if (samplingStep > 1)
            Logger.LogWarning($"[AmbientOcclusion] Using adaptive {samplingStep}x sampling across the complete " +
                              $"region to keep the working set below {settings.MaxWorkingSetMB} MB.");

        // Precompute ray directions
        CurrentStage = "Generating ray directions";
        _rayDirections = GenerateUniformRayDirections(settings.RayCount);
        Progress = 0.05f;

        // Compute AO field
        float[,,] aoField;
        string accelType;

        if (settings.UseGpu && _gpuAvailable)
        {
            CurrentStage = "Computing AO (GPU)";
            aoField = ComputeAoFieldGpu(dataset, settings, region, samplingStep, cancellationToken);
            accelType = "GPU (OpenCL)";
        }
        else if (Vector.IsHardwareAccelerated)
        {
            CurrentStage = "Computing AO (SIMD)";
            aoField = ComputeAoFieldSimd(dataset, settings, region, samplingStep, cancellationToken);
            accelType = "SIMD CPU";
        }
        else
        {
            CurrentStage = "Computing AO (CPU)";
            aoField = ComputeAoFieldCpu(dataset, settings, region, samplingStep, cancellationToken);
            accelType = "Multi-threaded CPU";
        }

        Progress = 0.8f;

        // Apply threshold to generate segmentation mask
        CurrentStage = "Applying threshold";
        var mask = ApplyThreshold(aoField, settings.SegmentationThreshold);
        Progress = 0.95f;

        stopwatch.Stop();
        var totalVoxels = (long)region.Width * region.Height * region.Depth;

        CurrentStage = "Complete";
        Progress = 1.0f;

        return new AmbientOcclusionResult
        {
            AoField = aoField,
            SegmentationMask = mask,
            ProcessingTime = stopwatch.Elapsed.TotalSeconds,
            VoxelsPerSecond = (long)(totalVoxels / stopwatch.Elapsed.TotalSeconds),
            AccelerationType = samplingStep == 1 ? accelType : $"{accelType}, adaptive {samplingStep}x",
            ProcessingRegion = region,
            SamplingStep = samplingStep,
            DatasetWidth = dataset.Width,
            DatasetHeight = dataset.Height,
            DatasetDepth = dataset.Depth
        };
    }

    /// <summary>
    /// Apply segmentation result to dataset's label volume
    /// </summary>
    public void ApplySegmentation(
        CtImageStackDataset dataset,
        AmbientOcclusionResult result,
        byte targetMaterialId,
        CancellationToken cancellationToken = default,
        IProgress<float> progress = null)
    {
        if (dataset.LabelData == null)
        {
            dataset.LabelData = new Data.VolumeData.ChunkedLabelVolume(
                dataset.Width, dataset.Height, dataset.Depth, 256, false,
                dataset.FilePath + "/" + dataset.Name + ".Labels.bin");
        }

        var mask = result.SegmentationMask;
        var region = result.ProcessingRegion;
        var step = Math.Max(1, result.SamplingStep);
        var depth = region.Depth;

        var completed = 0;
        Parallel.For(0, depth, new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
            CancellationToken = cancellationToken
        }, z =>
        {
            var slice = ArrayPool<byte>.Shared.Rent(dataset.Width * dataset.Height);
            try
            {
            var globalZ = region.MinZ + z;
            dataset.LabelData.ReadSliceZ(globalZ, slice);
            var modified = false;
            var mz = Math.Min(mask.GetLength(2) - 1, z / step);
            for (var y = region.MinY; y < region.MaxY; y++)
            for (var x = region.MinX; x < region.MaxX; x++)
            {
                    var mx = Math.Min(mask.GetLength(0) - 1, (x - region.MinX) / step);
                    var my = Math.Min(mask.GetLength(1) - 1, (y - region.MinY) / step);
                    if (mask[mx, my, mz])
                    {
                        slice[y * dataset.Width + x] = targetMaterialId;
                        modified = true;
                    }
            }
            if (modified) dataset.LabelData.WriteSliceZ(globalZ, slice);
            var done = Interlocked.Increment(ref completed);
            if ((done & 7) == 0 || done == depth) progress?.Report(done / (float)depth);
            }
            finally { ArrayPool<byte>.Shared.Return(slice); }
        });
    }

    #region Ray Direction Generation

    /// <summary>
    /// Generate uniformly distributed ray directions on unit sphere using Fibonacci spiral
    /// </summary>
    private Vector3[] GenerateUniformRayDirections(int count)
    {
        var directions = new Vector3[count];
        const float phi = 1.618033988749895f; // Golden ratio

        for (int i = 0; i < count; i++)
        {
            float y = 1f - (i / (float)Math.Max(1, count - 1)) * 2f; // y goes from 1 to -1
            float radius = MathF.Sqrt(1f - y * y);

            float theta = 2f * MathF.PI * i / phi;
            float x = MathF.Cos(theta) * radius;
            float z = MathF.Sin(theta) * radius;

            directions[i] = Vector3.Normalize(new Vector3(x, y, z));
        }

        return directions;
    }

    #endregion

    #region GPU Implementation (OpenCL)

    private const string AoKernelSource = @"
// Compute ambient occlusion for each voxel
__kernel void compute_ao(
    __global const uchar* volume,      // Input volume data
    __global const uchar* labels,      // Label data (optional, can be null)
    __global float* ao_field,          // Output AO values
    __constant float* ray_dirs,        // Ray directions (3 * ray_count floats)
    const int width,
    const int height,
    const int depth,
    const int ray_count,
    const float ray_length,
    const uchar material_threshold,
    const int use_labels,              // 0 = grayscale, 1 = labels
    const uchar source_material_id)    // Material ID to analyze (if use_labels = 1)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int z = get_global_id(2);

    if (x >= width || y >= height || z >= depth)
        return;

    int voxel_idx = z * (width * height) + y * width + x;

    // Check if voxel is material (based on mode)
    int is_material;
    if (use_labels)
    {
        uchar label = labels[voxel_idx];
        is_material = (label == source_material_id) ? 1 : 0;
    }
    else
    {
        uchar voxel_value = volume[voxel_idx];
        is_material = (voxel_value >= material_threshold) ? 1 : 0;
    }

    // Material voxels are never pores: fix openness at 1 so thresholding skips them.
    if (is_material)
    {
        ao_field[voxel_idx] = 1.0f;
        return;
    }

    int escaped_count = 0;

    // Cast rays in all directions from this void voxel
    for (int r = 0; r < ray_count; r++)
    {
        float3 dir;
        dir.x = ray_dirs[r * 3 + 0];
        dir.y = ray_dirs[r * 3 + 1];
        dir.z = ray_dirs[r * 3 + 2];

        // March along ray
        float t = 0.0f;
        const float step_size = 1.0f;
        bool blocked = false;

        while (t < ray_length)
        {
            t += step_size;

            int rx = x + (int)(dir.x * t + 0.5f);
            int ry = y + (int)(dir.y * t + 0.5f);
            int rz = z + (int)(dir.z * t + 0.5f);

            // Leaving the volume counts as escaping
            if (rx < 0 || rx >= width || ry < 0 || ry >= height || rz < 0 || rz >= depth)
                break;

            int ray_idx = rz * (width * height) + ry * width + rx;

            // Check if ray hit material (based on mode)
            int ray_hit_material;
            if (use_labels)
            {
                ray_hit_material = (labels[ray_idx] == source_material_id) ? 1 : 0;
            }
            else
            {
                ray_hit_material = (volume[ray_idx] >= material_threshold) ? 1 : 0;
            }

            if (ray_hit_material)
            {
                blocked = true;
                break;
            }
        }

        if (!blocked)
            escaped_count++;
    }

    // Openness = fraction of rays that escaped. Enclosed cavities -> low openness.
    ao_field[voxel_idx] = (float)escaped_count / (float)ray_count;
}

// Apply threshold to AO field to generate segmentation mask
__kernel void threshold_ao(
    __global const float* ao_field,
    __global uchar* mask,
    const float threshold,
    const int total_voxels)
{
    int i = get_global_id(0);
    if (i >= total_voxels)
        return;

    // Voxels with AO below threshold are segmented as pores
    mask[i] = (ao_field[i] < threshold) ? 1 : 0;
}
";

    private unsafe void InitializeGpu()
    {
        _cl = CL.GetApi();
        int err;

        var selectedDevice = GAIA.OpenCL.OpenCLDeviceManager.GetComputeDevice();
        if (selectedDevice == 0)
            throw new InvalidOperationException("No OpenCL device available.");

        var deviceInfo = GAIA.OpenCL.OpenCLDeviceManager.GetDeviceInfo();
        Logger.Log($"[AmbientOcclusionSegmentation] Using device: {deviceInfo.Name} ({deviceInfo.Vendor})");

        _context = _cl.CreateContext(null, 1, &selectedDevice, null, null, &err);
        CheckErr(err, "CreateContext");

        _commandQueue = _cl.CreateCommandQueue(_context, selectedDevice,
            CommandQueueProperties.ProfilingEnable, &err);
        CheckErr(err, "CreateCommandQueue");

        var sourceLength = (nuint)AoKernelSource.Length;
        _program = _cl.CreateProgramWithSource(_context, 1, new[] { AoKernelSource }, &sourceLength, &err);
        CheckErr(err, "CreateProgramWithSource");

        err = _cl.BuildProgram(_program, 1, &selectedDevice, "", null, null);
        if (err != (int)CLEnum.Success)
        {
            nuint logSize;
            const int clProgramBuildLog = 0x1183;
            _cl.GetProgramBuildInfo(_program, selectedDevice, (ProgramBuildInfo)clProgramBuildLog, 0, null, &logSize);
            if (logSize > 1)
            {
                var log = new byte[logSize];
                fixed (byte* pLog = log)
                {
                    _cl.GetProgramBuildInfo(_program, selectedDevice, (ProgramBuildInfo)clProgramBuildLog,
                        logSize, pLog, null);
                }
                throw new Exception($"OpenCL build error:\n{System.Text.Encoding.UTF8.GetString(log)}");
            }
            throw new Exception($"OpenCL build error with code: {err}");
        }

        _computeAoKernel = _cl.CreateKernel(_program, "compute_ao", &err);
        CheckErr(err, "CreateKernel (compute_ao)");

        _thresholdKernel = _cl.CreateKernel(_program, "threshold_ao", &err);
        CheckErr(err, "CreateKernel (threshold_ao)");
    }

    private unsafe float[,,] ComputeAoFieldGpu(
        CtImageStackDataset dataset,
        AmbientOcclusionSettings settings,
        Region3D region,
        int samplingStep,
        CancellationToken cancellationToken)
    {
        int width = (region.Width + samplingStep - 1) / samplingStep;
        int height = (region.Height + samplingStep - 1) / samplingStep;
        int depth = (region.Depth + samplingStep - 1) / samplingStep;
        long totalVoxels = (long)width * height * depth;

        var (volumeData, labelData) = ExtractSampledVolumes(dataset, settings, region, samplingStep,
            cancellationToken);
        var idx = 0;

        // Prepare ray directions for GPU
        var rayDirData = new float[_rayDirections.Length * 3];
        for (int i = 0; i < _rayDirections.Length; i++)
        {
            rayDirData[i * 3 + 0] = _rayDirections[i].X;
            rayDirData[i * 3 + 1] = _rayDirections[i].Y;
            rayDirData[i * 3 + 2] = _rayDirections[i].Z;
        }

        nint volumeBuffer = 0;
        nint labelBuffer = 0;
        nint aoBuffer = 0;
        nint rayDirBuffer = 0;

        try
        {
            int err;

            // Create buffers
            fixed (byte* pVolume = volumeData)
            {
                volumeBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                    (nuint)totalVoxels, pVolume, &err);
                CheckErr(err, "CreateBuffer (volume)");
            }

            // Create label buffer if using existing material
            if (labelData != null)
            {
                fixed (byte* pLabels = labelData)
                {
                    labelBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                        (nuint)totalVoxels, pLabels, &err);
                    CheckErr(err, "CreateBuffer (labels)");
                }
            }
            else
            {
                // Create dummy buffer (won't be used)
                labelBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly, 1, null, &err);
                CheckErr(err, "CreateBuffer (labels dummy)");
            }

            aoBuffer = _cl.CreateBuffer(_context, MemFlags.WriteOnly,
                (nuint)(totalVoxels * sizeof(float)), null, &err);
            CheckErr(err, "CreateBuffer (ao)");

            fixed (float* pRayDirs = rayDirData)
            {
                rayDirBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                    (nuint)(rayDirData.Length * sizeof(float)), pRayDirs, &err);
                CheckErr(err, "CreateBuffer (rayDirs)");
            }

            // Set kernel arguments
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 0, (nuint)sizeof(nint), &volumeBuffer), "SetKernelArg 0");
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 1, (nuint)sizeof(nint), &labelBuffer), "SetKernelArg 1");
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 2, (nuint)sizeof(nint), &aoBuffer), "SetKernelArg 2");
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 3, (nuint)sizeof(nint), &rayDirBuffer), "SetKernelArg 3");
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 4, (nuint)sizeof(int), &width), "SetKernelArg 4");
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 5, (nuint)sizeof(int), &height), "SetKernelArg 5");
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 6, (nuint)sizeof(int), &depth), "SetKernelArg 6");
            int rayCount = settings.RayCount;
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 7, (nuint)sizeof(int), &rayCount), "SetKernelArg 7");
            float rayLength = settings.RayLength / samplingStep;
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 8, (nuint)sizeof(float), &rayLength), "SetKernelArg 8");
            byte threshold = settings.MaterialThreshold;
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 9, (nuint)sizeof(byte), &threshold), "SetKernelArg 9");
            int useLabels = settings.UseExistingMaterial ? 1 : 0;
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 10, (nuint)sizeof(int), &useLabels), "SetKernelArg 10");
            byte sourceMaterialId = settings.SourceMaterialId;
            CheckErr(_cl.SetKernelArg(_computeAoKernel, 11, (nuint)sizeof(byte), &sourceMaterialId), "SetKernelArg 11");

            // Execute kernel
            nuint* globalWorkSize = stackalloc nuint[3];
            globalWorkSize[0] = (nuint)width;
            globalWorkSize[1] = (nuint)height;
            globalWorkSize[2] = (nuint)depth;

            CheckErr(_cl.EnqueueNdrangeKernel(_commandQueue, _computeAoKernel, 3, null,
                globalWorkSize, null, 0, null, null), "EnqueueNDRangeKernel");

            // Read results
            var aoData = new float[totalVoxels];
            CheckErr(_cl.Finish(_commandQueue), "Finish");

            fixed (float* pAo = aoData)
            {
                CheckErr(_cl.EnqueueReadBuffer(_commandQueue, aoBuffer, true, 0,
                    (nuint)(totalVoxels * sizeof(float)), pAo, 0, null, null), "EnqueueReadBuffer");
            }

            // Convert to 3D array
            var aoField = new float[width, height, depth];
            idx = 0;
            for (int z = 0; z < depth; z++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        aoField[x, y, z] = aoData[idx++];
                    }
                }
            }

            Progress = 0.7f;
            return aoField;
        }
        finally
        {
            if (volumeBuffer != 0) _cl.ReleaseMemObject(volumeBuffer);
            if (labelBuffer != 0) _cl.ReleaseMemObject(labelBuffer);
            if (aoBuffer != 0) _cl.ReleaseMemObject(aoBuffer);
            if (rayDirBuffer != 0) _cl.ReleaseMemObject(rayDirBuffer);
        }
    }

    #endregion

    #region SIMD CPU Implementation

    private static (byte[] volume, byte[] labels) ExtractSampledVolumes(CtImageStackDataset dataset,
        AmbientOcclusionSettings settings, Region3D region, int samplingStep, CancellationToken token)
    {
        var width = (region.Width + samplingStep - 1) / samplingStep;
        var height = (region.Height + samplingStep - 1) / samplingStep;
        var depth = (region.Depth + samplingStep - 1) / samplingStep;
        var volume = new byte[checked(width * height * depth)];
        var labels = settings.UseExistingMaterial && dataset.LabelData != null ? new byte[volume.Length] : null;
        var source = ArrayPool<byte>.Shared.Rent(checked(dataset.Width * dataset.Height));
        var labelSource = labels != null ? ArrayPool<byte>.Shared.Rent(checked(dataset.Width * dataset.Height)) : null;
        try
        {
            var outputZ = 0;
            for (var z = region.MinZ; z < region.MaxZ; z += samplingStep, outputZ++)
            {
                token.ThrowIfCancellationRequested();
                dataset.VolumeData.ReadSliceZ(z, source);
                if (labels != null) dataset.LabelData.ReadSliceZ(z, labelSource);
                var outputY = 0;
                for (var y = region.MinY; y < region.MaxY; y += samplingStep, outputY++)
                {
                    var output = (outputZ * height + outputY) * width;
                    var outputX = 0;
                    for (var x = region.MinX; x < region.MaxX; x += samplingStep, outputX++)
                    {
                        volume[output + outputX] = source[y * dataset.Width + x];
                        if (labels != null) labels[output + outputX] = labelSource[y * dataset.Width + x];
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(source);
            if (labelSource != null) ArrayPool<byte>.Shared.Return(labelSource);
        }
        return (volume, labels);
    }

    private float[,,] ComputeAoFieldSimd(
        CtImageStackDataset dataset,
        AmbientOcclusionSettings settings,
        Region3D region,
        int samplingStep,
        CancellationToken cancellationToken)
    {
        int width = (region.Width + samplingStep - 1) / samplingStep;
        int height = (region.Height + samplingStep - 1) / samplingStep;
        int depth = (region.Depth + samplingStep - 1) / samplingStep;

        var aoField = new float[width, height, depth];
        var (volume, labels) = ExtractSampledVolumes(dataset, settings, region, samplingStep, cancellationToken);
        var sampledRayLength = Math.Max(1, settings.RayLength / samplingStep);

        Parallel.For(0, depth, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = settings.CpuThreads
        }, z =>
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var center = (z * height + y) * width + x;
                    var isMaterial = labels != null ? labels[center] == settings.SourceMaterialId :
                        volume[center] >= settings.MaterialThreshold;

                    // Material voxels are never pores: openness fixed at 1 so thresholding skips them.
                    if (isMaterial)
                    {
                        aoField[x, y, z] = 1f;
                        continue;
                    }

                    int escapedCount = 0;

                    // Cast rays from this void voxel
                    for (int r = 0; r < _rayDirections.Length; r++)
                    {
                        var dir = _rayDirections[r];
                        float t = 0f;
                        const float stepSize = 1f;
                        bool blocked = false;

                        while (t < sampledRayLength)
                        {
                            t += stepSize;

                            int rx = x + (int)(dir.X * t + 0.5f);
                            int ry = y + (int)(dir.Y * t + 0.5f);
                            int rz = z + (int)(dir.Z * t + 0.5f);

                            // Leaving the volume counts as escaping
                            if (rx < 0 || rx >= width || ry < 0 || ry >= height || rz < 0 || rz >= depth)
                                break;

                            // Check if ray hit material (based on mode)
                            bool rayHitMaterial;
                            var rayIndex = (rz * height + ry) * width + rx;
                            rayHitMaterial = labels != null ? labels[rayIndex] == settings.SourceMaterialId :
                                volume[rayIndex] >= settings.MaterialThreshold;

                            if (rayHitMaterial)
                            {
                                blocked = true;
                                break;
                            }
                        }

                        if (!blocked)
                            escapedCount++;
                    }

                    // Openness = fraction of rays that escaped. Enclosed cavities -> low openness.
                    aoField[x, y, z] = (float)escapedCount / _rayDirections.Length;
                }
            }

            Progress = 0.1f + ((float)z / depth) * 0.6f;
        });

        return aoField;
    }

    #endregion

    #region CPU Implementation

    private float[,,] ComputeAoFieldCpu(
        CtImageStackDataset dataset,
        AmbientOcclusionSettings settings,
        Region3D region,
        int samplingStep,
        CancellationToken cancellationToken)
    {
        // Same as SIMD implementation for now
        return ComputeAoFieldSimd(dataset, settings, region, samplingStep, cancellationToken);
    }

    #endregion

    #region Thresholding

    private bool[,,] ApplyThreshold(float[,,] aoField, float threshold)
    {
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
                    // Enclosed pores/cavities have LOW openness; material voxels are fixed
                    // at 1.0 and open exterior space scores high, so neither is selected.
                    mask[x, y, z] = aoField[x, y, z] < threshold;
                }
            }
        }

        return mask;
    }

    #endregion

    #region Utility

    private static void CheckErr(int err, string name)
    {
        if (err != (int)CLEnum.Success)
            throw new Exception($"OpenCL Error: {name} returned {err}");
    }

    public void Dispose()
    {
        if (_gpuAvailable)
        {
            if (_thresholdKernel != 0) _cl.ReleaseKernel(_thresholdKernel);
            if (_computeAoKernel != 0) _cl.ReleaseKernel(_computeAoKernel);
            if (_program != 0) _cl.ReleaseProgram(_program);
            if (_commandQueue != 0) _cl.ReleaseCommandQueue(_commandQueue);
            if (_context != 0) _cl.ReleaseContext(_context);
        }
    }

    #endregion
}
