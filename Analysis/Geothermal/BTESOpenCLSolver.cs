// GeoscientistToolkit/Analysis/Geothermal/BTESOpenCLSolver.cs
//
// ================================================================================================
// REFERENCES (APA Format):
// ================================================================================================
// This OpenCL-accelerated BTES (Borehole Thermal Energy Storage) simulation solver implements
// GPU-based parallel computing for seasonal thermal battery simulations. The implementation is
// based on methods documented in the following scientific literature:
//
// BTES AND SEASONAL THERMAL ENERGY STORAGE:
// ------------------------------------------------------------------------------------------------
//
// Baser, T., & McCartney, J. S. (2020). Design and simulation of a borehole thermal energy
//     storage system for residential buildings. Renewable Energy, 157, 1152-1163.
//     https://doi.org/10.1016/j.renene.2020.05.104
//
// Gao, L., Zhao, J., An, Q., Wang, J., & Liu, X. (2017). A review on borehole seasonal solar
//     thermal energy storage. Energy Procedia, 158, 3967-3972.
//     https://doi.org/10.1016/j.egypro.2019.01.842
//
// Lanahan, M., & Tabares-Velasco, P. C. (2017). Seasonal thermal energy storage: A critical
//     review on BTES systems, modeling, and system design for higher system efficiency.
//     Energies, 10(6), Article 743. https://doi.org/10.3390/en10060743
//
// Novo, A. V., Bayon, J. R., Castro-Fresno, D., & Rodriguez-Hernandez, J. (2010). Review of
//     seasonal heat storage in large basins: Water tanks and gravel-water pits. Applied Energy,
//     87(2), 390-397. https://doi.org/10.1016/j.apenergy.2009.06.033
//
// Rad, F. M., & Fung, A. S. (2016). Solar community heating and cooling system with borehole
//     thermal energy storage – Review of systems. Renewable and Sustainable Energy Reviews,
//     60, 1550-1561. https://doi.org/10.1016/j.rser.2016.03.025
//
// Sibbitt, B., McClenahan, D., Djebbar, R., Thornton, J., Wong, B., Carriere, J., & Kokko, J.
//     (2012). The performance of a high solar fraction seasonal storage district heating system –
//     Five years of operation. Energy Procedia, 30, 856-865.
//     https://doi.org/10.1016/j.egypro.2012.11.097
//
// Xu, L., Torrens, J. I., Guo, F., Yang, X., & Hensen, J. L. M. (2018). Application of large
//     underground seasonal thermal energy storage in district heating system: A model-based
//     energy performance assessment of a pilot system in Chifeng, China. Applied Thermal
//     Engineering, 137, 319-328. https://doi.org/10.1016/j.applthermaleng.2018.03.047
//
//
// GPU/PARALLEL COMPUTING FOR THERMAL SIMULATIONS:
// ------------------------------------------------------------------------------------------------
//
// Akimova, E. N., Filimonov, M. Y., Misilov, V. E., Vaganova, N. A., & Kuznetsov, A. D. (2021).
//     Simulation of heat and mass transfer in open geothermal systems: A parallel implementation.
//     In L. Sokolinsky & M. Zymbler (Eds.), Parallel Computational Technologies (PCT 2021),
//     Communications in Computer and Information Science (Vol. 1437, pp. 243-254). Springer.
//     https://doi.org/10.1007/978-3-030-81691-9_17
//
// Liu, B., & Li, S. (2013). A fast and interactive heat conduction simulator on GPUs. Journal of
//     Computational and Applied Mathematics, 255, 581-592. https://doi.org/10.1016/j.cam.2013.06.031
//
// Munshi, A., Gaster, B., Mattson, T. G., Fung, J., & Ginsburg, D. (2011). OpenCL programming guide.
//     Addison-Wesley Professional.
//
// Stone, J. E., Gohara, D., & Shi, G. (2010). OpenCL: A parallel programming standard for
//     heterogeneous computing systems. Computing in Science & Engineering, 12(3), 66-73.
//     https://doi.org/10.1109/MCSE.2010.69
//
//
// OPENCL SPECIFICATIONS AND STANDARDS:
// ------------------------------------------------------------------------------------------------
//
// Khronos Group. (2020). The OpenCL specification version 1.2. Retrieved from
//     https://www.khronos.org/registry/OpenCL/specs/opencl-1.2.pdf
//
//
// ------------------------------------------------------------------------------------------------
// IMPLEMENTATION NOTES:
// ------------------------------------------------------------------------------------------------
// This OpenCL implementation extends the geothermal solver for BTES applications by:
//
// 1. Applying seasonal energy curves (365-day profiles) on GPU
// 2. Handling thermal charging (summer) and discharging (winter) cycles
// 3. Saving all time frames for animation visualization
// 4. Supporting random weather variations in energy demand
// 5. Maintaining identical numerical accuracy to geothermal solver
//
// The solver uses OpenCL 1.2 API through Silk.NET bindings, providing cross-platform GPU
// acceleration for long-term seasonal thermal storage simulations.
// ================================================================================================


