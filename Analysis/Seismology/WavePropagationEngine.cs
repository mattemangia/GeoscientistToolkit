using System;
using System.Numerics;
using System.Threading.Tasks;

namespace GeoscientistToolkit.Analysis.Seismology
{
    /// <summary>
    /// Wave types for seismic propagation
    /// </summary>
    public enum WaveType
    {
        P,          // Primary (compressional) waves
        S,          // Secondary (shear) waves
        Love,       // Love surface waves
        Rayleigh    // Rayleigh surface waves
    }

    /// <summary>
    /// Represents a seismic wavefield at a point in space and time
    /// </summary>
    public struct WaveField
    {
        public double DisplacementX;
        public double DisplacementY;
        public double DisplacementZ;
        public double VelocityX;
        public double VelocityY;
        public double VelocityZ;
        public double Amplitude;
        public double ArrivalTime;
    }

    /// <summary>
    /// Finite difference engine for 3D seismic wave propagation
    /// Simplified version inspired by SpecFEM methodology
    ///
    /// ALGORITHM: 3D Elastic Wave Propagation with Moment Tensor Sources
    ///
    /// Implements numerical solution of the elastic wave equation using finite differences.
    /// Supports double-couple moment tensor sources for earthquake simulation.
    ///
    /// References:
    /// - Komatitsch, D., & Vilotte, J.P. (1998). "The spectral element method: An efficient
    ///   tool to simulate the seismic response of 2D and 3D geological structures."
    ///   Bulletin of the Seismological Society of America, 88(2), 368-392.
    ///
    /// - Aki, K., & Richards, P.G. (2002). "Quantitative Seismology," 2nd ed.
    ///   University Science Books. (Chapters 2-4 for wave theory and moment tensors)
    ///
    /// - Jost, M.L., & Herrmann, R.B. (1989). "A student's guide to and review of moment
    ///   tensors." Seismological Research Letters, 60(2), 37-57.
    ///
    /// - Shearer, P.M. (2009). "Introduction to Seismology," 2nd ed. Cambridge University Press.
    /// </summary>
    public class WavePropagationEngine
    {
        private readonly CrustalModel _crustalModel;
        private readonly int _nx, _ny, _nz; // Grid dimensions
        private readonly double _dx, _dy, _dz; // Grid spacing in km
        private readonly double _dt; // Time step in seconds

        // Wavefield arrays (parallelized storage)
        private double[,,] _ux, _uy, _uz; // Displacement
        private double[,,] _vx, _vy, _vz; // Velocity
        private double[,,] _vp, _vs, _rho; // Material properties

        public WavePropagationEngine(
            CrustalModel crustalModel,
            int nx, int ny, int nz,
            double dx, double dy, double dz,
            double dt)
        {
            _crustalModel = crustalModel;
            _nx = nx;
            _ny = ny;
            _nz = nz;
            _dx = dx;
            _dy = dy;
            _dz = dz;
            _dt = dt;

            // Initialize arrays
            _ux = new double[nx, ny, nz];
            _uy = new double[nx, ny, nz];
            _uz = new double[nx, ny, nz];
            _vx = new double[nx, ny, nz];
            _vy = new double[nx, ny, nz];
            _vz = new double[nx, ny, nz];
            _vp = new double[nx, ny, nz];
            _vs = new double[nx, ny, nz];
            _rho = new double[nx, ny, nz];
        }

        /// <summary>
        /// Initialize material properties from crustal model
        /// </summary>
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
                        double depth = k * _dz;

                        var crustalType = _crustalModel.GetCrustalType(lat, lon);
                        var (_, layer, _) = crustalType.GetLayerAtDepth(depth);

