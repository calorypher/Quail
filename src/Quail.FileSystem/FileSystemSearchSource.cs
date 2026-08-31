using System.Globalization;
using Quail.Core;

namespace Quail.FileSystem;

public sealed record FileSystemSearchIndexScale(
    int ConfiguredIndexCount,
    long RecordCount,
    long DatabaseBytes,
    int UnavailableIndexCount);

internal sealed class FileSystemSearchSource : ISearchSource
{
    private const string FileFallbackIconGlyph = "\uE8A5";
    private const string FolderFallbackIconGlyph = "\uE8B7";
    private readonly Func<IReadOnlyList<string>> _paths;
    private readonly IndexedEntryOpener _opener;

    public FileSystemSearchSource(
        Func<IReadOnlyList<string>> paths,
        IndexedEntryOpener? opener = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _opener = opener ?? new IndexedEntryOpener();
    }

    public IReadOnlyList<SearchResult> Search(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var results = MultiIndexSearch.Search(
            _paths().Select(path => new IndexStore(path)),
            new FileSearchQuery(request.Query, Limit: request.Limit));

        return results
            .Select(result => Project(result.SourceIdentity, result.Result))
            .ToArray();
    }

    public FileSystemSearchIndexScale GetSearchIndexScale()
    {
        var paths = _paths();
        long recordCount = 0;
        long databaseBytes = 0;
        var unavailableIndexCount = 0;

        foreach (var path in paths)
        {
            try
            {
                recordCount += new IndexStore(path).GetStatus().RecordCount;
                databaseBytes += new FileInfo(path).Length;
            }
            catch (Exception)
            {
                unavailableIndexCount++;
            }
        }

        return new FileSystemSearchIndexScale(
            paths.Count,
            recordCount,
            databaseBytes,
            unavailableIndexCount);
    }

    public IReadOnlyList<IndexStatus> GetIndexStatuses() => _paths()
        .Select(path => new IndexStore(path).GetStatus())
        .ToArray();

    private SearchResult Project(string databasePath, FileSearchResult result)
    {
        var action = new FileSystemSearchAction(databasePath, result.FileId);
        var isDirectory = result.IsDirectory;
        return new SearchResult(
            new SearchResultAction(() => Open(action)),
            result.Name,
            result.FullPath,
            isDirectory ? "Folder" : "File",
            FormatMetadata(result),
            isDirectory ? "folder" : result.Extension?.ToUpperInvariant() ?? "file",
            isDirectory ? FolderFallbackIconGlyph : FileFallbackIconGlyph);
    }

    private void Open(FileSystemSearchAction action)
    {
        var store = _paths()
            .Select(path => new IndexStore(path))
            .FirstOrDefault(candidate => string.Equals(
                candidate.DatabasePath,
                action.DatabasePath,
                StringComparison.OrdinalIgnoreCase));
        if (store is null)
        {
            throw new InvalidOperationException("The result source is no longer configured.");
        }

        _opener.Open(store, action.FileId);
    }

    private static string FormatMetadata(FileSearchResult result)
    {
        if (result.IsDirectory)
        {
            return "Folder";
        }

        var type = string.IsNullOrWhiteSpace(result.Extension)
            ? "File"
            : result.Extension.ToUpperInvariant();
        return result.LogicalSize is long size
            ? $"{type} · {FormatSize(size)}"
            : type;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture)} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{(bytes / (1024d * 1024)).ToString("0.#", CultureInfo.InvariantCulture)} MB";
        return $"{(bytes / (1024d * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture)} GB";
    }
}

internal sealed record FileSystemSearchAction(string DatabasePath, NativeFileId FileId);
