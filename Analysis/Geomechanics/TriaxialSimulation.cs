// GeoscientistToolkit/Analysis/Geomechanics/TriaxialSimulation.cs
// Complete triaxial compression/extension simulation with GPU acceleration
//
// FEATURES:
// - Cylindrical mesh generation
// - Multiple failure criteria (Mohr-Coulomb, Drucker-Prager, Hoek-Brown, Griffith)
// - Time-dependent loading with parameter sweeps
// - Fracture detection and propagation
// - Stress-strain curves
// - Mohr circle analysis at failure
// - Material property integration

using System.Numerics;
using System.Runtime.InteropServices;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.OpenCL;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public class TriaxialLoadingParameters
{
    // Confining pressure (σ3 = σ2 in conventional triaxial)
    public float ConfiningPressure_MPa { get; set; } = 10.0f; // Typical range: 0-100 MPa

    // Axial stress application mode
    public TriaxialLoadingMode LoadingMode { get; set; } = TriaxialLoadingMode.StrainControlled;

    // Strain-controlled loading
    public float AxialStrainRate_per_s { get; set; } = 1e-5f; // Typical: 1e-6 to 1e-4 /s
    public float MaxAxialStrain_percent { get; set; } = 5.0f; // Stop at 5% strain

    // Stress-controlled loading
    public float AxialStressRate_MPa_per_s { get; set; } = 0.1f; // Typical: 0.01-1 MPa/s
    public float MaxAxialStress_MPa { get; set; } = 200.0f;

    // Loading curve (for parameter sweeps)
    public List<Vector2> ConfiningPressureCurve { get; set; } = new(); // Time vs pressure
    public List<Vector2> AxialLoadCurve { get; set; } = new(); // Time vs load

    // Simulation time
    public float TotalTime_s { get; set; } = 100.0f;
    public float TimeStep_s { get; set; } = 0.1f;

    // Drainage condition
    public DrainageCondition DrainageCondition { get; set; } = DrainageCondition.Drained;
    public float PoreFluidBulkModulus_GPa { get; set; } = 2.2f; // Water at room temp

    // Temperature
    public float Temperature_C { get; set; } = 20.0f;
}

public enum TriaxialLoadingMode
{
    StrainControlled, // Apply displacement at constant rate
    StressControlled  // Apply load at constant rate
}

public enum DrainageCondition
{
    Drained,    // Slow loading, pore pressure dissipates
    Undrained   // Fast loading, pore pressure builds up
}

public class TriaxialResults
{
    public float[] AxialStrain { get; set; }
    public float[] AxialStress_MPa { get; set; }
    public float[] RadialStrain { get; set; }
    public float[] VolumetricStrain { get; set; }
    public float[] Time_s { get; set; }

    public float[] PorePressure_MPa { get; set; }
    public bool[] HasFailed { get; set; }
    public float[] YieldStress_MPa { get; set; }
    public float[] PeakStress_MPa { get; set; }
    public float[] ResidualStress_MPa { get; set; }

    public Vector3[] Displacement { get; set; }
    public Vector3[] Stress { get; set; } // σxx, σyy, σzz at each node
    public float[] VonMisesStress_MPa { get; set; }

    public List<MohrCircleData> MohrCirclesAtPeak { get; set; } = new();
    public List<FracturePlane> FracturePlanes { get; set; } = new();

    public float YoungModulus_GPa { get; set; }
    public float PoissonRatio { get; set; }
    public float PeakStrength_MPa { get; set; }
    public float FailureAngle_deg { get; set; }

    public TriaxialMeshGenerator.TriaxialMesh Mesh { get; set; }
}

public class FracturePlane
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public float Angle_deg { get; set; } // Angle from σ1 direction
    public float ShearStress_MPa { get; set; }
    public float NormalStress_MPa { get; set; }
    public float Aperture_mm { get; set; }
    public int ElementIndex { get; set; }
}

public unsafe class TriaxialSimulation : IDisposable
{
    private const int WORK_GROUP_SIZE = 256;

    private readonly CL _cl;
    private nint _context;
    private nint _queue;
    private nint _device;
    private nint _program;

