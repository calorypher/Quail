namespace Quail.App;

internal static class QuickSearchFooterPresentation
{
    public const int ResultLimit = 50;

    public static string? ResultCountLabel(int returnedResultCount) => returnedResultCount switch
    {
        <= 0 => null,
        ResultLimit => $"Showing top {ResultLimit} results.",
        1 => "1 result.",
        _ => $"{returnedResultCount:N0} results."
    };
}
