using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Data.Sqlite;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class IndexStoreTests : IDisposable
{
    private readonly string _directory =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Quail-M02-" + Guid.NewGuid());
    private readonly NativeFileId _root = Id("0000000000000001");
    private readonly NativeFileId _alpha = Id("0000000000000002");
    private readonly NativeFileId _file = Id("0000000000000003");

    public IndexStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Fresh_database_is_absent()
    {
        Assert.Equal(IndexState.Absent, Store.GetStatus().State);
    }

    [Fact]
    public void Schema_and_complete_state_are_created()
    {
        Store.BuildFromRecords(Volume, Produce);
        var status = Store.GetStatus();

        Assert.Equal(IndexState.Complete, status.State);
        Assert.Equal(3, status.RecordCount);
        Assert.Equal(Volume.StableIdentity, status.VolumeIdentity);
    }

    [Fact]
    public void Eight_and_sixteen_byte_identifiers_round_trip_without_truncation()
    {
        var wide = Id("0123456789ABCDEF0011223344556677");
        Store.BuildFromRecords(
            Volume,
            sink =>
            {
                sink(new NamespaceRecord(_root, _root, "", 0, 0, 2));
                sink(new NamespaceRecord(wide, _root, "wide.bin", 0, 0, 3));
            });

        Assert.Contains(Store.ReadAllForDiagnostics(), record => record.FileId.Equals(wide));
    }

    [Fact]
    public void Path_reconstructs_after_reopen()
    {
        Store.BuildFromRecords(Volume, Produce);
        var reopened = new IndexStore(Path);
        var result = reopened.ReconstructPath(_file);

        Assert.True(result.Success);
        Assert.Equal("X:\\alpha\\file.txt", result.Path);
    }

    [Fact]
    public void Sixteen_byte_parent_chain_reaches_sixteen_byte_root()
    {
        var root = Id("00112233445566778899AABBCCDDEEFF");
        var child = Id("102132435465768798A9BACBDCEDFE0F");
        Store.BuildFromRecords(
            Volume,
            sink =>
            {
                sink(new NamespaceRecord(root, root, "", 16, 0, 3));
                sink(new NamespaceRecord(child, root, "wide.txt", 0, 0, 3));
            });
        var result = Store.ReconstructPath(child);

        Assert.True(result.Success);
        Assert.Equal("X:\\wide.txt", result.Path);
    }

    [Fact]
    public void Missing_parent_and_cycle_are_diagnosed()
    {
        Store.BuildFromRecords(Volume, Produce);
        using (var connection = new SqliteConnection($"Data Source={Path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE namespace_entries SET parent_file_id=$missing WHERE file_id=$file;";
            command.Parameters.Add("$missing", SqliteType.Blob).Value = Id("00000000000000990000000000000000").Bytes.ToArray();
            command.Parameters.Add("$file", SqliteType.Blob).Value = Id("00000000000000030000000000000000").Bytes.ToArray();
            command.ExecuteNonQuery();
        }
        Assert.Contains("Missing parent", Store.ReconstructPath(_file).Diagnostic);

        using (var connection = new SqliteConnection($"Data Source={Path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE namespace_entries SET parent_file_id=$file WHERE file_id=$alpha;
                UPDATE namespace_entries SET parent_file_id=$alpha WHERE file_id=$file;
                """;
            command.Parameters.Add("$alpha", SqliteType.Blob).Value = Id("00000000000000020000000000000000").Bytes.ToArray();
            command.Parameters.Add("$file", SqliteType.Blob).Value = Id("00000000000000030000000000000000").Bytes.ToArray();
            command.ExecuteNonQuery();
        }
        Assert.Contains("Cycle", Store.ReconstructPath(_file).Diagnostic);
    }

    [Fact]
    public void Failed_replacement_preserves_previous_complete_index()
    {
        Store.BuildFromRecords(Volume, Produce);

        Assert.Throws<InvalidOperationException>(() => Store.BuildFromRecords(Volume, Produce, 1));
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        Assert.True(File.Exists(Path + ".building"));
    }

    [Fact]
    public void Incomplete_only_database_is_not_usable()
    {
        Assert.Throws<InvalidOperationException>(() => Store.BuildFromRecords(Volume, Produce, 1));
        Assert.Equal(IndexState.Incomplete, Store.GetStatus().State);
    }

    [Fact]
    public void Malformed_enumeration_failure_does_not_promote_replacement()
    {
        Store.BuildFromRecords(Volume, Produce);
        Assert.Throws<InvalidDataException>(
            () => Store.BuildFromRecords(
                Volume,
                sink => NtfsEnumerator.ParseRecords(new byte[8], 0, 8, sink)));
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        Assert.True(File.Exists(Path + ".building"));
    }

    [Fact]
    public void Unsupported_enumeration_failure_does_not_promote_replacement()
    {
        Store.BuildFromRecords(Volume, Produce);
        var record = new byte[8];
        BitConverter.GetBytes(8).CopyTo(record, 0);
        BitConverter.GetBytes((ushort)4).CopyTo(record, 4);

        Assert.Throws<NotSupportedException>(
            () => Store.BuildFromRecords(
                Volume,
                sink => NtfsEnumerator.ParseRecords(record, 0, record.Length, sink)));
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
    }

    [Fact]
    public void Absent_diagnostics_do_not_create_database()
    {
        Assert.Empty(Store.ReadAllForDiagnostics());
        Assert.False(File.Exists(Path));
    }

    [Fact]
    public void Completed_index_supports_read_only_status_and_search_without_directory_write_access()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new IndexStore(Path, IndexStoreJournalLifecycle.DeleteWhenQuiescent);
        const ulong journalId = 0x1234567890ABCDEF;
        store.BuildFromRecords(
            Volume,
            Produce,
            checkpoint: new IncrementalCheckpoint(journalId, 100, 10, 5));
        AssertQuiescentRollbackState();
        AssertReadOnlyStatusAndSearch(store, "file", "file.txt");

        var updated = Id("0000000000000004");
        var journal = new UsnJournalState(journalId, 10, 100, 5, 2, 3);
        store.ApplyParsedBatchesForTesting(
            Volume,
            journal,
            [new JournalBatch(200, [new JournalRecord(
                new NamespaceRecord(updated, _alpha, "updated.txt", 0, 150, 2),
                UsnReason.FileCreate)])]);

        AssertQuiescentRollbackState();
        AssertReadOnlyStatusAndSearch(store, "updated", "updated.txt");
    }

    [Fact]
    public void Protected_lifecycle_returns_to_readable_delete_mode_after_failed_update()
    {
        var store = new IndexStore(Path, IndexStoreJournalLifecycle.DeleteWhenQuiescent);
        const ulong journalId = 0x1234567890ABCDEF;
        store.BuildFromRecords(
            Volume,
            Produce,
            checkpoint: new IncrementalCheckpoint(journalId, 100, 10, 5));
        var journal = new UsnJournalState(journalId, 10, 100, 5, 2, 3);
        var rename = new JournalBatch(200, [new JournalRecord(
            new NamespaceRecord(_file, _alpha, "renamed.txt", 0, 150, 2),
            UsnReason.RenameNewName)]);

        Assert.Throws<InvalidOperationException>(
            () => store.ApplyParsedBatchesForTesting(Volume, journal, [rename], failBeforeCommit: true));

        AssertQuiescentRollbackState();
        Assert.Contains(store.Search(new FileSearchQuery("file")), result => result.Name == "file.txt");
        Assert.Empty(store.Search(new FileSearchQuery("renamed")));
    }

    [Fact]
    public async Task Protected_lifecycle_waits_for_a_concurrent_reader_and_returns_to_delete_mode()
    {
        var store = new IndexStore(Path, IndexStoreJournalLifecycle.DeleteWhenQuiescent);
        const ulong journalId = 0x1234567890ABCDEF;
        store.BuildFromRecords(
            Volume,
            Produce,
            checkpoint: new IncrementalCheckpoint(journalId, 100, 10, 5));

        using var readerConnection = OpenReadOnlyConnection();
        using var transaction = readerConnection.BeginTransaction();
        using var command = readerConnection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM namespace_entries";
        Assert.Equal(3L, Convert.ToInt64(command.ExecuteScalar()));

        var update = Task.Run(() => store.ApplyParsedBatchesForTesting(
            Volume,
            new UsnJournalState(journalId, 10, 100, 5, 2, 3),
            []));
        await Task.Delay(100);
        command.Dispose();
        transaction.Dispose();
        readerConnection.Dispose();

        await update.WaitAsync(TimeSpan.FromSeconds(5));
        AssertQuiescentRollbackState();
        Assert.Equal(IndexState.Complete, store.GetStatus().State);
    }

    [Fact]
    public void Default_core_lifecycle_remains_in_wal_mode()
    {
        Store.BuildFromRecords(Volume, Produce);

        Assert.Equal("wal", ReadJournalMode());
    }

    [Fact]
    public void Protected_build_removes_orphaned_final_and_staging_sidecars()
    {
        var store = new IndexStore(Path, IndexStoreJournalLifecycle.DeleteWhenQuiescent);
        store.BuildFromRecords(Volume, Produce);
        var sidecars = new[]
        {
            Path + "-journal",
            Path + "-wal",
            Path + "-shm",
            Path + ".building-journal",
            Path + ".building-wal",
            Path + ".building-shm"
        };
        foreach (var sidecar in sidecars)
        {
            File.WriteAllText(sidecar, "orphaned-sidecar");
        }

        store.BuildFromRecords(Volume, Produce);

        Assert.All(sidecars, sidecar => Assert.False(File.Exists(sidecar)));
        Assert.Equal(IndexState.Complete, store.GetStatus().State);
    }

    private void AssertReadOnlyStatusAndSearch(IndexStore store, string query, string expectedName)
    {
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows identity has no SID.");
        SetDirectoryRights(user, FileSystemRights.ReadAndExecute);
        try
        {
            var probe = System.IO.Path.Combine(_directory, "reader-write-probe.tmp");
            Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(probe, "probe"));

            var status = store.GetStatus();
            var searchException = Record.Exception(
                () => store.Search(new FileSearchQuery(query, Limit: 10)));

            Assert.True(
                status.State == IndexState.Complete && searchException is null,
                $"Status: {status.State}: {status.Detail}; search: {searchException?.Message}");
            Assert.Contains(
                store.Search(new FileSearchQuery(query, Limit: 10)),
                result => result.Name == expectedName);
        }
        finally
        {
            SetDirectoryRights(user, FileSystemRights.FullControl);
        }
    }

    private void AssertQuiescentRollbackState()
    {
        Assert.Equal("delete", ReadJournalMode());
        Assert.False(File.Exists(Path + "-journal"));
        Assert.False(File.Exists(Path + "-wal"));
        Assert.False(File.Exists(Path + "-shm"));
    }

    private string ReadJournalMode()
    {
        using var connection = OpenReadOnlyConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        return Assert.IsType<string>(command.ExecuteScalar());
    }

    private SqliteConnection OpenReadOnlyConnection()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
        connection.Open();
        return connection;
    }

    private IndexStore Store => new(Path);
    private string Path => System.IO.Path.Combine(_directory, "index.db");
    private VolumeDescriptor Volume => new("\\\\?\\Volume{test}", "X:\\", "NTFS", "Test");

    private void Produce(Action<NamespaceRecord> sink)
    {
        sink(new NamespaceRecord(_root, _root, "", 16, 0, 2));
        sink(new NamespaceRecord(_alpha, _root, "alpha", 16, 0, 2));
        sink(new NamespaceRecord(_file, _alpha, "file.txt", 0, 0, 2));
    }

    private static NativeFileId Id(string hex) => new(Convert.FromHexString(hex));

    private void SetDirectoryRights(SecurityIdentifier user, FileSystemRights rights)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            user,
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(_directory).SetAccessControl(security);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
