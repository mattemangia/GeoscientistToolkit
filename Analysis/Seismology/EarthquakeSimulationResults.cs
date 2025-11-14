using System;
using System.Collections.Generic;

namespace GeoscientistToolkit.Analysis.Seismology
{
    /// <summary>
    /// Results from earthquake simulation
    /// </summary>
    public class EarthquakeSimulationResults
    {
        // Simulation metadata
        public DateTime SimulationDate { get; set; } = DateTime.Now;
        public double SimulationDurationSeconds { get; set; }
        public double ComputationTimeSeconds { get; set; }
        public int TotalTimeSteps { get; set; }

        // Source parameters (copied from input)
        public double EpicenterLatitude { get; set; }
        public double EpicenterLongitude { get; set; }
        public double HypocenterDepthKm { get; set; }
        public double MomentMagnitude { get; set; }
        public double StrikeDegrees { get; set; }
        public double DipDegrees { get; set; }
        public double RakeDegrees { get; set; }

        // Domain info
        public double MinLatitude { get; set; }
        public double MaxLatitude { get; set; }
        public double MinLongitude { get; set; }
        public double MaxLongitude { get; set; }
        public int GridNX { get; set; }
        public int GridNY { get; set; }
        public int GridNZ { get; set; }

        // Wave propagation results
        public double[,]? PeakGroundAcceleration { get; set; }  // g
        public double[,]? PeakGroundVelocity { get; set; }      // cm/s
        public double[,]? PeakGroundDisplacement { get; set; }  // cm

        // Wave arrival times
        public double[,]? PWaveArrivalTime { get; set; }        // seconds
        public double[,]? SWaveArrivalTime { get; set; }        // seconds
        public double[,]? LoveWaveArrivalTime { get; set; }     // seconds
        public double[,]? RayleighWaveArrivalTime { get; set; } // seconds

        // Wave amplitudes
        public double[,]? PWaveAmplitude { get; set; }          // m
        public double[,]? SWaveAmplitude { get; set; }          // m
        public double[,]? LoveWaveAmplitude { get; set; }       // m
        public double[,]? RayleighWaveAmplitude { get; set; }   // m

        // Damage assessment
        public DamageState[,]? DamageMap { get; set; }
        public double[,]? DamageRatioMap { get; set; }
        public int[,]? MMIMap { get; set; }  // Modified Mercalli Intensity

        // Fracture analysis
        public double[,]? FractureDensityMap { get; set; }
        public double[,]? CoulombStressMap { get; set; }  // Surface projection

        // Wave snapshots (time series)
        public List<WaveSnapshot>? WaveSnapshots { get; set; }

        // Statistics
        public double MaxPGA { get; set; }
        public double MaxPGV { get; set; }
        public double MaxPGD { get; set; }
        public double AffectedAreaKm2 { get; set; }  // Area with PGA > 0.1g
        public int EstimatedBuildingsAffected { get; set; }

        /// <summary>
        /// Calculate summary statistics
        /// </summary>
        public void CalculateStatistics()
        {
            if (PeakGroundAcceleration != null)
            {
                MaxPGA = 0.0;
                double pgaThreshold = 0.1; // g
                int affectedCells = 0;

                for (int i = 0; i < GridNX; i++)
                {
                    for (int j = 0; j < GridNY; j++)
                    {
                        double pga = PeakGroundAcceleration[i, j];
                        if (pga > MaxPGA)
                            MaxPGA = pga;

                        if (pga > pgaThreshold)
                            affectedCells++;
                    }
                }

                // Calculate affected area
                double cellAreaKm2 = ((MaxLatitude - MinLatitude) / GridNX) *
                                    ((MaxLongitude - MinLongitude) / GridNY) *
                                    111.0 * 111.0; // rough km conversion

                AffectedAreaKm2 = affectedCells * cellAreaKm2;
            }

            if (PeakGroundVelocity != null)
            {
                MaxPGV = 0.0;
                for (int i = 0; i < GridNX; i++)
                {
                    for (int j = 0; j < GridNY; j++)
                    {
                        if (PeakGroundVelocity[i, j] > MaxPGV)
                            MaxPGV = PeakGroundVelocity[i, j];
                    }
                }
            }

            if (PeakGroundDisplacement != null)
            {
                MaxPGD = 0.0;
                for (int i = 0; i < GridNX; i++)
                {
                    for (int j = 0; j < GridNY; j++)
                    {
                        if (PeakGroundDisplacement[i, j] > MaxPGD)
                            MaxPGD = PeakGroundDisplacement[i, j];
                    }
                }
            }

            // Estimate affected buildings (very rough: 100 buildings per km²)
            EstimatedBuildingsAffected = (int)(AffectedAreaKm2 * 100);
        }

