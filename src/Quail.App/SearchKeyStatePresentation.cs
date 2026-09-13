namespace Quail.App;

internal enum SearchKeyState
{
    None,
    QuickNoResults,
    QuickIndexUnavailable,
    FullEmptySearch,
    FullNoResults,
    FullIndexUnavailable
}

internal static class SearchKeyStatePresentation
{
    public static SearchKeyState ResolveQuick(
        bool hasQuery,
        bool completedSuccessfully,
        bool isCurrent,
        bool hasUsableSource,
        int resultCount) =>
        hasQuery && !hasUsableSource
            ? SearchKeyState.QuickIndexUnavailable
            : hasQuery && completedSuccessfully && isCurrent && resultCount == 0
                ? SearchKeyState.QuickNoResults
                : SearchKeyState.None;

    public static SearchKeyState ResolveFull(FullSearchInputState inputState) => inputState switch
    {
        FullSearchInputState.NoSource => SearchKeyState.FullIndexUnavailable,
        FullSearchInputState.EmptyQuery => SearchKeyState.FullEmptySearch,
        _ => SearchKeyState.None
    };

    public static SearchKeyState ResolveFullCompletion(int resultCount) =>
        resultCount == 0 ? SearchKeyState.FullNoResults : SearchKeyState.None;

    public static string Title(SearchKeyState state) => state switch
    {
        SearchKeyState.QuickNoResults => "No results found",
        SearchKeyState.QuickIndexUnavailable => "Index unavailable",
        SearchKeyState.FullEmptySearch => "Start typing to search",
        SearchKeyState.FullNoResults => "No results found",
        SearchKeyState.FullIndexUnavailable => "Index unavailable",
        _ => string.Empty
    };

    public static string Detail(SearchKeyState state) => state switch
    {
        SearchKeyState.QuickNoResults => "Try a different search term.",
        SearchKeyState.QuickIndexUnavailable => "Quail can't access a search index right now.",
        SearchKeyState.FullEmptySearch => "Use the search box above to find files, folders, and more.",
        SearchKeyState.FullNoResults => "Try a different search term.",
        SearchKeyState.FullIndexUnavailable => "Quail can't access the search index right now.\nCheck your indexing settings.",
        _ => string.Empty
    };

    public static string IconGlyph(SearchKeyState state) => state switch
    {
        SearchKeyState.QuickNoResults => "\uE8A5",
        SearchKeyState.QuickIndexUnavailable => "\uE7BA",
        SearchKeyState.FullEmptySearch => "\uE721",
        SearchKeyState.FullNoResults => "\uE8A5",
        SearchKeyState.FullIndexUnavailable => "\uE7BA",
        _ => string.Empty
    };
}
