using GeoscientistToolkit.Network;
using System.Collections.Concurrent;

namespace GeoscientistToolkit.NodeEndpoint;

/// <summary>
/// Tracks job submissions and results for async retrieval
/// </summary>
public class JobTracker
{
    private readonly ConcurrentDictionary<string, TrackedJob> _jobs = new();
    private readonly object _cleanupLock = new object();
    private DateTime _lastCleanup = DateTime.UtcNow;

    public class TrackedJob
    {
        public required string JobId { get; set; }
        public required JobMessage JobMessage { get; set; }
        public JobResultMessage? Result { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public JobStatus Status { get; set; } = JobStatus.Pending;
    }

    public enum JobStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public void RegisterJob(JobMessage job)
    {
        var tracked = new TrackedJob
        {
            JobId = job.JobId,
            JobMessage = job,
            SubmittedAt = DateTime.UtcNow,
            Status = JobStatus.Pending
        };

        _jobs.TryAdd(job.JobId, tracked);
        CleanupOldJobs();
    }

    public void UpdateJobResult(JobResultMessage result)
    {
        if (_jobs.TryGetValue(result.JobId, out var job))
        {
            job.Result = result;
            job.CompletedAt = DateTime.UtcNow;
            job.Status = result.Status == "Completed" ? JobStatus.Completed : JobStatus.Failed;
        }
    }

    public TrackedJob? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    public List<TrackedJob> GetAllJobs()
    {
        return _jobs.Values.OrderByDescending(j => j.SubmittedAt).ToList();
    }

    public List<TrackedJob> GetPendingJobs()
    {
        return _jobs.Values.Where(j => j.Status == JobStatus.Pending).ToList();
    }

    public List<TrackedJob> GetCompletedJobs()
    {
        return _jobs.Values.Where(j => j.Status == JobStatus.Completed).ToList();
    }

    public bool RemoveJob(string jobId)
    {
        return _jobs.TryRemove(jobId, out _);
    }

    /// <summary>
    /// Clean up jobs older than 1 hour to prevent memory leaks
    /// </summary>
    private void CleanupOldJobs()
    {
        lock (_cleanupLock)
        {
            // Only cleanup every 5 minutes
            if ((DateTime.UtcNow - _lastCleanup).TotalMinutes < 5)
                return;

            var cutoff = DateTime.UtcNow.AddHours(-1);
            var oldJobs = _jobs.Values
                .Where(j => j.Status == JobStatus.Completed || j.Status == JobStatus.Failed)
                .Where(j => j.CompletedAt.HasValue && j.CompletedAt.Value < cutoff)
                .Select(j => j.JobId)
                .ToList();

            foreach (var jobId in oldJobs)
            {
                _jobs.TryRemove(jobId, out _);
            }

            if (oldJobs.Count > 0)
            {
                Console.WriteLine($"Cleaned up {oldJobs.Count} old jobs");
            }

            _lastCleanup = DateTime.UtcNow;
        }
    }
}
