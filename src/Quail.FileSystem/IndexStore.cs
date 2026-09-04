using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;

namespace Quail.FileSystem;

public enum IndexStoreJournalLifecycle
{
    PersistentWal,
    DeleteWhenQuiescent
}

public sealed class IndexStore
{
    private const int SchemaVersion = 4;
    private const string NamespaceIdentityFormat = "canonical-file-id-128-v1";
    private const string SearchIndexFormat = "fts5-trigram-v1";
    private const string MetadataFormat = "file-metadata-v1";
    public const int DefaultSearchResultLimit = 50;
    public const int MaximumSearchResultLimit = 1_000;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeReadOnly = 0x1;
    private const uint FileAttributeHidden = 0x2;
    private const uint FileAttributeSystem = 0x4;
    private const uint ShortQueryRelevantAttributes = FileAttributeDirectory | FileAttributeHidden | FileAttributeSystem;
    private const int JournalTransitionTimeoutMilliseconds = 30_000;
    private readonly string _databasePath;
    private readonly IndexStoreJournalLifecycle _journalLifecycle;

    public IndexStore(string databasePath) : this(databasePath, IndexStoreJournalLifecycle.PersistentWal)
    {
    }

    public IndexStore(string databasePath, IndexStoreJournalLifecycle journalLifecycle)
    {
        if (!Enum.IsDefined(journalLifecycle))
        {
            throw new ArgumentOutOfRangeException(nameof(journalLifecycle));
        }

        _databasePath = Path.GetFullPath(databasePath);
        _journalLifecycle = journalLifecycle;
    }

    public string DatabasePath => _databasePath;
    private string StagingPath => _databasePath + ".building";

    public IndexStatus GetStatus()
    {
        if (File.Exists(_databasePath))
        {
            return ReadStatus(_databasePath);
        }

        if (File.Exists(StagingPath))
        {
            return new IndexStatus(
                IndexState.Incomplete,
                null,
                null,
                0,
                null,
                null,
                "A replacement build is incomplete; no complete index is available.");
        }

        return new IndexStatus(IndexState.Absent, null, null, 0, null, null, "No index database exists.");
    }

    public BuildMetrics Build(string mountPoint, int? failAfterRecords = null)
    {
        var volume = NtfsVolume.Validate(mountPoint);
        using var metadata = new NtfsMetadataAcquirer(volume);
        var startingJournal = NtfsJournal.Query(metadata.VolumeHandle);
        BuildMetrics? enumeration = null;
        return BuildWithHandoff(volume, sink =>
        {
            enumeration = NtfsEnumerator.Enumerate(volume, metadata.VolumeHandle, sink);
            foreach (var root in NtfsVolume.GetRootRecords(volume))
            {
                sink(root);
            }
        }, startingJournal, failAfterRecords, metadata.VolumeHandle, metadata.Acquire, () => metadata.Metrics) with
        {
            ParseErrors = enumeration?.ParseErrors ?? 0,
            UnsupportedRecords = enumeration?.UnsupportedRecords ?? 0,
        };
    }

    // This deterministic seam creates a valid v2 checkpoint for parser and transaction tests.
    public BuildMetrics BuildFromRecords(
        VolumeDescriptor volume,
        Action<Action<NamespaceRecord>> produce,
        int? failAfterRecords = null,
        IncrementalCheckpoint? checkpoint = null,
        Func<NamespaceRecord, FileMetadata>? acquireMetadata = null)
    {
        var finalCheckpoint = checkpoint ?? new IncrementalCheckpoint(0x515541494CUL, 0, 0, 0);
        return BuildStaging(
            volume,
            produce,
            failAfterRecords,
            connection => CompleteBuild(connection, finalCheckpoint),
            acquireMetadata ?? UnavailableMetadata,
            null);
    }

    internal BuildMetrics BuildFromRecordsWithHandoffForTesting(
        VolumeDescriptor volume,
        Action<Action<NamespaceRecord>> produce,
        UsnJournalState startingJournal,
        UsnJournalState endingJournal,
        IEnumerable<JournalBatch> batches,
        Func<NamespaceRecord, FileMetadata>? acquireMetadata = null)
    {
        var acquire = acquireMetadata ?? UnavailableMetadata;
        return BuildStaging(volume, produce, null, connection =>
        {
            var checkpoint = new IncrementalCheckpoint(
                startingJournal.JournalId,
                startingJournal.NextUsn,
                endingJournal.FirstUsn,
                endingJournal.LowestValidUsn);
            if (!TryValidateContinuity(checkpoint, endingJournal, out var reason))
            {
                throw new InvalidOperationException(
                    $"Initial build handoff requires rebuild: {reason}");
            }
            foreach (var batch in batches)
            {
                ApplyBatch(connection, batch, endingJournal, false, acquire);
                checkpoint = checkpoint with { NextUsn = batch.NextUsn };
            }
            CompleteBuild(connection, checkpoint);
        }, acquire, null);
    }

    public SyncResult Sync(string mountPoint)
    {
        var volume = NtfsVolume.Validate(mountPoint);
        if (!File.Exists(_databasePath))
        {
            return new SyncResult(true, "index-absent", 0, 0, null);
        }

        using var connection = Open(_databasePath);
        try
        {
            if (!TryReadSyncCheckpoint(connection, volume, out var checkpoint, out var reason))
            {
                MarkRebuildRequired(connection, reason!);
                return new SyncResult(true, reason, 0, 0, ReadCheckpoint(connection));
            }

            UsnJournalState journal;
            try
            {
                journal = NtfsJournal.Query(volume);
            }
            catch (Exception exception)
            {
                const string queryFailure = "journal-query-failed";
                MarkRebuildRequired(connection, queryFailure);
                return new SyncResult(true, $"{queryFailure}: {exception.Message}", 0, 0, checkpoint);
            }

            if (!TryValidateContinuity(checkpoint!, journal, out reason))
            {
                MarkRebuildRequired(connection, reason!);
                return new SyncResult(true, reason, 0, 0, checkpoint);
            }

            long applied = 0;
            long batches = 0;
            var finalCheckpoint = checkpoint! with { FirstUsn = journal.FirstUsn, LowestValidUsn = journal.LowestValidUsn };
            NtfsMetadataAcquirer? metadata = null;
            try
            {
                metadata = new NtfsMetadataAcquirer(volume);
                var finalCursor = NtfsJournal.Read(volume, finalCheckpoint, batch =>
                {
                    ApplyBatch(connection, batch, journal, false, metadata.Acquire);
                    applied += batch.Records.Count;
                    batches++;
                    finalCheckpoint = finalCheckpoint with { NextUsn = batch.NextUsn };
                });
                finalCheckpoint = finalCheckpoint with { NextUsn = finalCursor };
                PersistSuccessfulSync(connection, finalCheckpoint);
                return new SyncResult(false, null, applied, batches, finalCheckpoint, metadata.Metrics);
            }
            catch (Exception exception)
            {
                const string readFailure = "journal-read-or-parse-failed";
                MarkRebuildRequired(connection, $"{readFailure}: {exception.Message}");
                return new SyncResult(true, $"{readFailure}: {exception.Message}", applied, batches, ReadCheckpoint(connection), metadata?.Metrics);
            }
            finally
            {
                metadata?.Dispose();
            }
        }
        finally
        {
            FinalizeJournal(connection);
        }
    }

