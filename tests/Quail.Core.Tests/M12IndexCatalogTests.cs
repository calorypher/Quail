using Quail.App;
using Microsoft.Data.Sqlite;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class M12IndexCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "quail-m12-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Load_defaults_when_catalog_is_absent()
    {
        var result = await new IndexCatalogStore(Path.Combine(_directory, "indexes.json")).LoadAsync();
        Assert.True(result.IsValid);
        Assert.Empty(result.Catalog.Entries);
    }

    [Fact]
    public async Task Save_round_trips_versioned_entries_atomically()
    {
        var path = Path.Combine(_directory, "indexes.json");
        var store = new IndexCatalogStore(path);
        var expected = new IndexCatalogDocument(1, [new("volume-a", "D:\\", ManagedIndexPath.ForVolumeIdentity("volume-a"), true)]);
        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();
        Assert.True(actual.IsValid);
        Assert.Equal(expected.Version, actual.Catalog.Version);
        Assert.Equal(expected.Entries, actual.Catalog.Entries);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Malformed_catalog_is_fail_safe()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "indexes.json"), "{ malformed");
        var result = await new IndexCatalogStore(Path.Combine(_directory, "indexes.json")).LoadAsync();
        Assert.False(result.IsValid);
        Assert.Empty(result.Catalog.Entries);
    }

    [Fact]
    public async Task Duplicate_volume_identity_is_rejected()
    {
        var store = new IndexCatalogStore(Path.Combine(_directory, "indexes.json"));
        var duplicate = new IndexCatalogDocument(1, [new("volume-a", "D:\\", ManagedIndexPath.ForVolumeIdentity("volume-a"), false), new("VOLUME-A", "E:\\", ManagedIndexPath.ForVolumeIdentity("VOLUME-A"), false)]);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(duplicate));
    }

    [Fact]
    public async Task Arbitrary_catalog_database_path_is_rejected()
    {
        var store = new IndexCatalogStore(Path.Combine(_directory, "indexes.json"));
        var catalog = new IndexCatalogDocument(1, [new("volume-a", "D:\\", "C:\\Windows\\index.db", false)]);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(catalog));
    }

    [Fact]
    public void Managed_database_path_is_safe_and_deterministic()
    {
        var first = ManagedIndexPath.ForVolumeIdentity("\\\\?\\Volume{not-a-drive-letter}");
        Assert.Equal(first, ManagedIndexPath.ForVolumeIdentity("\\\\?\\Volume{not-a-drive-letter}"));
        Assert.Equal(first, ManagedIndexPath.ForVolumeIdentity("\\\\?\\VOLUME{NOT-A-DRIVE-LETTER}\\"));
        Assert.EndsWith(".db", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{", Path.GetFileName(first));
        Assert.DoesNotContain("C.db", Path.GetFileName(first), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(PrivilegedIndexStorage.IndexesPath, first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_development_path_is_normalized_without_adopting_the_database()
    {
        var path = Path.Combine(_directory, "indexes.json");
        Directory.CreateDirectory(_directory);
        var legacy = new IndexCatalogDocument(1, [new("volume-a", "D:\\", ManagedIndexPath.LegacyForVolumeIdentity("volume-a"), true)]);
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(legacy));

        var loaded = await new IndexCatalogStore(path).LoadAsync();

        Assert.True(loaded.IsValid);
        Assert.Equal(ManagedIndexPath.ForVolumeIdentity("volume-a"), loaded.Catalog.Entries.Single().DatabasePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

public sealed class AdminIndexWorkerTests
{
    [Theory]
    [InlineData("Build")]
    [InlineData("Refresh")]
    [InlineData("Rebuild")]
    public void Internal_worker_parses_only_narrow_supported_operations(string operation)
    {
        var id = Guid.NewGuid();
        var parsed = AdminIndexWorker.TryParse(["--internal-index-operation", operation, "--internal-operation-id", id.ToString(), "--internal-mount-point", "D:\\", "--internal-volume-identity", "volume-a"], out var request, out var error);
        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal(id, request!.Id);
    }

    [Fact]
    public void Internal_worker_rejects_bad_guid_unknown_operation_and_arbitrary_path_argument()
    {
        Assert.True(AdminIndexWorker.TryParse(["--internal-index-operation", "delete", "--internal-operation-id", Guid.NewGuid().ToString(), "--internal-mount-point", "D:\\", "--internal-volume-identity", "volume-a"], out _, out var operationError));
        Assert.NotNull(operationError);
        Assert.True(AdminIndexWorker.TryParse(["--internal-index-operation", "Build", "--internal-operation-id", "no", "--internal-mount-point", "D:\\", "--internal-volume-identity", "volume-a"], out _, out var guidError));
        Assert.NotNull(guidError);
        Assert.True(AdminIndexWorker.TryParse(["--internal-index-operation", "Build", "--internal-operation-id", Guid.NewGuid().ToString(), "--internal-mount-point", "D:\\", "--internal-volume-identity", "volume-a", "--index", "C:\\Windows\\x.db"], out _, out var pathError));
        Assert.NotNull(pathError);
    }

    [Fact]
    public void Elevated_worker_start_info_keeps_drive_root_as_one_argument()
    {
        var id = Guid.NewGuid();
        var entry = new IndexCatalogEntry("volume-a", "C:\\", ManagedIndexPath.ForVolumeIdentity("volume-a"), true);

        var startInfo = ElevatedIndexOperationRunner.CreateProcessStartInfo("Quail.exe", AdminIndexOperation.Build, id, entry);

        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("runas", startInfo.Verb);
        Assert.True(string.IsNullOrEmpty(startInfo.Arguments));
        Assert.Equal(
            ["--internal-index-operation", "Build", "--internal-operation-id", id.ToString("D"), "--internal-mount-point", "C:\\", "--internal-volume-identity", "volume-a"],
            startInfo.ArgumentList);
        Assert.DoesNotContain(startInfo.ArgumentList, argument => argument.Contains("AdminOperations", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class M12TransactionalCatalogTests
{
    private static readonly VolumeDescriptor VolumeA = new("volume-a", "D:\\", "NTFS", "A");
    private static readonly VolumeDescriptor VolumeB = new("volume-b", "E:\\", "NTFS", "B");

    [Fact]
    public async Task Add_failure_leaves_disk_entries_and_active_paths_committed()
    {
        var store = new FaultInjectingCatalogStore(IndexCatalogDocument.Empty) { FailNextSave = true };
        var controller = await LoadAsync(store);

        await Assert.ThrowsAsync<IOException>(() => controller.AddAsync(VolumeA));

        Assert.Empty(store.Persisted.Entries);
        Assert.Empty(controller.Entries);
        Assert.Empty(controller.ActivePaths);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disable_or_remove_failure_preserves_previous_generation(bool disable)
    {
        var entry = Entry(VolumeA, enabled: true);
        var store = new FaultInjectingCatalogStore(new(1, [entry]));
        var controller = await LoadAsync(store);
        store.FailNextSave = true;

        if (disable)
            await Assert.ThrowsAsync<IOException>(() => controller.SetEnabledAsync(VolumeA.StableIdentity, false));
        else
            await Assert.ThrowsAsync<IOException>(() => controller.RemoveAsync(VolumeA.StableIdentity));

        Assert.Equal([entry], store.Persisted.Entries);
        Assert.Equal([entry], controller.Entries);
        Assert.Equal([entry.DatabasePath], controller.ActivePaths);
    }

    [Fact]
    public async Task Retry_after_failed_add_does_not_persist_a_hidden_candidate()
    {
        var store = new FaultInjectingCatalogStore(IndexCatalogDocument.Empty) { FailNextSave = true };
        var controller = await LoadAsync(store);
        await Assert.ThrowsAsync<IOException>(() => controller.AddAsync(VolumeA));

        await controller.AddAsync(VolumeB);

        Assert.Equal([VolumeB.StableIdentity], store.Persisted.Entries.Select(entry => entry.VolumeIdentity));
        Assert.Equal([VolumeB.StableIdentity], controller.Entries.Select(entry => entry.VolumeIdentity));
    }

    [Fact]
    public async Task Overlapping_mutations_are_serialized_without_lost_updates()
    {
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FaultInjectingCatalogStore(IndexCatalogDocument.Empty)
        {
            BeforeSave = async saveNumber =>
            {
                if (saveNumber == 1)
                {
                    firstSaveStarted.SetResult();
                    await releaseFirstSave.Task;
                }
            }
        };
        var controller = await LoadAsync(store);

        var first = controller.AddAsync(VolumeA);
        await firstSaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = controller.AddAsync(VolumeB);
        releaseFirstSave.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal([VolumeA.StableIdentity, VolumeB.StableIdentity], store.Persisted.Entries.Select(entry => entry.VolumeIdentity));
        Assert.Equal(store.Persisted.Entries, controller.Entries);
    }

    internal static async Task<IndexCatalogController> LoadAsync(FaultInjectingCatalogStore store, Func<string, IndexStatus>? status = null)
    {
        var controller = new IndexCatalogController(
            store,
            mount => mount.StartsWith("D", StringComparison.OrdinalIgnoreCase) ? VolumeA : VolumeB,
            status ?? (path => Complete(string.Equals(path, ManagedIndexPath.ForVolumeIdentity(VolumeA.StableIdentity), StringComparison.OrdinalIgnoreCase) ? VolumeA.StableIdentity : VolumeB.StableIdentity)));
        await controller.LoadAsync();
        return controller;
    }

    internal static IndexCatalogEntry Entry(VolumeDescriptor volume, bool enabled) =>
        new(volume.StableIdentity, volume.MountPoint, ManagedIndexPath.ForVolumeIdentity(volume.StableIdentity), enabled);

    internal static IndexStatus Complete(string identity) => new(IndexState.Complete, identity, "D:\\", 1, DateTimeOffset.UtcNow, null, null);
}

internal sealed class FaultInjectingCatalogStore : IIndexCatalogStore
{
    private int _saveCount;

    public FaultInjectingCatalogStore(IndexCatalogDocument persisted) => Persisted = persisted;

    public IndexCatalogDocument Persisted { get; private set; }
    public bool FailNextSave { get; set; }
    public Func<int, Task>? BeforeSave { get; init; }

    public Task<IndexCatalogLoadResult> LoadAsync() => Task.FromResult(new IndexCatalogLoadResult(Persisted, null));

    public async Task SaveAsync(IndexCatalogDocument catalog)
    {
        var saveNumber = Interlocked.Increment(ref _saveCount);
        if (BeforeSave is not null) await BeforeSave(saveNumber);
        if (FailNextSave)
        {
            FailNextSave = false;
            throw new IOException("Injected catalog persistence failure.");
        }
        Persisted = catalog;
    }
}

public sealed class M12OperationCoordinationTests
{
    private static readonly VolumeDescriptor Volume = new("volume-operation", "D:\\", "NTFS", "Operation");

    [Fact]
    public async Task Running_operation_survives_window_independent_owner_and_rejects_second_start()
    {
        var release = new TaskCompletionSource<AdminOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = await ControllerAsync(enabled: false);
        var coordinator = new IndexOperationCoordinator(controller, (_, _) => release.Task);
        var entry = controller.Entries.Single();

        var running = coordinator.StartAsync(AdminIndexOperation.Build, entry);

        Assert.True(coordinator.HasRunningOperations);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync(AdminIndexOperation.Rebuild, entry));
        release.SetResult(Success(AdminIndexOperation.Build));
        await running;
        Assert.False(coordinator.HasRunningOperations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rebuild_preserves_explicit_enable_state(bool enabled)
    {
        var controller = await ControllerAsync(enabled);
        var coordinator = new IndexOperationCoordinator(controller, (operation, _) => Task.FromResult(Success(operation)));

        await coordinator.StartAsync(AdminIndexOperation.Rebuild, controller.Entries.Single());

        Assert.Equal(enabled, controller.Entries.Single().EnabledForSearch);
    }

    [Fact]
    public async Task Initial_build_auto_enables_only_when_entry_revision_is_unchanged()
    {
        var controller = await ControllerAsync(enabled: false);
        var coordinator = new IndexOperationCoordinator(controller, (operation, _) => Task.FromResult(Success(operation)));

        await coordinator.StartAsync(AdminIndexOperation.Build, controller.Entries.Single());

        Assert.True(controller.Entries.Single().EnabledForSearch);
    }

    [Fact]
    public async Task Explicit_disable_during_build_is_not_overwritten_by_completion()
    {
        var release = new TaskCompletionSource<AdminOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = await ControllerAsync(enabled: true);
        var coordinator = new IndexOperationCoordinator(controller, (_, _) => release.Task);
        var running = coordinator.StartAsync(AdminIndexOperation.Build, controller.Entries.Single());

        await controller.SetEnabledAsync(Volume.StableIdentity, false);
        release.SetResult(Success(AdminIndexOperation.Build));
        await running;

        Assert.False(controller.Entries.Single().EnabledForSearch);
    }

    private static async Task<IndexCatalogController> ControllerAsync(bool enabled)
    {
        var store = new FaultInjectingCatalogStore(new(1, [M12TransactionalCatalogTests.Entry(Volume, enabled)]));
        return await M12TransactionalCatalogTests.LoadAsync(store, _ => M12TransactionalCatalogTests.Complete(Volume.StableIdentity));
    }

    private static AdminOperationResult Success(AdminIndexOperation operation) =>
        new(Guid.NewGuid(), operation.ToString(), true, false, 1, null, 1, null, "Complete");
}

public sealed class M12DynamicSourceGenerationTests
{
    [Fact]
    public async Task Disable_invalidates_inflight_search_before_completion()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var completed = new SemaphoreSlim(0);
        SearchCompletion? observed = null;
        var entry = M12TransactionalCatalogTests.Entry(new("volume-a", "D:\\", "NTFS", "A"), enabled: true);
        var store = new FaultInjectingCatalogStore(new(1, [entry]));
        var controller = await M12TransactionalCatalogTests.LoadAsync(store, _ => M12TransactionalCatalogTests.Complete("volume-a"));
        using var coordinator = new LatestSearchCoordinator(_ =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return [];
        });
        controller.ActivePathsChanged += coordinator.Invalidate;
        coordinator.Completed += completion => { observed = completion; completed.Release(); };

        coordinator.Request("query");
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        await controller.SetEnabledAsync("volume-a", false);
        release.Set();
        await completed.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(observed);
        Assert.False(observed!.IsCurrent);
        Assert.Empty(controller.ActivePaths);
    }

    [Fact]
    public async Task Remove_and_one_to_zero_raise_source_change()
    {
        var entry = M12TransactionalCatalogTests.Entry(new("volume-a", "D:\\", "NTFS", "A"), enabled: true);
        var store = new FaultInjectingCatalogStore(new(1, [entry]));
        var controller = await M12TransactionalCatalogTests.LoadAsync(store, _ => M12TransactionalCatalogTests.Complete("volume-a"));
        var changes = 0;
        controller.ActivePathsChanged += () => changes++;

        await controller.RemoveAsync("volume-a");

        Assert.Equal(1, changes);
        Assert.Empty(controller.ActivePaths);
    }

    [Fact]
    public async Task One_to_two_and_complete_to_rebuild_required_raise_source_change()
    {
        var volumeA = new VolumeDescriptor("volume-a", "D:\\", "NTFS", "A");
        var volumeB = new VolumeDescriptor("volume-b", "E:\\", "NTFS", "B");
        var stateB = IndexState.Complete;
        var store = new FaultInjectingCatalogStore(new(1,
        [
            M12TransactionalCatalogTests.Entry(volumeA, enabled: true),
            M12TransactionalCatalogTests.Entry(volumeB, enabled: false)
        ]));
        var controller = await M12TransactionalCatalogTests.LoadAsync(store, path =>
            path == ManagedIndexPath.ForVolumeIdentity("volume-b")
                ? M12TransactionalCatalogTests.Complete("volume-b") with { State = stateB }
                : M12TransactionalCatalogTests.Complete("volume-a"));
        var snapshots = new List<string[]>();
        controller.ActivePathsChanged += () => snapshots.Add(controller.ActivePaths.ToArray());

        await controller.SetEnabledAsync("volume-b", true);
        stateB = IndexState.RebuildRequired;
        controller.ReevaluateActivePaths();

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(2, snapshots[0].Length);
        Assert.Single(snapshots[1]);
    }
}

public sealed class M12SettingsHotkeyRestoreGuardTests
{
    [Fact]
    public void Manage_indexes_restore_failure_keeps_navigation_blocked_with_error()
    {
        var restored = SettingsHotkeyRestoreGuard.TryRestore(() => false, out var error);

        Assert.False(restored);
        Assert.Equal(SettingsHotkeyRestoreGuard.ErrorMessage, error);
    }
}

public sealed class M12PrivilegedStorageAclTests
{
    [Fact]
    public void Read_and_execute_is_allowed_but_write_delete_and_acl_control_are_rejected()
    {
        Assert.False(PrivilegedIndexStorage.GrantsDangerousRights(System.Security.AccessControl.FileSystemRights.ReadAndExecute));
        Assert.True(PrivilegedIndexStorage.GrantsDangerousRights(System.Security.AccessControl.FileSystemRights.WriteData));
        Assert.True(PrivilegedIndexStorage.GrantsDangerousRights(System.Security.AccessControl.FileSystemRights.Delete));
        Assert.True(PrivilegedIndexStorage.GrantsDangerousRights(System.Security.AccessControl.FileSystemRights.ChangePermissions));
        Assert.True(PrivilegedIndexStorage.GrantsDangerousRights(System.Security.AccessControl.FileSystemRights.TakeOwnership));
    }
}


public sealed class M12CatalogActivePathTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "quail-m12-active-paths", Guid.NewGuid().ToString("N"));
    private readonly IndexCatalogEntry _entry = new("volume-a", "D:\\", ManagedIndexPath.ForVolumeIdentity("volume-a"), true);

    [Fact]
    public async Task Current_volume_identity_mismatch_is_excluded()
    {
        var controller = await LoadAsync(_ => new VolumeDescriptor("volume-b", "D:\\", "NTFS", ""), _ => Complete("volume-a"));

        Assert.Empty(controller.ActivePaths);
    }

    [Fact]
    public async Task Unavailable_mount_is_excluded()
    {
        var controller = await LoadAsync(_ => throw new NotSupportedException("Unavailable"), _ => Complete("volume-a"));

        Assert.Empty(controller.ActivePaths);
    }

    [Fact]
    public async Task Compatible_complete_index_is_included()
    {
        var controller = await LoadAsync(_ => new VolumeDescriptor("volume-a", "D:\\", "NTFS", ""), _ => Complete("volume-a"));

        Assert.Equal([_entry.DatabasePath], controller.ActivePaths);
    }

    [Fact]
    public async Task Rebuild_required_reevaluation_removes_source_without_restart()
    {
        var status = Complete("volume-a");
        var controller = await LoadAsync(_ => new VolumeDescriptor("volume-a", "D:\\", "NTFS", ""), _ => status);
        Assert.Equal([_entry.DatabasePath], controller.ActivePaths);

        status = status with { State = IndexState.RebuildRequired };
        controller.ReevaluateActivePaths();

        Assert.Empty(controller.ActivePaths);
    }

    [Fact]
    public async Task Complete_reevaluation_keeps_source_active()
    {
        var controller = await LoadAsync(_ => new VolumeDescriptor("volume-a", "D:\\", "NTFS", ""), _ => Complete("volume-a"));

        controller.ReevaluateActivePaths();

        Assert.Equal([_entry.DatabasePath], controller.ActivePaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private async Task<IndexCatalogController> LoadAsync(Func<string, VolumeDescriptor> validateVolume, Func<string, IndexStatus> readStatus)
    {
        var store = new IndexCatalogStore(Path.Combine(_directory, "indexes.json"));
        await store.SaveAsync(new IndexCatalogDocument(1, [_entry]));
        var controller = new IndexCatalogController(store, validateVolume, readStatus);
        await controller.LoadAsync();
        return controller;
    }

    private static IndexStatus Complete(string identity) => new(IndexState.Complete, identity, "D:\\", 1, DateTimeOffset.UtcNow, null, null);
}

public sealed class M12IndexFreshnessTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "quail-m12-freshness", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;
    private static readonly VolumeDescriptor Volume = new("volume-freshness", "X:\\", "NTFS", "Freshness");

    public M12IndexFreshnessTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "index.db");
    }

    [Fact]
    public void Build_sets_last_refreshed_utc()
    {
        var store = Build();
        Assert.NotNull(store.GetStatus().LastRefreshedUtc);
    }

    [Fact]
    public void Successful_zero_change_sync_updates_last_refreshed_utc()
    {
        var store = Build();
        var previous = DateTimeOffset.UtcNow.AddHours(-2);
        SetRefresh(previous);

        store.ApplyParsedBatchesForTesting(Volume, Journal(), []);

        Assert.True(store.GetStatus().LastRefreshedUtc > previous);
    }

    [Fact]
    public void Failed_or_rebuild_required_sync_does_not_update_last_refreshed_utc()
    {
        var store = Build();
        var previous = DateTimeOffset.UtcNow.AddHours(-2);
        SetRefresh(previous);
        var record = new NamespaceRecord(new NativeFileId(Convert.FromHexString("0300000000000000")), new NativeFileId(Convert.FromHexString("0100000000000000")), "new.txt", 0, 20, 2);

        Assert.Throws<InvalidOperationException>(() => store.ApplyParsedBatchesForTesting(Volume, Journal(), [new JournalBatch(20, [new JournalRecord(record, UsnReason.FileCreate)])], failBeforeCommit: true));
        Assert.Equal(previous, store.GetStatus().LastRefreshedUtc);

        SetMetadata("build_state", "rebuild-required");
        var status = store.GetStatus();
        Assert.Equal(IndexState.RebuildRequired, status.State);
        Assert.Equal(previous, status.LastRefreshedUtc);
    }

    [Fact]
    public void Missing_legacy_refresh_timestamp_remains_complete_and_search_compatible()
    {
        var store = Build();
        DeleteMetadata("last_refreshed_utc");

        var status = store.GetStatus();
        Assert.Equal(IndexState.Complete, status.State);
        Assert.Null(status.LastRefreshedUtc);
        Assert.Equal(IndexFreshness.Unknown, IndexFreshnessPolicy.Classify(status, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Freshness_recommends_refresh_at_twenty_four_hours()
    {
        var now = DateTimeOffset.UtcNow;
        var fresh = new IndexStatus(IndexState.Complete, null, null, 0, null, null, null, now.AddHours(-23));
        var stale = fresh with { LastRefreshedUtc = now.AddHours(-24) };

        Assert.Equal(IndexFreshness.Fresh, IndexFreshnessPolicy.Classify(fresh, now));
        Assert.Equal(IndexFreshness.RefreshRecommended, IndexFreshnessPolicy.Classify(stale, now));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private IndexStore Build()
    {
        var store = new IndexStore(_databasePath);
        var root = new NativeFileId(Convert.FromHexString("0100000000000000"));
        store.BuildFromRecords(Volume, sink => sink(new NamespaceRecord(root, root, string.Empty, 0, 0, 2)), checkpoint: new IncrementalCheckpoint(1, 10, 0, 0));
        return store;
    }

    private static UsnJournalState Journal() => new(1, 0, 10, 0, 2, 3);

    private void SetRefresh(DateTimeOffset value) => SetMetadata("last_refreshed_utc", value.ToString("O"));
    private void SetMetadata(string key, string value)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE metadata SET value=$value WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private void DeleteMetadata(string key)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM metadata WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }
}
