// GeoscientistToolkit/Analysis/Geomechanics/GeomechanicalSimulatorCPU_FluidGeothermal.cs
// Partial class extension for geothermal and hydraulic fracturing simulations

using System.Numerics;
using System.Runtime.CompilerServices;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Analysis.Geomechanics;

public partial class GeomechanicalSimulatorCPU
{
    private readonly List<float> _energyExtractionHistory = new();
    private readonly List<float> _flowRateHistory = new();
    private readonly List<float> _fractureVolumeHistory = new();
    private readonly List<float> _injectionPressureHistory = new();

    // Time series storage
    private readonly List<float> _timePoints = new();
    private float[,,] _fluidSaturation;
    private float[,,] _fractureAperture;
    private bool[,,] _isConnectedToInjection;
    private float[,,] _pressure;
    private float[,,] _temperature;

    private void InitializeGeothermalAndFluid(byte[,,] labels, BoundingBox extent)
    {
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;

        Logger.Log("[GeomechCPU] Initializing geothermal and fluid fields...");

        // Initialize temperature field with geothermal gradient
        if (_params.EnableGeothermal)
        {
            _temperature = new float[w, h, d];
            var dx = _params.PixelSize / 1e6f; // m

            Parallel.For(0, d, z =>
            {
                // Depth in meters from surface (assuming Z is vertical)
                var depth_m = z * dx;
                var temp = _params.SurfaceTemperature + _params.GeothermalGradient / 1000f * depth_m;

                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (labels[x, y, z] != 0) // Only in material
                        _temperature[x, y, z] = temp;
            });

            Logger.Log($"[GeomechCPU] Temperature range: {_params.SurfaceTemperature:F1}°C to " +
                       $"{_params.SurfaceTemperature + _params.GeothermalGradient / 1000f * d * dx:F1}°C");
        }

        // Initialize pressure field
        if (_params.EnableFluidInjection || _params.UsePorePressure)
        {
            _pressure = new float[w, h, d];
            _fluidSaturation = new float[w, h, d];
            _fractureAperture = new float[w, h, d];
            _isConnectedToInjection = new bool[w, h, d];

            var P0 = _params.InitialPorePressure * 1e6f; // Pa
            var dx = _params.PixelSize / 1e6f;
            var rho_water = 1000f; // kg/m³
            var g = 9.81f; // m/s²

            Parallel.For(0, d, z =>
            {
                var depth_m = z * dx;
                var hydrostaticP = P0 + rho_water * g * depth_m;

                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (labels[x, y, z] != 0)
                    {
                        _pressure[x, y, z] = hydrostaticP;
                        _fluidSaturation[x, y, z] = _params.Porosity; // Initial saturation
                    }
                    else if (_params.EnableAquifer)
                    {
                        // Aquifer boundary condition on exterior
                        _pressure[x, y, z] = _params.AquiferPressure * 1e6f;
                    }
            });

            Logger.Log($"[GeomechCPU] Initial pressure: {P0 / 1e6f:F1} MPa (hydrostatic)");
        }
    }

