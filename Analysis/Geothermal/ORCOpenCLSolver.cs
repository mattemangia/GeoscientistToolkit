using System;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.OpenCL;
using GeoscientistToolkit.OpenCL;
using GeoscientistToolkit.Business;

namespace GeoscientistToolkit.Analysis.Geothermal
{
    /// <summary>
    /// GPU-accelerated ORC simulation using OpenCL 1.2 via Silk.NET
    /// Processes large batches of operating conditions in parallel
    /// Uses ORCFluidLibrary for comprehensive working fluid properties
    /// </summary>
    public unsafe class ORCOpenCLSolver : IDisposable
    {
        private readonly CL _cl;
        private nint _context;
        private nint _commandQueue;
        private nint _device;
        private nint _program;
        private nint _kernelORCCycle;
        private nint _kernelReduction;

        private bool _isInitialized = false;
        private ORCConfiguration _config;
        private ORCFluid _currentFluid;

        #region OpenCL Buffers

        private nint _bufferGeoTemp;
        private nint _bufferGeoMassFlow;
        private nint _bufferNetPower;
        private nint _bufferEfficiency;
        private nint _bufferMassFlowRate;
        private nint _bufferTurbineWork;
        private nint _bufferPumpWork;
        private nint _bufferHeatInput;

        #endregion

        #region Initialization

        public ORCOpenCLSolver(ORCConfiguration config)
        {
            _config = config ?? new ORCConfiguration();
            _cl = CL.GetApi();

            // Load fluid from library
            _currentFluid = ORCFluidLibrary.Instance.GetFluidByName(_config.FluidName)
                         ?? ORCFluidLibrary.Instance.GetFluidByName("R245fa"); // Default fallback
        }

        public bool Initialize()
        {
            try
            {
                // Get device from OpenCLDeviceManager
                var deviceManager = OpenCLDeviceManager.Instance;
                _device = deviceManager.GetDevice(_cl);

                if (_device == nint.Zero)
                {
                    Console.WriteLine("Failed to get OpenCL device");
                    return false;
                }

                // Create context
                int errorCode;
                _context = _cl.CreateContext(null, 1, &_device, null, null, &errorCode);
                if (errorCode != (int)ErrorCodes.Success)
                {
                    Console.WriteLine($"Failed to create OpenCL context: {errorCode}");
                    return false;
                }

                // Create command queue (OpenCL 1.2)
                _commandQueue = _cl.CreateCommandQueue(_context, _device, 0, &errorCode);
                if (errorCode != (int)ErrorCodes.Success)
                {
                    Console.WriteLine($"Failed to create command queue: {errorCode}");
                    return false;
                }

                // Build program
                if (!BuildProgram())
                {
                    return false;
                }

                // Create kernels
                _kernelORCCycle = _cl.CreateKernel(_program, "orc_cycle_kernel", &errorCode);
                if (errorCode != (int)ErrorCodes.Success)
                {
                    Console.WriteLine($"Failed to create ORC cycle kernel: {errorCode}");
                    return false;
                }

                _kernelReduction = _cl.CreateKernel(_program, "reduction_sum_kernel", &errorCode);
                if (errorCode != (int)ErrorCodes.Success)
                {
                    Console.WriteLine($"Failed to create reduction kernel: {errorCode}");
                    return false;
                }

                _isInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenCL initialization error: {ex.Message}");
                return false;
            }
        }