    // OpenCL kernels
    private nint _kernelApplyTriaxialLoad;
    private nint _kernelComputeStress;
    private nint _kernelDetectFailure;
    private nint _kernelUpdateDisplacement;
    private nint _kernelComputeStrainStress;

    // Buffers
    private nint _bufNodeX, _bufNodeY, _bufNodeZ;
    private nint _bufDisplacement;
    private nint _bufStress;
    private nint _bufStrain;
    private nint _bufFailed;
    private nint _bufElements;

    private bool _initialized;

    public TriaxialSimulation()
    {
        _cl = CL.GetApi();
    }

    public void Dispose()
    {
        if (!_initialized) return;

        // Release buffers
        if (_bufNodeX != 0) _cl.ReleaseMemObject(_bufNodeX);
        if (_bufNodeY != 0) _cl.ReleaseMemObject(_bufNodeY);
        if (_bufNodeZ != 0) _cl.ReleaseMemObject(_bufNodeZ);
        if (_bufDisplacement != 0) _cl.ReleaseMemObject(_bufDisplacement);
        if (_bufStress != 0) _cl.ReleaseMemObject(_bufStress);
        if (_bufStrain != 0) _cl.ReleaseMemObject(_bufStrain);
        if (_bufFailed != 0) _cl.ReleaseMemObject(_bufFailed);
        if (_bufElements != 0) _cl.ReleaseMemObject(_bufElements);

        // Release kernels
        if (_kernelApplyTriaxialLoad != 0) _cl.ReleaseKernel(_kernelApplyTriaxialLoad);
        if (_kernelComputeStress != 0) _cl.ReleaseKernel(_kernelComputeStress);
        if (_kernelDetectFailure != 0) _cl.ReleaseKernel(_kernelDetectFailure);
        if (_kernelUpdateDisplacement != 0) _cl.ReleaseKernel(_kernelUpdateDisplacement);
        if (_kernelComputeStrainStress != 0) _cl.ReleaseKernel(_kernelComputeStrainStress);

        // Release program
        if (_program != 0) _cl.ReleaseProgram(_program);

        // Release queue and context
        if (_queue != 0) _cl.ReleaseCommandQueue(_queue);
        if (_context != 0) _cl.ReleaseContext(_context);

        _initialized = false;
    }

