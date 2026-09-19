using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Quail.FileSystem;

namespace Quail.Core.Tests;

public sealed class M20MaintenanceBoundaryTests : IDisposable
{
    private const string Volume = @"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"quail-m20-{Guid.NewGuid():N}");

    [Fact]
    public void Continuity_rejects_wrong_journal_below_range_and_future_checkpoint()
    {
        Assert.False(IndexStore.TryValidateContinuity(new(2, 100, 10, 10), new(1, 10, 200, 10, 2, 3), out var idReason));
        Assert.Equal("journal-id-mismatch", idReason);
        Assert.False(IndexStore.TryValidateContinuity(new(1, 9, 0, 0), new(1, 10, 200, 10, 2, 3), out var lowerReason));
        Assert.Equal("saved-usn-before-readable-range", lowerReason);
        Assert.False(IndexStore.TryValidateContinuity(new(1, 201, 10, 10), new(1, 10, 200, 10, 2, 3), out var upperReason));
        Assert.Equal("saved-usn-after-journal-frontier", upperReason);
        Assert.False(IndexStore.TryValidateContinuity(new(1, 9, 10, 8), new(1, 0, 200, 0, 2, 3), out var checkpointReason));
        Assert.Equal("checkpoint-lower-bounds-inconsistent", checkpointReason);
    }

    [Fact]
    public void Protected_index_rejects_non_service_writer_before_touching_disk()
    {
        var store = new IndexStore(ManagedIndexPath.ForVolumeIdentity(Volume));
        Assert.Throws<UnauthorizedAccessException>(() => store.BuildFromRecords(
            new VolumeDescriptor(Volume, "Q:\\", "NTFS", "Test"),
            _ => { }));
    }

