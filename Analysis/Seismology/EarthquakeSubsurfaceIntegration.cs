using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using GeoscientistToolkit.Data.GIS;

namespace GeoscientistToolkit.Analysis.Seismology
{
    /// <summary>
    /// Represents seismic data at a subsurface voxel location over time
    /// </summary>
    public class SeismicVoxelData
    {
        public Vector3 Position { get; set; }
        public double[] DisplacementHistory { get; set; } = Array.Empty<double>();
        public double[] VelocityHistory { get; set; } = Array.Empty<double>();
        public double[] AccelerationHistory { get; set; } = Array.Empty<double>();
        public double[] StressHistory { get; set; } = Array.Empty<double>();
        public double PeakDisplacement { get; set; }
        public double PeakVelocity { get; set; }
        public double PeakAcceleration { get; set; }
        public double PeakStress { get; set; }
        public double TimeOfPeakArrival { get; set; }
    }

    /// <summary>
    /// Integration between earthquake simulation and subsurface GIS
    /// Allows visualization of earthquake propagation in 3D subsurface
    /// </summary>
    public static class EarthquakeSubsurfaceIntegration
    {
        /// <summary>
        /// Create a subsurface GIS dataset from earthquake simulation results
        /// showing the evolution of seismic waves through the subsurface
        /// </summary>
        public static SubsurfaceGISDataset CreateSeismicSubsurfaceDataset(
            EarthquakeSimulationEngine engine,
            EarthquakeSimulationParameters parameters,
            EarthquakeSimulationResults results,
            int subsurfaceResolutionX = 50,
            int subsurfaceResolutionY = 50,
            int subsurfaceResolutionZ = 30)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            if (results == null)
                throw new ArgumentNullException(nameof(results));
            if (subsurfaceResolutionX < 1 || subsurfaceResolutionY < 1 || subsurfaceResolutionZ < 1)
                throw new ArgumentException("Resolution values must be at least 1");

            var dataset = new SubsurfaceGISDataset(
                $"Earthquake_M{parameters.MomentMagnitude:F1}_Subsurface",
                $"earthquake_subsurface_M{parameters.MomentMagnitude:F1}.subgis"
            );

            // Set grid parameters based on simulation domain
            dataset.GridOrigin = new Vector3(
                (float)parameters.MinLongitude,
                (float)parameters.MinLatitude,
                -(float)parameters.MaxDepthKm
            );

            dataset.GridSize = new Vector3(
                (float)(parameters.MaxLongitude - parameters.MinLongitude),
                (float)(parameters.MaxLatitude - parameters.MinLatitude),
                (float)parameters.MaxDepthKm
            );

            dataset.GridResolutionX = subsurfaceResolutionX;
            dataset.GridResolutionY = subsurfaceResolutionY;
            dataset.GridResolutionZ = subsurfaceResolutionZ;

            dataset.VoxelSize = new Vector3(
                dataset.GridSize.X / subsurfaceResolutionX,
                dataset.GridSize.Y / subsurfaceResolutionY,
                dataset.GridSize.Z / subsurfaceResolutionZ
            );

            // Create voxels from earthquake results
            BuildSeismicVoxelGrid(dataset, parameters, results);

            // Add earthquake metadata
            dataset.Metadata["EarthquakeMagnitude"] = parameters.MomentMagnitude.ToString();
            dataset.Metadata["EpicenterLat"] = parameters.EpicenterLatitude.ToString();
            dataset.Metadata["EpicenterLon"] = parameters.EpicenterLongitude.ToString();
            dataset.Metadata["HypocenterDepth"] = parameters.HypocenterDepthKm.ToString();
            dataset.Metadata["SimulationDuration"] = parameters.SimulationDurationSeconds.ToString();

            return dataset;
        }

