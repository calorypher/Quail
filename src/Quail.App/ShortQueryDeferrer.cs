namespace Quail.App;

internal sealed class ShortQueryDeferrer : IDisposable
{
    private readonly object _gate = new();
    private readonly TimeSpan _delay;
    private readonly Action<long, string> _ready;
    private readonly Timer _timer;
    private bool _disposed;
    private (long Generation, string Query)? _pending;

    public ShortQueryDeferrer(TimeSpan delay, Action<long, string> ready)
    {
        _delay = delay;
        _ready = ready;
        _timer = new Timer(OnTimer);
    }

    public void Schedule(long generation, string query)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = (generation, query);
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pending = null;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending = null;
            _timer.Dispose();
        }
    }

    private void OnTimer(object? state)
    {
        (long Generation, string Query)? request;
        lock (_gate)
        {
            if (_disposed || _pending is null)
            {
                return;
            }

            request = _pending;
            _pending = null;
        }

        _ready(request.Value.Generation, request.Value.Query);
    }
}