    [Fact]
    public void Maintenance_state_round_trips_atomically_and_rejects_invalid_target()
    {
        Directory.CreateDirectory(_directory);
        var store = new MaintenanceStateStore(
            Path.Combine(_directory, "targets.json"),
            Path.Combine(_directory, "health.json"));
        var targets = new MaintenanceTargetsDocument(1, 7, [new(Volume, "Q:\\")]);
        store.SaveTargets(targets);
        var loaded = store.LoadTargets();
        Assert.Equal(targets.Version, loaded.Version);
        Assert.Equal(targets.Generation, loaded.Generation);
        Assert.Equal(targets.Targets, loaded.Targets);
        Assert.Throws<InvalidDataException>(() => store.SaveTargets(new(1, 8, [new("C:\\", null)])));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Maintenance_state_reader_allows_atomic_replacement_and_keeps_its_snapshot()
    {
        Directory.CreateDirectory(_directory);
        var targetsPath = Path.Combine(_directory, "targets.json");
        var store = new MaintenanceStateStore(
            targetsPath,
            Path.Combine(_directory, "health.json"));
        store.SaveTargets(new MaintenanceTargetsDocument(1, 1, [new(Volume, "Q:\\")]));

        using var oldSnapshot = MaintenanceStateStore.OpenSnapshotForRead(targetsPath);
        store.SaveTargets(new MaintenanceTargetsDocument(1, 2, [new(Volume, "R:\\")]));

        var oldDocument = JsonSerializer.Deserialize<MaintenanceTargetsDocument>(oldSnapshot);
        Assert.NotNull(oldDocument);
        Assert.Equal(1, oldDocument.Generation);
        Assert.Equal("Q:\\", oldDocument.Targets.Single().LastKnownMountPoint);

        var currentDocument = store.LoadTargets();
        Assert.Equal(2, currentDocument.Generation);
        Assert.Equal("R:\\", currentDocument.Targets.Single().LastKnownMountPoint);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void Maintenance_registration_query_reads_only_machine_targets()
    {
        Directory.CreateDirectory(_directory);
        var store = new MaintenanceStateStore(
            Path.Combine(_directory, "targets.json"),
            Path.Combine(_directory, "health.json"));

        Assert.False(store.IsRegistered(Volume));
        store.SaveTargets(new MaintenanceTargetsDocument(1, 1, [new(Volume, "Q:\\")]));

        Assert.True(store.IsRegistered(Volume));
        Assert.False(store.IsRegistered(@"\\?\Volume{11111111-1111-1111-1111-111111111111}"));
    }

    [Fact]
    public async Task Control_framing_rejects_oversized_duplicate_and_stale_requests()
    {
        var stale = new MaintenanceControlRequest(1, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1), MaintenanceControlCommand.Rebuild, Volume, null);
        Assert.Equal("stale-request", MaintenanceControlValidation.Validate(stale, DateTimeOffset.UtcNow));
        var arbitraryPath = stale with { RequestId = Guid.NewGuid(), IssuedUtc = DateTimeOffset.UtcNow, VolumeIdentity = @"C:\attacker.db" };
        Assert.Equal("invalid-volume-identity", MaintenanceControlValidation.Validate(arbitraryPath, DateTimeOffset.UtcNow));

        var replayGuard = new MaintenanceRequestReplayGuard();
        var requestId = Guid.NewGuid();
        Assert.True(replayGuard.TryAccept(requestId, DateTimeOffset.UtcNow));
        Assert.False(replayGuard.TryAccept(requestId, DateTimeOffset.UtcNow));

        await using var oversized = new MemoryStream();
        await oversized.WriteAsync(BitConverter.GetBytes(MaintenanceControlValidation.MaximumFrameBytes + 1));
        oversized.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => MaintenanceControlFraming.ReadRequestAsync(oversized, CancellationToken.None));

        var duplicateJson = Encoding.UTF8.GetBytes("{\"Version\":1,\"Version\":1}");
        await using var duplicate = new MemoryStream();
        await duplicate.WriteAsync(BitConverter.GetBytes(duplicateJson.Length));
        await duplicate.WriteAsync(duplicateJson);
        duplicate.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => MaintenanceControlFraming.ReadRequestAsync(duplicate, CancellationToken.None));
    }

    [Fact]
    public void Journal_failure_classification_preserves_transient_and_fails_closed_on_proven_loss()
    {
        Assert.True(IndexStore.IsTransientJournalFailure(new Win32Exception(1178)));
        Assert.False(IndexStore.IsTransientJournalFailure(new Win32Exception(1179)));
        Assert.False(IndexStore.IsTransientJournalFailure(new Win32Exception(1181)));
    }

    [Fact]
    public void Sync_failure_classification_separates_transaction_rollback_from_journal_loss()
    {
        var apply = IndexStore.ClassifySyncFailure(
            SyncFailureStage.BatchApplication,
            new InvalidOperationException());
        Assert.False(apply.RebuildRequired);
        Assert.True(apply.Unavailable);
        Assert.Equal("index-update-failed", apply.Reason);

        var derived = IndexStore.ClassifySyncFailure(
            SyncFailureStage.BatchApplication,
            new InvalidOperationException("Short-query rank label gap is exhausted; rebuild is required."));
        Assert.True(derived.RebuildRequired);
        Assert.False(derived.Unavailable);
        Assert.Equal("derived-state-update-failed", derived.Reason);

        var unavailable = IndexStore.ClassifySyncFailure(
            SyncFailureStage.JournalRead,
            new Win32Exception(1178));
        Assert.False(unavailable.RebuildRequired);
        Assert.True(unavailable.Unavailable);

        var invalid = IndexStore.ClassifySyncFailure(
            SyncFailureStage.JournalRead,
            new InvalidDataException());
        Assert.True(invalid.RebuildRequired);
        Assert.False(invalid.Unavailable);
    }

    [Fact]
    public void Same_volume_owned_maintenance_gap_advances_only_the_in_memory_wait_frontier()
    {
        var applied = new IncrementalCheckpoint(1, 100, 10, 5);
        var journal = new UsnJournalState(1, 10, 150, 5, 2, 3);

        var result = MaintenanceJournalGap.CreateResult(
            applied,
            journal,
            cursor: 180,
            recordsInspected: 12,
            onlyOwnedChanges: true);

        Assert.True(result.CanWait);
        Assert.Equal(180, result.WaitCheckpoint.NextUsn);
        Assert.Equal(100, applied.NextUsn);
        Assert.Equal(12, result.RecordsInspected);
        Assert.Null(result.RebuildRequiredReason);
    }

    [Fact]
    public void External_change_in_sync_to_wait_gap_requires_another_authoritative_sync()
    {
        var applied = new IncrementalCheckpoint(1, 100, 10, 5);
        var journal = new UsnJournalState(1, 10, 150, 5, 2, 3);

        var result = MaintenanceJournalGap.CreateResult(
            applied,
            journal,
            cursor: 180,
            recordsInspected: 2,
            onlyOwnedChanges: false);

        Assert.False(result.CanWait);
        Assert.Null(result.RebuildRequiredReason);
    }

    [Fact]
    public void Wait_gap_continuity_loss_preserves_rebuild_required_semantics()
    {
        var applied = new IncrementalCheckpoint(1, 100, 10, 5);
        var changedJournal = new UsnJournalState(2, 10, 150, 5, 2, 3);

        var result = MaintenanceJournalGap.CreateResult(
            applied,
            changedJournal,
            cursor: 150,
            recordsInspected: 0,
            onlyOwnedChanges: true);

        Assert.False(result.CanWait);
        Assert.Equal("journal-id-mismatch", result.RebuildRequiredReason);
        Assert.Equal(applied, result.WaitCheckpoint);
    }

    [Fact]
    public void Transient_wait_gap_failure_retries_without_advancing_the_durable_checkpoint()
    {
        var applied = new IncrementalCheckpoint(1, 100, 10, 5);

        var result = MaintenanceJournalGap.CreateFailureResult(applied, new IOException("device unavailable"));

        Assert.False(result.CanWait);
        Assert.Null(result.RebuildRequiredReason);
        Assert.Equal("journal-read-unavailable", result.UnavailableReason);
        Assert.Equal(applied, result.WaitCheckpoint);
    }

    [Theory]
    [InlineData(1179)]
    [InlineData(1181)]
    public void Continuity_wait_gap_failure_remains_fail_closed(int nativeErrorCode)
    {
        var applied = new IncrementalCheckpoint(1, 100, 10, 5);

        var result = MaintenanceJournalGap.CreateFailureResult(
            applied,
            new System.ComponentModel.Win32Exception(nativeErrorCode));

        Assert.False(result.CanWait);
        Assert.Equal("journal-read-or-parse-failed", result.RebuildRequiredReason);
        Assert.Null(result.UnavailableReason);
        Assert.Equal(applied, result.WaitCheckpoint);
    }

    [Theory]
    [MemberData(nameof(NonTransientGapFailures))]
    public void Invalid_or_unsupported_wait_gap_is_rebuild_required(Exception failure)
    {
        var applied = new IncrementalCheckpoint(1, 100, 10, 5);

        var result = MaintenanceJournalGap.CreateFailureResult(applied, failure);

        Assert.False(result.CanWait);
        Assert.Equal("journal-read-or-parse-failed", result.RebuildRequiredReason);
        Assert.Null(result.UnavailableReason);
        Assert.Equal(applied, result.WaitCheckpoint);
    }

    [Fact]
    public void Owned_change_classifier_is_narrow_to_exact_protected_artifacts()
    {
        var common = Id(1);
        var root = Id(2);
        var legacyIndexesBytes = new byte[16];
        Array.Fill(legacyIndexesBytes, (byte)3, 0, 8);
        var indexes = new NativeFileId(legacyIndexesBytes);
        var locks = Id(4);
        var scope = new MaintenanceOwnedChangeScope(
            common,
            root,
            indexes,
            locks,
            "volume-test.db",
            "volume-test.lock");

        Assert.True(MaintenanceJournalGap.IsOwnedChange(Record(indexes, "volume-test.db-wal"), scope));
        Assert.True(MaintenanceJournalGap.IsOwnedChange(Record(indexes, "volume-test.db-journal"), scope));
        Assert.True(MaintenanceJournalGap.IsOwnedChange(Record(root, "maintenance-health.json"), scope));
        Assert.True(MaintenanceJournalGap.IsOwnedChange(Record(root, ".maintenance-health.json.0123456789abcdef0123456789abcdef.tmp"), scope));
        Assert.True(MaintenanceJournalGap.IsOwnedChange(Record(root, "maintenance-health.json~RF1234.TMP"), scope));
        Assert.True(MaintenanceJournalGap.IsOwnedChange(Record(locks, "volume-test.lock"), scope));
        Assert.True(MaintenanceJournalGap.IsOwnedChange(Record(common, "Quail"), scope));
        Assert.True(MaintenanceJournalGap.IsOwnedChange(
            Record(new NativeFileId(indexes.Bytes.Span[..8]), "volume-test.db-shm"),
            scope));

        Assert.False(MaintenanceJournalGap.IsOwnedChange(Record(indexes, "unrelated.db-wal"), scope));
        Assert.False(MaintenanceJournalGap.IsOwnedChange(Record(root, "maintenance-targets.json"), scope));
        Assert.False(MaintenanceJournalGap.IsOwnedChange(Record(Id(9), "volume-test.db-wal"), scope));
    }

    [Fact]
    public void Separate_volume_storage_does_not_need_same_volume_gap_inspection()
    {
        Assert.True(MaintenanceJournalGap.IsOnVolume(@"C:\ProgramData\Quail", @"C:\"));
        Assert.False(MaintenanceJournalGap.IsOnVolume(@"C:\ProgramData\Quail", @"D:\"));
    }

    [Fact]
    public async Task Native_pipe_acl_rejects_a_non_elevated_client_before_framing()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = new MaintenanceControlServer((request, _) => Task.FromResult(new MaintenanceControlResponse(
            MaintenanceControlResponse.CurrentVersion,
            request.RequestId,
            MaintenanceControlStatus.Accepted,
            Guid.NewGuid(),
            null)));
        var serverTask = Task.Run(() => server.RunAsync(cancellation.Token));
        var request = new MaintenanceControlRequest(
            MaintenanceControlRequest.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            MaintenanceControlCommand.RegisterAndBuild,
            Volume,
            null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new MaintenanceControlClient().SendAsync(request, cancellation.Token));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverTask);
    }

    [Fact]
    public void Control_authorization_allows_system_and_only_local_administrators()
    {
        Assert.True(MaintenancePipeAuthorization.AuthorizeMembership(true, true, true).Authorized);
        Assert.False(MaintenancePipeAuthorization.AuthorizeMembership(false, true, true).Authorized);
        Assert.True(MaintenancePipeAuthorization.AuthorizeMembership(false, false, true).Authorized);
        Assert.False(MaintenancePipeAuthorization.AuthorizeMembership(false, false, false).Authorized);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static NativeFileId Id(byte value) => new(Enumerable.Repeat(value, 16).ToArray());

    private static JournalRecord Record(NativeFileId parent, string name) => new(
        new NamespaceRecord(Id(8), parent, name, 0, 0, 3),
        UsnReason.Close);

    public static TheoryData<Exception> NonTransientGapFailures => new()
    {
        new InvalidDataException("malformed record"),
        new NotSupportedException("unsupported record version")
    };
}
