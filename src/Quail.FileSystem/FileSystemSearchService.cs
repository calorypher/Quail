namespace Quail.FileSystem;

public sealed record FileSystemSearchAction(string DatabasePath, NativeFileId FileId);

public sealed record FileSystemSearchResult(FileSystemSearchAction Action, FileSearchResult Result);

public sealed record FileSystemSearchIndexScale(
    int ConfiguredIndexCount,
    long RecordCount,
    long DatabaseBytes,
    int UnavailableIndexCount);

public sealed class FileSystemSearchService
{
    private readonly Func<IReadOnlyList<string>> _paths;
    private readonly IndexedEntryOpener _opener;

    public FileSystemSearchService(
        Func<IReadOnlyList<string>> paths,
        IndexedEntryOpener? opener = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _opener = opener ?? new IndexedEntryOpener();
    }

    public IReadOnlyList<FileSystemSearchResult> Search(string query)
    {
        var results = MultiIndexSearch.Search(
            _paths().Select(path => new IndexStore(path)),
            new FileSearchQuery(query, Limit: IndexStore.DefaultSearchResultLimit));

        return results
            .Select(result => new FileSystemSearchResult(
                new FileSystemSearchAction(result.SourceIdentity, result.Result.FileId),
                result.Result))
            .ToArray();
    }

    public FileSystemSearchIndexScale GetSearchIndexScale()
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

        return new FileSystemSearchIndexScale(
            paths.Count,
            recordCount,
            databaseBytes,
            unavailableIndexCount);
    }

    public IReadOnlyList<IndexStatus> GetIndexStatuses() => _paths()
        .Select(path => new IndexStore(path).GetStatus())
        .ToArray();

    public void Open(FileSystemSearchAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var store = _paths()
            .Select(path => new IndexStore(path))
            .FirstOrDefault(candidate => string.Equals(
                candidate.DatabasePath,
                action.DatabasePath,
                StringComparison.OrdinalIgnoreCase));
        if (store is null)
        {
            throw new InvalidOperationException("The result source is no longer configured.");
        }

        _opener.Open(store, action.FileId);
    }
}