        private bool BuildProgram()
        {
            string kernelSource = GetKernelSource();
            byte[] sourceBytes = Encoding.UTF8.GetBytes(kernelSource);

            int errorCode;
            nuint sourceLength = (nuint)sourceBytes.Length;

            fixed (byte* pSource = sourceBytes)
            {
                byte* pSourcePtr = pSource;
                _program = _cl.CreateProgramWithSource(_context, 1, &pSourcePtr, &sourceLength, &errorCode);
            }

            if (errorCode != (int)ErrorCodes.Success)
            {
                Console.WriteLine($"Failed to create program: {errorCode}");
                return false;
            }

            // Build program
            errorCode = _cl.BuildProgram(_program, 1, &_device, null, null, null);
            if (errorCode != (int)ErrorCodes.Success)
            {
                Console.WriteLine($"Failed to build program: {errorCode}");

                // Get build log
                nuint logSize;
                _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.Log, 0, null, &logSize);
                if (logSize > 0)
                {
                    byte* log = stackalloc byte[(int)logSize];
                    _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.Log, logSize, log, null);
                    string logString = Marshal.PtrToStringAnsi((nint)log);
                    Console.WriteLine($"Build log:\n{logString}");
                }
                return false;
            }

            return true;
        }

        #endregion

        #region GPU Simulation

        /// <summary>
        /// Run ORC simulation on GPU for batch of operating points
        /// </summary>
        public ORCSimulation.ORCCycleResults[] SimulateBatch(float[] geoTemperatures, float geoMassFlowRate)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Solver not initialized. Call Initialize() first.");
            }

            int n = geoTemperatures.Length;
            var results = new ORCSimulation.ORCCycleResults[n];

            try
            {
                // Create buffers
                CreateBuffers(n);

                // Upload input data
                UploadData(geoTemperatures, geoMassFlowRate, n);

                // Execute kernels
                ExecuteKernels(n);

                // Download results
                DownloadResults(results, n);

                return results;
            }
            finally
            {
                // Clean up buffers
                ReleaseBuffers();
            }
        }

        private void CreateBuffers(int n)
        {
            int errorCode;
            nuint size = (nuint)(n * sizeof(float));

            _bufferGeoTemp = _cl.CreateBuffer(_context, MemFlags.ReadOnly, size, null, &errorCode);
            CheckError(errorCode, "Create geoTemp buffer");

            _bufferGeoMassFlow = _cl.CreateBuffer(_context, MemFlags.ReadOnly, (nuint)sizeof(float), null, &errorCode);
            CheckError(errorCode, "Create geoMassFlow buffer");

            _bufferNetPower = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size, null, &errorCode);
            CheckError(errorCode, "Create netPower buffer");

            _bufferEfficiency = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size, null, &errorCode);
            CheckError(errorCode, "Create efficiency buffer");

            _bufferMassFlowRate = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size, null, &errorCode);
            CheckError(errorCode, "Create massFlowRate buffer");

            _bufferTurbineWork = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size, null, &errorCode);
            CheckError(errorCode, "Create turbineWork buffer");

            _bufferPumpWork = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size, null, &errorCode);
            CheckError(errorCode, "Create pumpWork buffer");

            _bufferHeatInput = _cl.CreateBuffer(_context, MemFlags.WriteOnly, size, null, &errorCode);
            CheckError(errorCode, "Create heatInput buffer");
        }

        private void UploadData(float[] geoTemps, float geoMassFlow, int n)
        {
            int errorCode;

            fixed (float* pGeoTemps = geoTemps)
            {
                errorCode = _cl.EnqueueWriteBuffer(_commandQueue, _bufferGeoTemp, true, 0,
                    (nuint)(n * sizeof(float)), pGeoTemps, 0, null, null);
                CheckError(errorCode, "Upload geoTemps");
            }

            errorCode = _cl.EnqueueWriteBuffer(_commandQueue, _bufferGeoMassFlow, true, 0,
                (nuint)sizeof(float), &geoMassFlow, 0, null, null);
            CheckError(errorCode, "Upload geoMassFlow");
        }

        private void ExecuteKernels(int n)
        {
            int errorCode;

            // Set kernel arguments
            float condTemp = _config.CondenserTemperature;
            float evapPress = _config.EvaporatorPressure;
            float pumpEff = _config.PumpEfficiency;
            float turbEff = _config.TurbineEfficiency;
            float pinch = _config.MinPinchPointTemperature;
            float superheat = _config.SuperheatDegrees;
            float geoCp = _config.GeothermalFluidCp;
            float maxORCFlow = _config.MaxORCMassFlowRate;

            int argIdx = 0;
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(nint), &_bufferGeoTemp);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(nint), &_bufferGeoMassFlow);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(float), &condTemp);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(float), &evapPress);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(float), &pumpEff);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(float), &turbEff);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(float), &pinch);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(float), &superheat);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(float), &geoCp);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(float), &maxORCFlow);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(nint), &_bufferNetPower);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(nint), &_bufferEfficiency);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(nint), &_bufferMassFlowRate);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(nint), &_bufferTurbineWork);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(nint), &_bufferPumpWork);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(nint), &_bufferHeatInput);
            _cl.SetKernelArg(_kernelORCCycle, (uint)argIdx++, (nuint)sizeof(int), &n);

            // Execute kernel
            nuint globalWorkSize = (nuint)n;
            nuint localWorkSize = 256;
            errorCode = _cl.EnqueueNdrangeKernel(_commandQueue, _kernelORCCycle, 1, null,
                &globalWorkSize, &localWorkSize, 0, null, null);
            CheckError(errorCode, "Execute ORC kernel");

            _cl.Finish(_commandQueue);
        }

        private void DownloadResults(ORCSimulation.ORCCycleResults[] results, int n)
        {
            int errorCode;

            float[] netPower = new float[n];
            float[] efficiency = new float[n];
            float[] massFlowRate = new float[n];
            float[] turbineWork = new float[n];
            float[] pumpWork = new float[n];
            float[] heatInput = new float[n];

            fixed (float* pNetPower = netPower)
            fixed (float* pEfficiency = efficiency)
            fixed (float* pMassFlow = massFlowRate)
            fixed (float* pTurbWork = turbineWork)
            fixed (float* pPumpWork = pumpWork)
            fixed (float* pHeatInput = heatInput)
            {
                errorCode = _cl.EnqueueReadBuffer(_commandQueue, _bufferNetPower, true, 0,
                    (nuint)(n * sizeof(float)), pNetPower, 0, null, null);
                CheckError(errorCode, "Download netPower");

                errorCode = _cl.EnqueueReadBuffer(_commandQueue, _bufferEfficiency, true, 0,
                    (nuint)(n * sizeof(float)), pEfficiency, 0, null, null);
                CheckError(errorCode, "Download efficiency");

                errorCode = _cl.EnqueueReadBuffer(_commandQueue, _bufferMassFlowRate, true, 0,
                    (nuint)(n * sizeof(float)), pMassFlow, 0, null, null);
                CheckError(errorCode, "Download massFlowRate");

                errorCode = _cl.EnqueueReadBuffer(_commandQueue, _bufferTurbineWork, true, 0,
                    (nuint)(n * sizeof(float)), pTurbWork, 0, null, null);
                CheckError(errorCode, "Download turbineWork");

                errorCode = _cl.EnqueueReadBuffer(_commandQueue, _bufferPumpWork, true, 0,
                    (nuint)(n * sizeof(float)), pPumpWork, 0, null, null);
                CheckError(errorCode, "Download pumpWork");

                errorCode = _cl.EnqueueReadBuffer(_commandQueue, _bufferHeatInput, true, 0,
                    (nuint)(n * sizeof(float)), pHeatInput, 0, null, null);
                CheckError(errorCode, "Download heatInput");
            }

            // Populate results
            for (int i = 0; i < n; i++)
            {
                results[i].NetPower = netPower[i];
                results[i].ThermalEfficiency = efficiency[i];
                results[i].MassFlowRate = massFlowRate[i];
                results[i].TurbineWork = turbineWork[i];
                results[i].PumpWork = pumpWork[i];
                results[i].HeatInput = heatInput[i];
                results[i].HeatRejected = heatInput[i] - netPower[i];
            }
        }

        private void ReleaseBuffers()
        {
            if (_bufferGeoTemp != nint.Zero) _cl.ReleaseMemObject(_bufferGeoTemp);
            if (_bufferGeoMassFlow != nint.Zero) _cl.ReleaseMemObject(_bufferGeoMassFlow);
            if (_bufferNetPower != nint.Zero) _cl.ReleaseMemObject(_bufferNetPower);
            if (_bufferEfficiency != nint.Zero) _cl.ReleaseMemObject(_bufferEfficiency);
            if (_bufferMassFlowRate != nint.Zero) _cl.ReleaseMemObject(_bufferMassFlowRate);
            if (_bufferTurbineWork != nint.Zero) _cl.ReleaseMemObject(_bufferTurbineWork);
            if (_bufferPumpWork != nint.Zero) _cl.ReleaseMemObject(_bufferPumpWork);
            if (_bufferHeatInput != nint.Zero) _cl.ReleaseMemObject(_bufferHeatInput);

            _bufferGeoTemp = nint.Zero;
            _bufferGeoMassFlow = nint.Zero;
            _bufferNetPower = nint.Zero;
            _bufferEfficiency = nint.Zero;
            _bufferMassFlowRate = nint.Zero;
            _bufferTurbineWork = nint.Zero;
            _bufferPumpWork = nint.Zero;
            _bufferHeatInput = nint.Zero;
        }

        #endregion

        #region OpenCL Kernel Source

        private string GetKernelSource()
        {
            // Generate dynamic kernel source based on current fluid properties
            var antoine = _currentFluid.AntoineCoefficients_A_B_C;
            var hLiq = _currentFluid.LiquidEnthalpyCoeff_A_B_C_D;
            var hVap = _currentFluid.VaporEnthalpyCoeff_A_B_C_D;
            var sLiq = _currentFluid.LiquidEntropyCoeff_A_B_C_D;
            var sVap = _currentFluid.VaporEntropyCoeff_A_B_C_D;
            var tCrit = _currentFluid.CriticalTemperature_K;
            var rhoLiq = _currentFluid.LiquidDensity_kgm3;

            return $@"
// {_currentFluid.Name} property correlations from ORCFluidLibrary
float sat_pressure(float T) {{
    // Antoine equation: log10(P[Pa]) = A - B/(T[K]+C)
    const float A = {antoine[0]}f;
    const float B = {antoine[1]}f;
    const float C = {antoine[2]}f;
    float log10P = A - B/(T+C);
    // Convert log10 to actual: P = 10^log10P = exp(log10P * ln(10))
    return exp(log10P * 2.302585f);
}}

float sat_temperature(float P) {{
    // Inverse Antoine: T = B/(A - log10(P)) - C
    const float A = {antoine[0]}f;
    const float B = {antoine[1]}f;
    const float C = {antoine[2]}f;
    float log10P = log(P) / 2.302585f; // log10(P) = ln(P)/ln(10)
    return B / (A - log10P) - C;
}}

float enthalpy_liquid(float T) {{
    return {hLiq[0]}f + {hLiq[1]}f*T + {hLiq[2]}f*T*T + {hLiq[3]}f*T*T*T;
}}

float enthalpy_vapor(float T) {{
    return {hVap[0]}f + {hVap[1]}f*T + {hVap[2]}f*T*T + {hVap[3]}f*T*T*T;
}}

float entropy_liquid(float T) {{
    return {sLiq[0]}f + {sLiq[1]}f*T + {sLiq[2]}f*T*T + {sLiq[3]}f*T*T*T;
}}

float entropy_vapor(float T) {{
    return {sVap[0]}f + {sVap[1]}f*T + {sVap[2]}f*T*T + {sVap[3]}f*T*T*T;
}}"

// Main ORC cycle kernel
__kernel void orc_cycle_kernel(
    __global const float* geoTemp,
    __global const float* geoMassFlow,
    const float condTemp,
    const float evapPress,
    const float pumpEff,
    const float turbEff,
    const float pinch,
    const float superheat,
    const float geoCp,
    const float maxORCFlow,
    __global float* netPower,
    __global float* efficiency,
    __global float* massFlowRate,
    __global float* turbineWork,
    __global float* pumpWork,
    __global float* heatInput,
    const int n)
{
    int gid = get_global_id(0);
    if (gid >= n) return;

    float T_geo = geoTemp[gid];
    float m_geo = *geoMassFlow;

    // State 1: Saturated liquid at condenser
    float P1 = sat_pressure(condTemp);
    float h1 = enthalpy_liquid(condTemp);
    float s1 = entropy_liquid(condTemp);

    // State 2: Pump outlet
    const float rho_liquid = {rhoLiq}f; // kg/m³
    float dh_pump = (evapPress - P1) / (rho_liquid * pumpEff);
    float h2 = h1 + dh_pump;

    // State 3: Turbine inlet (superheated vapor)
    float T_max_evap = T_geo - pinch;
    float T3 = T_max_evap - superheat;
    T3 = fmin(T3, {tCrit}f - 10.0f); // Below critical temp
    float h3 = enthalpy_vapor(T3) + superheat * 1000.0f;
    float s3 = entropy_vapor(T3) + superheat * 2.0f;

    // State 4: Turbine outlet (isentropic expansion with efficiency)
    float T4s = sat_temperature(P1);
    float h4s = enthalpy_vapor(T4s);
    float h4 = h3 - turbEff * (h3 - h4s);

    // Calculate ORC mass flow rate
    float dh_evap = h3 - h2;
    float Q_max = m_geo * geoCp * (T_geo - condTemp);
    float m_orc = fmin(Q_max / dh_evap, maxORCFlow);

    // Work and heat
    float W_pump = m_orc * (h2 - h1);
    float W_turb = m_orc * (h3 - h4);
    float W_net = W_turb - W_pump;
    float Q_in = m_orc * (h3 - h2);
    float eta = W_net / Q_in;

    // Store results
    netPower[gid] = W_net;
    efficiency[gid] = eta;
    massFlowRate[gid] = m_orc;
    turbineWork[gid] = W_turb;
    pumpWork[gid] = W_pump;
    heatInput[gid] = Q_in;
}

// Parallel reduction for total power calculation
__kernel void reduction_sum_kernel(
    __global const float* input,
    __global float* output,
    __local float* scratch,
    const int n)
{
    int gid = get_global_id(0);
    int lid = get_local_id(0);
    int lsize = get_local_size(0);

    // Load data into local memory
    float value = (gid < n) ? input[gid] : 0.0f;
    scratch[lid] = value;
    barrier(CLK_LOCAL_MEM_FENCE);

    // Parallel reduction in local memory
    for (int offset = lsize / 2; offset > 0; offset >>= 1) {
        if (lid < offset) {
            scratch[lid] += scratch[lid + offset];
        }
        barrier(CLK_LOCAL_MEM_FENCE);
    }

    // Write result for this work group
    if (lid == 0) {
        output[get_group_id(0)] = scratch[0];
    }
}
";
        }

        #endregion

        #region Utilities

        private void CheckError(int errorCode, string operation)
        {
            if (errorCode != (int)ErrorCodes.Success)
            {
                throw new Exception($"OpenCL error during {operation}: {errorCode}");
            }
        }

        public void Dispose()
        {
            ReleaseBuffers();

            if (_kernelORCCycle != nint.Zero) _cl.ReleaseKernel(_kernelORCCycle);
            if (_kernelReduction != nint.Zero) _cl.ReleaseKernel(_kernelReduction);
            if (_program != nint.Zero) _cl.ReleaseProgram(_program);
            if (_commandQueue != nint.Zero) _cl.ReleaseCommandQueue(_commandQueue);
            if (_context != nint.Zero) _cl.ReleaseContext(_context);

            _isInitialized = false;
        }

        #endregion
    }
}
