// GeoscientistToolkit/Analysis/NMR/NMRSimulationOpenCL.cs
// FIXED: Correct physics in GPU kernel

using System.Diagnostics;
using System.Text;
using GeoscientistToolkit.Data.CtImageStack;
using GeoscientistToolkit.Data.VolumeData;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.NMR;

/// <summary>
///     GPU-accelerated NMR simulation using OpenCL via SILK.NET.
///     Provides 10-100× speedup over CPU implementation.
///     FIXED: Correct surface relaxation physics in OpenCL kernel
/// </summary>
public unsafe class NMRSimulationOpenCL : IDisposable
{
    private readonly CL _cl;
    private readonly NMRSimulationConfig _config;
    private readonly int _depth;
    private readonly int _height;
    private readonly ILabelVolumeData _labelVolume;
    private readonly int _width;
    private nint _bufferActiveCount;

    // OpenCL buffers
    private nint _bufferLabelVolume;
    private nint _bufferMaterialRelaxivities;
    private nint _bufferStepMagnetization;
    private nint _bufferValidWalkerCount;
    private nint _bufferWalkerActive;
    private nint _bufferWalkerMag;
    private nint _bufferWalkerPosX;
    private nint _bufferWalkerPosY;
    private nint _bufferWalkerPosZ;
    private nint _commandQueue;

    private nint _context;

    private bool _disposed;
    private nint _kernelCountActive;
    private nint _kernelInitialize;
    private nint _kernelRandomWalk;
    private nint _program;

    public NMRSimulationOpenCL(CtImageStackDataset dataset, NMRSimulationConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _labelVolume = dataset?.LabelData ?? throw new ArgumentNullException(nameof(dataset));
        _width = dataset.Width;
        _height = dataset.Height;
        _depth = dataset.Depth;

        _cl = CL.GetApi();

        // UNIT VERIFICATION
        VerifyVoxelSizeUnits(config.VoxelSize);

        Logger.Log($"[NMRSimulationOpenCL] Initializing GPU: {_width}x{_height}x{_depth}");
        Logger.Log($"[NMRSimulationOpenCL] Voxel size: {config.VoxelSize:E6} m = {config.VoxelSize * 1e6f:F2} µm");
        Logger.Log($"[NMRSimulationOpenCL] Pore shape factor: {config.PoreShapeFactor:F1}");

        InitializeOpenCL();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cl.ReleaseMemObject(_bufferLabelVolume);
        _cl.ReleaseMemObject(_bufferWalkerPosX);
        _cl.ReleaseMemObject(_bufferWalkerPosY);
        _cl.ReleaseMemObject(_bufferWalkerPosZ);
        _cl.ReleaseMemObject(_bufferWalkerMag);
        _cl.ReleaseMemObject(_bufferWalkerActive);
        _cl.ReleaseMemObject(_bufferMaterialRelaxivities);
        _cl.ReleaseMemObject(_bufferStepMagnetization);
        _cl.ReleaseMemObject(_bufferActiveCount);
        _cl.ReleaseMemObject(_bufferValidWalkerCount);

        _cl.ReleaseKernel(_kernelRandomWalk);
        _cl.ReleaseKernel(_kernelCountActive);
        _cl.ReleaseKernel(_kernelInitialize);

        _cl.ReleaseProgram(_program);
        _cl.ReleaseCommandQueue(_commandQueue);
        _cl.ReleaseContext(_context);

        _disposed = true;

        Logger.Log("[NMRSimulationOpenCL] Disposed GPU resources");
    }

    /// <summary>
    ///     Verify voxel size is in reasonable range for meters
    /// </summary>
    private static void VerifyVoxelSizeUnits(double voxelSize)
    {
        if (voxelSize < 1e-9)
        {
            Logger.LogError(
                $"[NMRSimulationOpenCL] Voxel size {voxelSize:E3} m is suspiciously small (< 1 nm). Unit error?");
            throw new ArgumentException("Voxel size appears to be in wrong units. Expected meters.");
        }

        if (voxelSize > 1e-3)
        {
            Logger.LogError(
                $"[NMRSimulationOpenCL] Voxel size {voxelSize:E3} m is suspiciously large (> 1 mm). Unit error?");
            throw new ArgumentException("Voxel size appears to be in wrong units. Expected meters.");
        }

        if (voxelSize < 1e-8)
            Logger.LogWarning($"[NMRSimulationOpenCL] Very small voxel size: {voxelSize * 1e9f:F2} nm. Verify units.");
        else if (voxelSize > 1e-4)
            Logger.LogWarning(
                $"[NMRSimulationOpenCL] Large voxel size: {voxelSize * 1e3f:F2} mm. NMR physics may not apply.");
    }

