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
    private CancellationTokenSource? _stopping;
    private Task? _runtimeTask;
    private bool _started;
    private bool _stopped;

    public MaintenanceServiceLifecycle(IMaintenanceServiceRuntime runtime, TimeSpan stopTimeout)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        if (stopTimeout <= TimeSpan.Zero || stopTimeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(stopTimeout), "The stop timeout must be positive and finite.");
        }

        _stopTimeout = stopTimeout;
    }

    public void Start()
    {
        if (_started)
        {
            throw new InvalidOperationException("The maintenance service has already started.");
        }

        _started = true;
        _stopping = new CancellationTokenSource();
        try
        {
            _runtimeTask = _runtime.RunAsync(_stopping.Token)
                ?? throw new InvalidOperationException("The maintenance runtime returned a null task.");

            // Async methods can fail before their first await. Surface that failure
            // from OnStart so SCM does not record a falsely healthy service.
            if (_runtimeTask.IsFaulted)
            {
                _runtimeTask.GetAwaiter().GetResult();
            }
        }
        catch
        {
            _stopping.Cancel();
            _stopping.Dispose();
            _stopping = null;
            _runtimeTask = null;
            _started = false;
            throw;
        }
    }

    public void Stop()
    {
        if (!_started || _stopped)
        {
            return;
        }

        _stopped = true;
        _stopping!.Cancel();
        var runtimeTask = _runtimeTask!;
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
            }

            // Observe and propagate a background fault instead of silently
            // converting it into a successful service stop.
            if (runtimeTask.IsFaulted)
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
                _stopping.Dispose();
                _stopping = null;
            }
        }
    }
}

public sealed class MaintenanceService : ServiceBase
{
    private readonly MaintenanceServiceLifecycle _lifecycle;

    public MaintenanceService(IMaintenanceServiceRuntime runtime, TimeSpan stopTimeout)
    {
        ServiceName = "QuailMaintenance";
        CanStop = true;
        CanPauseAndContinue = false;
        AutoLog = true;
        _lifecycle = new MaintenanceServiceLifecycle(runtime, stopTimeout);
    }

    protected override void OnStart(string[] args) => _lifecycle.Start();

    protected override void OnStop() => _lifecycle.Stop();
}
