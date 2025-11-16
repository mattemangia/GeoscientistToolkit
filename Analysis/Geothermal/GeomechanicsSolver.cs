// GeoscientistToolkit/Analysis/Geothermal/GeomechanicsSolver.cs
//
// ================================================================================================
// REFERENCES (APA Format):
// ================================================================================================
// This geomechanics solver for coupled thermo-hydro-mechanical (THM) processes in geothermal
// systems is based on established methods documented in the following scientific literature:
//
// Azad, A., Daneshian, J., Rezaei-Gomari, S., & Niasar, V. (2019). Numerical simulation of
//     hydromechanical coupling in fractured geothermal reservoirs. Geothermics, 77, 124-133.
//     https://doi.org/10.1016/j.geothermics.2018.09.006
//
// Bai, B., He, Y., Li, X., Li, J., Huang, X., & Zhu, J. (2017). Experimental and numerical study
//     on the thermal-hydraulic-mechanical (THM) behavior in enhanced geothermal system.
//     Journal of Rock Mechanics and Geotechnical Engineering, 9(3), 494-505.
//     https://doi.org/10.1016/j.jrmge.2017.03.002
//
// Jacquey, A. B., & Cacace, M. (2020). Multiphysics modeling of a brittle-ductile lithosphere:
//     1. Explicit visco-elasto-plastic formulation and its numerical implementation.
//     Journal of Geophysical Research: Solid Earth, 125, e2019JB018474.
//     https://doi.org/10.1029/2019JB018474
//
// Kolditz, O., Görke, U., Shao, H., & Wang, W. (Eds.). (2012). Thermo-Hydro-Mechanical-Chemical
//     Processes in Porous Media: Benchmarks and Examples. Springer.
//     https://doi.org/10.1007/978-3-642-27177-9
//
// Rutqvist, J. (2011). Status of the TOUGH-FLAC simulator and recent applications related to
//     coupled fluid flow and crustal deformations. Computers & Geosciences, 37(6), 739-750.
//     https://doi.org/10.1016/j.cageo.2010.08.006
//
// Settgast, R. R., Fu, P., Walsh, S. D., White, J. A., Annavarapu, C., & Ryerson, F. J. (2017).
//     A fully coupled method for massively parallel simulation of hydraulically driven fractures
//     in 3-dimensions. International Journal for Numerical and Analytical Methods in Geomechanics,
//     41(5), 627-653. https://doi.org/10.1002/nag.2557
//
// Taron, J., Elsworth, D., & Min, K. B. (2009). Numerical simulation of thermal-hydrologic-
//     mechanical-chemical processes in deformable, fractured porous media. International Journal
//     of Rock Mechanics and Mining Sciences, 46(5), 842-854.
//     https://doi.org/10.1016/j.ijrmms.2009.01.008
//
// Wang, W., Kosakowski, G., & Kolditz, O. (2009). A parallel finite element scheme for thermo-
//     hydro-mechanical (THM) coupled problems in porous media. Computers & Geosciences, 35(8),
//     1631-1641. https://doi.org/10.1016/j.cageo.2008.07.007
//
// Yoon, J. S., Zimmermann, G., & Zang, A. (2015). Numerical investigation on stress shadowing
//     in fluid injection-induced fracture propagation in naturally fractured geothermal reservoirs.
//     Rock Mechanics and Rock Engineering, 48, 1439-1454.
//     https://doi.org/10.1007/s00603-014-0695-5
//
// Zhao, Y., Feng, Z., Feng, Z., Yang, D., & Liang, W. (2015). THM (Thermo-hydro-mechanical)
//     coupled mathematical model of fractured media and numerical simulation of a 3D enhanced
//     geothermal system at 573K and buried depth 6000-7000M. Energy, 82, 193-205.
//     https://doi.org/10.1016/j.energy.2015.01.030
//
// ------------------------------------------------------------------------------------------------
// METHODOLOGY NOTES:
// ------------------------------------------------------------------------------------------------
// This implementation uses:
// 1. Poroelasticity theory for coupled thermal-mechanical-hydraulic processes (Kolditz et al., 2012)
// 2. Finite difference method for stress-strain calculations (Rutqvist, 2011)
// 3. Thermal stress from temperature changes (Bai et al., 2017; Zhao et al., 2015)
// 4. SIMD (AVX2) vectorization for CPU performance (custom implementation)
// 5. OpenCL 1.2 GPU acceleration via Silk.NET (custom implementation)
// ================================================================================================

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using GeoscientistToolkit.OpenCL;
using GeoscientistToolkit.Util;
using Silk.NET.OpenCL;

namespace GeoscientistToolkit.Analysis.Geothermal;

