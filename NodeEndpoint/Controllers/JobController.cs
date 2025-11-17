using Microsoft.AspNetCore.Mvc;
using GeoscientistToolkit.Network;

namespace GeoscientistToolkit.NodeEndpoint.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobController : ControllerBase
{
    private readonly NodeManager _nodeManager;
    private readonly JobTracker _jobTracker;

    public JobController(NodeManager nodeManager, JobTracker jobTracker)
    {
        _nodeManager = nodeManager;
        _jobTracker = jobTracker;
    }

    /// <summary>
    /// Get job status by ID
    /// </summary>
    [HttpGet("{jobId}")]
    public IActionResult GetJobStatus(string jobId)
    {
        var job = _jobTracker.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = "Job not found" });
        }

        return Ok(new
        {
            jobId = job.JobId,
            status = job.Status.ToString(),
            submittedAt = job.SubmittedAt,
            completedAt = job.CompletedAt,
            jobType = job.JobMessage.JobType,
            hasResult = job.Result != null
        });
    }

    /// <summary>
    /// Get job result by ID
    /// </summary>
    [HttpGet("{jobId}/result")]
    public IActionResult GetJobResult(string jobId)
    {
        var job = _jobTracker.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = "Job not found" });
        }

        if (job.Result == null)
        {
            return Accepted(new
            {
                jobId = job.JobId,
                status = job.Status.ToString(),
                message = "Job is still running or pending"
            });
        }

        return Ok(new
        {
            jobId = job.JobId,
            status = job.Result.Status,
            result = job.Result.Result,
            executionTime = job.Result.ExecutionTime,
            completedAt = job.CompletedAt
        });
    }

    /// <summary>
    /// Wait for job completion (long polling)
    /// </summary>
    [HttpGet("{jobId}/wait")]
    public async Task<IActionResult> WaitForJobCompletion(string jobId, [FromQuery] int timeoutSeconds = 300)
    {
        var job = _jobTracker.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = "Job not found" });
        }

        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        while ((DateTime.UtcNow - startTime) < timeout)
        {
            job = _jobTracker.GetJob(jobId);
            if (job?.Result != null)
            {
                return Ok(new
                {
                    jobId = job.JobId,
                    status = job.Result.Status,
                    result = job.Result.Result,
                    executionTime = job.Result.ExecutionTime,
                    completedAt = job.CompletedAt
                });
            }

            await Task.Delay(1000); // Poll every second
        }

        return StatusCode(408, new
        {
            error = "Job did not complete within timeout",
            jobId,
            timeoutSeconds
        });
    }

    /// <summary>
    /// Get all jobs
    /// </summary>
    [HttpGet]
    public IActionResult GetAllJobs([FromQuery] string? status = null)
    {
        List<JobTracker.TrackedJob> jobs;

        if (status?.ToLower() == "pending")
        {
            jobs = _jobTracker.GetPendingJobs();
        }
        else if (status?.ToLower() == "completed")
        {
            jobs = _jobTracker.GetCompletedJobs();
        }
        else
        {
            jobs = _jobTracker.GetAllJobs();
        }

        var result = jobs.Select(j => new
        {
            jobId = j.JobId,
            jobType = j.JobMessage.JobType,
            status = j.Status.ToString(),
            submittedAt = j.SubmittedAt,
            completedAt = j.CompletedAt,
            hasResult = j.Result != null
        });

        return Ok(result);
    }

    /// <summary>
    /// Cancel a job
    /// </summary>
    [HttpDelete("{jobId}")]
    public IActionResult CancelJob(string jobId)
    {
        var job = _jobTracker.GetJob(jobId);
        if (job == null)
        {
            return NotFound(new { error = "Job not found" });
        }

        if (job.Status == JobTracker.JobStatus.Completed || job.Status == JobTracker.JobStatus.Failed)
        {
            return BadRequest(new { error = "Cannot cancel completed or failed job" });
        }

        // Note: NodeManager doesn't have a cancel method yet, so we just mark it as cancelled
        _jobTracker.RemoveJob(jobId);

        return Ok(new { message = "Job cancelled", jobId });
    }

    /// <summary>
    /// Submit a custom job
    /// </summary>
    [HttpPost]
    public IActionResult SubmitJob([FromBody] CustomJobRequest request)
    {
        try
        {
            var jobId = Guid.NewGuid().ToString();
            var job = new JobMessage
            {
                JobId = jobId,
                JobType = request.JobType ?? "Custom",
                Parameters = request.Parameters ?? new Dictionary<string, object>()
            };

            _nodeManager.SubmitJob(job);
            _jobTracker.RegisterJob(job);

            return Ok(new { jobId, message = "Job submitted", status = "pending" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

// Request models
public class CustomJobRequest
{
    public string? JobType { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
}
