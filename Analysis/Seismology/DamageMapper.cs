using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace GeoscientistToolkit.Analysis.Seismology
{
    /// <summary>
    /// Damage severity classification
    /// </summary>
    public enum DamageSeverity
    {
        None = 0,
        Minor = 1,
        Moderate = 2,
        Extensive = 3,
        Complete = 4
    }

    /// <summary>
    /// Building vulnerability class (European Macroseismic Scale)
    /// </summary>
    public enum VulnerabilityClass
    {
        A,  // Very vulnerable (adobe, rubble stone)
        B,  // Vulnerable (unreinforced masonry)
        C,  // Medium vulnerability (old reinforced concrete)
        D,  // Low vulnerability (modern reinforced concrete)
        E,  // Very low vulnerability (seismically designed)
        F   // Extremely low (base isolated)
    }

    /// <summary>
    /// Damage state at a location
    /// </summary>
    public struct DamageState
    {
        public double Latitude;
        public double Longitude;
        public double PeakGroundAcceleration; // in g
        public double PeakGroundVelocity;     // in cm/s
        public double PeakGroundDisplacement; // in cm
        public double SpectralAcceleration;   // SA(T=1.0s) in g
        public DamageSeverity Severity;
        public double DamageRatio;            // 0-1, fraction of structural damage
        public double[] FragilityCurve;       // Probability of exceeding damage states
    }

    /// <summary>
    /// Maps seismic ground motion to structural damage
    /// Based on HAZUS methodology and empirical fragility curves
    /// </summary>
    public class DamageMapper
    {
        private readonly double[,] _pga;   // Peak ground acceleration grid
        private readonly double[,] _pgv;   // Peak ground velocity grid
        private readonly double[,] _pgd;   // Peak ground displacement grid
        private readonly int _nx, _ny;

        public DamageMapper(int nx, int ny)
        {
            _nx = nx;
            _ny = ny;
            _pga = new double[nx, ny];
            _pgv = new double[nx, ny];
            _pgd = new double[nx, ny];
        }

        /// <summary>
        /// Update ground motion parameters from wavefield
        /// </summary>
        public void UpdateGroundMotion(WavePropagationEngine waveEngine, double dt)
        {
            Parallel.For(0, _nx, i =>
            {
                for (int j = 0; j < _ny; j++)
                {
                    var wf = waveEngine.GetWaveFieldAt(i, j, 0); // Surface

                    // Peak ground acceleration (convert to g)
                    double acc = Math.Sqrt(
                        wf.VelocityX * wf.VelocityX +
                        wf.VelocityY * wf.VelocityY +
                        wf.VelocityZ * wf.VelocityZ) / dt / 9.81;

                    if (acc > _pga[i, j])
                        _pga[i, j] = acc;

                    // Peak ground velocity (m/s to cm/s)
                    double vel = Math.Sqrt(
                        wf.VelocityX * wf.VelocityX +
                        wf.VelocityY * wf.VelocityY +
                        wf.VelocityZ * wf.VelocityZ) * 100.0;

                    if (vel > _pgv[i, j])
                        _pgv[i, j] = vel;

                    // Peak ground displacement (m to cm)
                    double disp = wf.Amplitude * 100.0;

                    if (disp > _pgd[i, j])
                        _pgd[i, j] = disp;
                }
            });
        }

        /// <summary>
        /// Calculate damage for a specific vulnerability class
        /// Uses fragility curves from HAZUS-MH
        /// </summary>
        public DamageState CalculateDamage(
            int i, int j,
            double latitude, double longitude,
            VulnerabilityClass vulnClass)
        {
            double pga = _pga[i, j];
            double pgv = _pgv[i, j];
            double pgd = _pgd[i, j];

            // Calculate spectral acceleration (simplified)
            double sa = CalculateSpectralAcceleration(pga, 1.0);

            // Fragility curve parameters (median and dispersion)
            // [Slight, Moderate, Extensive, Complete]
            double[] medians = GetFragilityMedians(vulnClass);
            double beta = 0.6; // Log-normal standard deviation

            // Calculate probabilities of exceeding each damage state
            double[] fragilities = new double[4];
            for (int ds = 0; ds < 4; ds++)
            {
                fragilities[ds] = CalculateLognormalCDF(pga, medians[ds], beta);
            }

            // Determine damage severity
            DamageSeverity severity = DetermineS severity(fragilities);

            // Calculate damage ratio (0-1)
            double damageRatio = CalculateDamageRatio(severity, fragilities);

            return new DamageState
            {
                Latitude = latitude,
                Longitude = longitude,
                PeakGroundAcceleration = pga,
                PeakGroundVelocity = pgv,
                PeakGroundDisplacement = pgd,
                SpectralAcceleration = sa,
                Severity = severity,
                DamageRatio = damageRatio,
                FragilityCurve = fragilities
            };
        }

        /// <summary>
        /// Get fragility curve medians for vulnerability class
        /// Values in g (PGA)
        /// </summary>
        private double[] GetFragilityMedians(VulnerabilityClass vulnClass)
        {
            return vulnClass switch
            {
                VulnerabilityClass.A => new[] { 0.10, 0.20, 0.40, 0.80 },
                VulnerabilityClass.B => new[] { 0.15, 0.30, 0.60, 1.20 },
                VulnerabilityClass.C => new[] { 0.20, 0.40, 0.80, 1.60 },
                VulnerabilityClass.D => new[] { 0.25, 0.50, 1.00, 2.00 },
                VulnerabilityClass.E => new[] { 0.35, 0.70, 1.40, 2.80 },
                VulnerabilityClass.F => new[] { 0.50, 1.00, 2.00, 4.00 },
                _ => new[] { 0.20, 0.40, 0.80, 1.60 }
            };
        }

        /// <summary>
        /// Calculate lognormal CDF for fragility
        /// </summary>
        private double CalculateLognormalCDF(double x, double median, double beta)
        {
            if (x <= 0) return 0.0;

            double z = (Math.Log(x) - Math.Log(median)) / beta;
            // Standard normal CDF approximation
            return 0.5 * (1.0 + Erf(z / Math.Sqrt(2.0)));
        }

        /// <summary>
        /// Error function approximation
        /// </summary>
        private double Erf(double x)
        {
            double a1 = 0.254829592;
            double a2 = -0.284496736;
            double a3 = 1.421413741;
            double a4 = -1.453152027;
            double a5 = 1.061405429;
            double p = 0.3275911;

            int sign = x < 0 ? -1 : 1;
            x = Math.Abs(x);

            double t = 1.0 / (1.0 + p * x);
            double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

            return sign * y;
        }

        /// <summary>
        /// Determine damage severity from fragility probabilities
        /// </summary>
        private DamageSeverity DetermineSeverity(double[] fragilities)
        {
            // Use threshold approach
            if (fragilities[3] > 0.5) return DamageSeverity.Complete;
            if (fragilities[2] > 0.5) return DamageSeverity.Extensive;
            if (fragilities[1] > 0.5) return DamageSeverity.Moderate;
            if (fragilities[0] > 0.5) return DamageSeverity.Minor;
            return DamageSeverity.None;
        }

        /// <summary>
        /// Calculate continuous damage ratio
        /// </summary>
        private double CalculateDamageRatio(DamageSeverity severity, double[] fragilities)
        {
            // Central damage factors for each state
            double[] centralDamageFactors = { 0.05, 0.20, 0.50, 0.80, 1.00 };

            // Weighted average based on probabilities
            double damageRatio = 0.0;

            // Probability of being in each discrete state
            double pNone = 1.0 - fragilities[0];
            double pMinor = fragilities[0] - fragilities[1];
            double pModerate = fragilities[1] - fragilities[2];
            double pExtensive = fragilities[2] - fragilities[3];
            double pComplete = fragilities[3];

            damageRatio += pNone * centralDamageFactors[0];
            damageRatio += pMinor * centralDamageFactors[1];
            damageRatio += pModerate * centralDamageFactors[2];
            damageRatio += pExtensive * centralDamageFactors[3];
            damageRatio += pComplete * centralDamageFactors[4];

            return Math.Clamp(damageRatio, 0.0, 1.0);
        }

        /// <summary>
        /// Calculate spectral acceleration at period T
        /// Simplified response spectrum
        /// </summary>
        private double CalculateSpectralAcceleration(double pga, double period)
        {
            // Simplified response spectrum (ASCE 7 style)
            double T0 = 0.2;  // Corner periods
            double Ts = 1.0;

            if (period <= T0)
            {
                return pga * (1.0 + (2.5 - 1.0) * period / T0);
            }
            else if (period <= Ts)
            {
                return 2.5 * pga;
            }
            else
            {
                return 2.5 * pga * Ts / period;
            }
        }

        /// <summary>
        /// Generate damage map for entire domain
        /// </summary>
        public DamageState[,] GenerateDamageMap(
            double minLat, double maxLat,
            double minLon, double maxLon,
            VulnerabilityClass vulnClass)
        {
            var damageMap = new DamageState[_nx, _ny];

            Parallel.For(0, _nx, i =>
            {
                double lat = minLat + (i / (double)(_nx - 1)) * (maxLat - minLat);

                for (int j = 0; j < _ny; j++)
                {
                    double lon = minLon + (j / (double)(_ny - 1)) * (maxLon - minLon);
                    damageMap[i, j] = CalculateDamage(i, j, lat, lon, vulnClass);
                }
            });

            return damageMap;
        }

        /// <summary>
        /// Calculate Modified Mercalli Intensity from ground motion
        /// </summary>
        public int CalculateMMI(double pga, double pgv)
        {
            // Empirical correlations (Wald et al., 1999)
            double mmi1 = 3.66 * Math.Log10(pga) + 2.20; // From PGA
            double mmi2 = 3.47 * Math.Log10(pgv) + 2.35; // From PGV

            // Average
            double mmi = (mmi1 + mmi2) / 2.0;

            return (int)Math.Round(Math.Clamp(mmi, 1.0, 12.0));
        }

        /// <summary>
        /// Get PGA map for visualization
        /// </summary>
        public double[,] GetPGAMap() => (double[,])_pga.Clone();

        /// <summary>
        /// Get PGV map
        /// </summary>
        public double[,] GetPGVMap() => (double[,])_pgv.Clone();

        /// <summary>
        /// Get PGD map
        /// </summary>
        public double[,] GetPGDMap() => (double[,])_pgd.Clone();
    }
}
