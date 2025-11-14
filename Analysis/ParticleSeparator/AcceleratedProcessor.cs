// GeoscientistToolkit/Analysis/ParticleSeparator/AcceleratedProcessor.cs

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.ParticleSeparator;

public enum AccelerationType
{
    Auto,
    CPU,
    SIMD,
    GPU
}

public class AcceleratedProcessor : IDisposable
{
    private readonly bool _gpuAvailable;

    public AcceleratedProcessor()
    {
        try
        {
            InitializeGpu();
            _gpuAvailable = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[AcceleratedProcessor] GPU initialization failed: {ex.Message}. Falling back to CPU.");
            _gpuAvailable = false;
        }
    }

    public float Progress { get; set; }
    public string CurrentStage { get; set; } = "";
    public double LastProcessingTime { get; set; }
    public long VoxelsPerSecond { get; set; }
    public int ThreadCount { get; set; } = Environment.ProcessorCount;
    public AccelerationType SelectedAcceleration { get; set; } = AccelerationType.Auto;

    public (string Message, Vector4 Color) GetAccelerationStatus()
    {
        var simdAvailable = Vector.IsHardwareAccelerated;
        if (_gpuAvailable) return ("✓ GPU Available", new Vector4(0, 1, 0, 1));
        if (simdAvailable) return ("✓ SIMD Available", new Vector4(0.5f, 1, 0, 1));
        return ("✓ Multi-threaded CPU", new Vector4(1, 1, 0, 1));
    }