using System.Runtime.InteropServices;
using System.Text;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     OpenCL accelerated BTES simulation solver using Silk.NET OpenCL 1.2.
///     Extends geothermal solver with seasonal energy curve application for thermal battery simulations.
/// </summary>
public class BTESOpenCLSolver : IDisposable
{
    private readonly CL _cl;
    private nint _boundaryConditionsKernel;
    private nint _cellVolumesBuffer;
    private nint _conductivityBuffer;
    private nint _context;
    private nint _densityBuffer;
    private nint _device;
    private nint _dispersionBuffer;
    private nint _fluidTempDownBuffer;
    private nint _fluidTempUpBuffer;
    private nint _heatExchangerParamsBuffer;
    private nint _heatTransferKernel;
    private bool _isInitialized;
    private nint _materialIdBuffer;
    private nint _maxChangeBuffer;
    private nint _newTempBuffer;
    private int _nr, _nth, _nz;
    private int _nzHE;
    private nint _program;
    private nint _queue;
    private nint _rCoordBuffer;
    private nint _reductionKernel;

    // BTES-specific buffers
    private nint _seasonalEnergyBuffer; // 365-day energy curve
    private int _seasonalCurveDays = 365;

    private nint _specificHeatBuffer;
    private nint _temperatureBuffer;
    private nint _temperatureChangeBuffer;
    private nint _temperatureOldBuffer;
    private nint _velocityBuffer;
    private nint _zCoordBuffer;

    public BTESOpenCLSolver()
    {
        _cl = CL.GetApi();
        IsAvailable = InitializeOpenCL();
    }

    public bool IsAvailable { get; }
    public string DeviceName { get; private set; }
    public string DeviceVendor { get; private set; }
    public ulong DeviceGlobalMemory { get; private set; }

    public void Dispose()
    {
        // Release BTES-specific buffers
        if (_seasonalEnergyBuffer != 0) _cl.ReleaseMemObject(_seasonalEnergyBuffer);

        // Release heat exchanger buffers
        if (_heatExchangerParamsBuffer != 0) _cl.ReleaseMemObject(_heatExchangerParamsBuffer);
        if (_fluidTempDownBuffer != 0) _cl.ReleaseMemObject(_fluidTempDownBuffer);
        if (_fluidTempUpBuffer != 0) _cl.ReleaseMemObject(_fluidTempUpBuffer);
        if (_cellVolumesBuffer != 0) _cl.ReleaseMemObject(_cellVolumesBuffer);

        // Release mesh buffers
        if (_maxChangeBuffer != 0) _cl.ReleaseMemObject(_maxChangeBuffer);
        if (_zCoordBuffer != 0) _cl.ReleaseMemObject(_zCoordBuffer);
        if (_rCoordBuffer != 0) _cl.ReleaseMemObject(_rCoordBuffer);
        if (_dispersionBuffer != 0) _cl.ReleaseMemObject(_dispersionBuffer);
        if (_velocityBuffer != 0) _cl.ReleaseMemObject(_velocityBuffer);
        if (_specificHeatBuffer != 0) _cl.ReleaseMemObject(_specificHeatBuffer);
        if (_densityBuffer != 0) _cl.ReleaseMemObject(_densityBuffer);
        if (_conductivityBuffer != 0) _cl.ReleaseMemObject(_conductivityBuffer);
        if (_newTempBuffer != 0) _cl.ReleaseMemObject(_newTempBuffer);
        if (_temperatureOldBuffer != 0) _cl.ReleaseMemObject(_temperatureOldBuffer);
        if (_temperatureBuffer != 0) _cl.ReleaseMemObject(_temperatureBuffer);
        if (_temperatureChangeBuffer != 0) _cl.ReleaseMemObject(_temperatureChangeBuffer);
        if (_materialIdBuffer != 0) _cl.ReleaseMemObject(_materialIdBuffer);

        // Release kernels
        if (_reductionKernel != 0) _cl.ReleaseKernel(_reductionKernel);
        if (_heatTransferKernel != 0) _cl.ReleaseKernel(_heatTransferKernel);

        // Release program, queue, context
        if (_program != 0) _cl.ReleaseProgram(_program);
        if (_queue != 0) _cl.ReleaseCommandQueue(_queue);
        if (_context != 0) _cl.ReleaseContext(_context);

        _cl?.Dispose();
    }

