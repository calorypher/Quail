using Quail.Core;
using Microsoft.Data.Sqlite;

namespace Quail.Core.Tests;

public sealed class FileSearchRankingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Quail-Search-Ranking-" + Guid.NewGuid());
    private static readonly FileSearchRankingContext Context = new("X:\\Users\\Alice", ["X:\\Windows"]);

    public FileSearchRankingTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Current_user_downloads_outranks_deep_appdata_without_a_downloads_boost()
    {
        var store = Build("downloads.db", sink =>
        {
            var users = AddDirectory(sink, Root, 2, "Users");
            var alice = AddDirectory(sink, users, 3, "Alice");
            var downloads = AddDirectory(sink, alice, 4, "Downloads");
            AddFile(sink, downloads, 5, "download-guide.txt");
            var appData = AddDirectory(sink, alice, 6, "AppData", hidden: true);
            var roaming = AddDirectory(sink, appData, 7, "Roaming");
            AddFile(sink, roaming, 8, "cached-download-history.txt");
        });

        var results = store.Search(new FileSearchQuery("download", SearchEntryType.File), Context);

        Assert.Equal("download-guide.txt", results[0].Name);
        Assert.Equal(FileSearchLocation.CurrentUserVisible, FileSearchRanking.Classify(results[0], "download", Context).Location);
        Assert.Equal(FileSearchLocation.CurrentUserInternal, FileSearchRanking.Classify(results[1], "download", Context).Location);
    }

    [Fact]
    public void Current_user_desktop_outranks_internal_recent_link()
    {
        var store = Build("recent.db", sink =>
        {
            var users = AddDirectory(sink, Root, 2, "Users");
            var alice = AddDirectory(sink, users, 3, "Alice");
            var desktop = AddDirectory(sink, alice, 4, "Desktop");
            AddFile(sink, desktop, 5, "desktop-plan.txt");
            var appData = AddDirectory(sink, alice, 6, "AppData", hidden: true);
            var roaming = AddDirectory(sink, appData, 7, "Roaming");
            var microsoft = AddDirectory(sink, roaming, 8, "Microsoft");
            var windows = AddDirectory(sink, microsoft, 9, "Windows");
            var recent = AddDirectory(sink, windows, 10, "Recent");
            AddFile(sink, recent, 11, "desktop-plan.txt.lnk");
        });

        var results = store.Search(new FileSearchQuery("desktop", SearchEntryType.File), Context);

        Assert.Equal(new[] { "desktop-plan.txt", "desktop-plan.txt.lnk" }, results.Select(result => result.Name));
    }

    [Fact]
    public void Current_user_desktop_outranks_windows_winsxs_infrastructure()
    {
        var store = Build("winsxs.db", sink =>
        {
            var users = AddDirectory(sink, Root, 2, "Users");
            var alice = AddDirectory(sink, users, 3, "Alice");
            var desktop = AddDirectory(sink, alice, 4, "Desktop");
            AddFile(sink, desktop, 5, "desktop-notes.txt");
            var windows = AddDirectory(sink, Root, 6, "Windows");
            var winsxs = AddDirectory(sink, windows, 7, "WinSxS");
            var fileMaps = AddDirectory(sink, winsxs, 8, "FileMaps");
            AddFile(sink, fileMaps, 9, "desktop-infrastructure.dat");
        });

        var results = store.Search(new FileSearchQuery("desktop", SearchEntryType.File), Context);

        Assert.Equal("desktop-notes.txt", results[0].Name);
        Assert.Equal(FileSearchLocation.SystemHeavy, FileSearchRanking.Classify(results[1], "desktop", Context).Location);
    }

    [Fact]
    public void Visible_non_profile_user_space_outranks_current_user_appdata()
    {
        var currentUserStore = Build("current-user.db", sink =>
        {
            var users = AddDirectory(sink, Root, 2, "Users");
            var alice = AddDirectory(sink, users, 3, "Alice");
            var appData = AddDirectory(sink, alice, 4, "AppData", hidden: true);
            var roaming = AddDirectory(sink, appData, 5, "Roaming");
            AddFile(sink, roaming, 6, "download-cache.txt");
        });
        var projectsStore = Build("projects.db", "D:\\", sink =>
        {
            var projects = AddDirectory(sink, Root, 2, "Projects");
            AddFile(sink, projects, 3, "download-guide.txt");
        });

        var results = MultiIndexSearch.Search(
            [currentUserStore, projectsStore],
            new FileSearchQuery("download", SearchEntryType.File),
            Context);

        Assert.Equal(new[] { "download-guide.txt", "download-cache.txt" }, results.Select(result => result.Result.Name));
        Assert.Equal(FileSearchLocation.OtherVisible, FileSearchRanking.Classify(results[0].Result, "download", Context).Location);
        Assert.Equal(FileSearchLocation.CurrentUserInternal, FileSearchRanking.Classify(results[1].Result, "download", Context).Location);
    }

    [Fact]
    public void Candidate_retrieval_does_not_lose_a_useful_prefix_after_more_than_fifty_alphabetical_substrings()
    {
        var store = Build("candidate-cutoff.db", sink =>
        {
            var users = AddDirectory(sink, Root, 2, "Users");
            var alice = AddDirectory(sink, users, 3, "Alice");
            var desktop = AddDirectory(sink, alice, 4, "Desktop");
            AddFile(sink, desktop, 5, "needle-useful.txt");
            for (var index = 0; index < 75; index++)
            {
                AddFile(sink, Root, 100 + index, $"aaa-{index:D3}-needle-noise.txt");
            }
        });

        var results = store.Search(new FileSearchQuery("needle", Limit: 50), Context);

        Assert.Equal(50, results.Count);
        Assert.Equal("needle-useful.txt", results[0].Name);
        Assert.Contains(results, result => result.Name == "needle-useful.txt");
    }

    [Fact]
    public void Token_prefix_separator_semantics_match_bounded_candidate_retrieval()
    {
        var store = Build("token-separator.db", sink =>
        {
            var users = AddDirectory(sink, Root, 2, "Users");
            var alice = AddDirectory(sink, users, 3, "Alice");
            var desktop = AddDirectory(sink, alice, 4, "Desktop");
            AddFile(sink, desktop, 5, "foo!needle.txt");
            for (var index = 0; index < 75; index++)
            {
                AddFile(sink, Root, 100 + index, $"aaa{index:D3}xneedle-noise.txt");
            }
        });

        var results = store.Search(new FileSearchQuery("needle", Limit: 50), Context);

        var tokenPrefix = Assert.Single(results, result => result.Name == "foo!needle.txt");
        Assert.Equal(FileSearchTextMatch.TokenPrefix, FileSearchRanking.Classify(tokenPrefix, "needle", Context).TextMatch);
        Assert.Equal("foo!needle.txt", results[0].Name);
    }

    [Theory]
    [InlineData("a", "ba", "ca", "da", "ea", "a")]
    [InlineData("ks", "bks", "cks", "dks", "eks", "ks")]
    public void Short_query_exact_match_is_not_lost_after_earlier_substring_candidates(
        string query,
        string first,
        string second,
        string third,
        string fourth,
        string exact)
    {
        var store = Build($"short-exact-{query}.db", sink =>
        {
            AddFile(sink, Root, 2, first);
            AddFile(sink, Root, 3, second);
            AddFile(sink, Root, 4, third);
            AddFile(sink, Root, 5, fourth);
            AddFile(sink, Root, 6, exact);
        });

        var result = Assert.Single(store.Search(new FileSearchQuery(query, Limit: 1), Context));

        Assert.Equal(exact, result.Name);
    }

    [Fact]
    public void Short_query_compact_order_preserves_location_text_and_static_rank()
    {
        var store = Build("short-compact-order.db", sink =>
        {
            var users = AddDirectory(sink, Root, 2, "Users");
            var alice = AddDirectory(sink, users, 3, "Alice");
            var desktop = AddDirectory(sink, alice, 4, "Desktop");
            AddFile(sink, desktop, 5, "za-user-substring.txt");
            AddFile(sink, desktop, 6, "xa-user-substring.txt");
            var windows = AddDirectory(sink, Root, 7, "Windows");
            AddFile(sink, windows, 8, "a");
        });

        var results = store.Search(new FileSearchQuery("a", Limit: 4), Context);

        Assert.Equal(
            ["Alice", "xa-user-substring.txt", "za-user-substring.txt", "a"],
            results.Select(result => result.Name));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ks")]
    public void Runtime_location_map_matches_authoritative_parent_walk_for_context_changes(string query)
    {
        var store = Build($"short-runtime-map-{query}.db", sink =>
        {
            var users = AddDirectory(sink, Root, 2, "Users");
            var alice = AddDirectory(sink, users, 3, "Alice");
            var aliceDesktop = AddDirectory(sink, alice, 4, "Desktop");
            AddFile(sink, aliceDesktop, 5, $"{query}-current.txt");
            var appData = AddDirectory(sink, alice, 6, "AppData", hidden: true);
            AddFile(sink, appData, 7, $"{query}-internal.txt");
            var bob = AddDirectory(sink, users, 8, "Bob");
            AddFile(sink, bob, 9, $"{query}-other-user.txt");
            var windows = AddDirectory(sink, Root, 10, "Windows");
            AddFile(sink, windows, 11, $"{query}-system.txt");
            AddFile(sink, Root, 12, $"other-{query}.txt");
        });
        FileSearchRankingContext[] contexts =
        [
            Context,
            new FileSearchRankingContext("x:\\users\\alice", ["x:\\windows"]),
            new FileSearchRankingContext("X:\\Users\\Bob", ["X:\\Windows"]),
            new FileSearchRankingContext(null, ["X:\\Users\\Alice\\Desktop"])
        ];

        using var connection = new SqliteConnection($"Data Source={store.DatabasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        foreach (var context in contexts)
        {
            var authoritative = ShortQueryIndex.SearchAuthoritative(connection, query, 50, context);
            var optimized = ShortQueryIndex.Search(connection, query, 50, context);

            Assert.Equal(authoritative.Select(result => result.FileId), optimized.Select(result => result.FileId));
        }
    }

    [Fact]
    public void Short_query_ascii_case_variants_share_one_posting_order()
    {
        var store = Build("short-ascii-case-order.db", sink =>
        {
            AddFile(sink, Root, 2, "A1");
            AddFile(sink, Root, 3, "a2");
            AddFile(sink, Root, 4, "a3");
            AddFile(sink, Root, 5, "A4");
        });

        var results = store.Search(new FileSearchQuery("a", Limit: 2), Context);

        Assert.Equal(["A1", "a2"], results.Select(result => result.Name));
    }

    [Fact]
    public void Short_query_ascii_canonicalization_preserves_non_ascii_literal_substrings()
    {
        var store = Build("short-non-ascii-literal.db", sink =>
        {
            AddFile(sink, Root, 2, "Ąą");
        });

        var result = Assert.Single(store.Search(new FileSearchQuery("ą", Limit: 1), Context));

        Assert.Equal("Ąą", result.Name);
    }

    [Fact]
    public void Short_query_retains_late_exact_candidates_across_posting_chunks()
    {
        var store = Build("short-chunk-recall.db", sink =>
        {
            for (var index = 0; index < 1_100; index++)
            {
                AddFile(sink, Root, index + 2, $"b{index:D4}a");
            }

            AddFile(sink, Root, 2_000, "a");
        });

        var results = store.Search(new FileSearchQuery("a", Limit: 1_000), Context);

        Assert.Equal(1_000, results.Count);
        Assert.Equal("a", results[0].Name);
        Assert.Contains(results, result => result.Name == "b0000a");
        Assert.Contains(results, result => result.Name == "b0998a");
    }

    [Fact]
    public void Text_match_quality_is_exact_then_prefix_then_token_prefix_then_substring()
    {
        var store = Build("text-quality.db", sink =>
        {
            var users = AddDirectory(sink, Root, 2, "Users");
            var alice = AddDirectory(sink, users, 3, "Alice");
            var desktop = AddDirectory(sink, alice, 4, "Desktop");
            AddFile(sink, desktop, 5, "query");
            AddFile(sink, desktop, 6, "query-prefix.txt");
            AddFile(sink, desktop, 7, "word_query.txt");
            AddFile(sink, desktop, 8, "subquery.txt");
        });

        var results = store.Search(new FileSearchQuery("query"), Context);

        Assert.Equal(
            new[] { "query", "query-prefix.txt", "word_query.txt", "subquery.txt" },
            results.Select(result => result.Name));
        Assert.Equal(
            new[] { FileSearchTextMatch.Exact, FileSearchTextMatch.Prefix, FileSearchTextMatch.TokenPrefix, FileSearchTextMatch.Substring },
            results.Select(result => FileSearchRanking.Classify(result, "query", Context).TextMatch));
    }

    private IndexStore Build(string fileName, Action<Action<NamespaceRecord>> produce) => Build(fileName, "X:\\", produce);

    private IndexStore Build(string fileName, string volumeRoot, Action<Action<NamespaceRecord>> produce)
    {
        var store = new IndexStore(Path.Combine(_directory, fileName));
        store.BuildFromRecords(
            new VolumeDescriptor(fileName, volumeRoot, "NTFS", "Search ranking test"),
            sink =>
            {
                sink(new NamespaceRecord(Root, Root, "", 0x10, 0, 2));
                produce(sink);
            },
            checkpoint: new IncrementalCheckpoint(1, 2, 0, 0));
        return store;
    }

    private static NativeFileId Root => Id(1);

    private static NativeFileId AddDirectory(Action<NamespaceRecord> sink, NativeFileId parent, int id, string name, bool hidden = false)
    {
        var fileId = Id(id);
        sink(new NamespaceRecord(fileId, parent, name, 0x10 | (hidden ? 0x2u : 0), 0, 2));
        return fileId;
    }

    private static void AddFile(Action<NamespaceRecord> sink, NativeFileId parent, int id, string name) =>
        sink(new NamespaceRecord(Id(id), parent, name, 0, 0, 2));

    private static NativeFileId Id(int value) => new(BitConverter.GetBytes((long)value));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
