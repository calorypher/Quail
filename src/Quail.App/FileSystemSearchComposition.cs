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
            () => catalog.ActivePathsChanged -= runtime!.NotifySourcesChanged,
            () => GetFreshnessNotice(options, source),
            trace =>
            {
                var scale = source.GetSearchIndexScale();
                trace.RecordSessionStart(new SearchIndexScale(
                    scale.ConfiguredIndexCount,
                    scale.RecordCount,
                    scale.DatabaseBytes,
                    scale.UnavailableIndexCount));
            });
        catalog.ActivePathsChanged += runtime.NotifySourcesChanged;
        return runtime;
    }

    private static string? GetFreshnessNotice(AppLaunchOptions options, FileSystemSearchSource source)
    {
        if (options.IndexPaths.Count > 0)
        {
            return null;
        }

        var freshness = source.GetIndexStatuses()
            .Select(status => IndexFreshnessPolicy.Classify(status, DateTimeOffset.UtcNow))
            .ToArray();
        return freshness.Contains(IndexFreshness.RefreshRecommended)
            ? "Refresh recommended for one or more indexes."
            : freshness.Contains(IndexFreshness.Unknown)
                ? "Last refresh unknown for one or more indexes."
                : null;
    }
}
