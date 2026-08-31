using Quail.FileSystem;

namespace Quail.App;

internal enum IndexFreshness
{
    Fresh,
    RefreshRecommended,
    Unknown
}

internal static class IndexFreshnessPolicy
{
    internal static readonly TimeSpan RefreshRecommendedAfter = TimeSpan.FromHours(24);

    public static IndexFreshness Classify(IndexStatus status, DateTimeOffset now)
    {
        if (status.State != IndexState.Complete || status.LastRefreshedUtc is null)
        {
            return IndexFreshness.Unknown;
        }

        return now - status.LastRefreshedUtc.Value >= RefreshRecommendedAfter
            ? IndexFreshness.RefreshRecommended
            : IndexFreshness.Fresh;
    }

    public static string? Describe(IndexStatus status, DateTimeOffset now)
    {
        if (status.State != IndexState.Complete)
        {
            return null;
        }

        return Classify(status, now) switch
        {
            IndexFreshness.RefreshRecommended => "Refresh recommended.",
            IndexFreshness.Unknown => "Last refresh unknown.",
            _ => null
        };
    }
}