    // Used by focused automated tests to prove transaction boundaries without a Windows volume.
    public void ApplyParsedBatchesForTesting(
        VolumeDescriptor volume,
        UsnJournalState journal,
        IEnumerable<JournalBatch> batches,
        bool failBeforeCommit = false,
        Func<NamespaceRecord, FileMetadata>? acquireMetadata = null)
    {
        using var connection = Open(_databasePath);
        try
        {
            if (!TryReadSyncCheckpoint(connection, volume, out var checkpoint, out var reason))
            {
                throw new InvalidOperationException(reason);
            }

            if (!TryValidateContinuity(checkpoint!, journal, out reason))
            {
                throw new InvalidOperationException(reason);
            }

            var acquire = acquireMetadata ?? UnavailableMetadata;
            foreach (var batch in batches)
            {
                ApplyBatch(connection, batch, journal, failBeforeCommit, acquire);
            }
            PersistSuccessfulSync(connection, ReadCheckpoint(connection) ?? throw new InvalidOperationException("Test sync requires a checkpoint."));
        }
        finally
        {
            FinalizeJournal(connection);
        }
    }

    public PathResolution ReconstructPath(NativeFileId fileId)
    {
        if (!File.Exists(_databasePath))
        {
            return new PathResolution(false, null, "No complete index database exists.");
        }

        using var connection = OpenReadOnly(_databasePath);
        if (GetMeta(connection, "build_state") != "complete" ||
            GetMeta(connection, "schema_version") != SchemaVersion.ToString() ||
            GetMeta(connection, "namespace_identity_format") != NamespaceIdentityFormat ||
            GetMeta(connection, "metadata_format") != MetadataFormat)
        {
            return new PathResolution(false, null, "The index is not current and complete.");
        }

        var mount = GetMeta(connection, "mount_point") ?? string.Empty;
        var parts = new List<string>();
        var seen = new HashSet<NativeFileId>();
        var current = CanonicalizeLegacyId(fileId);
        while (true)
        {
            if (!seen.Add(current))
            {
                return new PathResolution(false, null, "Cycle detected in parent relationships.");
            }

            var entry = ReadEntry(connection, current);
            if (entry is null)
            {
                return new PathResolution(false, null, $"Missing parent or record '{current}'.");
            }

            if (!string.IsNullOrEmpty(entry.Value.Name))
            {
                parts.Add(entry.Value.Name);
            }

            if (entry.Value.Parent.Equals(current))
            {
                break;
            }

            current = entry.Value.Parent;
        }
        parts.Reverse();
        return new PathResolution(true, Path.Combine(mount, Path.Combine(parts.ToArray())), null);
    }

    public PathResolution ResolveOpenPath(NativeFileId fileId)
    {
        var status = GetStatus();
        if (status.State != IndexState.Complete)
            throw new InvalidOperationException("Open requires a complete current index.");
        return ReconstructPath(fileId);
    }

    public IReadOnlyList<NamespaceRecord> ReadAllForDiagnostics()
    {
        if (!File.Exists(_databasePath))
        {
            return Array.Empty<NamespaceRecord>();
        }

        using var connection = OpenReadOnly(_databasePath);
        if (GetMeta(connection, "build_state") != "complete" ||
            GetMeta(connection, "schema_version") != SchemaVersion.ToString() ||
            GetMeta(connection, "namespace_identity_format") != NamespaceIdentityFormat ||
            GetMeta(connection, "metadata_format") != MetadataFormat)
        {
            return Array.Empty<NamespaceRecord>();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT file_id,parent_file_id,name,attributes,usn,record_version FROM namespace_entries ORDER BY name";
        using var reader = command.ExecuteReader();
        var result = new List<NamespaceRecord>();
        while (reader.Read())
        {
            var fileId = new NativeFileId((byte[])reader[0]);
            var parentFileId = new NativeFileId((byte[])reader[1]);
            var name = reader.GetString(2);
            var attributes = checked((uint)reader.GetInt64(3));
            var usn = reader.GetInt64(4);
            var recordVersion = checked((ushort)reader.GetInt64(5));
            result.Add(new NamespaceRecord(fileId, parentFileId, name, attributes, usn, recordVersion));
        }

        return result;
    }

    public IReadOnlyList<FileSearchResult> Search(FileSearchQuery query, FileSearchRankingContext? rankingContext = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        var nameQuery = ValidateNameQuery(query.NameQuery);
        var extension = NormalizeExtension(query.Extension);
        ValidateMetadataBounds(query);
        if (query.Limit is < 1 or > MaximumSearchResultLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(query.Limit), $"Search result limit must be between 1 and {MaximumSearchResultLimit}.");
        }

        if (!File.Exists(_databasePath))
        {
            throw new InvalidOperationException("Search requires a complete current index database.");
        }

        using var connection = OpenReadOnly(_databasePath);
        EnsureSearchable(connection);
        var context = rankingContext ?? FileSearchRankingContext.ForCurrentMachine();
        if (nameQuery.Length <= 2 && IsUnfiltered(query))
        {
            return ShortQueryIndex.Search(connection, nameQuery, query.Limit, context);
        }

        var usesTrigramIndex = nameQuery.Length >= 3;
        var candidateLimit = query.Limit;
        var results = ReadSearchCandidates(
            connection,
            query,
            nameQuery,
            extension,
            usesTrigramIndex,
            candidateLimit,
            null);
        var canExpandForCurrentUser = CanExpandForCurrentUser(results, context);
        var hasCurrentUserVisible = results.Any(result =>
            FileSearchRanking.Classify(result, nameQuery, context).Location == FileSearchLocation.CurrentUserVisible);
        if (canExpandForCurrentUser && (results.Count == candidateLimit || !hasCurrentUserVisible))
        {
            var seen = results.Select(result => result.FileId).ToHashSet();
            foreach (var textClass in Enum.GetValues<SearchTextCandidateClass>())
            {
                foreach (var result in ReadSearchCandidates(
                             connection,
                             query,
                             nameQuery,
                             extension,
                             usesTrigramIndex,
                             candidateLimit,
                             textClass))
                {
                    if (seen.Add(result.FileId)) results.Add(result);
                }
            }
        }

        return results
            .OrderBy(result => result, new FileSearchResultComparer(nameQuery, context))
            .Take(query.Limit)
            .ToArray();
    }