    private void SimulateFluidInjectionAndFracturing(GeomechanicalResults results, byte[,,] labels,
        IProgress<float> progress, CancellationToken token)
    {
        if (!_params.EnableFluidInjection) return;

        Logger.Log("[GeomechCPU] ========== FLUID INJECTION & FRACTURING ==========");

        var extent = _params.SimulationExtent;
        var w = extent.Width;
        var h = extent.Height;
        var d = extent.Depth;
        var dx = _params.PixelSize / 1e6f; // m

        // Calculate injection voxel location
        var injX = (int)(_params.InjectionLocation.X * w);
        var injY = (int)(_params.InjectionLocation.Y * h);
        var injZ = (int)(_params.InjectionLocation.Z * d);

        injX = Math.Clamp(injX, 0, w - 1);
        injY = Math.Clamp(injY, 0, h - 1);
        injZ = Math.Clamp(injZ, 0, d - 1);

        Logger.Log($"[GeomechCPU] Injection point: ({injX}, {injY}, {injZ})");

        var P_inj = _params.InjectionPressure * 1e6f; // Pa
        var Q_inj = _params.InjectionRate; // m³/s
        var dt_fluid = _params.FluidTimeStep; // s
        var maxTime = _params.MaxSimulationTime; // s
        var numSteps = (int)(maxTime / dt_fluid);

        var breakdownDetected = false;
        var breakdownTime = 0f;
        results.BreakdownPressure = 0f;

        Logger.Log($"[GeomechCPU] Simulating {numSteps} fluid time steps (dt={dt_fluid}s)");

        for (var step = 0; step < numSteps; step++)
        {
            token.ThrowIfCancellationRequested();

            var currentTime = step * dt_fluid;

            // Apply injection source
            ApplyInjectionSource(injX, injY, injZ, P_inj, Q_inj, dt_fluid, dx, labels);

            // Diffuse pressure through porous media
            for (var subStep = 0; subStep < _params.FluidIterationsPerMechanicalStep; subStep++)
                DiffusePressure(labels, dx, dt_fluid / _params.FluidIterationsPerMechanicalStep);

            // Update effective stress
            UpdateEffectiveStress(results, labels);

            // Check for new fractures
            var newFractureCount = DetectAndPropagateFractures(results, labels, dx);

            if (!breakdownDetected && newFractureCount > 0)
            {
                breakdownDetected = true;
                breakdownTime = currentTime;
                results.BreakdownPressure = _pressure[injX, injY, injZ] / 1e6f;
                Logger.Log(
                    $"[GeomechCPU] *** BREAKDOWN at t={breakdownTime:F1}s, P={results.BreakdownPressure:F1} MPa ***");
            }

            // Update fracture apertures based on stress
            if (_params.EnableFractureFlow)
                UpdateFractureApertures(results, labels, dx);

            // Pressure diffusion through fractures (enhanced permeability)
            if (_params.EnableFractureFlow && breakdownDetected)
                EnhancedDiffusionThroughFractures(labels, dx, dt_fluid);

            // Track connectivity from injection point
            if (step % 10 == 0)
                UpdateFractureConnectivity(injX, injY, injZ, labels);

            // Record time series
            if (step % 10 == 0)
            {
                _timePoints.Add(currentTime);
                _injectionPressureHistory.Add(_pressure[injX, injY, injZ] / 1e6f);

                var fractureVol = CalculateFractureVolume(labels, dx);
                _fractureVolumeHistory.Add(fractureVol);

                var flowRate = CalculateFlowRate(injX, injY, injZ, labels, dx);
                _flowRateHistory.Add(flowRate);

                if (_params.EnableGeothermal)
                {
                    var energyRate = CalculateEnergyExtraction(labels, dx);
                    _energyExtractionHistory.Add(energyRate);
                }
            }

            if (step % 100 == 0)
            {
                progress?.Report(0.92f + 0.08f * step / numSteps);
                Logger.Log($"[GeomechCPU] t={currentTime:F1}s, P_inj={_pressure[injX, injY, injZ] / 1e6f:F2} MPa, " +
                           $"Fractures={results.FractureVoxelCount}");
            }
        }

        // Calculate sustained propagation pressure (average after breakdown)
        if (breakdownDetected && _injectionPressureHistory.Count > 10)
        {
            var postBreakdownIdx = _timePoints.FindIndex(t => t >= breakdownTime);
            if (postBreakdownIdx >= 0 && postBreakdownIdx < _injectionPressureHistory.Count - 10)
                results.PropagationPressure = _injectionPressureHistory
                    .Skip(postBreakdownIdx)
                    .Take(20)
                    .Average();
        }

        Logger.Log(
            $"[GeomechCPU] Fluid injection complete. Final fracture volume: {results.TotalFractureVolume:F4} m³");
    }

    private void ApplyInjectionSource(int x, int y, int z, float P_inj, float Q_inj, float dt, float dx,
        byte[,,] labels)
    {
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);
        var radius = _params.InjectionRadius;

        // Inject in spherical region
        var voxelVolume = dx * dx * dx;
        var pressureIncrement = Q_inj * dt / (voxelVolume * _params.Porosity);

