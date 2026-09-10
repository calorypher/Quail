using Quail.Core;

namespace Quail.App;

internal sealed class SearchRuntime : IDisposable
{
    private readonly Action _dispose;
    private readonly Func<string?>? _getSourceStatusNotice;
    private readonly Action<SearchPerformanceTrace>? _recordSessionStart;
    private readonly Func<string, int, FullSearchCriteria, SearchRequest>? _createFullSearchRequest;
    private readonly Func<SearchResult, FullSearchResultFields?>? _getFullSearchFields;
    private bool _disposed;

    public SearchRuntime(
        SearchApplicationService search,
        Func<bool> hasSources,
        Action dispose,
        Func<string?>? getSourceStatusNotice = null,
        Action<SearchPerformanceTrace>? recordSessionStart = null,
        Func<string, int, FullSearchCriteria, SearchRequest>? createFullSearchRequest = null,
        Func<SearchResult, FullSearchResultFields?>? getFullSearchFields = null)
    {
        Search = search ?? throw new ArgumentNullException(nameof(search));
        HasSources = hasSources ?? throw new ArgumentNullException(nameof(hasSources));
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
        _getSourceStatusNotice = getSourceStatusNotice;
        _recordSessionStart = recordSessionStart;
        _createFullSearchRequest = createFullSearchRequest;
        _getFullSearchFields = getFullSearchFields;
    }

    public SearchApplicationService Search { get; }
    public Func<bool> HasSources { get; }

    public event Action? SourcesChanged;

    public void NotifySourcesChanged() => SourcesChanged?.Invoke();

    public string? GetSourceStatusNotice() => _getSourceStatusNotice?.Invoke();

    public SearchRequest CreateFullSearchRequest(string query, int limit, FullSearchCriteria criteria)
    {
        if (_createFullSearchRequest is null)
        {
            return new SearchRequest(query, limit);
        }

        return _createFullSearchRequest(query, limit, criteria);
    }

    public FullSearchResultFields? GetFullSearchFields(SearchResult result) =>
        _getFullSearchFields?.Invoke(result);

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