                        _vp[i, j, k] = layer.VpKmPerS;
                        _vs[i, j, k] = layer.VsKmPerS;
                        _rho[i, j, k] = layer.DensityGPerCm3;
                    }
                }
            });
        }

        /// <summary>
        /// Add point source (earthquake hypocenter)
        /// </summary>
        public void AddPointSource(int ix, int iy, int iz, double momentMagnitude, double strikeRad, double dipRad, double rakeRad)
        {
            // Convert moment magnitude to seismic moment (N·m)
            double M0 = Math.Pow(10, 1.5 * momentMagnitude + 9.1);

            // Moment tensor components (normalized)
            double strike = strikeRad;
            double dip = dipRad;
            double rake = rakeRad;

            // Double-couple moment tensor
            double Mxx = -M0 * (Math.Sin(dip) * Math.Cos(rake) * Math.Sin(2 * strike) +
                                Math.Sin(2 * dip) * Math.Sin(rake) * Math.Sin(strike) * Math.Sin(strike));
            double Myy = M0 * (Math.Sin(dip) * Math.Cos(rake) * Math.Sin(2 * strike) -
                               Math.Sin(2 * dip) * Math.Sin(rake) * Math.Cos(strike) * Math.Cos(strike));
            double Mzz = M0 * (Math.Sin(2 * dip) * Math.Sin(rake));
            double Mxy = M0 * (Math.Sin(dip) * Math.Cos(rake) * Math.Cos(2 * strike) +
                               0.5 * Math.Sin(2 * dip) * Math.Sin(rake) * Math.Sin(2 * strike));
            double Mxz = -M0 * (Math.Cos(dip) * Math.Cos(rake) * Math.Cos(strike) +
                                Math.Cos(2 * dip) * Math.Sin(rake) * Math.Sin(strike));
            double Myz = -M0 * (Math.Cos(dip) * Math.Cos(rake) * Math.Sin(strike) -
                                Math.Cos(2 * dip) * Math.Sin(rake) * Math.Cos(strike));

            // Apply source to displacement field (simplified)
            double sourceAmplitude = M0 / (_rho[ix, iy, iz] * Math.Pow(_dx * 1000, 3)); // Scale by volume

            _ux[ix, iy, iz] += sourceAmplitude * Mxx / M0;
            _uy[ix, iy, iz] += sourceAmplitude * Myy / M0;
            _uz[ix, iy, iz] += sourceAmplitude * Mzz / M0;
        }

        /// <summary>
        /// Perform one time step of wave propagation using finite differences
        /// Parallelized with OpenMP-style patterns
        /// </summary>
        public void TimeStep()
        {
            // Temporary arrays for updated velocities
            var vxNew = new double[_nx, _ny, _nz];
            var vyNew = new double[_nx, _ny, _nz];
            var vzNew = new double[_nx, _ny, _nz];

            // Update velocities from stresses (parallelized)
            Parallel.For(1, _nx - 1, i =>
            {
                for (int j = 1; j < _ny - 1; j++)
                {
                    for (int k = 1; k < _nz - 1; k++)
                    {
                        // Spatial derivatives of displacement (2nd order finite difference)
                        double dux_dx = (_ux[i + 1, j, k] - _ux[i - 1, j, k]) / (2.0 * _dx);
                        double duy_dy = (_uy[i, j + 1, k] - _uy[i, j - 1, k]) / (2.0 * _dy);
                        double duz_dz = (_uz[i, j, k + 1] - _uz[i, j, k - 1]) / (2.0 * _dz);

                        double dux_dy = (_ux[i, j + 1, k] - _ux[i, j - 1, k]) / (2.0 * _dy);
                        double dux_dz = (_ux[i, j, k + 1] - _ux[i, j, k - 1]) / (2.0 * _dz);
                        double duy_dx = (_uy[i + 1, j, k] - _uy[i - 1, j, k]) / (2.0 * _dx);
                        double duy_dz = (_uy[i, j, k + 1] - _uy[i, j, k - 1]) / (2.0 * _dz);
                        double duz_dx = (_uz[i + 1, j, k] - _uz[i - 1, j, k]) / (2.0 * _dx);
                        double duz_dy = (_uz[i, j + 1, k] - _uz[i, j - 1, k]) / (2.0 * _dy);

                        // Elastic moduli (convert to SI units)
                        double rho = _rho[i, j, k] * 1000.0; // kg/m^3
                        double vp = _vp[i, j, k] * 1000.0;   // m/s
                        double vs = _vs[i, j, k] * 1000.0;   // m/s

                        double mu = rho * vs * vs;
                        double lambda = rho * vp * vp - 2.0 * mu;

                        // Stress components
                        double strain_vol = dux_dx + duy_dy + duz_dz;
                        double sigma_xx = lambda * strain_vol + 2.0 * mu * dux_dx;
                        double sigma_yy = lambda * strain_vol + 2.0 * mu * duy_dy;
                        double sigma_zz = lambda * strain_vol + 2.0 * mu * duz_dz;
                        double sigma_xy = mu * (dux_dy + duy_dx);
                        double sigma_xz = mu * (dux_dz + duz_dx);
                        double sigma_yz = mu * (duy_dz + duz_dy);

                        // Stress divergence (acceleration) - properly compute derivatives
                        // Need to compute stress at neighboring points for finite differences
                        // Simplified approach: use centered differences on strain rates
                        double acc_x = (dux_dx * (lambda + 2.0 * mu) + duy_dy * lambda + duz_dz * lambda +
                                       mu * (dux_dy + duy_dx) + mu * (dux_dz + duz_dx)) / (rho * _dx);
                        double acc_y = (duy_dy * (lambda + 2.0 * mu) + dux_dx * lambda + duz_dz * lambda +
                                       mu * (dux_dy + duy_dx) + mu * (duy_dz + duz_dy)) / (rho * _dy);
                        double acc_z = (duz_dz * (lambda + 2.0 * mu) + dux_dx * lambda + duy_dy * lambda +
                                       mu * (dux_dz + duz_dx) + mu * (duy_dz + duz_dy)) / (rho * _dz);

                        // Update velocities
                        vxNew[i, j, k] = _vx[i, j, k] + _dt * acc_x;
                        vyNew[i, j, k] = _vy[i, j, k] + _dt * acc_y;
                        vzNew[i, j, k] = _vz[i, j, k] + _dt * acc_z;
                    }
                }
            });

            // Update displacements from velocities (parallelized)
            Parallel.For(0, _nx, i =>
            {
                for (int j = 0; j < _ny; j++)
                {
                    for (int k = 0; k < _nz; k++)
                    {
                        _vx[i, j, k] = vxNew[i, j, k];
                        _vy[i, j, k] = vyNew[i, j, k];
                        _vz[i, j, k] = vzNew[i, j, k];

                        _ux[i, j, k] += _dt * _vx[i, j, k];
                        _uy[i, j, k] += _dt * _vy[i, j, k];
                        _uz[i, j, k] += _dt * _vz[i, j, k];
                    }
                }
            });
        }

        /// <summary>
        /// Get wavefield at specific location
        /// </summary>
        public WaveField GetWaveFieldAt(int i, int j, int k)
        {
            return new WaveField
            {
                DisplacementX = _ux[i, j, k],
                DisplacementY = _uy[i, j, k],
                DisplacementZ = _uz[i, j, k],
                VelocityX = _vx[i, j, k],
                VelocityY = _vy[i, j, k],
                VelocityZ = _vz[i, j, k],
                Amplitude = Math.Sqrt(_ux[i, j, k] * _ux[i, j, k] +
                                     _uy[i, j, k] * _uy[i, j, k] +
                                     _uz[i, j, k] * _uz[i, j, k])
            };
        }

        /// <summary>
        /// Calculate surface wave (Love and Rayleigh) amplitudes
        /// Simplified analytical approximation for surface waves
        /// </summary>
        public double CalculateSurfaceWaveAmplitude(
            WaveType waveType,
            double distance_km,
            double magnitude,
            double frequency_hz)
        {
            // Seismic moment
            double M0 = Math.Pow(10, 1.5 * magnitude + 9.1);

            // Distance in meters
            double r = distance_km * 1000.0;

            double amplitude = 0.0;

            switch (waveType)
            {
                case WaveType.Love:
                    // Love waves (SH motion, trapped in crustal waveguide)
                    // Amplitude ~ M0 / (r^0.5) for surface waves
                    amplitude = M0 / (Math.Sqrt(r) * 1e15);
                    // Frequency dependence
                    amplitude *= Math.Exp(-0.01 * frequency_hz * distance_km);
                    break;

                case WaveType.Rayleigh:
                    // Rayleigh waves (elliptical motion)
                    // Typically larger amplitude than Love waves
                    amplitude = 1.5 * M0 / (Math.Sqrt(r) * 1e15);
                    // Frequency dependence
                    amplitude *= Math.Exp(-0.01 * frequency_hz * distance_km);
                    break;

                case WaveType.P:
                    // P-waves (body waves)
                    amplitude = M0 / (r * r * 1e15);
                    amplitude *= Math.Exp(-0.05 * frequency_hz * distance_km);
                    break;

                case WaveType.S:
                    // S-waves (body waves, larger than P)
                    amplitude = 2.0 * M0 / (r * r * 1e15);
                    amplitude *= Math.Exp(-0.03 * frequency_hz * distance_km);
                    break;
            }

            return amplitude;
        }

        /// <summary>
        /// Calculate wave arrival times
        /// </summary>
        public (double pTime, double sTime, double loveTime, double rayleighTime) CalculateArrivalTimes(
            double sourceLat, double sourceLon, double sourceDepthKm,
            double receiverLat, double receiverLon)
        {
            // Calculate epicentral distance (simplified great circle)
            double lat1 = sourceLat * Math.PI / 180.0;
            double lon1 = sourceLon * Math.PI / 180.0;
            double lat2 = receiverLat * Math.PI / 180.0;
            double lon2 = receiverLon * Math.PI / 180.0;

            double dlat = lat2 - lat1;
            double dlon = lon2 - lon1;

            double a = Math.Sin(dlat / 2) * Math.Sin(dlat / 2) +
                      Math.Cos(lat1) * Math.Cos(lat2) *
                      Math.Sin(dlon / 2) * Math.Sin(dlon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            double distance_km = 6371.0 * c; // Earth radius

            // Get average velocities along path
            var (avgVp, avgVs, _) = _crustalModel.GetAverageProperties(
                (sourceLat + receiverLat) / 2,
                (sourceLon + receiverLon) / 2,
                0, sourceDepthKm);

            // Travel times
            double pTime = distance_km / avgVp;
            double sTime = distance_km / avgVs;

            // Surface waves are slower (approximately 90% of S-wave velocity)
            double rayleighVelocity = avgVs * 0.92;
            double loveVelocity = avgVs * 0.95;

            double rayleighTime = distance_km / rayleighVelocity;
            double loveTime = distance_km / loveVelocity;

            return (pTime, sTime, loveTime, rayleighTime);
        }

        /// <summary>
        /// Get snapshot of the wavefield for visualization
        /// </summary>
        public double[,] GetSurfaceSnapshot()
        {
            var snapshot = new double[_nx, _ny];

            Parallel.For(0, _nx, i =>
            {
                for (int j = 0; j < _ny; j++)
                {
                    // Surface displacement (k=0)
                    snapshot[i, j] = Math.Sqrt(
                        _ux[i, j, 0] * _ux[i, j, 0] +
                        _uy[i, j, 0] * _uy[i, j, 0] +
                        _uz[i, j, 0] * _uz[i, j, 0]);
                }
            });

            return snapshot;
        }
    }
}
