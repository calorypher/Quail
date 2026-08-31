using Quail.Core;

namespace Quail.App;

internal static class SearchResultPresentation
{
    public static ResultItem Map(SearchResult result) => new()
    {
        Action = result.Action,
        Title = result.Title,
        Context = result.Context ?? "Context unavailable",
        Kind = result.Kind,
        Metadata = result.Metadata,
        IconKey = result.IconKey,
        FallbackIconGlyph = result.FallbackIconGlyph,
    };
}
