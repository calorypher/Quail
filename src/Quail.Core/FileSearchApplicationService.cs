using Quail.FileSystem;

namespace Quail.Core;

public sealed class FileSearchApplicationService
{
    private readonly FileSystemSearchService _fileSystem;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, FileSystemSearchAction> _actions = [];

    public FileSearchApplicationService(Func<IReadOnlyList<string>> paths)
    {
        _fileSystem = new FileSystemSearchService(paths);
    }

    internal FileSearchApplicationService(FileSystemSearchService fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public IReadOnlyList<SearchResult> Search(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = _fileSystem.Search(request.Query);
        var projected = new SearchResult[results.Count];
        var actions = new Dictionary<Guid, FileSystemSearchAction>(results.Count);

        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            var action = new SearchResultAction(Guid.NewGuid());
            actions.Add(action.Value, result.Action);
            projected[index] = new SearchResult(
                action,
                result.Result.Name,
                result.Result.FullPath,
                result.Result.IsDirectory,
                result.Result.Extension,
                result.Result.LogicalSize,
                result.Result.LastWriteTimeUtcFileTime,
                result.Result.Attributes);
        }

        lock (_gate)
        {
            foreach (var action in actions)
            {
                _actions.Add(action.Key, action.Value);
            }
        }

        return projected;
    }

    public SearchIndexScale GetSearchIndexScale()
    {
        var scale = _fileSystem.GetSearchIndexScale();
        return new SearchIndexScale(
            scale.ConfiguredIndexCount,
            scale.RecordCount,
            scale.DatabaseBytes,
            scale.UnavailableIndexCount);
    }

    public IReadOnlyList<SearchIndexStatus> GetIndexStatuses()
    {
        return _fileSystem.GetIndexStatuses()
            .Select(status => new SearchIndexStatus(
                status.State switch
                {
                    IndexState.Absent => SearchIndexState.Absent,
                    IndexState.Incomplete => SearchIndexState.Incomplete,
                    IndexState.Complete => SearchIndexState.Complete,
                    IndexState.RebuildRequired => SearchIndexState.RebuildRequired,
                    _ => throw new ArgumentOutOfRangeException(nameof(status))
                },
                status.LastRefreshedUtc))
            .ToArray();
    }

    public void Open(SearchResultAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        FileSystemSearchAction fileSystemAction;
        lock (_gate)
        {
            if (!_actions.TryGetValue(action.Value, out fileSystemAction!))
            {
                throw new InvalidOperationException("The selected result is no longer available.");
            }
        }

        _fileSystem.Open(fileSystemAction);
    }
}
