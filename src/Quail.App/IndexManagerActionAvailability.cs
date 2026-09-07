using Quail.FileSystem;

namespace Quail.App;

internal readonly record struct IndexManagerActionAvailability(
    AdminIndexOperation PrimaryOperation,
    bool ShowRebuild,
    bool EnableAfterBuild)
{
    public static IndexManagerActionAvailability For(IndexState state, bool isMachineTargetRegistered)
    {
        if (!isMachineTargetRegistered)
        {
            return new(AdminIndexOperation.Build, false, state == IndexState.Absent);
        }

        return new(AdminIndexOperation.Rebuild, false, false);
    }
}
