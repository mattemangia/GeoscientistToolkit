using Microsoft.AspNetCore.Mvc;
using GeoscientistToolkit.Network;

namespace GeoscientistToolkit.NodeEndpoint.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PNMController : ControllerBase
{
    private readonly NodeManager _nodeManager;
    private readonly JobTracker _jobTracker;

    public PNMController(NodeManager nodeManager, JobTracker jobTracker)
    {
        _nodeManager = nodeManager;
        _jobTracker = jobTracker;
    }

    /// <summary>
    /// Generate Pore Network Model from CT scan
    /// </summary>
    [HttpPost("generate")]
    public IActionResult GeneratePNM([FromBody] PNMGenerationRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "PNMGeneration",
                Parameters = new Dictionary<string, object>
                {
                    ["ctVolumePath"] = request.CtVolumePath ?? "",
                    ["materialId"] = request.MaterialId,
                    ["neighborhood"] = request.Neighborhood ?? "N26",
                    ["generationMode"] = request.GenerationMode ?? "Conservative",
                    ["useOpenCL"] = request.UseOpenCL,
                    ["enforceConnectivity"] = request.EnforceInletOutletConnectivity,
                    ["flowAxis"] = request.FlowAxis ?? "Z",
                    ["inletIsMinSide"] = request.InletIsMinSide,
                    ["outletIsMaxSide"] = request.OutletIsMaxSide,
                    ["conservativeErosions"] = request.ConservativeErosions,
                    ["aggressiveErosions"] = request.AggressiveErosions,
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "PNM generation job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Calculate absolute permeability from PNM
    /// </summary>
    [HttpPost("permeability")]
    public IActionResult CalculatePermeability([FromBody] PermeabilityCalculationRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "PermeabilityCalculation",
                Parameters = new Dictionary<string, object>
                {
                    ["pnmDatasetPath"] = request.PnmDatasetPath ?? "",
                    ["method"] = request.Method ?? "DirectSolver",
                    ["flowAxis"] = request.FlowAxis ?? "Z",
                    ["pressureDifference_Pa"] = request.PressureDifference_Pa,
                    ["fluidViscosity_Pas"] = request.FluidViscosity_Pas,
                    ["temperature_C"] = request.Temperature_C,
                    ["useOpenCL"] = request.UseOpenCL,
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Permeability calculation job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Calculate molecular diffusivity from PNM
    /// </summary>
    [HttpPost("diffusivity")]
    public IActionResult CalculateDiffusivity([FromBody] DiffusivityCalculationRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "DiffusivityCalculation",
                Parameters = new Dictionary<string, object>
                {
                    ["pnmDatasetPath"] = request.PnmDatasetPath ?? "",
                    ["bulkDiffusivity"] = request.BulkDiffusivity_m2_per_s,
                    ["numberOfWalkers"] = request.NumberOfWalkers,
                    ["numberOfSteps"] = request.NumberOfSteps,
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Diffusivity calculation job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Run reactive transport simulation on PNM
    /// </summary>
    [HttpPost("reactive-transport")]
    public IActionResult RunReactiveTransport([FromBody] ReactiveTransportRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "PNMReactiveTransport",
                Parameters = new Dictionary<string, object>
                {
                    ["pnmDatasetPath"] = request.PnmDatasetPath ?? "",
                    ["fluidComposition"] = request.FluidComposition ?? new Dictionary<string, object>(),
                    ["mineralComposition"] = request.MineralComposition ?? new Dictionary<string, object>(),
                    ["temperature_C"] = request.Temperature_C,
                    ["pressure_Pa"] = request.Pressure_Pa,
                    ["flowRate"] = request.FlowRate_m3_per_s,
                    ["simulationTime"] = request.SimulationTime_s,
                    ["timeStep"] = request.TimeStep_s,
                    ["updateGeometry"] = request.UpdateGeometry,
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Reactive transport simulation job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get available PNM operation types
    /// </summary>
    [HttpGet("types")]
    public IActionResult GetPNMOperationTypes()
    {
        var types = new[]
        {
            new { type = "PNMGeneration", description = "Generate pore network model from CT scan" },
            new { type = "PermeabilityCalculation", description = "Calculate absolute permeability using direct solver or iterative methods" },
            new { type = "DiffusivityCalculation", description = "Calculate molecular diffusivity using random walk method" },
            new { type = "PNMReactiveTransport", description = "Simulate reactive transport with mineral dissolution/precipitation" }
        };

        return Ok(types);
    }

    /// <summary>
    /// Get available generation modes
    /// </summary>
    [HttpGet("generation-modes")]
    public IActionResult GetGenerationModes()
    {
        var modes = new[]
        {
            new { mode = "Conservative", description = "Less aggressive pore detection (1 erosion), better for poorly connected samples" },
            new { mode = "Aggressive", description = "More aggressive pore detection (3 erosions), better for well-connected samples" }
        };

        return Ok(modes);
    }

    /// <summary>
    /// Get available neighborhoods for PNM generation
    /// </summary>
    [HttpGet("neighborhoods")]
    public IActionResult GetNeighborhoods()
    {
        var neighborhoods = new[]
        {
            new { type = "N6", connectivity = 6, description = "Face neighbors only" },
            new { type = "N18", connectivity = 18, description = "Face and edge neighbors" },
            new { type = "N26", connectivity = 26, description = "Face, edge, and corner neighbors (most connected)" }
        };

        return Ok(neighborhoods);
    }

    /// <summary>
    /// Get available permeability calculation methods
    /// </summary>
    [HttpGet("permeability-methods")]
    public IActionResult GetPermeabilityMethods()
    {
        var methods = new[]
        {
            new { method = "DirectSolver", description = "Direct linear solver (LU decomposition), accurate but memory intensive" },
            new { method = "IterativeSolver", description = "Iterative solver (Conjugate Gradient), better for large networks" },
            new { method = "KozenyCarman", description = "Empirical Kozeny-Carman equation, fast approximation" }
        };

        return Ok(methods);
    }
}

// Request models
public class PNMGenerationRequest
{
    public string? CtVolumePath { get; set; }
    public int MaterialId { get; set; } = 1;
    public string? Neighborhood { get; set; } = "N26";  // N6, N18, N26
    public string? GenerationMode { get; set; } = "Conservative";  // Conservative, Aggressive
    public bool UseOpenCL { get; set; } = false;

    // Connectivity options
    public bool EnforceInletOutletConnectivity { get; set; } = false;
    public string? FlowAxis { get; set; } = "Z";  // X, Y, Z
    public bool InletIsMinSide { get; set; } = true;
    public bool OutletIsMaxSide { get; set; } = true;

    // Aggressiveness controls
    public int ConservativeErosions { get; set; } = 1;
    public int AggressiveErosions { get; set; } = 3;

    public string? OutputPath { get; set; }
}

public class PermeabilityCalculationRequest
{
    public string? PnmDatasetPath { get; set; }
    public string? Method { get; set; } = "DirectSolver";  // DirectSolver, IterativeSolver, KozenyCarman
    public string? FlowAxis { get; set; } = "Z";  // X, Y, Z
    public double PressureDifference_Pa { get; set; } = 100000.0;  // 1 bar
    public double FluidViscosity_Pas { get; set; } = 0.001;  // Water at 20°C
    public double Temperature_C { get; set; } = 20.0;
    public bool UseOpenCL { get; set; } = false;
    public string? OutputPath { get; set; }
}

public class DiffusivityCalculationRequest
{
    public string? PnmDatasetPath { get; set; }
    public double BulkDiffusivity_m2_per_s { get; set; } = 2.299e-9;  // CO2 in water at 25°C
    public int NumberOfWalkers { get; set; } = 50000;
    public int NumberOfSteps { get; set; } = 2000;
    public string? OutputPath { get; set; }
}

public class ReactiveTransportRequest
{
    public string? PnmDatasetPath { get; set; }
    public Dictionary<string, object>? FluidComposition { get; set; }
    public Dictionary<string, object>? MineralComposition { get; set; }
    public double Temperature_C { get; set; } = 25.0;
    public double Pressure_Pa { get; set; } = 101325.0;
    public double FlowRate_m3_per_s { get; set; } = 1e-9;
    public double SimulationTime_s { get; set; } = 3600.0;
    public double TimeStep_s { get; set; } = 10.0;
    public bool UpdateGeometry { get; set; } = true;
    public string? OutputPath { get; set; }
}
