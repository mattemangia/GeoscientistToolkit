using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using GeoscientistToolkit.Analysis.Seismology;

namespace GeoscientistToolkit.Analysis.Seismology
{
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

    public enum WaveType
    {
        P,
        S,
        Love,
        Rayleigh
    }

    /// <summary>
    /// Wave propagation engine using 2nd-order Staggered Grid Finite Difference (Virieux, 1986).
    /// Grid Layout:
    /// - Normal Stresses (Sxx, Syy, Szz) and Lame parameters at integer nodes (i,j,k)
    /// - Shear Stresses: Sxy(i+0.5, j+0.5, k), Sxz(i+0.5, j, k+0.5), Syz(i, j+0.5, k+0.5)
    /// - Velocities: Vx(i+0.5, j, k), Vy(i, j+0.5, k), Vz(i, j, k+0.5)
    /// </summary>
    public class WavePropagationEngine
    {
        private CrustalModel _crustalModel;
        private int _nx, _ny, _nz;
        private double _dx, _dy, _dz;
        private double _dt;
        private bool _useGpu;

        // Fields
        private float[,,] _vx, _vy, _vz;
        private float[,,] _sxx, _syy, _szz;
        private float[,,] _sxy, _sxz, _syz;

        // Material properties
        private float[,,] _rho;
        private float[,,] _lambda;
        private float[,,] _mu;

        // Integration state
        private float[,,] _dx_field, _dy_field, _dz_field;
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

            _vx = new float[nx, ny, nz];
            _vy = new float[nx, ny, nz];
            _vz = new float[nx, ny, nz];

            _sxx = new float[nx, ny, nz];
            _syy = new float[nx, ny, nz];
            _szz = new float[nx, ny, nz];
            _sxy = new float[nx, ny, nz];
            _sxz = new float[nx, ny, nz];
            _syz = new float[nx, ny, nz];

            _dx_field = new float[nx, ny, nz];
            _dy_field = new float[nx, ny, nz];
            _dz_field = new float[nx, ny, nz];

            _rho = new float[nx, ny, nz];
            _lambda = new float[nx, ny, nz];
            _mu = new float[nx, ny, nz];
        }