        /// <summary>
        /// Build voxel grid from earthquake simulation wave snapshots
        /// </summary>
        private static void BuildSeismicVoxelGrid(
            SubsurfaceGISDataset dataset,
            EarthquakeSimulationParameters parameters,
            EarthquakeSimulationResults results)
        {
            if (results.WaveSnapshots == null || results.WaveSnapshots.Count == 0)
            {
                BuildStaticSeismicVoxelGrid(dataset, parameters, results);
                return;
            }

            // Build time-varying seismic data
            for (int i = 0; i < dataset.GridResolutionX; i++)
            {
                for (int j = 0; j < dataset.GridResolutionY; j++)
                {
                    for (int k = 0; k < dataset.GridResolutionZ; k++)
                    {
                        Vector3 position = new Vector3(
                            dataset.GridOrigin.X + (i + 0.5f) * dataset.VoxelSize.X,
                            dataset.GridOrigin.Y + (j + 0.5f) * dataset.VoxelSize.Y,
                            dataset.GridOrigin.Z + (k + 0.5f) * dataset.VoxelSize.Z
                        );

                        var voxel = CreateSeismicVoxel(position, i, j, k, parameters, results, dataset.GridResolutionX, dataset.GridResolutionY);
                        if (voxel != null)
                        {
                            dataset.VoxelGrid.Add(voxel);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Build static voxel grid when no time snapshots are available
        /// </summary>
        private static void BuildStaticSeismicVoxelGrid(
            SubsurfaceGISDataset dataset,
            EarthquakeSimulationParameters parameters,
            EarthquakeSimulationResults results)
        {
            for (int i = 0; i < dataset.GridResolutionX; i++)
            {
                double lat = parameters.MinLatitude + (i / (double)(dataset.GridResolutionX - 1)) *
                           (parameters.MaxLatitude - parameters.MinLatitude);

                for (int j = 0; j < dataset.GridResolutionY; j++)
                {
                    double lon = parameters.MinLongitude + (j / (double)(dataset.GridResolutionY - 1)) *
                               (parameters.MaxLongitude - parameters.MinLongitude);

                    for (int k = 0; k < dataset.GridResolutionZ; k++)
                    {
                        double depth = (k / (double)(dataset.GridResolutionZ - 1)) * parameters.MaxDepthKm;

                        Vector3 position = new Vector3((float)lon, (float)lat, -(float)depth);

                        var voxel = new SubsurfaceVoxel
                        {
                            Position = position,
                            LithologyType = "Seismic",
                            Parameters = new Dictionary<string, float>()
                        };

                        // Map grid indices to result arrays
                        int gridI = Math.Clamp(i * parameters.GridNX / dataset.GridResolutionX, 0, parameters.GridNX - 1);
                        int gridJ = Math.Clamp(j * parameters.GridNY / dataset.GridResolutionY, 0, parameters.GridNY - 1);

                        // Add peak ground motion data
                        if (results.PeakGroundAcceleration != null && k == 0)
                        {
                            voxel.Parameters["PGA_g"] = (float)results.PeakGroundAcceleration[gridI, gridJ];
                        }

                        if (results.PeakGroundVelocity != null && k == 0)
                        {
                            voxel.Parameters["PGV_cm_s"] = (float)results.PeakGroundVelocity[gridI, gridJ];
                        }

                        if (results.PeakGroundDisplacement != null && k == 0)
                        {
                            voxel.Parameters["PGD_cm"] = (float)results.PeakGroundDisplacement[gridI, gridJ];
                        }

                        // Add wave arrival times
                        if (results.PWaveArrivalTime != null)
                        {
                            voxel.Parameters["P_Wave_Arrival_s"] = (float)results.PWaveArrivalTime[gridI, gridJ];
                        }

                        if (results.SWaveArrivalTime != null)
                        {
                            voxel.Parameters["S_Wave_Arrival_s"] = (float)results.SWaveArrivalTime[gridI, gridJ];
                        }

                        // Add wave amplitudes
                        if (results.PWaveAmplitude != null)
                        {
                            voxel.Parameters["P_Wave_Amplitude_m"] = (float)results.PWaveAmplitude[gridI, gridJ];
                        }

                        if (results.SWaveAmplitude != null)
                        {
                            voxel.Parameters["S_Wave_Amplitude_m"] = (float)results.SWaveAmplitude[gridI, gridJ];
                        }

                        // Add damage information for surface voxels
                        if (k == 0 && results.DamageRatioMap != null)
                        {
                            voxel.Parameters["Damage_Ratio"] = (float)results.DamageRatioMap[gridI, gridJ];
                        }

                        if (k == 0 && results.MMIMap != null)
                        {
                            voxel.Parameters["MMI"] = (float)results.MMIMap[gridI, gridJ];
                        }

                        // Add fracture information
                        if (results.FractureDensityMap != null && k < parameters.GridNZ)
                        {
                            int depthIdx = Math.Clamp(k * parameters.GridNZ / dataset.GridResolutionZ, 0, parameters.GridNZ - 1);
                            // Note: FractureDensityMap is 2D surface, extend to 3D with depth attenuation
                            double surfaceFractureDensity = results.FractureDensityMap[gridI, gridJ];
                            double depthAttenuation = Math.Exp(-depth / 10.0); // Exponential decay with depth
                            voxel.Parameters["Fracture_Density"] = (float)(surfaceFractureDensity * depthAttenuation);
                        }

                        // Calculate distance from hypocenter
                        double dx = (lon - parameters.EpicenterLongitude) * 111.0; // Rough km conversion
                        double dy = (lat - parameters.EpicenterLatitude) * 111.0;
                        double dz = depth - parameters.HypocenterDepthKm;
                        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        voxel.Parameters["Distance_From_Hypocenter_km"] = (float)distance;

                        // Set confidence based on distance (closer to epicenter = higher confidence)
                        voxel.Confidence = (float)Math.Max(0.1, 1.0 - distance / (parameters.MaxDepthKm * 2));

                        dataset.VoxelGrid.Add(voxel);
                    }
                }
            }
        }

        /// <summary>
        /// Create a seismic voxel with time-varying data
        /// </summary>
        private static SubsurfaceVoxel? CreateSeismicVoxel(
            Vector3 position,
            int i, int j, int k,
            EarthquakeSimulationParameters parameters,
            EarthquakeSimulationResults results,
            int gridResolutionX,
            int gridResolutionY)
        {
            var voxel = new SubsurfaceVoxel
            {
                Position = position,
                LithologyType = "Seismic",
                Parameters = new Dictionary<string, float>()
            };

            // Extract time history from wave snapshots
            if (results.WaveSnapshots != null && results.WaveSnapshots.Count > 0)
            {
                var displacementHistory = new List<double>();
                double maxDisplacement = 0;

                foreach (var snapshot in results.WaveSnapshots)
                {
                    if (snapshot.SurfaceDisplacement != null && k == 0)
                    {
                        // For surface voxels, use surface displacement
                        // Map subsurface grid indices to simulation grid indices
                        int gridI = Math.Clamp(i * parameters.GridNX / Math.Max(1, gridResolutionX), 0, parameters.GridNX - 1);
                        int gridJ = Math.Clamp(j * parameters.GridNY / Math.Max(1, gridResolutionY), 0, parameters.GridNY - 1);

                        if (gridI < snapshot.SurfaceDisplacement.GetLength(0) &&
                            gridJ < snapshot.SurfaceDisplacement.GetLength(1))
                        {
                            double disp = snapshot.SurfaceDisplacement[gridI, gridJ];
                            displacementHistory.Add(disp);
                            maxDisplacement = Math.Max(maxDisplacement, Math.Abs(disp));
                        }
                    }
                }

                voxel.Parameters["Max_Displacement_m"] = (float)maxDisplacement;
                voxel.Parameters["Snapshot_Count"] = results.WaveSnapshots.Count;
            }

            return voxel;
        }

        /// <summary>
        /// Extract seismic time series data for a specific location
        /// </summary>
        public static SeismicVoxelData ExtractSeismicTimeSeries(
            Vector3 position,
            EarthquakeSimulationParameters parameters,
            EarthquakeSimulationResults results)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            var data = new SeismicVoxelData
            {
                Position = position
            };

            if (results.WaveSnapshots == null || results.WaveSnapshots.Count == 0)
            {
                return data;
            }

            // Convert position to grid indices
            double latFrac = (position.Y - parameters.MinLatitude) / (parameters.MaxLatitude - parameters.MinLatitude);
            double lonFrac = (position.X - parameters.MinLongitude) / (parameters.MaxLongitude - parameters.MinLongitude);

            int i = Math.Clamp((int)(latFrac * (parameters.GridNX - 1)), 0, parameters.GridNX - 1);
            int j = Math.Clamp((int)(lonFrac * (parameters.GridNY - 1)), 0, parameters.GridNY - 1);

            // Extract time histories
            var displacements = new List<double>();
            var velocities = new List<double>();

            foreach (var snapshot in results.WaveSnapshots)
            {
                if (snapshot.SurfaceDisplacement != null &&
                    i < snapshot.SurfaceDisplacement.GetLength(0) &&
                    j < snapshot.SurfaceDisplacement.GetLength(1))
                {
                    displacements.Add(snapshot.SurfaceDisplacement[i, j]);
                }

                if (snapshot.SurfaceVelocity != null &&
                    i < snapshot.SurfaceVelocity.GetLength(0) &&
                    j < snapshot.SurfaceVelocity.GetLength(1))
                {
                    velocities.Add(snapshot.SurfaceVelocity[i, j]);
                }
            }

            data.DisplacementHistory = displacements.ToArray();
            data.VelocityHistory = velocities.ToArray();

            // Calculate derived quantities
            if (displacements.Count > 0)
            {
                data.PeakDisplacement = displacements.Max(Math.Abs);
                int peakIndex = displacements.FindIndex(d => Math.Abs(d) == data.PeakDisplacement);
                if (peakIndex >= 0)
                {
                    data.TimeOfPeakArrival = results.WaveSnapshots[peakIndex].TimeSeconds;
                }
            }

            if (velocities.Count > 0)
            {
                data.PeakVelocity = velocities.Max(Math.Abs);
            }

            // Calculate acceleration from velocity
            if (velocities.Count > 1)
            {
                var accelerations = new List<double>();
                double dt = results.WaveSnapshots[1].TimeSeconds - results.WaveSnapshots[0].TimeSeconds;

                // Avoid division by zero
                if (Math.Abs(dt) > 1e-10)
                {
                    for (int idx = 1; idx < velocities.Count; idx++)
                    {
                        accelerations.Add((velocities[idx] - velocities[idx - 1]) / dt);
                    }
                    data.AccelerationHistory = accelerations.ToArray();
                    data.PeakAcceleration = accelerations.Count > 0 ? accelerations.Max(Math.Abs) : 0;
                }
            }

            return data;
        }

        /// <summary>
        /// Create 3D visualization layers showing earthquake wave propagation at different depths
        /// </summary>
        public static List<GISRasterLayer> CreateDepthSliceLayers(
            SubsurfaceGISDataset seismicDataset,
            string parameterName = "P_Wave_Amplitude_m",
            int numberOfDepthSlices = 5)
        {
            if (seismicDataset == null)
                throw new ArgumentNullException(nameof(seismicDataset));
            if (string.IsNullOrWhiteSpace(parameterName))
                throw new ArgumentException("Parameter name cannot be null or empty", nameof(parameterName));
            if (numberOfDepthSlices < 1)
                throw new ArgumentException("Number of depth slices must be at least 1", nameof(numberOfDepthSlices));

            var layers = new List<GISRasterLayer>();

            if (seismicDataset.VoxelGrid.Count == 0)
                return layers;

            // Determine depth range
            float minZ = seismicDataset.VoxelGrid.Min(v => v.Position.Z);
            float maxZ = seismicDataset.VoxelGrid.Max(v => v.Position.Z);
            float depthRange = maxZ - minZ;

            for (int slice = 0; slice < numberOfDepthSlices; slice++)
            {
                float targetDepth = minZ + (slice / (float)(numberOfDepthSlices - 1)) * depthRange;
                float depthTolerance = depthRange / (numberOfDepthSlices * 2);

                var sliceVoxels = seismicDataset.VoxelGrid
                    .Where(v => Math.Abs(v.Position.Z - targetDepth) < depthTolerance)
                    .ToList();

                if (sliceVoxels.Count == 0)
                    continue;

                // Create raster grid for this depth slice
                int gridSize = 100;
                float minX = sliceVoxels.Min(v => v.Position.X);
                float maxX = sliceVoxels.Max(v => v.Position.X);
                float minY = sliceVoxels.Min(v => v.Position.Y);
                float maxY = sliceVoxels.Max(v => v.Position.Y);

                var rasterData = new float[gridSize, gridSize];

                for (int i = 0; i < gridSize; i++)
                {
                    for (int j = 0; j < gridSize; j++)
                    {
                        float x = minX + (i / (float)Math.Max(1, gridSize - 1)) * (maxX - minX);
                        float y = minY + (j / (float)Math.Max(1, gridSize - 1)) * (maxY - minY);

                        // Find nearest voxel
                        var nearestVoxel = sliceVoxels
                            .OrderBy(v => Math.Abs(v.Position.X - x) + Math.Abs(v.Position.Y - y))
                            .FirstOrDefault();

                        if (nearestVoxel != null && nearestVoxel.Parameters.ContainsKey(parameterName))
                        {
                            rasterData[i, j] = nearestVoxel.Parameters[parameterName];
                        }
                    }
                }

                var bounds = new BoundingBox
                {
                    Min = new Vector2(minX, minY),
                    Max = new Vector2(maxX, maxY)
                };

                var layer = new GISRasterLayer(rasterData, bounds)
                {
                    Name = $"Seismic_Depth_{-targetDepth:F0}m_{parameterName}"
                };

                // Store metadata in Properties dictionary
                layer.Properties["Depth"] = $"{-targetDepth:F1}m";
                layer.Properties["Parameter"] = parameterName;
                layer.Properties["TargetDepth"] = targetDepth;

                layers.Add(layer);
            }

            return layers;
        }
    }
}
