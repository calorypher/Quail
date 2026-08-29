using Quail.Core;

namespace Quail.App;

internal interface IFileSearchService
{
    IReadOnlyList<IndexedFileSearchResult> Search(string query);
    SearchIndexScale GetSearchIndexScale();
    void Open(string sourceIdentity, NativeFileId fileId);
}

internal sealed class FileSearchService : IFileSearchService
{
    private readonly Func<IReadOnlyList<string>> _paths;
    private readonly IndexedEntryOpener _opener;

    public FileSearchService(IEnumerable<string> indexPaths, IndexedEntryOpener? opener = null)
        : this(() => indexPaths.ToArray(), opener)
    {
    }

    public FileSearchService(Func<IReadOnlyList<string>> paths, IndexedEntryOpener? opener = null)
    {
        _paths = paths;
        _opener = opener ?? new IndexedEntryOpener();
    }

    public IReadOnlyList<IndexedFileSearchResult> Search(string query) =>
        MultiIndexSearch.Search(_paths().Select(path => new IndexStore(path)), new FileSearchQuery(query, Limit: IndexStore.DefaultSearchResultLimit));

    public SearchIndexScale GetSearchIndexScale()
    {
        var paths = _paths();
        long recordCount = 0;
        long databaseBytes = 0;
        var unavailableIndexCount = 0;
        foreach (var path in paths)
        {
            try
            {
                recordCount += new IndexStore(path).GetStatus().RecordCount;
                databaseBytes += new FileInfo(path).Length;
            }
            catch (Exception)
            {
                unavailableIndexCount++;
            }
        }

        return new SearchIndexScale(paths.Count, recordCount, databaseBytes, unavailableIndexCount);
    }

    public void Open(string sourceIdentity, NativeFileId fileId)
    {
        var store = _paths().Select(path => new IndexStore(path)).FirstOrDefault(candidate => string.Equals(candidate.DatabasePath, sourceIdentity, StringComparison.OrdinalIgnoreCase));
        if (store is null)
            throw new InvalidOperationException("The result source is no longer configured.");
        _opener.Open(store, fileId);
    }
}