        for (var dz = -radius; dz <= radius; dz++)
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx_local = -radius; dx_local <= radius; dx_local++)
        {
            var nx = x + dx_local;
            var ny = y + dy;
            var nz = z + dz;

            if (nx >= 0 && nx < w && ny >= 0 && ny < h && nz >= 0 && nz < d)
                if (labels[nx, ny, nz] != 0)
                {
                    var dist = MathF.Sqrt(dx_local * dx_local + dy * dy + dz * dz);
                    if (dist <= radius)
                        // Pressure boundary condition at injection point
                        _pressure[nx, ny, nz] = P_inj;
                }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DiffusePressure(byte[,,] labels, float dx, float dt)
    {
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        // Proper poroelastic diffusion parameters
        var k = _params.RockPermeability; // m²
        var mu = _params.FluidViscosity; // Pa·s
        var phi = _params.Porosity; // fraction
        var K_s = 36e9f; // Solid grain bulk modulus (Pa) - typical for quartz
        var K_f = 2.2e9f; // Fluid bulk modulus (Pa) - water at room temp
        var K_d = _params.YoungModulus * 1e6f / (3f * (1f - 2f * _params.PoissonRatio)); // Drained bulk modulus
        var alpha = _params.BiotCoefficient; // Biot coefficient

        // Storage coefficient (proper derivation from Biot theory)
        // S = φ/K_f + (α - φ)/K_s
        var S_storage = phi / K_f + (alpha - phi) / K_s;

        // Total compressibility (fluid + pore space)
        // c_t = S_storage / φ
        var c_total = S_storage / phi;

        // Hydraulic diffusivity: D = k/(φ·μ·c_t)
        var diffusivity = k / (phi * mu * c_total);

        // Stability condition: α ≤ 0.25 for explicit scheme (3D)
        var alpha_cfl = diffusivity * dt / (dx * dx);
        if (alpha_cfl > 0.16667f) // 1/6 for 3D stability
        {
            alpha_cfl = 0.16667f;
            Logger.LogWarning($"[GeomechCPU] Fluid time step reduced for stability: α_CFL = {alpha_cfl:E3}");
        }

        var pressureNew = (float[,,])_pressure.Clone();

        Parallel.For(1, d - 1, z =>
        {
            for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
            {
                if (labels[x, y, z] == 0)
                {
                    if (_params.EnableAquifer)
                        pressureNew[x, y, z] = _params.AquiferPressure * 1e6f;
                    continue;
                }

                var P_c = _pressure[x, y, z];

                // Get neighbor pressures with boundary handling
                var P_xp = x + 1 < w && labels[x + 1, y, z] != 0 ? _pressure[x + 1, y, z] :
                    _params.EnableAquifer ? _params.AquiferPressure * 1e6f : P_c;
                var P_xm = x - 1 >= 0 && labels[x - 1, y, z] != 0 ? _pressure[x - 1, y, z] :
                    _params.EnableAquifer ? _params.AquiferPressure * 1e6f : P_c;
                var P_yp = y + 1 < h && labels[x, y + 1, z] != 0 ? _pressure[x, y + 1, z] :
                    _params.EnableAquifer ? _params.AquiferPressure * 1e6f : P_c;
                var P_ym = y - 1 >= 0 && labels[x, y - 1, z] != 0 ? _pressure[x, y - 1, z] :
                    _params.EnableAquifer ? _params.AquiferPressure * 1e6f : P_c;
                var P_zp = z + 1 < d && labels[x, y, z + 1] != 0 ? _pressure[x, y, z + 1] :
                    _params.EnableAquifer ? _params.AquiferPressure * 1e6f : P_c;
                var P_zm = z - 1 >= 0 && labels[x, y, z - 1] != 0 ? _pressure[x, y, z - 1] :
                    _params.EnableAquifer ? _params.AquiferPressure * 1e6f : P_c;

                // 7-point stencil Laplacian with gravity correction
                var rho_f = _params.FluidDensity;
                var g = 9.81f;

                // Gravity affects vertical pressure gradient
                var gravity_correction = rho_f * g * dx; // Pa/voxel in vertical direction

                var laplacian = P_xp + P_xm + P_yp + P_ym +
                    (P_zp - gravity_correction) + (P_zm + gravity_correction) - 6f * P_c;

                pressureNew[x, y, z] = P_c + alpha_cfl * laplacian;

                // Physical bounds: pressure cannot be negative
                if (pressureNew[x, y, z] < 0f)
                    pressureNew[x, y, z] = 0f;
            }
        });

        _pressure = pressureNew;
    }

    private void UpdateEffectiveStress(GeomechanicalResults results, byte[,,] labels)
    {
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        if (results.EffectiveStressXX == null)
        {
            results.EffectiveStressXX = new float[w, h, d];
            results.EffectiveStressYY = new float[w, h, d];
            results.EffectiveStressZZ = new float[w, h, d];
        }

        var alpha = _params.BiotCoefficient;

        // Terzaghi effective stress: σ'_ij = σ_ij - α·P·δ_ij
        // Only normal stresses are affected (not shear stresses)
        Parallel.For(0, d, z =>
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (labels[x, y, z] == 0) continue;

                var P = _pressure[x, y, z];
                var pore_correction = alpha * P;

                results.EffectiveStressXX[x, y, z] = results.StressXX[x, y, z] - pore_correction;
                results.EffectiveStressYY[x, y, z] = results.StressYY[x, y, z] - pore_correction;
                results.EffectiveStressZZ[x, y, z] = results.StressZZ[x, y, z] - pore_correction;

                // Shear stresses are unchanged by pore pressure
            }
        });
    }

    private int DetectAndPropagateFractures(GeomechanicalResults results, byte[,,] labels, float dx)
    {
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        var newFractureCount = 0;
        var cohesion_Pa = _params.Cohesion * 1e6f;
        var phi = _params.FrictionAngle * MathF.PI / 180f;
        var tensile_Pa = _params.TensileStrength * 1e6f;
        var alpha = _params.BiotCoefficient;

        // Fracture toughness criterion (Mode I)
        var K_Ic = _params.FractureToughness * 1e6f; // MPa·√m to Pa·√m

        Parallel.For(0, d, z =>
        {
            var localCount = 0;

            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (labels[x, y, z] == 0) continue;
                if (results.FractureField[x, y, z]) continue; // Already fractured

                var P = _pressure[x, y, z];

                // Calculate effective principal stresses (Biot-Geertsma)
                var s1_total = results.Sigma1[x, y, z];
                var s2_total = results.Sigma2[x, y, z];
                var s3_total = results.Sigma3[x, y, z];

                var s1_eff = s1_total - alpha * P;
                var s2_eff = s2_total - alpha * P;
                var s3_eff = s3_total - alpha * P;

                // Check failure with CORRECT σ₂ (not zero!)
                var failureIndex = CalculateFailureIndex(s1_eff, s2_eff, s3_eff,
                    cohesion_Pa, phi, tensile_Pa);

                // Additional check: Stress intensity factor criterion
                // For a pressurized crack: K_I = P√(πa) where a is crack half-length
                // Approximate crack size from voxel dimension
                var crack_half_length = dx / 2f;
                var delta_P = Math.Max(0, P - s3_eff); // Net pressure opening crack
                var K_I = delta_P * MathF.Sqrt(MathF.PI * crack_half_length);

                var fracture_toughness_exceeded = K_I > K_Ic;

                // Fracture if either criterion is met
                if (failureIndex >= 1.0f || fracture_toughness_exceeded)
                {
                    results.FractureField[x, y, z] = true;
                    results.DamageField[x, y, z] = 255;

                    // Initial aperture from Sneddon's solution for penny-shaped crack
                    // w = (4/π) · (1-ν²)/E · ΔP · a
                    var nu = _params.PoissonRatio;
                    var E = _params.YoungModulus * 1e6f;
                    var initial_aperture = 4f / MathF.PI * ((1f - nu * nu) / E) * delta_P * crack_half_length;
                    initial_aperture = Math.Max(initial_aperture, _params.MinimumFractureAperture);

                    _fractureAperture[x, y, z] = initial_aperture;
                    localCount++;
                }
            }

            lock (results)
            {
                newFractureCount += localCount;
            }
        });

        results.FractureVoxelCount = CountFractures(results.FractureField);
        return newFractureCount;
    }

    private void UpdateFractureApertures(GeomechanicalResults results, byte[,,] labels, float dx)
    {
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        var nu = _params.PoissonRatio;
        var E = _params.YoungModulus * 1e6f;
        var alpha = _params.BiotCoefficient;

        Parallel.For(0, d, z =>
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (!results.FractureField[x, y, z]) continue;

                var P = _pressure[x, y, z];

                // Normal stress on fracture plane (use minimum principal stress as approximation)
                var s_n_total = results.Sigma3[x, y, z];
                var s_n_eff = s_n_total - alpha * P;

                // Net pressure opening the fracture
                var delta_P = P - s_n_eff;

                if (delta_P > 0)
                {
                    // Fracture mechanical aperture (Sneddon 1946, Perkins-Kern 1961)
                    // For a pressurized fracture in elastic medium:
                    // w = (4(1-ν²)/E) · ΔP · √(L²/4 - r²)
                    // At fracture center (r=0), with L ≈ voxel size:
                    var L = dx;
                    var aperture_mechanical = 4f * (1f - nu * nu) / E * delta_P * L / 2f;

                    // Add stress-dependent component (Willis-Richards model)
                    // w = w₀ · exp(β · Δσ_n)
                    var w_residual = _params.MinimumFractureAperture;
                    var beta = 0.5f / 1e6f; // 0.5 MPa⁻¹ (typical for granite)
                    var aperture_stress = w_residual * MathF.Exp(beta * delta_P);

                    // Total aperture (mechanical + stress-dependent)
                    var aperture_total = aperture_mechanical + aperture_stress;

                    // Physical bounds
                    aperture_total = Math.Max(aperture_total, _params.MinimumFractureAperture);
                    aperture_total = Math.Min(aperture_total, dx / 10f); // Max 10% of voxel size

                    _fractureAperture[x, y, z] = aperture_total;
                }
                else
                {
                    // Fracture closing under compressive stress
                    // Residual aperture due to roughness/gouge
                    _fractureAperture[x, y, z] = _params.MinimumFractureAperture;
                }
            }
        });
    }

    private void EnhancedDiffusionThroughFractures(byte[,,] labels, float dx, float dt)
    {
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        var mu = _params.FluidViscosity;
        var rho_f = _params.FluidDensity;
        var g = 9.81f;

        // Cubic law for fracture flow: q = -w³/(12μ) · ∇P
        // Resulting in fracture permeability: k_f = w²/12

        var pressureNew = (float[,,])_pressure.Clone();

        Parallel.For(1, d - 1, z =>
        {
            for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
            {
                if (labels[x, y, z] == 0) continue;

                var P_c = _pressure[x, y, z];
                var aperture_c = _fractureAperture[x, y, z];

                // Check if this voxel or any neighbor is fractured
                var is_in_fracture_zone = aperture_c > _params.MinimumFractureAperture;

                if (!is_in_fracture_zone)
                {
                    // Check neighbors
                    if (x > 0 && _fractureAperture[x - 1, y, z] > _params.MinimumFractureAperture)
                        is_in_fracture_zone = true;
                    if (x < w - 1 && _fractureAperture[x + 1, y, z] > _params.MinimumFractureAperture)
                        is_in_fracture_zone = true;
                    if (y > 0 && _fractureAperture[x, y - 1, z] > _params.MinimumFractureAperture)
                        is_in_fracture_zone = true;
                    if (y < h - 1 && _fractureAperture[x, y + 1, z] > _params.MinimumFractureAperture)
                        is_in_fracture_zone = true;
                    if (z > 0 && _fractureAperture[x, y, z - 1] > _params.MinimumFractureAperture)
                        is_in_fracture_zone = true;
                    if (z < d - 1 && _fractureAperture[x, y, z + 1] > _params.MinimumFractureAperture)
                        is_in_fracture_zone = true;
                }

                if (!is_in_fracture_zone) continue;

                // Calculate enhanced diffusion using harmonic mean of interface permeabilities
                var flux_sum = 0f;
                var flux_count = 0;

                // X-direction
                if (x + 1 < w && labels[x + 1, y, z] != 0)
                {
                    var aperture_xp = _fractureAperture[x + 1, y, z];
                    var aperture_interface =
                        2f / (1f / (aperture_c + 1e-12f) + 1f / (aperture_xp + 1e-12f)); // Harmonic mean
                    var k_interface = aperture_interface * aperture_interface / 12f;
                    var conductivity = k_interface / mu;
                    var gradP = (_pressure[x + 1, y, z] - P_c) / dx;
                    flux_sum += conductivity * gradP;
                    flux_count++;
                }

                if (x - 1 >= 0 && labels[x - 1, y, z] != 0)
                {
                    var aperture_xm = _fractureAperture[x - 1, y, z];
                    var aperture_interface = 2f / (1f / (aperture_c + 1e-12f) + 1f / (aperture_xm + 1e-12f));
                    var k_interface = aperture_interface * aperture_interface / 12f;
                    var conductivity = k_interface / mu;
                    var gradP = (P_c - _pressure[x - 1, y, z]) / dx;
                    flux_sum += conductivity * gradP;
                    flux_count++;
                }

                // Y-direction
                if (y + 1 < h && labels[x, y + 1, z] != 0)
                {
                    var aperture_yp = _fractureAperture[x, y + 1, z];
                    var aperture_interface = 2f / (1f / (aperture_c + 1e-12f) + 1f / (aperture_yp + 1e-12f));
                    var k_interface = aperture_interface * aperture_interface / 12f;
                    var conductivity = k_interface / mu;
                    var gradP = (_pressure[x, y + 1, z] - P_c) / dx;
                    flux_sum += conductivity * gradP;
                    flux_count++;
                }

                if (y - 1 >= 0 && labels[x, y - 1, z] != 0)
                {
                    var aperture_ym = _fractureAperture[x, y - 1, z];
                    var aperture_interface = 2f / (1f / (aperture_c + 1e-12f) + 1f / (aperture_ym + 1e-12f));
                    var k_interface = aperture_interface * aperture_interface / 12f;
                    var conductivity = k_interface / mu;
                    var gradP = (P_c - _pressure[x, y - 1, z]) / dx;
                    flux_sum += conductivity * gradP;
                    flux_count++;
                }

                // Z-direction (with gravity)
                if (z + 1 < d && labels[x, y, z + 1] != 0)
                {
                    var aperture_zp = _fractureAperture[x, y, z + 1];
                    var aperture_interface = 2f / (1f / (aperture_c + 1e-12f) + 1f / (aperture_zp + 1e-12f));
                    var k_interface = aperture_interface * aperture_interface / 12f;
                    var conductivity = k_interface / mu;
                    var gradP = (_pressure[x, y, z + 1] - P_c) / dx - rho_f * g;
                    flux_sum += conductivity * gradP;
                    flux_count++;
                }

                if (z - 1 >= 0 && labels[x, y, z - 1] != 0)
                {
                    var aperture_zm = _fractureAperture[x, y, z - 1];
                    var aperture_interface = 2f / (1f / (aperture_c + 1e-12f) + 1f / (aperture_zm + 1e-12f));
                    var k_interface = aperture_interface * aperture_interface / 12f;
                    var conductivity = k_interface / mu;
                    var gradP = (P_c - _pressure[x, y, z - 1]) / dx + rho_f * g;
                    flux_sum += conductivity * gradP;
                    flux_count++;
                }

                if (flux_count > 0)
                {
                    // Conservation of mass: ∂P/∂t = -(1/(φ·c_t)) · ∇·q
                    // Simplified explicit update
                    var phi = _params.Porosity;
                    var c_t = 1e-9f; // Total compressibility (Pa⁻¹)
                    var dP_dt = -(1f / (phi * c_t)) * flux_sum / flux_count;

                    pressureNew[x, y, z] = P_c + dP_dt * dt;

                    // Stability limit
                    var max_dP = 10e6f; // 10 MPa per time step
                    if (Math.Abs(pressureNew[x, y, z] - P_c) > max_dP)
                        pressureNew[x, y, z] = P_c + Math.Sign(dP_dt) * max_dP;
                }
            }
        });

        _pressure = pressureNew;
    }

    private void UpdateFractureConnectivity(int injX, int injY, int injZ, byte[,,] labels)
    {
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        // Flood fill from injection point through fracture network
        var queue = new Queue<(int, int, int)>();
        var visited = new bool[w, h, d];

        queue.Enqueue((injX, injY, injZ));
        visited[injX, injY, injZ] = true;
        _isConnectedToInjection[injX, injY, injZ] = true;

        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();

            // Check 6 neighbors
            var neighbors = new[]
            {
                (x + 1, y, z), (x - 1, y, z),
                (x, y + 1, z), (x, y - 1, z),
                (x, y, z + 1), (x, y, z - 1)
            };

            foreach (var (nx, ny, nz) in neighbors)
            {
                if (nx < 0 || nx >= w || ny < 0 || ny >= h || nz < 0 || nz >= d) continue;
                if (visited[nx, ny, nz]) continue;
                if (labels[nx, ny, nz] == 0) continue;

                // Can traverse if fractured or high pressure gradient
                var isFractured = _fractureAperture[nx, ny, nz] > _params.MinimumFractureAperture;
                var pressureGradient = Math.Abs(_pressure[nx, ny, nz] - _pressure[x, y, z]);
                var isHighPressure = pressureGradient > 1e6f; // 1 MPa gradient

                if (isFractured || isHighPressure)
                {
                    visited[nx, ny, nz] = true;
                    _isConnectedToInjection[nx, ny, nz] = true;
                    queue.Enqueue((nx, ny, nz));
                }
            }
        }
    }

    private float CalculateFractureVolume(byte[,,] labels, float dx)
    {
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        double volume = 0;
        var voxelVolume = dx * dx * dx;

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            if (labels[x, y, z] != 0 && _fractureAperture[x, y, z] > _params.MinimumFractureAperture)
            {
                // Fracture volume = aperture * cell face area (approximation)
                var faceArea = dx * dx;
                volume += _fractureAperture[x, y, z] * faceArea;
            }

        return (float)volume;
    }

    private float CalculateFlowRate(int injX, int injY, int injZ, byte[,,] labels, float dx)
    {
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        // Calculate flow rate using Darcy's law at injection zone
        var totalFlow = 0f;
        var mu = _params.FluidViscosity;
        var radius = _params.InjectionRadius;

        for (var dz = -radius; dz <= radius; dz++)
        for (var dy = -radius; dy <= radius; dy++)
        for (var dx_local = -radius; dx_local <= radius; dx_local++)
        {
            var x = injX + dx_local;
            var y = injY + dy;
            var z = injZ + dz;

            if (x <= 0 || x >= w - 1 || y <= 0 || y >= h - 1 || z <= 0 || z >= d - 1) continue;
            if (labels[x, y, z] == 0) continue;

            var P_c = _pressure[x, y, z];

            // Pressure gradient (approximate)
            var gradP_x = (_pressure[x + 1, y, z] - _pressure[x - 1, y, z]) / (2f * dx);
            var gradP_y = (_pressure[x, y + 1, z] - _pressure[x, y - 1, z]) / (2f * dx);
            var gradP_z = (_pressure[x, y, z + 1] - _pressure[x, y, z - 1]) / (2f * dx);
            var gradP_mag = MathF.Sqrt(gradP_x * gradP_x + gradP_y * gradP_y + gradP_z * gradP_z);

            // Darcy velocity
            var k_eff = _fractureAperture[x, y, z] > _params.MinimumFractureAperture
                ? _fractureAperture[x, y, z] * _fractureAperture[x, y, z] / 12f
                : _params.RockPermeability;

            var v = k_eff / mu * gradP_mag;
            totalFlow += v * dx * dx; // Flow through area element
        }

        return totalFlow;
    }

    private float CalculateEnergyExtraction(byte[,,] labels, float dx)
    {
        if (_temperature == null) return 0f;

        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        // Energy extraction rate = mass flow rate × specific heat × ΔT
        var rho = _params.FluidDensity;
        var cp = 4182f; // J/kg·K (water)
        var T_injection = 20f; // °C (cold water injected)

        double energyRate = 0; // Watts

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (labels[x, y, z] == 0) continue;
            if (!_isConnectedToInjection[x, y, z]) continue;

            var T_rock = _temperature[x, y, z];
            var deltaT = T_rock - T_injection;

            if (deltaT > 0)
            {
                // Approximate heat transfer rate
                var voxelVolume = dx * dx * dx;
                var fluidVolume = voxelVolume * _params.Porosity;
                var mass = rho * fluidVolume;

                // Heat transfer coefficient (simplified)
                var h_transfer = 1000f; // W/m²·K (typical for forced convection)
                var surfaceArea = 6f * dx * dx; // 6 faces
                var Q_dot = h_transfer * surfaceArea * deltaT;

                energyRate += Q_dot;
            }
        }

        return (float)(energyRate / 1e6); // MW
    }

    private void PopulateGeothermalAndFluidResults(GeomechanicalResults results)
    {
        if (_temperature != null)
        {
            results.TemperatureField = _temperature;

            // Calculate geothermal gradient from simulation
            var extent = _params.SimulationExtent;
            var dx = _params.PixelSize / 1e6f;
            var depth_m = extent.Depth * dx;
            var T_top = _temperature[extent.Width / 2, extent.Height / 2, 0];
            var T_bottom = _temperature[extent.Width / 2, extent.Height / 2, extent.Depth - 1];
            results.AverageThermalGradient = (T_bottom - T_top) / depth_m * 1000f; // °C/km

            // Calculate energy potential
            if (_params.CalculateEnergyPotential)
                results.GeothermalEnergyPotential = CalculateEnergyPotential(results.MaterialLabels, dx);
        }

        if (_pressure != null)
        {
            results.PressureField = _pressure;
            results.FractureAperture = _fractureAperture;
            results.FluidSaturation = _fluidSaturation;
            results.FractureConnectivity = _isConnectedToInjection;

            // Statistics
            var (minP, maxP) = FindPressureRange(_pressure, results.MaterialLabels);
            results.MinFluidPressure = minP;
            results.MaxFluidPressure = maxP;
            results.PeakInjectionPressure = maxP / 1e6f;

            var dx = _params.PixelSize / 1e6f;
            results.TotalFractureVolume = CalculateFractureVolume(results.MaterialLabels, dx);

            // Time series
            results.TimePoints = new List<float>(_timePoints);
            results.InjectionPressureHistory = new List<float>(_injectionPressureHistory);
            results.FlowRateHistory = new List<float>(_flowRateHistory);
            results.FractureVolumeHistory = new List<float>(_fractureVolumeHistory);
            if (_params.EnableGeothermal)
                results.EnergyExtractionHistory = new List<float>(_energyExtractionHistory);

            // Generate fracture network
            results.FractureNetwork = ExtractFractureNetwork(results.MaterialLabels, dx);
        }

        Logger.Log($"[GeomechCPU] Geothermal/Fluid results populated. " +
                   $"Energy potential: {results.GeothermalEnergyPotential:F2} MWh");
    }

    private float CalculateEnergyPotential(byte[,,] labels, float dx)
    {
        if (_temperature == null) return 0f;

        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        var rho_rock = _params.Density; // kg/m³
        var cp_rock = 1000f; // J/kg·K (typical for rock)
        var T_ref = 20f; // °C (reference/extraction temperature)
        var recovery_factor = 0.1f; // 10% thermal energy recovery (realistic for EGS)

        double totalEnergy = 0; // Joules

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (labels[x, y, z] == 0) continue;

            var T = _temperature[x, y, z];
            if (T > T_ref)
            {
                var voxelVolume = dx * dx * dx;
                var mass = rho_rock * voxelVolume;
                var energy = mass * cp_rock * (T - T_ref) * recovery_factor;
                totalEnergy += energy;
            }
        }

        // Convert to MWh
        return (float)(totalEnergy / 3.6e9);
    }

    private (float min, float max) FindPressureRange(float[,,] pressure, byte[,,] labels)
    {
        var w = pressure.GetLength(0);
        var h = pressure.GetLength(1);
        var d = pressure.GetLength(2);

        var minP = float.MaxValue;
        var maxP = float.MinValue;

        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (labels[x, y, z] == 0) continue;
            var P = pressure[x, y, z];
            if (P < minP) minP = P;
            if (P > maxP) maxP = P;
        }

        return (minP, maxP);
    }

    private List<FractureSegment> ExtractFractureNetwork(byte[,,] labels, float dx)
    {
        var network = new List<FractureSegment>();
        var w = labels.GetLength(0);
        var h = labels.GetLength(1);
        var d = labels.GetLength(2);

        // Extract fracture segments (simplified - could use proper segmentation)
        for (var z = 0; z < d - 1; z++)
        for (var y = 0; y < h - 1; y++)
        for (var x = 0; x < w - 1; x++)
        {
            if (labels[x, y, z] == 0) continue;
            if (_fractureAperture[x, y, z] <= _params.MinimumFractureAperture) continue;

            // Check for fracture continuation
            for (var dir = 0; dir < 3; dir++)
            {
                int nx = x, ny = y, nz = z;
                if (dir == 0) nx++;
                else if (dir == 1) ny++;
                else nz++;

                if (nx >= w || ny >= h || nz >= d) continue;
                if (labels[nx, ny, nz] == 0) continue;
                if (_fractureAperture[nx, ny, nz] <= _params.MinimumFractureAperture) continue;

                // Found connected fracture segment
                var segment = new FractureSegment
                {
                    Start = new Vector3(x * dx, y * dx, z * dx),
                    End = new Vector3(nx * dx, ny * dx, nz * dx),
                    Aperture = (_fractureAperture[x, y, z] + _fractureAperture[nx, ny, nz]) / 2f,
                    Permeability = _fractureAperture[x, y, z] * _fractureAperture[x, y, z] / 12f,
                    Pressure = (_pressure[x, y, z] + _pressure[nx, ny, nz]) / 2f,
                    Temperature = _temperature != null ? (_temperature[x, y, z] + _temperature[nx, ny, nz]) / 2f : 0f,
                    IsConnectedToInjection = _isConnectedToInjection[x, y, z] && _isConnectedToInjection[nx, ny, nz]
                };

                network.Add(segment);
            }
        }

        Logger.Log($"[GeomechCPU] Extracted {network.Count} fracture segments");
        return network;
    }

    private int CountFractures(bool[,,] fractureField)
    {
        var w = fractureField.GetLength(0);
        var h = fractureField.GetLength(1);
        var d = fractureField.GetLength(2);

        var count = 0;
        for (var z = 0; z < d; z++)
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
            if (fractureField[x, y, z])
                count++;

        return count;
    }
}