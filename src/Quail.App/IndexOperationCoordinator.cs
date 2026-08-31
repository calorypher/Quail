using Quail.FileSystem;

namespace Quail.App;

internal sealed record RunningIndexOperation(string VolumeIdentity, AdminIndexOperation Operation);

internal sealed class IndexOperationCoordinator
{
    private readonly IndexCatalogController _catalog;
    private readonly Func<AdminIndexOperation, IndexCatalogEntry, Task<AdminOperationResult>> _run;
    private readonly object _gate = new();
    private readonly Dictionary<string, RunningIndexOperation> _running = new(StringComparer.OrdinalIgnoreCase);

    public IndexOperationCoordinator(
        IndexCatalogController catalog,
        Func<AdminIndexOperation, IndexCatalogEntry, Task<AdminOperationResult>>? run = null)
    {
        _catalog = catalog;
        _run = run ?? new ElevatedIndexOperationRunner().RunAsync;
    }

    public event Action? Changed;
    public bool HasRunningOperations { get { lock (_gate) return _running.Count != 0; } }
    public IReadOnlyList<RunningIndexOperation> Running { get { lock (_gate) return _running.Values.ToArray(); } }

    public Task<AdminOperationResult> StartAsync(AdminIndexOperation operation, IndexCatalogEntry entry)
    {
        var snapshot = _catalog.GetEntrySnapshot(entry.VolumeIdentity)
            ?? throw new InvalidOperationException("The configured index no longer exists.");
        var completion = new TaskCompletionSource<AdminOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_running.ContainsKey(entry.VolumeIdentity))
                throw new InvalidOperationException("Another index operation is already running for this volume.");
            _running.Add(entry.VolumeIdentity, new(entry.VolumeIdentity, operation));
        }

        Changed?.Invoke();
        _ = ExecuteAsync(operation, snapshot.Entry, snapshot.Revision, completion);
        return completion.Task;
    }

    private async Task ExecuteAsync(
        AdminIndexOperation operation,
        IndexCatalogEntry entry,
        long entryRevision,
        TaskCompletionSource<AdminOperationResult> completion)
    {
        AdminOperationResult result;
        try
        {
            result = await _run(operation, entry);
            if (result.Success && !result.RebuildRequired && operation == AdminIndexOperation.Build)
            {
                try
                {
                    await _catalog.TryEnableAfterInitialBuildAsync(entry.VolumeIdentity, entryRevision);
                }
                catch (Exception exception)
                {
                    result = result with
                    {
                        Success = false,
                        Detail = $"Build completed, but the catalog could not be updated: {exception.Message}",
                        Status = "Error"
                    };
                }
            }

            _catalog.ReevaluateActivePaths();
        }
        catch (Exception exception)
        {
            result = new(Guid.Empty, operation.ToString(), false, false, null, null, 0, exception.Message, "Error");
        }
        finally
        {
            lock (_gate) _running.Remove(entry.VolumeIdentity);
            Changed?.Invoke();
        }

        completion.TrySetResult(result);
    }
}
