using System.Runtime.CompilerServices;
using GAIA.Util;

namespace GAIA.Data.CtImageStack;

public enum CtOperationStatus { Queued, Running, Completed, Cancelled, Failed }

public sealed class CtOperationHandle
{
    private readonly CancellationTokenSource _cancellation = new();
    internal CtOperationHandle(string name) => Name = name;
    public string Name { get; }
    public CtOperationStatus Status { get; internal set; } = CtOperationStatus.Queued;
    public float Progress { get; internal set; }
    public string Error { get; internal set; }
    public Task Completion { get; internal set; } = Task.CompletedTask;
    internal CancellationToken Token => _cancellation.Token;
    public bool IsActive => Status is CtOperationStatus.Queued or CtOperationStatus.Running;
    public void Cancel() => _cancellation.Cancel();
}

/// <summary>Serializes expensive CT operations per dataset while keeping them off the UI thread.</summary>
public sealed class CtOperationCoordinator
{
    private static readonly ConditionalWeakTable<CtImageStackDataset, CtOperationCoordinator> Instances = new();
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    public static CtOperationCoordinator For(CtImageStackDataset dataset) =>
        Instances.GetValue(dataset, _ => new CtOperationCoordinator());

    public CtOperationHandle Enqueue(string name,
        Func<CancellationToken, IProgress<float>, Task> operation)
    {
        var handle = new CtOperationHandle(name);
        lock (_gate)
        {
            handle.Completion = _tail = RunAfterAsync(_tail, handle, operation);
        }
        return handle;
    }

    private static async Task RunAfterAsync(Task predecessor, CtOperationHandle handle,
        Func<CancellationToken, IProgress<float>, Task> operation)
    {
        try { await predecessor.ConfigureAwait(false); }
        catch { /* Each handle records its own failure; the queue must continue. */ }
        await Task.Yield();

        if (handle.Token.IsCancellationRequested)
        {
            handle.Status = CtOperationStatus.Cancelled;
            return;
        }

        handle.Status = CtOperationStatus.Running;
        var progress = new InlineProgress(value => handle.Progress = Math.Clamp(value, 0f, 1f));
        try
        {
            await operation(handle.Token, progress).ConfigureAwait(false);
            handle.Progress = 1f;
            handle.Status = CtOperationStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            handle.Status = CtOperationStatus.Cancelled;
        }
        catch (Exception ex)
        {
            handle.Error = ex.Message;
            handle.Status = CtOperationStatus.Failed;
            Logger.LogError($"[CT] {handle.Name} failed: {ex}");
        }
    }

    private sealed class InlineProgress(Action<float> report) : IProgress<float>
    {
        public void Report(float value) => report(value);
    }
}
