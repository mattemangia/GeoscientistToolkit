// GeoscientistToolkit/Analysis/AcousticSimulation/AcousticSimulatorCPU.cs

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

/// <summary>
///     CPU implementation of acoustic wave propagation using finite differences.
/// </summary>
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
        int w = vx.GetLength(0);
        int h = vx.GetLength(1);
        int d = vx.GetLength(2);
        
        // Step 1: Update stresses from velocity gradients
        UpdateStresses(vx, vy, vz, sxx, syy, szz, sxy, sxz, syz, E, nu, dt, dx, w, h, d);
        
        // Step 2: Update velocities from stress gradients
        UpdateVelocities(vx, vy, vz, sxx, syy, szz, sxy, sxz, syz, rho, dt, dx, dampingFactor, w, h, d);
    }

    private void UpdateStresses(
        float[,,] vx, float[,,] vy, float[,,] vz,
        float[,,] sxx, float[,,] syy, float[,,] szz,
        float[,,] sxy, float[,,] sxz, float[,,] syz,
        float[,,] E, float[,,] nu,
        float dt, float dx, int w, int h, int d)
    {
        // Compute Lamé parameters from E and nu at each point
        Parallel.For(1, d - 1, z =>
        {
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float E_local = E[x, y, z];
                float nu_local = Math.Clamp(nu[x, y, z], 0.01f, 0.49f); // Prevent singularities
                
                // Lamé parameters
                float mu = E_local / (2f * (1f + nu_local));
                float lambda = E_local * nu_local / ((1f + nu_local) * (1f - 2f * nu_local));
                
                // Velocity gradients (central differences)
                float dvx_dx = (vx[x + 1, y, z] - vx[x - 1, y, z]) / (2f * dx);
                float dvy_dy = (vy[x, y + 1, z] - vy[x, y - 1, z]) / (2f * dx);
                float dvz_dz = (vz[x, y, z + 1] - vz[x, y, z - 1]) / (2f * dx);
                
                // Volumetric strain
                float div_v = dvx_dx + dvy_dy + dvz_dz;
                
                // Update normal stresses (Hooke's law for isotropic elastic material)
                sxx[x, y, z] += dt * ((lambda + 2f * mu) * dvx_dx + lambda * (dvy_dy + dvz_dz));
                syy[x, y, z] += dt * ((lambda + 2f * mu) * dvy_dy + lambda * (dvx_dx + dvz_dz));
                szz[x, y, z] += dt * ((lambda + 2f * mu) * dvz_dz + lambda * (dvx_dx + dvy_dy));
                
                // Shear strains
                float dvx_dy = (vx[x, y + 1, z] - vx[x, y - 1, z]) / (2f * dx);
                float dvy_dx = (vy[x + 1, y, z] - vy[x - 1, y, z]) / (2f * dx);
                float dvx_dz = (vx[x, y, z + 1] - vx[x, y, z - 1]) / (2f * dx);
                float dvz_dx = (vz[x + 1, y, z] - vz[x - 1, y, z]) / (2f * dx);
                float dvy_dz = (vy[x, y, z + 1] - vy[x, y, z - 1]) / (2f * dx);
                float dvz_dy = (vz[x, y + 1, z] - vz[x, y - 1, z]) / (2f * dx);
                
                // Update shear stresses
                sxy[x, y, z] += dt * mu * (dvx_dy + dvy_dx);
                sxz[x, y, z] += dt * mu * (dvx_dz + dvz_dx);
                syz[x, y, z] += dt * mu * (dvy_dz + dvz_dy);
            }
        });
    }

    private void UpdateVelocities(
        float[,,] vx, float[,,] vy, float[,,] vz,
        float[,,] sxx, float[,,] syy, float[,,] szz,
        float[,,] sxy, float[,,] sxz, float[,,] syz,
        float[,,] rho, float dt, float dx, float dampingFactor,
        int w, int h, int d)
    {
        Parallel.For(1, d - 1, z =>
        {
            for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float rho_local = Math.Max(rho[x, y, z], 1.0f); // Prevent division by zero
                
                // Stress gradients (central differences)
                float dsxx_dx = (sxx[x + 1, y, z] - sxx[x - 1, y, z]) / (2f * dx);
                float dsxy_dy = (sxy[x, y + 1, z] - sxy[x, y - 1, z]) / (2f * dx);
                float dsxz_dz = (sxz[x, y, z + 1] - sxz[x, y, z - 1]) / (2f * dx);
                
                float dsxy_dx = (sxy[x + 1, y, z] - sxy[x - 1, y, z]) / (2f * dx);
                float dsyy_dy = (syy[x, y + 1, z] - syy[x, y - 1, z]) / (2f * dx);
                float dsyz_dz = (syz[x, y, z + 1] - syz[x, y, z - 1]) / (2f * dx);
                
                float dsxz_dx = (sxz[x + 1, y, z] - sxz[x - 1, y, z]) / (2f * dx);
                float dsyz_dy = (syz[x, y + 1, z] - syz[x, y - 1, z]) / (2f * dx);
                float dszz_dz = (szz[x, y, z + 1] - szz[x, y, z - 1]) / (2f * dx);
                
                // Momentum equation: ρ * dv/dt = ∇·σ
                float ax = (dsxx_dx + dsxy_dy + dsxz_dz) / rho_local;
                float ay = (dsxy_dx + dsyy_dy + dsyz_dz) / rho_local;
                float az = (dsxz_dx + dsyz_dy + dszz_dz) / rho_local;
                
                // Update velocities with artificial damping for stability
                float damping = 1f - dampingFactor * dt;
                vx[x, y, z] = vx[x, y, z] * damping + dt * ax;
                vy[x, y, z] = vy[x, y, z] * damping + dt * ay;
                vz[x, y, z] = vz[x, y, z] * damping + dt * az;
            }
        });
    }

    public void Dispose()
    {
        // No resources to dispose for CPU version
    }
}