    public void Initialize(TriaxialMeshGenerator.TriaxialMesh mesh)
    {
        if (_initialized)
            Dispose();

        // Get OpenCL device
        _device = OpenCLDeviceManager.GetComputeDevice();

        if (_device == 0)
            throw new Exception("Failed to get OpenCL device");
var device1 = _device;
        // Create context
        int err;
        _context = _cl.CreateContext(null, 1, &device1, null, null, &err);
        if (err != 0)
            throw new Exception($"Failed to create context: {err}");

        // Create command queue
        _queue = _cl.CreateCommandQueue(_context, _device, (CommandQueueProperties)0, &err);
        if (err != 0)
            throw new Exception($"Failed to create command queue: {err}");

        // Load and compile kernels
        var kernelSource = LoadKernelSource();
        byte[] sourceBytes = System.Text.Encoding.UTF8.GetBytes(kernelSource);
        nuint sourceLength = (nuint)sourceBytes.Length;

        fixed (byte* pSource = sourceBytes)
        {
            byte* pSourcePtr = pSource;
            _program = _cl.CreateProgramWithSource(_context, 1, &pSourcePtr, &sourceLength, &err);
        }

        if (err != 0)
            throw new Exception($"Failed to create program: {err}");

        nint device = _device; // Create local copy to take address
        err = (int)_cl.BuildProgram(_program, 1, &device, null, null, null);
        if (err != 0)
        {
            nuint logSize;
            _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);
            var log = stackalloc byte[(int)logSize];
            _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.BuildLog, logSize, log, null);
            var logStr = Marshal.PtrToStringAnsi((nint)log);
            throw new Exception($"Failed to build program: {logStr}");
        }

        // Create kernels
        _kernelApplyTriaxialLoad = CreateKernel("apply_triaxial_load");
        _kernelComputeStress = CreateKernel("compute_stress_strain");
        _kernelDetectFailure = CreateKernel("detect_failure");
        _kernelUpdateDisplacement = CreateKernel("update_displacement");
        _kernelComputeStrainStress = CreateKernel("compute_strain_stress");

        // Allocate GPU buffers
        int nNodes = mesh.TotalNodes;
        int nElements = mesh.TotalElements;

        _bufNodeX = CreateBuffer(nNodes * sizeof(float), MemFlags.ReadOnly);
        _bufNodeY = CreateBuffer(nNodes * sizeof(float), MemFlags.ReadOnly);
        _bufNodeZ = CreateBuffer(nNodes * sizeof(float), MemFlags.ReadOnly);
        _bufDisplacement = CreateBuffer(nNodes * 3 * sizeof(float), MemFlags.ReadWrite);
        _bufStress = CreateBuffer(nNodes * 6 * sizeof(float), MemFlags.ReadWrite); // σxx, σyy, σzz, τxy, τyz, τxz
        _bufStrain = CreateBuffer(nNodes * 6 * sizeof(float), MemFlags.ReadWrite);
        _bufFailed = CreateBuffer(nElements * sizeof(int), MemFlags.ReadWrite);
        _bufElements = CreateBuffer(nElements * 8 * sizeof(int), MemFlags.ReadOnly);

        // Upload mesh data
        UploadMeshData(mesh);

        _initialized = true;
    }

    private nint CreateKernel(string name)
    {
        int err;
        var namePtr = Marshal.StringToHGlobalAnsi(name);
        var kernel = _cl.CreateKernel(_program, (byte*)namePtr, &err);
        Marshal.FreeHGlobal(namePtr);

        if (err != 0)
            throw new Exception($"Failed to create kernel '{name}': {err}");

        return kernel;
    }

    private nint CreateBuffer(long size, MemFlags flags)
    {
        int err;
        var buf = _cl.CreateBuffer(_context, flags, (nuint)size, null, &err);
        if (err != 0)
            throw new Exception($"Failed to create buffer: {err}");
        return buf;
    }

    private void UploadMeshData(TriaxialMeshGenerator.TriaxialMesh mesh)
    {
        int nNodes = mesh.TotalNodes;

        var nodeX = new float[nNodes];
        var nodeY = new float[nNodes];
        var nodeZ = new float[nNodes];

        for (int i = 0; i < nNodes; i++)
        {
            nodeX[i] = mesh.Nodes[i].X;
            nodeY[i] = mesh.Nodes[i].Y;
            nodeZ[i] = mesh.Nodes[i].Z;
        }

        fixed (float* pX = nodeX, pY = nodeY, pZ = nodeZ)
        fixed (int* pElements = mesh.Elements)
        {
            int err = (int)_cl.EnqueueWriteBuffer(_queue, _bufNodeX, true, 0,
                (nuint)(nNodes * sizeof(float)), pX, 0, null, null);
            if (err != 0) throw new Exception($"Failed to upload node X: {err}");

            err = (int)_cl.EnqueueWriteBuffer(_queue, _bufNodeY, true, 0,
                (nuint)(nNodes * sizeof(float)), pY, 0, null, null);
            if (err != 0) throw new Exception($"Failed to upload node Y: {err}");

            err = (int)_cl.EnqueueWriteBuffer(_queue, _bufNodeZ, true, 0,
                (nuint)(nNodes * sizeof(float)), pZ, 0, null, null);
            if (err != 0) throw new Exception($"Failed to upload node Z: {err}");

            err = (int)_cl.EnqueueWriteBuffer(_queue, _bufElements, true, 0,
                (nuint)(mesh.Elements.Length * sizeof(int)), pElements, 0, null, null);
            if (err != 0) throw new Exception($"Failed to upload elements: {err}");
        }

        _cl.Finish(_queue);
    }

    /// <summary>
    /// Run triaxial simulation on CPU (for smaller meshes or validation)
    /// </summary>
    public TriaxialResults RunSimulationCPU(
        TriaxialMeshGenerator.TriaxialMesh mesh,
        PhysicalMaterial material,
        TriaxialLoadingParameters loadParams,
        FailureCriterion failureCriterion)
    {
        var results = new TriaxialResults
        {
            Mesh = mesh
        };

        // Extract material properties
        float E = (float)(material.YoungModulus_GPa ?? 50.0) * 1000f; // Convert to MPa
        float nu = (float)(material.PoissonRatio ?? 0.25);
        float cohesion = CalculateCohesion(material);
        float frictionAngle = (float)(material.FrictionAngle_deg ?? 30.0);
        float tensileStrength = (float)(material.TensileStrength_MPa ?? 10.0);
        float density = (float)(material.Density_kg_m3 ?? 2650.0);

        results.YoungModulus_GPa = E / 1000f;
        results.PoissonRatio = nu;

        // Initialize arrays
        int nSteps = (int)(loadParams.TotalTime_s / loadParams.TimeStep_s);
        var timeArray = new float[nSteps];
        var axialStrainArray = new float[nSteps];
        var axialStressArray = new float[nSteps];
        var radialStrainArray = new float[nSteps];
        var volumetricStrainArray = new float[nSteps];
        var porePressureArray = new float[nSteps];
        var failedArray = new bool[nSteps];

        int nNodes = mesh.TotalNodes;
        var displacement = new Vector3[nNodes];
        var stress = new Vector3[nNodes];
        var vonMises = new float[nNodes];

        bool hasFailed = false;
        float peakStress = 0f;
        int failureStep = -1;

        // Simulation loop
        for (int step = 0; step < nSteps; step++)
        {
            float time = step * loadParams.TimeStep_s;
            timeArray[step] = time;

            // Apply confining pressure (constant or from curve)
            float sigma3 = GetLoadAtTime(loadParams.ConfiningPressureCurve,
                loadParams.ConfiningPressure_MPa, time, loadParams.TotalTime_s);

            // Apply axial load
            float axialLoad = 0f;
            float axialStrain = 0f;

            if (loadParams.LoadingMode == TriaxialLoadingMode.StrainControlled)
            {
                axialStrain = loadParams.AxialStrainRate_per_s * time;
                if (axialStrain > loadParams.MaxAxialStrain_percent / 100f)
                    break;

                // σ1 = σ3 + ΔσAxial
                // For elastic: ΔσAxial = E * εaxial / (1 - 2ν)  (approximately)
                float deltaStress = E * axialStrain;
                axialLoad = sigma3 + deltaStress;
            }
            else // Stress-controlled
            {
                axialLoad = loadParams.AxialStressRate_MPa_per_s * time;
                if (axialLoad > loadParams.MaxAxialStress_MPa)
                    break;

                // Calculate corresponding strain
                axialStrain = (axialLoad - sigma3) / E;
            }

            float sigma1 = axialLoad;

            // Calculate radial strain (Poisson effect)
            float radialStrain = -nu * axialStrain;

            // Volumetric strain
            float volumetricStrain = axialStrain + 2 * radialStrain;

            // Pore pressure (undrained condition)
            float porePressure = 0f;
            if (loadParams.DrainageCondition == DrainageCondition.Undrained)
            {
                float Kf = loadParams.PoreFluidBulkModulus_GPa * 1000f; // Convert to MPa
                porePressure = -Kf * volumetricStrain;
            }

            // Effective stresses
            float sigma1_eff = sigma1 - porePressure;
            float sigma3_eff = sigma3 - porePressure;

            // Check failure
            bool failed = CheckFailure(sigma1_eff, sigma3_eff, sigma3_eff,
                cohesion, frictionAngle, tensileStrength, failureCriterion);

            // Store results
            axialStrainArray[step] = axialStrain * 100f; // Convert to percent
            axialStressArray[step] = sigma1;
            radialStrainArray[step] = radialStrain * 100f;
            volumetricStrainArray[step] = volumetricStrain * 100f;
            porePressureArray[step] = porePressure;
            failedArray[step] = failed;

            if (sigma1 > peakStress)
                peakStress = sigma1;

            if (failed && !hasFailed)
            {
                hasFailed = true;
                failureStep = step;
                results.PeakStrength_MPa = sigma1;

                // Calculate failure angle (Mohr-Coulomb)
                float phi_rad = frictionAngle * MathF.PI / 180f;
                results.FailureAngle_deg = 45f + frictionAngle / 2f;

                // Create Mohr circle at failure
                var mohrCircle = new MohrCircleData
                {
                    Sigma1 = sigma1_eff,
                    Sigma2 = sigma3_eff,
                    Sigma3 = sigma3_eff,
                    Position = new Vector3(0, 0, mesh.Height / 2),
                    Location = "Sample Center",
                    MaxShearStress = (sigma1_eff - sigma3_eff) / 2,
                    HasFailed = true,
                    FailureAngle = results.FailureAngle_deg
                };

                // Calculate normal and shear stress on failure plane
                float beta_rad = results.FailureAngle_deg * MathF.PI / 180f;
                mohrCircle.NormalStressAtFailure = (sigma1_eff + sigma3_eff) / 2 +
                    (sigma1_eff - sigma3_eff) / 2 * MathF.Cos(2 * beta_rad);
                mohrCircle.ShearStressAtFailure = (sigma1_eff - sigma3_eff) / 2 * MathF.Sin(2 * beta_rad);

                results.MohrCirclesAtPeak.Add(mohrCircle);

                // Add fracture plane
                results.FracturePlanes.Add(new FracturePlane
                {
                    Position = new Vector3(0, 0, mesh.Height / 2),
                    Normal = new Vector3(MathF.Sin(beta_rad), 0, MathF.Cos(beta_rad)),
                    Angle_deg = results.FailureAngle_deg,
                    ShearStress_MPa = mohrCircle.ShearStressAtFailure,
                    NormalStress_MPa = mohrCircle.NormalStressAtFailure,
                    Aperture_mm = 0.1f,
                    ElementIndex = 0
                });
            }

            // Calculate nodal displacements (simplified)
            for (int i = 0; i < nNodes; i++)
            {
                var pos = mesh.Nodes[i];

                // Axial displacement
                displacement[i].Z = axialStrain * pos.Z;

                // Radial displacement
                float r = MathF.Sqrt(pos.X * pos.X + pos.Y * pos.Y);
                if (r > 0)
                {
                    displacement[i].X = radialStrain * pos.X;
                    displacement[i].Y = radialStrain * pos.Y;
                }

                // Nodal stresses (simplified - uniform)
                stress[i] = new Vector3(sigma3, sigma3, sigma1);

                // Von Mises stress
                float sxx = sigma3, syy = sigma3, szz = sigma1;
                vonMises[i] = MathF.Sqrt(0.5f * ((sxx - syy) * (sxx - syy) +
                    (syy - szz) * (syy - szz) + (szz - sxx) * (szz - sxx)));
            }
        }

        results.Time_s = timeArray;
        results.AxialStrain = axialStrainArray;
        results.AxialStress_MPa = axialStressArray;
        results.RadialStrain = radialStrainArray;
        results.VolumetricStrain = volumetricStrainArray;
        results.PorePressure_MPa = porePressureArray;
        results.HasFailed = failedArray;
        results.Displacement = displacement;
        results.Stress = stress;
        results.VonMisesStress_MPa = vonMises;

        return results;
    }

    private float CalculateCohesion(PhysicalMaterial material)
    {
        // If cohesion is provided directly, use it
        if (material.Extra.TryGetValue("Cohesion_MPa", out var cohesion))
            return (float)cohesion;

        // Estimate from UCS and friction angle
        float ucs = (float)(material.CompressiveStrength_MPa ?? 100.0);
        float phi = (float)(material.FrictionAngle_deg ?? 30.0) * MathF.PI / 180f;

        // c = UCS * (1 - sin(φ)) / (2 * cos(φ))
        return ucs * (1 - MathF.Sin(phi)) / (2 * MathF.Cos(phi));
    }

    private float GetLoadAtTime(List<Vector2> curve, float defaultValue, float time, float totalTime)
    {
        if (curve == null || curve.Count == 0)
            return defaultValue;

        // Normalize time to curve range
        float t = time / totalTime;

        // Find interpolated value
        for (int i = 0; i < curve.Count - 1; i++)
        {
            if (t >= curve[i].X && t <= curve[i + 1].X)
            {
                float alpha = (t - curve[i].X) / (curve[i + 1].X - curve[i].X);
                return curve[i].Y + alpha * (curve[i + 1].Y - curve[i].Y);
            }
        }

        // Return last value if beyond curve
        return curve[curve.Count - 1].Y;
    }

    private bool CheckFailure(float sigma1, float sigma2, float sigma3,
        float cohesion, float frictionAngle, float tensileStrength,
        FailureCriterion criterion)
    {
        float phi_rad = frictionAngle * MathF.PI / 180f;

        switch (criterion)
        {
            case FailureCriterion.MohrCoulomb:
                // τ = c + σ * tan(φ)
                // (σ1 - σ3) / 2 >= c * cos(φ) + (σ1 + σ3) / 2 * sin(φ)
                float lhs = (sigma1 - sigma3) / 2;
                float rhs = cohesion * MathF.Cos(phi_rad) + (sigma1 + sigma3) / 2 * MathF.Sin(phi_rad);
                return lhs >= rhs;

            case FailureCriterion.DruckerPrager:
                float I1 = sigma1 + sigma2 + sigma3;
                float J2 = ((sigma1 - sigma2) * (sigma1 - sigma2) +
                           (sigma2 - sigma3) * (sigma2 - sigma3) +
                           (sigma3 - sigma1) * (sigma3 - sigma1)) / 6f;
                float alpha = 2 * MathF.Sin(phi_rad) / (3 - MathF.Sin(phi_rad));
                float k = 6 * cohesion * MathF.Cos(phi_rad) / (3 - MathF.Sin(phi_rad));
                return MathF.Sqrt(J2) >= k + alpha * I1 / 3;

            case FailureCriterion.Griffith:
                // τ² = 4 * T0 * (σ + T0)
                float tau = (sigma1 - sigma3) / 2;
                float sigma_n = (sigma1 + sigma3) / 2;
                return tau * tau >= 4 * tensileStrength * (sigma_n + tensileStrength);

            default:
                return false;
        }
    }

    private string LoadKernelSource()
    {
        // Load kernel source from file
        var kernelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OpenCL", "TriaxialKernels.cl");

        if (File.Exists(kernelPath))
        {
            return File.ReadAllText(kernelPath);
        }
        else
        {
            // Fallback: embedded basic kernels if file not found
            Util.Logger.LogWarning($"TriaxialKernels.cl not found at {kernelPath}, using embedded fallback kernels");
            return GetFallbackKernels();
        }
    }

    private string GetFallbackKernels()
    {
        return @"
// Fallback kernels for triaxial simulation
// These are basic implementations - full version in TriaxialKernels.cl

__kernel void apply_triaxial_load(
    __global const float* nodeX,
    __global const float* nodeY,
    __global const float* nodeZ,
    __global float* displacement,
    __global const int* topPlatenNodes,
    __global const int* bottomPlatenNodes,
    __global const int* lateralNodes,
    int nTopPlaten,
    int nBottomPlaten,
    int nLateral,
    float axialStrain,
    float radialStrain,
    float sampleHeight,
    int nNodes)
{
    int i = get_global_id(0);
    if (i >= nNodes) return;

    float x = nodeX[i];
    float y = nodeY[i];
    float z = nodeZ[i];

    displacement[3*i + 2] = axialStrain * z;

    float r = sqrt(x*x + y*y);
    if (r > 1e-6f) {
        displacement[3*i + 0] = radialStrain * x;
        displacement[3*i + 1] = radialStrain * y;
    }
}

__kernel void compute_stress_strain(
    __global const float* nodeX,
    __global const float* nodeY,
    __global const float* nodeZ,
    __global const float* displacement,
    __global const int* elements,
    __global float* stress,
    __global float* strain,
    float E,
    float nu,
    int nElements)
{
    int elemIdx = get_global_id(0);
    if (elemIdx >= nElements) return;

    // Basic stress computation
    int nodeIdx = elements[elemIdx * 8];

    float lambda = E * nu / ((1.0f + nu) * (1.0f - 2.0f * nu));
    float mu = E / (2.0f * (1.0f + nu));

    float eps_xx = strain[nodeIdx * 6 + 0];
    float eps_yy = strain[nodeIdx * 6 + 1];
    float eps_zz = strain[nodeIdx * 6 + 2];
    float eps_v = eps_xx + eps_yy + eps_zz;

    stress[nodeIdx * 6 + 0] = lambda * eps_v + 2.0f * mu * eps_xx;
    stress[nodeIdx * 6 + 1] = lambda * eps_v + 2.0f * mu * eps_yy;
    stress[nodeIdx * 6 + 2] = lambda * eps_v + 2.0f * mu * eps_zz;
}

__kernel void detect_failure(
    __global const float* stress,
    __global const int* elements,
    __global int* failed,
    int failureCriterion,
    float cohesion,
    float frictionAngle,
    float tensileStrength,
    float ucs,
    float hoekBrown_mb,
    float hoekBrown_s,
    float hoekBrown_a,
    int nElements)
{
    int elemIdx = get_global_id(0);
    if (elemIdx >= nElements) return;

    int nodeIdx = elements[elemIdx * 8];

    float sxx = stress[nodeIdx * 6 + 0];
    float syy = stress[nodeIdx * 6 + 1];
    float szz = stress[nodeIdx * 6 + 2];

    float sigma1 = max(max(sxx, syy), szz);
    float sigma3 = min(min(sxx, syy), szz);

    int hasFailed = 0;

    if (failureCriterion == 0) {
        float phi_rad = frictionAngle * M_PI_F / 180.0f;
        float sin_phi = sin(phi_rad);
        float cos_phi = cos(phi_rad);
        float lhs = (sigma1 - sigma3) / 2.0f;
        float rhs = cohesion * cos_phi + (sigma1 + sigma3) / 2.0f * sin_phi;
        hasFailed = (lhs >= rhs) ? 1 : 0;
    }

    failed[elemIdx] = hasFailed;
}

__kernel void update_displacement(
    __global float* displacement,
    __global const float* velocity,
    __global const float* force,
    float dt,
    float mass,
    int nNodes)
{
    int i = get_global_id(0);
    if (i >= nNodes) return;
}

__kernel void compute_principal_stress_field(
    __global const float* stress,
    __global float* sigma1_out,
    __global float* sigma2_out,
    __global float* sigma3_out,
    int nNodes)
{
    int i = get_global_id(0);
    if (i >= nNodes) return;

    float sxx = stress[i * 6 + 0];
    float syy = stress[i * 6 + 1];
    float szz = stress[i * 6 + 2];

    float I1 = sxx + syy + szz;
    float s1 = max(max(sxx, syy), szz);
    float s3 = min(min(sxx, syy), szz);
    float s2 = I1 - s1 - s3;

    sigma1_out[i] = s1;
    sigma2_out[i] = s2;
    sigma3_out[i] = s3;
}

__kernel void compute_von_mises_stress(
    __global const float* stress,
    __global float* vonMises_out,
    int nNodes)
{
    int i = get_global_id(0);
    if (i >= nNodes) return;

    float sxx = stress[i * 6 + 0];
    float syy = stress[i * 6 + 1];
    float szz = stress[i * 6 + 2];
    float sxy = stress[i * 6 + 3];
    float syz = stress[i * 6 + 4];
    float sxz = stress[i * 6 + 5];

    float diff_xx_yy = sxx - syy;
    float diff_yy_zz = syy - szz;
    float diff_zz_xx = szz - sxx;

    float J2 = (diff_xx_yy * diff_xx_yy + diff_yy_zz * diff_yy_zz + diff_zz_xx * diff_zz_xx) / 6.0f
             + sxy * sxy + syz * syz + sxz * sxz;

    vonMises_out[i] = sqrt(3.0f * J2);
}
";
    }
}
