using Quail.Core;

namespace Quail.App;

internal interface IFileSearchService
{
    IReadOnlyList<SearchResult> Search(string query);
    SearchIndexScale GetSearchIndexScale();
    IReadOnlyList<SearchIndexStatus> GetIndexStatuses();
    void Open(SearchResultAction action);
}

internal sealed class FileSearchService : IFileSearchService
{
    private readonly FileSearchApplicationService _core;

    public FileSearchService(IEnumerable<string> indexPaths)
        : this(() => indexPaths.ToArray())
    {
    }

    public FileSearchService(Func<IReadOnlyList<string>> paths)
    {
        _core = new FileSearchApplicationService(paths);
    }

    public IReadOnlyList<SearchResult> Search(string query) => _core.Search(new SearchRequest(query));

    public SearchIndexScale GetSearchIndexScale()
    {
        var scale = _core.GetSearchIndexScale();
        return new SearchIndexScale(
            scale.ConfiguredIndexCount,
            scale.RecordCount,
            scale.DatabaseBytes,
            scale.UnavailableIndexCount);
    }

    public IReadOnlyList<SearchIndexStatus> GetIndexStatuses() => _core.GetIndexStatuses();

    public void Open(SearchResultAction action) => _core.Open(action);
}
