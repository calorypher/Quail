using Quail.App;

namespace Quail.Core.Tests;

public sealed class SearchPerformanceRenderWaiterTests
{
    [Fact]
    public async Task Waiter_binds_to_the_generation_of_the_matching_processed_input()
    {
        var waiter = new SearchPerformanceRenderWaiter();
        var completion = waiter.PrepareForQuery("readme");

        waiter.ObserveProcessedInput("other", 3);
        waiter.ObserveFirstTextRender(3);
        Assert.False(completion.IsCompleted);

        waiter.ObserveProcessedInput("readme", 7);
        waiter.ObserveFirstTextRender(6);
        Assert.False(completion.IsCompleted);

        waiter.ObserveFirstTextRender(7);
        await completion;
    }

    [Fact]
    public async Task Empty_input_does_not_bind_or_complete_the_waiter_for_a_repeated_query()
    {
        var waiter = new SearchPerformanceRenderWaiter();
        var firstCompletion = waiter.PrepareForQuery("readme");
        waiter.ObserveProcessedInput("readme", 1);
        waiter.ObserveFirstTextRender(1);
        await firstCompletion;

        var repeatedCompletion = waiter.PrepareForQuery("readme");
        waiter.ObserveProcessedInput(string.Empty, 2);
        waiter.ObserveFirstTextRender(2);
        Assert.False(repeatedCompletion.IsCompleted);

        waiter.ObserveProcessedInput("readme", 3);
        waiter.ObserveFirstTextRender(3);
        await repeatedCompletion;
    }

    [Fact]
    public async Task Waiter_completes_only_for_the_final_rapid_typing_query_generation()
    {
        var waiter = new SearchPerformanceRenderWaiter();
        waiter.ObserveProcessedInput("q", 1);
        waiter.ObserveProcessedInput("qu", 2);
        waiter.ObserveProcessedInput("qua", 3);
        waiter.ObserveProcessedInput("quai", 4);

        var completion = waiter.PrepareForQuery("quail");
        waiter.ObserveFirstTextRender(4);
        Assert.False(completion.IsCompleted);

        waiter.ObserveProcessedInput("quail", 5);
        waiter.ObserveFirstTextRender(5);
        await completion;
    }
}
