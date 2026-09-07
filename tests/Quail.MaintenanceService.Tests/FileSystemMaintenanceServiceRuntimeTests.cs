using Quail.FileSystem;
using Quail.MaintenanceService;

namespace Quail.MaintenanceService.Tests;

public sealed class FileSystemMaintenanceServiceRuntimeTests
{
    [Fact]
    public async Task Target_loop_fault_terminates_the_top_level_runtime()
    {
        var controlServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var targetFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidDataException("health-corrupt");

        var runtime = FileSystemMaintenanceServiceRuntime.AwaitControlOrTargetFailureAsync(
            controlServer.Task,
            targetFailure.Task);
        targetFailure.SetResult(failure);

        var observed = await Assert.ThrowsAsync<InvalidDataException>(() => runtime);
        Assert.Same(failure, observed);
    }

    [Fact]
    public async Task Normal_control_server_completion_does_not_wait_for_a_target_fault()
    {
        var targetFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await FileSystemMaintenanceServiceRuntime.AwaitControlOrTargetFailureAsync(
            Task.CompletedTask,
            targetFailure.Task);

        Assert.False(targetFailure.Task.IsCompleted);
    }

    [Theory]
    [InlineData(MaintenanceHealthState.Unavailable, "service-stopped")]
    [InlineData(MaintenanceHealthState.Error, "runtime-failed")]
    public void Inactive_health_is_untrusted_and_preserves_the_committed_checkpoint(
        MaintenanceHealthState state,
        string reason)
    {
        var checkpoint = new IncrementalCheckpoint(42, 100, 7, 11);
        var previous = new MaintenanceTargetHealth(
            "volume-a",
            MaintenanceHealthState.Healthy,
            true,
            DateTimeOffset.Parse("2026-09-07T10:00:00Z"),
            DateTimeOffset.Parse("2026-09-07T09:59:00Z"),
            checkpoint,
            Guid.NewGuid(),
            null);
        var targets = new MaintenanceTargetsDocument(
            MaintenanceTargetsDocument.CurrentVersion,
            3,
            [new MaintenanceTarget("volume-a", "D:\\")]);
        var current = new MaintenanceHealthDocument(MaintenanceHealthDocument.CurrentVersion, 3, [previous]);
        var now = DateTimeOffset.Parse("2026-09-07T11:00:00Z");

        var document = FileSystemMaintenanceServiceRuntime.CreateInactiveHealth(targets, current, state, reason, now);

        var health = Assert.Single(document.Targets);
        Assert.Equal(state, health.State);
        Assert.False(health.TrustedForSearch);
        Assert.Equal(now, health.UpdatedUtc);
        Assert.Equal(previous.LastSuccessfulMaintenanceUtc, health.LastSuccessfulMaintenanceUtc);
        Assert.Equal(checkpoint, health.Checkpoint);
        Assert.Null(health.OperationId);
        Assert.Equal(reason, health.Reason);
    }
}
