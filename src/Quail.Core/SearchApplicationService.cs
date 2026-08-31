namespace Quail.Core;

internal sealed class SearchApplicationService
{
    private readonly IReadOnlyList<ISearchSource> _sources;

    public SearchApplicationService(IEnumerable<ISearchSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToArray();
    }

    public IReadOnlyList<SearchResult> Search(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        if (request.Limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Search result limit must be at least one.");
        }

        return _sources
            .SelectMany(source => source.Search(request))
            .Take(request.Limit)
            .ToArray();
    }

    public void Open(SearchResultAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action.Open();
    }
}
