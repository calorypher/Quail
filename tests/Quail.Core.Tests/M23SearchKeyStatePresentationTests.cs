using Quail.App;

namespace Quail.Core.Tests;

public sealed class M23SearchKeyStatePresentationTests
{
    [Fact]
    public void Quick_empty_query_has_no_expanded_key_state()
    {
        var state = SearchKeyStatePresentation.ResolveQuick(
            hasQuery: false,
            completedSuccessfully: true,
            isCurrent: true,
            hasUsableSource: true,
            resultCount: 0);

        Assert.Equal(SearchKeyState.None, state);
    }

    [Fact]
    public void Quick_current_successful_zero_result_query_shows_no_results()
    {
        var state = SearchKeyStatePresentation.ResolveQuick(
            hasQuery: true,
            completedSuccessfully: true,
            isCurrent: true,
            hasUsableSource: true,
            resultCount: 0);

        Assert.Equal(SearchKeyState.QuickNoResults, state);
    }

    [Fact]
    public void Quick_results_and_superseded_completion_do_not_restore_no_results()
    {
        var results = SearchKeyStatePresentation.ResolveQuick(
            hasQuery: true,
            completedSuccessfully: true,
            isCurrent: true,
            hasUsableSource: true,
            resultCount: 1);
        var superseded = SearchKeyStatePresentation.ResolveQuick(
            hasQuery: true,
            completedSuccessfully: true,
            isCurrent: false,
            hasUsableSource: true,
            resultCount: 0);

        Assert.Equal(SearchKeyState.None, results);
        Assert.Equal(SearchKeyState.None, superseded);
    }

    [Fact]
    public void Full_usable_empty_query_shows_empty_search()
    {
        var input = FullSearchInputPolicy.Evaluate(string.Empty, hasSources: true, filtersValid: true);

        Assert.Equal(SearchKeyState.FullEmptySearch, SearchKeyStatePresentation.ResolveFull(input));
    }

    [Fact]
    public void Full_no_usable_source_precedes_empty_query_and_invalid_filters()
    {
        var input = FullSearchInputPolicy.Evaluate(string.Empty, hasSources: false, filtersValid: false);

        Assert.Equal(FullSearchInputState.NoSource, input);
        Assert.Equal(SearchKeyState.FullIndexUnavailable, SearchKeyStatePresentation.ResolveFull(input));
    }

    [Fact]
    public void Full_usable_query_with_results_has_no_key_state()
    {
        var input = FullSearchInputPolicy.Evaluate("quail", hasSources: true, filtersValid: true);

        Assert.Equal(FullSearchInputState.Ready, input);
        Assert.Equal(SearchKeyState.None, SearchKeyStatePresentation.ResolveFull(input));
    }
}
