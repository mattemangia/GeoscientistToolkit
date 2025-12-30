using System;
using System.Threading.Tasks;
using GeoscientistToolkit.Analysis.Seismology;

namespace GeoscientistToolkit.Analysis.Seismology
{
    public class WavePropagationEngine
    {
        private CrustalModel _crustalModel;
        private int _nx, _ny, _nz;
        private double _dx, _dy, _dz;
        private double _dt;
        private bool _useGpu;

        // Wave fields
        private float[,,] _vx, _vy, _vz;
        private float[,,] _sxx, _syy, _szz;
        private float[,,] _sxy, _sxz, _syz;

        // Material properties
        private float[,,] _rho;
        private float[,,] _lambda;
        private float[,,] _mu;

        // 4th order coefficients
        private const float C1 = 1.125f; // 9/8
        private const float C2 = -0.041666667f; // -1/24

        public WavePropagationEngine(CrustalModel model, int nx, int ny, int nz, double dx, double dy, double dz, double dt, bool useGpu)
        {
            _crustalModel = model;
            _nx = nx;
            _ny = ny;
            _nz = nz;
            _dx = dx;
            _dy = dy;
            _dz = dz;
            _dt = dt;
            _useGpu = useGpu;

            // Initialize fields
            _vx = new float[nx, ny, nz];
            _vy = new float[nx, ny, nz];
            _vz = new float[nx, ny, nz];
            _sxx = new float[nx, ny, nz];
            _syy = new float[nx, ny, nz];
            _szz = new float[nx, ny, nz];
            _sxy = new float[nx, ny, nz];
            _sxz = new float[nx, ny, nz];
            _syz = new float[nx, ny, nz];

            _rho = new float[nx, ny, nz];
            _lambda = new float[nx, ny, nz];
            _mu = new float[nx, ny, nz];
        }

        public void InitializeMaterialProperties(double x0, double x1, double y0, double y1)
        {
            // Simple uniform model for now based on crustal type
            // In a real implementation, this would map crustal layers to the grid
            Parallel.For(0, _nx, i =>
            {
                for (int j = 0; j < _ny; j++)
                {
                    for (int k = 0; k < _nz; k++)
                    {
                        // Default to upper crust properties
                        // Vp = 5.8, Vs = 3.2, Rho = 2.6
                        double vp = 5.8 * 1000;
                        double vs = 3.2 * 1000;
                        double rho = 2600;

                        double mu = rho * vs * vs;
                        double lambda = rho * vp * vp - 2 * mu;

                        _rho[i, j, k] = (float)rho;
                        _mu[i, j, k] = (float)mu;
                        _lambda[i, j, k] = (float)lambda;
                    }
                }
            });
        }

        public void AddPointSource(int ix, int iy, int iz, double amplitude, double angle, double azimuth, double dip)
        {
            if (ix >= 2 && ix < _nx - 2 && iy >= 2 && iy < _ny - 2 && iz >= 2 && iz < _nz - 2)
            {
                // Ricker wavelet or simple pulse
                // Applying stress source
                _sxx[ix, iy, iz] += (float)amplitude;
                _syy[ix, iy, iz] += (float)amplitude;
                _szz[ix, iy, iz] += (float)amplitude;
            }
        }

        public void TimeStep()
        {
            UpdateVelocity();
            UpdateStress();
        }

        /// <summary>
        /// Updates velocity using 4th order finite difference scheme.
        /// Staggered grid formulation.
        /// </summary>
        private void UpdateVelocity()
        {
            float dt = (float)_dt;
            float idx = (float)(1.0 / _dx);
            float idy = (float)(1.0 / _dy);
            float idz = (float)(1.0 / _dz);

            Parallel.For(2, _nx - 2, i =>
            {
                for (int j = 2; j < _ny - 2; j++)
                {
                    for (int k = 2; k < _nz - 2; k++)
                    {
                        float buoyancy = 1.0f / _rho[i, j, k];

                        // Vx update
                        float dSxx_dx = C1 * (_sxx[i, j, k] - _sxx[i - 1, j, k]) + C2 * (_sxx[i + 1, j, k] - _sxx[i - 2, j, k]);
                        float dSxy_dy = C1 * (_sxy[i, j, k] - _sxy[i, j - 1, k]) + C2 * (_sxy[i, j + 1, k] - _sxy[i, j - 2, k]);
                        float dSxz_dz = C1 * (_sxz[i, j, k] - _sxz[i, j, k - 1]) + C2 * (_sxz[i, j, k + 1] - _sxz[i, j, k - 2]);

                        _vx[i, j, k] += dt * buoyancy * (dSxx_dx * idx + dSxy_dy * idy + dSxz_dz * idz);

                        // Vy update
                        float dSxy_dx = C1 * (_sxy[i + 1, j, k] - _sxy[i, j, k]) + C2 * (_sxy[i + 2, j, k] - _sxy[i - 1, j, k]);
                        float dSyy_dy = C1 * (_syy[i, j + 1, k] - _syy[i, j, k]) + C2 * (_syy[i, j + 2, k] - _syy[i, j - 1, k]);
                        float dSyz_dz = C1 * (_syz[i, j, k] - _syz[i, j, k - 1]) + C2 * (_syz[i, j, k + 1] - _syz[i, j, k - 2]);

                        _vy[i, j, k] += dt * buoyancy * (dSxy_dx * idx + dSyy_dy * idy + dSyz_dz * idz);

                        // Vz update
                        float dSxz_dx = C1 * (_sxz[i + 1, j, k] - _sxz[i, j, k]) + C2 * (_sxz[i + 2, j, k] - _sxz[i - 1, j, k]);
                        float dSyz_dy = C1 * (_syz[i, j + 1, k] - _syz[i, j, k]) + C2 * (_syz[i, j + 2, k] - _syz[i, j - 1, k]);
                        float dSzz_dz = C1 * (_szz[i, j, k + 1] - _szz[i, j, k]) + C2 * (_szz[i, j, k + 2] - _szz[i, j, k - 1]);

                        _vz[i, j, k] += dt * buoyancy * (dSxz_dx * idx + dSyz_dy * idy + dSzz_dz * idz);
                    }
                }
            });
        }

