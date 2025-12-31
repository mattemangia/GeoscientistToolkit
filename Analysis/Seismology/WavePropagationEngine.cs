using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using GeoscientistToolkit.Analysis.Seismology;

namespace GeoscientistToolkit.Analysis.Seismology
{
    /// <summary>
    /// Wave field data structure for a single point
    /// </summary>
    public struct WaveField
    {
        public double Amplitude;
        public double Time;
        public double VelocityX;
        public double VelocityY;
        public double VelocityZ;
        public double DisplacementX;
        public double DisplacementY;
        public double DisplacementZ;
    }

    /// <summary>
    /// Wave type enumeration
    /// </summary>
    public enum WaveType
    {
        P,
        S,
        Love,
        Rayleigh
    }

    /// <summary>
    /// Wave propagation engine using 4th-order Finite Difference Time Domain (FDTD)
    /// Velocity-Stress formulation on a staggered grid.
    /// </summary>
    public class WavePropagationEngine
    {
        private CrustalModel _crustalModel;
        private int _nx, _ny, _nz;
        private double _dx, _dy, _dz;
        private double _dt;
        private bool _useGpu;

        // Wave fields
        private float[,,] _vx, _vy, _vz;
        private float[,,] _dx_field, _dy_field, _dz_field; // Displacement fields
        private float[,,] _sxx, _syy, _szz;
        private float[,,] _sxy, _sxz, _syz;

        // Material properties
        private float[,,] _rho;
        private float[,,] _lambda;
        private float[,,] _mu;

        // 4th order coefficients
        // Standard centered 4th order: (-f(i+2) + 8f(i+1) - 8f(i-1) + f(i-2)) / 12h
        // Staggered grid 4th order:
        // C1 = 9/8, C2 = -1/24
        // D_x(f) ~ (C1*(f[i] - f[i-1]) + C2*(f[i+1] - f[i-2])) / h?
        // Actually, standard staggered coefficients for derivatives at half-points:
        // c1 = 1.125 (9/8)
        // c2 = -0.0416667 (-1/24)
        private const float C1 = 1.125f;
        private const float C2 = -0.041666667f;

        // Time tracking
        private double _currentTime;

        public WavePropagationEngine(CrustalModel model, int nx, int ny, int nz, double dx, double dy, double dz, double dt, bool useGpu = false)
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
            _currentTime = 0;

            // Initialize fields
            _vx = new float[nx, ny, nz];
            _vy = new float[nx, ny, nz];
            _vz = new float[nx, ny, nz];

            _dx_field = new float[nx, ny, nz];
            _dy_field = new float[nx, ny, nz];
            _dz_field = new float[nx, ny, nz];

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

        public void InitializeMaterialProperties(double minLat, double maxLat, double minLon, double maxLon)
        {
            // Map crustal layers to the grid
            Parallel.For(0, _nx, i =>
            {
                double lat = minLat + (i / (double)(_nx - 1)) * (maxLat - minLat);
                for (int j = 0; j < _ny; j++)
                {
                    double lon = minLon + (j / (double)(_ny - 1)) * (maxLon - minLon);
                    for (int k = 0; k < _nz; k++)
                    {
                        double depth = k * _dz * 0.001; // Depth in km (dz is in meters usually)

                        // Default properties if model is null or fails
                        double vp = 5.8 * 1000;
                        double vs = 3.2 * 1000;
                        double rho = 2600;

                        if (_crustalModel != null)
                        {
                            try
                            {
                                var type = _crustalModel.GetCrustalType(lat, lon);
                                if (type != null)
                                {
                                    var layerInfo = type.GetLayerAtDepth(depth);
                                    var layer = layerInfo.Item2;
                                    if (layer != null)
                                    {
                                        vp = layer.VpKmPerS * 1000;
                                        vs = layer.VsKmPerS * 1000;
                                        rho = layer.DensityGPerCm3 * 1000;
                                    }
                                }
                            }
                            catch { /* Fallback to default */ }
                        }

                        double mu = rho * vs * vs;
                        double lambda = rho * vp * vp - 2 * mu;

                        _rho[i, j, k] = (float)rho;
                        _mu[i, j, k] = (float)mu;
                        _lambda[i, j, k] = (float)lambda;
                    }
                }
            });
        }

        public void AddPointSource(int ix, int iy, int iz, double amplitude, double strike, double dip, double rake)
        {
            if (ix >= 2 && ix < _nx - 2 && iy >= 2 && iy < _ny - 2 && iz >= 2 && iz < _nz - 2)
            {
                // Simple explosive source for verification
                float amp = (float)amplitude;
                _sxx[ix, iy, iz] += amp;
                _syy[ix, iy, iz] += amp;
                _szz[ix, iy, iz] += amp;
            }
        }

