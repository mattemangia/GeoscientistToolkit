// GeoscientistToolkit/Analysis/Geothermal/MultiphaseCLSolver.cs

using System;
using System.Runtime.InteropServices;
using GeoscientistToolkit.Data.Mesh3D;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
/// OpenCL 1.2 accelerated multiphase flow solver
/// </summary>
public unsafe class MultiphaseCLSolver : IDisposable
{
    private readonly CL _cl;
    private nint _context;
    private nint _commandQueue;
    private nint _device;
    private nint _program;

    // Kernels
    private nint _kernelUpdatePhaseProperties;
    private nint _kernelUpdateSaturations;

    // Buffers
    private nint _bufferPressure;
    private nint _bufferTemperature;
    private nint _bufferSalinity;
    private nint _bufferSaturationWater;
    private nint _bufferSaturationGas;
    private nint _bufferSaturationCO2;
    private nint _bufferDensityWater;
    private nint _bufferDensityGas;
    private nint _bufferDensityCO2;
    private nint _bufferViscosityWater;
    private nint _bufferViscosityGas;
    private nint _bufferViscosityCO2;
    private nint _bufferBrineDensity;
    private nint _bufferDissolvedCO2;

    private int _nr, _ntheta, _nz;
    private nuint _totalElements;

    public MultiphaseCLSolver()
    {
        _cl = CL.GetApi();
    }

    public bool IsAvailable { get; private set; }
    public string DeviceName { get; private set; }
    public ulong DeviceGlobalMemory { get; private set; }

    public bool InitializeBuffers(GeothermalMesh mesh, MultiphaseOptions options)
    {
        try
        {
            _nr = mesh.Nr;
            _ntheta = mesh.Ntheta;
            _nz = mesh.Nz;
            _totalElements = (nuint)(_nr * _ntheta * _nz);

            // Get OpenCL device from centralized manager
            _device = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetComputeDevice();
            if (_device == 0)
            {
                Logger.LogWarning("No OpenCL device available from OpenCLDeviceManager.");
                return false;
            }

            // Get device info
            var deviceInfo = GeoscientistToolkit.OpenCL.OpenCLDeviceManager.GetDeviceInfo();
            DeviceName = deviceInfo.Name;
            DeviceGlobalMemory = deviceInfo.GlobalMemory;

            Logger.Log($"Multiphase using device: {DeviceName} ({deviceInfo.Vendor})");

            // Create context
            int err;
            _context = _cl.CreateContext(null, 1, &_device, null, null, &err);
            if (err != 0)
            {
                Logger.LogError($"Failed to create OpenCL context: {err}");
                return false;
            }

            // Create command queue (OpenCL 1.2 compatible)
            _commandQueue = _cl.CreateCommandQueue(_context, _device, 0, &err);
            if (err != 0)
            {
                Logger.LogError($"Failed to create command queue: {err}");
                return false;
            }

            // Create buffers
            nuint bufferSize = _totalElements * sizeof(float);

            _bufferPressure = _cl.CreateBuffer(_context, MemFlags.ReadOnly, bufferSize, null, &err);
            _bufferTemperature = _cl.CreateBuffer(_context, MemFlags.ReadOnly, bufferSize, null, &err);
            _bufferSalinity = _cl.CreateBuffer(_context, MemFlags.ReadOnly, bufferSize, null, &err);

            _bufferSaturationWater = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &err);
            _bufferSaturationGas = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &err);
            _bufferSaturationCO2 = _cl.CreateBuffer(_context, MemFlags.ReadWrite, bufferSize, null, &err);

            _bufferDensityWater = _cl.CreateBuffer(_context, MemFlags.WriteOnly, bufferSize, null, &err);
            _bufferDensityGas = _cl.CreateBuffer(_context, MemFlags.WriteOnly, bufferSize, null, &err);
            _bufferDensityCO2 = _cl.CreateBuffer(_context, MemFlags.WriteOnly, bufferSize, null, &err);

            _bufferViscosityWater = _cl.CreateBuffer(_context, MemFlags.WriteOnly, bufferSize, null, &err);
            _bufferViscosityGas = _cl.CreateBuffer(_context, MemFlags.WriteOnly, bufferSize, null, &err);
            _bufferViscosityCO2 = _cl.CreateBuffer(_context, MemFlags.WriteOnly, bufferSize, null, &err);

