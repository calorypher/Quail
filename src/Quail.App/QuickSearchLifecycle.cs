namespace Quail.App;

internal static class QuickSearchLifecycle
{
    public static bool ShouldHideOnDeactivation(bool overlayVisible, bool exiting) =>
        overlayVisible && !exiting;
}
