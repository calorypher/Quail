namespace Quail.App;

internal enum FullSearchDismissKind
{
    Collapse,
    NativeClose
}

internal static class FullSearchLifecycle
{
    public static bool ShouldCreateWindow(bool hasWindow) => !hasWindow;

    public static bool ShouldShowQuickSearch(FullSearchDismissKind kind) =>
        kind == FullSearchDismissKind.Collapse;

    public static string TransferQuery(string? query) => query ?? string.Empty;
}

internal enum FullSearchInputState
{
    Ready,
    EmptyQuery,
    NoSource,
    InvalidFilters
}

internal static class FullSearchInputPolicy
{
    public static FullSearchInputState Evaluate(string? query, bool hasSources, bool filtersValid)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return FullSearchInputState.EmptyQuery;
        }
        if (!hasSources)
        {
            return FullSearchInputState.NoSource;
        }

        return filtersValid ? FullSearchInputState.Ready : FullSearchInputState.InvalidFilters;
    }
}
