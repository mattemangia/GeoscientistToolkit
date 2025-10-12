// GeoscientistToolkit/Business/Thermodynamics/ThermodynamicsOpenCL.cs
//
// GPU-accelerated thermodynamic calculations using OpenCL via Silk.NET.
// Offloads computationally intensive operations to GPU for massive parallelism.
//
// REFERENCES:
// - Khronos OpenCL 3.0 Specification
// - Munshi, A., Gaster, B., Mattson, T.G., Fung, J. & Ginsburg, D., 2011. 
//   OpenCL Programming Guide. Addison-Wesley Professional.
// - Kirk, D.B. & Hwu, W.W., 2016. Programming Massively Parallel Processors: 
//   A Hands-on Approach, 3rd ed. Morgan Kaufmann.
//

using System.Runtime.InteropServices;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Business.Thermodynamics;

/// <summary>
///     OpenCL-accelerated thermodynamic calculations for large-scale systems.
///     Ideal for CT scan voxel-based dissolution/precipitation simulations.
/// </summary>
public class ThermodynamicsOpenCL : IDisposable
{
    private readonly CL _cl;
    private nint _arrheniusKernel;
    private nint _commandQueue;
    private nint _context;

    // Compiled kernels
    private nint _debyeHuckelKernel;
    private nint _device;
    private bool _disposed;
    private nint _dissolutionRateKernel;

    private bool _initialized;
    private nint _program;
    private nint _saturationStateKernel;

    public ThermodynamicsOpenCL()
    {
        _cl = CL.GetApi();
        InitializeOpenCL();
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_initialized)
        {
            _cl.ReleaseKernel(_debyeHuckelKernel);
            _cl.ReleaseKernel(_arrheniusKernel);
            _cl.ReleaseKernel(_dissolutionRateKernel);
            _cl.ReleaseKernel(_saturationStateKernel);
            _cl.ReleaseProgram(_program);
            _cl.ReleaseCommandQueue(_commandQueue);
            _cl.ReleaseContext(_context);
        }

        _cl.Dispose();
        _disposed = true;

