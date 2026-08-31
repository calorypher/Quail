using Quail.Core;

namespace Quail.App;

internal sealed class SearchRuntime : IDisposable
{
    private readonly Action _dispose;
    private readonly Func<string?>? _getSourceStatusNotice;
    private readonly Action<SearchPerformanceTrace>? _recordSessionStart;
    private bool _disposed;

    public SearchRuntime(
        SearchApplicationService search,
        Func<bool> hasSources,
        Action dispose,
        Func<string?>? getSourceStatusNotice = null,
        Action<SearchPerformanceTrace>? recordSessionStart = null)
    {
        Search = search ?? throw new ArgumentNullException(nameof(search));
        HasSources = hasSources ?? throw new ArgumentNullException(nameof(hasSources));
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        _getSourceStatusNotice = getSourceStatusNotice;
        _recordSessionStart = recordSessionStart;
    }

    public SearchApplicationService Search { get; }
    public Func<bool> HasSources { get; }

    public event Action? SourcesChanged;

    public void NotifySourcesChanged() => SourcesChanged?.Invoke();

    public string? GetSourceStatusNotice() => _getSourceStatusNotice?.Invoke();

    public void RecordSessionStart(SearchPerformanceTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        if (_recordSessionStart is null)
        {
            trace.RecordSessionStart();
            return;
        }

        _recordSessionStart(trace);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispose();
    }

}
