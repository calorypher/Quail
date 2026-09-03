using System.Buffers.Binary;
using Microsoft.Data.Sqlite;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class IncrementalIndexStoreTests : IDisposable
{
    private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Quail-M03-" + Guid.NewGuid());
    private readonly NativeFileId _root = Id("0000000000000001");
    private readonly NativeFileId _directoryId = Id("0000000000000002");
    private readonly NativeFileId _file = Id("0000000000000003");
    private const ulong JournalId = 0x1234567890ABCDEF;

    public IncrementalIndexStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Schema_v3_persists_authoritative_checkpoint()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));

        var status = new IndexStore(DatabasePath).GetStatus();

        Assert.Equal(IndexState.Complete, status.State);
        Assert.Equal(JournalId, status.Checkpoint!.JournalId);
        Assert.Equal(100, status.Checkpoint.NextUsn);
        Assert.Equal(10, status.Checkpoint.FirstUsn);
        Assert.Equal(5, status.Checkpoint.LowestValidUsn);
    }

    [Fact]
    public void Mutation_and_checkpoint_are_visible_after_one_commit()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            new[]
            {
                Batch(
                    200,
                    new JournalRecord(
                        new NamespaceRecord(_file, _directoryId, "renamed.txt", 0, 150, 2),
                        UsnReason.RenameNewName))
            });

        var reopened = new IndexStore(DatabasePath);
        Assert.Equal(200, reopened.GetStatus().Checkpoint!.NextUsn);
        Assert.Equal("X:\\alpha\\renamed.txt", reopened.ReconstructPath(_file).Path);
        Assert.Equal("renamed.txt", Assert.Single(reopened.Search(new FileSearchQuery("r", Limit: 1))).Name);
    }

    [Fact]
    public void Short_query_chunks_follow_create_rename_and_delete()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        var created = Id("0000000000000004");

        Store.ApplyParsedBatchesForTesting(Volume, Journal(100),
        [
            Batch(200, new JournalRecord(new NamespaceRecord(created, _root, "beta.txt", 0, 120, 2), UsnReason.FileCreate)),
            Batch(300, new JournalRecord(new NamespaceRecord(created, _root, "bravo.txt", 0, 220, 2), UsnReason.RenameNewName)),
        ]);

        Assert.Equal("bravo.txt", Assert.Single(Store.Search(new FileSearchQuery("br", Limit: 1))).Name);
        Assert.DoesNotContain(Store.Search(new FileSearchQuery("be", Limit: 50)), result => result.Name == "beta.txt");

        Store.ApplyParsedBatchesForTesting(Volume, Journal(100),
        [Batch(400, new JournalRecord(new NamespaceRecord(created, _root, "bravo.txt", 0, 320, 2), UsnReason.FileDelete))]);

        Assert.DoesNotContain(Store.Search(new FileSearchQuery("br", Limit: 50)), result => result.Name == "bravo.txt");
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT max(posting_count) FROM short_query_posting_chunks";
        Assert.True(Convert.ToInt64(command.ExecuteScalar()) <= 1_024);
    }

    [Fact]
    public void Short_query_clustered_creates_do_not_exhaust_a_fresh_rank_label_gap()
    {
        var lower = Id("0000000000000011");
        var upper = Id("0000000000000012");
        Store.BuildFromRecords(Volume, sink =>
        {
            sink(new NamespaceRecord(_root, _root, "", 16, 0, 2));
            sink(new NamespaceRecord(lower, _root, "aaaa", 0, 0, 2));
            sink(new NamespaceRecord(upper, _root, "zzzz", 0, 0, 2));
        }, checkpoint: Checkpoint(100));

        var records = Enumerable.Range(0, 14)
            .Select(index =>
            {
                var name = new string((char)('y' - index), 4);
                return new JournalRecord(
                    new NamespaceRecord(Id($"000000000000{index + 0x20:X4}"), _root, name, 0, index + 1, 2),
                    UsnReason.FileCreate);
            })
            .ToArray();

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(200, records)]);

        var names = Store.ReadAllForDiagnostics().Select(record => record.Name).ToHashSet();
        Assert.All(records, record => Assert.Contains(record.NamespaceRecord.Name, names));
        Assert.Equal("mmmm", Assert.Single(Store.Search(new FileSearchQuery("m", Limit: 1))).Name);
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
    }

    [Fact]
    public void Short_query_coalesces_one_mutation_without_relabeling_metadata_only_updates()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        var created = Id("0000000000000021");
        var metadataCalls = 0;
        FileMetadata Metadata(NamespaceRecord _)
        {
            metadataCalls++;
            return new FileMetadata(20, 200);
        }

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(
                200,
                new JournalRecord(new NamespaceRecord(created, _directoryId, "probe.txt", 0, 120, 2), UsnReason.FileCreate),
                new JournalRecord(new NamespaceRecord(created, _directoryId, "probe.txt", 0, 121, 2), UsnReason.DataExtend),
                new JournalRecord(new NamespaceRecord(created, _directoryId, "probe.txt", 0, 122, 2), UsnReason.BasicInfoChange))],
            acquireMetadata: Metadata);

        Assert.Equal(1, metadataCalls);
        var labelBeforeMetadata = ReadRankLabel(created);
        metadataCalls = 0;
        var metadataOnly = Enumerable.Range(0, 32)
            .Select(index => new JournalRecord(
                new NamespaceRecord(created, _directoryId, "probe.txt", 0, 130 + index, 2),
                index % 2 == 0 ? UsnReason.DataOverwrite : UsnReason.BasicInfoChange))
            .ToArray();

        Store.ApplyParsedBatchesForTesting(Volume, Journal(200), [Batch(300, metadataOnly)], acquireMetadata: Metadata);

        Assert.Equal(1, metadataCalls);
        Assert.Equal(labelBeforeMetadata, ReadRankLabel(created));
        Assert.Equal("probe.txt", Assert.Single(Store.Search(new FileSearchQuery("pro", Limit: 1))).Name);

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(300),
            [Batch(
                400,
                new JournalRecord(new NamespaceRecord(created, _directoryId, "probe.txt", 0, 170, 2), UsnReason.RenameOldName),
                new JournalRecord(new NamespaceRecord(created, _directoryId, "renamed.txt", 0, 171, 2), UsnReason.RenameNewName))]);

        Assert.Empty(Store.Search(new FileSearchQuery("pro", Limit: 50)));
        Assert.Equal("renamed.txt", Assert.Single(Store.Search(new FileSearchQuery("ren", Limit: 1))).Name);

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(400),
            [Batch(500, new JournalRecord(new NamespaceRecord(created, _directoryId, "renamed.txt", 0, 180, 2), UsnReason.FileDelete))]);

        Assert.Empty(Store.Search(new FileSearchQuery("ren", Limit: 50)));
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
    }

    [Fact]
    public void Short_query_case_variants_remain_consistent_through_incremental_rename()
    {
        var first = Id("0000000000000011");
        var second = Id("0000000000000012");
        var third = Id("0000000000000013");
        var fourth = Id("0000000000000014");
        Store.BuildFromRecords(Volume, sink =>
        {
            sink(new NamespaceRecord(_root, _root, "", 16, 0, 2));
            sink(new NamespaceRecord(first, _root, "A1", 0, 0, 2));
            sink(new NamespaceRecord(second, _root, "a2", 0, 0, 2));
            sink(new NamespaceRecord(third, _root, "a3", 0, 0, 2));
            sink(new NamespaceRecord(fourth, _root, "A4", 0, 0, 2));
        }, checkpoint: Checkpoint(100));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(200, new JournalRecord(new NamespaceRecord(fourth, _root, "b4", 0, 0, 2), UsnReason.RenameNewName))]);

        Assert.Equal(
            ["A1", "a2", "a3"],
            Store.Search(new FileSearchQuery("a", Limit: 3)).Select(result => result.Name));
        Assert.Equal("b4", Assert.Single(Store.Search(new FileSearchQuery("b", Limit: 1))).Name);
    }

    [Fact]
    public void Short_query_format_mismatch_requires_a_safe_rebuild()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE metadata SET value='compact-short-query-v1' WHERE key='short_query_format'";
            command.ExecuteNonQuery();
        }

        var status = Store.GetStatus();

        Assert.Equal(IndexState.RebuildRequired, status.State);
        Assert.Contains("Short-query", status.Detail!);
        Assert.Throws<InvalidOperationException>(() => Store.Search(new FileSearchQuery("f")));
    }

    [Fact]
    public void Short_query_generation_mismatch_requires_a_safe_rebuild()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE metadata SET value='stale' WHERE key='short_query_generation'";
            command.ExecuteNonQuery();
        }

        var status = Store.GetStatus();

        Assert.Equal(IndexState.RebuildRequired, status.State);
        Assert.Contains("Short-query", status.Detail!);
        Assert.Throws<InvalidOperationException>(() => Store.Search(new FileSearchQuery("f")));
    }

    [Fact]
    public void Failure_before_commit_preserves_frontier_and_batch_replays_idempotently()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        var batch = Batch(200, new JournalRecord(new NamespaceRecord(_file, _directoryId, "renamed.txt", 0, 150, 2), UsnReason.RenameNewName));

        Assert.Throws<InvalidOperationException>(
            () => Store.ApplyParsedBatchesForTesting(
                Volume,
                Journal(100),
                new[] { batch },
                failBeforeCommit: true));
        var reopened = new IndexStore(DatabasePath);
        Assert.Equal(100, reopened.GetStatus().Checkpoint!.NextUsn);
        Assert.Equal("X:\\alpha\\file.txt", reopened.ReconstructPath(_file).Path);

        reopened.ApplyParsedBatchesForTesting(Volume, Journal(100), new[] { batch });
        reopened.ApplyParsedBatchesForTesting(Volume, Journal(100), new[] { batch });
        Assert.Equal(200, reopened.GetStatus().Checkpoint!.NextUsn);
        Assert.Equal("X:\\alpha\\renamed.txt", reopened.ReconstructPath(_file).Path);
    }

    [Fact]
    public void Malformed_or_unsupported_journal_records_fail_before_any_checkpoint_advance()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        Assert.Throws<InvalidDataException>(() => NtfsEnumerator.ParseJournalRecords(new byte[8], 0, 8));
        var unsupported = new byte[8];
        BitConverter.GetBytes(8).CopyTo(unsupported, 0);
        BitConverter.GetBytes((ushort)4).CopyTo(unsupported, 4);
        Assert.Throws<NotSupportedException>(() => NtfsEnumerator.ParseJournalRecords(unsupported, 0, unsupported.Length));
        Assert.Equal(100, new IndexStore(DatabasePath).GetStatus().Checkpoint!.NextUsn);
    }

    [Fact]
    public void Schema_v1_is_rebuild_required_without_a_guessed_checkpoint()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE metadata SET value='1' WHERE key='schema_version'";
            command.ExecuteNonQuery();
        }

        var status = new IndexStore(DatabasePath).GetStatus();
        Assert.Equal(IndexState.RebuildRequired, status.State);
        Assert.Null(status.Checkpoint);
        Assert.Contains("authoritative", status.Detail!);
    }

    [Fact]
    public void Rename_move_delete_and_directory_rename_converge_without_descendant_rewrites()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        var movedParent = Id("0000000000000004");
        var batches = new[]
        {
            Batch(200, new JournalRecord(new NamespaceRecord(movedParent, _root, "beta", 16, 120, 2), UsnReason.FileCreate)),
            Batch(300, new JournalRecord(new NamespaceRecord(_file, movedParent, "moved.txt", 0, 220, 2), UsnReason.RenameNewName)),
            Batch(400, new JournalRecord(new NamespaceRecord(_directoryId, _root, "renamed-alpha", 16, 320, 2), UsnReason.RenameNewName)),
            Batch(500, new JournalRecord(new NamespaceRecord(_file, movedParent, "moved.txt", 0, 420, 2), UsnReason.FileDelete)),
        };

        Store.ApplyParsedBatchesForTesting(Volume, Journal(100), batches.Take(3));
        Assert.Equal("X:\\beta\\moved.txt", Store.ReconstructPath(_file).Path);
        var descendant = Store.ReadAllForDiagnostics().Single(record => record.Name == "moved.txt");
        Assert.Equal(V3(movedParent), descendant.ParentFileId);
        Store.ApplyParsedBatchesForTesting(Volume, Journal(100), batches.Skip(3));
        Assert.False(Store.ReconstructPath(_file).Success);
        Assert.Equal(500, Store.GetStatus().Checkpoint!.NextUsn);
    }

    [Fact]
    public void Initial_build_handoff_replays_the_enumeration_window_before_promotion()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(50));
        var createdDuringEnumeration = Id("0000000000000009");
        var handoffBatch = Batch(200, new JournalRecord(new NamespaceRecord(createdDuringEnumeration, _directoryId, "during-enumeration.txt", 0, 150, 2), UsnReason.FileCreate));

        Store.BuildFromRecordsWithHandoffForTesting(Volume, Produce, Journal(100), Journal(200), new[] { handoffBatch });

        var rebuilt = new IndexStore(DatabasePath);
        Assert.Equal(IndexState.Complete, rebuilt.GetStatus().State);
        Assert.Equal(200, rebuilt.GetStatus().Checkpoint!.NextUsn);
        Assert.Equal("X:\\alpha\\during-enumeration.txt", rebuilt.ReconstructPath(createdDuringEnumeration).Path);
    }

    [Fact]
    public void V3_mutation_preserves_its_full_identifier_and_resolves_through_a_v2_parent()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        var wideFile = Id("09000000000000000000000000000000");
        var wideParent = V3(_root);
        Store.ApplyParsedBatchesForTesting(Volume, Journal(100), new[]
        {
            Batch(200, new JournalRecord(new NamespaceRecord(wideFile, wideParent, "wide-v3.txt", 0, 150, 3), UsnReason.FileCreate)),
        });

        Assert.Contains(Store.ReadAllForDiagnostics(), record => record.FileId.Equals(wideFile));
        Assert.Equal("X:\\wide-v3.txt", Store.ReconstructPath(wideFile).Path);
    }

    [Fact]
    public void Existing_v2_file_and_ordinary_v3_update_have_one_namespace_entry()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        var v3File = V3(_file);
        var v3Parent = V3(_directoryId);

        Store.ApplyParsedBatchesForTesting(Volume, Journal(100), new[]
        {
            Batch(200, new JournalRecord(new NamespaceRecord(v3File, v3Parent, "file.txt", 0, 150, 3), 0x00000001)),
        });

        Assert.Single(Store.ReadAllForDiagnostics(), record => record.Name == "file.txt");
    }

    [Fact]
    public void V3_directory_rename_keeps_an_untouched_v2_descendant_resolvable_without_subtree_rewrite()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        var v3Directory = V3(_directoryId);
        var v3Root = V3(_root);

        Store.ApplyParsedBatchesForTesting(Volume, Journal(100), new[]
        {
            Batch(200,
                new JournalRecord(new NamespaceRecord(v3Directory, v3Root, "alpha", 16, 150, 3), UsnReason.RenameOldName),
                new JournalRecord(new NamespaceRecord(v3Directory, v3Root, "renamed-alpha", 16, 160, 3), UsnReason.RenameNewName)),
        });

        Assert.Equal("X:\\renamed-alpha\\file.txt", Store.ReconstructPath(_file).Path);
        Assert.Equal("file.txt", Assert.Single(Store.Search(new FileSearchQuery("f", Limit: 1))).Name);
        var descendant = Store.ReadAllForDiagnostics().Single(record => record.Name == "file.txt");
        Assert.Equal(V3(_directoryId), descendant.ParentFileId);
    }

    [Fact]
    public void V3_delete_and_replay_are_idempotent_against_a_v2_initial_entry()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        var delete = Batch(200, new JournalRecord(new NamespaceRecord(V3(_file), V3(_directoryId), "file.txt", 0, 150, 3), UsnReason.FileDelete));

        Store.ApplyParsedBatchesForTesting(Volume, Journal(100), new[] { delete });
        Store.ApplyParsedBatchesForTesting(Volume, Journal(100), new[] { delete });

        Assert.DoesNotContain(Store.ReadAllForDiagnostics(), record => record.Name == "file.txt");
        Assert.False(Store.ReconstructPath(_file).Success);
    }

    [Fact]
    public void Prior_schema_v2_identity_format_requires_a_safe_rebuild()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM metadata WHERE key='namespace_identity_format'";
            command.ExecuteNonQuery();
        }

        var status = Store.GetStatus();
        Assert.Equal(IndexState.RebuildRequired, status.State);
        Assert.Contains("identity", status.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Store.ReadAllForDiagnostics());
        Assert.False(Store.ReconstructPath(_file).Success);
    }

    [Fact]
    public void Unobserved_v3_identifier_shape_fails_closed_without_checkpoint_advance()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));
        var unsupportedV3 = Id("00000000000000031122334455667788");

        Assert.Throws<NotSupportedException>(() => Store.ApplyParsedBatchesForTesting(Volume, Journal(100), new[]
        {
            Batch(200, new JournalRecord(new NamespaceRecord(unsupportedV3, V3(_directoryId), "unsupported-v3.txt", 0, 150, 3), UsnReason.FileCreate)),
        }));

        Assert.Equal(100, Store.GetStatus().Checkpoint!.NextUsn);
        Assert.DoesNotContain(Store.ReadAllForDiagnostics(), record => record.Name == "unsupported-v3.txt");
    }

    private IndexStore Store => new(DatabasePath);
    private string DatabasePath => System.IO.Path.Combine(_directory, "index.db");
    private VolumeDescriptor Volume => new("\\\\?\\Volume{test}", "X:\\", "NTFS", "Test");
    private IncrementalCheckpoint Checkpoint(long next) => new(JournalId, next, 10, 5);
    private UsnJournalState Journal(long next) => new(JournalId, 10, next, 5, 2, 3);
    private static JournalBatch Batch(long next, params JournalRecord[] records) => new(next, records);
    private void Produce(Action<NamespaceRecord> sink)
    {
        sink(new NamespaceRecord(_root, _root, "", 16, 0, 2));
        sink(new NamespaceRecord(_directoryId, _root, "alpha", 16, 0, 2));
        sink(new NamespaceRecord(_file, _directoryId, "file.txt", 0, 0, 2));
    }
    private static NativeFileId Id(string hex) => new(Convert.FromHexString(hex));
    private static NativeFileId V3(NativeFileId legacy)
    {
        var full = new byte[16];
        legacy.Bytes.Span.CopyTo(full);
        return new NativeFileId(full);
    }

    private long ReadRankLabel(NativeFileId fileId)
    {
        var canonical = fileId.Bytes.Length == 16 ? fileId : V3(fileId);
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();
        using var rowIdCommand = connection.CreateCommand();
        rowIdCommand.CommandText = "SELECT rowid FROM namespace_entries WHERE file_id=$id;";
        rowIdCommand.Parameters.Add("$id", SqliteType.Blob).Value = canonical.Bytes.ToArray();
        var rowId = Convert.ToInt64(rowIdCommand.ExecuteScalar());
        using var ranks = connection.CreateCommand();
        ranks.CommandText = "SELECT entry_count,payload FROM short_query_rank_chunks;";
        using var reader = ranks.ExecuteReader();
        while (reader.Read())
        {
            var count = reader.GetInt32(0);
            var payload = (byte[])reader[1];
            for (var index = 0; index < count; index++)
            {
                var offset = index * 28;
                if (BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 8, 8)) == rowId)
                {
                    return BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset, 8));
                }
            }
        }

        throw new InvalidOperationException("Short-query rank label is missing.");
    }
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