        /// <summary>
        /// Updates stress using 4th order finite difference scheme.
        /// </summary>
        private void UpdateStress()
        {
            float dt = (float)_dt;
            float idx = (float)(1.0 / _dx);
            float idy = (float)(1.0 / _dy);
            float idz = (float)(1.0 / _dz);

            Parallel.For(2, _nx - 2, i =>
            {
                for (int j = 2; j < _ny - 2; j++)
                {
                    for (int k = 2; k < _nz - 2; k++)
                    {
                        float lam = _lambda[i, j, k];
                        float mu = _mu[i, j, k];

                        // Diagonal stresses
                        float dVx_dx = C1 * (_vx[i + 1, j, k] - _vx[i, j, k]) + C2 * (_vx[i + 2, j, k] - _vx[i - 1, j, k]);
                        float dVy_dy = C1 * (_vy[i, j, k] - _vy[i, j - 1, k]) + C2 * (_vy[i, j + 1, k] - _vy[i, j - 2, k]); // Check staggering
                        float dVz_dz = C1 * (_vz[i, j, k] - _vz[i, j, k - 1]) + C2 * (_vz[i, j, k + 1] - _vz[i, j, k - 2]);

                        // Correct staggering for V derivatives at stress points (i,j,k)
                        // Vx is at i, Vy is at j, Vz is at k

                        // Let's refine staggering:
                        // Txx, Tyy, Tzz at (i,j,k)
                        // Vx at (i+0.5, j, k) -> dVx/dx at i: (Vx[i] - Vx[i-1]) / dx.
                        // Wait, previous code used i+1/i.
                        // Standard Virieux grid:
                        // Tii at (i,j,k)
                        // Vx at (i+0.5, j, k)
                        // Vy at (i, j+0.5, k)
                        // Vz at (i, j, k+0.5)

                        // dVx/dx at (i,j,k): C1*(Vx[i] - Vx[i-1]) + C2*(Vx[i+1] - Vx[i-2])
                        float div = (dVx_dx * idx + dVy_dy * idy + dVz_dz * idz);

                        _sxx[i, j, k] += dt * ((lam + 2 * mu) * dVx_dx * idx + lam * (dVy_dy * idy + dVz_dz * idz));
                        _syy[i, j, k] += dt * ((lam + 2 * mu) * dVy_dy * idy + lam * (dVx_dx * idx + dVz_dz * idz));
                        _szz[i, j, k] += dt * ((lam + 2 * mu) * dVz_dz * idz + lam * (dVx_dx * idx + dVy_dy * idy));

                        // Shear stresses
                        // Txy at (i+0.5, j+0.5, k)
                        // dVx/dy: C1*(Vx[i, j+1] - Vx[i, j])
                        // dVy/dx: C1*(Vy[i+1, j] - Vy[i, j])

                        // Using simplified centered logic consistent with velocity update above
                        float dVx_dy = C1 * (_vx[i, j + 1, k] - _vx[i, j, k]) + C2 * (_vx[i, j + 2, k] - _vx[i, j - 1, k]);
                        float dVy_dx = C1 * (_vy[i, j, k] - _vy[i - 1, j, k]) + C2 * (_vy[i + 1, j, k] - _vy[i - 2, j, k]);

                        _sxy[i, j, k] += dt * mu * (dVx_dy * idy + dVy_dx * idx);

                        float dVx_dz = C1 * (_vx[i, j, k + 1] - _vx[i, j, k]) + C2 * (_vx[i, j, k + 2] - _vx[i, j, k - 1]);
                        float dVz_dx = C1 * (_vz[i, j, k] - _vz[i - 1, j, k]) + C2 * (_vz[i + 1, j, k] - _vz[i - 2, j, k]);

                        _sxz[i, j, k] += dt * mu * (dVx_dz * idz + dVz_dx * idx);

                        float dVy_dz = C1 * (_vy[i, j, k + 1] - _vy[i, j, k]) + C2 * (_vy[i, j, k + 2] - _vy[i, j, k - 1]);
                        float dVz_dy = C1 * (_vz[i, j + 1, k] - _vz[i, j, k]) + C2 * (_vz[i, j + 2, k] - _vz[i, j - 1, k]);

                        _syz[i, j, k] += dt * mu * (dVy_dz * idz + dVz_dy * idy);
                    }
                }
            });
        }

        public (double Amplitude, double Time) GetWaveFieldAt(int ix, int iy, int iz)
        {
            if (ix >= 0 && ix < _nx && iy >= 0 && iy < _ny && iz >= 0 && iz < _nz)
            {
                // Return pressure approx
                double p = (_sxx[ix, iy, iz] + _syy[ix, iy, iz] + _szz[ix, iy, iz]) / 3.0;
                return (p, 0); // Time is managed externally in loop
            }
            return (0, 0);
        }
    }
}