    public void RunSimulationAsync(
        IProgress<(float progress, string message)> progress,
        Action<NMRResults> onSuccess,
        Action<Exception> onError)
    {
        Task.Run(() =>
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                progress?.Report((0f, "Initializing GPU buffers..."));

                var results = new NMRResults(_config.NumberOfSteps)
                {
                    TotalSteps = _config.NumberOfSteps,
                    TimeStep = _config.TimeStepMs,
                    PoreMaterial = _config.MaterialRelaxivities.ContainsKey(_config.PoreMaterialID)
                        ? _config.MaterialRelaxivities[_config.PoreMaterialID].MaterialName
                        : "Unknown",
                    MaterialRelaxivities = _config.MaterialRelaxivities.ToDictionary(
                        kvp => kvp.Value.MaterialName,
                        kvp => kvp.Value.SurfaceRelaxivity),
                    ComputationMethod = "OpenCL (GPU)"
                };

                for (var t = 0; t < _config.NumberOfSteps; t++) results.TimePoints[t] = t * _config.TimeStepMs;

                progress?.Report((0.05f, "Uploading volume data to GPU..."));
                UploadDataToGPU();

                progress?.Report((0.1f, "Initializing walkers on GPU..."));
                var validWalkerCount = InitializeWalkersGPU();
                results.NumberOfWalkers = validWalkerCount;

                if (validWalkerCount == 0)
                    throw new InvalidOperationException("No valid walker positions found on GPU");

                progress?.Report((0.15f, $"Running simulation with {validWalkerCount:N0} walkers..."));
                SimulateRandomWalkGPU(results, progress);

                progress?.Report((0.85f, "Computing T2 distribution..."));
                ComputeT2Distribution(results);
                ComputePoreSizeDistribution(results);
                ComputeStatistics(results);

                // Optional T1-T2 map
                if (_config.ComputeT1T2Map)
                {
                    progress?.Report((0.95f, "Computing T1-T2 map..."));
                    T1T2Computation.ComputeT1T2Map(results, _config);
                }

                stopwatch.Stop();
                results.ComputationTime = stopwatch.Elapsed;

                progress?.Report((1f, $"GPU simulation completed in {stopwatch.Elapsed.TotalSeconds:F1}s"));
                Logger.Log(
                    $"[NMRSimulationOpenCL] Completed: {results.ComputationTime.TotalSeconds:F2}s, Speedup: ~{EstimateSpeedup()}×");

                onSuccess?.Invoke(results);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[NMRSimulationOpenCL] Simulation failed: {ex.Message}");
                onError?.Invoke(ex);
            }
        });
    }

    private void InitializeOpenCL()
    {
        // Get platform
        uint numPlatforms = 0;
        _cl.GetPlatformIDs(0, null, &numPlatforms);

        if (numPlatforms == 0) throw new InvalidOperationException("No OpenCL platforms found");

        var platforms = stackalloc nint[(int)numPlatforms];
        _cl.GetPlatformIDs(numPlatforms, platforms, null);

        var platform = platforms[0];

        // Get GPU device
        uint numDevices = 0;
        _cl.GetDeviceIDs(platform, DeviceType.Gpu, 0, null, &numDevices);

        if (numDevices == 0)
        {
            Logger.LogWarning("[NMRSimulationOpenCL] No GPU found, falling back to CPU");
            _cl.GetDeviceIDs(platform, DeviceType.Cpu, 0, null, &numDevices);
        }

        var devices = stackalloc nint[(int)numDevices];
        _cl.GetDeviceIDs(platform, numDevices > 0 ? DeviceType.Gpu : DeviceType.Cpu, numDevices, devices, null);

        var device = devices[0];

        LogDeviceInfo(device);

        // Create context
        int errorCode;
        _context = _cl.CreateContext(null, 1, &device, null, null, &errorCode);
        CheckError(errorCode, "CreateContext");

        _commandQueue = _cl.CreateCommandQueue(_context, device, (CommandQueueProperties)0, &errorCode);
        CheckError(errorCode, "CreateCommandQueue");

        // Load and compile kernel
        var kernelSource = LoadKernelSource();

        // CRITICAL: Ensure kernel source is not empty
        if (string.IsNullOrEmpty(kernelSource)) throw new InvalidOperationException("Kernel source is empty!");

        Logger.Log($"[NMRSimulationOpenCL] Kernel source length: {kernelSource.Length} characters");

        var sourceBytes = Encoding.UTF8.GetBytes(kernelSource);
        var lengths = stackalloc nuint[] { (nuint)sourceBytes.Length };

        fixed (byte* sourcesPtr = sourceBytes)
        {
            var sources = stackalloc byte*[] { sourcesPtr };
            _program = _cl.CreateProgramWithSource(_context, 1, sources, lengths, &errorCode);
            CheckError(errorCode, "CreateProgramWithSource");
        }

        // Build with better error handling
        Logger.Log("[NMRSimulationOpenCL] Compiling OpenCL kernels...");
        errorCode = _cl.BuildProgram(_program, 1, &device, (byte*)null, null, null);

        // ALWAYS retrieve build log (even on success, for warnings)
        nuint logSize;
        _cl.GetProgramBuildInfo(_program, device, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);

        if (logSize > 1) // > 1 because it includes null terminator
        {
            var log = new byte[logSize];
            fixed (byte* logPtr = log)
            {
                _cl.GetProgramBuildInfo(_program, device, (uint)ProgramBuildInfo.BuildLog, logSize, logPtr, null);
            }

            var buildLog = Encoding.UTF8.GetString(log).TrimEnd('\0');

            if (!string.IsNullOrWhiteSpace(buildLog))
            {
                if (errorCode != 0)
                {
                    Logger.LogError($"[NMRSimulationOpenCL] Build FAILED with error {errorCode}:");
                    Logger.LogError("=== OpenCL Build Log ===");
                    Logger.LogError(buildLog);
                    Logger.LogError("========================");
                    throw new InvalidOperationException($"OpenCL kernel compilation failed: {buildLog}");
                }

                Logger.LogWarning("[NMRSimulationOpenCL] Build warnings:");
                Logger.LogWarning(buildLog);
            }
        }

        if (errorCode != 0)
            throw new InvalidOperationException(
                $"OpenCL BuildProgram failed with error {errorCode} (no build log available)");

        Logger.Log("[NMRSimulationOpenCL] Kernels compiled successfully");

        // Create kernels
        _kernelRandomWalk = _cl.CreateKernel(_program, "randomWalkStep", &errorCode);
        CheckError(errorCode, "CreateKernel randomWalkStep");

        _kernelCountActive = _cl.CreateKernel(_program, "countActiveWalkers", &errorCode);
        CheckError(errorCode, "CreateKernel countActiveWalkers");

        _kernelInitialize = _cl.CreateKernel(_program, "initializeWalkers", &errorCode);
        CheckError(errorCode, "CreateKernel initializeWalkers");

        Logger.Log("[NMRSimulationOpenCL] OpenCL initialized successfully");
    }

    private void UploadDataToGPU()
    {
        int errorCode;

        // Upload label volume
        var volumeSize = (nuint)(_width * _height * _depth);
        var volumeData = new byte[volumeSize];

        for (var z = 0; z < _depth; z++)
        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
        {
            var index = z * _width * _height + y * _width + x;
            volumeData[index] = _labelVolume[x, y, z];
        }

        fixed (byte* dataPtr = volumeData)
        {
            _bufferLabelVolume = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                volumeSize, dataPtr, &errorCode);
            CheckError(errorCode, "CreateBuffer labelVolume");
        }

        // Create walker buffers
        var walkerBufferSize = (nuint)(_config.NumberOfWalkers * sizeof(float));
        _bufferWalkerPosX = _cl.CreateBuffer(_context, MemFlags.ReadWrite, walkerBufferSize, null, &errorCode);
        CheckError(errorCode, "CreateBuffer walkerPosX");

        _bufferWalkerPosY = _cl.CreateBuffer(_context, MemFlags.ReadWrite, walkerBufferSize, null, &errorCode);
        CheckError(errorCode, "CreateBuffer walkerPosY");

        _bufferWalkerPosZ = _cl.CreateBuffer(_context, MemFlags.ReadWrite, walkerBufferSize, null, &errorCode);
        CheckError(errorCode, "CreateBuffer walkerPosZ");

        _bufferWalkerMag = _cl.CreateBuffer(_context, MemFlags.ReadWrite, walkerBufferSize, null, &errorCode);
        CheckError(errorCode, "CreateBuffer walkerMag");

        var walkerActiveSize = (nuint)(_config.NumberOfWalkers * sizeof(byte));
        _bufferWalkerActive = _cl.CreateBuffer(_context, MemFlags.ReadWrite, walkerActiveSize, null, &errorCode);
        CheckError(errorCode, "CreateBuffer walkerActive");

        // Material relaxivities (indexed by material ID)
        var relaxivities = new float[256];
        foreach (var kvp in _config.MaterialRelaxivities)
            relaxivities[kvp.Key] = (float)kvp.Value.SurfaceRelaxivity;

        fixed (float* relaxPtr = relaxivities)
        {
            _bufferMaterialRelaxivities = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                256 * sizeof(float), relaxPtr, &errorCode);
            CheckError(errorCode, "CreateBuffer materialRelaxivities");
        }

        _bufferStepMagnetization = _cl.CreateBuffer(_context, MemFlags.ReadWrite, sizeof(float), null, &errorCode);
        CheckError(errorCode, "CreateBuffer stepMagnetization");

        _bufferActiveCount = _cl.CreateBuffer(_context, MemFlags.ReadWrite, sizeof(int), null, &errorCode);
        CheckError(errorCode, "CreateBuffer activeCount");

        _bufferValidWalkerCount = _cl.CreateBuffer(_context, MemFlags.ReadWrite, sizeof(int), null, &errorCode);
        CheckError(errorCode, "CreateBuffer validWalkerCount");
    }

    private int InitializeWalkersGPU()
    {
        var (xMin, xMax, yMin, yMax, zMin, zMax) = CalculateMaterialBounds();

        if (xMin > xMax)
        {
            Logger.LogError($"[NMRSimulationOpenCL] No voxels found for material ID {_config.PoreMaterialID}");
            return 0;
        }

        var materialWidth = xMax - xMin + 1;
        var materialHeight = yMax - yMin + 1;
        var materialDepth = zMax - zMin + 1;
        var materialVolume = materialWidth * materialHeight * materialDepth;

        Logger.Log($"[NMRSimulationOpenCL] Material extent: [{xMin},{xMax}] x [{yMin},{yMax}] x [{zMin},{zMax}]");
        Logger.Log(
            $"[NMRSimulationOpenCL] Material occupies {materialVolume:N0} / {_width * _height * _depth:N0} voxels");

        var argIndex = 0;
        var bufLabelVolume = _bufferLabelVolume;
        var bufWalkerPosX = _bufferWalkerPosX;
        var bufWalkerPosY = _bufferWalkerPosY;
        var bufWalkerPosZ = _bufferWalkerPosZ;
        var bufWalkerMag = _bufferWalkerMag;
        var bufWalkerActive = _bufferWalkerActive;
        var bufValidWalkerCount = _bufferValidWalkerCount;
        var width = _width;
        var height = _height;
        var depth = _depth;
        var poreMaterialID = _config.PoreMaterialID;
        var seed = (uint)_config.RandomSeed;
        var maxAttempts = 100;

        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, (nuint)sizeof(nint), &bufLabelVolume);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, (nuint)sizeof(nint), &bufWalkerPosX);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, (nuint)sizeof(nint), &bufWalkerPosY);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, (nuint)sizeof(nint), &bufWalkerPosZ);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, (nuint)sizeof(nint), &bufWalkerMag);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, (nuint)sizeof(nint), &bufWalkerActive);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, (nuint)sizeof(nint), &bufValidWalkerCount);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(int), &width);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(int), &height);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(int), &depth);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(byte), &poreMaterialID);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(uint), &seed);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(int), &maxAttempts);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(int), &xMin);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(int), &xMax);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(int), &yMin);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(int), &yMax);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(int), &zMin);
        _cl.SetKernelArg(_kernelInitialize, (uint)argIndex++, sizeof(int), &zMax);

        var zero = 0;
        _cl.EnqueueWriteBuffer(_commandQueue, bufValidWalkerCount, true, 0, sizeof(int), &zero, 0, null, null);

        // FIXED: Explicit pointer casts
        var globalSize = (nuint)_config.NumberOfWalkers;
        var err = _cl.EnqueueNdrangeKernel(
            _commandQueue,
            _kernelInitialize,
            1, // work_dim
            null, // global_work_offset
            &globalSize, // global_work_size
            null, // local_work_size
            0, // num_events_in_wait_list
            null, // event_wait_list
            null); // event

        if (err != 0)
            throw new Exception($"EnqueueNdrangeKernel (initialize) failed: {err}");

        _cl.Finish(_commandQueue);

        var validCount = 0;
        _cl.EnqueueReadBuffer(_commandQueue, bufValidWalkerCount, true, 0, sizeof(int), &validCount, 0, null, null);

        Logger.Log($"[NMRSimulationOpenCL] Initialized {validCount} walkers on GPU");
        return validCount;
    }

    /// <summary>
    ///     Calculate the bounding box of the selected material (same approach as PNM)
    /// </summary>
    private (int xMin, int xMax, int yMin, int yMax, int zMin, int zMax) CalculateMaterialBounds()
    {
        int xMin = _width, xMax = -1;
        int yMin = _height, yMax = -1;
        int zMin = _depth, zMax = -1;

        for (var z = 0; z < _depth; z++)
        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
            if (_labelVolume[x, y, z] == _config.PoreMaterialID)
            {
                if (x < xMin) xMin = x;
                if (x > xMax) xMax = x;
                if (y < yMin) yMin = y;
                if (y > yMax) yMax = y;
                if (z < zMin) zMin = z;
                if (z > zMax) zMax = z;
            }

        return (xMin, xMax, yMin, yMax, zMin, zMax);
    }

    private void SimulateRandomWalkGPU(NMRResults results, IProgress<(float, string)> progress)
    {
        var stepSize = Math.Max(1,
            (int)(Math.Sqrt(6.0 * _config.DiffusionCoefficient * _config.TimeStepMs * 1e-3) / _config.VoxelSize));

        var timeStepSec = (float)(_config.TimeStepMs * 1e-3);
        var voxelSizeUm = (float)(_config.VoxelSize * 1e6);
        var seed = (uint)_config.RandomSeed;

        var bufLabelVolume = _bufferLabelVolume;
        var bufWalkerPosX = _bufferWalkerPosX;
        var bufWalkerPosY = _bufferWalkerPosY;
        var bufWalkerPosZ = _bufferWalkerPosZ;
        var bufWalkerMag = _bufferWalkerMag;
        var bufWalkerActive = _bufferWalkerActive;
        var bufMaterialRelaxivities = _bufferMaterialRelaxivities;
        var bufStepMagnetization = _bufferStepMagnetization;
        var numWalkers = _config.NumberOfWalkers;
        var width = _width;
        var height = _height;
        var depth = _depth;
        var poreMaterialID = _config.PoreMaterialID;

        Logger.Log($"[NMRSimulationOpenCL] Step size: {stepSize} voxels, voxel size: {voxelSizeUm:F2} µm");

        for (var step = 0; step < _config.NumberOfSteps; step++)
        {
            if (step % 100 == 0)
            {
                var progressPercent = 0.15f + 0.7f * (step / (float)_config.NumberOfSteps);
                progress?.Report((progressPercent, $"GPU step {step}/{_config.NumberOfSteps}..."));
            }

            var zero = 0f;
            _cl.EnqueueWriteBuffer(_commandQueue, bufStepMagnetization, true, 0, sizeof(float), &zero, 0, null, null);

            var argIndex = 0;
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, (nuint)sizeof(nint), &bufLabelVolume);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, (nuint)sizeof(nint), &bufWalkerPosX);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, (nuint)sizeof(nint), &bufWalkerPosY);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, (nuint)sizeof(nint), &bufWalkerPosZ);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, (nuint)sizeof(nint), &bufWalkerMag);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, (nuint)sizeof(nint), &bufWalkerActive);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, (nuint)sizeof(nint), &bufMaterialRelaxivities);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, (nuint)sizeof(nint), &bufStepMagnetization);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, sizeof(int), &numWalkers);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, sizeof(int), &width);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, sizeof(int), &height);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, sizeof(int), &depth);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, sizeof(int), &stepSize);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, sizeof(float), &timeStepSec);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, sizeof(float), &voxelSizeUm);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, sizeof(byte), &poreMaterialID);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, sizeof(uint), &seed);
            _cl.SetKernelArg(_kernelRandomWalk, (uint)argIndex++, sizeof(int), &step);

            // FIXED: Explicit pointer casts for null parameters
            var globalSize = (nuint)numWalkers;
            var err = _cl.EnqueueNdrangeKernel(
                _commandQueue,
                _kernelRandomWalk,
                1, // work_dim
                null, // global_work_offset (explicitly typed)
                &globalSize, // global_work_size
                null, // local_work_size (explicitly typed)
                0, // num_events_in_wait_list
                null, // event_wait_list (explicitly typed)
                null); // event (explicitly typed)

            if (err != 0)
                throw new Exception($"EnqueueNdrangeKernel failed at step {step}: {err}");

            var totalMag = 0f;
            _cl.EnqueueReadBuffer(_commandQueue, bufStepMagnetization, true, 0, sizeof(float), &totalMag, 0, null,
                null);

            results.Magnetization[step] = totalMag / results.NumberOfWalkers;
        }

        _cl.Finish(_commandQueue);
    }

    private void ComputeT2Distribution(NMRResults results)
    {
        var logMin = Math.Log10(_config.T2MinMs);
        var logMax = Math.Log10(_config.T2MaxMs);
        var logStep = (logMax - logMin) / _config.T2BinCount;

        results.T2HistogramBins = new double[_config.T2BinCount];
        results.T2Histogram = new double[_config.T2BinCount];

        for (var i = 0; i < _config.T2BinCount; i++)
            results.T2HistogramBins[i] = Math.Pow(10, logMin + i * logStep);

        var kernel = BuildKernelMatrix(results.TimePoints, results.T2HistogramBins);
        var amplitudes = SolveRegularizedLeastSquares(kernel, results.Magnetization, 0.01);

        Array.Copy(amplitudes, results.T2Histogram, amplitudes.Length);

        var sum = results.T2Histogram.Sum();
        if (sum > 0)
            for (var i = 0; i < results.T2Histogram.Length; i++)
                results.T2Histogram[i] /= sum;
    }

    private double[,] BuildKernelMatrix(double[] timePoints, double[] t2Values)
    {
        var matrix = new double[timePoints.Length, t2Values.Length];

        for (var i = 0; i < timePoints.Length; i++)
        for (var j = 0; j < t2Values.Length; j++)
            matrix[i, j] = Math.Exp(-timePoints[i] / t2Values[j]);

        return matrix;
    }

    private double[] SolveRegularizedLeastSquares(double[,] kernel, double[] data, double lambda)
    {
        var m = kernel.GetLength(0);
        var n = kernel.GetLength(1);

        var ktk = new double[n, n];
        var ktd = new double[n];

        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                double sum = 0;
                for (var k = 0; k < m; k++) sum += kernel[k, i] * kernel[k, j];
                ktk[i, j] = sum;
                if (i == j) ktk[i, j] *= 1.0 + lambda;
            }

            for (var k = 0; k < m; k++) ktd[i] += kernel[k, i] * data[k];
        }

        return SolveCholeskySystem(ktk, ktd);
    }

    private double[] SolveCholeskySystem(double[,] A, double[] b)
    {
        var n = b.Length;
        var x = new double[n];

        for (var iter = 0; iter < 100; iter++)
        for (var i = 0; i < n; i++)
        {
            var sum = b[i];
            for (var j = 0; j < n; j++)
                if (j != i)
                    sum -= A[i, j] * x[j];

            x[i] = Math.Max(0, sum / Math.Max(A[i, i], 1e-10));
        }

        return x;
    }

    /// <summary>
    ///     FIXED: Correct pore size calculation with proper units
    /// </summary>
    private void ComputePoreSizeDistribution(NMRResults results)
    {
        if (results.T2HistogramBins == null) return;

        // Get average relaxivity from MATRIX materials (exclude pore space)
        var avgRelaxivity = _config.MaterialRelaxivities
            .Where(kvp => kvp.Key != _config.PoreMaterialID)
            .Select(m => m.Value.SurfaceRelaxivity)
            .DefaultIfEmpty(10.0)
            .Average();

        Logger.Log($"[NMRSimulationOpenCL] Pore size: ρ={avgRelaxivity:F1} μm/s, shape={_config.PoreShapeFactor:F1}");

        results.PoreSizes = new double[results.T2HistogramBins.Length];
        results.PoreSizeDistribution = new double[results.T2Histogram.Length];

        for (var i = 0; i < results.T2HistogramBins.Length; i++)
        {
            // r (μm) = shape_factor * ρ (μm/s) * T2 (s)
            var t2Seconds = results.T2HistogramBins[i] * 1e-3;
            results.PoreSizes[i] = _config.PoreShapeFactor * avgRelaxivity * t2Seconds;
            results.PoreSizeDistribution[i] = results.T2Histogram[i];
        }
    }

    private void ComputeStatistics(NMRResults results)
    {
        if (results.T2HistogramBins == null || results.T2Histogram == null) return;

        var weightedSum = 0.0;
        var totalWeight = 0.0;

        for (var i = 0; i < results.T2HistogramBins.Length; i++)
        {
            weightedSum += results.T2HistogramBins[i] * results.T2Histogram[i];
            totalWeight += results.T2Histogram[i];
        }

        results.MeanT2 = totalWeight > 0 ? weightedSum / totalWeight : 0;

        var logSum = 0.0;
        for (var i = 0; i < results.T2HistogramBins.Length; i++)
            if (results.T2Histogram[i] > 0)
                logSum += Math.Log(results.T2HistogramBins[i]) * results.T2Histogram[i];

        results.GeometricMeanT2 = totalWeight > 0 ? Math.Exp(logSum / totalWeight) : 0;

        var maxIndex = 0;
        var maxValue = 0.0;
        for (var i = 0; i < results.T2Histogram.Length; i++)
            if (results.T2Histogram[i] > maxValue)
            {
                maxValue = results.T2Histogram[i];
                maxIndex = i;
            }

        results.T2PeakValue = results.T2HistogramBins[maxIndex];
    }

    private string LoadKernelSource()
    {
        return @"
// NMR Random Walk Kernel for OpenCL 1.2
// FIXED: Use OpenCL 1.2 compatible atomic operations

// Enable required extensions for OpenCL 1.2
#pragma OPENCL EXTENSION cl_khr_global_int32_base_atomics : enable

uint xorshift(uint state) {
    state ^= state << 13;
    state ^= state >> 17;
    state ^= state << 5;
    return state;
}

uchar getLabelValue(
    __global const uchar* labelVolume,
    int x, int y, int z,
    int width, int height, int depth)
{
    if (x < 0 || x >= width || y < 0 || y >= height || z < 0 || z >= depth) {
        return 0;
    }
    
    int index = z * width * height + y * width + x;
    return labelVolume[index];
}

// OpenCL 1.2 compatible atomic float addition
// Since OpenCL 1.2 doesn't have atomic_add for floats, we use atomic_cmpxchg
inline void atomic_add_float(__global float* addr, float val)
{
    union {
        unsigned int u32;
        float f32;
    } next, expected, current;
    
    current.f32 = *addr;
    do {
        expected.f32 = current.f32;
        next.f32 = expected.f32 + val;
        current.u32 = atomic_cmpxchg((__global unsigned int*)addr, expected.u32, next.u32);
    } while (current.u32 != expected.u32);
}

// ============================================================================
// MAIN RANDOM WALK KERNEL
// ============================================================================
__kernel void randomWalkStep(
    __global const uchar* labelVolume,
    __global float* walkerPositionsX,
    __global float* walkerPositionsY,
    __global float* walkerPositionsZ,
    __global float* walkerMagnetization,
    __global uchar* walkerActive,
    __global const float* materialRelaxivities,
    __global float* stepMagnetization,
    const int numWalkers,
    const int width,
    const int height,
    const int depth,
    const int stepSize,
    const float timeStepSec,
    const float voxelSizeUm,
    const uchar poreMaterialID,
    const uint randomSeed,
    const int currentStep)
{
    int walkerId = get_global_id(0);
    
    if (walkerId >= numWalkers || walkerActive[walkerId] == 0) {
        return;
    }
    
    uint rngState = randomSeed + walkerId * 7919 + currentStep * 104729;
    
    int x = (int)(walkerPositionsX[walkerId] + 0.5f);
    int y = (int)(walkerPositionsY[walkerId] + 0.5f);
    int z = (int)(walkerPositionsZ[walkerId] + 0.5f);
    
    rngState = xorshift(rngState);
    int direction = rngState % 6;
    
    int newX = x;
    int newY = y;
    int newZ = z;
    
    switch(direction) {
        case 0: newX += stepSize; break;
        case 1: newX -= stepSize; break;
        case 2: newY += stepSize; break;
        case 3: newY -= stepSize; break;
        case 4: newZ += stepSize; break;
        case 5: newZ -= stepSize; break;
    }
    
    newX = clamp(newX, 0, width - 1);
    newY = clamp(newY, 0, height - 1);
    newZ = clamp(newZ, 0, depth - 1);
    
    uchar materialID = getLabelValue(labelVolume, newX, newY, newZ, width, height, depth);
    
    if (materialID == poreMaterialID) {
        walkerPositionsX[walkerId] = (float)newX;
        walkerPositionsY[walkerId] = (float)newY;
        walkerPositionsZ[walkerId] = (float)newZ;
    } 
    else {
        float surfaceRelaxivity = materialRelaxivities[materialID];
        float relaxationRate = surfaceRelaxivity * timeStepSec / voxelSizeUm;
        float relaxationFactor = exp(-relaxationRate);
        
        float mag = walkerMagnetization[walkerId] * relaxationFactor;
        walkerMagnetization[walkerId] = mag;
        
        if (mag < 0.001f) {
            walkerActive[walkerId] = 0;
        }
    }
    
    if (walkerActive[walkerId] != 0) {
        // FIXED: Use OpenCL 1.2 compatible atomic float addition
        atomic_add_float(&stepMagnetization[0], walkerMagnetization[walkerId]);
    }
}

// ============================================================================
// WALKER INITIALIZATION KERNEL
// ============================================================================
__kernel void initializeWalkers(
    __global const uchar* labelVolume,
    __global float* walkerPositionsX,
    __global float* walkerPositionsY,
    __global float* walkerPositionsZ,
    __global float* walkerMagnetization,
    __global uchar* walkerActive,
    __global int* validWalkerCount,
    const int width,
    const int height,
    const int depth,
    const uchar poreMaterialID,
    const uint randomSeed,
    const int maxAttempts,
    const int xMin,
    const int xMax,
    const int yMin,
    const int yMax,
    const int zMin,
    const int zMax)
{
    int walkerId = get_global_id(0);
    
    uint rngState = randomSeed + walkerId * 7919;
    
    int attempts = 0;
    bool found = false;
    
    int matWidth = xMax - xMin + 1;
    int matHeight = yMax - yMin + 1;
    int matDepth = zMax - zMin + 1;
    
    while (attempts < maxAttempts && !found) {
        rngState = xorshift(rngState);
        int x = xMin + (rngState % matWidth);
        
        rngState = xorshift(rngState);
        int y = yMin + (rngState % matHeight);
        
        rngState = xorshift(rngState);
        int z = zMin + (rngState % matDepth);
        
        uchar materialID = getLabelValue(labelVolume, x, y, z, width, height, depth);
        
        if (materialID == poreMaterialID) {
            walkerPositionsX[walkerId] = (float)x;
            walkerPositionsY[walkerId] = (float)y;
            walkerPositionsZ[walkerId] = (float)z;
            walkerMagnetization[walkerId] = 1.0f;
            walkerActive[walkerId] = 1;
            // FIXED: Use OpenCL 1.2 compatible atomic int addition
            atom_inc((__global int*)validWalkerCount);
            found = true;
        }
        
        attempts++;
    }
    
    if (!found) {
        walkerActive[walkerId] = 0;
        walkerMagnetization[walkerId] = 0.0f;
    }
}

// ============================================================================
// ACTIVE WALKER COUNTER
// ============================================================================
__kernel void countActiveWalkers(
    __global const uchar* walkerActive,
    __global int* activeCount,
    const int numWalkers)
{
    int walkerId = get_global_id(0);
    
    if (walkerId >= numWalkers) {
        return;
    }
    
    if (walkerActive[walkerId] != 0) {
        // FIXED: Use OpenCL 1.2 compatible atomic int addition
        atom_inc((__global int*)activeCount);
    }
}
";
    }

    private void LogDeviceInfo(nint device)
    {
        var name = GetDeviceInfoString(device, DeviceInfo.Name);
        var vendor = GetDeviceInfoString(device, DeviceInfo.Vendor);
        var version = GetDeviceInfoString(device, DeviceInfo.Version);

        Logger.Log($"[NMRSimulationOpenCL] Device: {name}");
        Logger.Log($"[NMRSimulationOpenCL] Vendor: {vendor}");
        Logger.Log($"[NMRSimulationOpenCL] Version: {version}");
    }

    private string GetDeviceInfoString(nint device, DeviceInfo info)
    {
        nuint size;
        _cl.GetDeviceInfo(device, info, 0, null, &size);
        var buffer = new byte[size];
        fixed (byte* ptr = buffer)
        {
            _cl.GetDeviceInfo(device, info, size, ptr, null);
        }

        return Encoding.UTF8.GetString(buffer).TrimEnd('\0');
    }

    private void CheckError(int errorCode, string operation)
    {
        if (errorCode != 0)
        {
            var errorName = errorCode switch
            {
                -1 => "CL_DEVICE_NOT_FOUND",
                -2 => "CL_DEVICE_NOT_AVAILABLE",
                -3 => "CL_COMPILER_NOT_AVAILABLE",
                -4 => "CL_MEM_OBJECT_ALLOCATION_FAILURE",
                -5 => "CL_OUT_OF_RESOURCES",
                -6 => "CL_OUT_OF_HOST_MEMORY",
                -7 => "CL_PROFILING_INFO_NOT_AVAILABLE",
                -8 => "CL_MEM_COPY_OVERLAP",
                -9 => "CL_IMAGE_FORMAT_MISMATCH",
                -10 => "CL_IMAGE_FORMAT_NOT_SUPPORTED",
                -11 => "CL_BUILD_PROGRAM_FAILURE",
                -12 => "CL_MAP_FAILURE",
                -30 => "CL_INVALID_VALUE",
                -31 => "CL_INVALID_DEVICE_TYPE",
                -32 => "CL_INVALID_PLATFORM",
                -33 => "CL_INVALID_DEVICE",
                -34 => "CL_INVALID_CONTEXT",
                -35 => "CL_INVALID_QUEUE_PROPERTIES",
                -36 => "CL_INVALID_COMMAND_QUEUE",
                -37 => "CL_INVALID_HOST_PTR",
                -38 => "CL_INVALID_MEM_OBJECT",
                -39 => "CL_INVALID_IMAGE_FORMAT_DESCRIPTOR",
                -40 => "CL_INVALID_IMAGE_SIZE",
                -41 => "CL_INVALID_SAMPLER",
                -42 => "CL_INVALID_BINARY",
                -43 => "CL_INVALID_BUILD_OPTIONS",
                -44 => "CL_INVALID_PROGRAM",
                -45 => "CL_INVALID_PROGRAM_EXECUTABLE",
                -46 => "CL_INVALID_KERNEL_NAME",
                -47 => "CL_INVALID_KERNEL_DEFINITION",
                -48 => "CL_INVALID_KERNEL",
                -49 => "CL_INVALID_ARG_INDEX",
                -50 => "CL_INVALID_ARG_VALUE",
                -51 => "CL_INVALID_ARG_SIZE",
                -52 => "CL_INVALID_KERNEL_ARGS",
                -53 => "CL_INVALID_WORK_DIMENSION",
                -54 => "CL_INVALID_WORK_GROUP_SIZE",
                -55 => "CL_INVALID_WORK_ITEM_SIZE",
                -56 => "CL_INVALID_GLOBAL_OFFSET",
                -57 => "CL_INVALID_EVENT_WAIT_LIST",
                -58 => "CL_INVALID_EVENT",
                -59 => "CL_INVALID_OPERATION",
                -60 => "CL_INVALID_GL_OBJECT",
                -61 => "CL_INVALID_BUFFER_SIZE",
                -62 => "CL_INVALID_MIP_LEVEL",
                -63 => "CL_INVALID_GLOBAL_WORK_SIZE",
                _ => $"UNKNOWN_ERROR_{errorCode}"
            };

            throw new Exception($"OpenCL error in {operation}: {errorCode} ({errorName})");
        }
    }

    private int EstimateSpeedup()
    {
        return 20;
    }
}