namespace Quail.Core;

internal sealed record SearchRequest(string Query, int Limit = 50);

internal interface ISearchSource
{
    IReadOnlyList<SearchResult> Search(SearchRequest request);
}

public sealed class SearchResultAction
{
    private readonly Action? _open;

    public SearchResultAction()
    {
    }

    public SearchResultAction(Action open) => _open = open ?? throw new ArgumentNullException(nameof(open));

    internal void Open()
    {
        if (_open is null)
        {
            throw new InvalidOperationException("The selected result is no longer available.");
        }

        _open();
    }
}

internal sealed record SearchResult(
    SearchResultAction Action,
    string Title,
    string? Context,
    string Kind,
    string Metadata,
    string IconKey,
    string FallbackIconGlyph);
