namespace Quail.MaintenanceService;

internal sealed class ContinuousMaintenanceScheduling
{
    private readonly FixedMaintenanceCoalescingWindow _window = new();

    internal DateTimeOffset? Deadline => _window.Deadline;

    internal bool HasPendingCoalescing(DateTimeOffset now) => _window.IsPending(now);

    internal bool BeginAfterExternalGap(DateTimeOffset now)
    {
        if (!ReachedSteadyState)
        {
            return false;
        }

        _window.Start(now);
        return true;
    }

    internal void BeginAfterChangeWake(DateTimeOffset now)
    {
        ReachedSteadyState = true;
        _window.Start(now);
    }

    internal Task WaitForPendingCoalescingAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        _window.WaitAsync(now, cancellationToken);

    private bool ReachedSteadyState { get; set; }
}
