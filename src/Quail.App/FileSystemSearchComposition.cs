using Quail.Core;
using Quail.FileSystem;

namespace Quail.App;

internal static class FileSystemSearchComposition
{
    public static SearchRuntime Create(AppLaunchOptions options, IndexCatalogController catalog)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);

        IReadOnlyList<string> GetCurrentPaths() => options.IndexPaths.Count > 0
            ? options.IndexPaths
            : catalog.GetActivePathsForSearch();

        var source = new FileSystemSearchSource(GetCurrentPaths);
        SearchRuntime? runtime = null;
        runtime = new SearchRuntime(
            new SearchApplicationService([source]),
            () => GetCurrentPaths().Count > 0,
            () => catalog.ActivePathsChanged -= runtime!.NotifySourcesChanged,
            recordSessionStart: trace =>
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
}
