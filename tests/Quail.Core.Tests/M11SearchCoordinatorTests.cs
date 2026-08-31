using System.Collections.Concurrent;
using Quail.App;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class M11SearchCoordinatorTests
{
    [Fact]
    public async Task Latest_request_wins_and_pending_requests_are_coalesced()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var calls = new ConcurrentQueue<string>();
        var completions = new ConcurrentQueue<SearchCompletion>();
        using var completed = new SemaphoreSlim(0);
        using var coordinator = new LatestFileSearchCoordinator(query =>
        {
            calls.Enqueue(query);
            if (query == "one")
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
            return [Result(query)];
        });
        coordinator.Completed += completion =>
        {
            completions.Enqueue(completion);
            completed.Release();
        };

        coordinator.Request("one");
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));
        for (var index = 2; index <= 200; index++)
        {
            coordinator.Request($"request-{index}");
        }
        releaseFirst.Set();

        await completed.WaitAsync(TimeSpan.FromSeconds(5));
        await completed.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["one", "request-200"], calls);
        Assert.Contains(completions, completion => completion.Generation == 1 && !completion.IsCurrent);
        Assert.Contains(completions, completion => completion.IsCurrent && completion.Results!.Single().Name == "request-200");
    }

    [Fact]
    public void Bounded_cache_evicts_the_least_recent_item()
    {
        var cache = new BoundedLruCache<string, int>(2);
        cache.Set("one", 1);
        cache.Set("two", 2);
        Assert.True(cache.TryGetValue("one", out _));
        cache.Set("three", 3);

        Assert.True(cache.TryGetValue("one", out var one));
        Assert.False(cache.TryGetValue("two", out _));
        Assert.True(cache.TryGetValue("three", out var three));
        Assert.Equal(1, one);
        Assert.Equal(3, three);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void Metadata_formatting_uses_concise_file_and_folder_labels()
    {
        var file = Result("report") with { Extension = "pdf", LogicalSize = 2_621_440 };
        var folder = Result("documents") with { IsDirectory = true };

        Assert.Equal("PDF · 2.5 MB", FileSearchMetadata.Format(file));
        Assert.Equal("Folder", FileSearchMetadata.Format(folder));
    }

    [Theory]
    [InlineData(1, 0, -1, 0)]
    [InlineData(3, 0, -1, 0)]
    [InlineData(3, 2, 1, 2)]
    public void Result_navigation_keeps_nonempty_selection_in_range(int count, int selected, int delta, int expected)
    {
        Assert.True(ResultSelection.TryGetMoveTarget(count, selected, delta, out var target));
        Assert.Equal(expected, target);
    }

    [Fact]
    public void Result_navigation_is_a_no_op_for_empty_results()
    {
        Assert.False(ResultSelection.TryGetMoveTarget(0, -1, -1, out _));
        Assert.False(ResultSelection.TryGetMoveTarget(0, -1, 1, out _));
        Assert.False(ResultSelection.TryGetBoundaryTarget(0, last: false, out _));
        Assert.False(ResultSelection.TryGetBoundaryTarget(0, last: true, out _));
    }

    private static SearchResult Result(string name) => new(
        new SearchResultAction(), name, $"C:\\{name}", false, "txt", 1);
}