        public void TimeStep()
        {
            UpdateVelocity();
            UpdateStress();
            UpdateDisplacement();
            _currentTime += _dt;
        }

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
                        // dSxx/dx ~ (C1*(Sxx[i,j,k] - Sxx[i-1,j,k]) + C2*(Sxx[i+1,j,k] - Sxx[i-2,j,k])) / dx
                        float dSxx_dx = C1 * (_sxx[i, j, k] - _sxx[i - 1, j, k]) + C2 * (_sxx[i + 1, j, k] - _sxx[i - 2, j, k]);
                        float dSxy_dy = C1 * (_sxy[i, j, k] - _sxy[i, j - 1, k]) + C2 * (_sxy[i, j + 1, k] - _sxy[i, j - 2, k]);
                        float dSxz_dz = C1 * (_sxz[i, j, k] - _sxz[i, j, k - 1]) + C2 * (_sxz[i, j, k + 1] - _sxz[i, j, k - 2]);

                        _vx[i, j, k] += dt * buoyancy * (dSxx_dx * idx + dSxy_dy * idy + dSxz_dz * idz);

                        // Vy update
                        float dSxy_dx = C1 * (_sxy[i + 1, j, k] - _sxy[i, j, k]) + C2 * (_sxy[i + 2, j, k] - _sxy[i - 1, j, k]);
                        float dSyy_dy = C1 * (_syy[i, j + 1, k] - _syy[i, j, k]) + C2 * (_syy[i, j + 2, k] - _syy[i, j - 1, k]); // Correction: Syy is at j+1 vs j
                        // Check staggering: Vy at (i, j+0.5, k). Syy at (i, j, k).
                        // dSyy/dy ~ (Syy[j+1] - Syy[j])?
                        // If Vy at j+0.5, we need derivatives at j+0.5.
                        // Standard: Vy[j+0.5] += (Syy[j+1] - Syy[j])
                        // My code: (_syy[i, j + 1, k] - _syy[i, j, k]) -> This is Forward. Correct for staggered.
                        // Previous 2nd order code used: (_syy[i, j, k] - _syy[i, j - 1, k]) -> Backward?

                        // Let's align with the 4th order stencil:
                        // D_minus(f) at i = c1*(f[i+1]-f[i]) ? No.
                        // Standard: D(f) at i+0.5 = c1(f[i+1]-f[i]) + c2(f[i+2]-f[i-1])

                        // Let's ensure indices are safe (2 to N-2)

                        float dSyz_dz = C1 * (_syz[i, j, k] - _syz[i, j, k - 1]) + C2 * (_syz[i, j, k + 1] - _syz[i, j, k - 2]);

                        _vy[i, j, k] += dt * buoyancy * (dSxy_dx * idx + dSyy_dy * idy + dSyz_dz * idz);

                        // Vz update
                        float dSxz_dx = C1 * (_sxz[i + 1, j, k] - _sxz[i, j, k]) + C2 * (_sxz[i + 2, j, k] - _sxz[i - 1, j, k]);
                        float dSyz_dy = C1 * (_syz[i, j + 1, k] - _syz[i, j, k]) + C2 * (_syz[i, j + 2, k] - _syz[i, j - 1, k]);
                        float dSzz_dz = C1 * (_szz[i, j, k + 1] - _szz[i, j, k]) + C2 * (_szz[i, j, k + 2] - _szz[i, j, k - 1]); // Forward-ish