/// <summary>
///     Geomechanics solver for coupled thermo-hydro-mechanical (THM) processes.
///     Calculates stress, strain, and deformation from temperature and pressure changes.
///     Supports SIMD (AVX2) CPU optimization and OpenCL 1.2 GPU acceleration.
/// </summary>
public class GeomechanicsSolver : IDisposable
{
    private readonly CL? _cl;
    private readonly int _nr, _nth, _nz;

    // Material properties
    private readonly float[] _youngsModulus;
    private readonly float[] _poissonsRatio;
    private readonly float[] _thermalExpansion;
    private readonly float[] _biotCoefficient;

    // Computed fields
    private readonly float[] _stressXX;
    private readonly float[] _stressYY;
    private readonly float[] _stressZZ;
    private readonly float[] _stressXY;
    private readonly float[] _stressYZ;
    private readonly float[] _stressXZ;

    private readonly float[] _strainXX;
    private readonly float[] _strainYY;
    private readonly float[] _strainZZ;

    private readonly float[] _displacement;
    private readonly float[] _vonMisesStress;

    // OpenCL resources
    private nint _context;
    private nint _queue;
    private nint _device;
    private nint _program;

    private nint _geomechanicsKernel;
    private nint _stressReductionKernel;

    private nint _temperatureBuffer;
    private nint _temperatureOldBuffer;
    private nint _pressureBuffer;
    private nint _youngsModulusBuffer;
    private nint _poissonsRatioBuffer;
    private nint _thermalExpansionBuffer;
    private nint _biotCoefficientBuffer;
    private nint _stressBuffer;
    private nint _strainBuffer;
    private nint _displacementBuffer;
    private nint _vonMisesBuffer;

    private bool _useOpenCL;
    private bool _isOpenCLInitialized;

    public GeomechanicsSolver(int nr, int nth, int nz, bool useGPU = false)
    {
        _nr = nr;
        _nth = nth;
        _nz = nz;

        int totalSize = nr * nth * nz;

        // Allocate material property arrays
        _youngsModulus = new float[totalSize];
        _poissonsRatio = new float[totalSize];
        _thermalExpansion = new float[totalSize];
        _biotCoefficient = new float[totalSize];

        // Allocate stress arrays
        _stressXX = new float[totalSize];
        _stressYY = new float[totalSize];
        _stressZZ = new float[totalSize];
        _stressXY = new float[totalSize];
        _stressYZ = new float[totalSize];
        _stressXZ = new float[totalSize];

        // Allocate strain arrays
        _strainXX = new float[totalSize];
        _strainYY = new float[totalSize];
        _strainZZ = new float[totalSize];

        // Allocate result arrays
        _displacement = new float[totalSize];
        _vonMisesStress = new float[totalSize];

        // Initialize with default rock properties (granite)
        InitializeDefaultProperties();

        // Initialize OpenCL if requested
        if (useGPU)
        {
            _cl = CL.GetApi();
            _useOpenCL = InitializeOpenCL();
        }
    }

    public bool IsOpenCLAvailable => _isOpenCLInitialized;
    public string DeviceName { get; private set; } = "";

    public float[] VonMisesStress => _vonMisesStress;
    public float[] DisplacementField => _displacement;
    public float[] StressXX => _stressXX;
    public float[] StressYY => _stressYY;
    public float[] StressZZ => _stressZZ;

    private void InitializeDefaultProperties()
    {
        // Default rock properties for granite/basalt
        // Reference: Kolditz et al. (2012), Bai et al. (2017)

        for (int i = 0; i < _youngsModulus.Length; i++)
        {
            _youngsModulus[i] = 50e9f;      // 50 GPa - typical for crystalline rocks
            _poissonsRatio[i] = 0.25f;       // Dimensionless
            _thermalExpansion[i] = 8e-6f;    // 8 × 10⁻⁶ K⁻¹
            _biotCoefficient[i] = 0.7f;      // Effective stress coefficient (0-1)
        }
    }

    /// <summary>
    ///     Set uniform material properties across the entire domain.
    ///     Use this for homogeneous rock formations.
    /// </summary>
    public void SetUniformProperties(float youngsModulusGPa, float poissonsRatio,
        float thermalExpansionCoeff, float biotCoeff = 0.7f)
    {
        ValidateMaterialProperties(youngsModulusGPa, poissonsRatio, thermalExpansionCoeff, biotCoeff);

        float youngsModulusPa = youngsModulusGPa * 1e9f;

        for (int i = 0; i < _youngsModulus.Length; i++)
        {
            _youngsModulus[i] = youngsModulusPa;
            _poissonsRatio[i] = poissonsRatio;
            _thermalExpansion[i] = thermalExpansionCoeff;
            _biotCoefficient[i] = biotCoeff;
        }
    }

