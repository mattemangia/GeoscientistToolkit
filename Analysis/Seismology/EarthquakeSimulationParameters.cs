using System;
using System.Numerics;

namespace GeoscientistToolkit.Analysis.Seismology
{
    /// <summary>
    /// Fault mechanism type
    /// </summary>
    public enum FaultMechanism
    {
        StrikeSlip,
        Normal,
        Reverse,
        Oblique
    }

    /// <summary>
    /// Parameters for earthquake simulation
    /// </summary>
    public class EarthquakeSimulationParameters
    {
        // Source location
        public double EpicenterLatitude { get; set; } = 37.0;
        public double EpicenterLongitude { get; set; } = -122.0;
        public double HypocenterDepthKm { get; set; } = 10.0;

        // Source characteristics
        public double MomentMagnitude { get; set; } = 6.0;
        public FaultMechanism Mechanism { get; set; } = FaultMechanism.StrikeSlip;

        // Focal mechanism (angles in degrees)
        public double StrikeDegrees { get; set; } = 0.0;
        public double DipDegrees { get; set; } = 90.0;
        public double RakeDegrees { get; set; } = 0.0;

        // Simulation domain
        public double MinLatitude { get; set; } = 36.0;
        public double MaxLatitude { get; set; } = 38.0;
        public double MinLongitude { get; set; } = -123.0;
        public double MaxLongitude { get; set; } = -121.0;
        public double MaxDepthKm { get; set; } = 50.0;

        // Grid resolution
        public int GridNX { get; set; } = 100;
        public int GridNY { get; set; } = 100;
        public int GridNZ { get; set; } = 50;

        // Time parameters
        public double SimulationDurationSeconds { get; set; } = 60.0;
        public double TimeStepSeconds { get; set; } = 0.01;

        // Crustal model
        public string CrustalModelPath { get; set; } = "Data/CrustalModels/GlobalCrustalModel.json";
        public string CrustalTypeOverride { get; set; } = ""; // Empty = auto-detect

        // Analysis options
        public bool CalculateDamage { get; set; } = true;
        public bool CalculateFractures { get; set; } = true;
        public bool TrackWaveTypes { get; set; } = true;

        // Damage assessment
        public VulnerabilityClass BuildingVulnerability { get; set; } = VulnerabilityClass.C;

        // Output options
        public bool SaveWaveSnapshots { get; set; } = true;
        public int SnapshotIntervalSteps { get; set; } = 100;
        public bool ExportResults { get; set; } = true;

        // Parallelization
        public int MaxParallelThreads { get; set; } = Environment.ProcessorCount;
        public bool UseGPUAcceleration { get; set; } = false;

        /// <summary>
        /// Convert degrees to radians
        /// </summary>
        public double StrikeRadians => StrikeDegrees * Math.PI / 180.0;
        public double DipRadians => DipDegrees * Math.PI / 180.0;
        public double RakeRadians => RakeDegrees * Math.PI / 180.0;

        /// <summary>
        /// Calculate grid spacing
        /// </summary>
        public double GridDX => (MaxLatitude - MinLatitude) / (GridNX - 1);
        public double GridDY => (MaxLongitude - MinLongitude) / (GridNY - 1);
        public double GridDZ => MaxDepthKm / (GridNZ - 1);

        /// <summary>
        /// Calculate seismic moment (N·m)
        /// </summary>
        public double SeismicMoment => Math.Pow(10, 1.5 * MomentMagnitude + 9.1);

        /// <summary>
        /// Estimate rupture area (km²) from moment magnitude
        /// Wells and Coppersmith (1994)
        /// </summary>
        public double EstimateRuptureArea()
        {
            return Math.Pow(10, MomentMagnitude - 3.98) / 0.98;
        }

        /// <summary>
        /// Estimate rupture length (km)
        /// </summary>
        public double EstimateRuptureLength()
        {
            return Math.Pow(10, 0.5 * MomentMagnitude - 1.88);
        }

