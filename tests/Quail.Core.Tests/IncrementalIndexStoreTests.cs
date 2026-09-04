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

        AssertShortQueryIntegrity();
        var names = Store.ReadAllForDiagnostics().Select(record => record.Name).ToHashSet();
        Assert.All(records, record => Assert.Contains(record.NamespaceRecord.Name, names));
        Assert.Equal("mmmm", Assert.Single(Store.Search(new FileSearchQuery("m", Limit: 1))).Name);

        var renamed = records.Single(record => record.NamespaceRecord.Name == "mmmm").NamespaceRecord.FileId;
        var deleted = records.Single(record => record.NamespaceRecord.Name == "llll").NamespaceRecord.FileId;
        var inserted = Id("0000000000000040");
        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(200),
            [Batch(300, new JournalRecord(
                new NamespaceRecord(renamed, _root, "mmmm-renamed", 0, 30, 2),
                UsnReason.RenameNewName))]);
        AssertShortQueryIntegrity();

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(300),
            [Batch(400, new JournalRecord(
                new NamespaceRecord(deleted, _root, "llll", 0, 31, 2),
                UsnReason.FileDelete))]);
        AssertShortQueryIntegrity();

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(400),
            [Batch(500, new JournalRecord(
                new NamespaceRecord(inserted, _root, "mmmm-middle", 0, 32, 2),
                UsnReason.FileCreate))]);

        AssertShortQueryIntegrity();
        Assert.Equal(
            ["mmmm-middle", "mmmm-renamed"],
            Store.Search(new FileSearchQuery("mm", Limit: 10))
                .Where(result => result.Name.StartsWith("mmmm", StringComparison.Ordinal))
                .Select(result => result.Name));
        Assert.DoesNotContain(Store.ReadAllForDiagnostics(), record => record.Name == "llll");
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
    }

    [Fact]
    public void Short_query_deduplicates_metadata_acquisition_without_relabeling_metadata_only_updates()
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
    public void Short_query_source_order_preserves_interleaved_child_before_parent_delete()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(
                200,
                new JournalRecord(
                    new NamespaceRecord(_directoryId, _root, "alpha", 16, 120, 2),
                    UsnReason.BasicInfoChange),
                new JournalRecord(
                    new NamespaceRecord(_file, _directoryId, "file.txt", 0, 121, 2),
                    UsnReason.FileDelete),
                new JournalRecord(
                    new NamespaceRecord(_directoryId, _root, "alpha", 16, 122, 2),
                    UsnReason.FileDelete))]);

        Assert.False(Store.ReconstructPath(_file).Success);
        Assert.False(Store.ReconstructPath(_directoryId).Success);
        Assert.Empty(Store.Search(new FileSearchQuery("f", Limit: 50)));
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        AssertShortQueryIntegrity();
    }

    [Fact]
    public void Short_query_source_order_keeps_interleaved_parent_create_before_child()
    {
        var parent = Id("0000000000000051");
        var child = Id("0000000000000052");
        Store.BuildFromRecords(
            Volume,
            sink => sink(new NamespaceRecord(_root, _root, "", 16, 0, 2)),
            checkpoint: Checkpoint(100));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(
                200,
                new JournalRecord(
                    new NamespaceRecord(parent, _root, "parent", 16, 120, 2),
                    UsnReason.FileCreate),
                new JournalRecord(
                    new NamespaceRecord(parent, _root, "parent", 16, 121, 2),
                    UsnReason.BasicInfoChange),
                new JournalRecord(
                    new NamespaceRecord(child, parent, "child.txt", 0, 122, 2),
                    UsnReason.FileCreate),
                new JournalRecord(
                    new NamespaceRecord(parent, _root, "parent", 16, 123, 2),
                    UsnReason.DataExtend),
                new JournalRecord(
                    new NamespaceRecord(child, parent, "child.txt", 0, 124, 2),
                    UsnReason.DataExtend))]);

        Assert.Equal("X:\\parent\\child.txt", Store.ReconstructPath(child).Path);
        Assert.Equal("child.txt", Assert.Single(Store.Search(new FileSearchQuery("ch", Limit: 1))).Name);
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        AssertShortQueryIntegrity();
    }

    [Fact]
    public void Short_query_create_then_move_into_a_new_parent_preserves_source_order()
    {
        var child = Id("0000000000000061");
        var parent = Id("0000000000000062");
        Store.BuildFromRecords(
            Volume,
            sink => sink(new NamespaceRecord(_root, _root, "", 16, 0, 2)),
            checkpoint: Checkpoint(100));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(
                200,
                new JournalRecord(
                    new NamespaceRecord(child, _root, "child.txt", 0, 120, 2),
                    UsnReason.FileCreate),
                new JournalRecord(
                    new NamespaceRecord(parent, _root, "new-parent", 16, 121, 2),
                    UsnReason.FileCreate),
                new JournalRecord(
                    new NamespaceRecord(child, parent, "child.txt", 0, 122, 2),
                    UsnReason.RenameNewName))]);

        Assert.Equal("X:\\new-parent\\child.txt", Store.ReconstructPath(child).Path);
        Assert.Equal("child.txt", Assert.Single(Store.Search(new FileSearchQuery("ch", Limit: 1))).Name);
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        AssertShortQueryIntegrity();
    }

    [Fact]
    public void Short_query_directory_create_then_move_into_a_new_parent_updates_its_subtree()
    {
        var childDirectory = Id("0000000000000071");
        var descendant = Id("0000000000000072");
        var parent = Id("0000000000000073");
        Store.BuildFromRecords(
            Volume,
            sink => sink(new NamespaceRecord(_root, _root, "", 16, 0, 2)),
            checkpoint: Checkpoint(100));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(
                200,
                new JournalRecord(
                    new NamespaceRecord(childDirectory, _root, "child-directory", 16, 120, 2),
                    UsnReason.FileCreate),
                new JournalRecord(
                    new NamespaceRecord(descendant, childDirectory, "descendant.txt", 0, 121, 2),
                    UsnReason.FileCreate),
                new JournalRecord(
                    new NamespaceRecord(parent, _root, "new-parent", 16, 122, 2),
                    UsnReason.FileCreate),
                new JournalRecord(
                    new NamespaceRecord(childDirectory, parent, "child-directory", 16, 123, 2),
                    UsnReason.RenameNewName))]);

        Assert.Equal(
            "X:\\new-parent\\child-directory\\descendant.txt",
            Store.ReconstructPath(descendant).Path);
        Assert.Equal("descendant.txt", Assert.Single(Store.Search(new FileSearchQuery("de", Limit: 1))).Name);
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        AssertShortQueryIntegrity();
    }

    [Fact]
    public void Short_query_create_and_rename_in_one_batch_converges_in_source_order()
    {
        var created = Id("0000000000000081");
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(
                200,
                new JournalRecord(
                    new NamespaceRecord(created, _root, "before.txt", 0, 120, 2),
                    UsnReason.FileCreate),
                new JournalRecord(
                    new NamespaceRecord(created, _root, "before.txt", 0, 121, 2),
                    UsnReason.RenameOldName),
                new JournalRecord(
                    new NamespaceRecord(created, _directoryId, "after.txt", 0, 122, 2),
                    UsnReason.RenameNewName))]);

        Assert.Equal("X:\\alpha\\after.txt", Store.ReconstructPath(created).Path);
        Assert.Empty(Store.Search(new FileSearchQuery("be", Limit: 50)));
        Assert.Equal("after.txt", Assert.Single(Store.Search(new FileSearchQuery("af", Limit: 1))).Name);
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        AssertShortQueryIntegrity();
    }

    [Fact]
    public void Short_query_create_and_delete_in_one_batch_leaves_no_compact_state()
    {
        var created = Id("0000000000000091");
        Store.BuildFromRecords(
            Volume,
            sink => sink(new NamespaceRecord(_root, _root, "", 16, 0, 2)),
            checkpoint: Checkpoint(100));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(
                200,
                new JournalRecord(
                    new NamespaceRecord(created, _root, "transient.txt", 0, 120, 2),
                    UsnReason.FileCreate),
                new JournalRecord(
                    new NamespaceRecord(created, _root, "transient.txt", 0, 121, 2),
                    UsnReason.BasicInfoChange),
                new JournalRecord(
                    new NamespaceRecord(created, _root, "transient.txt", 0, 122, 2),
                    UsnReason.FileDelete))]);

        Assert.False(Store.ReconstructPath(created).Success);
        Assert.Empty(Store.Search(new FileSearchQuery("tr", Limit: 50)));
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        AssertShortQueryIntegrity();
    }

    [Fact]
    public void Short_query_existing_move_preserves_interleaved_unrelated_source_order()
    {
        var secondParent = Id("00000000000000A1");
        var unrelated = Id("00000000000000A2");
        Store.BuildFromRecords(Volume, sink =>
        {
            Produce(sink);
            sink(new NamespaceRecord(secondParent, _root, "beta", 16, 0, 2));
            sink(new NamespaceRecord(unrelated, _root, "unrelated.txt", 0, 0, 2));
        }, checkpoint: Checkpoint(100));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(
                200,
                new JournalRecord(
                    new NamespaceRecord(_file, _directoryId, "file.txt", 0, 120, 2),
                    UsnReason.RenameOldName),
                new JournalRecord(
                    new NamespaceRecord(unrelated, _root, "unrelated.txt", 0, 121, 2),
                    UsnReason.BasicInfoChange),
                new JournalRecord(
                    new NamespaceRecord(_file, secondParent, "moved.txt", 0, 122, 2),
                    UsnReason.RenameNewName))]);

        Assert.Equal("X:\\beta\\moved.txt", Store.ReconstructPath(_file).Path);
        Assert.Equal("X:\\unrelated.txt", Store.ReconstructPath(unrelated).Path);
        Assert.Equal("moved.txt", Assert.Single(Store.Search(new FileSearchQuery("mo", Limit: 1))).Name);
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        AssertShortQueryIntegrity();
    }

    [Fact]
    public void Short_query_source_order_preserves_interleaved_directory_rename_and_subtree_updates()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(
                200,
                new JournalRecord(
                    new NamespaceRecord(_directoryId, _root, "alpha", 16, 120, 2),
                    UsnReason.RenameOldName),
                new JournalRecord(
                    new NamespaceRecord(_file, _directoryId, "file.txt", 0, 121, 2),
                    UsnReason.BasicInfoChange),
                new JournalRecord(
                    new NamespaceRecord(_directoryId, _root, "renamed-alpha", 16, 122, 2),
                    UsnReason.RenameNewName))]);

        Assert.Equal("X:\\renamed-alpha\\file.txt", Store.ReconstructPath(_file).Path);
        Assert.Equal("file.txt", Assert.Single(Store.Search(new FileSearchQuery("fi", Limit: 1))).Name);
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        AssertShortQueryIntegrity();
    }

    [Fact]
    public void Short_query_directory_rename_across_batches_preserves_parent_topology()
    {
        Store.BuildFromRecords(Volume, Produce, checkpoint: Checkpoint(100));

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(100),
            [Batch(200, new JournalRecord(
                new NamespaceRecord(_directoryId, _root, "alpha", 16, 120, 2),
                UsnReason.RenameOldName))]);

        Assert.Equal("X:\\alpha\\file.txt", Store.ReconstructPath(_file).Path);
        AssertShortQueryIntegrity();

        Store.ApplyParsedBatchesForTesting(
            Volume,
            Journal(200),
            [Batch(300, new JournalRecord(
                new NamespaceRecord(_directoryId, _root, "renamed-alpha", 16, 220, 2),
                UsnReason.RenameNewName))]);

        Assert.Equal("X:\\renamed-alpha\\file.txt", Store.ReconstructPath(_file).Path);
        Assert.Equal("file.txt", Assert.Single(Store.Search(new FileSearchQuery("fi", Limit: 1))).Name);
        Assert.Equal(IndexState.Complete, Store.GetStatus().State);
        AssertShortQueryIntegrity();
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

    private void AssertShortQueryIntegrity()
    {
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();

        var ranksByRowId = new Dictionary<long, (long Label, long ParentLabel)>();
        var rankLabels = new HashSet<long>();
        using (var ranks = connection.CreateCommand())
        {
            ranks.CommandText = "SELECT entry_count,payload FROM short_query_rank_chunks ORDER BY first_label;";
            using var reader = ranks.ExecuteReader();
            while (reader.Read())
            {
                var count = reader.GetInt32(0);
                var payload = (byte[])reader[1];
                Assert.Equal(count * 28, payload.Length);
                for (var index = 0; index < count; index++)
                {
                    var offset = index * 28;
                    var label = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset, 8));
                    var rowId = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 8, 8));
                    var parentLabel = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 16, 8));
                    Assert.True(rankLabels.Add(label), $"Duplicate rank label {label}.");
                    Assert.True(ranksByRowId.TryAdd(rowId, (label, parentLabel)), $"Duplicate rank rowid {rowId}.");
                }
            }
        }

        var namespaceEntries = new List<(long RowId, string FileId, string ParentFileId)>();
        using (var entries = connection.CreateCommand())
        {
            entries.CommandText = "SELECT rowid,file_id,parent_file_id FROM namespace_entries;";
            using var reader = entries.ExecuteReader();
            while (reader.Read())
            {
                namespaceEntries.Add((
                    reader.GetInt64(0),
                    Convert.ToHexString((byte[])reader[1]),
                    Convert.ToHexString((byte[])reader[2])));
            }
        }

        Assert.Equal(namespaceEntries.Count, ranksByRowId.Count);
        var rowIdsByFileId = namespaceEntries.ToDictionary(entry => entry.FileId, entry => entry.RowId, StringComparer.Ordinal);
        foreach (var entry in namespaceEntries)
        {
            var rank = ranksByRowId[entry.RowId];
            if (entry.FileId == entry.ParentFileId)
            {
                Assert.Equal(rank.Label, rank.ParentLabel);
            }
            else
            {
                Assert.True(rowIdsByFileId.TryGetValue(entry.ParentFileId, out var parentRowId), $"Missing namespace parent for rowid {entry.RowId}.");
                Assert.Equal(ranksByRowId[parentRowId].Label, rank.ParentLabel);
            }
        }

        var orderLabels = new List<long>();
        using (var order = connection.CreateCommand())
        {
            order.CommandText = "SELECT entry_count,payload FROM short_query_rank_order_chunks ORDER BY first_sort_key;";
            using var reader = order.ExecuteReader();
            while (reader.Read())
            {
                var count = reader.GetInt32(0);
                var payload = (byte[])reader[1];
                Assert.Equal(count * sizeof(long), payload.Length);
                for (var index = 0; index < count; index++)
                {
                    orderLabels.Add(BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(index * sizeof(long), sizeof(long))));
                }
            }
        }

        Assert.Equal(rankLabels.Count, orderLabels.Count);
        Assert.Equal(rankLabels.Order(), orderLabels.Order());
        Assert.Equal(orderLabels.Count, orderLabels.Distinct().Count());
        Assert.True(orderLabels.Zip(orderLabels.Skip(1), (left, right) => left < right).All(value => value));

        using var postings = connection.CreateCommand();
        postings.CommandText = "SELECT first_label,last_label,posting_count,payload FROM short_query_posting_chunks;";
        using var postingReader = postings.ExecuteReader();
        while (postingReader.Read())
        {
            var labels = DecodePostingLabels((byte[])postingReader[3]);
            Assert.Equal(postingReader.GetInt32(2), labels.Count);
            Assert.Equal(postingReader.GetInt64(0), labels[0]);
            Assert.Equal(postingReader.GetInt64(1), labels[^1]);
            Assert.All(labels, label => Assert.Contains(label, rankLabels));
            Assert.True(labels.Zip(labels.Skip(1), (left, right) => left < right).All(value => value));
        }
    }

    private static List<long> DecodePostingLabels(byte[] payload)
    {
        var labels = new List<long>();
        long previous = 0;
        var offset = 0;
        while (offset < payload.Length)
        {
            ulong delta = 0;
            var shift = 0;
            while (true)
            {
                Assert.True(offset < payload.Length && shift <= 63, "Invalid posting varint.");
                var next = payload[offset++];
                delta |= (ulong)(next & 0x7f) << shift;
                if ((next & 0x80) == 0) break;
                shift += 7;
            }

            Assert.True(delta <= long.MaxValue && previous <= long.MaxValue - (long)delta, "Posting label overflow.");
            previous += (long)delta;
            labels.Add(previous);
        }

        return labels;
    }
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
