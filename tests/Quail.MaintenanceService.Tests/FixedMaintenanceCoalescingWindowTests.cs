using Quail.FileSystem;
using Quail.MaintenanceService;

namespace Quail.MaintenanceService.Tests;

public sealed class FixedMaintenanceCoalescingWindowTests
{
    [Fact]
    public void First_external_change_starts_one_fixed_window_without_moving_its_deadline()
    {
        var window = new FixedMaintenanceCoalescingWindow();
        var firstChange = DateTimeOffset.Parse("2026-09-20T10:00:00Z");

        window.Start(firstChange);
        window.Start(firstChange.AddSeconds(45));

        Assert.Equal(firstChange.Add(FixedMaintenanceCoalescingWindow.Duration), window.Deadline);
        Assert.True(window.IsPending(firstChange.AddSeconds(59)));
        Assert.False(window.IsPending(firstChange.AddSeconds(60)));
    }

    [Fact]
    public void Completing_a_window_allows_the_next_external_change_to_start_a_new_fixed_window()
    {
        var window = new FixedMaintenanceCoalescingWindow();
        var firstChange = DateTimeOffset.Parse("2026-09-20T10:00:00Z");
        window.Start(firstChange);

        window.Complete(firstChange.Add(FixedMaintenanceCoalescingWindow.Duration));
        window.Start(firstChange.AddMinutes(2));

        Assert.Equal(firstChange.AddMinutes(3), window.Deadline);
    }

    [Fact]
    public void Startup_catch_up_is_not_coalesced_but_an_external_gap_after_steady_state_is()
    {
        var scheduling = new ContinuousMaintenanceScheduling();
        var now = DateTimeOffset.Parse("2026-09-20T10:00:00Z");

        Assert.False(scheduling.BeginAfterExternalGap(now));
        Assert.Null(scheduling.Deadline);

        scheduling.BeginAfterChangeWake(now);
        Assert.True(scheduling.BeginAfterExternalGap(now.AddSeconds(45)));
        Assert.Equal(now.Add(FixedMaintenanceCoalescingWindow.Duration), scheduling.Deadline);
    }

    [Fact]
    public async Task Cancellation_interrupts_a_pending_coalescing_wait_without_advancing_a_checkpoint()
    {
        var window = new FixedMaintenanceCoalescingWindow();
        var now = DateTimeOffset.UtcNow;
        var checkpoint = new IncrementalCheckpoint(42, 100, 1, 0);
        window.Start(now);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => window.WaitAsync(now, cancellation.Token));

        Assert.Equal(checkpoint, new IncrementalCheckpoint(42, 100, 1, 0));
        Assert.Equal(now.Add(FixedMaintenanceCoalescingWindow.Duration), window.Deadline);
    }

    [Fact]
    public async Task Cancellation_interrupts_the_steady_state_coalescing_wait_used_by_a_queued_rebuild()
    {
        var scheduling = new ContinuousMaintenanceScheduling();
        var now = DateTimeOffset.UtcNow;
        scheduling.BeginAfterChangeWake(now);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scheduling.WaitForPendingCoalescingAsync(now, cancellation.Token));
    }
}