        /// <summary>
        /// Get result summary as string
        /// </summary>
        public string GetSummary()
        {
            return $@"Earthquake Simulation Results
================================
Magnitude: M{MomentMagnitude:F1}
Epicenter: {EpicenterLatitude:F3}°, {EpicenterLongitude:F3}°
Depth: {HypocenterDepthKm:F1} km
Mechanism: Strike={StrikeDegrees:F0}° Dip={DipDegrees:F0}° Rake={RakeDegrees:F0}°

Ground Motion:
- Max PGA: {MaxPGA:F3} g
- Max PGV: {MaxPGV:F1} cm/s
- Max PGD: {MaxPGD:F1} cm

Impact:
- Affected area: {AffectedAreaKm2:F1} km²
- Est. buildings affected: {EstimatedBuildingsAffected:N0}

Computation:
- Simulation time: {SimulationDurationSeconds:F1} s
- Computation time: {ComputationTimeSeconds:F2} s
- Time steps: {TotalTimeSteps:N0}
- Grid: {GridNX}×{GridNY}×{GridNZ}
";
        }

        /// <summary>
        /// Export results to CSV for GIS integration
        /// </summary>
        public void ExportToCSV(string basePath)
        {
            // Export PGA map
            if (PeakGroundAcceleration != null)
            {
                using var writer = new System.IO.StreamWriter($"{basePath}_pga.csv");
                writer.WriteLine("Latitude,Longitude,PGA_g");

                for (int i = 0; i < GridNX; i++)
                {
                    double lat = MinLatitude + (i / (double)(GridNX - 1)) * (MaxLatitude - MinLatitude);
                    for (int j = 0; j < GridNY; j++)
                    {
                        double lon = MinLongitude + (j / (double)(GridNY - 1)) * (MaxLongitude - MinLongitude);
                        writer.WriteLine($"{lat:F6},{lon:F6},{PeakGroundAcceleration[i, j]:F6}");
                    }
                }
            }

            // Export damage map
            if (DamageMap != null)
            {
                using var writer = new System.IO.StreamWriter($"{basePath}_damage.csv");
                writer.WriteLine("Latitude,Longitude,DamageRatio,MMI,PGA_g,PGV_cms,PGD_cm");

                for (int i = 0; i < GridNX; i++)
                {
                    for (int j = 0; j < GridNY; j++)
                    {
                        var damage = DamageMap[i, j];
                        int mmi = MMIMap != null ? MMIMap[i, j] : 0;

                        writer.WriteLine($"{damage.Latitude:F6},{damage.Longitude:F6}," +
                                       $"{damage.DamageRatio:F3},{mmi}," +
                                       $"{damage.PeakGroundAcceleration:F6}," +
                                       $"{damage.PeakGroundVelocity:F2}," +
                                       $"{damage.PeakGroundDisplacement:F2}");
                    }
                }
            }

            // Export wave arrival times
            if (PWaveArrivalTime != null)
            {
                using var writer = new System.IO.StreamWriter($"{basePath}_arrivals.csv");
                writer.WriteLine("Latitude,Longitude,P_time,S_time,Love_time,Rayleigh_time");

                for (int i = 0; i < GridNX; i++)
                {
                    double lat = MinLatitude + (i / (double)(GridNX - 1)) * (MaxLatitude - MinLatitude);
                    for (int j = 0; j < GridNY; j++)
                    {
                        double lon = MinLongitude + (j / (double)(GridNY - 1)) * (MaxLongitude - MinLongitude);

                        double pTime = PWaveArrivalTime[i, j];
                        double sTime = SWaveArrivalTime?[i, j] ?? 0;
                        double loveTime = LoveWaveArrivalTime?[i, j] ?? 0;
                        double rayleighTime = RayleighWaveArrivalTime?[i, j] ?? 0;

                        writer.WriteLine($"{lat:F6},{lon:F6},{pTime:F2},{sTime:F2},{loveTime:F2},{rayleighTime:F2}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Snapshot of wavefield at a specific time
    /// </summary>
    public class WaveSnapshot
    {
        public double TimeSeconds { get; set; }
        public double[,] SurfaceDisplacement { get; set; } = new double[0, 0];
        public double[,] SurfaceVelocity { get; set; } = new double[0, 0];
    }
}