    /// <summary>
    ///     Initializes OpenCL context and compiles kernels.
    /// </summary>
    private unsafe bool InitializeOpenCL()
    {
        try
        {
            Logger.Log("Attempting to initialize OpenCL for BTES...");

            // Get platform
            uint numPlatforms;
            _cl.GetPlatformIDs(0, null, &numPlatforms);

            if (numPlatforms == 0)
            {
                Logger.LogWarning("No OpenCL platforms found.");
                Logger.LogWarning("Please ensure GPU drivers with OpenCL support are installed.");
                return false;
            }

            Logger.Log($"Found {numPlatforms} OpenCL platform(s)");

            var platforms = new nint[numPlatforms];
            fixed (nint* platformsPtr = platforms)
            {
                _cl.GetPlatformIDs(numPlatforms, platformsPtr, null);
            }

            // Get device (prefer GPU)
            foreach (var platform in platforms)
            {
                uint numDevices;
                _cl.GetDeviceIDs(platform, DeviceType.Gpu, 0, null, &numDevices);

                if (numDevices > 0)
                {
                    var devices = new nint[numDevices];
                    fixed (nint* devicesPtr = devices)
                    {
                        _cl.GetDeviceIDs(platform, DeviceType.Gpu, numDevices, devicesPtr, null);
                    }

                    _device = devices[0];

                    // Get device info
                    nuint paramSize;
                    var nameBuffer = new byte[256];
                    fixed (byte* namePtr = nameBuffer)
                    {
                        _cl.GetDeviceInfo(_device, (uint)DeviceInfo.Name, 256, namePtr, &paramSize);
                        DeviceName = Encoding.UTF8.GetString(nameBuffer, 0, (int)paramSize - 1);

                        _cl.GetDeviceInfo(_device, (uint)DeviceInfo.Vendor, 256, namePtr, &paramSize);
                        DeviceVendor = Encoding.UTF8.GetString(nameBuffer, 0, (int)paramSize - 1);
                    }

                    ulong globalMem;
                    _cl.GetDeviceInfo(_device, (uint)DeviceInfo.GlobalMemSize, sizeof(ulong), &globalMem, null);
                    DeviceGlobalMemory = globalMem;

                    Logger.Log($"OpenCL Device: {DeviceName} ({DeviceVendor})");
                    Logger.Log($"Global Memory: {DeviceGlobalMemory / (1024 * 1024)} MB");

                    break;
                }
            }

            // Fallback to CPU if no GPU found
            if (_device == 0)
            {
                uint numDevices;
                _cl.GetDeviceIDs(platforms[0], DeviceType.Cpu, 0, null, &numDevices);

                if (numDevices > 0)
                {
                    var devices = new nint[numDevices];
                    fixed (nint* devicesPtr = devices)
                    {
                        _cl.GetDeviceIDs(platforms[0], DeviceType.Cpu, numDevices, devicesPtr, null);
                    }

                    _device = devices[0];
                    Logger.Log("Using OpenCL CPU device for BTES");
                }
                else
                {
                    Logger.LogWarning("No OpenCL devices found.");
                    return false;
                }
            }

            // Create context
            int errCode;
            var device = _device;
            _context = _cl.CreateContext(null, 1, &device, null, null, &errCode);
            if (errCode != 0)
            {
                Logger.LogWarning($"Failed to create OpenCL context: {errCode}");
                return false;
            }

            // Create command queue
            device = _device;
            _queue = _cl.CreateCommandQueue(_context, device, (CommandQueueProperties)0, &errCode);
            if (errCode != 0)
            {
                Logger.LogWarning($"Failed to create command queue: {errCode}");
                return false;
            }

            // Compile kernels
            if (!CompileKernels())
            {
                Logger.LogWarning("Failed to compile OpenCL kernels");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"OpenCL initialization error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Compiles OpenCL kernels for BTES heat transfer simulation.
    /// </summary>
    private unsafe bool CompileKernels()
    {
        var kernelSource = GetKernelSource();
        var sourcePtr = Marshal.StringToHGlobalAnsi(kernelSource);

        try
        {
            var sourceLen = (nuint)kernelSource.Length;
            int errCode;

            _program = _cl.CreateProgramWithSource(_context, 1, (byte**)&sourcePtr, &sourceLen, &errCode);
            if (errCode != 0)
            {
                Logger.LogWarning($"Failed to create program: {errCode}");
                return false;
            }

            // Build program
            var device = _device;
            errCode = _cl.BuildProgram(_program, 1, &device, (byte*)null, null, null);
            if (errCode != 0)
            {
                // Get build log
                nuint logSize;
                _cl.GetProgramBuildInfo(_program, device, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);

                var log = new byte[logSize];
                fixed (byte* logPtr = log)
                {
                    _cl.GetProgramBuildInfo(_program, device, (uint)ProgramBuildInfo.BuildLog, logSize, logPtr, null);
                }

                Logger.LogWarning($"BTES Kernel build failed:\n{Encoding.UTF8.GetString(log)}");
                return false;
            }

            // Create kernels
            _heatTransferKernel = _cl.CreateKernel(_program, "btes_heat_transfer_kernel", &errCode);
            if (errCode != 0)
            {
                Logger.LogWarning($"Failed to create btes_heat_transfer_kernel: {errCode}");
                return false;
            }

            _reductionKernel = _cl.CreateKernel(_program, "reduction_max_kernel", &errCode);
            if (errCode != 0)
            {
                Logger.LogWarning($"Failed to create reduction_max_kernel: {errCode}");
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(sourcePtr);
        }
    }

    /// <summary>
    ///     Initializes GPU buffers for BTES simulation data.
    /// </summary>
    public unsafe bool InitializeBuffers(GeothermalMesh mesh, GeothermalSimulationOptions options)
    {
        if (!IsAvailable)
            return false;

        _nr = mesh.RadialPoints;
        _nth = mesh.AngularPoints;
        _nz = mesh.VerticalPoints;
        _nzHE = Math.Max(20, (int)(options.BoreholeDataset.TotalDepth / 50));

        try
        {
            int errCode;
            var totalSize = _nr * _nth * _nz;

            // --- 3D mesh field buffers
            _temperatureBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "temperatureBuffer");

            _temperatureOldBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "temperatureOldBuffer");

            _newTempBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "newTempBuffer");

            _conductivityBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "conductivityBuffer");

            _densityBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "densityBuffer");

            _specificHeatBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "specificHeatBuffer");

