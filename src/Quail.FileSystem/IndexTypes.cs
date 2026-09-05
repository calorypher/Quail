namespace Quail.FileSystem;

public readonly struct NativeFileId : IEquatable<NativeFileId>
{
    private readonly byte[] _bytes;

    public NativeFileId(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is not (8 or 16))
        {
            throw new ArgumentException(
                "A native file identifier must be 8 or 16 bytes.",
                nameof(bytes));
        }
        _bytes = bytes.ToArray();
    }

    public ReadOnlyMemory<byte> Bytes => _bytes ?? Array.Empty<byte>();
    public bool Equals(NativeFileId other) => Bytes.Span.SequenceEqual(other.Bytes.Span);
    public override bool Equals(object? obj) => obj is NativeFileId other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var value in Bytes.Span)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
    public override string ToString() => Convert.ToHexString(Bytes.Span);
}

public sealed record NamespaceRecord(
    NativeFileId FileId,
    NativeFileId ParentFileId,
    string Name,
    uint Attributes,
    long Usn,
    ushort RecordVersion);

public sealed record VolumeDescriptor(
    string StableIdentity,
    string MountPoint,
    string FileSystem,
    string Label);

public sealed record UsnJournalState(
    ulong JournalId,
    long FirstUsn,
    long NextUsn,
    long LowestValidUsn,
    ushort MinimumSupportedMajorVersion,
    ushort MaximumSupportedMajorVersion);

public sealed record IncrementalCheckpoint(
    ulong JournalId,
    long NextUsn,
    long FirstUsn,
    long LowestValidUsn);

public sealed record JournalRecord(NamespaceRecord NamespaceRecord, uint Reason);
public sealed record JournalBatch(long NextUsn, IReadOnlyList<JournalRecord> Records);
public sealed record FileMetadata(long? LogicalSize, long? LastWriteTimeUtcFileTime);
public sealed record MetadataAcquisitionMetrics(long Attempts, long Succeeded, long Failed, string FailureCodes);

public sealed record BuildPhaseMetrics(
    TimeSpan SetupSchema,
    TimeSpan MftEnumerationReadParse,
    TimeSpan MetadataAcquisition,
    TimeSpan NamespaceAndFtsWrites,
    TimeSpan BulkTransactionCommits,
    TimeSpan JournalHandoff,
    TimeSpan NamespaceNormalization,
    TimeSpan ShortQueryBuild,
    TimeSpan CheckpointFinalization,
    TimeSpan StagingPromotion,
    TimeSpan Residual);

public enum IndexState
{
    Absent,
    Incomplete,
    Complete,
    RebuildRequired
}

public sealed record IndexStatus(
    IndexState State,
    string? VolumeIdentity,
    string? MountPoint,
    long RecordCount,
    DateTimeOffset? CompletedUtc,
    IncrementalCheckpoint? Checkpoint,
    string? Detail,
    DateTimeOffset? LastRefreshedUtc = null);

public sealed record BuildMetrics(
    long RecordCount,
    long ParseErrors,
    long UnsupportedRecords,
    TimeSpan Elapsed,
    TimeSpan CpuTime,
    long PeakWorkingSetBytes,
    MetadataAcquisitionMetrics? Metadata = null,
    BuildPhaseMetrics? Phases = null,
    TimeSpan? SinkElapsed = null);

public sealed record SyncResult(
    bool RebuildRequired,
    string? Reason,
    long AppliedRecords,
    long Batches,
    IncrementalCheckpoint? Checkpoint,
    MetadataAcquisitionMetrics? Metadata = null);

public sealed record PathResolution(bool Success, string? Path, string? Diagnostic);

public enum SearchEntryType { Any, File, Directory }

public sealed record FileSearchQuery(
    string NameQuery,
    SearchEntryType EntryType = SearchEntryType.Any,
    string? Extension = null,
    int Limit = 50,
    long? MinimumSize = null,
    long? MaximumSize = null,
    long? ModifiedAfterUtcFileTime = null,
    long? ModifiedBeforeUtcFileTime = null,
    bool Hidden = false,
    bool ReadOnly = false,
    bool System = false);

public sealed record FileSearchResult(
    NativeFileId FileId,
    string Name,
    string? FullPath,
    bool IsDirectory,
    string? Extension,
    long? LogicalSize,
    long? LastWriteTimeUtcFileTime,
    uint Attributes = 0);
