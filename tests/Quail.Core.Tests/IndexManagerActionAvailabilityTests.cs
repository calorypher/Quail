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
