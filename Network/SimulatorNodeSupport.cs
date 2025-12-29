// GeoscientistToolkit/Network/SimulatorNodeSupport.cs

using GeoscientistToolkit.Settings;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Network;

/// <summary>
///     Helper class providing node manager integration support for simulators
/// </summary>
public class SimulatorNodeSupport
{
    protected bool _useNodes;
    protected readonly NodeManager _nodeManager;

    public SimulatorNodeSupport(bool? useNodesOverride = null)
    {
        // Check if we should use nodes from settings or override
        if (useNodesOverride.HasValue)
        {
            _useNodes = useNodesOverride.Value;
        }
        else
        {
            var settings = SettingsManager.Instance?.Settings?.NodeManager;
            _useNodes = settings != null && settings.EnableNodeManager && settings.UseNodesForSimulators;
        }

        if (_useNodes)
        {
            _nodeManager = NodeManager.Instance;

            // Check if node manager is running
            if (_nodeManager != null && !_nodeManager.IsRunning)
            {
                Logger.LogWarning("Node Manager is not running. Falling back to local computation.");
                _useNodes = false;
            }
            else if (_nodeManager == null)
            {
                // Should not happen if instance is singleton, but safety check
                _useNodes = false;
            }
        }
    }

    /// <summary>
    ///     Indicates whether this simulator should use distributed nodes for computation
    /// </summary>
    public bool UseNodes => _useNodes;

    /// <summary>
    ///     Submit a simulation job to the node network
    /// </summary>
    /// <param name="jobType">Type of simulation job</param>
    /// <param name="parameters">Job parameters</param>
    /// <param name="requiresGpu">Whether the job requires GPU</param>
    /// <returns>Job ID if submitted successfully, null otherwise</returns>
    protected string SubmitSimulationJob(string jobType, Dictionary<string, object> parameters, bool requiresGpu = false)
    {
        if (!_useNodes || _nodeManager == null)
            return null;

        var job = new JobMessage
        {
            JobId = Guid.NewGuid().ToString(),
            JobType = jobType,
            Priority = 0,
            RequiresGpu = requiresGpu,
            Parameters = parameters
        };

        if (_nodeManager.SubmitJob(job))
        {
            Logger.Log($"Simulation job {job.JobId} submitted to node network (Type: {jobType})");
            return job.JobId;
        }
        else
        {
            Logger.LogWarning($"Failed to submit simulation job to node network. Falling back to local computation.");
            _useNodes = false;
            return null;
        }
    }

    /// <summary>
    ///     Wait for a submitted job to complete (blocking call)
    /// </summary>
    /// <param name="jobId">Job ID to wait for</param>
    /// <param name="timeout">Timeout in milliseconds</param>
    /// <returns>Job result if successful, null if timeout or failure</returns>
    protected JobResultMessage WaitForJobCompletion(string jobId, int timeout = 300000)
    {
        if (!_useNodes || _nodeManager == null || string.IsNullOrEmpty(jobId))
            return null;

        var startTime = DateTime.UtcNow;
        JobResultMessage result = null;

        // Subscribe to job completion event
        void OnJobCompleted(JobResultMessage msg)
        {
            if (msg.JobId == jobId)
                result = msg;
        }

        _nodeManager.JobCompleted += OnJobCompleted;

        try
        {
            // Poll for completion
            while (result == null && (DateTime.UtcNow - startTime).TotalMilliseconds < timeout)
            {
                Thread.Sleep(100);
            }

            if (result == null)
            {
                Logger.LogWarning($"Job {jobId} timed out after {timeout}ms");
            }
            else
            {
                Logger.Log($"Job {jobId} completed: {(result.Success ? "Success" : "Failed")} in {result.ExecutionTimeMs}ms");
            }

            return result;
        }
        finally
        {
            _nodeManager.JobCompleted -= OnJobCompleted;
        }
    }
}
