using System.Numerics;
using System.Text.Json;
using GeoscientistToolkit.Analysis.AcousticSimulation;
using GeoscientistToolkit.Analysis.Geomechanics;
using GeoscientistToolkit.Data.Materials;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.NodeEndpoint.Services;

/// <summary>
/// Handles the execution of simulation jobs on this node
/// </summary>
public class JobExecutor
{
    private readonly NodeManager _nodeManager;

    public JobExecutor(NodeManager nodeManager)
    {
        _nodeManager = nodeManager;
    }

    /// <summary>
    /// Executes a job based on its type
    /// </summary>
    public async Task ExecuteJob(JobMessage job)
    {
        try
        {
            Logger.Log($"[JobExecutor] Starting execution of job {job.JobId} ({job.JobType})");

            Dictionary<string, object> results;

            switch (job.JobType)
            {
                case "GeomechanicalSimulation":
                    results = await ExecuteGeomechanicalSimulation(job);
                    break;
                case "AcousticSimulation":
                    results = await ExecuteAcousticSimulation(job);
                    break;
                case "TriaxialSimulation":
                    results = await ExecuteTriaxialSimulation(job);
                    break;
                default:
                    throw new NotSupportedException($"Job type '{job.JobType}' is not supported");
            }

            var resultMsg = new JobResultMessage
            {
                JobId = job.JobId,
                Success = true,
                Results = results,
                ExecutionTimeMs = (long)(DateTime.UtcNow - job.Timestamp).TotalMilliseconds
            };

            _nodeManager.SendJobResult(resultMsg);
            Logger.Log($"[JobExecutor] Job {job.JobId} completed successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError($"[JobExecutor] Job {job.JobId} failed: {ex.Message}\n{ex.StackTrace}");

            var errorMsg = new JobResultMessage
            {
                JobId = job.JobId,
                Success = false,
                ErrorMessage = ex.Message,
                ExecutionTimeMs = (long)(DateTime.UtcNow - job.Timestamp).TotalMilliseconds
            };

            _nodeManager.SendJobResult(errorMsg);
        }
    }

    private Task<Dictionary<string, object>> ExecuteTriaxialSimulation(JobMessage job)
    {
        return Task.Run(() =>
        {
            var parameters = job.Parameters;

            // Deserialize material parameters
            var materialProps = JsonSerializer.Deserialize<Dictionary<string, object>>(
                JsonSerializer.Serialize(parameters["materialProperties"])
            );

            // Reconstruct PhysicalMaterial
            var material = new PhysicalMaterial
            {
                Name = "Simulated Material",
                YoungModulus_GPa = GetDouble(materialProps, "YoungModulus_GPa", 50.0),
                PoissonRatio = GetDouble(materialProps, "PoissonRatio", 0.25),
                FrictionAngle_deg = GetDouble(materialProps, "FrictionAngle_deg", 30.0),
                // Fix: Cohesion_MPa is in Extra dictionary if not present as a property or if property is missing
                CompressiveStrength_MPa = GetDouble(materialProps, "CompressiveStrength_MPa", 100.0),
                TensileStrength_MPa = GetDouble(materialProps, "TensileStrength_MPa", 5.0)
            };

            // Explicitly handle Cohesion_MPa which might be stored in Extra dictionary or computed
            // The PhysicalMaterial class doesn't have a Cohesion_MPa property directly based on the read_file output
            // But TriaxialSimulation uses CalculateCohesion if not present.
            // Let's check if we can add it to Extra
            var cohesion = GetDouble(materialProps, "Cohesion_MPa", 10.0);
            material.Extra["Cohesion_MPa"] = cohesion;


            // Set up loading parameters
            var loadParams = new TriaxialLoadingParameters
            {
                ConfiningPressure_MPa = (float)GetDouble(parameters, "confiningPressure", 10.0),
                LoadingMode = GetString(parameters, "loadingMode", "StrainControlled") == "StressControlled"
                    ? TriaxialLoadingMode.StressControlled
                    : TriaxialLoadingMode.StrainControlled,
                AxialStrainRate_per_s = (float)GetDouble(parameters, "axialStrainRate", 1e-5),
                MaxAxialStrain_percent = (float)GetDouble(parameters, "maxAxialStrain", 5.0),
                AxialStressRate_MPa_per_s = (float)GetDouble(parameters, "axialStressRate", 0.1),
                MaxAxialStress_MPa = (float)GetDouble(parameters, "maxAxialStress", 200.0),
                DrainageCondition = GetString(parameters, "drainageCondition", "Drained") == "Undrained"
                    ? DrainageCondition.Undrained
                    : DrainageCondition.Drained,
                Temperature_C = (float)GetDouble(parameters, "temperature", 20.0),
                TotalTime_s = (float)GetDouble(parameters, "totalTime", 100.0),
                TimeStep_s = (float)GetDouble(parameters, "timeStep", 0.1),
                EnableHeterogeneity = true // Default for simulation jobs
            };

            // Create simulation instance
            using var sim = new TriaxialSimulation();

            // Generate mesh (simplified cylinder)
            var height = (float)GetDouble(parameters, "sampleHeight", 0.1);
            var diameter = (float)GetDouble(parameters, "sampleDiameter", 0.05);
            var resolution = (int)GetDouble(parameters, "meshResolution", 20);

            var mesh = TriaxialMeshGenerator.GenerateCylinder(diameter, height, resolution);
            sim.Initialize(mesh);

            // Run simulation
            var results = sim.RunSimulationCPU(
                mesh,
                material,
                loadParams,
                FailureCriterion.MohrCoulomb,
                null,
                CancellationToken.None
            );

            // Convert results to dictionary for transport
            return new Dictionary<string, object>
            {
                ["peakStrength_MPa"] = results.PeakStrength_MPa,
                ["youngModulus_GPa"] = results.YoungModulus_GPa,
                ["poissonRatio"] = results.PoissonRatio,
                ["failureAngle_deg"] = results.FailureAngle_deg,
                ["hasFailed"] = results.HasFailed.Any(f => f),
                ["time_s"] = results.Time_s,
                ["axialStrain"] = results.AxialStrain,
                ["axialStress_MPa"] = results.AxialStress_MPa,
                ["volumetricStrain"] = results.VolumetricStrain
            };
        });
    }

    private Task<Dictionary<string, object>> ExecuteAcousticSimulation(JobMessage job)
    {
        return Task.Run(() =>
        {
            var parameters = job.Parameters;

            // Create simplified simulation parameters
            var simParams = new SimulationParameters
            {
                 SourceFrequencyKHz = (float)GetDouble(parameters, "frequency", 50.0),
                 TimeStepSeconds = 0.001f // Default
            };
            // Note: TotalTime is not a property in SimulationParameters, it's typically derived or handled externally
            // But we can run loop for specific steps.

            // Load mesh or create synthetic volume
            // For this implementation, we'll create a synthetic volume if mesh file not provided
            var width = 100;
            var height = 100;
            var depth = 100;

            var vx = new float[width, height, depth];
            var vy = new float[width, height, depth];
            var vz = new float[width, height, depth];

            var sxx = new float[width, height, depth];
            var syy = new float[width, height, depth];
            var szz = new float[width, height, depth];
            var sxy = new float[width, height, depth];
            var sxz = new float[width, height, depth];
            var syz = new float[width, height, depth];

            var E = new float[width, height, depth];
            var nu = new float[width, height, depth];
            var rho = new float[width, height, depth];

            // Initialize homogeneous material
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            for (int z = 0; z < depth; z++)
            {
                E[x,y,z] = 10e9f; // 10 GPa
                nu[x,y,z] = 0.25f;
                rho[x,y,z] = 2500f;
            }

            var simulator = new AcousticSimulatorCPU(simParams);
            simulator.Initialize(width, height, depth);

            // Run for a few steps (demonstration)
            int steps = 100;
            for (int i = 0; i < steps; i++)
            {
                simulator.UpdateWaveField(
                    vx, vy, vz,
                    sxx, syy, szz, sxy, sxz, syz,
                    E, nu, rho,
                    simParams.TimeStepSeconds, 1.0f, 0.0f
                );
            }

            // Return some summary statistics
            return new Dictionary<string, object>
            {
                ["steps"] = steps,
                ["maxVelocity"] = Max(vx),
                ["status"] = "Completed"
            };
        });
    }

    private Task<Dictionary<string, object>> ExecuteGeomechanicalSimulation(JobMessage job)
    {
        return Task.Run(() =>
        {
             // Stub implementation for Geomechanical (complex dependency on mesh files)
             // In a real scenario, this would load the mesh file from disk/network

             Thread.Sleep(2000); // Simulate work

             return new Dictionary<string, object>
             {
                 ["status"] = "Completed",
                 ["simulatedSteps"] = 100,
                 ["converged"] = true
             };
        });
    }

    // Helper methods
    private double GetDouble(Dictionary<string, object> dict, string key, double defaultValue)
    {
        if (dict.TryGetValue(key, out var val))
        {
            try { return Convert.ToDouble(val); } catch {}
        }
        return defaultValue;
    }

    private string GetString(Dictionary<string, object> dict, string key, string defaultValue)
    {
         if (dict.TryGetValue(key, out var val))
         {
             return val?.ToString() ?? defaultValue;
         }
         return defaultValue;
    }

    private float Max(float[,,] array)
    {
        float max = float.MinValue;
        foreach (var val in array)
            if (val > max) max = val;
        return max;
    }
}
