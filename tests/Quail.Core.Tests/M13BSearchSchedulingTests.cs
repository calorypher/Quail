using System.Collections.Concurrent;
using Quail.App;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class M13BSearchSchedulingTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    public async Task Short_query_does_not_release_before_a_configured_delay(string query)
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deferrer = new ShortQueryDeferrer(TimeSpan.FromMilliseconds(100), (_, _) => released.TrySetResult());

        deferrer.Schedule(1, query);

        await Task.Delay(25);
        Assert.False(released.Task.IsCompleted);
        deferrer.Cancel();
    }

    [Fact]
    public void Production_short_query_policy_uses_the_temporary_one_second_delay()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), QuickSearchInputPolicy.ShortQueryDefer);
    }

    [Fact]
    public async Task Later_short_input_resets_the_defer_window()
    {
        var released = new TaskCompletionSource<(long Generation, string Query)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deferrer = new ShortQueryDeferrer(
            TimeSpan.FromMilliseconds(150),
            (generation, query) => released.TrySetResult((generation, query)));

        deferrer.Schedule(1, "a");
        await Task.Delay(100);
        deferrer.Schedule(2, "ab");

        await Task.Delay(75);
        Assert.False(released.Task.IsCompleted);
        Assert.Equal((2L, "ab"), await released.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Rapid_short_to_interactive_typing_starts_no_short_core_search()
    {
        var shortCalls = 0;
        using var shortCoordinator = new LatestSearchCoordinator(_ =>
        {
            Interlocked.Increment(ref shortCalls);
            return [];
        }, lane: SearchExecutionLane.ShortQuery);
        using var interactiveCompleted = new SemaphoreSlim(0);
        using var interactiveCoordinator = new LatestSearchCoordinator(_ => [Result("four")]);
        interactiveCoordinator.Completed += _ => interactiveCompleted.Release();
        using var deferrer = new ShortQueryDeferrer(TimeSpan.FromMilliseconds(100), (_, query) => shortCoordinator.Request(query));

        deferrer.Schedule(1, "a");
        deferrer.Schedule(2, "ab");
        deferrer.Cancel();
        interactiveCoordinator.Request("three", uiGeneration: 3);
        interactiveCoordinator.Request("four", uiGeneration: 4);

        await interactiveCompleted.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(150);
        Assert.Equal(0, Volatile.Read(ref shortCalls));
    }

    [Fact]
    public async Task Interactive_search_starts_while_short_lane_is_running()
    {
        using var shortStarted = new ManualResetEventSlim();
        using var releaseShort = new ManualResetEventSlim();
        using var interactiveStarted = new ManualResetEventSlim();
        using var shortCoordinator = new LatestSearchCoordinator(_ =>
        {
            shortStarted.Set();
            releaseShort.Wait(TimeSpan.FromSeconds(5));
            return [Result("short")];
        }, lane: SearchExecutionLane.ShortQuery);
        using var interactiveCoordinator = new LatestSearchCoordinator(_ =>
        {
            interactiveStarted.Set();
            return [Result("interactive")];
        });

        shortCoordinator.Request("a", uiGeneration: 1);
        Assert.True(shortStarted.Wait(TimeSpan.FromSeconds(2)));
        interactiveCoordinator.Request("four", uiGeneration: 2);

        Assert.True(interactiveStarted.Wait(TimeSpan.FromSeconds(2)));
        releaseShort.Set();
    }

    [Fact]
    public async Task Stale_short_completion_cannot_apply_over_a_newer_interactive_result()
    {
        using var shortStarted = new ManualResetEventSlim();
        using var releaseShort = new ManualResetEventSlim();
        var applied = new ConcurrentQueue<string>();
        long currentUiGeneration = 1;
        using var shortCompleted = new SemaphoreSlim(0);
        using var interactiveCompleted = new SemaphoreSlim(0);
        using var shortCoordinator = new LatestSearchCoordinator(_ =>
        {
            shortStarted.Set();
            releaseShort.Wait(TimeSpan.FromSeconds(5));
            return [Result("short")];
        }, lane: SearchExecutionLane.ShortQuery);
        using var interactiveCoordinator = new LatestSearchCoordinator(_ => [Result("interactive")]);
        Action<SearchCompletion> applyIfCurrent = completion =>
        {
            if (completion.IsCurrent && completion.UiGeneration == Volatile.Read(ref currentUiGeneration))
            {
                applied.Enqueue(completion.Results!.Single().Title);
            }
        };
        shortCoordinator.Completed += completion =>
        {
            applyIfCurrent(completion);
            shortCompleted.Release();
        };
        interactiveCoordinator.Completed += completion =>
        {
            applyIfCurrent(completion);
            interactiveCompleted.Release();
        };

        shortCoordinator.Request("a", uiGeneration: 1);
        Assert.True(shortStarted.Wait(TimeSpan.FromSeconds(2)));
        Volatile.Write(ref currentUiGeneration, 2);
        interactiveCoordinator.Request("four", uiGeneration: 2);
        await interactiveCompleted.WaitAsync(TimeSpan.FromSeconds(2));
        releaseShort.Set();
        await shortCompleted.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["interactive"], applied);
    }

    [Fact]
    public async Task Pending_work_is_bounded_to_the_latest_request_per_lane()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var calls = new ConcurrentQueue<string>();
        using var completions = new SemaphoreSlim(0);
        using var coordinator = new LatestSearchCoordinator(query =>
        {
            calls.Enqueue(query);
            if (query == "one")
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
            return [Result(query)];
        }, lane: SearchExecutionLane.ShortQuery);
        coordinator.Completed += _ => completions.Release();

        coordinator.Request("one");
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        for (var index = 2; index <= 200; index++)
        {
            coordinator.Request($"request-{index}");
        }

        releaseFirst.Set();
        await completions.WaitAsync(TimeSpan.FromSeconds(2));
        await completions.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(["one", "request-200"], calls);
    }

    [Fact]
    public async Task Duplicate_running_query_reuses_the_active_core_search_for_the_latest_ui_generation()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var completion = new TaskCompletionSource<SearchCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new LatestSearchCoordinator(query =>
        {
            Interlocked.Increment(ref calls);
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return [Result(query)];
        });
        coordinator.Completed += completed => completion.TrySetResult(completed);

        coordinator.Request("same", uiGeneration: 1);
        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        coordinator.Request("same", uiGeneration: 2);
        release.Set();

        var actual = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.True(actual.IsCurrent);
        Assert.Equal(2, actual.UiGeneration);
        Assert.Equal("same", Assert.Single(actual.Results!).Title);
    }

    [Fact]
    public async Task Invalidate_discards_pending_work_after_a_running_request()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var calls = new ConcurrentQueue<string>();
        var completions = new ConcurrentQueue<SearchCompletion>();
        using var completionSignal = new SemaphoreSlim(0);
        using var coordinator = new LatestSearchCoordinator(query =>
        {
            calls.Enqueue(query);
            if (query == "one")
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
            return [Result(query)];
        }, lane: SearchExecutionLane.ShortQuery);
        coordinator.Completed += completion =>
        {
            completions.Enqueue(completion);
            completionSignal.Release();
        };

        coordinator.Request("one");
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        coordinator.Request("two");
        coordinator.Invalidate();
        releaseFirst.Set();

        await completionSignal.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(100);

        Assert.Equal(["one"], calls);
        Assert.DoesNotContain(completions, completion => completion.Results!.Single().Title == "two");
    }

    [Fact]
    public async Task Invalidate_then_new_request_reuses_the_pending_wakeup()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var calls = new ConcurrentQueue<string>();
        using var completions = new SemaphoreSlim(0);
        using var coordinator = new LatestSearchCoordinator(query =>
        {
            calls.Enqueue(query);
            if (query == "one")
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
            return [Result(query)];
        });
        coordinator.Completed += _ => completions.Release();

        coordinator.Request("one");
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        coordinator.Request("two");
        coordinator.Invalidate();
        coordinator.Request("three");
        releaseFirst.Set();

        await completions.WaitAsync(TimeSpan.FromSeconds(2));
        await completions.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["one", "three"], calls);
    }

    [Fact]
    public async Task Disposal_cancels_pending_work_and_suppresses_late_callbacks()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var callbacks = 0;
        var coordinator = new LatestSearchCoordinator(_ =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return [];
        });
        coordinator.Completed += _ => Interlocked.Increment(ref callbacks);

        coordinator.Request("running");
        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        coordinator.Request("pending");
        coordinator.Dispose();
        release.Set();

        await Task.Delay(100);
        Assert.Equal(0, Volatile.Read(ref callbacks));
    }

    private static SearchResult Result(string name) => new(
        new SearchResultAction(), name, $"C:\\{name}", "File", "TXT · 1 B", ".TXT", "\uE8A5");
}
