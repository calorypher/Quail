using Quail.FileSystem;

namespace Quail.App;

internal readonly record struct IndexingActionPolicy(
    AdminIndexOperation? PrimaryOperation,
    bool ShowSecondaryRebuild,
    bool EnableAfterBuild)
{
    public static IndexingActionPolicy For(IndexState state, bool isMachineTargetRegistered) =>
        !isMachineTargetRegistered
            ? new(AdminIndexOperation.Build, false, state == IndexState.Absent)
            : state is IndexState.RebuildRequired or IndexState.Incomplete
                ? new(AdminIndexOperation.Rebuild, false, false)
                : new(null, true, false);
}
