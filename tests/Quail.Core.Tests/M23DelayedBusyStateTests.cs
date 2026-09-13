using Quail.App;

namespace Quail.Core.Tests;

public sealed class M23DelayedBusyStateTests
{
    [Fact]
    public void Completion_before_threshold_never_makes_busy_visible()
    {
        var state = new DelayedBusyState();

        state.Begin(1);
        Assert.True(state.Complete(1));

        Assert.False(state.TryShow(1));
        Assert.False(state.IsVisible);
    }

    [Fact]
    public void Current_request_becomes_busy_eligible_after_the_delay()
    {
        var state = new DelayedBusyState();

        state.Begin(2);

        Assert.True(state.TryShow(2));
        Assert.True(state.IsVisible);
    }

    [Fact]
    public void Superseded_or_cancelled_request_cannot_show_stale_busy_state()
    {
        var state = new DelayedBusyState();

        state.Begin(3);
        state.Begin(4);
        Assert.False(state.TryShow(3));
        Assert.True(state.TryShow(4));

        state.Cancel();
        Assert.False(state.TryShow(4));
        Assert.False(state.IsVisible);
    }
}
