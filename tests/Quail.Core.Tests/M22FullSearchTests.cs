using System.Collections.Concurrent;
using Quail.App;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class M22FullSearchTests : IDisposable
{
    private const long TimeOne = 133_000_000_000_000_000;
    private const long TimeTwo = TimeOne + 10_000_000;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Quail-M22-" + Guid.NewGuid().ToString("N"));

    public M22FullSearchTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Core_details_are_optional_typed_and_do_not_break_a_basic_fake_source()
    {
        var source = new CapturingSource();
        var service = new SearchApplicationService([source]);

        var basic = Assert.Single(service.Search(new SearchRequest("basic")));
        var details = new FakeRequestDetails("structured");
        var structured = Assert.Single(service.Search(new SearchRequest("full", 1, details)));

        Assert.Equal("basic", basic.Title);
        Assert.Equal("full", structured.Title);
        Assert.Null(source.Requests[0].Details);
        Assert.Same(details, source.Requests[1].Details);
        Assert.False(service.CanReveal(basic.Action));
        Assert.False(service.CanCopyText(basic.Action));
        Assert.DoesNotContain(
            typeof(SearchApplicationService).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "Quail.FileSystem");
    }

    [Fact]
    public void Quick_and_full_requests_share_one_core_search_service()
    {
        var source = new CapturingSource();
        var service = new SearchApplicationService([source]);
        using var runtime = new SearchRuntime(
            service,
            () => true,
            () => { },
            createFullSearchRequest: (query, limit, _) =>
                new SearchRequest(query, limit, new FakeRequestDetails("full")));

        runtime.Search.Search(new SearchRequest("quick"));
        runtime.Search.Search(runtime.CreateFullSearchRequest(
            "full",
            FullSearchWindowLayout.ResultLimit,
            new FullSearchCriteria(
                FullSearchEntryType.All,
                null,
                null,
                null,
                null,
                null,
                false,
                false,
                false,
                FullSearchSortField.Relevance,
                FullSearchSortDirection.Ascending)));

        Assert.Same(service, runtime.Search);
        Assert.Equal(["quick", "full"], source.Requests.Select(request => request.Query));
        Assert.Null(source.Requests[0].Details);
        Assert.Equal("full", Assert.IsType<FakeRequestDetails>(source.Requests[1].Details).Value);
        Assert.Equal(FullSearchWindowLayout.ResultLimit, source.Requests[1].Limit);
    }

    [Fact]
    public void Filesystem_details_apply_filters_and_project_structured_values_through_core()
    {
        var store = BuildFilterStore("filters.db");
        var source = new FileSystemSearchSource(() => [store.DatabasePath]);
        var service = new SearchApplicationService([source]);

        var filtered = Assert.Single(service.Search(new SearchRequest(
            "item",
            100,
            new FileSystemSearchRequestDetails(
                SearchEntryType.File,
                ".TXT",
                MinimumSize: 10,
                MaximumSize: 10,
                ModifiedAfterUtcFileTime: TimeOne,
                ModifiedBeforeUtcFileTime: TimeOne,
                Hidden: true,
                ReadOnly: true,
                System: true))));
        var fields = Assert.IsType<FileSystemSearchResultDetails>(filtered.Details);

        Assert.Equal("hidden-item.TXT", filtered.Title);
        Assert.Equal(@"X:\folder\hidden-item.TXT", fields.FullPath);
        Assert.False(fields.IsDirectory);
        Assert.Equal(10, fields.LogicalSize);
        Assert.Equal(TimeOne, fields.LastWriteTimeUtcFileTime);
        Assert.Equal(7u, fields.Attributes);
        Assert.Single(service.Search(new SearchRequest(
            "item-folder",
            100,
            new FileSystemSearchRequestDetails(SearchEntryType.Directory))));
        Assert.Empty(service.Search(new SearchRequest(
            "unknown-item",
            100,
            new FileSystemSearchRequestDetails(MinimumSize: 0))));
        Assert.Throws<ArgumentException>(() => service.Search(new SearchRequest(
            "item",
            100,
            new FileSystemSearchRequestDetails(MinimumSize: 11, MaximumSize: 10))));
    }

    [Fact]
    public void Filesystem_filter_boundaries_work_individually_and_compose_across_indexes()
    {
        var first = BuildFilterStore("filter-one.db");
        var second = BuildSortStore("filter-two.db", @"Y:\", [
            new SortEntry("other-item.txt", 30, TimeTwo + 10_000_000)
        ]);
        var source = new FileSystemSearchSource(() => [first.DatabasePath, second.DatabasePath]);
        var service = new SearchApplicationService([source]);

        Assert.Equal(
            ["hidden-item.TXT"],
            Search(service, new FileSystemSearchRequestDetails(Extension: "txt", MaximumSize: 10)));
        Assert.Equal(
            ["normal-item.log", "other-item.txt"],
            Search(service, new FileSystemSearchRequestDetails(MinimumSize: 20)));
        Assert.Equal(
            ["hidden-item.TXT", "hidden-only-item.txt", "read-only-item.txt", "system-only-item.txt"],
            Search(service, new FileSystemSearchRequestDetails(ModifiedBeforeUtcFileTime: TimeOne)));
        Assert.Equal(
            ["normal-item.log", "other-item.txt"],
            Search(service, new FileSystemSearchRequestDetails(ModifiedAfterUtcFileTime: TimeTwo)));
        Assert.Equal(
            ["hidden-item.TXT", "hidden-only-item.txt"],
            Search(service, new FileSystemSearchRequestDetails(Hidden: true)));
        Assert.Equal(
            ["hidden-item.TXT", "system-only-item.txt"],
            Search(service, new FileSystemSearchRequestDetails(System: true)));
        Assert.Equal(
            ["hidden-item.TXT", "read-only-item.txt"],
            Search(service, new FileSystemSearchRequestDetails(ReadOnly: true)));
        Assert.Throws<ArgumentException>(() => service.Search(new SearchRequest(
            "item",
            100,
            new FileSystemSearchRequestDetails(
                ModifiedAfterUtcFileTime: TimeTwo,
                ModifiedBeforeUtcFileTime: TimeOne))));
    }

    [Fact]
    public void Filesystem_result_actions_open_reveal_and_copy_without_a_global_registry()
    {
        var store = BuildFilterStore("actions.db");
        var shell = new RecordingShell();
        var source = new FileSystemSearchSource(
            () => [store.DatabasePath],
            new IndexedEntryOpener(shell, _ => true));
        var service = new SearchApplicationService([source]);
        var result = Assert.Single(service.Search(new SearchRequest(
            "hidden-item",
            10,
            new FileSystemSearchRequestDetails())));

        service.Open(result.Action);
        service.Reveal(result.Action);
        var copied = service.GetCopyText(result.Action);

        Assert.Equal(@"X:\folder\hidden-item.TXT", shell.OpenedPath);
        Assert.Equal(@"X:\folder\hidden-item.TXT", shell.RevealedPath);
        Assert.False(shell.RevealedDirectory);
        Assert.Equal(@"X:\folder\hidden-item.TXT", copied);
        Assert.Empty(typeof(SearchResultAction).GetProperties());
    }

    [Fact]
    public void Field_sorts_are_global_deterministic_and_keep_nulls_last()
    {
        var first = BuildSortStore("one.db", @"X:\", [
            new SortEntry("sort-b.txt", 20, TimeTwo),
            new SortEntry("sort-null.txt", null, null)
        ]);
        var second = BuildSortStore("two.db", @"Y:\", [
            new SortEntry("sort-A.txt", 10, TimeOne),
            new SortEntry("sort-c.txt", 30, TimeTwo + 10_000_000)
        ]);
        var stores = new[] { first, second };

        AssertNames(stores, FileSearchSortField.Name, FileSearchSortDirection.Ascending,
            "sort-A.txt", "sort-b.txt", "sort-c.txt", "sort-null.txt");
        AssertNames(stores, FileSearchSortField.Name, FileSearchSortDirection.Descending,
            "sort-null.txt", "sort-c.txt", "sort-b.txt", "sort-A.txt");
        AssertNames(stores, FileSearchSortField.Size, FileSearchSortDirection.Ascending,
            "sort-A.txt", "sort-b.txt", "sort-c.txt", "sort-null.txt");
        AssertNames(stores, FileSearchSortField.Size, FileSearchSortDirection.Descending,
            "sort-c.txt", "sort-b.txt", "sort-A.txt", "sort-null.txt");
        AssertNames(stores, FileSearchSortField.Modified, FileSearchSortDirection.Ascending,
            "sort-A.txt", "sort-b.txt", "sort-c.txt", "sort-null.txt");
        AssertNames(stores, FileSearchSortField.Modified, FileSearchSortDirection.Descending,
            "sort-c.txt", "sort-b.txt", "sort-A.txt", "sort-null.txt");

        var pathAscending = Search(stores, FileSearchSortField.Path, FileSearchSortDirection.Ascending);
        var pathDescending = Search(stores, FileSearchSortField.Path, FileSearchSortDirection.Descending);
        Assert.Equal(pathAscending.Select(item => item.Result.FullPath).Reverse(), pathDescending.Select(item => item.Result.FullPath));
        Assert.Equal(
            Search(stores, FileSearchSortField.Size, FileSearchSortDirection.Ascending)
                .Select(item => (item.SourceIdentity, item.Result.FileId)),
            Search(stores.Reverse(), FileSearchSortField.Size, FileSearchSortDirection.Ascending)
                .Select(item => (item.SourceIdentity, item.Result.FileId)));
        Assert.Equal(2, MultiIndexSearch.Search(
            stores,
            new FileSearchQuery("sort", Limit: 2, SortField: FileSearchSortField.Size)).Count);
    }

    [Fact]
    public void Explicit_relevance_sort_preserves_the_existing_order()
    {
        var store = BuildSortStore("relevance.db", @"X:\", [
            new SortEntry("sort.txt", 1, TimeOne),
            new SortEntry("alpha-sort.txt", 2, TimeTwo)
        ]);

        var existing = MultiIndexSearch.Search([store], new FileSearchQuery("sort"));
        var explicitRelevance = MultiIndexSearch.Search([
            store
        ], new FileSearchQuery(
            "sort",
            SortField: FileSearchSortField.Relevance,
            SortDirection: FileSearchSortDirection.Descending));

        Assert.Equal(existing.Select(item => item.Result.FileId), explicitRelevance.Select(item => item.Result.FileId));
    }

    [Fact]
    public void Relevance_candidates_keep_the_m18_lightweight_projection()
    {
        var projection = IndexStore.GetSearchCandidateProjection(
            FileSearchSortField.Relevance,
            "namespace_entries");

        Assert.Equal("namespace_entries.rowid, namespace_entries.name", projection);
        Assert.DoesNotContain("file_id", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("logical_size", projection, StringComparison.Ordinal);
        Assert.DoesNotContain("last_write_time_utc", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void Equal_field_values_use_file_id_then_source_identity_globally()
    {
        var alpha = BuildSortStore("alpha.db", @"X:\\", [
            new SortEntry("sort-equal.txt", 10, TimeOne),
            new SortEntry("sort-equal.txt", 10, TimeOne),
            new SortEntry("sort-equal.txt", 10, TimeOne)
        ]);
        var beta = BuildSortStore("beta.db", @"Y:\\", [
            new SortEntry("sort-equal.txt", 10, TimeOne),
            new SortEntry("sort-equal.txt", 10, TimeOne),
            new SortEntry("sort-equal.txt", 10, TimeOne)
        ]);

        var results = MultiIndexSearch.Search(
            [beta, alpha],
            new FileSearchQuery("sort", Limit: 3, SortField: FileSearchSortField.Size));

        Assert.Equal(alpha.DatabasePath, results[0].SourceIdentity);
        Assert.Equal(beta.DatabasePath, results[1].SourceIdentity);
        Assert.Equal(alpha.DatabasePath, results[2].SourceIdentity);
        Assert.Equal(results[0].Result.FileId, results[1].Result.FileId);
        Assert.True(StringComparer.Ordinal.Compare(
            results[0].Result.FileId.ToString(),
            results[2].Result.FileId.ToString()) < 0);
    }

    [Fact]
    public void Local_calendar_day_conversion_is_inclusive_and_dst_sensitive()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        var date = new DateOnly(2024, 3, 10);

        var bounds = FullSearchDateRange.ToUtcFileTimeBounds(date, date, zone);
        var start = DateTime.FromFileTimeUtc(bounds.FromUtcFileTime!.Value);
        var endExclusive = DateTime.FromFileTimeUtc(bounds.ToUtcFileTime!.Value).AddTicks(1);

        Assert.Equal(TimeSpan.FromHours(23), endExclusive - start);
        Assert.Equal(date, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, zone)));
        Assert.Equal(date.AddDays(1), DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(endExclusive, zone)));
    }

    [Fact]
    public void Criteria_validation_uses_human_units_and_rejects_invalid_ranges()
    {
        Assert.True(FullSearchCriteriaFactory.TryCreate(
            FullSearchEntryType.Files,
            ".pdf",
            1.5,
            FullSearchSizeUnit.MB,
            2,
            FullSearchSizeUnit.GB,
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            true,
            false,
            true,
            FullSearchSortField.Size,
            FullSearchSortDirection.Descending,
            out var criteria,
            out var error));
        Assert.Null(error);
        Assert.Equal("pdf", criteria!.Extension);
        Assert.Equal(1_572_864, criteria!.MinimumSize);
        Assert.Equal(2_147_483_648, criteria.MaximumSize);

        Assert.False(FullSearchCriteriaFactory.TryCreate(
            FullSearchEntryType.All,
            null,
            2,
            FullSearchSizeUnit.GB,
            1,
            FullSearchSizeUnit.GB,
            null,
            null,
            false,
            false,
            false,
            FullSearchSortField.Relevance,
            FullSearchSortDirection.Ascending,
            out _,
            out var rangeError));
        Assert.Equal("Minimum size must not exceed maximum size.", rangeError);
    }

    [Fact]
    public void Relevance_direction_uses_the_default_presentation()
    {
        Assert.False(FullSearchSortPresentation.IsDirectionEnabled(FullSearchSortField.Relevance));
        Assert.Equal("Default", FullSearchSortPresentation.GetDirectionLabel(
            FullSearchSortField.Relevance,
            descending: true));
        Assert.True(FullSearchSortPresentation.IsDirectionEnabled(FullSearchSortField.Size));
        Assert.Equal("↑ Ascending", FullSearchSortPresentation.GetDirectionLabel(
            FullSearchSortField.Size,
            descending: false));
    }

    [Fact]
    public async Task Structured_coordinator_request_supersedes_same_text_with_older_filters()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var completionSignal = new SemaphoreSlim(0);
        var completed = new ConcurrentQueue<SearchCompletion>();
        using var coordinator = LatestSearchCoordinator.ForRequests(request =>
        {
            if (((FakeRequestDetails?)request.Details)?.Value == "old")
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
            return [FakeResult(((FakeRequestDetails?)request.Details)?.Value ?? "none")];
        });
        coordinator.Completed += completion =>
        {
            completed.Enqueue(completion);
            completionSignal.Release();
        };

        coordinator.Request(new SearchRequest("same", Details: new FakeRequestDetails("old")), 1);
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        coordinator.Request(new SearchRequest("same", Details: new FakeRequestDetails("new")), 2);
        releaseFirst.Set();

        await completionSignal.WaitAsync(TimeSpan.FromSeconds(2));
        await completionSignal.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Contains(completed, item => !item.IsCurrent && item.Results!.Single().Title == "old");
        Assert.Contains(completed, item => item.IsCurrent && item.Results!.Single().Title == "new");
    }

    [Fact]
    public void Surface_lifecycle_preserves_queries_singleton_and_close_semantics()
    {
        Assert.True(FullSearchLifecycle.ShouldCreateWindow(hasWindow: false));
        Assert.False(FullSearchLifecycle.ShouldCreateWindow(hasWindow: true));
        Assert.Equal("  exact query  ", FullSearchLifecycle.TransferQuery("  exact query  "));
        Assert.True(FullSearchLifecycle.ShouldShowQuickSearch(FullSearchDismissKind.Collapse));
        Assert.False(FullSearchLifecycle.ShouldShowQuickSearch(FullSearchDismissKind.NativeClose));
        Assert.Equal(FullSearchInputState.EmptyQuery, FullSearchInputPolicy.Evaluate(" ", true, true));
        Assert.Equal(FullSearchInputState.NoSource, FullSearchInputPolicy.Evaluate("query", false, true));
        Assert.Equal(FullSearchInputState.InvalidFilters, FullSearchInputPolicy.Evaluate("query", true, false));
        Assert.Equal(FullSearchInputState.Ready, FullSearchInputPolicy.Evaluate("query", true, true));
        Assert.Equal(1_000, FullSearchWindowLayout.ResultLimit);
        Assert.Equal(new PhysicalSize(1180, 760), FullSearchWindowLayout.InitialSizeToPhysical(96));
        Assert.Equal(new PhysicalSize(820, 560), FullSearchWindowLayout.MinimumSizeToPhysical(96));
    }

    private IndexStore BuildFilterStore(string databaseName)
    {
        var store = new IndexStore(Path.Combine(_directory, databaseName));
        var root = Id(1);
        var folder = Id(2);
        store.BuildFromRecords(
            new VolumeDescriptor("filter-volume", "X:\\", "NTFS", "Test"),
            sink =>
            {
                sink(new NamespaceRecord(root, root, string.Empty, 0x10, 0, 2));
                sink(new NamespaceRecord(folder, root, "folder", 0x10, 0, 2));
                sink(new NamespaceRecord(Id(3), folder, "hidden-item.TXT", 0x7, 0, 2));
                sink(new NamespaceRecord(Id(4), folder, "normal-item.log", 0, 0, 2));
                sink(new NamespaceRecord(Id(5), folder, "unknown-item.bin", 0, 0, 2));
                sink(new NamespaceRecord(Id(6), root, "item-folder", 0x10, 0, 2));
                sink(new NamespaceRecord(Id(7), folder, "hidden-only-item.txt", 0x2, 0, 2));
                sink(new NamespaceRecord(Id(8), folder, "system-only-item.txt", 0x4, 0, 2));
                sink(new NamespaceRecord(Id(9), folder, "read-only-item.txt", 0x1, 0, 2));
            },
            checkpoint: new IncrementalCheckpoint(1, 2, 0, 0),
            acquireMetadata: record => record.Name switch
            {
                "hidden-item.TXT" => new FileMetadata(10, TimeOne),
                "normal-item.log" => new FileMetadata(20, TimeTwo),
                "hidden-only-item.txt" => new FileMetadata(11, TimeOne),
                "system-only-item.txt" => new FileMetadata(12, TimeOne),
                "read-only-item.txt" => new FileMetadata(13, TimeOne),
                _ => new FileMetadata(null, null)
            });
        return store;
    }

    private IndexStore BuildSortStore(string databaseName, string mount, IReadOnlyList<SortEntry> entries)
    {
        var store = new IndexStore(Path.Combine(_directory, databaseName));
        var root = Id(Math.Abs(databaseName.GetHashCode()) + 10_000L);
        store.BuildFromRecords(
            new VolumeDescriptor(databaseName, mount, "NTFS", "Test"),
            sink =>
            {
                sink(new NamespaceRecord(root, root, string.Empty, 0x10, 0, 2));
                for (var index = 0; index < entries.Count; index++)
                {
                    sink(new NamespaceRecord(Id(index + 100), root, entries[index].Name, 0, 0, 2));
                }
            },
            checkpoint: new IncrementalCheckpoint(1, 2, 0, 0),
            acquireMetadata: record =>
            {
                var entry = entries.FirstOrDefault(item => item.Name == record.Name);
                return entry is null
                    ? new FileMetadata(null, null)
                    : new FileMetadata(entry.Size, entry.Modified);
            });
        return store;
    }

    private static IReadOnlyList<IndexedFileSearchResult> Search(
        IEnumerable<IndexStore> stores,
        FileSearchSortField field,
        FileSearchSortDirection direction) => MultiIndexSearch.Search(
            stores,
            new FileSearchQuery("sort", Limit: 10, SortField: field, SortDirection: direction));

    private static string[] Search(SearchApplicationService service, FileSystemSearchRequestDetails details) =>
        service.Search(new SearchRequest("item", 100, details))
            .Select(result => result.Title)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void AssertNames(
        IEnumerable<IndexStore> stores,
        FileSearchSortField field,
        FileSearchSortDirection direction,
        params string[] expected) =>
        Assert.Equal(expected, Search(stores, field, direction).Select(item => item.Result.Name));

    private static SearchResult FakeResult(string title) => new(
        new SearchResultAction(),
        title,
        null,
        "Fake",
        string.Empty,
        null,
        "\uE8A5");

    private static NativeFileId Id(long value) => new(BitConverter.GetBytes(value));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record FakeRequestDetails(string Value) : ISearchRequestDetails;

    private sealed class CapturingSource : ISearchSource
    {
        public List<SearchRequest> Requests { get; } = [];

        public IReadOnlyList<SearchResult> Search(SearchRequest request)
        {
            Requests.Add(request);
            return [FakeResult(request.Query)];
        }
    }

    private sealed class RecordingShell : IWindowsShellLauncher
    {
        public string? OpenedPath { get; private set; }
        public string? RevealedPath { get; private set; }
        public bool RevealedDirectory { get; private set; }

        public void Open(string path) => OpenedPath = path;

        public void Reveal(string path, bool isDirectory)
        {
            RevealedPath = path;
            RevealedDirectory = isDirectory;
        }
    }

    private sealed record SortEntry(string Name, long? Size, long? Modified);
}
