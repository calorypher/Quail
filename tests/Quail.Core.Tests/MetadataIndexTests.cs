using Microsoft.Data.Sqlite;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class MetadataIndexTests : IDisposable
{
    private const ulong JournalId = 0x1234567890ABCDEF;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Quail-M05-" + Guid.NewGuid());
    private readonly NativeFileId _root = Id("0000000000000001");
    private readonly NativeFileId _folder = Id("0000000000000002");
    private readonly NativeFileId _zero = Id("0000000000000003");
    private readonly NativeFileId _small = Id("0000000000000004");
    private readonly NativeFileId _large = Id("0000000000000005");
    private readonly NativeFileId _unavailable = Id("0000000000000006");
    private readonly NativeFileId _reparse = Id("0000000000000007");
    private const long TimeOne = 133_000_000_000_000_000;
    private const long TimeTwo = TimeOne + 10_000_000;

    public MetadataIndexTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Persistent_size_and_modified_filters_have_exact_nullable_boundaries()
    {
        Build();

        Assert.Single(Store.Search(new FileSearchQuery("zero")));
        Assert.Equal(
            new[] { "zero.log" },
            Store.Search(new FileSearchQuery("zero", MinimumSize: 0, MaximumSize: 0))
                .Select(result => result.Name));
        Assert.Equal(
            new[] { "small.log" },
            Store.Search(new FileSearchQuery("small", MinimumSize: 10, MaximumSize: 10))
                .Select(result => result.Name));
        Assert.Equal(
            new[] { "large.log" },
            Store.Search(new FileSearchQuery("large", MinimumSize: 11))
                .Select(result => result.Name));
        Assert.Equal(
            new[] { "small.log" },
            Store.Search(new FileSearchQuery(
                    "small",
                    ModifiedAfterUtcFileTime: TimeOne,
                    ModifiedBeforeUtcFileTime: TimeOne))
                .Select(result => result.Name));
        Assert.DoesNotContain(Store.Search(new FileSearchQuery("unavailable", MinimumSize: 0)), result => result.Name == "unavailable.log");
        Assert.DoesNotContain(Store.Search(new FileSearchQuery("unavailable", ModifiedAfterUtcFileTime: TimeOne)), result => result.Name == "unavailable.log");
        Assert.Throws<ArgumentException>(() => Store.Search(new FileSearchQuery("small", MinimumSize: 11, MaximumSize: 10)));
        Assert.Throws<ArgumentException>(() => Store.Search(new FileSearchQuery("small", ModifiedAfterUtcFileTime: TimeTwo, ModifiedBeforeUtcFileTime: TimeOne)));
    }

    [Fact]
    public void Attribute_and_metadata_filters_compose_with_name_extension_type_order_and_limit()
    {
        Build();

        var result = Assert.Single(
            Store.Search(new FileSearchQuery(
                "small",
                SearchEntryType.File,
                "log",
                1,
                10,
                10,
                TimeOne,
                TimeOne,
                Hidden: true,
                ReadOnly: true,
                System: true)));

        Assert.Equal("small.log", result.Name);
        Assert.Equal(10, result.LogicalSize);
        Assert.Equal(TimeOne, result.LastWriteTimeUtcFileTime);
        var first = Store.Search(new FileSearchQuery(".log", Limit: 2, MinimumSize: 0));
        var second = Store.Search(new FileSearchQuery(".log", Limit: 2, MinimumSize: 0));
        Assert.Equal(first.Select(item => item.FileId), second.Select(item => item.FileId));
    }

    [Fact]
    public void Old_complete_index_without_metadata_marker_is_rebuild_required()
    {
        Build();
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM metadata WHERE key='metadata_format'";
            command.ExecuteNonQuery();
        }

        Assert.Equal(IndexState.RebuildRequired, Store.GetStatus().State);
        Assert.Throws<InvalidOperationException>(() => Store.Search(new FileSearchQuery("small")));
    }

    [Fact]
    public void Refresh_reasons_replace_metadata_once_per_file_and_failure_becomes_null()
    {
        Build();
        var calls = 0;
        FileMetadata Refresh(NamespaceRecord record)
        {
            calls++;
            return record.FileId.Equals(Canonical(_small)) ? new FileMetadata(20, TimeTwo) : new FileMetadata(null, null);
        }

        Store.ApplyParsedBatchesForTesting(Volume, Journal(100), new[]
        {
            Batch(200,
                new JournalRecord(new NamespaceRecord(_small, _folder, "small.log", 7, 150, 2), UsnReason.DataExtend),
                new JournalRecord(new NamespaceRecord(_small, _folder, "small.log", 7, 151, 2), UsnReason.BasicInfoChange | 0x80000000)),
        }, acquireMetadata: Refresh);

        Assert.Equal(1, calls);
        var refreshed = Assert.Single(
            Store.Search(new FileSearchQuery(
                "small",
                MinimumSize: 20,
                MaximumSize: 20,
                ModifiedAfterUtcFileTime: TimeTwo,
                ModifiedBeforeUtcFileTime: TimeTwo)));
        Assert.Equal(20, refreshed.LogicalSize);
        Store.ApplyParsedBatchesForTesting(Volume, Journal(200), new[]
        {
            Batch(300, new JournalRecord(new NamespaceRecord(_small, _folder, "small.log", 7, 250, 2), UsnReason.DataTruncation)),
        }, acquireMetadata: _ => new FileMetadata(null, null));
        Assert.Empty(Store.Search(new FileSearchQuery("small", MinimumSize: 0)));
    }

    [Fact]
    public void Rename_pair_across_batches_preserves_metadata_without_lookup_and_missing_new_name_uses_fallback()
    {
        Build();
        var calls = 0;
        FileMetadata Metadata(NamespaceRecord _)
        {
            calls++;
            return new FileMetadata(999, TimeTwo);
        }

        Store.ApplyParsedBatchesForTesting(Volume, Journal(100), new[]
        {
            Batch(200, new JournalRecord(new NamespaceRecord(_small, _folder, "small.log", 7, 150, 2), UsnReason.RenameOldName)),
        }, acquireMetadata: Metadata);
        Store.ApplyParsedBatchesForTesting(Volume, Journal(200), new[]
        {
            Batch(300, new JournalRecord(new NamespaceRecord(_small, _folder, "renamed.log", 7, 250, 2), UsnReason.RenameNewName)),
        }, acquireMetadata: Metadata);

        Assert.Equal(0, calls);
        var preserved = Assert.Single(Store.Search(new FileSearchQuery("renamed", MinimumSize: 10, MaximumSize: 10)));
        Assert.Equal(TimeOne, preserved.LastWriteTimeUtcFileTime);

        var created = Id("0000000000000008");
        Store.ApplyParsedBatchesForTesting(Volume, Journal(300), new[]
        {
            Batch(400, new JournalRecord(new NamespaceRecord(created, _folder, "fallback.log", 0, 350, 2), UsnReason.RenameNewName)),
        }, acquireMetadata: Metadata);
        Assert.Equal(1, calls);
        Assert.Single(Store.Search(new FileSearchQuery("fallback", MinimumSize: 999)));
    }

    [Fact]
    public void Delete_removes_metadata_and_fault_before_commit_rolls_back_namespace_fts_and_checkpoint()
    {
        Build();
        var replacement = new FileMetadata(50, TimeTwo);
        var update = Batch(200, new JournalRecord(new NamespaceRecord(_small, _folder, "updated.log", 7, 150, 2), UsnReason.DataOverwrite | UsnReason.RenameNewName));

        Assert.Throws<InvalidOperationException>(
            () => Store.ApplyParsedBatchesForTesting(
                Volume,
                Journal(100),
                new[] { update },
                true,
                _ => replacement));
        Assert.Equal(100, Store.GetStatus().Checkpoint!.NextUsn);
        Assert.Single(Store.Search(new FileSearchQuery("small", MinimumSize: 10, MaximumSize: 10)));
        Assert.Empty(Store.Search(new FileSearchQuery("updated")));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            new[]
            {
                Batch(
                    200,
                    new JournalRecord(
                        new NamespaceRecord(_small, _folder, "small.log", 7, 250, 2),
                        UsnReason.FileDelete))
            });
        Assert.Empty(Store.Search(new FileSearchQuery("small")));
    }

    private IndexStore Store => new(DatabasePath);
    private string DatabasePath => Path.Combine(_directory, "index.db");
    private VolumeDescriptor Volume => new("\\\\?\\Volume{test}", "X:\\", "NTFS", "Test");
    private UsnJournalState Journal(long next) => new(JournalId, 10, next, 5, 2, 3);
    private static JournalBatch Batch(long next, params JournalRecord[] records) => new(next, records);
    private void Build() => Store.BuildFromRecords(Volume, Produce, checkpoint: new IncrementalCheckpoint(JournalId, 100, 10, 5), acquireMetadata: MetadataForInitialRecord);
    private FileMetadata MetadataForInitialRecord(NamespaceRecord record) => record.FileId.Equals(Canonical(_zero)) ? new FileMetadata(0, TimeOne) :
        record.FileId.Equals(Canonical(_small)) ? new FileMetadata(10, TimeOne) :
        record.FileId.Equals(Canonical(_large)) ? new FileMetadata(100, TimeTwo) :
        record.FileId.Equals(Canonical(_reparse)) ? new FileMetadata(null, TimeTwo) : new FileMetadata(null, null);
    private void Produce(Action<NamespaceRecord> sink)
    {
        sink(new NamespaceRecord(_root, _root, "", 16, 0, 2));
        sink(new NamespaceRecord(_folder, _root, "folder", 16, 0, 2));
        sink(new NamespaceRecord(_zero, _folder, "zero.log", 0, 0, 2));
        sink(new NamespaceRecord(_small, _folder, "small.log", 7, 0, 2));
        sink(new NamespaceRecord(_large, _folder, "large.log", 0, 0, 2));
        sink(new NamespaceRecord(_unavailable, _folder, "unavailable.log", 0, 0, 2));
        sink(new NamespaceRecord(_reparse, _folder, "reparse.log", 0x400, 0, 2));
    }
    private static NativeFileId Id(string hex) => new(Convert.FromHexString(hex));
    private static NativeFileId Canonical(NativeFileId legacy)
    {
        var bytes = new byte[16]; legacy.Bytes.Span.CopyTo(bytes); return new NativeFileId(bytes);
    }
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
