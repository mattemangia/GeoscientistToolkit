// GeoscientistToolkit/Analysis/AcousticSimulation/AcousticSimulatorCPU.cs
// OPTIMIZED VERSION - Better parallelization and reduced overhead

using System.Runtime.CompilerServices;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

public class AcousticSimulatorCPU : IAcousticKernel
{
    private readonly SimulationParameters _params;
    private int _width, _height, _depth;

    public AcousticSimulatorCPU(SimulationParameters parameters)
    {
        _params = parameters;
    }

    public void Initialize(int width, int height, int depth)
    {
        _width = width;
        _height = height;
        _depth = depth;
    }

    public void UpdateWaveField(
        float[,,] vx, float[,,] vy, float[,,] vz,
        float[,,] sxx, float[,,] syy, float[,,] szz,
        float[,,] sxy, float[,,] sxz, float[,,] syz,
        float[,,] E, float[,,] nu, float[,,] rho,
        float dt, float dx, float dampingFactor)
    {
        var w = vx.GetLength(0);
        var h = vx.GetLength(1);
        var d = vx.GetLength(2);

        // Step 1: Update stresses from velocity gradients
        UpdateStresses(vx, vy, vz, sxx, syy, szz, sxy, sxz, syz, E, nu, dt, dx, w, h, d);

        // Step 2: Update velocities from stress gradients
        UpdateVelocities(vx, vy, vz, sxx, syy, szz, sxy, sxz, syz, rho, dt, dx, dampingFactor, w, h, d);
    }

    public void Dispose()
    {
        // No resources to dispose
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateStresses(
        float[,,] vx, float[,,] vy, float[,,] vz,
        float[,,] sxx, float[,,] syy, float[,,] szz,
        float[,,] sxy, float[,,] sxz, float[,,] syz,
        float[,,] E, float[,,] nu,
        float dt, float dx, int w, int h, int d)
    {
        var inv_2dx = 1.0f / (2.0f * dx);

        // OPTIMIZATION: Parallel over Z slices (better cache locality)
        Parallel.For(1, d - 1, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, z =>
        {
            for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
            {
                // Get material properties
                var E_local = E[x, y, z];
                var nu_local = Math.Clamp(nu[x, y, z], 0.01f, 0.49f);

                // Lamé parameters
                var mu = E_local / (2f * (1f + nu_local));
                var lambda = E_local * nu_local / ((1f + nu_local) * (1f - 2f * nu_local));

                // OPTIMIZATION: Manual array indexing (faster than multi-dim indexing)
                // Velocity gradients (central differences)
                var dvx_dx = (vx[x + 1, y, z] - vx[x - 1, y, z]) * inv_2dx;
                var dvy_dy = (vy[x, y + 1, z] - vy[x, y - 1, z]) * inv_2dx;
                var dvz_dz = (vz[x, y, z + 1] - vz[x, y, z - 1]) * inv_2dx;

                // Normal stresses (Hooke's law)
                var lambda_2mu = lambda + 2f * mu;
                sxx[x, y, z] += dt * (lambda_2mu * dvx_dx + lambda * (dvy_dy + dvz_dz));
                syy[x, y, z] += dt * (lambda_2mu * dvy_dy + lambda * (dvx_dx + dvz_dz));
                szz[x, y, z] += dt * (lambda_2mu * dvz_dz + lambda * (dvx_dx + dvy_dy));

                // Shear strains
                var dvx_dy = (vx[x, y + 1, z] - vx[x, y - 1, z]) * inv_2dx;
                var dvy_dx = (vy[x + 1, y, z] - vy[x - 1, y, z]) * inv_2dx;
                var dvx_dz = (vx[x, y, z + 1] - vx[x, y, z - 1]) * inv_2dx;
                var dvz_dx = (vz[x + 1, y, z] - vz[x - 1, y, z]) * inv_2dx;
                var dvy_dz = (vy[x, y, z + 1] - vy[x, y, z - 1]) * inv_2dx;
                var dvz_dy = (vz[x, y + 1, z] - vz[x, y - 1, z]) * inv_2dx;

                // Shear stresses
                sxy[x, y, z] += dt * mu * (dvx_dy + dvy_dx);
                sxz[x, y, z] += dt * mu * (dvx_dz + dvz_dx);
                syz[x, y, z] += dt * mu * (dvy_dz + dvz_dy);
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateVelocities(
        float[,,] vx, float[,,] vy, float[,,] vz,
        float[,,] sxx, float[,,] syy, float[,,] szz,
        float[,,] sxy, float[,,] sxz, float[,,] syz,
        float[,,] rho, float dt, float dx, float dampingFactor,
        int w, int h, int d)
    {
        var inv_2dx = 1.0f / (2.0f * dx);
        var damping = 1f - dampingFactor * dt;

        // OPTIMIZATION: Parallel over Z slices
        Parallel.For(1, d - 1, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, z =>
        {
            for (var y = 1; y < h - 1; y++)
                // OPTIMIZATION: Process row at a time (better cache locality)
            for (var x = 1; x < w - 1; x++)
            {
                var rho_local = Math.Max(rho[x, y, z], 1.0f);
                var inv_rho = 1.0f / rho_local;

                // Stress gradients
                var dsxx_dx = (sxx[x + 1, y, z] - sxx[x - 1, y, z]) * inv_2dx;
                var dsxy_dy = (sxy[x, y + 1, z] - sxy[x, y - 1, z]) * inv_2dx;
                var dsxz_dz = (sxz[x, y, z + 1] - sxz[x, y, z - 1]) * inv_2dx;

                var dsxy_dx = (sxy[x + 1, y, z] - sxy[x - 1, y, z]) * inv_2dx;
                var dsyy_dy = (syy[x, y + 1, z] - syy[x, y - 1, z]) * inv_2dx;
                var dsyz_dz = (syz[x, y, z + 1] - syz[x, y, z - 1]) * inv_2dx;

                var dsxz_dx = (sxz[x + 1, y, z] - sxz[x - 1, y, z]) * inv_2dx;
                var dsyz_dy = (syz[x, y + 1, z] - syz[x, y - 1, z]) * inv_2dx;
                var dszz_dz = (szz[x, y, z + 1] - szz[x, y, z - 1]) * inv_2dx;

                // Accelerations
                var ax = (dsxx_dx + dsxy_dy + dsxz_dz) * inv_rho;
                var ay = (dsxy_dx + dsyy_dy + dsyz_dz) * inv_rho;
                var az = (dsxz_dx + dsyz_dy + dszz_dz) * inv_rho;

                // Update velocities with damping (fused multiply-add)
                vx[x, y, z] = vx[x, y, z] * damping + dt * ax;
                vy[x, y, z] = vy[x, y, z] * damping + dt * ay;
                vz[x, y, z] = vz[x, y, z] * damping + dt * az;
            }
        });
    }
}