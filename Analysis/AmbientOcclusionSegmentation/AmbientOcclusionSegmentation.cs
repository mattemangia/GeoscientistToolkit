// GeoscientistToolkit/Analysis/AmbientOcclusionSegmentation/AmbientOcclusionSegmentation.cs
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
// - Ambient occlusion measures how exposed each voxel is to ambient lighting
// - Pores/cavities have LOW AO values (rays escape)
// - Solid material has HIGH AO values (rays are blocked)
// - Generates smooth scalar fields suitable for segmentation
// ================================================================================================

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.AmbientOcclusionSegmentation;

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
    /// AO threshold for segmentation (0.0-1.0)
    /// Voxels with AO below this are segmented as pores
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
    /// Ambient occlusion values (0.0-1.0) for each voxel
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
            aoField = ComputeAoFieldGpu(dataset, settings, region, cancellationToken);
            accelType = "GPU (OpenCL)";
        }
        else if (Vector.IsHardwareAccelerated)
        {
            CurrentStage = "Computing AO (SIMD)";
            aoField = ComputeAoFieldSimd(dataset, settings, region, cancellationToken);
            accelType = "SIMD CPU";
        }
        else
        {
            CurrentStage = "Computing AO (CPU)";
            aoField = ComputeAoFieldCpu(dataset, settings, region, cancellationToken);
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
            AccelerationType = accelType
        };
    }

    /// <summary>
    /// Apply segmentation result to dataset's label volume
    /// </summary>
    public void ApplySegmentation(
        CtImageStackDataset dataset,
        AmbientOcclusionResult result,
        byte targetMaterialId)
    {
        if (dataset.LabelData == null)
        {
            dataset.LabelData = new Data.VolumeData.ChunkedLabelVolume(
                dataset.Width, dataset.Height, dataset.Depth, 256, false,
                dataset.FilePath + "/" + dataset.Name + ".Labels.bin");
        }

        var mask = result.SegmentationMask;
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        var depth = mask.GetLength(2);

        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (mask[x, y, z])
                    {
                        dataset.LabelData[x, y, z] = targetMaterialId;
                    }
                }
            }
        }
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
            float y = 1f - (i / (float)(count - 1)) * 2f; // y goes from 1 to -1
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

    // If voxel is not material, AO = 0
    if (!is_material)
    {
        ao_field[voxel_idx] = 0.0f;
        return;
    }

    int occluded_count = 0;

    // Cast rays in all directions
    for (int r = 0; r < ray_count; r++)
    {
        float3 dir;
        dir.x = ray_dirs[r * 3 + 0];
        dir.y = ray_dirs[r * 3 + 1];
        dir.z = ray_dirs[r * 3 + 2];

        // March along ray
        float t = 0.0f;
        const float step_size = 1.0f;
        bool hit = false;

        while (t < ray_length)
        {
            t += step_size;

            int rx = x + (int)(dir.x * t + 0.5f);
            int ry = y + (int)(dir.y * t + 0.5f);
            int rz = z + (int)(dir.z * t + 0.5f);

            // Check bounds
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
                hit = true;
                occluded_count++;
                break;
            }
        }
    }

    // AO = fraction of rays that were occluded
    ao_field[voxel_idx] = (float)occluded_count / (float)ray_count;
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

        var selectedDevice = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetComputeDevice();
        if (selectedDevice == 0)
            throw new InvalidOperationException("No OpenCL device available.");

        var deviceInfo = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetDeviceInfo();
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
        CancellationToken cancellationToken)
    {
        int width = region.Width;
        int height = region.Height;
        int depth = region.Depth;
        long totalVoxels = (long)width * height * depth;

        // Extract volume data
        var volumeData = new byte[totalVoxels];
        int idx = 0;
        for (int z = region.MinZ; z < region.MaxZ; z++)
        {
            for (int y = region.MinY; y < region.MaxY; y++)
            {
                for (int x = region.MinX; x < region.MaxX; x++)
                {
                    volumeData[idx++] = dataset.VolumeData[x, y, z];
                }
            }
        }

        // Extract label data if using existing material
        byte[] labelData = null;
        if (settings.UseExistingMaterial && dataset.LabelData != null)
        {
            labelData = new byte[totalVoxels];
            idx = 0;
            for (int z = region.MinZ; z < region.MaxZ; z++)
            {
                for (int y = region.MinY; y < region.MaxY; y++)
                {
                    for (int x = region.MinX; x < region.MaxX; x++)
                    {
                        labelData[idx++] = dataset.LabelData[x, y, z];
                    }
                }
            }
        }

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
            float rayLength = settings.RayLength;
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

    private float[,,] ComputeAoFieldSimd(
        CtImageStackDataset dataset,
        AmbientOcclusionSettings settings,
        Region3D region,
        CancellationToken cancellationToken)
    {
        int width = region.Width;
        int height = region.Height;
        int depth = region.Depth;

        var aoField = new float[width, height, depth];

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
                    int gx = region.MinX + x;
                    int gy = region.MinY + y;
                    int gz = region.MinZ + z;

                    // Check if voxel is material (based on mode)
                    bool isMaterial;
                    if (settings.UseExistingMaterial && dataset.LabelData != null)
                    {
                        isMaterial = dataset.LabelData[gx, gy, gz] == settings.SourceMaterialId;
                    }
                    else
                    {
                        byte voxelValue = dataset.VolumeData[gx, gy, gz];
                        isMaterial = voxelValue >= settings.MaterialThreshold;
                    }

                    if (!isMaterial)
                    {
                        aoField[x, y, z] = 0f;
                        continue;
                    }

                    int occludedCount = 0;

                    // Cast rays using SIMD where possible
                    for (int r = 0; r < _rayDirections.Length; r++)
                    {
                        var dir = _rayDirections[r];
                        float t = 0f;
                        const float stepSize = 1f;
                        bool hit = false;

                        while (t < settings.RayLength)
                        {
                            t += stepSize;

                            int rx = gx + (int)(dir.X * t + 0.5f);
                            int ry = gy + (int)(dir.Y * t + 0.5f);
                            int rz = gz + (int)(dir.Z * t + 0.5f);

                            if (rx < 0 || rx >= dataset.Width ||
                                ry < 0 || ry >= dataset.Height ||
                                rz < 0 || rz >= dataset.Depth)
                                break;

                            // Check if ray hit material (based on mode)
                            bool rayHitMaterial;
                            if (settings.UseExistingMaterial && dataset.LabelData != null)
                            {
                                rayHitMaterial = dataset.LabelData[rx, ry, rz] == settings.SourceMaterialId;
                            }
                            else
                            {
                                rayHitMaterial = dataset.VolumeData[rx, ry, rz] >= settings.MaterialThreshold;
                            }

                            if (rayHitMaterial)
                            {
                                hit = true;
                                occludedCount++;
                                break;
                            }
                        }
                    }

                    aoField[x, y, z] = (float)occludedCount / _rayDirections.Length;
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
        CancellationToken cancellationToken)
    {
        // Same as SIMD implementation for now
        return ComputeAoFieldSimd(dataset, settings, region, cancellationToken);
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
                    // Pores have LOW AO values
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