            _velocityBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * 3 * sizeof(float)), null, &errCode);
            CheckError(errCode, "velocityBuffer");

            _dispersionBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "dispersionBuffer");

            _rCoordBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(_nr * sizeof(float)), null, &errCode);
            CheckError(errCode, "rCoordBuffer");

            _zCoordBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(_nz * sizeof(float)), null, &errCode);
            CheckError(errCode, "zCoordBuffer");

            _materialIdBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(byte)), null, &errCode);
            CheckError(errCode, "materialIdBuffer");

            // --- Reduction buffers for max |ΔT|
            _maxChangeBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                1024 * sizeof(float), null, &errCode);
            CheckError(errCode, "maxChangeBuffer");

            _temperatureChangeBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "temperatureChangeBuffer");

            // --- Heat exchanger (HE) buffers
            _heatExchangerParamsBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                16 * sizeof(float), null, &errCode);
            CheckError(errCode, "heatExchangerParamsBuffer");

            _fluidTempDownBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(_nzHE * sizeof(float)), null, &errCode);
            CheckError(errCode, "fluidTempDownBuffer");

            _fluidTempUpBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                (nuint)(_nzHE * sizeof(float)), null, &errCode);
            CheckError(errCode, "fluidTempUpBuffer");

            _cellVolumesBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &errCode);
            CheckError(errCode, "cellVolumesBuffer");

            // --- BTES-specific: Seasonal energy curve buffer
            _seasonalEnergyBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly,
                (nuint)(_seasonalCurveDays * sizeof(float)), null, &errCode);
            CheckError(errCode, "seasonalEnergyBuffer");

            // Upload static mesh data
            UploadMeshData(mesh);

            // Upload initial HE parameters
            UploadHeatExchangerParams(options);

            // Upload seasonal energy curve
            UploadSeasonalEnergyCurve(options);

            _isInitialized = true;
            Logger.Log("BTES OpenCL buffers initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Failed to initialize BTES OpenCL buffers: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Uploads mesh data to GPU.
    /// </summary>
    private unsafe void UploadMeshData(GeothermalMesh mesh)
    {
        var totalSize = _nr * _nth * _nz;

        // Flatten 3D arrays to 1D for GPU upload
        var conductivity = new float[totalSize];
        var density = new float[totalSize];
        var specificHeat = new float[totalSize];
        var cellVolumes = new float[totalSize];
        var materialIds = new byte[totalSize];

        var idx = 0;
        for (var i = 0; i < _nr; i++)
        for (var j = 0; j < _nth; j++)
        for (var k = 0; k < _nz; k++)
        {
            conductivity[idx] = mesh.ThermalConductivities[i, j, k];
            density[idx] = mesh.Densities[i, j, k];
            specificHeat[idx] = mesh.SpecificHeats[i, j, k];
            cellVolumes[idx] = mesh.CellVolumes[i, j, k];
            materialIds[idx] = mesh.MaterialIds[i, j, k];
            idx++;
        }

        // Upload data to GPU
        fixed (float* conductivityPtr = conductivity)
        {
            _cl.EnqueueWriteBuffer(_queue, _conductivityBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), conductivityPtr, 0, null, null);
        }

        fixed (float* densityPtr = density)
        {
            _cl.EnqueueWriteBuffer(_queue, _densityBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), densityPtr, 0, null, null);
        }

        fixed (float* specificHeatPtr = specificHeat)
        {
            _cl.EnqueueWriteBuffer(_queue, _specificHeatBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), specificHeatPtr, 0, null, null);
        }

        fixed (float* cellVolumesPtr = cellVolumes)
        {
            _cl.EnqueueWriteBuffer(_queue, _cellVolumesBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), cellVolumesPtr, 0, null, null);
        }

        fixed (float* rPtr = mesh.R)
        {
            _cl.EnqueueWriteBuffer(_queue, _rCoordBuffer, true, 0,
                (nuint)(_nr * sizeof(float)), rPtr, 0, null, null);
        }

        fixed (float* zPtr = mesh.Z)
        {
            _cl.EnqueueWriteBuffer(_queue, _zCoordBuffer, true, 0,
                (nuint)(_nz * sizeof(float)), zPtr, 0, null, null);
        }

        fixed (byte* materialIdsPtr = materialIds)
        {
            _cl.EnqueueWriteBuffer(_queue, _materialIdBuffer, true, 0,
                (nuint)(totalSize * sizeof(byte)), materialIdsPtr, 0, null, null);
        }
    }

    /// <summary>
    ///     Uploads heat exchanger parameters to GPU.
    /// </summary>
    private unsafe void UploadHeatExchangerParams(GeothermalSimulationOptions options)
    {
        // HTC base
        var D_in = (float)Math.Max(0.01, options.PipeInnerDiameter);
        var mu = (float)Math.Max(1e-3, options.FluidViscosity);
        var mdot = (float)options.FluidMassFlowRate;
        var kf = (float)Math.Max(0.2, options.FluidThermalConductivity);
        var cpf = (float)Math.Max(1000, options.FluidSpecificHeat);

        var Re = 4.0f * mdot / (MathF.PI * D_in * mu);
        var Pr = mu * cpf / kf;
        var Nu = Re < 2300f ? 4.36f : 0.023f * MathF.Pow(Re, 0.8f) * MathF.Pow(Pr, 0.4f);
        var baseHTC = MathF.Min(2000f, Nu * kf / D_in);

        // z-taper
        var totalDepth = (float)(options.BoreholeDataset?.TotalDepth ?? 0.0);
        var nz = Math.Max(2, options.VerticalGridPoints);
        var spanZ = (float)(totalDepth + 2.0 * options.DomainExtension);
        var dzMean = spanZ / (nz - 1);
        var endTaperMeters = MathF.Max(2f * dzMean, 0.25f);

        var heParams = new float[16];
        heParams[0] = (float)(options.PipeOuterDiameter * 0.5); // pipeRadius
        heParams[1] = totalDepth; // totalBoreDepth
        heParams[2] = (float)options.FluidInletTemperature;
        heParams[3] = (float)options.FluidMassFlowRate;
        heParams[4] = (float)options.FluidSpecificHeat;
        heParams[5] = baseHTC;
        heParams[6] = options.HeatExchangerType == HeatExchangerType.Coaxial ? 0f : 1f;
        heParams[7] = _nzHE;
        heParams[8] = (float)options.FluidViscosity;
        heParams[9] = (float)options.FluidThermalConductivity;
        heParams[10] = (float)options.PipeInnerDiameter;
        heParams[11] = options.HeatExchangerDepth;
        heParams[12] = endTaperMeters;

        fixed (float* p = heParams)
        {
            _cl.EnqueueWriteBuffer(_queue, _heatExchangerParamsBuffer, true, 0,
                (nuint)(heParams.Length * sizeof(float)), p, 0, null, null);
        }

        // Initialize fluid profiles (inlet)
        var init = new float[_nzHE];
        for (var i = 0; i < _nzHE; i++) init[i] = (float)options.FluidInletTemperature;

        fixed (float* t = init)
        {
            _cl.EnqueueWriteBuffer(_queue, _fluidTempDownBuffer, true, 0,
                (nuint)(_nzHE * sizeof(float)), t, 0, null, null);
            _cl.EnqueueWriteBuffer(_queue, _fluidTempUpBuffer, true, 0,
                (nuint)(_nzHE * sizeof(float)), t, 0, null, null);
        }
    }

    /// <summary>
    ///     Uploads seasonal energy curve to GPU for BTES simulation.
    /// </summary>
    private unsafe void UploadSeasonalEnergyCurve(GeothermalSimulationOptions options)
    {
        if (!options.EnableBTESMode || options.SeasonalEnergyCurve.Count == 0)
        {
            // Upload zeros if BTES not enabled
            var zeros = new float[_seasonalCurveDays];
            fixed (float* zeroPtr = zeros)
            {
                _cl.EnqueueWriteBuffer(_queue, _seasonalEnergyBuffer, true, 0,
                    (nuint)(_seasonalCurveDays * sizeof(float)), zeroPtr, 0, null, null);
            }
            return;
        }

        // Ensure curve has exactly 365 days
        var curve = new float[_seasonalCurveDays];
        for (var i = 0; i < _seasonalCurveDays; i++)
        {
            if (i < options.SeasonalEnergyCurve.Count)
                curve[i] = (float)options.SeasonalEnergyCurve[i];
            else
                curve[i] = 0f;
        }

        fixed (float* curvePtr = curve)
        {
            _cl.EnqueueWriteBuffer(_queue, _seasonalEnergyBuffer, true, 0,
                (nuint)(_seasonalCurveDays * sizeof(float)), curvePtr, 0, null, null);
        }

        Logger.Log($"Uploaded BTES seasonal energy curve ({_seasonalCurveDays} days) to GPU");
    }

    /// <summary>
    ///     Solves BTES heat transfer on GPU for one time step with seasonal energy curve application.
    /// </summary>
    public unsafe float SolveBTESHeatTransferGPU(
        float[,,] temperature,
        float[,,,] velocity,
        float[,,] dispersion,
        float dt,
        double currentTime,
        bool simulateGroundwater,
        float[] fluidTempDown,
        float[] fluidTempUp,
        FlowConfiguration flowConfig)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("BTES OpenCL solver not initialized");

        var totalSize = _nr * _nth * _nz;

        // Upload current state
        var tempFlat = FlattenArray(temperature);
        fixed (float* tempPtr = tempFlat)
        {
            _cl.EnqueueWriteBuffer(_queue, _temperatureBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), tempPtr, 0, null, null);
        }

        if (simulateGroundwater && velocity is not null)
        {
            var velocityFlat = FlattenVelocityArray(velocity);
            fixed (float* velPtr = velocityFlat)
            {
                _cl.EnqueueWriteBuffer(_queue, _velocityBuffer, true, 0,
                    (nuint)(totalSize * 3 * sizeof(float)), velPtr, 0, null, null);
            }
        }

        if (dispersion is not null)
        {
            var dispersionFlat = FlattenArray(dispersion);
            fixed (float* dispPtr = dispersionFlat)
            {
                _cl.EnqueueWriteBuffer(_queue, _dispersionBuffer, true, 0,
                    (nuint)(totalSize * sizeof(float)), dispPtr, 0, null, null);
            }
        }

        if (fluidTempDown is not null && fluidTempDown.Length > 0)
            fixed (float* downPtr = fluidTempDown)
            {
                _cl.EnqueueWriteBuffer(_queue, _fluidTempDownBuffer, true, 0,
                    (nuint)(Math.Min(_nzHE, fluidTempDown.Length) * sizeof(float)), downPtr, 0, null, null);
            }

        if (fluidTempUp is not null && fluidTempUp.Length > 0)
            fixed (float* upPtr = fluidTempUp)
            {
                _cl.EnqueueWriteBuffer(_queue, _fluidTempUpBuffer, true, 0,
                    (nuint)(Math.Min(_nzHE, fluidTempUp.Length) * sizeof(float)), upPtr, 0, null, null);
            }

        // Set kernel arguments (includes currentTime for seasonal curve)
        uint argIdx = 0;
        var temperatureBuffer = _temperatureBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &temperatureBuffer);
        var newTempBuffer = _newTempBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &newTempBuffer);
        var conductivityBuffer = _conductivityBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &conductivityBuffer);
        var densityBuffer = _densityBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &densityBuffer);
        var specificHeatBuffer = _specificHeatBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &specificHeatBuffer);
        var velocityBuffer = _velocityBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &velocityBuffer);
        var dispersionBuffer = _dispersionBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &dispersionBuffer);
        var rCoordBuffer = _rCoordBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &rCoordBuffer);
        var zCoordBuffer = _zCoordBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &zCoordBuffer);
        var cellVolumesBuffer = _cellVolumesBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &cellVolumesBuffer);
        var heParamsBuffer = _heatExchangerParamsBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &heParamsBuffer);
        var fluidDownBuffer = _fluidTempDownBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &fluidDownBuffer);
        var fluidUpBuffer = _fluidTempUpBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &fluidUpBuffer);
        var seasonalEnergyBuffer = _seasonalEnergyBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &seasonalEnergyBuffer);
        var temperatureChangeBuffer = _temperatureChangeBuffer;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, (nuint)sizeof(nint), &temperatureChangeBuffer);

        var dt_f = dt;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, sizeof(float), &dt_f);
        var currentTime_f = (float)currentTime;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, sizeof(float), &currentTime_f);
        var nr_i = _nr;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, sizeof(int), &nr_i);
        var nth_i = _nth;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, sizeof(int), &nth_i);
        var nz_i = _nz;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, sizeof(int), &nz_i);
        var gw_i = simulateGroundwater ? 1 : 0;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, sizeof(int), &gw_i);
        var flow_i = (int)flowConfig;
        _cl.SetKernelArg(_heatTransferKernel, argIdx++, sizeof(int), &flow_i);

        // Launch heat transfer kernel
        var globalWorkSize = (nuint)totalSize;
        _cl.EnqueueNdrangeKernel(_queue, _heatTransferKernel, 1, null, &globalWorkSize, null, 0, null, null);

        // Reduction for max(|ΔT|)
        const int localWorkSize = 256;
        var numGroups = (uint)Math.Ceiling((double)totalSize / localWorkSize);
        var reductionGlobal = (nuint)(numGroups * localWorkSize);

        // Reallocate maxChangeBuffer if needed
        {
            _cl.ReleaseMemObject(_maxChangeBuffer);
            int err;
            _maxChangeBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite,
                numGroups * sizeof(float), null, &err);
            CheckError(err, "realloc maxChangeBuffer");
        }

        var inBuf = _temperatureChangeBuffer;
        _cl.SetKernelArg(_reductionKernel, 0, (nuint)sizeof(nint), &inBuf);
        var outBuf = _maxChangeBuffer;
        _cl.SetKernelArg(_reductionKernel, 1, (nuint)sizeof(nint), &outBuf);
        _cl.SetKernelArg(_reductionKernel, 2, localWorkSize * sizeof(float), null);
        var n_int = totalSize;
        _cl.SetKernelArg(_reductionKernel, 3, sizeof(int), &n_int);

        var lsz = (nuint)localWorkSize;
        _cl.EnqueueNdrangeKernel(_queue, _reductionKernel, 1, null, &reductionGlobal, &lsz, 0, null, null);

        // Read back partial maxima
        var partial = new float[numGroups];
        fixed (float* p = partial)
        {
            _cl.EnqueueReadBuffer(_queue, _maxChangeBuffer, true, 0,
                numGroups * sizeof(float), p, 0, null, null);
        }

        var maxChange = 0f;
        for (var g = 0; g < numGroups; g++)
            if (partial[g] > maxChange)
                maxChange = partial[g];

        // Read back updated temperature
        var finalTempFlat = new float[totalSize];
        fixed (float* newTempPtr = finalTempFlat)
        {
            _cl.EnqueueReadBuffer(_queue, _newTempBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), newTempPtr, 0, null, null);
        }

        UnflattenArray(finalTempFlat, temperature);

        return maxChange;
    }

    /// <summary>
    ///     Flattens 3D temperature array for GPU upload.
    /// </summary>
    private float[] FlattenArray(float[,,] array)
    {
        var result = new float[_nr * _nth * _nz];
        var idx = 0;

        for (var i = 0; i < _nr; i++)
        for (var j = 0; j < _nth; j++)
        for (var k = 0; k < _nz; k++)
            result[idx++] = array[i, j, k];

        return result;
    }

    /// <summary>
    ///     Flattens 4D velocity array for GPU upload.
    /// </summary>
    private float[] FlattenVelocityArray(float[,,,] array)
    {
        var result = new float[_nr * _nth * _nz * 3];
        var idx = 0;

        for (var i = 0; i < _nr; i++)
        for (var j = 0; j < _nth; j++)
        for (var k = 0; k < _nz; k++)
        {
            result[idx++] = array[i, j, k, 0]; // vr
            result[idx++] = array[i, j, k, 1]; // vtheta
            result[idx++] = array[i, j, k, 2]; // vz
        }

        return result;
    }

    /// <summary>
    ///     Unflattens 1D array back to 3D.
    /// </summary>
    private void UnflattenArray(float[] flat, float[,,] array)
    {
        var idx = 0;

        for (var i = 0; i < _nr; i++)
        for (var j = 0; j < _nth; j++)
        for (var k = 0; k < _nz; k++)
            array[i, j, k] = flat[idx++];
    }

    /// <summary>
    ///     Gets the OpenCL kernel source code for BTES simulation with seasonal energy curves.
    /// </summary>
    private string GetKernelSource()
    {
        return @"
// ======================================================================
// OpenCL 1.2 — BTES Heat transfer (cylindrical grid) with seasonal energy curves
// Conduction (FV) + optional advection + HE coupling + seasonal charging/discharging
// heParams: [0]=pipeRadius,[1]=totalBoreDepth,[2]=Tin,[3]=mdot,[4]=cp_f,
//           [5]=baseHTC,[6]=heType(1=U),[7]=nzHE,[8]=mu,[9]=kf,[10]=D_in,
//           [11]=activeHeDepth,[12]=endTaperMeters
// seasonalEnergy: 365-day array of daily energy (kWh/day), positive=charging, negative=discharging
// currentTime: simulation time in seconds
// ======================================================================
#define M_PI_F 3.14159265358979323846f
#define FLOW_STANDARD 0
#define FLOW_REVERSED 1

inline int IDX(int i,int j,int k,int nth,int nz){ return i*nth*nz + j*nz + k; }
inline float sstep(float x){ return x*x*(3.0f - 2.0f*x); }
inline float clampf(float x,float a,float b){ return fmin(fmax(x,a),b); }

__kernel void btes_heat_transfer_kernel(
    __global const float* temperature,
    __global float*       newTemp,
    __global const float* conductivity,
    __global const float* density,
    __global const float* specificHeat,
    __global const float* velocity,
    __global const float* dispersion,     // unused (ABI)
    __global const float* r_coord,
    __global const float* z_coord,
    __global const float* cellVolumes,
    __global const float* heParams,
    __global const float* fluidTempDown,
    __global const float* fluidTempUp,
    __global const float* seasonalEnergy,  // NEW: 365-day energy curve
    __global float*       temperatureChange,
    const float dt,
    const float currentTime,                // NEW: current simulation time (seconds)
    const int nr, const int nth, const int nz,
    const int simulateGroundwater,
    const int flow_config)
{
    const int gid = get_global_id(0);
    const int N = nr*nth*nz;
    if (gid >= N) return;

    const int k = gid % nz;
    const int j = (gid / nz) % nth;
    const int i = gid / (nz * nth);

    // Boundary conditions
    if (i==0 || i==nr-1 || k==0 || k==nz-1){
        newTemp[gid] = temperature[gid];
        temperatureChange[gid] = 0.0f;
        return;
    }

    // Material properties
    const float T_old = temperature[gid];
    const float lam   = clampf(conductivity[gid], 0.05f, 15.0f);
    const float rho   = clampf(density[gid],     500.0f, 5000.0f);
    const float cp    = clampf(specificHeat[gid],100.0f, 5000.0f);
    const float rho_cp = fmax(1.0f, rho*cp);
    const float alpha  = lam / rho_cp;

    // Geometry
    const float r   = fmax(0.01f, r_coord[i]);
    const int   jm  = (j - 1 + nth) % nth;
    const int   jp  = (j + 1) % nth;
    const float drm = fmax(0.001f, r_coord[i]   - r_coord[i-1]);
    const float drp = fmax(0.001f, r_coord[i+1] - r_coord[i]);
    const float dth = 2.0f * M_PI_F / nth;
    const float dzm = fmax(0.001f, fabs(z_coord[k]   - z_coord[k-1]));
    const float dzp = fmax(0.001f, fabs(z_coord[k+1] - z_coord[k]));

    // Neighbor temperatures
    const float T_rm  = temperature[IDX(i-1,j ,k ,nth,nz)];
    const float T_rp  = temperature[IDX(i+1,j ,k ,nth,nz)];
    const float T_zm  = temperature[IDX(i  ,j ,k-1,nth,nz)];
    const float T_zp  = temperature[IDX(i  ,j ,k+1,nth,nz)];
    const float T_thm = temperature[IDX(i  ,jm,k ,nth,nz)];
    const float T_thp = temperature[IDX(i  ,jp,k ,nth,nz)];

    // Conduction + 1/r * dT/dr
    float neighbor =
          (T_rp + T_rm) / (drm*drp)
        + (T_thp + T_thm) / (r*r*dth*dth)
        + (T_zp + T_zm) / (dzm*dzp)
        + (T_rp - T_rm) / (drp + drm) / r;

    // Advection (optional groundwater)
    float adv = 0.0f;
    if (simulateGroundwater){
        const int vbase = gid*3;
        const float vr  = velocity[vbase+0];
        const float vth = velocity[vbase+1];
        const float vz  = velocity[vbase+2];
        const float dTdr  = (vr>=0.0f) ? (T_old - T_rm)/drm : (T_rp - T_old)/drp;
        const float dTdth = (T_thp - T_thm)/(2.0f*r*dth);
        const float dTdz  = (vz>=0.0f) ? (T_old - T_zm)/dzm : (T_zp - T_old)/dzp;
        adv = -(vr*dTdr + vth*dTdth + vz*dTdz);
    }

    float num = T_old + dt*(alpha*neighbor + adv);
    float den = 1.0f + dt*alpha*(2.0f/(drm*drp) + 2.0f/(r*r*dth*dth) + 2.0f/(dzm*dzp));

    // -------- BTES: Apply seasonal energy curve --------
    // Calculate day of year from currentTime
    const float secondsPerDay = 86400.0f;
    const float daysPerYear = 365.0f;
    const float dayOfYear_f = fmod(currentTime / secondsPerDay, daysPerYear);
    const int dayOfYear = (int)dayOfYear_f;
    const int dayIndex = clamp(dayOfYear, 0, 364);

    const float dailyEnergy_kWh = seasonalEnergy[dayIndex]; // kWh/day

    // Convert daily energy to inlet temperature adjustment
    // Power (W) = dailyEnergy (kWh/day) * 1000 / 24 hours
    const float mdot = heParams[3];
    const float cp_f = heParams[4];
    const float powerWatts = dailyEnergy_kWh * 1000.0f / 24.0f;
    const float deltaT = (fabs(mdot) > 1e-6f) ? (powerWatts / (mdot * cp_f)) : 0.0f;

    // Adjust fluid inlet temperature based on energy demand
    float T_inlet_adjusted = heParams[2]; // Base inlet temperature
    if (dailyEnergy_kWh > 0.0f) {
        // Charging: increase inlet temperature
        T_inlet_adjusted += deltaT;
    } else if (dailyEnergy_kWh < 0.0f) {
        // Discharging: decrease inlet temperature
        T_inlet_adjusted -= fabs(deltaT);
    }
    T_inlet_adjusted = clampf(T_inlet_adjusted, 273.15f, 373.15f);

    // -------- HE coupling with TAPER --------
    const float pipeRadius     = heParams[0];
    const float totalBoreDepth = heParams[1];
    const float baseHTC        = heParams[5];
    const float heType         = heParams[6];
    const int   nzHE           = (int)heParams[7];
    const float activeHeDepth  = heParams[11];
    const float zTaperMeters   = fmax(heParams[12], 1e-3f);

    const float rInfluence = fmax(pipeRadius*5.0f, 0.25f);
    const float depth      = fmax(0.0f, -z_coord[k]);

    float rTaper = 0.0f;
    if (r <= rInfluence){
        const float u = clampf(1.0f - r / rInfluence, 0.0f, 1.0f);
        rTaper = sstep(u);
    }

    float depthFactor = 0.0f;
    if      (depth <= activeHeDepth)                depthFactor = 1.0f;
    else if (depth <= activeHeDepth + zTaperMeters) depthFactor = sstep(1.0f - (depth - activeHeDepth)/zTaperMeters);
    else                                            depthFactor = 0.0f;

    const float taper = rTaper * depthFactor;

    if (taper > 0.0f){
        const int h = clamp((int)(depth / totalBoreDepth * nzHE), 0, nzHE - 1);
        float Tfluid = (flow_config == FLOW_REVERSED)
            ? ((heType>0.5f) ? 0.5f*(fluidTempDown[h] + fluidTempUp[h]) : fluidTempDown[h])
            : ((heType>0.5f) ? 0.5f*(fluidTempDown[h] + fluidTempUp[h]) : fluidTempUp[h]);

        // Apply seasonal adjustment to fluid temperature
        Tfluid = T_inlet_adjusted;

        const float vol  = fmax(1e-6f, cellVolumes[gid]);
        const float area = 2.0f * M_PI_F * r * (dzm + dzp) * 0.5f;
        float Uvol       = (baseHTC * area / vol) * taper;

        num += dt * Uvol * Tfluid / rho_cp;
        den += dt * Uvol / rho_cp;
    }

    const float T_new = clampf(num/den, 273.0f, 573.0f);
    newTemp[gid] = T_new;
    temperatureChange[gid] = fabs(T_new - T_old);
}

// -------------------- REDUCTION (max) --------------------
__kernel void reduction_max_kernel(__global const float* input, __global float* output, __local float* scratch, const int n)
{
    const int tid = get_local_id(0);
    const int gid = get_group_id(0);
    const int lsz = get_local_size(0);

    int i = (get_global_id(0)) * 2;
    float v = 0.0f;
    if (i < n)     v = fmax(v, fabs(input[i]));
    if (i + 1 < n) v = fmax(v, fabs(input[i+1]));
    scratch[tid] = v;
    barrier(CLK_LOCAL_MEM_FENCE);

    for (int s = lsz>>1; s>0; s>>=1){
        if (tid < s) scratch[tid] = fmax(scratch[tid], scratch[tid+s]);
        barrier(CLK_LOCAL_MEM_FENCE);
    }
    if (tid==0) output[gid] = scratch[0];
}
";
    }

    /// <summary>
    ///     Updates heat exchanger parameters on GPU during simulation.
    /// </summary>
    public void UpdateHeatExchangerParameters(GeothermalSimulationOptions options)
    {
        if (!_isInitialized) return;

        UploadHeatExchangerParams(options);
    }

    /// <summary>
    ///     Updates seasonal energy curve on GPU during simulation.
    /// </summary>
    public void UpdateSeasonalEnergyCurve(GeothermalSimulationOptions options)
    {
        if (!_isInitialized) return;

        UploadSeasonalEnergyCurve(options);
    }

    private void CheckError(int errCode, string operation)
    {
        if (errCode != 0)
            throw new InvalidOperationException($"BTES OpenCL error during {operation}: {errCode}");
    }
}