    public ParticleSeparationResult SeparateParticles(CtImageStackDataset dataset, Material material, bool use3D,
        bool conservative, float minSize, int zSlice, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Progress = 0.1f;
            CurrentStage = "Extracting mask";
            var mask = ExtractMaterialMaskOptimized(dataset, material, cancellationToken);
            Progress = 0.3f;
            CurrentStage = "Labeling components";

            int[,,] labels;
            if (use3D)
                labels = SelectedAcceleration switch
                {
                    AccelerationType.GPU when _gpuAvailable => LabelComponents3DGpu(mask, cancellationToken),
                    AccelerationType.SIMD when Vector.IsHardwareAccelerated => LabelComponents3DParallel(mask,
                        cancellationToken), // Placeholder
                    _ => LabelComponents3DParallel(mask, cancellationToken)
                };
            else
                labels = LabelComponents2DOptimized(mask, zSlice, cancellationToken);

            Progress = 0.7f;
            CurrentStage = "Analyzing particles";
            var particles = AnalyzeParticlesOptimized(labels, dataset.PixelSize, conservative ? (int)minSize : 1,
                cancellationToken);
            Progress = 1.0f;
            CurrentStage = "Complete";

            stopwatch.Stop();
            LastProcessingTime = stopwatch.Elapsed.TotalSeconds;
            var totalVoxels = use3D
                ? (long)dataset.Width * dataset.Height * dataset.Depth
                : (long)dataset.Width * dataset.Height;
            VoxelsPerSecond = (long)(totalVoxels / LastProcessingTime);

            return new ParticleSeparationResult { LabelVolume = labels, Particles = particles, Is3D = use3D };
        }
        catch (Exception ex)
        {
            Logger.LogError($"[AcceleratedProcessor] Error: {ex.Message}");
            throw;
        }
    }

    private byte[,,] ExtractMaterialMaskOptimized(CtImageStackDataset dataset, Material material,
        CancellationToken cancellationToken)
    {
        var width = dataset.Width;
        var height = dataset.Height;
        var depth = dataset.Depth;
        var mask = new byte[width, height, depth];
        var partitioner = Partitioner.Create(0, depth);
        Parallel.ForEach(partitioner,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = ThreadCount },
            range =>
            {
                for (var z = range.Item1; z < range.Item2; z++)
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (dataset.LabelData != null && dataset.LabelData[x, y, z] == material.ID)
                        mask[x, y, z] = 1;

                Progress = 0.1f + (float)range.Item2 / depth * 0.2f;
            });
        return mask;
    }

    private unsafe int[,,] LabelComponents3DGpu(byte[,,] mask, CancellationToken cancellationToken)
    {
        int width = mask.GetLength(0), height = mask.GetLength(1), depth = mask.GetLength(2);
        var totalVoxels = (long)width * height * depth;
        int err;

        var flatMask = new byte[totalVoxels];
        Buffer.BlockCopy(mask, 0, flatMask, 0, (int)totalVoxels);

        nint maskBuffer = 0, labelsBuffer = 0, changedBuffer = 0;
        try
        {
            fixed (void* pMask = flatMask)
            {
                maskBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (nuint)totalVoxels,
                    pMask, &err);
                CheckErr(err, "CreateBuffer (mask)");
            }

            labelsBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite, (nuint)(totalVoxels * sizeof(int)), null,
                &err);
            CheckErr(err, "CreateBuffer (labels)");
            changedBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite, sizeof(int), null, &err);
            CheckErr(err, "CreateBuffer (changed)");

            // 1. Initialize labels kernel
            CheckErr(_cl.SetKernelArg(_initKernel, 0, (nuint)sizeof(nint), &maskBuffer), "SetKernelArg (init 0)");
            CheckErr(_cl.SetKernelArg(_initKernel, 1, (nuint)sizeof(nint), &labelsBuffer), "SetKernelArg (init 1)");
            var globalWorkSize1D = (nuint)totalVoxels;
            CheckErr(
                _cl.EnqueueNdrangeKernel(_commandQueue, _initKernel, 1, null, &globalWorkSize1D, null, 0, null, null),
                "EnqueueNDRangeKernel (init)");

            // 2. Iteratively propagate labels
            var globalWorkSize3D = new[] { (nuint)width, (nuint)height, (nuint)depth };
            CheckErr(_cl.SetKernelArg(_propagateKernel, 0, (nuint)sizeof(nint), &labelsBuffer),
                "SetKernelArg (propagate 0)");
            CheckErr(_cl.SetKernelArg(_propagateKernel, 1, (nuint)sizeof(nint), &changedBuffer),
                "SetKernelArg (propagate 1)");
            CheckErr(_cl.SetKernelArg(_propagateKernel, 2, sizeof(int), &width), "SetKernelArg (propagate 2)");
            CheckErr(_cl.SetKernelArg(_propagateKernel, 3, sizeof(int), &height), "SetKernelArg (propagate 3)");
            CheckErr(_cl.SetKernelArg(_propagateKernel, 4, sizeof(int), &depth), "SetKernelArg (propagate 4)");

            var changed = 1;
            var maxIterations = width + height + depth;
            for (var i = 0; i < maxIterations && changed != 0; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                changed = 0;
                CheckErr(
                    _cl.EnqueueWriteBuffer(_commandQueue, changedBuffer, true, 0, sizeof(int), &changed, 0, null, null),
                    "EnqueueWriteBuffer");
                CheckErr(
                    _cl.EnqueueNdrangeKernel(_commandQueue, _propagateKernel, 3, (nuint*)null, globalWorkSize3D,
                        (nuint*)null, 0, (nint*)null, (nint*)null), "EnqueueNDRangeKernel (propagate)");
                CheckErr(
                    _cl.EnqueueReadBuffer(_commandQueue, changedBuffer, true, 0, sizeof(int), &changed, 0, null, null),
                    "EnqueueReadBuffer");
                Progress = 0.3f + (float)(i + 1) / maxIterations * 0.3f;
            }

            // 3. Resolve label chains to root
            CheckErr(_cl.SetKernelArg(_resolveKernel, 0, (nuint)sizeof(nint), &labelsBuffer),
                "SetKernelArg (resolve 0)");
            CheckErr(
                _cl.EnqueueNdrangeKernel(_commandQueue, _resolveKernel, 1, null, &globalWorkSize1D, null, 0, null,
                    null), "EnqueueNDRangeKernel (resolve)");

            var flatLabels = new int[totalVoxels];
            CheckErr(_cl.Finish(_commandQueue), "Finish");
            fixed (void* pLabels = flatLabels)
            {
                CheckErr(
                    _cl.EnqueueReadBuffer(_commandQueue, labelsBuffer, true, 0, (nuint)(totalVoxels * sizeof(int)),
                        pLabels, 0, null, null), "EnqueueReadBuffer (final)");
            }

            var resultLabels = new int[width, height, depth];
            Buffer.BlockCopy(flatLabels, 0, resultLabels, 0, (int)totalVoxels * sizeof(int));
            Progress = 0.6f;
            return resultLabels;
        }
        finally
        {
            if (maskBuffer != 0) _cl.ReleaseMemObject(maskBuffer);
            if (labelsBuffer != 0) _cl.ReleaseMemObject(labelsBuffer);
            if (changedBuffer != 0) _cl.ReleaseMemObject(changedBuffer);
        }
    }

    private int[,,] LabelComponents3DParallel(byte[,,] mask, CancellationToken cancellationToken)
    {
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        var depth = mask.GetLength(2);
        var labels = new int[width, height, depth];
        var unionFind = new ConcurrentUnionFind();
        var nextLabel = 1;

        Parallel.For(0, depth,
            new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = ThreadCount }, z =>
            {
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (mask[x, y, z] == 0) continue;
                    var neighbors = new List<int>(3);
                    if (x > 0 && labels[x - 1, y, z] > 0) neighbors.Add(labels[x - 1, y, z]);
                    if (y > 0 && labels[x, y - 1, z] > 0) neighbors.Add(labels[x, y - 1, z]);
                    if (z > 0 && labels[x, y, z - 1] > 0) neighbors.Add(labels[x, y, z - 1]);

                    if (neighbors.Count == 0)
                    {
                        var label = Interlocked.Increment(ref nextLabel) - 1;
                        labels[x, y, z] = label;
                        unionFind.MakeSet(label);
                    }
                    else
                    {
                        var minLabel = neighbors.Min();
                        labels[x, y, z] = minLabel;
                        foreach (var label in neighbors) unionFind.Union(minLabel, label);
                    }
                }

                Progress = 0.3f + (float)(z + 1) / depth * 0.2f;
            });

        ResolveEquivalencesParallel(labels, unionFind, cancellationToken);
        return labels;
    }

    private void ResolveEquivalencesParallel(int[,,] labels, ConcurrentUnionFind unionFind, CancellationToken token)
    {
        int width = labels.GetLength(0), height = labels.GetLength(1), depth = labels.GetLength(2);
        var finalLabels = new ConcurrentDictionary<int, int>();
        var finalLabelCounter = 0;
        Parallel.For(0, depth, new ParallelOptions { CancellationToken = token }, z =>
        {
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (labels[x, y, z] > 0)
                {
                    var root = unionFind.Find(labels[x, y, z]);
                    if (!finalLabels.ContainsKey(root))
                        finalLabels.TryAdd(root, Interlocked.Increment(ref finalLabelCounter));
                    labels[x, y, z] = finalLabels[root];
                }

            Progress = 0.5f + (float)(z + 1) / depth * 0.2f;
        });
    }

    private int[,,] LabelComponents2DOptimized(byte[,,] mask, int slice, CancellationToken cancellationToken)
    {
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        var depth = mask.GetLength(2);
        var labels = new int[width, height, depth];
        var nextLabel = 1;
        var equivalences = new Dictionary<int, int>();

        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                if (mask[x, y, slice] == 0) continue;
                var left = x > 0 ? labels[x - 1, y, slice] : 0;
                var top = y > 0 ? labels[x, y - 1, slice] : 0;
                if (left == 0 && top == 0)
                {
                    labels[x, y, slice] = nextLabel++;
                }
                else if (left > 0 && top == 0)
                {
                    labels[x, y, slice] = left;
                }
                else if (left == 0 && top > 0)
                {
                    labels[x, y, slice] = top;
                }
                else
                {
                    labels[x, y, slice] = Math.Min(left, top);
                    if (left != top) UnionLabels(equivalences, left, top);
                }
            }
        }

        for (var y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
                if (labels[x, y, slice] > 0)
                    labels[x, y, slice] = FindRoot(equivalences, labels[x, y, slice]);
        }

        return labels;
    }

    private void UnionLabels(Dictionary<int, int> equivalences, int a, int b)
    {
        var rootA = FindRoot(equivalences, a);
        var rootB = FindRoot(equivalences, b);
        if (rootA != rootB) equivalences[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
    }

    private int FindRoot(Dictionary<int, int> equivalences, int label)
    {
        while (equivalences.ContainsKey(label)) label = equivalences[label];
        return label;
    }

    private List<Particle> AnalyzeParticlesOptimized(int[,,] labels, double pixelSize, int minSize,
        CancellationToken cancellationToken)
    {
        var particles = new ConcurrentDictionary<int, ParticleAccumulator>();
        var width = labels.GetLength(0);
        var height = labels.GetLength(1);
        var depth = labels.GetLength(2);
        Parallel.For(0, depth, new ParallelOptions { CancellationToken = cancellationToken }, z =>
        {
            var localParticles = new Dictionary<int, ParticleAccumulator>();
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var label = labels[x, y, z];
                if (label == 0) continue;
                if (!localParticles.TryGetValue(label, out var acc))
                {
                    acc = new ParticleAccumulator { Id = label };
                    localParticles[label] = acc;
                }

                acc.VoxelCount++;
                acc.CenterSum += new Vector3(x, y, z);
                acc.UpdateBounds(x, y, z);
            }

            foreach (var kvp in localParticles)
                particles.AddOrUpdate(kvp.Key, kvp.Value, (key, existing) =>
                {
                    existing.Merge(kvp.Value);
                    return existing;
                });
            Progress = 0.7f + (float)(z + 1) / depth * 0.3f;
        });

        var voxelVolume = pixelSize * pixelSize * pixelSize;
        var nextParticleId = 1;

        return particles.Values.Where(acc => acc.VoxelCount >= minSize)
            .OrderBy(acc => acc.Id)
            .Select(acc => new Particle
            {
                Id = nextParticleId++,
                VoxelCount = acc.VoxelCount,
                Center = new Point3D
                {
                    X = (int)(acc.CenterSum.X / acc.VoxelCount), Y = (int)(acc.CenterSum.Y / acc.VoxelCount),
                    Z = (int)(acc.CenterSum.Z / acc.VoxelCount)
                },
                Bounds = new BoundingBox
                {
                    MinX = acc.MinX, MinY = acc.MinY, MinZ = acc.MinZ, MaxX = acc.MaxX, MaxY = acc.MaxY, MaxZ = acc.MaxZ
                },
                VolumeMicrometers = acc.VoxelCount * voxelVolume * 1e18,
                VolumeMillimeters = acc.VoxelCount * voxelVolume * 1e9
            }).ToList();
    }

    private static void CheckErr(int err, string name)
    {
        if (err != (int)CLEnum.Success) throw new Exception($"OpenCL Error: {name} returned {err}");
    }

    private class ParticleAccumulator
    {
        public Vector3 CenterSum;
        public int Id;
        public int MaxX = int.MinValue, MaxY = int.MinValue, MaxZ = int.MinValue;
        public int MinX = int.MaxValue, MinY = int.MaxValue, MinZ = int.MaxValue;
        public int VoxelCount;

        public void UpdateBounds(int x, int y, int z)
        {
            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MinZ = Math.Min(MinZ, z);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
            MaxZ = Math.Max(MaxZ, z);
        }

        public void Merge(ParticleAccumulator other)
        {
            VoxelCount += other.VoxelCount;
            CenterSum += other.CenterSum;
            MinX = Math.Min(MinX, other.MinX);
            MinY = Math.Min(MinY, other.MinY);
            MinZ = Math.Min(MinZ, other.MinZ);
            MaxX = Math.Max(MaxX, other.MaxX);
            MaxY = Math.Max(MaxY, other.MaxY);
            MaxZ = Math.Max(MaxZ, other.MaxZ);
        }
    }

    #region OpenCL Fields

    private const string GpuLabelingKernelSource = @"
            __kernel void initialize_labels(__global const uchar* mask, __global int* labels) {
                int i = get_global_id(0);
                if (mask[i] > 0) {
                    labels[i] = i + 1;
                } else {
                    labels[i] = 0;
                }
            }

            __kernel void propagate_labels(
                __global int* labels,
                __global volatile int* changed,
                const int width,
                const int height,
                const int depth)
            {
                int x = get_global_id(0);
                int y = get_global_id(1);
                int z = get_global_id(2);

                if (x >= width || y >= height || z >= depth) return;

                int index = z * (width * height) + y * width + x;
                int label = labels[index];
                if (label == 0) return;

                int min_label = label;

                // 6-connectivity neighborhood check
                if (x > 0 && labels[index - 1] > 0) min_label = min(min_label, labels[index - 1]);
                if (x < width - 1 && labels[index + 1] > 0) min_label = min(min_label, labels[index + 1]);
                if (y > 0 && labels[index - width] > 0) min_label = min(min_label, labels[index - width]);
                if (y < height - 1 && labels[index + width] > 0) min_label = min(min_label, labels[index + width]);
                if (z > 0 && labels[index - (width * height)] > 0) min_label = min(min_label, labels[index - (width * height)]);
                if (z < depth - 1 && labels[index + (width * height)] > 0) min_label = min(min_label, labels[index + (width * height)]);

                if (label > min_label) {
                    atomic_min(&labels[index], min_label);
                    *changed = 1;
                }
            }

            __kernel void resolve_labels(__global int* labels) {
                 int i = get_global_id(0);
                 int label = labels[i];
                 if (label == 0) return;

                 while (labels[label - 1] != label) {
                     label = labels[label - 1];
                 }
                 labels[i] = label;
            }";

    private CL _cl;
    private nint _context;
    private nint _commandQueue;
    private nint _program;
    private nint _initKernel;
    private nint _propagateKernel;
    private nint _resolveKernel;

    #endregion

    #region OpenCL Initialization and Cleanup

    private unsafe void InitializeGpu()
    {
        _cl = CL.GetApi();
        int err;

        // Use centralized device manager to get the device from settings
        var selectedDevice = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetComputeDevice();

        if (selectedDevice == 0)
            throw new InvalidOperationException("No OpenCL device available from OpenCLDeviceManager.");

        // Get device info from the centralized manager
        var deviceInfo = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetDeviceInfo();
        Logger.Log($"[AcceleratedProcessor] Using device: {deviceInfo.Name} ({deviceInfo.Vendor})");

        // Create Context and Command Queue
        _context = _cl.CreateContext(null, 1, &selectedDevice, null, null, &err);
        CheckErr(err, "CreateContext");

        _commandQueue = _cl.CreateCommandQueue(
            _context,
            selectedDevice,
            CommandQueueProperties.ProfilingEnable, // ← correct name
            &err);

        // 4. Create and Build Program
        var sourceLength = (nuint)GpuLabelingKernelSource.Length;
        _program = _cl.CreateProgramWithSource(_context, 1, new[] { GpuLabelingKernelSource }, &sourceLength, &err);
        CheckErr(err, "CreateProgramWithSource");

        err = _cl.BuildProgram(_program, 1, &selectedDevice, "", null, null);
        if (err != (int)CLEnum.Success)
        {
            nuint logSize;
            // FIX: Use the raw integer value for CL_PROGRAM_BUILD_LOG (0x1183) to bypass symbol resolution issues.
            const int clProgramBuildLog = 0x1183;
            _cl.GetProgramBuildInfo(_program, selectedDevice, (ProgramBuildInfo)clProgramBuildLog, 0, null, &logSize);
            if (logSize > 1)
            {
                var log = new byte[logSize];
                fixed (byte* pLog = log)
                {
                    _cl.GetProgramBuildInfo(_program, selectedDevice, (ProgramBuildInfo)clProgramBuildLog, logSize,
                        pLog, null);
                }

                throw new Exception($"OpenCL build error:\n {Encoding.UTF8.GetString(log)}");
            }

            throw new Exception($"OpenCL build error with code: {err}. No build log available.");
        }

        // 5. Create Kernels
        _initKernel = _cl.CreateKernel(_program, "initialize_labels", &err);
        CheckErr(err, "CreateKernel (initialize_labels)");
        _propagateKernel = _cl.CreateKernel(_program, "propagate_labels", &err);
        CheckErr(err, "CreateKernel (propagate_labels)");
        _resolveKernel = _cl.CreateKernel(_program, "resolve_labels", &err);
        CheckErr(err, "CreateKernel (resolve_labels)");
    }

    public void Dispose()
    {
        if (_gpuAvailable)
        {
            if (_resolveKernel != 0) _cl.ReleaseKernel(_resolveKernel);
            if (_propagateKernel != 0) _cl.ReleaseKernel(_propagateKernel);
            if (_initKernel != 0) _cl.ReleaseKernel(_initKernel);
            if (_program != 0) _cl.ReleaseProgram(_program);
            if (_commandQueue != 0) _cl.ReleaseCommandQueue(_commandQueue);
            if (_context != 0) _cl.ReleaseContext(_context);
        }
    }

    #endregion
}

public class ConcurrentUnionFind
{
    private readonly ConcurrentDictionary<int, int> _p = new();

    public void MakeSet(int x)
    {
        _p.TryAdd(x, x);
    }

    public int Find(int x)
    {
        if (!_p.TryGetValue(x, out var parent))
        {
            MakeSet(x);
            return x;
        }

        if (parent == x) return x;
        return _p[x] = Find(parent);
    }

    public void Union(int x, int y)
    {
        int rX = Find(x), rY = Find(y);
        if (rX != rY) _p[Math.Max(rX, rY)] = Math.Min(rX, rY);
    }
}

public class Particle
{
    public BoundingBox Bounds;
    public Point3D Center;
    public int Id;
    public double VolumeMicrometers, VolumeMillimeters;
    public int VoxelCount;
}

public class Point3D
{
    public int X, Y, Z;
}

public class BoundingBox
{
    public int MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
}

public class ParticleSeparationResult
{
    public bool Is3D;
    public int[,,] LabelVolume;
    public List<Particle> Particles;
}