        public void InitializeMaterialProperties(double minLat, double maxLat, double minLon, double maxLon)
        {
            Parallel.For(0, _nx, i =>
            {
                double lat = minLat + (i / (double)(_nx - 1)) * (maxLat - minLat);
                for (int j = 0; j < _ny; j++)
                {
                    double lon = minLon + (j / (double)(_ny - 1)) * (maxLon - minLon);
                    for (int k = 0; k < _nz; k++)
                    {
                        double depth = k * _dz * 0.001;

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
                            catch { }
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
            // Add a Ricker wavelet or smoothed impulse to Stress components
            if (ix >= 2 && ix < _nx - 2 && iy >= 2 && iy < _ny - 2 && iz >= 2 && iz < _nz - 2)
            {
                float amp = (float)amplitude;
                // Approximating explosive source
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
            float dxi = (float)(1.0 / _dx);
            float dyi = (float)(1.0 / _dy);
            float dzi = (float)(1.0 / _dz);

            // Update Vx (lives at i+0.5, j, k)
            // Needs dSxx/dx, dSxy/dy, dSxz/dz
            // dSxx/dx ~ (Sxx[i+1] - Sxx[i]) / dx
            // dSxy/dy ~ (Sxy[j] - Sxy[j-1]) / dy (Sxy at j+0.5)
            // dSxz/dz ~ (Sxz[k] - Sxz[k-1]) / dz (Sxz at k+0.5)

            Parallel.For(1, _nx - 1, i =>
            {
                for (int j = 1; j < _ny - 1; j++)
                {
                    for (int k = 1; k < _nz - 1; k++)
                    {
                        // Avg density at i+0.5
                        float rho = 0.5f * (_rho[i, j, k] + _rho[i + 1, j, k]);
                        float buoyancy = 1.0f / rho;

                        float dSxx_dx = (_sxx[i + 1, j, k] - _sxx[i, j, k]) * dxi;
                        float dSxy_dy = (_sxy[i, j, k] - _sxy[i, j - 1, k]) * dyi;
                        float dSxz_dz = (_sxz[i, j, k] - _sxz[i, j, k - 1]) * dzi;

                        _vx[i, j, k] += dt * buoyancy * (dSxx_dx + dSxy_dy + dSxz_dz);
                    }
                }
            });

            // Update Vy (lives at i, j+0.5, k)
            Parallel.For(1, _nx - 1, i =>
            {
                for (int j = 1; j < _ny - 1; j++)
                {
                    for (int k = 1; k < _nz - 1; k++)
                    {
                        float rho = 0.5f * (_rho[i, j, k] + _rho[i, j + 1, k]);
                        float buoyancy = 1.0f / rho;

                        float dSxy_dx = (_sxy[i, j, k] - _sxy[i - 1, j, k]) * dxi;
                        float dSyy_dy = (_syy[i, j + 1, k] - _syy[i, j, k]) * dyi;
                        float dSyz_dz = (_syz[i, j, k] - _syz[i, j, k - 1]) * dzi;

                        _vy[i, j, k] += dt * buoyancy * (dSxy_dx + dSyy_dy + dSyz_dz);
                    }
                }
            });

            // Update Vz (lives at i, j, k+0.5)
            Parallel.For(1, _nx - 1, i =>
            {
                for (int j = 1; j < _ny - 1; j++)
                {
                    for (int k = 1; k < _nz - 1; k++)
                    {
                        float rho = 0.5f * (_rho[i, j, k] + _rho[i, j, k + 1]);
                        float buoyancy = 1.0f / rho;

                        float dSxz_dx = (_sxz[i, j, k] - _sxz[i - 1, j, k]) * dxi;
                        float dSyz_dy = (_syz[i, j, k] - _syz[i, j - 1, k]) * dyi;
                        float dSzz_dz = (_szz[i, j, k + 1] - _szz[i, j, k]) * dzi;

                        _vz[i, j, k] += dt * buoyancy * (dSxz_dx + dSyz_dy + dSzz_dz);
                    }
                }
            });
        }

        private void UpdateStress()
        {
            float dt = (float)_dt;
            float dxi = (float)(1.0 / _dx);
            float dyi = (float)(1.0 / _dy);
            float dzi = (float)(1.0 / _dz);

            // Update Normal Stresses (i,j,k)
            Parallel.For(1, _nx - 1, i =>
            {
                for (int j = 1; j < _ny - 1; j++)
                {
                    for (int k = 1; k < _nz - 1; k++)
                    {
                        float lam = _lambda[i, j, k];
                        float mu = _mu[i, j, k];

                        // dVx/dx at i. Vx is at i+0.5.
                        // (Vx[i] - Vx[i-1]) / dx
                        float dVx_dx = (_vx[i, j, k] - _vx[i - 1, j, k]) * dxi;
                        float dVy_dy = (_vy[i, j, k] - _vy[i, j - 1, k]) * dyi;
                        float dVz_dz = (_vz[i, j, k] - _vz[i, j, k - 1]) * dzi;

                        float div = dVx_dx + dVy_dy + dVz_dz;

                        _sxx[i, j, k] += dt * (lam * div + 2 * mu * dVx_dx);
                        _syy[i, j, k] += dt * (lam * div + 2 * mu * dVy_dy);
                        _szz[i, j, k] += dt * (lam * div + 2 * mu * dVz_dz);
                    }
                }
            });

            // Update Shear Stresses

            // Sxy at (i+0.5, j+0.5, k)
            Parallel.For(1, _nx - 1, i =>
            {
                for (int j = 1; j < _ny - 1; j++)
                {
                    for (int k = 1; k < _nz - 1; k++)
                    {
                        // Avg mu
                        // 4 points around (i+0.5, j+0.5)
                        float mu = 0.25f * (_mu[i, j, k] + _mu[i + 1, j, k] + _mu[i, j + 1, k] + _mu[i + 1, j + 1, k]);

                        // dVx/dy at i+0.5, j+0.5.
                        // Vx at i+0.5, j.
                        // (Vx[j+1] - Vx[j])
                        float dVx_dy = (_vx[i, j + 1, k] - _vx[i, j, k]) * dyi;
                        float dVy_dx = (_vy[i + 1, j, k] - _vy[i, j, k]) * dxi;

                        _sxy[i, j, k] += dt * mu * (dVx_dy + dVy_dx);
                    }
                }
            });

            // Sxz at (i+0.5, j, k+0.5)
            Parallel.For(1, _nx - 1, i =>
            {
                for (int j = 1; j < _ny - 1; j++)
                {
                    for (int k = 1; k < _nz - 1; k++)
                    {
                        float mu = 0.25f * (_mu[i, j, k] + _mu[i + 1, j, k] + _mu[i, j, k + 1] + _mu[i + 1, j, k + 1]);

                        float dVx_dz = (_vx[i, j, k + 1] - _vx[i, j, k]) * dzi;
                        float dVz_dx = (_vz[i + 1, j, k] - _vz[i, j, k]) * dxi;

                        _sxz[i, j, k] += dt * mu * (dVx_dz + dVz_dx);
                    }
                }
            });

            // Syz at (i, j+0.5, k+0.5)
            Parallel.For(1, _nx - 1, i =>
            {
                for (int j = 1; j < _ny - 1; j++)
                {
                    for (int k = 1; k < _nz - 1; k++)
                    {
                        float mu = 0.25f * (_mu[i, j, k] + _mu[i, j + 1, k] + _mu[i, j, k + 1] + _mu[i, j + 1, k + 1]);

                        float dVy_dz = (_vy[i, j, k + 1] - _vy[i, j, k]) * dzi;
                        float dVz_dy = (_vz[i, j + 1, k] - _vz[i, j, k]) * dyi;

                        _syz[i, j, k] += dt * mu * (dVy_dz + dVz_dy);
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
                        // Interpolate velocities to node (i,j,k)
                        float vx = (i > 0) ? 0.5f * (_vx[i, j, k] + _vx[i - 1, j, k]) : _vx[i, j, k];
                        float vy = (j > 0) ? 0.5f * (_vy[i, j, k] + _vy[i, j - 1, k]) : _vy[i, j, k];
                        float vz = (k > 0) ? 0.5f * (_vz[i, j, k] + _vz[i, j, k - 1]) : _vz[i, j, k];

                        _dx_field[i, j, k] += vx * dt;
                        _dy_field[i, j, k] += vy * dt;
                        _dz_field[i, j, k] += vz * dt;
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
