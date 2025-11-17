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
    /// Get available simulation types
    /// </summary>
    [HttpGet("types")]
    public IActionResult GetSimulationTypes()
    {
        var types = new[]
        {
            new { type = "GeomechanicalSimulation", description = "FEM-based geomechanical analysis with plasticity and damage" },
            new { type = "AcousticSimulation", description = "Acoustic wave propagation simulation" },
            new { type = "TriaxialSimulation", description = "Triaxial compression/extension test with multiple failure criteria" }
        };

        return Ok(types);
    }
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
    public string? OutputPath { get; set; }
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
