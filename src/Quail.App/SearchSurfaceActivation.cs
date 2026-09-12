namespace Quail.App;

internal enum SearchSurfaceActivationTarget
{
    Quick,
    Full
}

internal static class SearchSurfaceActivation
{
    public static SearchSurfaceActivationTarget ForGlobalActivation(bool fullSearchVisible, bool fullSearchMinimized) =>
        fullSearchVisible || fullSearchMinimized
            ? SearchSurfaceActivationTarget.Full
            : SearchSurfaceActivationTarget.Quick;
}
