using Quail.App;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class IndexManagerActionAvailabilityTests
{
    [Theory]
    [InlineData(IndexState.Complete, true, 1, false)]
    [InlineData(IndexState.Complete, false, 0, false)]
    [InlineData(IndexState.Absent, false, 0, true)]
    [InlineData(IndexState.RebuildRequired, true, 1, false)]
    [InlineData(IndexState.Incomplete, true, 1, false)]
    public void Chooses_the_recovery_action_without_manual_refresh(
        IndexState state,
        bool isMachineTargetRegistered,
        int expectedPrimaryOperation,
        bool expectedEnableAfterBuild)
    {
        var actual = IndexManagerActionAvailability.For(state, isMachineTargetRegistered);

        Assert.Equal((AdminIndexOperation)expectedPrimaryOperation, actual.PrimaryOperation);
        Assert.False(actual.ShowRebuild);
        Assert.Equal(expectedEnableAfterBuild, actual.EnableAfterBuild);
    }
}

public sealed class IndexingActionPolicyTests
{
    [Theory]
    [InlineData(IndexState.Complete, true, null, true, false)]
    [InlineData(IndexState.Absent, false, 0, false, true)]
    [InlineData(IndexState.Complete, false, 0, false, false)]
    [InlineData(IndexState.RebuildRequired, true, 1, false, false)]
    [InlineData(IndexState.Incomplete, true, 1, false, false)]
    [InlineData(IndexState.Complete, true, 1, false, false, MaintenanceHealthState.RebuildRequired)]
    public void Makes_rebuild_a_recovery_action_not_healthy_freshness_workflow(
        IndexState state,
        bool registered,
        int? primary,
        bool secondaryRebuild,
        bool enableAfterBuild,
        MaintenanceHealthState? healthState = null)
    {
        var actual = IndexingActionPolicy.For(state, registered, healthState);

        Assert.Equal(primary is null ? null : (AdminIndexOperation)primary.Value, actual.PrimaryOperation);
        Assert.Equal(secondaryRebuild, actual.ShowSecondaryRebuild);
        Assert.Equal(enableAfterBuild, actual.EnableAfterBuild);
    }
}

public sealed class SettingsWindowHotkeyLifecycleTests
{
    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, 2, false)]
    [InlineData(false, 0, false)]
    public void Restores_only_active_capture_when_settings_lifecycle_requires_it(
        bool captureActive,
        int eventValue,
        bool expected)
    {
        Assert.Equal(expected, SettingsWindowHotkeyLifecycle.ShouldRestoreHotkey(captureActive, (SettingsWindowLifecycleEvent)eventValue));
    }
}
