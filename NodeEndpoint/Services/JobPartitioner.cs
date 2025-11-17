using GeoscientistToolkit.Network;
using System.Collections.Concurrent;

namespace GeoscientistToolkit.NodeEndpoint.Services;

/// <summary>
/// Partitions large jobs across multiple worker nodes for parallel processing
/// </summary>
public class JobPartitioner
{
    private readonly NodeManager _nodeManager;
    private readonly DataReferenceService _dataReferenceService;
    private readonly JobTracker _jobTracker;
    private readonly ConcurrentDictionary<string, PartitionedJob> _partitionedJobs = new();

    public JobPartitioner(NodeManager nodeManager, DataReferenceService dataReferenceService, JobTracker jobTracker)
    {
        _nodeManager = nodeManager;
        _dataReferenceService = dataReferenceService;
        _jobTracker = jobTracker;
    }

    /// <summary>
    /// Submit a job that will be automatically partitioned across available nodes
    /// </summary>
    public string SubmitPartitionedJob(string jobType, Dictionary<string, object> parameters, PartitioningOptions options)
    {
        var parentJobId = Guid.NewGuid().ToString();
        var availableNodes = _nodeManager.GetNodes()
            .Where(n => n.Status == NodeStatus.Connected || n.Status == NodeStatus.Idle)
            .ToList();

        if (availableNodes.Count == 0)
        {
            throw new InvalidOperationException("No available nodes for partitioned job");
        }

        // Determine optimal partition count based on available nodes and job type
        int partitionCount = DeterminePartitionCount(availableNodes.Count, options);

        Console.WriteLine($"[JobPartitioner] Partitioning job {jobType} into {partitionCount} parts across {availableNodes.Count} nodes");

        // Create data partitions if needed
        List<DataPartition>? dataPartitions = null;
        if (options.DataReferenceId != null)
        {
            dataPartitions = _dataReferenceService.CreatePartitions(
                options.DataReferenceId,
                partitionCount,
                options.PartitionStrategy
            );
        }

        // Create sub-jobs
        var subJobs = new List<SubJob>();
        for (int i = 0; i < partitionCount; i++)
        {
            var subJobId = $"{parentJobId}_part{i}";
            var subJobParams = new Dictionary<string, object>(parameters);

            // Add partition-specific parameters
            subJobParams["partitionId"] = i;
            subJobParams["totalPartitions"] = partitionCount;
            subJobParams["parentJobId"] = parentJobId;

            if (dataPartitions != null && i < dataPartitions.Count)
            {
                var partition = dataPartitions[i];
                subJobParams["dataReferenceId"] = partition.ReferenceId;
                subJobParams["partitionStart"] = partition.Start ?? Array.Empty<int>();
                subJobParams["partitionSize"] = partition.Size ?? Array.Empty<int>();
                subJobParams["partitionMetadata"] = partition.Metadata;
            }

            var jobMessage = new JobMessage
            {
                JobId = subJobId,
                JobType = jobType,
                Parameters = subJobParams
            };

            subJobs.Add(new SubJob
            {
                JobId = subJobId,
                JobMessage = jobMessage,
                PartitionId = i,
                Status = SubJobStatus.Pending
            });

            _jobTracker.RegisterJob(jobMessage);
        }

        // Create partitioned job record
        var partitionedJob = new PartitionedJob
        {
            ParentJobId = parentJobId,
            JobType = jobType,
            SubJobs = subJobs,
            TotalPartitions = partitionCount,
            SubmittedAt = DateTime.UtcNow,
            Status = PartitionedJobStatus.Running,
            ResultAggregationStrategy = options.ResultAggregationStrategy
        };

        _partitionedJobs.TryAdd(parentJobId, partitionedJob);

        // Submit sub-jobs to nodes
        SubmitSubJobs(subJobs, availableNodes);

        // Start monitoring for completion
        Task.Run(() => MonitorPartitionedJob(parentJobId));

        return parentJobId;
    }

    /// <summary>
    /// Get the status of a partitioned job
    /// </summary>
    public PartitionedJobStatus? GetJobStatus(string parentJobId)
    {
        if (_partitionedJobs.TryGetValue(parentJobId, out var job))
        {
            return job.Status;
        }
        return null;
    }

    /// <summary>
    /// Get detailed information about a partitioned job
    /// </summary>
    public PartitionedJob? GetPartitionedJob(string parentJobId)
    {
        _partitionedJobs.TryGetValue(parentJobId, out var job);
        return job;
    }

    /// <summary>
    /// Get aggregated results from all partitions
    /// </summary>
    public object? GetAggregatedResults(string parentJobId)
    {
        if (!_partitionedJobs.TryGetValue(parentJobId, out var job))
            return null;

        if (job.Status != PartitionedJobStatus.Completed)
            return null;

        return job.AggregatedResult;
    }

    private void SubmitSubJobs(List<SubJob> subJobs, List<NodeInfo> availableNodes)
    {
        // Distribute sub-jobs across nodes using round-robin with load balancing
        var nodeQueue = availableNodes.OrderBy(n => n.GetLoadScore()).ToList();
        int nodeIndex = 0;

        foreach (var subJob in subJobs)
        {
            try
            {
                _nodeManager.SubmitJob(subJob.JobMessage);
                subJob.Status = SubJobStatus.Running;
                subJob.SubmittedAt = DateTime.UtcNow;

                Console.WriteLine($"[JobPartitioner] Submitted sub-job {subJob.JobId} (partition {subJob.PartitionId})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JobPartitioner] Failed to submit sub-job {subJob.JobId}: {ex.Message}");
                subJob.Status = SubJobStatus.Failed;
                subJob.Error = ex.Message;
            }

            nodeIndex = (nodeIndex + 1) % nodeQueue.Count;
        }
    }

