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
            },
            createFullSearchRequest: (query, limit, criteria) =>
            {
                var dates = FullSearchDateRange.ToUtcFileTimeBounds(
                    criteria.ModifiedFrom,
                    criteria.ModifiedTo,
                    TimeZoneInfo.Local);
                return new SearchRequest(
                    query,
                    limit,
                    new FileSystemSearchRequestDetails(
                        criteria.EntryType switch
                        {
                            FullSearchEntryType.Files => SearchEntryType.File,
                            FullSearchEntryType.Folders => SearchEntryType.Directory,
                            _ => SearchEntryType.Any
                        },
                        criteria.Extension,
                        criteria.MinimumSize,
                        criteria.MaximumSize,
                        dates.FromUtcFileTime,
                        dates.ToUtcFileTime,
                        criteria.Hidden,
                        criteria.ReadOnly,
                        criteria.System,
                        (FileSearchSortField)criteria.SortField,
                        (FileSearchSortDirection)criteria.SortDirection));
            },
            getFullSearchFields: result => result.Details is FileSystemSearchResultDetails details
                ? new FullSearchResultFields(
                    details.FullPath,
                    details.IsDirectory,
                    details.LogicalSize,
                    details.LastWriteTimeUtcFileTime,
                    details.Attributes)
                : null);
        catalog.ActivePathsChanged += runtime.NotifySourcesChanged;
        return runtime;
    }
}
