using Quail.App;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class IndexManagerActionAvailabilityTests
{
    [Theory]
    [InlineData(IndexState.Absent, 0, false, false, false)]
    [InlineData(IndexState.Complete, 2, true, false, true)]
    [InlineData(IndexState.RebuildRequired, 1, false, true, false)]
    [InlineData(IndexState.Incomplete, 1, false, true, false)]
    public void Chooses_the_recovery_action_and_refresh_availability(IndexState state, int expectedPrimaryOperation, bool expectedShowRebuild, bool expectedShowRefresh, bool expectedRefreshAvailability)
    {
        var actual = IndexManagerActionAvailability.For(state);

        Assert.Equal((AdminIndexOperation)expectedPrimaryOperation, actual.PrimaryOperation);
        Assert.Equal(expectedShowRebuild, actual.ShowRebuild);
        Assert.Equal(expectedShowRefresh, actual.ShowRefresh);
        Assert.Equal(expectedRefreshAvailability, actual.RefreshAvailable);
    }
}
