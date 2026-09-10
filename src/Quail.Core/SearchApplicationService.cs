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

    public bool CanReveal(SearchResultAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action.CanReveal;
    }

    public void Reveal(SearchResultAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action.Reveal();
    }

    public bool CanCopyText(SearchResultAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action.CanCopyText;
    }

    public string GetCopyText(SearchResultAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action.GetCopyText();
    }
}
