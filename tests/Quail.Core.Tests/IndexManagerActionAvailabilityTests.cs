using Quail.App;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class IndexManagerActionAvailabilityTests
{
    [Theory]
    [InlineData(IndexState.Absent, 0, false)]
    [InlineData(IndexState.Complete, 1, false)]
    [InlineData(IndexState.RebuildRequired, 1, false)]
    [InlineData(IndexState.Incomplete, 1, false)]
    public void Chooses_the_recovery_action_without_manual_refresh(IndexState state, int expectedPrimaryOperation, bool expectedShowRebuild)
    {
        var actual = IndexManagerActionAvailability.For(state);

        Assert.Equal((AdminIndexOperation)expectedPrimaryOperation, actual.PrimaryOperation);
        Assert.Equal(expectedShowRebuild, actual.ShowRebuild);
    }
}