    /// <summary>
    ///     Set material properties mapped from lithology layers.
    ///     Properties vary with depth according to geological stratification.
    /// </summary>
    public void SetPropertiesFromLithology(
        Data.Borehole.BoreholeDataset borehole,
        Dictionary<string, float> layerYoungsModulus,
        Dictionary<string, float> layerPoissonsRatio,
        Dictionary<string, float> layerThermalExpansion,
        Dictionary<string, float> layerBiotCoefficient,
        GeothermalMesh mesh)
    {
        // Default values for unknown lithologies
        const float defaultYoungsGPa = 50.0f;
        const float defaultPoissons = 0.25f;
        const float defaultThermalExp = 8e-6f;
        const float defaultBiot = 0.7f;

        for (int iz = 0; iz < _nz; iz++)
        {
            for (int ith = 0; ith < _nth; ith++)
            {
                for (int ir = 0; ir < _nr; ir++)
                {
                    int idx = ir + ith * _nr + iz * _nr * _nth;

                    // Get depth from mesh Z coordinate
                    float depth = -mesh.Z[iz]; // Z is negative below surface

                    // Find lithology layer at this depth
                    var layer = borehole.Lithology?.FirstOrDefault(l =>
                        depth >= l.DepthFrom && depth < l.DepthTo);

                    string layerName = layer?.RockType ?? "Unknown";

                    // Get properties from dictionaries or use defaults
                    float youngsGPa = layerYoungsModulus.GetValueOrDefault(layerName, defaultYoungsGPa);
                    float poissons = layerPoissonsRatio.GetValueOrDefault(layerName, defaultPoissons);
                    float thermalExp = layerThermalExpansion.GetValueOrDefault(layerName, defaultThermalExp);
                    float biot = layerBiotCoefficient.GetValueOrDefault(layerName, defaultBiot);

                    // Validate and clamp to safe ranges
                    youngsGPa = Math.Max(0.1f, youngsGPa); // Min 0.1 GPa
                    poissons = Math.Clamp(poissons, 0.0f, 0.49f);
                    thermalExp = Math.Max(0.0f, thermalExp);
                    biot = Math.Clamp(biot, 0.0f, 1.0f);

                    // Assign to arrays
                    _youngsModulus[idx] = youngsGPa * 1e9f; // Convert GPa to Pa
                    _poissonsRatio[idx] = poissons;
                    _thermalExpansion[idx] = thermalExp;
                    _biotCoefficient[idx] = biot;
                }
            }
        }
    }

    /// <summary>
    ///     Validate material property ranges.
    /// </summary>
    private void ValidateMaterialProperties(float youngsModulusGPa, float poissonsRatio,
        float thermalExpansionCoeff, float biotCoeff)
    {
        if (youngsModulusGPa <= 0)
            throw new ArgumentException($"Young's modulus must be positive, got {youngsModulusGPa} GPa");

        if (poissonsRatio < 0.0f || poissonsRatio >= 0.5f)
            throw new ArgumentException($"Poisson's ratio must be in range [0, 0.5), got {poissonsRatio}");

        if (thermalExpansionCoeff < 0)
            throw new ArgumentException($"Thermal expansion coefficient must be non-negative, got {thermalExpansionCoeff}");

        if (biotCoeff < 0.0f || biotCoeff > 1.0f)
            throw new ArgumentException($"Biot coefficient must be in range [0, 1], got {biotCoeff}");
    }

    /// <summary>
    ///     DEPRECATED: Use SetUniformProperties() instead.
    ///     Kept for backward compatibility.
    /// </summary>
    [Obsolete("Use SetUniformProperties() for uniform properties or SetPropertiesFromLithology() for layered properties")]
    public void SetMaterialProperties(string lithology, float youngsModulusGPa,
        float poissonsRatio, float thermalExpansionCoeff, float biotCoeff = 0.7f)
    {
        SetUniformProperties(youngsModulusGPa, poissonsRatio, thermalExpansionCoeff, biotCoeff);
    }

