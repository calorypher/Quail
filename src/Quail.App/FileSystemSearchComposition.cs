using Quail.Core;
using Quail.FileSystem;

namespace Quail.App;

internal static class FileSystemSearchComposition
{
    public static SearchRuntime Create(AppLaunchOptions options, IndexCatalogController catalog)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);

        var source = new FileSystemSearchSource(
            () => options.IndexPaths.Count > 0 ? options.IndexPaths : catalog.ActivePaths);
        SearchRuntime? runtime = null;
        runtime = new SearchRuntime(
            new SearchApplicationService([source]),
            () => options.IndexPaths.Count > 0 || catalog.ActivePaths.Count > 0,
            () =>
            {
                var scale = source.GetSearchIndexScale();
                return new SearchIndexScale(
                    scale.ConfiguredIndexCount,
                    scale.RecordCount,
                    scale.DatabaseBytes,
                    scale.UnavailableIndexCount);
            },
            () => GetFreshness(options, source),
            () => catalog.ActivePathsChanged -= runtime!.NotifySourcesChanged);
        catalog.ActivePathsChanged += runtime.NotifySourcesChanged;
        return runtime;
    }

    private static IndexFreshness? GetFreshness(AppLaunchOptions options, FileSystemSearchSource source)
    {
        if (options.IndexPaths.Count > 0)
        {
            return null;
        }

        var freshness = source.GetIndexStatuses()
            .Select(status => IndexFreshnessPolicy.Classify(status, DateTimeOffset.UtcNow))
            .ToArray();
        return freshness.Contains(IndexFreshness.RefreshRecommended)
            ? IndexFreshness.RefreshRecommended
            : freshness.Contains(IndexFreshness.Unknown)
                ? IndexFreshness.Unknown
                : null;
    }
}
