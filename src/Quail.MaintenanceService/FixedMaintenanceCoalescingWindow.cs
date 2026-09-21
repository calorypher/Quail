namespace Quail.MaintenanceService;

internal sealed class FixedMaintenanceCoalescingWindow
{
    internal static readonly TimeSpan Duration = TimeSpan.FromSeconds(60);
    private DateTimeOffset? _deadline;

    internal DateTimeOffset? Deadline => _deadline;

    internal void Start(DateTimeOffset now) => _deadline ??= now.Add(Duration);

    internal bool IsPending(DateTimeOffset now) => _deadline is { } deadline && deadline > now;

    internal void Complete(DateTimeOffset now)
    {
        if (_deadline is { } deadline && deadline <= now)
        {
            _deadline = null;
        }
    }

    internal async Task WaitAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_deadline is not { } deadline)
        {
            return;
        }

        var remaining = deadline - now;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }

        Complete(deadline);
    }
}
