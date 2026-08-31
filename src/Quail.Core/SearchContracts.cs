namespace Quail.Core;

public sealed record SearchRequest(string Query);

public sealed class SearchResultAction
{
    public SearchResultAction() : this(Guid.NewGuid())
    {
    }

    internal SearchResultAction(Guid value) => Value = value;

    internal Guid Value { get; }
}

public sealed record SearchResult(
    SearchResultAction Action,
    string Name,
    string? FullPath,
    bool IsDirectory,
    string? Extension,
    long? LogicalSize,
    long? LastWriteTimeUtcFileTime,
    uint Attributes);

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