    /// <summary>
    ///     Solve geomechanics using scalar implementation (baseline).
    /// </summary>
    public void SolveScalar(float[] temperature, float[] temperatureOld,
        float[] pressure, float dt)
    {
        for (int iz = 0; iz < _nz; iz++)
        {
            for (int ith = 0; ith < _nth; ith++)
            {
                for (int ir = 0; ir < _nr; ir++)
                {
                    int idx = ir + ith * _nr + iz * _nr * _nth;

                    // Calculate temperature change
                    float dT = temperature[idx] - temperatureOld[idx];

                    // Thermal strain (isotropic expansion)
                    float thermalStrain = _thermalExpansion[idx] * dT;

                    // Pore pressure effect (Biot's theory)
                    float pressureStrain = _biotCoefficient[idx] * pressure[idx] / _youngsModulus[idx];

                    // Total volumetric strain
                    float volumetricStrain = 3.0f * thermalStrain + pressureStrain;

                    // Simplified stress calculation (assuming constrained conditions)
                    // σ = E/(1-2ν) * [ν*ε_vol*δ_ij + (1-2ν)*ε_ij]
                    // For thermal loading with constraint, we use simplified form

                    float E = _youngsModulus[idx];
                    float nu = _poissonsRatio[idx];

                    // BUGFIX: Prevent division by zero when nu → 0.5 (incompressible material)
                    nu = Math.Clamp(nu, 0.0f, 0.49f);
                    float factor = E / (1.0f - 2.0f * nu);

                    // Constrained thermal stress (rock is confined)
                    float thermalStress = factor * nu * thermalStrain;
                    float pressureStress = -_biotCoefficient[idx] * pressure[idx];

                    // Update stress components (assuming isotropic conditions)
                    _stressXX[idx] = thermalStress + pressureStress;
                    _stressYY[idx] = thermalStress + pressureStress;
                    _stressZZ[idx] = thermalStress + pressureStress;

                    // Shear stresses are zero for isotropic thermal loading
                    _stressXY[idx] = 0;
                    _stressYZ[idx] = 0;
                    _stressXZ[idx] = 0;

                    // Update strain
                    _strainXX[idx] = thermalStrain;
                    _strainYY[idx] = thermalStrain;
                    _strainZZ[idx] = thermalStrain;

                    // Von Mises stress (for failure analysis)
                    // σ_vm = sqrt(0.5 * ((σ_xx - σ_yy)² + (σ_yy - σ_zz)² + (σ_zz - σ_xx)² + 6*(τ_xy² + τ_yz² + τ_xz²)))
                    float sxx = _stressXX[idx];
                    float syy = _stressYY[idx];
                    float szz = _stressZZ[idx];

                    _vonMisesStress[idx] = MathF.Sqrt(0.5f * (
                        (sxx - syy) * (sxx - syy) +
                        (syy - szz) * (syy - szz) +
                        (szz - sxx) * (szz - sxx)));

                    // Displacement (simplified - volumetric expansion)
                    _displacement[idx] += volumetricStrain * dt;
                }
            }
        }
    }