        Logger.Log("[ThermodynamicsOpenCL] Disposed");
    }

    private unsafe void InitializeOpenCL()
    {
        try
        {
            // Get platform
            uint numPlatforms = 0;
            _cl.GetPlatformIDs(0, null, &numPlatforms);

            if (numPlatforms == 0)
            {
                Logger.LogWarning("[ThermodynamicsOpenCL] No OpenCL platforms found");
                return;
            }

            var platforms = stackalloc nint[(int)numPlatforms];
            _cl.GetPlatformIDs(numPlatforms, platforms, null);
            var platform = platforms[0];

            // Get device (prefer GPU)
            uint numDevices = 0;
            _cl.GetDeviceIDs(platform, DeviceType.Gpu, 0, null, &numDevices);

            if (numDevices == 0)
            {
                // Fall back to CPU
                _cl.GetDeviceIDs(platform, DeviceType.Cpu, 0, null, &numDevices);
                if (numDevices == 0)
                {
                    Logger.LogWarning("[ThermodynamicsOpenCL] No OpenCL devices found");
                    return;
                }
            }

            var devices = stackalloc nint[(int)numDevices];
            _cl.GetDeviceIDs(platform, DeviceType.Gpu, numDevices, devices, null);
            _device = devices[0];

            // Log device info
            LogDeviceInfo(_device);

            // Create context
            int errorCode;
            _context = _cl.CreateContext(null, 1, devices, null, null, &errorCode);
            CheckError(errorCode, "CreateContext");

            // Create command queue
            _commandQueue = _cl.CreateCommandQueue(_context, _device, (CommandQueueProperties)0, &errorCode);
            CheckError(errorCode, "CreateCommandQueue");

            // Create and build program
            BuildKernels();

            _initialized = true;
            Logger.Log("[ThermodynamicsOpenCL] Initialized successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[ThermodynamicsOpenCL] Initialization failed: {ex.Message}");
            _initialized = false;
        }
    }

    private unsafe void LogDeviceInfo(nint device)
    {
        // Get device name
        nuint size = 0;
        _cl.GetDeviceInfo(device, (uint)DeviceInfo.Name, 0, null, &size);
        var nameBytes = stackalloc byte[(int)size];
        _cl.GetDeviceInfo(device, (uint)DeviceInfo.Name, size, nameBytes, null);
        var name = Marshal.PtrToStringAnsi((nint)nameBytes);

        // Get compute units
        uint computeUnits = 0;
        _cl.GetDeviceInfo(device, (uint)DeviceInfo.MaxComputeUnits, sizeof(uint),
            &computeUnits, null);

        Logger.Log($"[ThermodynamicsOpenCL] Device: {name}, Compute Units: {computeUnits}");
    }

    private unsafe void BuildKernels()
    {
        var kernelSource = GetKernelSource();

        int errorCode;
        var sources = new[] { kernelSource };
        var sourceLength = (nuint)kernelSource.Length;

        _program = _cl.CreateProgramWithSource(_context, 1, sources, &sourceLength, &errorCode);
        CheckError(errorCode, "CreateProgramWithSource");

        // Build program
        var device = _device; // Use local variable for address-of
        errorCode = _cl.BuildProgram(_program, 1, &device, (string)null, null, null);
        if (errorCode != 0)
        {
            // Get build log
            nuint logSize = 0;
            _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);
            var log = stackalloc byte[(int)logSize];
            _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.BuildLog, logSize, log, null);
            var logStr = Marshal.PtrToStringAnsi((nint)log);
            Logger.LogError($"[ThermodynamicsOpenCL] Build error:\n{logStr}");
            CheckError(errorCode, "BuildProgram");
        }

        // Create kernels
        _debyeHuckelKernel = _cl.CreateKernel(_program, "calculate_debye_huckel", &errorCode);
        CheckError(errorCode, "CreateKernel(debye_huckel)");

        _arrheniusKernel = _cl.CreateKernel(_program, "calculate_arrhenius", &errorCode);
        CheckError(errorCode, "CreateKernel(arrhenius)");

        _dissolutionRateKernel = _cl.CreateKernel(_program, "calculate_dissolution_rates", &errorCode);
        CheckError(errorCode, "CreateKernel(dissolution_rates)");

        _saturationStateKernel = _cl.CreateKernel(_program, "calculate_saturation_states", &errorCode);
        CheckError(errorCode, "CreateKernel(saturation_states)");
    }

    /// <summary>
    ///     OpenCL kernel source code for thermodynamic calculations.
    ///     Written in OpenCL C (C99 subset).
    /// </summary>
    private string GetKernelSource()
    {
        return @"
/*
 * OpenCL kernels for thermodynamic calculations
 * Implements algorithms from cited geochemical literature
 */

// Extended Debye-Hückel activity coefficients
// Source: Truesdell & Jones, 1974. WATEQ model
__kernel void calculate_debye_huckel(
    __global const double* charges,      // Ion charges
    __global const double* ion_sizes,    // Ion size parameters (Angstrom)
    __global double* log_gammas,         // Output: log10(gamma)
    const double A,                      // DH A parameter
    const double B,                      // DH B parameter  
    const double sqrt_I)                 // sqrt(ionic strength)
{
    int i = get_global_id(0);
    
    double z = charges[i];
    double a = ion_sizes[i];
    double z2 = z * z;
    
    // log10(gamma) = -A*z²*sqrt(I) / (1 + B*a*sqrt(I))
    double denominator = 1.0 + B * a * sqrt_I;
    log_gammas[i] = -A * z2 * sqrt_I / denominator;
}

// Arrhenius rate constants
// k(T) = k0 * exp(-Ea/(R*T))
__kernel void calculate_arrhenius(
    __global const double* k0_values,    // Pre-exponential factors
    __global const double* Ea_values,    // Activation energies (kJ/mol)
    __global double* rate_constants,     // Output: k(T)
    const double inv_RT)                 // 1/(R*T) with proper units
{
    int i = get_global_id(0);
    
    double k0 = k0_values[i];
    double Ea = Ea_values[i];
    
    // k = k0 * exp(-Ea/(R*T))
    rate_constants[i] = k0 * exp(-Ea * inv_RT);
}

// Mineral dissolution rates
// Source: Palandri & Kharaka, 2004
// r = k * A * (1 - Omega^n)
__kernel void calculate_dissolution_rates(
    __global const double* rate_constants,   // k values (mol/m²/s)
    __global const double* surface_areas,    // A values (m²)
    __global const double* saturation_states,// Omega values
    __global const double* reaction_orders,  // n values
    __global double* rates)                  // Output: rates (mol/s)
{
    int i = get_global_id(0);
    
    double k = rate_constants[i];
    double A = surface_areas[i];
    double omega = saturation_states[i];
    double n = reaction_orders[i];
    
    // Calculate (1 - Omega^n)
    double omega_n = pow(fmax(omega, 1e-30), n);
    double factor = 1.0 - omega_n;
    
    // Rate: r = k * A * (1 - Omega^n)
    // Only positive for undersaturation (Omega < 1)
    rates[i] = fmax(0.0, k * A * factor);
}

// Saturation state calculation
// Omega = IAP / K = exp(sum(nu_i * ln(a_i))) / K
__kernel void calculate_saturation_states(
    __global const double* activities,       // Species activities
    __global const int* stoichiometry,       // Stoichiometric coefficients
    __global const double* log_K_values,     // log10(K) equilibrium constants
    __global double* saturation_states,      // Output: Omega values
    const int num_species,
    const int num_reactions)
{
    int rxn = get_global_id(0);
    
    if (rxn >= num_reactions) return;
    
    // Calculate log(IAP) = sum(nu_i * log(a_i))
    double log_IAP = 0.0;
    for (int i = 0; i < num_species; i++) {
        int stoich = stoichiometry[rxn * num_species + i];
        if (stoich != 0) {
            double activity = fmax(activities[i], 1e-30);
            log_IAP += stoich * log10(activity);
        }
    }
    
    // Omega = 10^(log_IAP - log_K)
    double log_K = log_K_values[rxn];
    double log_omega = log_IAP - log_K;
    saturation_states[rxn] = pow(10.0, log_omega);
}

// 3D voxel-based dissolution for CT scan data
// Each voxel represents a mineral phase that can dissolve
__kernel void ct_voxel_dissolution(
    __global double* voxel_moles,           // Mineral moles in each voxel
    __global const double* rate_constants,   // k for each mineral type
    __global const double* saturation_states,// Omega in fluid phase
    __global const uchar* mineral_types,     // Mineral ID for each voxel
    __global const double* voxel_volumes,    // Voxel volumes (m³)
    const double dt,                         // Time step (s)
    const double specific_surface_area,     // m²/g
    const double molar_mass)                 // g/mol
{
    int idx = get_global_id(0);
    int idy = get_global_id(1);
    int idz = get_global_id(2);
    
    int nx = get_global_size(0);
    int ny = get_global_size(1);
    int i = idx + idy * nx + idz * nx * ny;
    
    double moles = voxel_moles[i];
    if (moles < 1e-12) return; // Skip empty voxels
    
    uchar mineral_type = mineral_types[i];
    double k = rate_constants[mineral_type];
    double omega = saturation_states[mineral_type];
    double V = voxel_volumes[i];
    
    // Surface area from moles
    double mass_g = moles * molar_mass;
    double A = mass_g * specific_surface_area;
    
    // Dissolution rate
    double factor = 1.0 - pow(fmax(omega, 1e-30), 1.0);
    double rate = fmax(0.0, k * A * factor);
    
    // Update moles: n(t+dt) = n(t) - rate * dt
    double delta_moles = rate * dt;
    voxel_moles[i] = fmax(0.0, moles - delta_moles);
}
";
    }

    /// <summary>
    ///     Calculate Debye-Hückel activity coefficients on GPU.
    /// </summary>
    public unsafe double[] CalculateDebyeHuckelGPU(double[] charges, double[] ionSizes,
        double A, double B, double sqrtI)
    {
        if (!_initialized)
            throw new InvalidOperationException("OpenCL not initialized");

        var count = charges.Length;
        var results = new double[count];

        int errorCode;

        // Create buffers
        fixed (double* pCharges = charges, pIonSizes = ionSizes, pResults = results)
        {
            var chargesBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(count * sizeof(double)), pCharges, &errorCode);
            CheckError(errorCode, "CreateBuffer(charges)");

            var ionSizesBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(count * sizeof(double)), pIonSizes, &errorCode);
            CheckError(errorCode, "CreateBuffer(ionSizes)");

            var resultsBuffer = _cl.CreateBuffer(_context, MemFlags.WriteOnly,
                (nuint)(count * sizeof(double)), null, &errorCode);
            CheckError(errorCode, "CreateBuffer(results)");

            // Set kernel arguments
            _cl.SetKernelArg(_debyeHuckelKernel, 0, (nuint)sizeof(nint), &chargesBuffer);
            _cl.SetKernelArg(_debyeHuckelKernel, 1, (nuint)sizeof(nint), &ionSizesBuffer);
            _cl.SetKernelArg(_debyeHuckelKernel, 2, (nuint)sizeof(nint), &resultsBuffer);
            _cl.SetKernelArg(_debyeHuckelKernel, 3, sizeof(double), &A);
            _cl.SetKernelArg(_debyeHuckelKernel, 4, sizeof(double), &B);
            _cl.SetKernelArg(_debyeHuckelKernel, 5, sizeof(double), &sqrtI);

            // Execute kernel
            var globalWorkSize = (nuint)count;
            errorCode = _cl.EnqueueNdrangeKernel(_commandQueue, _debyeHuckelKernel, 1, null,
                &globalWorkSize, null, 0, null, null);
            CheckError(errorCode, "EnqueueNDRangeKernel");

            // Read results
            errorCode = _cl.EnqueueReadBuffer(_commandQueue, resultsBuffer, true, 0,
                (nuint)(count * sizeof(double)), pResults, 0, null, null);
            CheckError(errorCode, "EnqueueReadBuffer");

            // Cleanup
            _cl.ReleaseMemObject(chargesBuffer);
            _cl.ReleaseMemObject(ionSizesBuffer);
            _cl.ReleaseMemObject(resultsBuffer);
        }

        return results;
    }

    /// <summary>
    ///     Calculate dissolution rates for CT scan voxel data on GPU.
    ///     Processes millions of voxels in parallel.
    /// </summary>
    public unsafe void CalculateCTDissolutionGPU(
        double[] voxelMoles, // In/out: mineral moles per voxel
        double[] rateConstants, // Rate constant for each mineral type
        double[] saturationStates, // Saturation state for each mineral type
        byte[] mineralTypes, // Mineral type ID for each voxel
        double[] voxelVolumes, // Volume of each voxel
        double dt, // Time step
        double specificSurfaceArea, // m²/g
        double molarMass, // g/mol
        int nx, int ny, int nz) // Grid dimensions
    {
        if (!_initialized)
            throw new InvalidOperationException("OpenCL not initialized");

        var totalVoxels = nx * ny * nz;
        int errorCode;

        fixed (double* pMoles = voxelMoles, pRates = rateConstants, pOmegas = saturationStates,
               pVolumes = voxelVolumes)
        fixed (byte* pTypes = mineralTypes)
        {
            // Create buffers
            var molesBuffer = _cl.CreateBuffer(_context, MemFlags.ReadWrite | MemFlags.CopyHostPtr,
                (nuint)(totalVoxels * sizeof(double)), pMoles, &errorCode);
            CheckError(errorCode, "CreateBuffer(moles)");

            var ratesBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(rateConstants.Length * sizeof(double)), pRates, &errorCode);
            CheckError(errorCode, "CreateBuffer(rates)");

            var omegasBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(saturationStates.Length * sizeof(double)), pOmegas, &errorCode);
            CheckError(errorCode, "CreateBuffer(omegas)");

            var typesBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)totalVoxels, pTypes, &errorCode);
            CheckError(errorCode, "CreateBuffer(types)");

            var volumesBuffer = _cl.CreateBuffer(_context, MemFlags.ReadOnly | MemFlags.CopyHostPtr,
                (nuint)(totalVoxels * sizeof(double)), pVolumes, &errorCode);
            CheckError(errorCode, "CreateBuffer(volumes)");

            // Get kernel
            var kernel = _cl.CreateKernel(_program, "ct_voxel_dissolution", &errorCode);
            CheckError(errorCode, "CreateKernel(ct_voxel_dissolution)");

            // Set arguments
            _cl.SetKernelArg(kernel, 0, (nuint)sizeof(nint), &molesBuffer);
            _cl.SetKernelArg(kernel, 1, (nuint)sizeof(nint), &ratesBuffer);
            _cl.SetKernelArg(kernel, 2, (nuint)sizeof(nint), &omegasBuffer);
            _cl.SetKernelArg(kernel, 3, (nuint)sizeof(nint), &typesBuffer);
            _cl.SetKernelArg(kernel, 4, (nuint)sizeof(nint), &volumesBuffer);
            _cl.SetKernelArg(kernel, 5, sizeof(double), &dt);
            _cl.SetKernelArg(kernel, 6, sizeof(double), &specificSurfaceArea);
            _cl.SetKernelArg(kernel, 7, sizeof(double), &molarMass);

            // Execute 3D kernel
            var globalWorkSize = stackalloc nuint[3];
            globalWorkSize[0] = (nuint)nx;
            globalWorkSize[1] = (nuint)ny;
            globalWorkSize[2] = (nuint)nz;

            errorCode = _cl.EnqueueNdrangeKernel(_commandQueue, kernel, 3, null,
                globalWorkSize, null, 0, null, null);
            CheckError(errorCode, "EnqueueNDRangeKernel(3D)");

            // Read back results
            errorCode = _cl.EnqueueReadBuffer(_commandQueue, molesBuffer, true, 0,
                (nuint)(totalVoxels * sizeof(double)), pMoles, 0, null, null);
            CheckError(errorCode, "EnqueueReadBuffer");

            // Cleanup
            _cl.ReleaseMemObject(molesBuffer);
            _cl.ReleaseMemObject(ratesBuffer);
            _cl.ReleaseMemObject(omegasBuffer);
            _cl.ReleaseMemObject(typesBuffer);
            _cl.ReleaseMemObject(volumesBuffer);
            _cl.ReleaseKernel(kernel);
        }
    }

    private void CheckError(int errorCode, string operation)
    {
        if (errorCode != 0) throw new Exception($"OpenCL error in {operation}: {errorCode}");
    }
}