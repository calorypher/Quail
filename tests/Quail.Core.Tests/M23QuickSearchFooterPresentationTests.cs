using Quail.App;

namespace Quail.Core.Tests;

public sealed class M23QuickSearchFooterPresentationTests
{
    [Theory]
    [InlineData(1, "1 result.")]
    [InlineData(6, "6 results.")]
    [InlineData(49, "49 results.")]
    [InlineData(50, "Showing top 50 results.")]
    public void Result_count_uses_the_compact_bounded_label(int returnedResultCount, string expected)
    {
        Assert.Equal(expected, QuickSearchFooterPresentation.ResultCountLabel(returnedResultCount));
    }

    [Fact]
    public void Zero_results_has_no_footer_count_label()
    {
        Assert.Null(QuickSearchFooterPresentation.ResultCountLabel(0));
    }
}