    /// <summary>
    ///     Solve geomechanics using SIMD (AVX2) vectorization.
    /// </summary>
    public void SolveSIMD(float[] temperature, float[] temperatureOld,
        float[] pressure, float dt)
    {
        if (!Avx2.IsSupported)
        {
            SolveScalar(temperature, temperatureOld, pressure, dt);
            return;
        }

        int vectorSize = Vector256<float>.Count; // 8 floats
        int totalSize = _nr * _nth * _nz;
        int vectorCount = totalSize / vectorSize;
        int remainder = totalSize % vectorSize;

        unsafe
        {
            fixed (float* pTemp = temperature)
            fixed (float* pTempOld = temperatureOld)
            fixed (float* pPress = pressure)
            fixed (float* pYoungs = _youngsModulus)
            fixed (float* pPoisson = _poissonsRatio)
            fixed (float* pThermalExp = _thermalExpansion)
            fixed (float* pBiot = _biotCoefficient)
            fixed (float* pStressXX = _stressXX)
            fixed (float* pStressYY = _stressYY)
            fixed (float* pStressZZ = _stressZZ)
            fixed (float* pStrainXX = _strainXX)
            fixed (float* pStrainYY = _strainYY)
            fixed (float* pStrainZZ = _strainZZ)
            fixed (float* pVonMises = _vonMisesStress)
            fixed (float* pDisp = _displacement)
            {
                Vector256<float> v3 = Vector256.Create(3.0f);
                Vector256<float> v05 = Vector256.Create(0.5f);
                Vector256<float> v1 = Vector256.Create(1.0f);
                Vector256<float> v2 = Vector256.Create(2.0f);
                Vector256<float> vDt = Vector256.Create(dt);

                for (int i = 0; i < vectorCount; i++)
                {
                    int idx = i * vectorSize;

                    // Load vectors
                    Vector256<float> vTemp = Avx.LoadVector256(pTemp + idx);
                    Vector256<float> vTempOld = Avx.LoadVector256(pTempOld + idx);
                    Vector256<float> vPress = Avx.LoadVector256(pPress + idx);
                    Vector256<float> vYoungs = Avx.LoadVector256(pYoungs + idx);
                    Vector256<float> vPoisson = Avx.LoadVector256(pPoisson + idx);
                    Vector256<float> vThermalExp = Avx.LoadVector256(pThermalExp + idx);
                    Vector256<float> vBiot = Avx.LoadVector256(pBiot + idx);

                    // Calculate temperature change: dT = T - T_old
                    Vector256<float> vDT = Avx.Subtract(vTemp, vTempOld);

                    // Thermal strain: α * dT
                    Vector256<float> vThermalStrain = Avx.Multiply(vThermalExp, vDT);

                    // Pressure strain: b * P / E
                    Vector256<float> vPressureStrain = Avx.Divide(
                        Avx.Multiply(vBiot, vPress), vYoungs);

                    // Volumetric strain: 3*α*dT + b*P/E
                    Vector256<float> vVolStrain = Avx.Add(
                        Avx.Multiply(v3, vThermalStrain), vPressureStrain);

                    // Stress factor: E / (1 - 2ν)
                    Vector256<float> vStressFactor = Avx.Divide(vYoungs,
                        Avx.Subtract(v1, Avx.Multiply(v2, vPoisson)));

                    // Thermal stress: factor * ν * α * dT
                    Vector256<float> vThermalStress = Avx.Multiply(
                        Avx.Multiply(vStressFactor, vPoisson), vThermalStrain);

                    // Pressure stress: -b * P
                    Vector256<float> vPressureStress = Avx.Multiply(
                        Avx.Multiply(vBiot, vPress), Vector256.Create(-1.0f));

                    // Total stress
                    Vector256<float> vStress = Avx.Add(vThermalStress, vPressureStress);

                    // Store stress components
                    Avx.Store(pStressXX + idx, vStress);
                    Avx.Store(pStressYY + idx, vStress);
                    Avx.Store(pStressZZ + idx, vStress);

                    // Store strain components
                    Avx.Store(pStrainXX + idx, vThermalStrain);
                    Avx.Store(pStrainYY + idx, vThermalStrain);
                    Avx.Store(pStrainZZ + idx, vThermalStrain);

                    // Von Mises stress (for isotropic stress, von Mises = 0)
                    // But we store absolute stress as indicator
                    Vector256<float> vVonMises = Avx.Multiply(
                        Avx.Sqrt(Avx.Multiply(vStress, vStress)), v05);
                    Avx.Store(pVonMises + idx, vVonMises);

                    // Update displacement
                    Vector256<float> vDispOld = Avx.LoadVector256(pDisp + idx);
                    Vector256<float> vDispNew = Avx.Add(vDispOld,
                        Avx.Multiply(vVolStrain, vDt));
                    Avx.Store(pDisp + idx, vDispNew);
                }

                // Handle remainder
                for (int i = vectorCount * vectorSize; i < totalSize; i++)
                {
                    float dT = pTemp[i] - pTempOld[i];
                    float thermalStrain = pThermalExp[i] * dT;
                    float pressureStrain = pBiot[i] * pPress[i] / pYoungs[i];
                    float volumetricStrain = 3.0f * thermalStrain + pressureStrain;

                    float E = pYoungs[i];
                    float nu = Math.Clamp(pPoisson[i], 0.0f, 0.49f); // BUGFIX: Prevent division by zero
                    float factor = E / (1.0f - 2.0f * nu);

                    float thermalStress = factor * nu * thermalStrain;
                    float pressureStress = -pBiot[i] * pPress[i];

                    pStressXX[i] = thermalStress + pressureStress;
                    pStressYY[i] = thermalStress + pressureStress;
                    pStressZZ[i] = thermalStress + pressureStress;

                    pStrainXX[i] = thermalStrain;
                    pStrainYY[i] = thermalStrain;
                    pStrainZZ[i] = thermalStrain;

                    pVonMises[i] = MathF.Abs(pStressXX[i]) * 0.5f;
                    pDisp[i] += volumetricStrain * dt;
                }
            }
        }
    }

    /// <summary>
    ///     Solve geomechanics using OpenCL GPU acceleration.
    /// </summary>
    public unsafe void SolveGPU(float[] temperature, float[] temperatureOld,
        float[] pressure, float dt)
    {
        if (!_isOpenCLInitialized || _cl == null)
        {
            SolveSIMD(temperature, temperatureOld, pressure, dt);
            return;
        }

        int totalSize = _nr * _nth * _nz;
        int err;

        // Upload data to GPU
        fixed (float* pTemp = temperature)
        fixed (float* pTempOld = temperatureOld)
        fixed (float* pPress = pressure)
        fixed (float* pDisp = _displacement)
        {
            err = _cl.EnqueueWriteBuffer(_queue, _temperatureBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), pTemp, 0, null, null);
            CheckCLError(err, "Upload temperature buffer");

            err = _cl.EnqueueWriteBuffer(_queue, _temperatureOldBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), pTempOld, 0, null, null);
            CheckCLError(err, "Upload temperature_old buffer");

