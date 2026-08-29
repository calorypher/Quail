using Quail.App;

namespace Quail.Core.Tests;

public sealed class M11ShortQueryDeferrerTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    public async Task Executes_a_deliberate_short_query_after_the_defer_window(string query)
    {
        var completion = new TaskCompletionSource<(long Generation, string Query)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deferrer = new ShortQueryDeferrer(
            TimeSpan.FromMilliseconds(25),
            (generation, query) => completion.TrySetResult((generation, query)));

        deferrer.Schedule(7, query);

        var actual = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal((7L, query), actual);
    }

    [Fact]
    public async Task Replaces_an_intermediate_short_query_with_the_latest_one()
    {
        var completions = new List<(long Generation, string Query)>();
        using var completed = new ManualResetEventSlim();
        using var deferrer = new ShortQueryDeferrer(
            TimeSpan.FromMilliseconds(50),
            (generation, query) =>
            {
                lock (completions)
                {
                    completions.Add((generation, query));
                    completed.Set();
                }
            });

        deferrer.Schedule(1, "a");
        await Task.Delay(10);
        deferrer.Schedule(2, "ab");

        Assert.True(completed.Wait(TimeSpan.FromSeconds(2)));
        await Task.Delay(100);
        Assert.Equal([(2L, "ab")], completions);
    }

    [Fact]
    public async Task Cancelling_a_short_query_prevents_its_execution()
    {
        var executed = false;
        using var deferrer = new ShortQueryDeferrer(
            TimeSpan.FromMilliseconds(50),
            (_, _) => executed = true);

        deferrer.Schedule(1, "ab");
        deferrer.Cancel();
        await Task.Delay(100);

        Assert.False(executed);
    }
}
