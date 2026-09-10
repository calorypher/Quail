namespace Quail.Core;

internal interface ISearchRequestDetails;

internal interface ISearchResultDetails;

internal sealed record SearchRequest(
    string Query,
    int Limit = 50,
    ISearchRequestDetails? Details = null);

internal interface ISearchSource
{
    IReadOnlyList<SearchResult> Search(SearchRequest request);
}

public sealed class SearchResultAction
{
    private readonly Action? _open;
    private readonly Action? _reveal;
    private readonly Func<string?>? _getCopyText;

    public SearchResultAction()
    {
    }

    public SearchResultAction(Action open) => _open = open ?? throw new ArgumentNullException(nameof(open));

    internal SearchResultAction(Action open, Action? reveal, Func<string?>? getCopyText)
    {
        _open = open ?? throw new ArgumentNullException(nameof(open));
        _reveal = reveal;
        _getCopyText = getCopyText;
    }

    internal bool CanReveal => _reveal is not null;

    internal bool CanCopyText => _getCopyText is not null;

    internal void Open()
    {
        if (_open is null)
        {
            throw new InvalidOperationException("The selected result is no longer available.");
        }

        _open();
    }

    internal void Reveal()
    {
        if (_reveal is null)
        {
            throw new InvalidOperationException("Reveal is not available for the selected result.");
        }

        _reveal();
    }

    internal string GetCopyText()
    {
        if (_getCopyText?.Invoke() is not { Length: > 0 } value)
        {
            throw new InvalidOperationException("Copy is not available for the selected result.");
        }

        return value;
    }
}

internal sealed record SearchResult(
    SearchResultAction Action,
    string Title,
    string? Context,
    string Kind,
    string Metadata,
    string? IconKey,
    string FallbackIconGlyph,
    ISearchResultDetails? Details = null);
