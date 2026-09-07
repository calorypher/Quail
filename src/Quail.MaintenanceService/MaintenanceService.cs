using System.ServiceProcess;

namespace Quail.MaintenanceService;

/// <summary>
/// The runtime owned by the service. The runtime must complete when the token is
/// cancelled; it must not own the service lifecycle or ServiceBase instance.
/// </summary>
public interface IMaintenanceServiceRuntime
{
    Task RunAsync(CancellationToken stoppingToken);
}

/// <summary>
/// Small, deterministic lifecycle coordinator kept independent from the SCM.
/// </summary>
public sealed class MaintenanceServiceLifecycle
{
    private readonly IMaintenanceServiceRuntime _runtime;
    private readonly TimeSpan _stopTimeout;
    private readonly Action<Exception> _terminalFailure;
    private readonly object _gate = new();
    private CancellationTokenSource? _stopping;
    private Task? _runtimeTask;
    private Task? _startupCompletion;
    private LifecycleState _state;

    public MaintenanceServiceLifecycle(
        IMaintenanceServiceRuntime runtime,
        TimeSpan stopTimeout,
        Action<Exception> terminalFailure)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _terminalFailure = terminalFailure ?? throw new ArgumentNullException(nameof(terminalFailure));
        if (stopTimeout <= TimeSpan.Zero || stopTimeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(stopTimeout), "The stop timeout must be positive and finite.");
        }

        _stopTimeout = stopTimeout;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_state != LifecycleState.Created)
            {
                throw new InvalidOperationException("The maintenance service has already started.");
            }

            _state = LifecycleState.Starting;
        }

        var stopping = new CancellationTokenSource();
        Task runtimeTask;
        try
        {
            runtimeTask = _runtime.RunAsync(stopping.Token)
                ?? throw new InvalidOperationException("The maintenance runtime returned a null task.");

            lock (_gate)
            {
                _stopping = stopping;
                _runtimeTask = runtimeTask;
            }

            _ = runtimeTask.ContinueWith(
                ObserveRuntimeCompletion,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            Task? startupCompletion;
            lock (_gate)
            {
                startupCompletion = _startupCompletion ?? (runtimeTask.IsCompleted ? runtimeTask : null);
                if (startupCompletion is null)
                {
                    _state = LifecycleState.Running;
                }
                else
                {
                    _state = LifecycleState.Created;
                    _stopping = null;
                    _runtimeTask = null;
                    _startupCompletion = null;
                }
            }

            if (startupCompletion is not null)
            {
                ThrowStartupCompletion(startupCompletion);
            }
        }
        catch
        {
            stopping.Cancel();
            stopping.Dispose();
            lock (_gate)
            {
                _stopping = null;
                _runtimeTask = null;
                _startupCompletion = null;
                _state = LifecycleState.Created;
            }
            throw;
        }
    }

    public void Stop()
    {
        CancellationTokenSource stopping;
        Task runtimeTask;
        bool terminalFailureReported;
        lock (_gate)
        {
            if (_state is LifecycleState.Created or LifecycleState.Stopping or LifecycleState.Stopped)
            {
                return;
            }

            terminalFailureReported = _state == LifecycleState.Failed;
            _state = LifecycleState.Stopping;
            stopping = _stopping!;
            runtimeTask = _runtimeTask!;
        }

        stopping.Cancel();
        var completed = false;
        try
        {
            try
            {
                if (!runtimeTask.Wait(_stopTimeout))
                {
                    throw new System.TimeoutException($"The maintenance runtime did not stop within {_stopTimeout}.");
                }

                completed = true;
            }
            catch (AggregateException) when (runtimeTask.IsFaulted || runtimeTask.IsCanceled)
            {
                // Wait observes the task but wraps its result. The await-style
                // observation below preserves the original runtime exception.
                completed = true;
            }

            // Observe and propagate a background fault instead of silently
            // converting it into a successful service stop.
            if (runtimeTask.IsFaulted && !terminalFailureReported)
            {
                runtimeTask.GetAwaiter().GetResult();
            }
        }
        finally
        {
            // Keep the token source alive if a non-cooperative runtime exceeded
            // the bounded stop window; disposing it while that task is still
            // running would create a lifetime race.
            if (completed)
            {
                stopping.Dispose();
                lock (_gate)
                {
                    if (ReferenceEquals(_stopping, stopping))
                    {
                        _stopping = null;
                        _runtimeTask = null;
                    }

                    _state = LifecycleState.Stopped;
                }
            }
        }
    }

    private void ObserveRuntimeCompletion(Task runtimeTask)
    {
        lock (_gate)
        {
            if (_state == LifecycleState.Starting)
            {
                _startupCompletion = runtimeTask;
                return;
            }

            if (_state != LifecycleState.Running)
            {
                return;
            }

            _state = LifecycleState.Failed;
        }

        _terminalFailure(GetUnexpectedCompletion(runtimeTask));
    }

    private static void ThrowStartupCompletion(Task runtimeTask)
    {
        var failure = GetUnexpectedCompletion(runtimeTask);
        if (runtimeTask.IsFaulted)
        {
            runtimeTask.GetAwaiter().GetResult();
        }

        throw failure;
    }

    private static Exception GetUnexpectedCompletion(Task runtimeTask)
    {
        if (runtimeTask.IsFaulted)
        {
            try
            {
                runtimeTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        return runtimeTask.IsCanceled
            ? new InvalidOperationException("The maintenance runtime was canceled without a stop request.")
            : new InvalidOperationException("The maintenance runtime completed without a stop request.");
    }

    private enum LifecycleState
    {
        Created,
        Starting,
        Running,
        Failed,
        Stopping,
        Stopped
    }
}

public sealed class MaintenanceService : ServiceBase
{
    private readonly MaintenanceServiceLifecycle _lifecycle;

    public MaintenanceService(IMaintenanceServiceRuntime runtime, TimeSpan stopTimeout)
        : this(runtime, stopTimeout, TerminateProcess)
    {
    }

    internal MaintenanceService(
        IMaintenanceServiceRuntime runtime,
        TimeSpan stopTimeout,
        Action<Exception> terminalFailure)
    {
        ServiceName = "QuailMaintenance";
        CanStop = true;
        CanShutdown = true;
        CanPauseAndContinue = false;
        AutoLog = true;
        _lifecycle = new MaintenanceServiceLifecycle(runtime, stopTimeout, terminalFailure);
    }

    protected override void OnStart(string[] args) => StartCore();

    protected override void OnStop() => StopCore();

    protected override void OnShutdown() => ShutdownCore();

    internal void StartCore() => _lifecycle.Start();

    internal void StopCore() => _lifecycle.Stop();

    internal void ShutdownCore() => _lifecycle.Stop();

    private static void TerminateProcess(Exception failure) =>
        Environment.FailFast("The Quail maintenance runtime terminated unexpectedly.", failure);
}
