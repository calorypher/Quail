using Microsoft.Data.Sqlite;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class FileSearchTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Quail-M04-" + Guid.NewGuid());
    private readonly NativeFileId _root = Id("0000000000000001");
    private readonly NativeFileId _documents = Id("0000000000000002");
    private readonly NativeFileId _report = Id("0000000000000003");
    private readonly NativeFileId _summary = Id("0000000000000004");
    private readonly NativeFileId _reportDirectory = Id("0000000000000005");

    public FileSearchTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Case_insensitive_partial_search_matches_indexed_names_only()
    {
        Build();

        var results = Store.Search(new FileSearchQuery("rEpOrT"));

        Assert.Equal(new[] { "report-archive", "Quarterly Report.PDF" }, results.Select(result => result.Name));
        Assert.All(results, result => Assert.Contains("Documents", result.FullPath!));
    }

    [Fact]
    public void No_match_returns_an_empty_result_set()
    {
        Build();

        Assert.Empty(Store.Search(new FileSearchQuery("not-present")));
    }

    [Fact]
    public void One_and_two_character_partial_queries_remain_correct()
    {
        Build();

        var firstShortQuery = Store.Search(new FileSearchQuery("r", Limit: 1));
        var repeatedShortQuery = Store.Search(new FileSearchQuery("r", Limit: 1));

        Assert.Single(firstShortQuery);
        Assert.Equal(firstShortQuery.Select(result => result.FileId), repeatedShortQuery.Select(result => result.FileId));
        Assert.All(firstShortQuery, result => Assert.Contains("r", result.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Store.Search(new FileSearchQuery("r")), result => result.Name == "Quarterly Report.PDF");
        Assert.Contains(Store.Search(new FileSearchQuery("RE")), result => result.Name == "Quarterly Report.PDF");
    }

    [Fact]
    public void Type_and_extension_filters_are_applied_by_the_persistent_query()
    {
        Build();

        Assert.Equal(new[] { "Quarterly Report.PDF" }, Store.Search(new FileSearchQuery("report", SearchEntryType.File)).Select(result => result.Name));
        Assert.Equal(new[] { "report-archive" }, Store.Search(new FileSearchQuery("report", SearchEntryType.Directory)).Select(result => result.Name));
        var extension = Store.Search(new FileSearchQuery("report", Extension: ".pdf"));
        var result = Assert.Single(extension);
        Assert.Equal("Quarterly Report.PDF", result.Name);
        Assert.Equal("PDF", result.Extension);
    }

    [Fact]
    public void Limit_and_ordering_are_bounded_and_deterministic()
    {
        Build();

        var first = Store.Search(new FileSearchQuery("report", Limit: 1));
        var second = Store.Search(new FileSearchQuery("report", Limit: 1));

        Assert.Equal(new[] { "Quarterly Report.PDF" }, first.Select(result => result.Name));
        Assert.Equal(first.Select(result => result.FileId), second.Select(result => result.FileId));
        Assert.Throws<ArgumentOutOfRangeException>(() => Store.Search(new FileSearchQuery("report", Limit: 0)));
        Assert.Throws<ArgumentException>(() => Store.Search(new FileSearchQuery("report", Extension: ".")));
        Assert.Throws<ArgumentException>(() => Store.Search(new FileSearchQuery("report", Extension: "p%f")));
    }

    [Fact]
    public void Search_refuses_absent_incomplete_and_rebuild_required_indexes()
    {
        Assert.Throws<InvalidOperationException>(() => Store.Search(new FileSearchQuery("report")));

        Assert.Throws<InvalidOperationException>(() => Store.BuildFromRecords(Volume, Produce, failAfterRecords: 1));
        Assert.Throws<InvalidOperationException>(() => Store.Search(new FileSearchQuery("report")));

        Build();
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE metadata SET value='1' WHERE key='schema_version'";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(() => Store.Search(new FileSearchQuery("report")));

        Build();
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM metadata WHERE key='search_index_format'";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(() => Store.Search(new FileSearchQuery("report")));
    }

    [Fact]
    public void Status_marks_missing_search_format_rebuild_required()
    {
        Build();
        using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM metadata WHERE key='search_index_format'";
        command.ExecuteNonQuery();
        Assert.Equal(IndexState.RebuildRequired, Store.GetStatus().State);
    }

    [Fact]
    public void Committed_incremental_create_rename_and_delete_are_visible_to_search()
    {
        Build();
        var created = Id("0000000000000006");
        var journal = new UsnJournalState(0x1234567890ABCDEF, 10, 100, 5, 2, 3);

        Store.ApplyParsedBatchesForTesting(Volume, journal, new[]
        {
            Batch(200, new JournalRecord(new NamespaceRecord(created, _documents, "draft-report.txt", 0, 150, 2), UsnReason.FileCreate)),
        });
        Assert.Contains(Store.Search(new FileSearchQuery("draft")), result => result.Name == "draft-report.txt");

        Store.ApplyParsedBatchesForTesting(Volume, journal, new[]
        {
            Batch(300, new JournalRecord(new NamespaceRecord(created, _documents, "final-report.txt", 0, 250, 2), UsnReason.RenameNewName)),
        });
        Assert.Empty(Store.Search(new FileSearchQuery("draft")));
        Assert.Contains(Store.Search(new FileSearchQuery("final")), result => result.Name == "final-report.txt");

        Store.ApplyParsedBatchesForTesting(Volume, journal, new[]
        {
            Batch(400, new JournalRecord(new NamespaceRecord(created, _documents, "final-report.txt", 0, 350, 2), UsnReason.FileDelete)),
        });
        Assert.Empty(Store.Search(new FileSearchQuery("final")));
    }

    [Fact]
    public void Auxiliary_search_state_rolls_back_with_a_failed_incremental_batch()
    {
        Build();
        var journal = new UsnJournalState(0x1234567890ABCDEF, 10, 100, 5, 2, 3);
        var rename = Batch(200, new JournalRecord(new NamespaceRecord(_report, _documents, "renamed-report.pdf", 0, 150, 2), UsnReason.RenameNewName));

        Assert.Throws<InvalidOperationException>(() => Store.ApplyParsedBatchesForTesting(Volume, journal, new[] { rename }, failBeforeCommit: true));

        Assert.Contains(Store.Search(new FileSearchQuery("quarterly")), result => result.Name == "Quarterly Report.PDF");
        Assert.Empty(Store.Search(new FileSearchQuery("renamed")));
    }

    private IndexStore Store => new(DatabasePath);
    private string DatabasePath => Path.Combine(_directory, "index.db");
    private VolumeDescriptor Volume => new("\\\\?\\Volume{test}", "X:\\", "NTFS", "Test");
    private void Build() => Store.BuildFromRecords(Volume, Produce, checkpoint: new IncrementalCheckpoint(0x1234567890ABCDEF, 100, 10, 5));
    private void Produce(Action<NamespaceRecord> sink)
    {
        sink(new NamespaceRecord(_root, _root, "", 16, 0, 2));
        sink(new NamespaceRecord(_documents, _root, "Documents", 16, 0, 2));
        sink(new NamespaceRecord(_report, _documents, "Quarterly Report.PDF", 0, 0, 2));
        sink(new NamespaceRecord(_summary, _documents, "summary.txt", 0, 0, 2));
        sink(new NamespaceRecord(_reportDirectory, _documents, "report-archive", 16, 0, 2));
    }
    private static JournalBatch Batch(long nextUsn, params JournalRecord[] records) => new(nextUsn, records);
    private static NativeFileId Id(string hex) => new(Convert.FromHexString(hex));
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
