namespace Quail.FileSystem;

internal sealed record MaintenanceOwnedChangeScope(
    NativeFileId CommonApplicationDataDirectoryId,
    NativeFileId RootDirectoryId,
    NativeFileId IndexesDirectoryId,
    NativeFileId LocksDirectoryId,
    string DatabaseFileName,
    string LockFileName);

internal sealed record MaintenanceJournalGapResult(
    bool CanWait,
    IncrementalCheckpoint WaitCheckpoint,
    long RecordsInspected,
    string? RebuildRequiredReason = null,
    string? UnavailableReason = null);

internal static class MaintenanceJournalGap
{
    internal static MaintenanceJournalGapResult Inspect(
        VolumeDescriptor volume,
        IncrementalCheckpoint appliedCheckpoint,
        string databasePath)
    {
        if (!IsOnVolume(PrivilegedIndexStorage.RootPath, volume.MountPoint))
        {
            return new MaintenanceJournalGapResult(true, appliedCheckpoint, 0);
        }

        try
        {
            var journal = NtfsJournal.Query(volume);
            if (!IndexStore.TryValidateContinuity(appliedCheckpoint, journal, out var reason))
            {
                return new MaintenanceJournalGapResult(false, appliedCheckpoint, 0, reason);
            }

            var scope = CreateScope(volume, databasePath);
            var onlyOwnedChanges = true;
            long recordsInspected = 0;
            var scanCheckpoint = appliedCheckpoint with
            {
                FirstUsn = journal.FirstUsn,
                LowestValidUsn = journal.LowestValidUsn
            };
            var cursor = NtfsJournal.Read(volume, scanCheckpoint, journal.NextUsn, batch =>
            {
                foreach (var record in batch.Records)
                {
                    recordsInspected++;
                    if (!IsOwnedChange(record, scope))
                    {
                        onlyOwnedChanges = false;
                    }
                }
            });

            return CreateResult(appliedCheckpoint, journal, cursor, recordsInspected, onlyOwnedChanges);
        }
        catch (Exception exception) when (IsClassifiableInspectionFailure(exception))
        {
            return CreateFailureResult(appliedCheckpoint, exception);
        }
    }

    internal static MaintenanceJournalGapResult CreateFailureResult(
        IncrementalCheckpoint appliedCheckpoint,
        Exception exception)
    {
        var failure = IndexStore.ClassifySyncFailure(SyncFailureStage.JournalRead, exception);
        return failure.RebuildRequired
            ? new MaintenanceJournalGapResult(false, appliedCheckpoint, 0, failure.Reason)
            : new MaintenanceJournalGapResult(false, appliedCheckpoint, 0, UnavailableReason: failure.Reason);
    }

    internal static MaintenanceJournalGapResult CreateResult(
        IncrementalCheckpoint appliedCheckpoint,
        UsnJournalState journal,
        long cursor,
        long recordsInspected,
        bool onlyOwnedChanges)
    {
        if (!IndexStore.TryValidateContinuity(appliedCheckpoint, journal, out var reason))
        {
            return new MaintenanceJournalGapResult(false, appliedCheckpoint, recordsInspected, reason);
        }

        if (cursor != journal.NextUsn)
        {
            return new MaintenanceJournalGapResult(
                false,
                appliedCheckpoint,
                recordsInspected,
                "journal-read-or-parse-failed");
        }

        var waitCheckpoint = appliedCheckpoint with
        {
            NextUsn = cursor,
            FirstUsn = journal.FirstUsn,
            LowestValidUsn = journal.LowestValidUsn
        };
        return new MaintenanceJournalGapResult(onlyOwnedChanges, waitCheckpoint, recordsInspected);
    }

    internal static bool IsOwnedChange(JournalRecord record, MaintenanceOwnedChangeScope scope)
    {
        var name = record.NamespaceRecord.Name;
        var parent = Canonicalize(record.NamespaceRecord.ParentFileId);

        if (parent.Equals(scope.IndexesDirectoryId))
        {
            return IsDatabaseArtifact(name, scope.DatabaseFileName);
        }

        if (parent.Equals(scope.LocksDirectoryId))
        {
            return string.Equals(name, scope.LockFileName, StringComparison.OrdinalIgnoreCase);
        }

        if (parent.Equals(scope.RootDirectoryId))
        {
            return IsHealthArtifact(name) ||
                   string.Equals(name, "Indexes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "Locks", StringComparison.OrdinalIgnoreCase);
        }

        return parent.Equals(scope.CommonApplicationDataDirectoryId) &&
               string.Equals(name, "Quail", StringComparison.OrdinalIgnoreCase);
    }

    private static MaintenanceOwnedChangeScope CreateScope(VolumeDescriptor volume, string databasePath)
    {
        var canonicalDatabasePath = Path.GetFullPath(databasePath);
        var expectedDatabasePath = Path.GetFullPath(ManagedIndexPath.ForVolumeIdentity(volume.StableIdentity));
        if (!string.Equals(canonicalDatabasePath, expectedDatabasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Protected index path does not match the maintenance volume identity.");
        }

        var locksPath = Path.Combine(PrivilegedIndexStorage.RootPath, "Locks");
        return new MaintenanceOwnedChangeScope(
            NtfsVolume.GetPathFileId(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
            NtfsVolume.GetPathFileId(PrivilegedIndexStorage.RootPath),
            NtfsVolume.GetPathFileId(PrivilegedIndexStorage.IndexesPath),
            NtfsVolume.GetPathFileId(locksPath),
            Path.GetFileName(canonicalDatabasePath),
            $"{ManagedIndexPath.SafeVolumeName(volume.StableIdentity)}.lock");
    }

    internal static bool IsOnVolume(string path, string mountPoint) =>
        string.Equals(
            Path.GetPathRoot(Path.GetFullPath(path)),
            Path.GetPathRoot(Path.GetFullPath(mountPoint)),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsDatabaseArtifact(string name, string databaseFileName)
    {
        if (string.Equals(name, databaseFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var suffix in new[]
        {
            "-journal",
            "-wal",
            "-shm",
            ".building",
            ".building-journal",
            ".building-wal",
            ".building-shm",
            ".previous"
        })
        {
            if (string.Equals(name, databaseFileName + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHealthArtifact(string name)
    {
        const string healthFileName = "maintenance-health.json";
        if (string.Equals(name, healthFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (name.StartsWith($".{healthFileName}.", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) ||
               (name.StartsWith($"{healthFileName}~RF", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".TMP", StringComparison.OrdinalIgnoreCase));
    }

    private static NativeFileId Canonicalize(NativeFileId fileId)
    {
        if (fileId.Bytes.Length != 8)
        {
            return fileId;
        }

        Span<byte> extended = stackalloc byte[16];
        fileId.Bytes.Span.CopyTo(extended);
        return new NativeFileId(extended);
    }

    private static bool IsClassifiableInspectionFailure(Exception exception) =>
        exception is System.ComponentModel.Win32Exception or
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            NotSupportedException or
            InvalidOperationException;
}
