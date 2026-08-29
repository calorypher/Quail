using Quail.Core;

namespace Quail.Core.Tests;

public sealed class MultiIndexSearchTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Quail-M06-" + Guid.NewGuid());

    public MultiIndexSearchTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Global_order_limit_and_source_identity_are_deterministic()
    {
        var first = Build("one.db", "Zeta.txt", "Alpha.txt");
        var second = Build("two.db", "alpha.txt", "Beta.txt");
        var query = new FileSearchQuery("ta", Limit: 2);

        var results = MultiIndexSearch.Search(new[] { first, second }, query);

        Assert.Equal(2, results.Count);
        Assert.Equal(new[] { "Beta.txt", "Zeta.txt" }, results.Select(result => result.Result.Name));
        Assert.All(results, result => Assert.Contains(".db", result.SourceIdentity, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Equal_names_and_file_ids_remain_distinct_by_source()
    {
        var first = Build("one.db", "duplicate.txt");
        var second = Build("two.db", "duplicate.txt");

        var results = MultiIndexSearch.Search(new[] { first, second }, new FileSearchQuery("duplicate", Limit: 2));

        Assert.Equal(2, results.Count);
        Assert.Equal(results[0].Result.FileId, results[1].Result.FileId);
        Assert.NotEqual(results[0].SourceIdentity, results[1].SourceIdentity);
        Assert.True(string.CompareOrdinal(results[0].SourceIdentity, results[1].SourceIdentity) < 0);
    }

    [Fact]
    public void Ranking_keeps_global_limit_source_identity_and_order_independent_of_input_order()
    {
        var first = Build("ranking-one.db", "needle.txt", "alpha-needle.txt");
        var second = Build("ranking-two.db", "needle.txt", "beta-needle.txt");
        var query = new FileSearchQuery("needle", Limit: 3);

        var forward = MultiIndexSearch.Search([first, second], query);
        var reverse = MultiIndexSearch.Search([second, first], query);

        Assert.Equal(3, forward.Count);
        Assert.Equal(
            forward.Select(result => (result.SourceIdentity, result.Result.FileId, result.Result.Name)),
            reverse.Select(result => (result.SourceIdentity, result.Result.FileId, result.Result.Name)));
        Assert.Equal(2, forward.Count(result => result.Result.Name == "needle.txt"));
        Assert.NotEqual(
            forward.First(result => result.Result.Name == "needle.txt").SourceIdentity,
            forward.Last(result => result.Result.Name == "needle.txt").SourceIdentity);
    }

    [Fact]
    public void Global_prefix_uses_sqlite_nocase_then_utf8_binary_order()
    {
        var first = Build("unicode-one.db", "å.txt", "Å.txt");
        var second = Build("unicode-two.db", "Z.txt");
        var results = MultiIndexSearch.Search(new[] { first, second }, new FileSearchQuery(".txt", Limit: 2));
        Assert.Equal(new[] { "Z.txt", "Å.txt" }, results.Select(result => result.Result.Name));
    }

    [Fact]
    public void Global_bounded_prefix_uses_sqlite_utf8_order_for_supplementary_unicode()
    {
        var first = Build("unicode-private-use.db", "\uE000.txt", "\U00010000.txt");
        var second = Build("unicode-supplementary.db", "\U00010001.txt");

        var results = MultiIndexSearch.Search(
            new[] { first, second },
            new FileSearchQuery(".txt", Limit: 2));

        Assert.Equal(
            new[] { "\uE000.txt", "\U00010000.txt" },
            results.Select(result => result.Result.Name));
    }

    [Fact]
    public void Global_order_folds_ascii_and_uses_utf8_binary_tie_break()
    {
        var first = Build("ascii-one.db", "a.txt", "B.txt");
        var second = Build("ascii-two.db", "A.txt");

        var results = MultiIndexSearch.Search(
            new[] { first, second },
            new FileSearchQuery(".txt", Limit: 3));

        Assert.Equal(
            new[] { "A.txt", "a.txt", "B.txt" },
            results.Select(result => result.Result.Name));
    }

    [Fact]
    public void Invalid_requested_index_fails_the_whole_search()
    {
        var complete = Build("complete.db", "report.txt");
        var absent = new IndexStore(Path.Combine(_directory, "absent.db"));

        Assert.Throws<InvalidOperationException>(() => MultiIndexSearch.Search(new[] { complete, absent }, new FileSearchQuery("report")));
    }

    [Fact]
    public void Open_uses_resolved_path_and_reports_missing_paths()
    {
        var store = Build("open.db", "report.txt");
        var result = Assert.Single(store.Search(new FileSearchQuery("report")));
        var shell = new RecordingShell();
        new IndexedEntryOpener(shell, _ => true).Open(store, result.FileId);
        Assert.Equal(result.FullPath, shell.Path);

        var missing = new IndexedEntryOpener(shell, _ => false);
        Assert.Throws<FileNotFoundException>(() => missing.Open(store, result.FileId));
        Assert.Throws<InvalidOperationException>(() => missing.Open(store, Id("0102030405060708")));
    }

    private IndexStore Build(string databaseName, params string[] names)
    {
        var store = new IndexStore(Path.Combine(_directory, databaseName));
        var root = Id("0000000000000001");
        store.BuildFromRecords(new VolumeDescriptor($"\\\\?\\Volume{{{databaseName}}}", "X:\\", "NTFS", "Test"), sink =>
        {
            sink(new NamespaceRecord(root, root, "", 16, 0, 2));
            for (var index = 0; index < names.Length; index++)
            {
                sink(new NamespaceRecord(Id($"000000000000{index + 2:X4}"), root, names[index], 0, 0, 2));
            }
        }, checkpoint: new IncrementalCheckpoint(1, 2, 0, 0));
        return store;
    }

    private static NativeFileId Id(string hex) => new(Convert.FromHexString(hex));
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class RecordingShell : IWindowsShellLauncher
    {
        public string? Path { get; private set; }
        public void Open(string path) => Path = path;
    }
}
