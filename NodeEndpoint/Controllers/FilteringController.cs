using Microsoft.AspNetCore.Mvc;
using GeoscientistToolkit.Network;

namespace GeoscientistToolkit.NodeEndpoint.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilteringController : ControllerBase
{
    private readonly NodeManager _nodeManager;
    private readonly JobTracker _jobTracker;

    public FilteringController(NodeManager nodeManager, JobTracker jobTracker)
    {
        _nodeManager = nodeManager;
        _jobTracker = jobTracker;
    }

    /// <summary>
    /// Apply a filter to a CT volume
    /// </summary>
    [HttpPost("apply")]
    public IActionResult ApplyFilter([FromBody] FilterRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "CTFiltering",
                Parameters = new Dictionary<string, object>
                {
                    ["volumePath"] = request.VolumePath ?? "",
                    ["filterType"] = request.FilterType ?? "Gaussian",
                    ["kernelSize"] = request.KernelSize,
                    ["sigma"] = request.Sigma,
                    ["iterations"] = request.Iterations,
                    ["useGPU"] = request.UseGPU,
                    ["subVolumeMode"] = request.SubVolumeMode,
                    ["subVolumeStart"] = request.SubVolumeStart ?? new int[3],
                    ["subVolumeSize"] = request.SubVolumeSize ?? new int[3],
                    ["outputPath"] = request.OutputPath ?? "",
                    ["is3D"] = request.Is3D
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Filter job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Apply multiple filters in sequence (pipeline)
    /// </summary>
    [HttpPost("pipeline")]
    public IActionResult ApplyFilterPipeline([FromBody] FilterPipelineRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "CTFilteringPipeline",
                Parameters = new Dictionary<string, object>
                {
                    ["volumePath"] = request.VolumePath ?? "",
                    ["filters"] = request.Filters ?? new List<FilterStep>(),
                    ["useGPU"] = request.UseGPU,
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Filter pipeline job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Apply edge detection to CT volume
    /// </summary>
    [HttpPost("edge-detection")]
    public IActionResult ApplyEdgeDetection([FromBody] EdgeDetectionRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "CTEdgeDetection",
                Parameters = new Dictionary<string, object>
                {
                    ["volumePath"] = request.VolumePath ?? "",
                    ["method"] = request.Method ?? "Sobel",
                    ["threshold1"] = request.Threshold1,
                    ["threshold2"] = request.Threshold2,
                    ["useGPU"] = request.UseGPU,
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Edge detection job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Apply segmentation to CT volume
    /// </summary>
    [HttpPost("segmentation")]
    public IActionResult ApplySegmentation([FromBody] SegmentationRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = "CTSegmentation",
                Parameters = new Dictionary<string, object>
                {
                    ["volumePath"] = request.VolumePath ?? "",
                    ["method"] = request.Method ?? "Threshold",
                    ["thresholdValue"] = request.ThresholdValue,
                    ["minSize"] = request.MinSize,
                    ["maxSize"] = request.MaxSize,
                    ["useGPU"] = request.UseGPU,
                    ["outputPath"] = request.OutputPath ?? ""
                }
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Segmentation job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get available filter types
    /// </summary>
    [HttpGet("types")]
    public IActionResult GetFilterTypes()
    {
        var types = new[]
        {
            new { type = "Gaussian", description = "Gaussian blur filter for noise reduction" },
            new { type = "Median", description = "Median filter for salt-and-pepper noise" },
            new { type = "Mean", description = "Mean filter for smoothing" },
            new { type = "NonLocalMeans", description = "Advanced denoising preserving details" },
            new { type = "EdgeSobel", description = "Sobel edge detection" },
            new { type = "EdgeCanny", description = "Canny edge detection" },
            new { type = "UnsharpMask", description = "Sharpening filter" },
            new { type = "Bilateral", description = "Edge-preserving smoothing" }
        };

        return Ok(types);
    }
}

// Request models
public class FilterRequest
{
    public string? VolumePath { get; set; }
    public string? FilterType { get; set; }
    public int KernelSize { get; set; } = 3;
    public double Sigma { get; set; } = 1.0;
    public int Iterations { get; set; } = 1;
    public bool UseGPU { get; set; } = true;
    public bool SubVolumeMode { get; set; } = false;
    public int[]? SubVolumeStart { get; set; }
    public int[]? SubVolumeSize { get; set; }
    public string? OutputPath { get; set; }
    public bool Is3D { get; set; } = true;
}

public class FilterPipelineRequest
{
    public string? VolumePath { get; set; }
    public List<FilterStep>? Filters { get; set; }
    public bool UseGPU { get; set; } = true;
    public string? OutputPath { get; set; }
}

public class FilterStep
{
    public required string FilterType { get; set; }
    public int KernelSize { get; set; } = 3;
    public double Sigma { get; set; } = 1.0;
    public int Iterations { get; set; } = 1;
}

public class EdgeDetectionRequest
{
    public string? VolumePath { get; set; }
    public string? Method { get; set; }
    public double Threshold1 { get; set; } = 100;
    public double Threshold2 { get; set; } = 200;
    public bool UseGPU { get; set; } = true;
    public string? OutputPath { get; set; }
}

public class SegmentationRequest
{
    public string? VolumePath { get; set; }
    public string? Method { get; set; }
    public double ThresholdValue { get; set; } = 128;
    public int MinSize { get; set; } = 100;
    public int MaxSize { get; set; } = int.MaxValue;
    public bool UseGPU { get; set; } = true;
    public string? OutputPath { get; set; }
}
