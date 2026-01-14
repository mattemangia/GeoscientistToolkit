using Microsoft.AspNetCore.Mvc;
using GeoscientistToolkit.Network;
using GeoscientistToolkit.Analysis.Geomechanics;
using GeoscientistToolkit.Analysis.AcousticSimulation;

namespace GeoscientistToolkit.NodeEndpoint.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SimulationController : ControllerBase
{
    private readonly NodeManager _nodeManager;
    private readonly JobTracker _jobTracker;

    public SimulationController(NodeManager nodeManager, JobTracker jobTracker)
    {
        _nodeManager = nodeManager;
        _jobTracker = jobTracker;
    }

    /// <summary>
    /// Submit a geomechanical simulation job
    /// </summary>
    [HttpPost("geomechanical")]
    public IActionResult SubmitGeomechanicalSimulation([FromBody] GeomechanicalSimulationRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "GeomechanicalSimulation",
                Parameters = new Dictionary<string, object>
                {
                    ["meshFile"] = request.MeshFile ?? "",
                    ["materialProperties"] = request.MaterialProperties ?? new Dictionary<string, object>(),
                    ["boundaryConditions"] = request.BoundaryConditions ?? new Dictionary<string, object>(),
                    ["solverSettings"] = request.SolverSettings ?? new Dictionary<string, object>(),
                    ["enablePlasticity"] = request.EnablePlasticity,
                    ["enableDamage"] = request.EnableDamage,
                    ["enableFluidCoupling"] = request.EnableFluidCoupling,
                    ["timeSteps"] = request.TimeSteps,
                    ["convergenceTolerance"] = request.ConvergenceTolerance,
                    ["applyGravity"] = request.ApplyGravity,
                    ["gravityX"] = request.GravityX,
                    ["gravityY"] = request.GravityY,
                    ["gravityZ"] = request.GravityZ,
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Geomechanical simulation job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Submit an acoustic simulation job
    /// </summary>
    [HttpPost("acoustic")]
    public IActionResult SubmitAcousticSimulation([FromBody] AcousticSimulationRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "AcousticSimulation",
                Parameters = new Dictionary<string, object>
                {
                    ["meshFile"] = request.MeshFile ?? "",
                    ["frequency"] = request.Frequency,
                    ["sourcePosition"] = request.SourcePosition ?? new double[3],
                    ["receiverPositions"] = request.ReceiverPositions ?? new List<double[]>(),
                    ["materialProperties"] = request.MaterialProperties ?? new Dictionary<string, object>(),
                    ["useGPU"] = request.UseGPU,
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Acoustic simulation job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Submit a triaxial compression/extension test simulation
    /// </summary>
    [HttpPost("triaxial")]
    public IActionResult SubmitTriaxialSimulation([FromBody] TriaxialSimulationRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "TriaxialSimulation",
                Parameters = new Dictionary<string, object>
                {
                    ["sampleHeight"] = request.SampleHeight,
                    ["sampleDiameter"] = request.SampleDiameter,
                    ["meshResolution"] = request.MeshResolution,
                    ["materialProperties"] = request.MaterialProperties ?? new Dictionary<string, object>(),
                    ["confiningPressure"] = request.ConfiningPressure_MPa,
                    ["loadingMode"] = request.LoadingMode ?? "StrainControlled",
                    ["axialStrainRate"] = request.AxialStrainRate_per_s,
                    ["maxAxialStrain"] = request.MaxAxialStrain_percent,
                    ["axialStressRate"] = request.AxialStressRate_MPa_per_s,
                    ["maxAxialStress"] = request.MaxAxialStress_MPa,
                    ["drainageCondition"] = request.DrainageCondition ?? "Drained",
                    ["temperature"] = request.Temperature_C,
                    ["totalTime"] = request.TotalTime_s,
                    ["timeStep"] = request.TimeStep_s,
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Triaxial simulation job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Submit a 2D geomechanical simulation job
    /// </summary>
    [HttpPost("geomech2d")]
    public IActionResult SubmitGeomech2DSimulation([FromBody] Geomech2DSimulationRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "Geomech2DSimulation",
                Priority = request.Priority,
                Parameters = new Dictionary<string, object>
                {
                    ["meshJson"] = request.MeshJson ?? "",
                    ["materialsJson"] = request.MaterialsJson ?? "",
                    ["boundaryConditionsJson"] = request.BoundaryConditionsJson ?? "",
                    ["loadsJson"] = request.LoadsJson ?? "",
                    ["analysisType"] = request.AnalysisType ?? "Static",
                    ["solverType"] = request.SolverType ?? "ConjugateGradient",
                    ["numLoadSteps"] = request.NumLoadSteps,
                    ["tolerance"] = request.Tolerance,
                    ["applyGravity"] = request.ApplyGravity,
                    ["enablePlasticity"] = request.EnablePlasticity,
                    ["partitionIndex"] = request.PartitionIndex,
                    ["boundaryNodeIds"] = request.BoundaryNodeIds ?? new List<int>(),
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "2D Geomechanical simulation job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Submit a 2D parameter sweep simulation job
    /// </summary>
    [HttpPost("geomech2d/parametersweep")]
    public IActionResult SubmitGeomech2DParameterSweep([FromBody] Geomech2DParameterSweepRequest request)
    {
        try
        {
            var parentJobId = Guid.NewGuid().ToString();
            var jobIds = new List<string>();

            // Create individual jobs for each parameter set
            foreach (var paramSet in request.ParameterSets)
            {
                var jobId = Guid.NewGuid().ToString();
                var job = new JobMessage
                {
                    JobId = jobId,
                    JobType = "Geomech2DParameterSweep",
                    Priority = request.Priority,
                    Parameters = new Dictionary<string, object>
                    {
                        ["parentJobId"] = parentJobId,
                        ["meshJson"] = request.BaseMeshJson ?? "",
                        ["materialsJson"] = request.BaseMaterialsJson ?? "",
                        ["boundaryConditionsJson"] = request.BaseBoundaryConditionsJson ?? "",
                        ["loadsJson"] = request.BaseLoadsJson ?? "",
                        ["parameterSet"] = paramSet,
                        ["analysisType"] = request.AnalysisType ?? "Static",
                        ["solverType"] = request.SolverType ?? "ConjugateGradient",
                        ["numLoadSteps"] = request.NumLoadSteps,
                        ["tolerance"] = request.Tolerance
                    }
                };

                _nodeManager.SubmitJob(job);
                _jobTracker.RegisterJob(job);
                jobIds.Add(jobId);
            }

            return Ok(new { parentJobId, jobIds, message = $"{jobIds.Count} parameter sweep jobs submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Submit a 2D Monte Carlo simulation job
    /// </summary>
    [HttpPost("geomech2d/montecarlo")]
    public IActionResult SubmitGeomech2DMonteCarlo([FromBody] Geomech2DMonteCarloRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "Geomech2DMonteCarlo",
                Priority = request.Priority,
                Parameters = new Dictionary<string, object>
                {
                    ["meshJson"] = request.MeshJson ?? "",
                    ["materialsJson"] = request.MaterialsJson ?? "",
                    ["boundaryConditionsJson"] = request.BoundaryConditionsJson ?? "",
                    ["loadsJson"] = request.LoadsJson ?? "",
                    ["randomParameters"] = request.RandomParameters ?? new Dictionary<string, double[]>(),
                    ["numSamples"] = request.NumSamples,
                    ["analysisType"] = request.AnalysisType ?? "Static",
                    ["solverType"] = request.SolverType ?? "ConjugateGradient",
                    ["numLoadSteps"] = request.NumLoadSteps,
                    ["tolerance"] = request.Tolerance,
                    ["seed"] = request.RandomSeed
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = $"Monte Carlo simulation with {request.NumSamples} samples submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Submit a partitioned 2D simulation (domain decomposition)
    /// </summary>
    [HttpPost("geomech2d/partitioned")]
    public IActionResult SubmitPartitionedGeomech2D([FromBody] Geomech2DPartitionedRequest request)
    {
        try
        {
            var parentJobId = Guid.NewGuid().ToString();
            var jobIds = new List<string>();

            // Create jobs for each partition
            for (int i = 0; i < request.NumPartitions; i++)
            {
                var jobId = Guid.NewGuid().ToString();
                var job = new JobMessage
                {
                    JobId = jobId,
                    JobType = "Geomech2DPartition",
                    Priority = request.Priority,
                    Parameters = new Dictionary<string, object>
                    {
                        ["parentJobId"] = parentJobId,
                        ["partitionIndex"] = i,
                        ["numPartitions"] = request.NumPartitions,
                        ["meshJson"] = request.MeshJson ?? "",
                        ["materialsJson"] = request.MaterialsJson ?? "",
                        ["boundaryConditionsJson"] = request.BoundaryConditionsJson ?? "",
                        ["loadsJson"] = request.LoadsJson ?? "",
                        ["analysisType"] = request.AnalysisType ?? "Static",
                        ["solverType"] = request.SolverType ?? "ConjugateGradient",
                        ["numLoadSteps"] = request.NumLoadSteps,
                        ["tolerance"] = request.Tolerance,
                        ["overlapNodes"] = request.OverlapNodes
                    }
                };

                _nodeManager.SubmitJob(job);
                _jobTracker.RegisterJob(job);
                jobIds.Add(jobId);
            }

            return Ok(new
            {
                parentJobId,
                jobIds,
                numPartitions = request.NumPartitions,
                message = $"Partitioned simulation with {request.NumPartitions} partitions submitted",
                status = "pending"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get available simulation types
    /// </summary>
    [HttpGet("types")]
    public IActionResult GetSimulationTypes()
    {
        var types = new[]
        {
            new { type = "GeomechanicalSimulation", description = "FEM-based geomechanical analysis with plasticity and damage" },
            new { type = "AcousticSimulation", description = "Acoustic wave propagation simulation" },
            new { type = "TriaxialSimulation", description = "Triaxial compression/extension test with multiple failure criteria" },
            new { type = "Geomech2DSimulation", description = "2D FEM geomechanical simulation with curved Mohr-Coulomb" },
            new { type = "Geomech2DParameterSweep", description = "2D parameter sweep for sensitivity analysis" },
            new { type = "Geomech2DMonteCarlo", description = "2D Monte Carlo probabilistic analysis" },
            new { type = "Geomech2DPartition", description = "2D domain decomposition for parallel solving" }
        };

        return Ok(types);
    }
}

// Additional request models for 2D geomechanics

public class Geomech2DSimulationRequest
{
    public string? MeshJson { get; set; }
    public string? MaterialsJson { get; set; }
    public string? BoundaryConditionsJson { get; set; }
    public string? LoadsJson { get; set; }
    public string? AnalysisType { get; set; } = "Static";
    public string? SolverType { get; set; } = "ConjugateGradient";
    public int NumLoadSteps { get; set; } = 10;
    public double Tolerance { get; set; } = 1e-6;
    public bool ApplyGravity { get; set; }
    public bool EnablePlasticity { get; set; } = true;
    public int PartitionIndex { get; set; } = -1;
    public List<int>? BoundaryNodeIds { get; set; }
    public int Priority { get; set; }
    public string? OutputPath { get; set; }

    /// <summary>Custom gravity X component (m/s²). Default: 0</summary>
    public float GravityX { get; set; } = 0;

    /// <summary>Custom gravity Y component (m/s²). Default: -9.81 (Earth downward)</summary>
    public float GravityY { get; set; } = -9.81f;

    /// <summary>Planetary preset for gravity. Options: earth, moon, mars, venus, jupiter, saturn, mercury</summary>
    public string? GravityPreset { get; set; }
}

public class Geomech2DParameterSweepRequest
{
    public string? BaseMeshJson { get; set; }
    public string? BaseMaterialsJson { get; set; }
    public string? BaseBoundaryConditionsJson { get; set; }
    public string? BaseLoadsJson { get; set; }
    public List<Dictionary<string, double>> ParameterSets { get; set; } = new();
    public string? AnalysisType { get; set; } = "Static";
    public string? SolverType { get; set; } = "ConjugateGradient";
    public int NumLoadSteps { get; set; } = 10;
    public double Tolerance { get; set; } = 1e-6;
    public int Priority { get; set; }
}

public class Geomech2DMonteCarloRequest
{
    public string? MeshJson { get; set; }
    public string? MaterialsJson { get; set; }
    public string? BoundaryConditionsJson { get; set; }
    public string? LoadsJson { get; set; }
    public Dictionary<string, double[]>? RandomParameters { get; set; }  // name -> [mean, stddev]
    public int NumSamples { get; set; } = 1000;
    public string? AnalysisType { get; set; } = "Static";
    public string? SolverType { get; set; } = "ConjugateGradient";
    public int NumLoadSteps { get; set; } = 10;
    public double Tolerance { get; set; } = 1e-6;
    public int RandomSeed { get; set; }
    public int Priority { get; set; }
}

public class Geomech2DPartitionedRequest
{
    public string? MeshJson { get; set; }
    public string? MaterialsJson { get; set; }
    public string? BoundaryConditionsJson { get; set; }
    public string? LoadsJson { get; set; }
    public int NumPartitions { get; set; } = 4;
    public int OverlapNodes { get; set; } = 2;
    public string? AnalysisType { get; set; } = "Static";
    public string? SolverType { get; set; } = "ConjugateGradient";
    public int NumLoadSteps { get; set; } = 10;
    public double Tolerance { get; set; } = 1e-6;
    public int Priority { get; set; }
}

// Request models
public class GeomechanicalSimulationRequest
{
    public string? MeshFile { get; set; }
    public Dictionary<string, object>? MaterialProperties { get; set; }
    public Dictionary<string, object>? BoundaryConditions { get; set; }
    public Dictionary<string, object>? SolverSettings { get; set; }
    public bool EnablePlasticity { get; set; }
    public bool EnableDamage { get; set; }
    public bool EnableFluidCoupling { get; set; }
    public int TimeSteps { get; set; } = 100;
    public double ConvergenceTolerance { get; set; } = 1e-6;
    public string? OutputPath { get; set; }
    public bool ApplyGravity { get; set; }
    public float GravityX { get; set; } = 0;
    public float GravityY { get; set; } = 0;
    public float GravityZ { get; set; } = -9.81f;
}

public class AcousticSimulationRequest
{
    public string? MeshFile { get; set; }
    public double Frequency { get; set; }
    public double[]? SourcePosition { get; set; }
    public List<double[]>? ReceiverPositions { get; set; }
    public Dictionary<string, object>? MaterialProperties { get; set; }
    public bool UseGPU { get; set; }
    public string? OutputPath { get; set; }
}

public class TriaxialSimulationRequest
{
    // Sample geometry
    public double SampleHeight { get; set; } = 0.1;  // meters
    public double SampleDiameter { get; set; } = 0.05;  // meters
    public int MeshResolution { get; set; } = 20;  // elements along height

    // Material properties
    public Dictionary<string, object>? MaterialProperties { get; set; }

    // Loading parameters
    public double ConfiningPressure_MPa { get; set; } = 10.0;
    public string? LoadingMode { get; set; } = "StrainControlled";  // StrainControlled or StressControlled

    // Strain-controlled parameters
    public double AxialStrainRate_per_s { get; set; } = 1e-5;
    public double MaxAxialStrain_percent { get; set; } = 5.0;

    // Stress-controlled parameters
    public double AxialStressRate_MPa_per_s { get; set; } = 0.1;
    public double MaxAxialStress_MPa { get; set; } = 200.0;

    // Drainage condition
    public string? DrainageCondition { get; set; } = "Drained";  // Drained or Undrained

    // Test conditions
    public double Temperature_C { get; set; } = 20.0;
    public double TotalTime_s { get; set; } = 100.0;
    public double TimeStep_s { get; set; } = 0.1;

    public string? OutputPath { get; set; }
}