            _bufferBrineDensity = _cl.CreateBuffer(_context, MemFlags.WriteOnly, bufferSize, null, &err);
            _bufferDissolvedCO2 = _cl.CreateBuffer(_context, MemFlags.WriteOnly, bufferSize, null, &err);

            if (_bufferPressure == 0 || _bufferTemperature == 0)
            {
                Logger.LogError("Failed to create OpenCL buffers");
                return false;
            }

            // Compile kernels
            if (!CompileKernels())
            {
                return false;
            }

            IsAvailable = true;
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError($"MultiphaseCLSolver initialization failed: {ex.Message}");
            return false;
        }
    }

    private bool CompileKernels()
    {
        string kernelSource = GetKernelSource();

        int err;
        nuint sourceLength = (nuint)kernelSource.Length;
        byte[] sourceBytes = System.Text.Encoding.UTF8.GetBytes(kernelSource);

        fixed (byte* sourcePtr = sourceBytes)
        {
            byte** sourcePtrPtr = &sourcePtr;
            _program = _cl.CreateProgramWithSource(_context, 1, sourcePtrPtr, &sourceLength, &err);
            if (err != 0)
            {
                Logger.LogError($"Failed to create program: {err}");
                return false;
            }
        }

        err = _cl.BuildProgram(_program, 1, &_device, null, null, null);
        if (err != 0)
        {
            Logger.LogError($"Failed to build program: {err}");
            // Get build log
            nuint logSize;
            _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.Log, 0, null, &logSize);
            byte* log = stackalloc byte[(int)logSize];
            _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.Log, logSize, log, null);
            string logStr = Marshal.PtrToStringAnsi((nint)log);
            Logger.LogError($"Build log: {logStr}");
            return false;
        }

        // Create kernels
        _kernelUpdatePhaseProperties = _cl.CreateKernel(_program, "update_phase_properties", &err);
        if (err != 0)
        {
            Logger.LogError($"Failed to create kernel update_phase_properties: {err}");
            return false;
        }

        _kernelUpdateSaturations = _cl.CreateKernel(_program, "update_saturations", &err);
        if (err != 0)
        {
            Logger.LogError($"Failed to create kernel update_saturations: {err}");
            return false;
        }

        return true;
    }

    public void UpdatePhaseProperties(
        float[,,] pressure, float[,,] temperature, float[,,] salinity,
        float[,,] densityWater, float[,,] densityGas, float[,,] densityCO2,
        float[,,] viscosityWater, float[,,] viscosityGas, float[,,] viscosityCO2,
        float[,,] brineDensity, float[,,] dissolvedCO2)
    {
        // Upload input data
        nuint bufferSize = _totalElements * sizeof(float);
        int err;

        fixed (float* pPtr = pressure, tPtr = temperature, sPtr = salinity)
        {
            err = _cl.EnqueueWriteBuffer(_commandQueue, _bufferPressure, true, 0, bufferSize, pPtr, 0, null, null);
            err |= _cl.EnqueueWriteBuffer(_commandQueue, _bufferTemperature, true, 0, bufferSize, tPtr, 0, null, null);
            err |= _cl.EnqueueWriteBuffer(_commandQueue, _bufferSalinity, true, 0, bufferSize, sPtr, 0, null, null);

            if (err != 0)
            {
                Logger.LogError($"Failed to upload data to GPU: {err}");
                return;
            }
        }

        // Set kernel arguments
        err = _cl.SetKernelArg(_kernelUpdatePhaseProperties, 0, (nuint)sizeof(nint), &_bufferPressure);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 1, (nuint)sizeof(nint), &_bufferTemperature);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 2, (nuint)sizeof(nint), &_bufferSalinity);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 3, (nuint)sizeof(nint), &_bufferDensityWater);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 4, (nuint)sizeof(nint), &_bufferDensityGas);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 5, (nuint)sizeof(nint), &_bufferDensityCO2);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 6, (nuint)sizeof(nint), &_bufferViscosityWater);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 7, (nuint)sizeof(nint), &_bufferViscosityGas);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 8, (nuint)sizeof(nint), &_bufferViscosityCO2);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 9, (nuint)sizeof(nint), &_bufferBrineDensity);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 10, (nuint)sizeof(nint), &_bufferDissolvedCO2);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 11, sizeof(int), &_nr);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 12, sizeof(int), &_ntheta);
        err |= _cl.SetKernelArg(_kernelUpdatePhaseProperties, 13, sizeof(int), &_nz);

        if (err != 0)
        {
            Logger.LogError($"Failed to set kernel arguments: {err}");
            return;
        }

        // Execute kernel
        nuint globalWorkSize = _totalElements;
        err = _cl.EnqueueNDRangeKernel(_commandQueue, _kernelUpdatePhaseProperties, 1, null, &globalWorkSize, null, 0, null, null);
        if (err != 0)
        {
            Logger.LogError($"Failed to execute kernel: {err}");
            return;
        }

        // Download results
        fixed (float* dwPtr = densityWater, dgPtr = densityGas, dcPtr = densityCO2,
               vwPtr = viscosityWater, vgPtr = viscosityGas, vcPtr = viscosityCO2,
               bdPtr = brineDensity, dco2Ptr = dissolvedCO2)
        {
            err = _cl.EnqueueReadBuffer(_commandQueue, _bufferDensityWater, true, 0, bufferSize, dwPtr, 0, null, null);
            err |= _cl.EnqueueReadBuffer(_commandQueue, _bufferDensityGas, true, 0, bufferSize, dgPtr, 0, null, null);
            err |= _cl.EnqueueReadBuffer(_commandQueue, _bufferDensityCO2, true, 0, bufferSize, dcPtr, 0, null, null);
            err |= _cl.EnqueueReadBuffer(_commandQueue, _bufferViscosityWater, true, 0, bufferSize, vwPtr, 0, null, null);
            err |= _cl.EnqueueReadBuffer(_commandQueue, _bufferViscosityGas, true, 0, bufferSize, vgPtr, 0, null, null);
            err |= _cl.EnqueueReadBuffer(_commandQueue, _bufferViscosityCO2, true, 0, bufferSize, vcPtr, 0, null, null);
            err |= _cl.EnqueueReadBuffer(_commandQueue, _bufferBrineDensity, true, 0, bufferSize, bdPtr, 0, null, null);
            err |= _cl.EnqueueReadBuffer(_commandQueue, _bufferDissolvedCO2, true, 0, bufferSize, dco2Ptr, 0, null, null);
        }

        _cl.Finish(_commandQueue);
    }

    public void UpdateSaturations(float[,,] pressure, float[,,] temperature,
        float[,,] saturationWater, float[,,] saturationGas, float[,,] saturationCO2, float dt)
    {
        nuint bufferSize = _totalElements * sizeof(float);
        int err;

        // Upload current saturations
        fixed (float* swPtr = saturationWater, sgPtr = saturationGas, scPtr = saturationCO2)
        {
            err = _cl.EnqueueWriteBuffer(_commandQueue, _bufferSaturationWater, true, 0, bufferSize, swPtr, 0, null, null);
            err |= _cl.EnqueueWriteBuffer(_commandQueue, _bufferSaturationGas, true, 0, bufferSize, sgPtr, 0, null, null);
            err |= _cl.EnqueueWriteBuffer(_commandQueue, _bufferSaturationCO2, true, 0, bufferSize, scPtr, 0, null, null);
        }

        // Set kernel arguments
        err = _cl.SetKernelArg(_kernelUpdateSaturations, 0, (nuint)sizeof(nint), &_bufferSaturationWater);
        err |= _cl.SetKernelArg(_kernelUpdateSaturations, 1, (nuint)sizeof(nint), &_bufferSaturationGas);
        err |= _cl.SetKernelArg(_kernelUpdateSaturations, 2, (nuint)sizeof(nint), &_bufferSaturationCO2);
        err |= _cl.SetKernelArg(_kernelUpdateSaturations, 3, sizeof(float), &dt);
        err |= _cl.SetKernelArg(_kernelUpdateSaturations, 4, sizeof(int), &_nr);
        err |= _cl.SetKernelArg(_kernelUpdateSaturations, 5, sizeof(int), &_ntheta);
        err |= _cl.SetKernelArg(_kernelUpdateSaturations, 6, sizeof(int), &_nz);

        // Execute kernel
        nuint globalWorkSize = _totalElements;
        err = _cl.EnqueueNDRangeKernel(_commandQueue, _kernelUpdateSaturations, 1, null, &globalWorkSize, null, 0, null, null);

        // Download results
        fixed (float* swPtr = saturationWater, sgPtr = saturationGas, scPtr = saturationCO2)
        {
            err = _cl.EnqueueReadBuffer(_commandQueue, _bufferSaturationWater, true, 0, bufferSize, swPtr, 0, null, null);
            err |= _cl.EnqueueReadBuffer(_commandQueue, _bufferSaturationGas, true, 0, bufferSize, sgPtr, 0, null, null);
            err |= _cl.EnqueueReadBuffer(_commandQueue, _bufferSaturationCO2, true, 0, bufferSize, scPtr, 0, null, null);
        }

        _cl.Finish(_commandQueue);
    }

    private string GetKernelSource()
    {
        return @"
// Multiphase Flow OpenCL Kernels (OpenCL 1.2)

// Calculate water density (IAPWS simplified)
float calculate_water_density(float P_Pa, float T_K)
{
    float T_C = T_K - 273.15f;
    float rho0 = 1000.0f - 0.01687f * T_C + 0.0002f * T_C * T_C;
    float P_MPa = P_Pa / 1e6f;
    float rho = rho0 * (1.0f + 5e-10f * P_MPa);
    return clamp(rho, 500.0f, 1200.0f);
}

// Calculate brine density with salinity (Batzle-Wang 1992)
float calculate_brine_density(float P_Pa, float T_K, float salinity)
{
    float T_C = T_K - 273.15f;
    float rho_w = calculate_water_density(P_Pa, T_K);

    float S = salinity * 100.0f;
    float delta_rho = S * (0.668f + 0.44f * S + 1e-6f * S * S * S +
        T_C * (-0.00182f - 0.00012f * T_C - 6.6e-6f * T_C * T_C));

    return rho_w + delta_rho;
}

// Calculate water/brine viscosity
float calculate_water_viscosity(float P_Pa, float T_K, float salinity)
{
    float T_C = T_K - 273.15f;

    // Pure water viscosity (Vogel equation)
    float mu_w = 0.001f * exp(-3.7188f + 578.919f / (T_K - 137.546f));

    // Salinity effect
    float S = salinity * 100.0f;
    float A = 1.0f + S * (0.0816f + S * (-0.0122f + S * 0.000128f));
    float B = 1.0f + T_C * (0.0263f + T_C * (-0.000594f));

    return mu_w * A * B;
}

// Calculate CO2 density (Span-Wagner simplified)
float calculate_co2_density(float P_Pa, float T_K)
{
    float P_MPa = P_Pa / 1e6f;
    float Pc = 7.377f;  // MPa
    float Tc = 304.13f; // K

    float Pr = P_MPa / Pc;
    float Tr = T_K / Tc;

    float Z;
    if (T_K < Tc && P_MPa < Pc)
        Z = 1.0f - 0.5f * Pr / Tr;
    else
        Z = 0.3f + 0.7f * (Tr - 1.0f) / Tr;

    const float R = 188.9f;
    float rho = P_Pa / (Z * R * T_K);

    return clamp(rho, 1.0f, 1200.0f);
}

// Calculate CO2 viscosity
float calculate_co2_viscosity(float P_Pa, float T_K)
{
    float rho = calculate_co2_density(P_Pa, T_K);

    float mu0 = 1.00697f * sqrt(T_K) / (1.0f + 0.625f * exp(-206.0f / T_K)) * 1e-6f;

    float mu_excess = 0.0f;
    if (rho > 100.0f)
        mu_excess = 1.5e-6f * (rho / 467.6f);

    return mu0 + mu_excess;
}

// Calculate CO2 solubility (Duan-Sun 2003)
float calculate_co2_solubility(float P_Pa, float T_K, float salinity)
{
    float P_bar = P_Pa / 1e5f;
    float T_C = T_K - 273.15f;

    float c1 = -1.1730f;
    float c2 = 0.01372f;
    float c3 = -0.00001417f;
    float c4 = 0.0000000003145f;

    float ln_x = c1 + c2 * T_C + c3 * T_C * T_C + c4 * T_C * T_C * T_C;
    ln_x += log(P_bar);

    float S = salinity * 100.0f;
    ln_x -= 0.411f * S / 58.44f;

    float x_CO2 = exp(ln_x);

    float M_CO2 = 44.01f;
    float M_H2O = 18.015f;
    float m_CO2 = x_CO2 * M_CO2 / (x_CO2 * M_CO2 + (1.0f - x_CO2) * M_H2O);

    return clamp(m_CO2, 0.0f, 0.1f);
}

__kernel void update_phase_properties(
    __global const float* pressure,
    __global const float* temperature,
    __global const float* salinity,
    __global float* density_water,
    __global float* density_gas,
    __global float* density_co2,
    __global float* viscosity_water,
    __global float* viscosity_gas,
    __global float* viscosity_co2,
    __global float* brine_density,
    __global float* dissolved_co2,
    int nr, int ntheta, int nz)
{
    int gid = get_global_id(0);
    int total = nr * ntheta * nz;

    if (gid >= total) return;

    float P_Pa = pressure[gid];
    float T_K = temperature[gid] + 273.15f;
    float S = salinity[gid];

    // Water/brine properties
    density_water[gid] = calculate_water_density(P_Pa, T_K);
    brine_density[gid] = calculate_brine_density(P_Pa, T_K, S);
    viscosity_water[gid] = calculate_water_viscosity(P_Pa, T_K, S);

    // CO2 properties
    density_co2[gid] = calculate_co2_density(P_Pa, T_K);
    viscosity_co2[gid] = calculate_co2_viscosity(P_Pa, T_K);

    // Steam properties (simplified)
    const float R_steam = 461.5f;
    density_gas[gid] = clamp(P_Pa / (R_steam * T_K), 0.1f, 100.0f);
    viscosity_gas[gid] = 1.84e-5f * pow(T_K / 373.15f, 0.7f);

    // CO2 solubility
    dissolved_co2[gid] = calculate_co2_solubility(P_Pa, T_K, S);
}

__kernel void update_saturations(
    __global float* saturation_water,
    __global float* saturation_gas,
    __global float* saturation_co2,
    float dt,
    int nr, int ntheta, int nz)
{
    int gid = get_global_id(0);
    int total = nr * ntheta * nz;

    if (gid >= total) return;

    // Normalize saturations to sum to 1.0
    float Sw = saturation_water[gid];
    float Sg = saturation_gas[gid];
    float Sc = saturation_co2[gid];
    float Stotal = Sw + Sg + Sc;

    if (Stotal > 0.001f)
    {
        saturation_water[gid] = Sw / Stotal;
        saturation_gas[gid] = Sg / Stotal;
        saturation_co2[gid] = Sc / Stotal;
    }
    else
    {
        saturation_water[gid] = 1.0f;
        saturation_gas[gid] = 0.0f;
        saturation_co2[gid] = 0.0f;
    }
}
";
    }

    public void Dispose()
    {
        if (_kernelUpdatePhaseProperties != 0) _cl.ReleaseKernel(_kernelUpdatePhaseProperties);
        if (_kernelUpdateSaturations != 0) _cl.ReleaseKernel(_kernelUpdateSaturations);
        if (_program != 0) _cl.ReleaseProgram(_program);

        if (_bufferPressure != 0) _cl.ReleaseMemObject(_bufferPressure);
        if (_bufferTemperature != 0) _cl.ReleaseMemObject(_bufferTemperature);
        if (_bufferSalinity != 0) _cl.ReleaseMemObject(_bufferSalinity);
        if (_bufferSaturationWater != 0) _cl.ReleaseMemObject(_bufferSaturationWater);
        if (_bufferSaturationGas != 0) _cl.ReleaseMemObject(_bufferSaturationGas);
        if (_bufferSaturationCO2 != 0) _cl.ReleaseMemObject(_bufferSaturationCO2);
        if (_bufferDensityWater != 0) _cl.ReleaseMemObject(_bufferDensityWater);
        if (_bufferDensityGas != 0) _cl.ReleaseMemObject(_bufferDensityGas);
        if (_bufferDensityCO2 != 0) _cl.ReleaseMemObject(_bufferDensityCO2);
        if (_bufferViscosityWater != 0) _cl.ReleaseMemObject(_bufferViscosityWater);
        if (_bufferViscosityGas != 0) _cl.ReleaseMemObject(_bufferViscosityGas);
        if (_bufferViscosityCO2 != 0) _cl.ReleaseMemObject(_bufferViscosityCO2);
        if (_bufferBrineDensity != 0) _cl.ReleaseMemObject(_bufferBrineDensity);
        if (_bufferDissolvedCO2 != 0) _cl.ReleaseMemObject(_bufferDissolvedCO2);

        if (_commandQueue != 0) _cl.ReleaseCommandQueue(_commandQueue);
        if (_context != 0) _cl.ReleaseContext(_context);
    }
}