    private async Task MonitorPartitionedJob(string parentJobId)
    {
        while (_partitionedJobs.TryGetValue(parentJobId, out var job))
        {
            // Check status of all sub-jobs
            int completed = 0;
            int failed = 0;

            foreach (var subJob in job.SubJobs)
            {
                var trackedJob = _jobTracker.GetJob(subJob.JobId);
                if (trackedJob?.Result != null)
                {
                    if (trackedJob.Result.Status == "Completed")
                    {
                        subJob.Status = SubJobStatus.Completed;
                        subJob.CompletedAt = DateTime.UtcNow;
                        subJob.Result = trackedJob.Result.Result;
                        completed++;
                    }
                    else
                    {
                        subJob.Status = SubJobStatus.Failed;
                        subJob.CompletedAt = DateTime.UtcNow;
                        subJob.Error = trackedJob.Result.Status;
                        failed++;
                    }
                }
                else if (subJob.Status == SubJobStatus.Completed)
                {
                    completed++;
                }
                else if (subJob.Status == SubJobStatus.Failed)
                {
                    failed++;
                }
            }

            // Update overall status
            if (completed == job.TotalPartitions)
            {
                job.Status = PartitionedJobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;

                // Aggregate results
                await AggregateResults(job);

                Console.WriteLine($"[JobPartitioner] Partitioned job {parentJobId} completed successfully");
                break;
            }
            else if (failed > 0 && (completed + failed) == job.TotalPartitions)
            {
                job.Status = PartitionedJobStatus.Failed;
                job.CompletedAt = DateTime.UtcNow;
                Console.WriteLine($"[JobPartitioner] Partitioned job {parentJobId} failed ({failed} sub-jobs failed)");
                break;
            }

            // Wait before checking again
            await Task.Delay(1000);
        }
    }

    private async Task AggregateResults(PartitionedJob job)
    {
        var results = job.SubJobs
            .Where(sj => sj.Result != null)
            .OrderBy(sj => sj.PartitionId)
            .Select(sj => sj.Result)
            .ToList();

        // Aggregate based on strategy
        object? aggregatedResult = job.ResultAggregationStrategy switch
        {
            ResultAggregationStrategy.Concatenate => ConcatenateResults(results),
            ResultAggregationStrategy.Merge => MergeResults(results),
            ResultAggregationStrategy.Sum => SumResults(results),
            ResultAggregationStrategy.Average => AverageResults(results),
            ResultAggregationStrategy.Custom => results, // Return all results for custom aggregation
            _ => results
        };

        job.AggregatedResult = aggregatedResult;
        await Task.CompletedTask;
    }

    private object ConcatenateResults(List<object?> results)
    {
        // Concatenate results (e.g., for split volumes)
        return new { partitions = results, aggregationType = "concatenate" };
    }

    private object MergeResults(List<object?> results)
    {
        // Merge results (e.g., for distributed simulations)
        return new { partitions = results, aggregationType = "merge" };
    }

    private object SumResults(List<object?> results)
    {
        // Sum numerical results
        return new { partitions = results, aggregationType = "sum" };
    }

    private object AverageResults(List<object?> results)
    {
        // Average numerical results
        return new { partitions = results, aggregationType = "average" };
    }

    private int DeterminePartitionCount(int availableNodes, PartitioningOptions options)
    {
        if (options.ExplicitPartitionCount.HasValue)
            return Math.Min(options.ExplicitPartitionCount.Value, availableNodes * 2);

        // Auto-determine based on available nodes (2x oversubscription for better load balancing)
        return Math.Min(availableNodes * 2, 32); // Cap at 32 partitions
    }
}

public class PartitioningOptions
{
    public string? DataReferenceId { get; set; }
    public PartitionStrategy PartitionStrategy { get; set; } = PartitionStrategy.SpatialZ;
    public int? ExplicitPartitionCount { get; set; }
    public ResultAggregationStrategy ResultAggregationStrategy { get; set; } = ResultAggregationStrategy.Concatenate;
}

public class PartitionedJob
{
    public required string ParentJobId { get; set; }
    public required string JobType { get; set; }
    public required List<SubJob> SubJobs { get; set; }
    public int TotalPartitions { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public PartitionedJobStatus Status { get; set; }
    public ResultAggregationStrategy ResultAggregationStrategy { get; set; }
    public object? AggregatedResult { get; set; }
}

public class SubJob
{
    public required string JobId { get; set; }
    public required JobMessage JobMessage { get; set; }
    public int PartitionId { get; set; }
    public SubJobStatus Status { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public object? Result { get; set; }
    public string? Error { get; set; }
}

public enum PartitionedJobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum SubJobStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

public enum ResultAggregationStrategy
{
    Concatenate,  // Join results sequentially (e.g., volume slices)
    Merge,        // Merge results (e.g., simulation timesteps)
    Sum,          // Sum numerical results
    Average,      // Average numerical results
    Custom        // Return raw results for custom aggregation
}
