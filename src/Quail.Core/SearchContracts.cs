namespace Quail.Core;

public sealed record SearchRequest(string Query);

public sealed class SearchResultAction
{
    private readonly Action? _open;

    public SearchResultAction()
    {
    }

    internal SearchResultAction(Action open) => _open = open ?? throw new ArgumentNullException(nameof(open));

    internal void Open()
    {
        if (_open is null)
        {
            throw new InvalidOperationException("The selected result is no longer available.");
        }

        _open();
    }
}

public sealed record SearchResult(
    SearchResultAction Action,
    string Name,
    string? FullPath,
    bool IsDirectory,
    string? Extension,
    long? LogicalSize);

public sealed record SearchIndexScale(
    int ConfiguredIndexCount,
    long RecordCount,
    long DatabaseBytes,
    int UnavailableIndexCount);

public enum SearchIndexState
{
    Absent,
    Incomplete,
    Complete,
    RebuildRequired
}

public sealed record SearchIndexStatus(
    SearchIndexState State,
    DateTimeOffset? LastRefreshedUtc);
