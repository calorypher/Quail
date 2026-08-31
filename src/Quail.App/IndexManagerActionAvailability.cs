using Quail.FileSystem;

namespace Quail.App;

internal readonly record struct IndexManagerActionAvailability(AdminIndexOperation PrimaryOperation, bool ShowRebuild, bool ShowRefresh, bool RefreshAvailable)
{
    public static IndexManagerActionAvailability For(IndexState state) => state switch
    {
        IndexState.Absent => new(AdminIndexOperation.Build, false, false, false),
        IndexState.Complete => new(AdminIndexOperation.Refresh, true, false, true),
        IndexState.RebuildRequired => new(AdminIndexOperation.Rebuild, false, true, false),
        _ => new(AdminIndexOperation.Rebuild, false, true, false)
    };
}
