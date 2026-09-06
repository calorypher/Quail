using System.Collections.Concurrent;
using System.Diagnostics;
using Quail.FileSystem;

namespace Quail.MaintenanceService;

internal sealed class FileSystemMaintenanceServiceRuntime : IMaintenanceServiceRuntime
{
    private const int MaximumOperations = 256;
    private readonly MaintenanceStateStore _state = new();
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly object _stateGate = new();
    private readonly object _targetGate = new();
    private readonly ConcurrentDictionary<Guid, MaintenanceOperationSnapshot> _operations = new();
    private readonly ConcurrentDictionary<Guid, Task> _operationTasks = new();
    private readonly Dictionary<string, TargetLoop> _targetLoops = new(StringComparer.OrdinalIgnoreCase);
    private CancellationToken _stoppingToken;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        var targets = _state.LoadTargets();
        SaveHealth(new MaintenanceHealthDocument(
            MaintenanceHealthDocument.CurrentVersion,
            targets.Generation,
            targets.Targets.Select(target => NewHealth(target.VolumeIdentity, MaintenanceHealthState.CatchingUp, false, "startup-catch-up")).ToArray()));
        foreach (var target in targets.Targets)
        {
            StartTargetLoop(target.VolumeIdentity);
        }

        var server = new MaintenanceControlServer(HandleControlAsync, diagnostic: Log);
        try
        {
            await server.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            TargetLoop[] loops;
            lock (_targetGate)
            {
                loops = _targetLoops.Values.ToArray();
                _targetLoops.Clear();
            }

            foreach (var loop in loops) loop.Cancellation.Cancel();
            await Task.WhenAll(loops.Select(loop => IgnoreCancellationAsync(loop.Task))).ConfigureAwait(false);
            foreach (var loop in loops) loop.Cancellation.Dispose();
            await Task.WhenAll(_operationTasks.Values.Select(IgnoreCancellationAsync)).ConfigureAwait(false);
        }
    }

    private Task<MaintenanceControlResponse> HandleControlAsync(MaintenanceControlRequest request, CancellationToken cancellationToken)
    {
        if (request.Command == MaintenanceControlCommand.GetOperationStatus)
        {
            return Task.FromResult(GetOperationStatus(request));
        }

        if (_operations.Count >= MaximumOperations)
        {
            return Task.FromResult(Response(request, MaintenanceControlStatus.Busy, null, "operation-history-full"));
        }

        var identity = request.VolumeIdentity!;
        if (_operations.Values.Any(operation =>
                string.Equals(operation.VolumeIdentity, identity, StringComparison.OrdinalIgnoreCase) &&
                operation.Status == MaintenanceControlStatus.Accepted))
        {
            return Task.FromResult(Response(request, MaintenanceControlStatus.Busy, null, "volume-operation-active"));
        }

        var operationId = Guid.NewGuid();
        var snapshot = new MaintenanceOperationSnapshot(operationId, identity, request.Command, MaintenanceControlStatus.Accepted, null);
        if (!_operations.TryAdd(operationId, snapshot))
        {
            return Task.FromResult(Response(request, MaintenanceControlStatus.Busy, null, "operation-collision"));
        }

        var task = Task.Run(() => ExecuteOperationAsync(snapshot));
        _operationTasks[operationId] = task;
        _ = task.ContinueWith(
            _ => { _operationTasks.TryRemove(operationId, out var ignored); },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return Task.FromResult(Response(request, MaintenanceControlStatus.Accepted, operationId, null));
    }

    private MaintenanceControlResponse GetOperationStatus(MaintenanceControlRequest request)
    {
        var operationId = request.OperationId;
        if (operationId is null || !_operations.TryGetValue(operationId.Value, out var operation))
        {
            return Response(request, MaintenanceControlStatus.Rejected, operationId, "unknown-operation");
        }

        var response = Response(request, operation.Status, operation.OperationId, operation.Diagnostic);
        if (operation.Status != MaintenanceControlStatus.Accepted)
        {
            _operations.TryRemove(operation.OperationId, out _);
        }
        return response;
    }

    private async Task ExecuteOperationAsync(MaintenanceOperationSnapshot operation)
    {
        Log($"control-operation-start command={operation.Command} id={operation.OperationId:D}");
        var status = MaintenanceControlStatus.Error;
        string? diagnostic = null;
        try
        {
            switch (operation.Command)
            {
                case MaintenanceControlCommand.RegisterAndBuild:
                    await BuildAsync(operation, register: true).ConfigureAwait(false);
                    break;
                case MaintenanceControlCommand.Rebuild:
                    await BuildAsync(operation, register: false).ConfigureAwait(false);
                    break;
                case MaintenanceControlCommand.Unregister:
                    await UnregisterAsync(operation.VolumeIdentity).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported maintenance command.");
            }

            status = MaintenanceControlStatus.Succeeded;
        }
        catch (RebuildRequiredException exception)
        {
            status = MaintenanceControlStatus.RebuildRequired;
            diagnostic = exception.Message;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            diagnostic = "operation-failed";
        }
        catch (Exception)
        {
            diagnostic = "operation-failed";
        }

        _operations[operation.OperationId] = operation with { Status = status, Diagnostic = diagnostic };
        Log($"control-operation-stop status={status} id={operation.OperationId:D}");
    }

    private async Task BuildAsync(MaintenanceOperationSnapshot operation, bool register)
    {
        var identity = operation.VolumeIdentity;
        var existing = _state.LoadTargets();
        if (!register && !existing.Targets.Any(target => SameIdentity(target.VolumeIdentity, identity)))
        {
            throw new InvalidOperationException("volume-not-registered");
        }

        var volume = ResolveVolume(identity);
        Log($"control-operation-volume-resolved id={operation.OperationId:D}");
        await StopTargetLoopAsync(identity).ConfigureAwait(false);
        if (register && !existing.Targets.Any(target => SameIdentity(target.VolumeIdentity, identity)))
        {
            var updated = new MaintenanceTargetsDocument(
                MaintenanceTargetsDocument.CurrentVersion,
                checked(existing.Generation + 1),
                existing.Targets.Append(new MaintenanceTarget(identity, volume.MountPoint)).ToArray());
            _state.SaveTargets(updated);
            existing = updated;
            Log($"control-operation-target-saved id={operation.OperationId:D}");
        }

        UpdateHealth(NewHealth(identity, MaintenanceHealthState.CatchingUp, false, "full-build", operation.OperationId), existing.Generation);
        await _writer.WaitAsync(_stoppingToken).ConfigureAwait(false);
        try
        {
            volume = ResolveVolume(identity);
            using var storage = PrivilegedIndexStorage.Acquire(identity);
            var store = IndexStore.CreateProtectedWriter(storage.DatabasePath);
            Log($"control-operation-build-start id={operation.OperationId:D}");
            store.Build(volume.MountPoint);
            Log($"control-operation-build-stop id={operation.OperationId:D}");
            var status = store.GetStatus();
            UpdateHealth(new MaintenanceTargetHealth(
                identity,
                MaintenanceHealthState.Healthy,
                true,
                DateTimeOffset.UtcNow,
                status.LastRefreshedUtc,
                status.Checkpoint,
                operation.OperationId,
                null), existing.Generation);
        }
        finally
        {
            _writer.Release();
        }

        StartTargetLoop(identity);
    }

    private async Task UnregisterAsync(string identity)
    {
        await StopTargetLoopAsync(identity).ConfigureAwait(false);
        var current = _state.LoadTargets();
        var retained = current.Targets.Where(target => !SameIdentity(target.VolumeIdentity, identity)).ToArray();
        if (retained.Length == current.Targets.Count)
        {
            throw new InvalidOperationException("volume-not-registered");
        }

        var updated = new MaintenanceTargetsDocument(MaintenanceTargetsDocument.CurrentVersion, checked(current.Generation + 1), retained);
        _state.SaveTargets(updated);
        lock (_stateGate)
        {
            var health = _state.LoadHealth();
            _state.SaveHealth(new MaintenanceHealthDocument(
                MaintenanceHealthDocument.CurrentVersion,
                updated.Generation,
                health.Targets.Where(target => !SameIdentity(target.VolumeIdentity, identity)).ToArray()));
        }
    }

    private void StartTargetLoop(string identity)
    {
        lock (_targetGate)
        {
            if (_targetLoops.ContainsKey(identity) || _stoppingToken.IsCancellationRequested) return;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stoppingToken);
            var task = Task.Run(() => MaintainTargetAsync(identity, cancellation.Token));
            _targetLoops.Add(identity, new TargetLoop(cancellation, task));
        }
    }

    private async Task StopTargetLoopAsync(string identity)
    {
        TargetLoop? loop = null;
        lock (_targetGate)
        {
            if (_targetLoops.Remove(identity, out var found)) loop = found;
        }

        if (loop is null) return;
        loop.Cancellation.Cancel();
        await IgnoreCancellationAsync(loop.Task).ConfigureAwait(false);
        loop.Cancellation.Dispose();
    }

    private async Task MaintainTargetAsync(string identity, CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var volume = ResolveVolume(identity);
                IncrementalCheckpoint checkpoint;
                await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    volume = ResolveVolume(identity);
                    using var storage = PrivilegedIndexStorage.Acquire(identity);
                    var store = IndexStore.CreateProtectedWriter(storage.DatabasePath);
                    var status = store.GetStatus();
                    if (status.State != IndexState.Complete)
                    {
                        throw new RebuildRequiredException(status.Detail ?? "index-not-complete");
                    }

                    var sync = store.Sync(volume.MountPoint);
                    if (sync.RebuildRequired) throw new RebuildRequiredException(sync.Reason ?? "continuity-unprovable");
                    if (sync.Unavailable) throw new IOException(sync.Reason ?? "journal-unavailable");
                    checkpoint = sync.Checkpoint ?? throw new RebuildRequiredException("checkpoint-missing-after-sync");
                    UpdateHealth(new MaintenanceTargetHealth(
                        identity,
                        MaintenanceHealthState.Healthy,
                        true,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        checkpoint,
                        null,
                        null));
                }
                finally
                {
                    _writer.Release();
                }

                retryDelay = TimeSpan.FromSeconds(1);
                await NtfsJournal.WaitForChangesAsync(volume, checkpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (RebuildRequiredException exception)
            {
                UpdateHealth(NewHealth(identity, MaintenanceHealthState.RebuildRequired, false, exception.Message));
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                UpdateHealth(NewHealth(identity, MaintenanceHealthState.Retrying, false, exception.Message));
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
                retryDelay = TimeSpan.FromSeconds(Math.Min(60, retryDelay.TotalSeconds * 2));
            }
        }
    }

    private static VolumeDescriptor ResolveVolume(string identity) =>
        VolumeDiscovery.Discover().SingleOrDefault(volume => SameIdentity(volume.StableIdentity, identity))
        ?? throw new IOException("configured-volume-unavailable");

    private void UpdateHealth(MaintenanceTargetHealth target, long? generation = null)
    {
        lock (_stateGate)
        {
            var current = _state.LoadHealth();
            var targets = current.Targets.Where(item => !SameIdentity(item.VolumeIdentity, target.VolumeIdentity)).Append(target).ToArray();
            _state.SaveHealth(new MaintenanceHealthDocument(
                MaintenanceHealthDocument.CurrentVersion,
                generation ?? current.ConfigurationGeneration,
                targets));
        }
    }

    private void SaveHealth(MaintenanceHealthDocument document)
    {
        lock (_stateGate) _state.SaveHealth(document);
    }

    private static MaintenanceTargetHealth NewHealth(
        string identity,
        MaintenanceHealthState state,
        bool trusted,
        string? reason,
        Guid? operationId = null) =>
        new(identity, state, trusted, DateTimeOffset.UtcNow, null, null, operationId, MaintenanceControlValidation.BoundDiagnostic(reason));

    private static MaintenanceControlResponse Response(
        MaintenanceControlRequest request,
        MaintenanceControlStatus status,
        Guid? operationId,
        string? diagnostic) =>
        new(MaintenanceControlResponse.CurrentVersion, request.RequestId, status, operationId, MaintenanceControlValidation.BoundDiagnostic(diagnostic));

    private static bool SameIdentity(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private static void Log(string message)
    {
        try { EventLog.WriteEntry("QuailMaintenance", message, EventLogEntryType.Information); }
        catch { }
    }

    private sealed record TargetLoop(CancellationTokenSource Cancellation, Task Task);
    private sealed class RebuildRequiredException(string message) : Exception(message);
}