        /// <summary>
        /// Estimate rupture width (km)
        /// </summary>
        public double EstimateRuptureWidth()
        {
            return Math.Pow(10, 0.5 * MomentMagnitude - 2.17);
        }

        /// <summary>
        /// Estimate average slip (m)
        /// </summary>
        public double EstimateAverageSlip()
        {
            return Math.Pow(10, 0.5 * MomentMagnitude - 1.36);
        }

        /// <summary>
        /// Estimate rupture duration (seconds)
        /// </summary>
        public double EstimateRuptureDuration()
        {
            // Rough estimate: rupture length / rupture velocity (typically 2-3 km/s)
            double ruptureVelocity = 2.5; // km/s
            return EstimateRuptureLength() / ruptureVelocity;
        }

        /// <summary>
        /// Set focal mechanism from fault type
        /// </summary>
        public void SetMechanismFromType()
        {
            switch (Mechanism)
            {
                case FaultMechanism.StrikeSlip:
                    StrikeDegrees = 0.0;
                    DipDegrees = 90.0;
                    RakeDegrees = 0.0;
                    break;

                case FaultMechanism.Normal:
                    StrikeDegrees = 0.0;
                    DipDegrees = 60.0;
                    RakeDegrees = -90.0;
                    break;

                case FaultMechanism.Reverse:
                    StrikeDegrees = 0.0;
                    DipDegrees = 30.0;
                    RakeDegrees = 90.0;
                    break;

                case FaultMechanism.Oblique:
                    StrikeDegrees = 0.0;
                    DipDegrees = 45.0;
                    RakeDegrees = 45.0;
                    break;
            }
        }

        /// <summary>
        /// Validate parameters
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            if (MomentMagnitude < 3.0 || MomentMagnitude > 9.5)
            {
                errorMessage = "Moment magnitude must be between 3.0 and 9.5";
                return false;
            }

            if (HypocenterDepthKm < 0 || HypocenterDepthKm > MaxDepthKm)
            {
                errorMessage = "Hypocenter depth must be within simulation domain";
                return false;
            }

            if (EpicenterLatitude < MinLatitude || EpicenterLatitude > MaxLatitude ||
                EpicenterLongitude < MinLongitude || EpicenterLongitude > MaxLongitude)
            {
                errorMessage = "Epicenter must be within simulation domain";
                return false;
            }

            if (GridNX < 10 || GridNY < 10 || GridNZ < 10)
            {
                errorMessage = "Grid resolution too low (minimum 10x10x10)";
                return false;
            }

            if (TimeStepSeconds <= 0 || TimeStepSeconds > 1.0)
            {
                errorMessage = "Time step must be positive and reasonable (< 1s)";
                return false;
            }

            // CFL condition check
            double maxVelocity = 8.5; // km/s (approximate mantle velocity)
            double minGridSpacing = Math.Min(Math.Min(GridDX, GridDY), GridDZ);
            double cflLimit = minGridSpacing / maxVelocity;

            if (TimeStepSeconds > cflLimit)
            {
                errorMessage = $"Time step violates CFL condition. Max allowed: {cflLimit:F4}s";
                return false;
            }

            errorMessage = "";
            return true;
        }

        /// <summary>
        /// Create default parameters for quick testing
        /// </summary>
        public static EarthquakeSimulationParameters CreateDefault()
        {
            return new EarthquakeSimulationParameters
            {
                EpicenterLatitude = 37.0,
                EpicenterLongitude = -122.0,
                HypocenterDepthKm = 10.0,
                MomentMagnitude = 6.0,
                Mechanism = FaultMechanism.StrikeSlip,
                MinLatitude = 36.0,
                MaxLatitude = 38.0,
                MinLongitude = -123.0,
                MaxLongitude = -121.0,
                MaxDepthKm = 50.0,
                GridNX = 50,
                GridNY = 50,
                GridNZ = 25,
                SimulationDurationSeconds = 60.0,
                TimeStepSeconds = 0.02,
                CalculateDamage = true,
                CalculateFractures = true,
                TrackWaveTypes = true
            };
        }
    }
}
