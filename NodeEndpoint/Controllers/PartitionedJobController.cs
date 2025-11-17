using Microsoft.AspNetCore.Mvc;
using GeoscientistToolkit.NodeEndpoint.Services;

namespace GeoscientistToolkit.NodeEndpoint.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PartitionedJobController : ControllerBase
{
    private readonly JobPartitioner _jobPartitioner;
    private readonly DataReferenceService _dataReferenceService;

    public PartitionedJobController(JobPartitioner jobPartitioner, DataReferenceService dataReferenceService)
    {
        _jobPartitioner = jobPartitioner;
        _dataReferenceService = dataReferenceService;
    }

    /// <summary>
    /// Register a data file and get a reference ID (avoids transmitting large files)
    /// </summary>
    [HttpPost("register-data")]
    public IActionResult RegisterData([FromBody] DataRegistrationRequest request)
    {
        try
        {
            var metadata = new Dictionary<string, object>();

            // Add dimensional metadata for volumetric data
            if (request.Width.HasValue) metadata["width"] = request.Width.Value;
            if (request.Height.HasValue) metadata["height"] = request.Height.Value;
            if (request.Depth.HasValue) metadata["depth"] = request.Depth.Value;
            if (request.VoxelSize.HasValue) metadata["voxelSize"] = request.VoxelSize.Value;

            var referenceId = _dataReferenceService.RegisterDataFile(
                request.FilePath ?? "",
                request.DataType,
                metadata
            );

            // Optionally copy to shared storage immediately
            string? sharedPath = null;
            if (request.CopyToSharedStorage)
            {
                sharedPath = _dataReferenceService.EnsureInSharedStorage(referenceId);
            }

            return Ok(new
            {
                referenceId,
                dataType = request.DataType.ToString(),
                sharedPath,
                message = "Data registered successfully"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Submit a partitioned job that will be split across multiple nodes
    /// </summary>
    [HttpPost("submit")]
    public IActionResult SubmitPartitionedJob([FromBody] PartitionedJobRequest request)
    {
        try
        {
            var options = new PartitioningOptions
            {
                DataReferenceId = request.DataReferenceId,
                PartitionStrategy = request.PartitionStrategy,
                ExplicitPartitionCount = request.PartitionCount,
                ResultAggregationStrategy = request.AggregationStrategy
            };

            var parentJobId = _jobPartitioner.SubmitPartitionedJob(
                request.JobType ?? "Custom",
                request.Parameters ?? new Dictionary<string, object>(),
                options
            );

            return Ok(new
            {
                parentJobId,
                message = "Partitioned job submitted",
                status = "running"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get status of a partitioned job
    /// </summary>
    [HttpGet("{parentJobId}/status")]
    public IActionResult GetPartitionedJobStatus(string parentJobId)
    {
        var job = _jobPartitioner.GetPartitionedJob(parentJobId);
        if (job == null)
        {
            return NotFound(new { error = "Partitioned job not found" });
        }

        var completedPartitions = job.SubJobs.Count(sj => sj.Status == SubJobStatus.Completed);
        var failedPartitions = job.SubJobs.Count(sj => sj.Status == SubJobStatus.Failed);
        var runningPartitions = job.SubJobs.Count(sj => sj.Status == SubJobStatus.Running);

        return Ok(new
        {
            parentJobId = job.ParentJobId,
            status = job.Status.ToString(),
            totalPartitions = job.TotalPartitions,
            completedPartitions,
            failedPartitions,
            runningPartitions,
            submittedAt = job.SubmittedAt,
            completedAt = job.CompletedAt,
            progress = (double)completedPartitions / job.TotalPartitions * 100
        });
    }

    /// <summary>
    /// Get aggregated results from a completed partitioned job
    /// </summary>
    [HttpGet("{parentJobId}/result")]
    public IActionResult GetPartitionedJobResult(string parentJobId)
    {
        var job = _jobPartitioner.GetPartitionedJob(parentJobId);
        if (job == null)
        {
            return NotFound(new { error = "Partitioned job not found" });
        }

        if (job.Status != PartitionedJobStatus.Completed)
        {
            return Accepted(new
            {
                parentJobId,
                status = job.Status.ToString(),
                message = "Job is still running or has not completed successfully"
            });
        }

        var result = _jobPartitioner.GetAggregatedResults(parentJobId);
        return Ok(new
        {
            parentJobId,
            status = "Completed",
            result,
            aggregationStrategy = job.ResultAggregationStrategy.ToString(),
            totalPartitions = job.TotalPartitions,
            completedAt = job.CompletedAt
        });
    }

    /// <summary>
    /// Get detailed information about all sub-jobs
    /// </summary>
    [HttpGet("{parentJobId}/sub-jobs")]
    public IActionResult GetSubJobs(string parentJobId)
    {
        var job = _jobPartitioner.GetPartitionedJob(parentJobId);
        if (job == null)
        {
            return NotFound(new { error = "Partitioned job not found" });
        }

        var subJobsInfo = job.SubJobs.Select(sj => new
        {
            jobId = sj.JobId,
            partitionId = sj.PartitionId,
            status = sj.Status.ToString(),
            submittedAt = sj.SubmittedAt,
            completedAt = sj.CompletedAt,
            hasResult = sj.Result != null,
            error = sj.Error
        });

        return Ok(new
        {
            parentJobId,
            totalPartitions = job.TotalPartitions,
            subJobs = subJobsInfo
        });
    }

    /// <summary>
    /// Get information about a data reference
    /// </summary>
    [HttpGet("data/{referenceId}")]
    public IActionResult GetDataReference(string referenceId)
    {
        var reference = _dataReferenceService.GetReference(referenceId);
        if (reference == null)
        {
            return NotFound(new { error = "Data reference not found" });
        }

        return Ok(new
        {
            referenceId = reference.ReferenceId,
            dataType = reference.DataType.ToString(),
            originalPath = reference.OriginalPath,
            sharedPath = reference.SharedPath,
            fileSize = reference.FileSize,
            metadata = reference.Metadata,
            registeredAt = reference.RegisteredAt
        });
    }
}

// Request models
public class DataRegistrationRequest
{
    public string? FilePath { get; set; }
    public DataType DataType { get; set; }
    public bool CopyToSharedStorage { get; set; } = true;

    // Optional metadata for volumetric data
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? Depth { get; set; }
    public double? VoxelSize { get; set; }
}

public class PartitionedJobRequest
{
    public string? JobType { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
    public string? DataReferenceId { get; set; }
    public PartitionStrategy PartitionStrategy { get; set; } = PartitionStrategy.SpatialZ;
    public int? PartitionCount { get; set; }
    public ResultAggregationStrategy AggregationStrategy { get; set; } = ResultAggregationStrategy.Concatenate;
}