            err = _cl.EnqueueWriteBuffer(_queue, _pressureBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), pPress, 0, null, null);
            CheckCLError(err, "Upload pressure buffer");

            // BUGFIX: Upload current displacement buffer (it's read-write, accumulates over time)
            err = _cl.EnqueueWriteBuffer(_queue, _displacementBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), pDisp, 0, null, null);
            CheckCLError(err, "Upload displacement buffer");
        }

        // Set kernel arguments
        // BUGFIX: Create local copies of readonly fields to take their address
        int nr = _nr;
        int nth = _nth;
        int nz = _nz;
        var temperatureBuffer = _temperatureBuffer;
        var temperatureOldBuffer = _temperatureOldBuffer;
        var pressureBuffer = _pressureBuffer;
        var youngsModulusBuffer = _youngsModulusBuffer;
        var poissonsRatioBuffer= _poissonsRatioBuffer;
        var thermalExpansionBuffer = _thermalExpansionBuffer;
        var biotCoefficientBuffer = _biotCoefficientBuffer;
        var stressBuffer = _stressBuffer;
        var strainBuffer = _strainBuffer;
        var vonMisesBuffer = _vonMisesBuffer;
        var displacementBuffer = _displacementBuffer;
        
        err = _cl.SetKernelArg(_geomechanicsKernel, 0, (nuint)sizeof(nint), &temperatureBuffer);
        CheckCLError(err, "Set kernel arg 0 (temperature)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 1, (nuint)sizeof(nint), &temperatureOldBuffer);
        CheckCLError(err, "Set kernel arg 1 (temperature_old)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 2, (nuint)sizeof(nint), &pressureBuffer);
        CheckCLError(err, "Set kernel arg 2 (pressure)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 3, (nuint)sizeof(nint), &youngsModulusBuffer);
        CheckCLError(err, "Set kernel arg 3 (Young's modulus)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 4, (nuint)sizeof(nint), &poissonsRatioBuffer);
        CheckCLError(err, "Set kernel arg 4 (Poisson's ratio)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 5, (nuint)sizeof(nint), &thermalExpansionBuffer);
        CheckCLError(err, "Set kernel arg 5 (thermal expansion)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 6, (nuint)sizeof(nint), &biotCoefficientBuffer);
        CheckCLError(err, "Set kernel arg 6 (Biot coefficient)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 7, (nuint)sizeof(nint), &stressBuffer);
        CheckCLError(err, "Set kernel arg 7 (stress)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 8, (nuint)sizeof(nint), &strainBuffer);
        CheckCLError(err, "Set kernel arg 8 (strain)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 9, (nuint)sizeof(nint), &vonMisesBuffer);
        CheckCLError(err, "Set kernel arg 9 (von Mises)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 10, (nuint)sizeof(nint), &displacementBuffer);
        CheckCLError(err, "Set kernel arg 10 (displacement)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 11, (nuint)sizeof(float), &dt);
        CheckCLError(err, "Set kernel arg 11 (dt)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 12, (nuint)sizeof(int), &nr);
        CheckCLError(err, "Set kernel arg 12 (nr)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 13, (nuint)sizeof(int), &nth);
        CheckCLError(err, "Set kernel arg 13 (nth)");
        err = _cl.SetKernelArg(_geomechanicsKernel, 14, (nuint)sizeof(int), &nz);
        CheckCLError(err, "Set kernel arg 14 (nz)");

        // Execute kernel
        nuint globalSize = (nuint)totalSize;
        nuint localSize = 256;
        err = _cl.EnqueueNdrangeKernel(_queue, _geomechanicsKernel, 1, null, &globalSize,
            &localSize, 0, null, null);
        CheckCLError(err, "Execute geomechanics kernel");

        // BUGFIX: Read results from GPU into temporary packed buffers, then unpack
        // GPU stores stress/strain as: [XX0, XX1, ..., YY0, YY1, ..., ZZ0, ZZ1, ...]
        float[] stressPacked = new float[totalSize * 3];
        float[] strainPacked = new float[totalSize * 3];

        fixed (float* pStressPacked = stressPacked)
        fixed (float* pStrainPacked = strainPacked)
        fixed (float* pVonMises = _vonMisesStress)
        fixed (float* pDisp = _displacement)
        {
            // Read packed stress and strain buffers
            err = _cl.EnqueueReadBuffer(_queue, _stressBuffer, true, 0,
                (nuint)(totalSize * 3 * sizeof(float)), pStressPacked, 0, null, null);
            CheckCLError(err, "Read stress buffer");

            err = _cl.EnqueueReadBuffer(_queue, _strainBuffer, true, 0,
                (nuint)(totalSize * 3 * sizeof(float)), pStrainPacked, 0, null, null);
            CheckCLError(err, "Read strain buffer");

            // Read von Mises stress
            err = _cl.EnqueueReadBuffer(_queue, _vonMisesBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), pVonMises, 0, null, null);
            CheckCLError(err, "Read von Mises buffer");

            // Read displacement
            err = _cl.EnqueueReadBuffer(_queue, _displacementBuffer, true, 0,
                (nuint)(totalSize * sizeof(float)), pDisp, 0, null, null);
            CheckCLError(err, "Read displacement buffer");
        }

        _cl.Finish(_queue);

        // Unpack stress and strain components into separate arrays
        for (int i = 0; i < totalSize; i++)
        {
            _stressXX[i] = stressPacked[i];
            _stressYY[i] = stressPacked[totalSize + i];
            _stressZZ[i] = stressPacked[2 * totalSize + i];
            _strainXX[i] = strainPacked[i];
            _strainYY[i] = strainPacked[totalSize + i];
            _strainZZ[i] = strainPacked[2 * totalSize + i];
        }
    }

    /// <summary>
    ///     Check OpenCL error code and throw exception if error occurred.
    /// </summary>
    private void CheckCLError(int err, string operation)
    {
        if (err != 0)
        {
            throw new InvalidOperationException($"OpenCL error in {operation}: Error code {err}");
        }
    }

    private unsafe bool InitializeOpenCL()
    {
        if (_cl == null) return false;

        try
        {
            
            _device = OpenCLDeviceManager.GetComputeDevice();

            if (_device == nint.Zero)
                return false;

            int err;

            // Get device info
            DeviceName = OpenCLDeviceManager.GetDeviceInfo().Name;
            var device = _device;
            // Create context
            _context = _cl.CreateContext(null, 1, &device, null, null, &err);
            if (err != 0) return false;

            // Create command queue
            _queue = _cl.CreateCommandQueue(_context, _device, (CommandQueueProperties)0, &err);
            if (err != 0) return false;

            // Create and compile program
            string kernelSource = GetGeomechanicsKernelSource();
            byte[] sourceBytes = Encoding.UTF8.GetBytes(kernelSource);

            fixed (byte* pSource = sourceBytes)
            {
                byte** ppSource = &pSource;
                nuint sourceLength = (nuint)sourceBytes.Length;

                _program = _cl.CreateProgramWithSource(_context, 1, ppSource, &sourceLength, &err);
                if (err != 0) return false;
            }

            nint device = _device; // Create local copy to take address
            err = _cl.BuildProgram(_program, 1, &device, null, null, null);
            if (err != 0)
            {
                // Get build log for debugging
                nuint logSize;
                _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.BuildLog, 0, null, &logSize);
                byte[] log = new byte[logSize];
                fixed (byte* pLog = log)
                {
                    _cl.GetProgramBuildInfo(_program, _device, (uint)ProgramBuildInfo.BuildLog, logSize, pLog, null);
                }
                return false;
            }

            // Create kernels
            _geomechanicsKernel = _cl.CreateKernel(_program, "geomechanics_kernel", &err);
            if (err != 0) return false;

            // Create buffers
            int totalSize = _nr * _nth * _nz;

            _temperatureBuffer = _cl.CreateBuffer(_context, (MemFlags)MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &err);
            _temperatureOldBuffer = _cl.CreateBuffer(_context, (MemFlags)MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &err);
            _pressureBuffer = _cl.CreateBuffer(_context, (MemFlags)MemFlags.ReadOnly,
                (nuint)(totalSize * sizeof(float)), null, &err);

            // Material properties
            fixed (float* pYoungs = _youngsModulus)
            fixed (float* pPoisson = _poissonsRatio)
            fixed (float* pThermalExp = _thermalExpansion)
            fixed (float* pBiot = _biotCoefficient)
            {
                _youngsModulusBuffer = _cl.CreateBuffer(_context,
                    (MemFlags)(MemFlags.ReadOnly | MemFlags.CopyHostPtr),
                    (nuint)(totalSize * sizeof(float)), pYoungs, &err);
                _poissonsRatioBuffer = _cl.CreateBuffer(_context,
                    (MemFlags)(MemFlags.ReadOnly | MemFlags.CopyHostPtr),
                    (nuint)(totalSize * sizeof(float)), pPoisson, &err);
                _thermalExpansionBuffer = _cl.CreateBuffer(_context,
                    (MemFlags)(MemFlags.ReadOnly | MemFlags.CopyHostPtr),
                    (nuint)(totalSize * sizeof(float)), pThermalExp, &err);
                _biotCoefficientBuffer = _cl.CreateBuffer(_context,
                    (MemFlags)(MemFlags.ReadOnly | MemFlags.CopyHostPtr),
                    (nuint)(totalSize * sizeof(float)), pBiot, &err);
            }

            // Result buffers (stress stored as 3*totalSize: XX, YY, ZZ)
            _stressBuffer = _cl.CreateBuffer(_context, (MemFlags)MemFlags.ReadWrite,
                (nuint)(totalSize * 3 * sizeof(float)), null, &err);
            _strainBuffer = _cl.CreateBuffer(_context, (MemFlags)MemFlags.ReadWrite,
                (nuint)(totalSize * 3 * sizeof(float)), null, &err);
            _vonMisesBuffer = _cl.CreateBuffer(_context, (MemFlags)MemFlags.WriteOnly,
                (nuint)(totalSize * sizeof(float)), null, &err);
            _displacementBuffer = _cl.CreateBuffer(_context, (MemFlags)MemFlags.ReadWrite,
                (nuint)(totalSize * sizeof(float)), null, &err);

            _isOpenCLInitialized = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetGeomechanicsKernelSource()
    {
        return @"
// OpenCL 1.2 Geomechanics Kernel
// Implements thermo-hydro-mechanical coupling for geothermal systems

__kernel void geomechanics_kernel(
    __global const float* temperature,
    __global const float* temperature_old,
    __global const float* pressure,
    __global const float* youngs_modulus,
    __global const float* poissons_ratio,
    __global const float* thermal_expansion,
    __global const float* biot_coefficient,
    __global float* stress,           // Output: stress components (XX, YY, ZZ interleaved)
    __global float* strain,           // Output: strain components (XX, YY, ZZ interleaved)
    __global float* von_mises_stress, // Output: von Mises stress
    __global float* displacement,     // Input/Output: displacement field
    const float dt,
    const int nr,
    const int nth,
    const int nz)
{
    int gid = get_global_id(0);
    int total_size = nr * nth * nz;

    if (gid >= total_size)
        return;

    // Load material properties
    float E = youngs_modulus[gid];
    float nu = poissons_ratio[gid];
    float alpha = thermal_expansion[gid];
    float biot = biot_coefficient[gid];

    // BUGFIX: Clamp Poisson's ratio to prevent division by zero
    nu = clamp(nu, 0.0f, 0.49f);

    // Calculate temperature change
    float dT = temperature[gid] - temperature_old[gid];

    // Thermal strain (isotropic expansion)
    float thermal_strain = alpha * dT;

    // Pore pressure effect (Biot's theory)
    float pressure_strain = biot * pressure[gid] / E;

    // Volumetric strain
    float volumetric_strain = 3.0f * thermal_strain + pressure_strain;

    // Stress calculation (constrained thermal expansion)
    // σ = E/(1-2ν) * ν * α * dT - b * P
    float stress_factor = E / (1.0f - 2.0f * nu);
    float thermal_stress = stress_factor * nu * thermal_strain;
    float pressure_stress = -biot * pressure[gid];

    float total_stress = thermal_stress + pressure_stress;

    // Store stress components (isotropic)
    stress[gid] = total_stress;                    // XX
    stress[gid + total_size] = total_stress;       // YY
    stress[gid + 2 * total_size] = total_stress;   // ZZ

    // Store strain components
    strain[gid] = thermal_strain;                  // XX
    strain[gid + total_size] = thermal_strain;     // YY
    strain[gid + 2 * total_size] = thermal_strain; // ZZ

    // Von Mises stress (for isotropic stress state)
    // For σ_xx = σ_yy = σ_zz, von Mises = 0, but we use absolute value as indicator
    von_mises_stress[gid] = fabs(total_stress) * 0.5f;

    // Update displacement
    displacement[gid] += volumetric_strain * dt;
}
";
    }

    public void Dispose()
    {
        if (_cl != null && _isOpenCLInitialized)
        {
            if (_temperatureBuffer != 0) _cl.ReleaseMemObject(_temperatureBuffer);
            if (_temperatureOldBuffer != 0) _cl.ReleaseMemObject(_temperatureOldBuffer);
            if (_pressureBuffer != 0) _cl.ReleaseMemObject(_pressureBuffer);
            if (_youngsModulusBuffer != 0) _cl.ReleaseMemObject(_youngsModulusBuffer);
            if (_poissonsRatioBuffer != 0) _cl.ReleaseMemObject(_poissonsRatioBuffer);
            if (_thermalExpansionBuffer != 0) _cl.ReleaseMemObject(_thermalExpansionBuffer);
            if (_biotCoefficientBuffer != 0) _cl.ReleaseMemObject(_biotCoefficientBuffer);
            if (_stressBuffer != 0) _cl.ReleaseMemObject(_stressBuffer);
            if (_strainBuffer != 0) _cl.ReleaseMemObject(_strainBuffer);
            if (_vonMisesBuffer != 0) _cl.ReleaseMemObject(_vonMisesBuffer);
            if (_displacementBuffer != 0) _cl.ReleaseMemObject(_displacementBuffer);

            if (_geomechanicsKernel != 0) _cl.ReleaseKernel(_geomechanicsKernel);
            if (_stressReductionKernel != 0) _cl.ReleaseKernel(_stressReductionKernel);

            if (_program != 0) _cl.ReleaseProgram(_program);
            if (_queue != 0) _cl.ReleaseCommandQueue(_queue);
            if (_context != 0) _cl.ReleaseContext(_context);

            _cl?.Dispose();
        }
    }
}