    private static List<FileSearchResult> ReadSearchCandidates(
        SqliteConnection connection,
        FileSearchQuery query,
        string nameQuery,
        string? extension,
        bool usesTrigramIndex,
        int candidateLimit,
        SearchTextCandidateClass? textClass)
    {
        var results = new List<FileSearchResult>();
        using var command = CreateSearchCandidateCommand(
            connection,
            query,
            nameQuery,
            extension,
            usesTrigramIndex,
            candidateLimit,
            textClass);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var fileId = new NativeFileId((byte[])reader[0]);
            var name = reader.GetString(2);
            var attributes = checked((uint)reader.GetInt64(3));
            var isDirectory = (attributes & FileAttributeDirectory) != 0;
            results.Add(new FileSearchResult(
                fileId,
                name,
                ReconstructPath(connection, fileId).Path,
                isDirectory,
                isDirectory ? null : GetExtension(name),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                attributes));
        }

        return results;
    }

    private static bool CanExpandForCurrentUser(IReadOnlyList<FileSearchResult> candidates, FileSearchRankingContext context)
    {
        var currentUserSegments = FileSearchRankingContext.GetSegments(context.CurrentUserProfilePath);
        return currentUserSegments.Count > 0 && candidates.Any(candidate =>
        {
            var pathSegments = FileSearchRankingContext.GetSegments(candidate.FullPath);
            return pathSegments.Count > 0 && string.Equals(pathSegments[0], currentUserSegments[0], StringComparison.OrdinalIgnoreCase);
        });
    }

    private static SqliteCommand CreateSearchCandidateCommand(
        SqliteConnection connection,
        FileSearchQuery query,
        string nameQuery,
        string? extension,
        bool usesTrigramIndex,
        int candidateLimit,
        SearchTextCandidateClass? textClass)
    {
        var command = connection.CreateCommand();
        var entry = "namespace_entries";
        var textPredicate = textClass is null ? null : GetTextCandidatePredicate(textClass.Value);
        var textClassClause = textPredicate is null ? string.Empty : $"AND {textPredicate}";
        var ordering = $"{entry}.name COLLATE NOCASE ASC, {entry}.name COLLATE BINARY ASC, {entry}.file_id ASC";
        command.CommandText = usesTrigramIndex
            ? $"""
                SELECT {entry}.file_id, {entry}.parent_file_id, {entry}.name, {entry}.attributes, {entry}.logical_size, {entry}.last_write_time_utc
                FROM search_entries
                JOIN namespace_entries ON {entry}.rowid = search_entries.rowid
                WHERE search_entries MATCH $match
                  {textClassClause}
                  AND ($type = 0 OR ($type = 1 AND ({entry}.attributes & $directoryAttribute) = 0) OR ($type = 2 AND ({entry}.attributes & $directoryAttribute) != 0))
                  AND ($extension IS NULL OR (({entry}.attributes & $directoryAttribute) = 0 AND lower({entry}.name) LIKE '%.' || $extension))
                  AND ($minimumSize IS NULL OR {entry}.logical_size >= $minimumSize)
                  AND ($maximumSize IS NULL OR {entry}.logical_size <= $maximumSize)
                  AND ($modifiedAfter IS NULL OR {entry}.last_write_time_utc >= $modifiedAfter)
                  AND ($modifiedBefore IS NULL OR {entry}.last_write_time_utc <= $modifiedBefore)
                  AND ($hidden = 0 OR ({entry}.attributes & $hiddenAttribute) != 0)
                  AND ($readOnly = 0 OR ({entry}.attributes & $readOnlyAttribute) != 0)
                  AND ($system = 0 OR ({entry}.attributes & $systemAttribute) != 0)
                ORDER BY {ordering}
                LIMIT $limit;
                """
            : $"""
                SELECT {entry}.file_id, {entry}.parent_file_id, {entry}.name, {entry}.attributes, {entry}.logical_size, {entry}.last_write_time_utc
                FROM namespace_entries
                WHERE instr(lower({entry}.name), lower($query)) > 0
                  {textClassClause}
                  AND ($type = 0 OR ($type = 1 AND ({entry}.attributes & $directoryAttribute) = 0) OR ($type = 2 AND ({entry}.attributes & $directoryAttribute) != 0))
                  AND ($extension IS NULL OR (({entry}.attributes & $directoryAttribute) = 0 AND lower({entry}.name) LIKE '%.' || $extension))
                  AND ($minimumSize IS NULL OR {entry}.logical_size >= $minimumSize)
                  AND ($maximumSize IS NULL OR {entry}.logical_size <= $maximumSize)
                  AND ($modifiedAfter IS NULL OR {entry}.last_write_time_utc >= $modifiedAfter)
                  AND ($modifiedBefore IS NULL OR {entry}.last_write_time_utc <= $modifiedBefore)
                  AND ($hidden = 0 OR ({entry}.attributes & $hiddenAttribute) != 0)
                  AND ($readOnly = 0 OR ({entry}.attributes & $readOnlyAttribute) != 0)
                  AND ($system = 0 OR ({entry}.attributes & $systemAttribute) != 0)
                ORDER BY {ordering}
                LIMIT $limit;
                """;
        command.Parameters.AddWithValue("$match", ToFtsPhrase(nameQuery));
        command.Parameters.AddWithValue("$query", nameQuery);
        command.Parameters.AddWithValue("$likeQuery", EscapeLike(nameQuery));
        command.Parameters.AddWithValue("$type", (int)query.EntryType);
        command.Parameters.AddWithValue("$directoryAttribute", (long)FileAttributeDirectory);
        command.Parameters.AddWithValue("$extension", (object?)extension ?? DBNull.Value);
        command.Parameters.AddWithValue("$minimumSize", (object?)query.MinimumSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$maximumSize", (object?)query.MaximumSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$modifiedAfter", (object?)query.ModifiedAfterUtcFileTime ?? DBNull.Value);
        command.Parameters.AddWithValue("$modifiedBefore", (object?)query.ModifiedBeforeUtcFileTime ?? DBNull.Value);
        command.Parameters.AddWithValue("$hidden", query.Hidden ? 1 : 0);
        command.Parameters.AddWithValue("$readOnly", query.ReadOnly ? 1 : 0);
        command.Parameters.AddWithValue("$system", query.System ? 1 : 0);
        command.Parameters.AddWithValue("$hiddenAttribute", (long)FileAttributeHidden);
        command.Parameters.AddWithValue("$readOnlyAttribute", (long)FileAttributeReadOnly);
        command.Parameters.AddWithValue("$systemAttribute", (long)FileAttributeSystem);
        command.Parameters.AddWithValue("$limit", candidateLimit);
        return command;
    }

    private static string GetTextCandidatePredicate(SearchTextCandidateClass textClass)
    {
        const string exact = "lower(namespace_entries.name) = lower($query)";
        const string prefix = "lower(namespace_entries.name) LIKE lower($likeQuery) || '%' ESCAPE '\\'";
        var tokenPrefix = string.Join(
            " OR ",
            FileSearchRanking.TokenSeparators
                .Select(separator => $"lower(namespace_entries.name) LIKE '%' || '{EscapeSqlLiteral(EscapeLike(separator.ToString()))}' || lower($likeQuery) || '%' ESCAPE '\\'"));

        return textClass switch
        {
            SearchTextCandidateClass.Exact => exact,
            SearchTextCandidateClass.Prefix => $"NOT ({exact}) AND ({prefix})",
            SearchTextCandidateClass.TokenPrefix => $"NOT ({exact}) AND NOT ({prefix}) AND ({tokenPrefix})",
            SearchTextCandidateClass.Substring => $"NOT ({exact}) AND NOT ({prefix}) AND NOT ({tokenPrefix})",
            _ => throw new ArgumentOutOfRangeException(nameof(textClass))
        };
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static bool IsUnfiltered(FileSearchQuery query) =>
        query.EntryType == SearchEntryType.Any &&
        query.Extension is null &&
        query.MinimumSize is null &&
        query.MaximumSize is null &&
        query.ModifiedAfterUtcFileTime is null &&
        query.ModifiedBeforeUtcFileTime is null &&
        !query.Hidden &&
        !query.ReadOnly &&
        !query.System;

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''");

    private enum SearchTextCandidateClass
    {
        Exact,
        Prefix,
        TokenPrefix,
        Substring
    }

    private sealed class FileSearchResultComparer(string query, FileSearchRankingContext context) : IComparer<FileSearchResult>
    {
        public int Compare(FileSearchResult? left, FileSearchResult? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            return FileSearchRanking.Compare(left, right, query, context);
        }
    }

    public void EnsureSearchReady()
    {
        var status = GetStatus();
        if (status.State != IndexState.Complete)
        {
            throw new InvalidOperationException($"Search requires a usable index: {status.Detail ?? status.State.ToString()}.");
        }
        try
        {
            using var connection = OpenReadOnly(_databasePath);
            EnsureSearchable(connection);
        }
        catch (SqliteException exception)
        {
            throw new InvalidOperationException("Search requires a readable current index.", exception);
        }
    }

    private BuildMetrics BuildWithHandoff(
        VolumeDescriptor volume,
        Action<Action<NamespaceRecord>> produce,
        UsnJournalState startingJournal,
        int? failAfterRecords,
        SafeFileHandle volumeHandle,
        Func<NamespaceRecord, FileMetadata> acquireMetadata,
        Func<MetadataAcquisitionMetrics>? metadataMetrics)
    {
        return BuildStaging(volume, produce, failAfterRecords, connection =>
        {
            var afterEnumeration = NtfsJournal.Query(volumeHandle);
            var initialCheckpoint = new IncrementalCheckpoint(
                startingJournal.JournalId,
                startingJournal.NextUsn,
                afterEnumeration.FirstUsn,
                afterEnumeration.LowestValidUsn);
            if (!TryValidateContinuity(initialCheckpoint, afterEnumeration, out var reason))
            {
                throw new InvalidOperationException(
                    $"Initial build handoff requires rebuild: {reason}");
            }

            var finalCheckpoint = initialCheckpoint;
            var finalCursor = NtfsJournal.Read(volumeHandle, initialCheckpoint, batch =>
            {
                ApplyBatch(connection, batch, afterEnumeration, false, acquireMetadata);
                finalCheckpoint = finalCheckpoint with { NextUsn = batch.NextUsn };
            });
            if (finalCursor != finalCheckpoint.NextUsn)
            {
                finalCheckpoint = finalCheckpoint with { NextUsn = finalCursor };
            }
            CompleteBuild(connection, finalCheckpoint);
        }, acquireMetadata, metadataMetrics);
    }

    private BuildMetrics BuildStaging(
        VolumeDescriptor volume,
        Action<Action<NamespaceRecord>> produce,
        int? failAfterRecords,
        Action<SqliteConnection> finish,
        Func<NamespaceRecord, FileMetadata> acquireMetadata,
        Func<MetadataAcquisitionMetrics>? metadataMetrics)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        DeleteDatabaseAndSidecars(StagingPath);

        var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var stopwatch = Stopwatch.StartNew();
        long count = 0;
        try
        {
            using (var connection = Open(StagingPath))
            {
                try
                {
                    CreateSchema(connection);
                    SetMeta(connection, "build_state", "building");
                    SetMeta(connection, "volume_identity", volume.StableIdentity);
                    SetMeta(connection, "mount_point", volume.MountPoint);
                    SetMeta(connection, "file_system", volume.FileSystem);
                    SetMeta(connection, "volume_label", volume.Label);
                    SetMeta(connection, "namespace_identity_format", NamespaceIdentityFormat);
                    SetMeta(connection, "started_utc", DateTimeOffset.UtcNow.ToString("O"));
                    count = WriteProducedRecords(connection, produce, failAfterRecords, acquireMetadata);
                    finish(connection);
                    using var checkpoint = connection.CreateCommand();
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    checkpoint.ExecuteNonQuery();
                }
                finally
                {
                    FinalizeJournal(connection);
                }
            }
            PromoteStaging();
            stopwatch.Stop();
            return new BuildMetrics(
                count,
                0,
                0,
                stopwatch.Elapsed,
                process.TotalProcessorTime - cpu,
                process.PeakWorkingSet64,
                metadataMetrics?.Invoke());
        }
        catch
        {
            stopwatch.Stop();
            throw;
        }
    }

    private static long WriteProducedRecords(
        SqliteConnection connection,
        Action<Action<NamespaceRecord>> produce,
        int? failAfterRecords,
        Func<NamespaceRecord, FileMetadata> acquireMetadata)
    {
        long count = 0;
        var transaction = connection.BeginTransaction();
        try
        {
            produce(record =>
            {
                if (failAfterRecords is not null && count >= failAfterRecords)
                {
                    throw new InvalidOperationException(
                        "Test fault injection interrupted the build.");
                }

                var canonical = CanonicalizeInitialRecord(record);
                Upsert(connection, transaction, canonical, acquireMetadata(canonical));
                count++;
                if (count % 2048 == 0)
                {
                    transaction.Commit();
                    transaction.Dispose();
                    transaction = connection.BeginTransaction();
                }
            });
            transaction.Commit();
            return count;
        }
        finally
        {
            transaction.Dispose();
        }
    }

    private static void ApplyBatch(
        SqliteConnection connection,
        JournalBatch batch,
        UsnJournalState journal,
        bool failBeforeCommit,
        Func<NamespaceRecord, FileMetadata> acquireMetadata)
    {
        var canonicalRecords = batch.Records
            .Select(record => new JournalRecord(CanonicalizeJournalRecord(record.NamespaceRecord), record.Reason))
            .ToArray();
        // Metadata is current filesystem state and can be acquired once per
        // FileId. Namespace and compact-index transitions below must retain the
        // journal's source order because parent/child lifecycle records depend
        // on it.
        var metadata = new Dictionary<NativeFileId, FileMetadata>();
        foreach (var record in canonicalRecords)
        {
            if (UsnReason.IsFileDelete(record.Reason) || metadata.ContainsKey(record.NamespaceRecord.FileId))
            {
                continue;
            }

            var requiresFallback = UsnReason.IsRenameNewName(record.Reason) && !EntryExists(connection, record.NamespaceRecord.FileId);
            if (UsnReason.RequiresMetadataRefresh(record.Reason) || requiresFallback)
            {
                metadata.Add(record.NamespaceRecord.FileId, acquireMetadata(record.NamespaceRecord));
            }
        }
        using var transaction = connection.BeginTransaction();
        var maintainShortQueryIndex = ShortQueryIndex.IsCurrent(connection);
        foreach (var record in canonicalRecords)
        {
            if (UsnReason.IsFileDelete(record.Reason))
            {
                DeleteCurrentEntry(
                    connection,
                    transaction,
                    record.NamespaceRecord.FileId,
                    maintainShortQueryIndex);
            }
            else if (!UsnReason.IsRenameOldName(record.Reason))
            {
                if (maintainShortQueryIndex)
                {
                    UpsertWithShortQueryIndex(
                        connection,
                        transaction,
                        record.NamespaceRecord,
                        metadata.GetValueOrDefault(record.NamespaceRecord.FileId));
                }
                else
                {
                    Upsert(connection, transaction, record.NamespaceRecord, metadata.GetValueOrDefault(record.NamespaceRecord.FileId));
                }
            }
        }
        if (failBeforeCommit)
        {
            throw new InvalidOperationException(
                "Test fault injection interrupted the journal batch before commit.");
        }

        if (maintainShortQueryIndex)
        {
            ShortQueryIndex.AdvanceGeneration(connection, transaction);
        }

        var checkpoint = new IncrementalCheckpoint(journal.JournalId, batch.NextUsn, journal.FirstUsn, journal.LowestValidUsn);
        SetCheckpoint(connection, transaction, checkpoint);
        SetMeta(
            connection,
            transaction,
            "record_count",
            CountEntries(connection, transaction).ToString(System.Globalization.CultureInfo.InvariantCulture));
        transaction.Commit();
    }

    private static void UpsertWithShortQueryIndex(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NamespaceRecord record,
        FileMetadata? metadata)
    {
        var existing = ReadShortQueryEntry(connection, record.FileId);
        if (existing is not null &&
            existing.Value.ParentFileId.Equals(record.ParentFileId) &&
            string.Equals(existing.Value.Name, record.Name, StringComparison.Ordinal) &&
            ((existing.Value.Attributes ^ record.Attributes) & ShortQueryRelevantAttributes) == 0)
        {
            Upsert(connection, transaction, record, metadata);
            return;
        }

        var affected = ShortQueryIndex.IsDirectory(connection, record.FileId)
            ? ShortQueryIndex.ReadSubtreeIds(connection, record.FileId)
            : new[] { record.FileId };
        foreach (var fileId in affected)
        {
            ShortQueryIndex.RemoveCurrentEntry(connection, transaction, fileId);
        }

        Upsert(connection, transaction, record, metadata);
        foreach (var fileId in affected)
        {
            ShortQueryIndex.InsertCurrentEntry(connection, transaction, fileId);
        }
    }

    private static void Upsert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NamespaceRecord record,
        FileMetadata? metadata)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO namespace_entries(file_id,parent_file_id,name,attributes,usn,record_version,logical_size,last_write_time_utc) VALUES($id,$parent,$name,$attributes,$usn,$version,$logicalSize,$lastWriteTime) ON CONFLICT(file_id) DO UPDATE SET parent_file_id=excluded.parent_file_id,name=excluded.name,attributes=excluded.attributes,usn=excluded.usn,record_version=excluded.record_version,logical_size=CASE WHEN $replaceMetadata THEN excluded.logical_size ELSE namespace_entries.logical_size END,last_write_time_utc=CASE WHEN $replaceMetadata THEN excluded.last_write_time_utc ELSE namespace_entries.last_write_time_utc END";
        command.Parameters.Add("$id", SqliteType.Blob).Value = record.FileId.Bytes.ToArray();
        command.Parameters.Add("$parent", SqliteType.Blob).Value = record.ParentFileId.Bytes.ToArray();
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$attributes", (long)record.Attributes);
        command.Parameters.AddWithValue("$usn", record.Usn);
        command.Parameters.AddWithValue("$version", record.RecordVersion);
        command.Parameters.AddWithValue("$replaceMetadata", metadata is not null ? 1 : 0);
        command.Parameters.AddWithValue("$logicalSize", (object?)metadata?.LogicalSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastWriteTime", (object?)metadata?.LastWriteTimeUtcFileTime ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static FileMetadata UnavailableMetadata(NamespaceRecord _) => new(null, null);

    private static bool EntryExists(SqliteConnection connection, NativeFileId fileId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM namespace_entries WHERE file_id=$id LIMIT 1";
        command.Parameters.Add("$id", SqliteType.Blob).Value = fileId.Bytes.ToArray();
        return command.ExecuteScalar() is not null;
    }

    private static ShortQueryEntry? ReadShortQueryEntry(SqliteConnection connection, NativeFileId fileId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT parent_file_id,name,attributes FROM namespace_entries WHERE file_id=$id LIMIT 1;";
        command.Parameters.Add("$id", SqliteType.Blob).Value = fileId.Bytes.ToArray();
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ShortQueryEntry(new NativeFileId((byte[])reader[0]), reader.GetString(1), checked((uint)reader.GetInt64(2)))
            : null;
    }

    private static void DeleteCurrentEntry(
        SqliteConnection connection,
        SqliteTransaction transaction,
        NativeFileId fileId,
        bool maintainShortQueryIndex)
    {
        // A directory delete makes every currently known descendant
        // unreachable. Remove the known subtree child-first so this committed
        // batch cannot leave namespace or rank-parent orphans; later journal
        // delete records for descendants are idempotent no-ops.
        var affected = ShortQueryIndex.IsDirectory(connection, fileId)
            ? ShortQueryIndex.ReadSubtreeIds(connection, fileId)
            : [fileId];
        for (var index = affected.Count - 1; index >= 0; index--)
        {
            if (maintainShortQueryIndex)
            {
                ShortQueryIndex.RemoveCurrentEntry(connection, transaction, affected[index]);
            }

            DeleteOne(connection, transaction, affected[index]);
        }
    }

    private static void DeleteOne(SqliteConnection connection, SqliteTransaction transaction, NativeFileId fileId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM namespace_entries WHERE file_id=$id";
        command.Parameters.Add("$id", SqliteType.Blob).Value = fileId.Bytes.ToArray();
        command.ExecuteNonQuery();
    }

    private static long CountEntries(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM namespace_entries";
        return Convert.ToInt64(
            command.ExecuteScalar(),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static NamespaceRecord CanonicalizeInitialRecord(NamespaceRecord record) => record with
    {
        FileId = CanonicalizeLegacyId(record.FileId),
        ParentFileId = CanonicalizeLegacyId(record.ParentFileId),
    };

    private static NamespaceRecord CanonicalizeJournalRecord(NamespaceRecord record)
    {
        if (record.RecordVersion == 2)
        {
            return CanonicalizeInitialRecord(record);
        }

        if (record.RecordVersion != 3)
        {
            throw new NotSupportedException(
                $"Unsupported USN record major version {record.RecordVersion}.");
        }

        if (!IsObservedV3Shape(record.FileId) || !IsObservedV3Shape(record.ParentFileId))
        {
            throw new NotSupportedException("USN v3 file identifiers are not compatible with the validated NTFS canonical identity format.");
        }

        return record;
    }

    private static NativeFileId CanonicalizeLegacyId(NativeFileId id)
    {
        if (id.Bytes.Length == 16) return id;
        var canonical = new byte[16];
        id.Bytes.Span.CopyTo(canonical);
        return new NativeFileId(canonical);
    }

    private static bool IsObservedV3Shape(NativeFileId id) =>
        id.Bytes.Length == 16 && id.Bytes.Span[8..].SequenceEqual(new byte[8]);

    private static string ValidateNameQuery(string? nameQuery)
    {
        if (string.IsNullOrWhiteSpace(nameQuery))
        {
            throw new ArgumentException("Search query must not be empty.", nameof(nameQuery));
        }

        if (nameQuery.Contains('\0'))
        {
            throw new ArgumentException(
                "Search query must not contain a null character.",
                nameof(nameQuery));
        }

        return nameQuery;
    }

    private static void ValidateMetadataBounds(FileSearchQuery query)
    {
        if (query.MinimumSize < 0 || query.MaximumSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(query),
                "Size bounds must be non-negative.");
        }

        if (query.MinimumSize is not null && query.MaximumSize is not null && query.MinimumSize > query.MaximumSize)
        {
            throw new ArgumentException("Minimum size must not exceed maximum size.", nameof(query));
        }

        if (query.ModifiedAfterUtcFileTime <= 0 || query.ModifiedBeforeUtcFileTime <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Modified-time bounds must be positive UTC FILETIME values.");
        }

        if (query.ModifiedAfterUtcFileTime is not null && query.ModifiedBeforeUtcFileTime is not null && query.ModifiedAfterUtcFileTime > query.ModifiedBeforeUtcFileTime)
        {
            throw new ArgumentException("Modified-after must not exceed modified-before.", nameof(query));
        }
    }

    private static string ToFtsPhrase(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static string? NormalizeExtension(string? extension)
    {
        if (extension is null)
        {
            return null;
        }

        var normalized = extension.Trim();
        if (normalized.StartsWith(".", StringComparison.Ordinal))
        {
            normalized = normalized.TrimStart('.');
        }

        if (string.IsNullOrEmpty(normalized) || normalized.Contains('.') || normalized.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '%', '_']) >= 0)
        {
            throw new ArgumentException("Extension must be a single extension without dots, paths, or wildcards.", nameof(extension));
        }

        return normalized.ToLowerInvariant();
    }

    private static string? GetExtension(string name)
    {
        var extension = Path.GetExtension(name);
        return string.IsNullOrEmpty(extension) ? null : extension[1..];
    }

    private static bool TryReadSyncCheckpoint(
        SqliteConnection connection,
        VolumeDescriptor volume,
        out IncrementalCheckpoint? checkpoint,
        out string? reason)
    {
        checkpoint = null;
        reason = null;
        if (GetMeta(connection, "schema_version") != SchemaVersion.ToString())
        {
            reason = "schema-lacks-authoritative-checkpoint";
            return false;
        }

        if (GetMeta(connection, "metadata_format") != MetadataFormat)
        {
            reason = "metadata-format-rebuild-required";
            return false;
        }

        if (GetMeta(connection, "namespace_identity_format") != NamespaceIdentityFormat)
        {
            reason = "namespace-identity-rebuild-required";
            return false;
        }

        if (!ShortQueryIndex.IsCurrent(connection))
        {
            reason = "short-query-derived-state-rebuild-required";
            return false;
        }

        if (GetMeta(connection, "build_state") != "complete")
        {
            reason = "index-not-complete";
            return false;
        }

        if (!string.Equals(
                GetMeta(connection, "volume_identity"),
                volume.StableIdentity,
                StringComparison.OrdinalIgnoreCase))
        {
            reason = "volume-identity-mismatch";
            return false;
        }

        checkpoint = ReadCheckpoint(connection);
        if (checkpoint is null)
        {
            reason = "checkpoint-missing";
            return false;
        }

        return true;
    }

    private static bool TryValidateContinuity(IncrementalCheckpoint checkpoint, UsnJournalState journal, out string? reason)
    {
        if (checkpoint.JournalId != journal.JournalId)
        {
            reason = "journal-id-mismatch";
            return false;
        }

        if (checkpoint.NextUsn < journal.FirstUsn || checkpoint.NextUsn < journal.LowestValidUsn)
        {
            reason = "saved-usn-before-readable-range";
            return false;
        }

        reason = null;
        return true;
    }

    private static void CompleteBuild(SqliteConnection connection, IncrementalCheckpoint checkpoint)
    {
        ShortQueryIndex.Build(connection);
        using var transaction = connection.BeginTransaction();
        SetCheckpoint(connection, transaction, checkpoint);
        SetMeta(connection, transaction, "record_count", CountEntries(connection, transaction).ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "completed_utc", DateTimeOffset.UtcNow.ToString("O"));
        SetMeta(connection, transaction, "last_refreshed_utc", DateTimeOffset.UtcNow.ToString("O"));
        SetMeta(connection, transaction, "build_state", "complete");
        transaction.Commit();
    }

    private static void PersistCheckpoint(SqliteConnection connection, IncrementalCheckpoint checkpoint)
    {
        using var transaction = connection.BeginTransaction();
        SetCheckpoint(connection, transaction, checkpoint);
        transaction.Commit();
    }

    private static void PersistSuccessfulSync(SqliteConnection connection, IncrementalCheckpoint checkpoint)
    {
        using var transaction = connection.BeginTransaction();
        SetCheckpoint(connection, transaction, checkpoint);
        SetMeta(connection, transaction, "last_refreshed_utc", DateTimeOffset.UtcNow.ToString("O"));
        transaction.Commit();
    }

    private static void SetCheckpoint(SqliteConnection connection, SqliteTransaction transaction, IncrementalCheckpoint checkpoint)
    {
        SetMeta(connection, transaction, "journal_id", checkpoint.JournalId.ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "next_usn", checkpoint.NextUsn.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "journal_first_usn", checkpoint.FirstUsn.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMeta(connection, transaction, "journal_lowest_valid_usn", checkpoint.LowestValidUsn.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static IncrementalCheckpoint? ReadCheckpoint(SqliteConnection connection)
    {
        if (!ulong.TryParse(
                GetMeta(connection, "journal_id"),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var id) ||
            !long.TryParse(GetMeta(connection, "next_usn"), out var next) ||
            !long.TryParse(GetMeta(connection, "journal_first_usn"), out var first) ||
            !long.TryParse(GetMeta(connection, "journal_lowest_valid_usn"), out var lowest))
        {
            return null;
        }

        return new IncrementalCheckpoint(id, next, first, lowest);
    }

    private static void MarkRebuildRequired(SqliteConnection connection, string reason)
    {
        using var transaction = connection.BeginTransaction();
        SetMeta(connection, transaction, "build_state", "rebuild-required");
        SetMeta(connection, transaction, "rebuild_reason", reason);
        transaction.Commit();
    }

    private void PromoteStaging()
    {
        if (_journalLifecycle == IndexStoreJournalLifecycle.DeleteWhenQuiescent)
        {
            if (File.Exists(_databasePath))
            {
                using (var connection = Open(_databasePath))
                {
                    FinalizeJournal(connection);
                }
            }

            DeleteSqliteSidecars(_databasePath);
        }

        if (File.Exists(_databasePath))
        {
            var backup = _databasePath + ".previous";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }

            File.Replace(StagingPath, _databasePath, backup, true);
            File.Delete(backup);
        }
        else
        {
            File.Move(StagingPath, _databasePath);
        }
    }

    private SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString());
        try
        {
            connection.Open();
            if (_journalLifecycle == IndexStoreJournalLifecycle.DeleteWhenQuiescent)
            {
                ChangeJournalMode(connection, "wal");
            }
            else
            {
                using var journal = connection.CreateCommand();
                journal.CommandText = "PRAGMA journal_mode=WAL;";
                journal.ExecuteNonQuery();
            }

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void DeleteDatabaseAndSidecars(string path)
    {
        DeleteSqliteSidecars(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteSqliteSidecars(string path)
    {
        foreach (var sidecar in new[] { path + "-journal", path + "-wal", path + "-shm" })
        {
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }
    }

    private void FinalizeJournal(SqliteConnection connection)
    {
        if (_journalLifecycle != IndexStoreJournalLifecycle.DeleteWhenQuiescent)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var checkpointComplete = false;
                using var checkpoint = connection.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                using (var reader = checkpoint.ExecuteReader())
                {
                    checkpointComplete = reader.Read() && reader.GetInt32(0) == 0;
                }

                if (checkpointComplete)
                {
                    ChangeJournalMode(connection, "delete", stopwatch);
                    return;
                }
            }
            catch (SqliteException exception) when (IsBusy(exception))
            {
            }

            WaitForJournalTransition(stopwatch, "checkpoint the protected index");
        }
    }

    private static void ChangeJournalMode(
        SqliteConnection connection,
        string expectedMode,
        Stopwatch? existingStopwatch = null)
    {
        var stopwatch = existingStopwatch ?? Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA journal_mode={expectedMode};";
                var actualMode = Convert.ToString(
                    command.ExecuteScalar(),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (string.Equals(actualMode, expectedMode, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch (SqliteException exception) when (IsBusy(exception))
            {
            }

            WaitForJournalTransition(stopwatch, $"switch the protected index to {expectedMode} journal mode");
        }
    }

    private static bool IsBusy(SqliteException exception) => exception.SqliteErrorCode is 5 or 6;

    private static void WaitForJournalTransition(Stopwatch stopwatch, string operation)
    {
        if (stopwatch.ElapsedMilliseconds >= JournalTransitionTimeoutMilliseconds)
        {
            throw new InvalidOperationException($"Could not {operation} within the protected-storage timeout.");
        }

        Thread.Sleep(50);
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
        connection.Open();
        return connection;
    }

    private static void EnsureSearchable(SqliteConnection connection)
    {
        if (GetMeta(connection, "build_state") != "complete" ||
            GetMeta(connection, "schema_version") != SchemaVersion.ToString() ||
            GetMeta(connection, "namespace_identity_format") != NamespaceIdentityFormat ||
            GetMeta(connection, "search_index_format") != SearchIndexFormat ||
            GetMeta(connection, "metadata_format") != MetadataFormat ||
            !ShortQueryIndex.IsCurrent(connection) ||
            ReadCheckpoint(connection) is null)
        {
            throw new InvalidOperationException("Search requires a complete current schema-v4 metadata index.");
        }
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY NOT NULL,value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS namespace_entries(file_id BLOB PRIMARY KEY NOT NULL,parent_file_id BLOB NOT NULL,name TEXT NOT NULL,attributes INTEGER NOT NULL,usn INTEGER NOT NULL,record_version INTEGER NOT NULL,logical_size INTEGER NULL,last_write_time_utc INTEGER NULL);
            CREATE INDEX IF NOT EXISTS ix_namespace_parent_name ON namespace_entries(parent_file_id,name);
            CREATE VIRTUAL TABLE IF NOT EXISTS search_entries USING fts5(name, content='namespace_entries', content_rowid='rowid', tokenize='trigram case_sensitive 0');
            CREATE TRIGGER IF NOT EXISTS namespace_entries_search_insert AFTER INSERT ON namespace_entries BEGIN
                INSERT INTO search_entries(rowid,name) VALUES (new.rowid,new.name);
            END;
            CREATE TRIGGER IF NOT EXISTS namespace_entries_search_delete AFTER DELETE ON namespace_entries BEGIN
                INSERT INTO search_entries(search_entries,rowid,name) VALUES ('delete',old.rowid,old.name);
            END;
            CREATE TRIGGER IF NOT EXISTS namespace_entries_search_update AFTER UPDATE ON namespace_entries BEGIN
                INSERT INTO search_entries(search_entries,rowid,name) VALUES ('delete',old.rowid,old.name);
                INSERT INTO search_entries(rowid,name) VALUES (new.rowid,new.name);
            END;
            """;
        command.ExecuteNonQuery();
        ShortQueryIndex.CreateSchema(connection);
        SetMeta(connection, "schema_version", SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SetMeta(connection, "search_index_format", SearchIndexFormat);
        SetMeta(connection, "metadata_format", MetadataFormat);
    }

    private static void SetMeta(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO metadata(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static void SetMeta(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO metadata(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static string? GetMeta(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static (NativeFileId Parent, string Name)? ReadEntry(SqliteConnection connection, NativeFileId id)
    {
        return ReadEntryExact(connection, CanonicalizeLegacyId(id));
    }

    private static PathResolution ReconstructPath(SqliteConnection connection, NativeFileId fileId)
    {
        var mount = GetMeta(connection, "mount_point") ?? string.Empty;
        var parts = new List<string>();
        var seen = new HashSet<NativeFileId>();
        var current = CanonicalizeLegacyId(fileId);
        while (true)
        {
            if (!seen.Add(current))
            {
                return new PathResolution(false, null, "Cycle detected in parent relationships.");
            }

            var entry = ReadEntry(connection, current);
            if (entry is null)
            {
                return new PathResolution(false, null, $"Missing parent or record '{current}'.");
            }

            if (!string.IsNullOrEmpty(entry.Value.Name))
            {
                parts.Add(entry.Value.Name);
            }

            if (entry.Value.Parent.Equals(current))
            {
                break;
            }

            current = entry.Value.Parent;
        }
        parts.Reverse();
        return new PathResolution(true, Path.Combine(mount, Path.Combine(parts.ToArray())), null);
    }

    private static (NativeFileId Parent, string Name)? ReadEntryExact(SqliteConnection connection, NativeFileId id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT parent_file_id,name FROM namespace_entries WHERE file_id=$id";
        command.Parameters.Add("$id", SqliteType.Blob).Value = id.Bytes.ToArray();
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (new NativeFileId((byte[])reader[0]), reader.GetString(1))
            : null;
    }

    private static IndexStatus ReadStatus(string path)
    {
        try
        {
            using var connection = OpenReadOnly(path);
            var schema = GetMeta(connection, "schema_version");
            var state = GetMeta(connection, "build_state");
            var count = long.TryParse(GetMeta(connection, "record_count"), out var value) ? value : 0;
            DateTimeOffset? complete = DateTimeOffset.TryParse(
                GetMeta(connection, "completed_utc"),
                out var time) ? time : null;
            DateTimeOffset? refreshed = DateTimeOffset.TryParse(
                GetMeta(connection, "last_refreshed_utc"),
                out var refreshedTime) ? refreshedTime : null;
            var volumeIdentity = GetMeta(connection, "volume_identity");
            var mountPoint = GetMeta(connection, "mount_point");

            if (schema != SchemaVersion.ToString())
            {
                return new IndexStatus(
                    IndexState.RebuildRequired,
                    volumeIdentity,
                    mountPoint,
                    count,
                    complete,
                    null,
                    "Schema lacks authoritative file metadata; rebuild is required.", refreshed);
            }

            if (GetMeta(connection, "search_index_format") != SearchIndexFormat)
            {
                return new IndexStatus(
                    IndexState.RebuildRequired,
                    volumeIdentity,
                    mountPoint,
                    count,
                    complete,
                    null,
                    "Search index format requires a safe rebuild.", refreshed);
            }

            if (GetMeta(connection, "metadata_format") != MetadataFormat)
            {
                return new IndexStatus(
                    IndexState.RebuildRequired,
                    volumeIdentity,
                    mountPoint,
                    count,
                    complete,
                    null,
                    "Metadata format requires a safe rebuild.", refreshed);
            }

            if (GetMeta(connection, "namespace_identity_format") != NamespaceIdentityFormat)
            {
                return new IndexStatus(
                    IndexState.RebuildRequired,
                    volumeIdentity,
                    mountPoint,
                    count,
                    complete,
                    null,
                    "Namespace identity format requires a safe rebuild.", refreshed);
            }

            if (!ShortQueryIndex.IsCurrent(connection))
            {
                return new IndexStatus(
                    IndexState.RebuildRequired,
                    volumeIdentity,
                    mountPoint,
                    count,
                    complete,
                    null,
                    "Short-query derived state requires a safe rebuild.", refreshed);
            }

            var checkpoint = ReadCheckpoint(connection);
            if (state == "rebuild-required")
            {
                return new IndexStatus(
                    IndexState.RebuildRequired,
                    volumeIdentity,
                    mountPoint,
                    count,
                    complete,
                    checkpoint,
                    GetMeta(connection, "rebuild_reason") ?? "Rebuild is required.", refreshed);
            }

            if (state == "complete" && checkpoint is not null)
            {
                return new IndexStatus(
                    IndexState.Complete,
                    volumeIdentity,
                    mountPoint,
                    count,
                    complete,
                    checkpoint,
                    null,
                    refreshed);
            }

            return new IndexStatus(
                IndexState.Incomplete,
                volumeIdentity,
                mountPoint,
                count,
                complete,
                checkpoint,
                $"Build state is '{state ?? "unknown"}'.",
                refreshed);
        }
        catch (SqliteException exception)
        {
            return new IndexStatus(IndexState.Incomplete, null, null, 0, null, null, exception.Message);
        }
    }

    private readonly record struct ShortQueryEntry(NativeFileId ParentFileId, string Name, uint Attributes);
}