                        _vz[i, j, k] += dt * buoyancy * (dSxz_dx * idx + dSyz_dy * idy + dSzz_dz * idz);
                    }
                }
            });
        }

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

                        // T(i,j) at integer nodes.
                        // dVx/dx needs to be at integer nodes.
                        // Vx is at i+0.5 (if previous update assumed that).
                        // Then dVx/dx at i ~ c1(Vx[i] - Vx[i-1]) + c2(Vx[i+1] - Vx[i-2])
                        // Wait, Vx[i] in code usually stores Vx at i.
                        // If we assume Vx stores values at i+0.5, then Vx[i] = V(i+0.5).
                        // Then V(i+0.5) - V(i-0.5) gives derivative at i.
                        // So code: _vx[i] - _vx[i-1] matches V(i+0.5) - V(i-0.5).

                        float dVx_dx = C1 * (_vx[i, j, k] - _vx[i - 1, j, k]) + C2 * (_vx[i + 1, j, k] - _vx[i - 2, j, k]);
                        float dVy_dy = C1 * (_vy[i, j, k] - _vy[i, j - 1, k]) + C2 * (_vy[i, j + 1, k] - _vy[i, j - 2, k]);
                        float dVz_dz = C1 * (_vz[i, j, k] - _vz[i, j, k - 1]) + C2 * (_vz[i, j, k + 1] - _vz[i, j, k - 2]);

                        float div = (dVx_dx * idx + dVy_dy * idy + dVz_dz * idz);

                        _sxx[i, j, k] += dt * (lam * div + 2 * mu * dVx_dx * idx);
                        _syy[i, j, k] += dt * (lam * div + 2 * mu * dVy_dy * idy);
                        _szz[i, j, k] += dt * (lam * div + 2 * mu * dVz_dz * idz);

                        // Shear stress Sxy at i+0.5, j+0.5
                        // dVx/dy at i+0.5, j+0.5
                        // Vx is at i+0.5, j
                        // dVx/dy ~ c1(Vx[j+1] - Vx[j])
                        float dVx_dy = C1 * (_vx[i, j + 1, k] - _vx[i, j, k]) + C2 * (_vx[i, j + 2, k] - _vx[i, j - 1, k]);
                        float dVy_dx = C1 * (_vy[i + 1, j, k] - _vy[i, j, k]) + C2 * (_vy[i + 2, j, k] - _vy[i - 1, j, k]);
                        _sxy[i, j, k] += dt * mu * (dVx_dy * idy + dVy_dx * idx);

                        float dVx_dz = C1 * (_vx[i, j, k + 1] - _vx[i, j, k]) + C2 * (_vx[i, j, k + 2] - _vx[i, j, k - 1]);
                        float dVz_dx = C1 * (_vz[i + 1, j, k] - _vz[i, j, k]) + C2 * (_vz[i + 2, j, k] - _vz[i - 1, j, k]);
                        _sxz[i, j, k] += dt * mu * (dVx_dz * idz + dVz_dx * idx);

                        float dVy_dz = C1 * (_vy[i, j, k + 1] - _vy[i, j, k]) + C2 * (_vy[i, j, k + 2] - _vy[i, j, k - 1]);
                        float dVz_dy = C1 * (_vz[i, j + 1, k] - _vz[i, j, k]) + C2 * (_vz[i, j + 2, k] - _vz[i, j - 1, k]);
                        _syz[i, j, k] += dt * mu * (dVy_dz * idz + dVz_dy * idy);
                    }
                }
            });
        }

        private void UpdateDisplacement()
        {
            float dt = (float)_dt;
            Parallel.For(0, _nx, i =>
            {
                for (int j = 0; j < _ny; j++)
                {
                    for (int k = 0; k < _nz; k++)
                    {
                        _dx_field[i, j, k] += _vx[i, j, k] * dt;
                        _dy_field[i, j, k] += _vy[i, j, k] * dt;
                        _dz_field[i, j, k] += _vz[i, j, k] * dt;
                    }
                }
            });
        }

        public WaveField GetWaveFieldAt(int ix, int iy, int iz)
        {
            if (ix >= 0 && ix < _nx && iy >= 0 && iy < _ny && iz >= 0 && iz < _nz)
            {
                double p = (_sxx[ix, iy, iz] + _syy[ix, iy, iz] + _szz[ix, iy, iz]) / 3.0;

                return new WaveField
                {
                    Amplitude = p,
                    Time = _currentTime,
                    VelocityX = _vx[ix, iy, iz],
                    VelocityY = _vy[ix, iy, iz],
                    VelocityZ = _vz[ix, iy, iz],
                    DisplacementX = _dx_field[ix, iy, iz],
                    DisplacementY = _dy_field[ix, iy, iz],
                    DisplacementZ = _dz_field[ix, iy, iz]
                };
            }
            return new WaveField();
        }

        public double[,] GetSurfaceSnapshot()
        {
            var snapshot = new double[_nx, _ny];
            int k = 0;
            Parallel.For(0, _nx, i =>
            {
                for (int j = 0; j < _ny; j++)
                {
                    snapshot[i, j] = _dz_field[i, j, k];
                }
            });
            return snapshot;
        }

        public (double pTime, double sTime, double loveTime, double rayleighTime) CalculateArrivalTimes(
            double epiLat, double epiLon, double depthKm, double targetLat, double targetLon)
        {
            double distKm = CalculateDistance(epiLat, epiLon, targetLat, targetLon);
            double totalDist = Math.Sqrt(distKm * distKm + depthKm * depthKm);

            double vp = 5.8;
            double vs = 3.2;

            double pTime = totalDist / vp;
            double sTime = totalDist / vs;
            double loveTime = distKm / 3.0;
            double rayleighTime = distKm / 2.8;

            return (pTime, sTime, loveTime, rayleighTime);
        }

        public double CalculateSurfaceWaveAmplitude(WaveType type, double distKm, double magnitude, double frequency)
        {
            if (distKm < 1.0) distKm = 1.0;
            double geometricSpreading = 1.0 / Math.Sqrt(distKm);
            double attenuation = Math.Exp(-0.005 * distKm * frequency);
            double sourceAmp = Math.Pow(10, magnitude - 5.0);
            return sourceAmp * geometricSpreading * attenuation;
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            double dlat = lat2 - lat1;
            double dlon = lon2 - lon1;
            double latMid = (lat1 + lat2) / 2.0;
            double kmPerDegLat = 111.0;
            double kmPerDegLon = 111.0 * Math.Cos(latMid * Math.PI / 180.0);

            double x = dlon * kmPerDegLon;
            double y = dlat * kmPerDegLat;

            return Math.Sqrt(x * x + y * y);
        }
    }
}
