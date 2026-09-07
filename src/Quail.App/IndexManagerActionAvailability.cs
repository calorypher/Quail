using Quail.FileSystem;

namespace Quail.App;

internal readonly record struct IndexManagerActionAvailability(AdminIndexOperation PrimaryOperation, bool ShowRebuild)
{
    public static IndexManagerActionAvailability For(IndexState state) => state switch
    {
        IndexState.Absent => new(AdminIndexOperation.Build, false),
        IndexState.Complete => new(AdminIndexOperation.Rebuild, false),
        IndexState.RebuildRequired => new(AdminIndexOperation.Rebuild, false),
        _ => new(AdminIndexOperation.Rebuild, false)
    };
}
