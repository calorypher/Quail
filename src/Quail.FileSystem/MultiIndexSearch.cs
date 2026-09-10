using System.Diagnostics;
using System.Text;

namespace Quail.FileSystem;

public sealed record IndexedFileSearchResult(string SourceIdentity, FileSearchResult Result);

public static class MultiIndexSearch
{
    public static IReadOnlyList<IndexedFileSearchResult> Search(
        IEnumerable<IndexStore> stores,
        FileSearchQuery query,
        FileSearchRankingContext? rankingContext = null)
    {
        ArgumentNullException.ThrowIfNull(stores);
        ArgumentNullException.ThrowIfNull(query);
        var indexes = stores.ToArray();
        var context = rankingContext ?? FileSearchRankingContext.ForCurrentMachine();
        if (indexes.Length == 0) throw new ArgumentException("Search requires at least one index.", nameof(stores));
        if (query.Limit is < 1 or > IndexStore.MaximumSearchResultLimit)
            throw new ArgumentOutOfRangeException(nameof(query), $"Search limit must be between 1 and {IndexStore.MaximumSearchResultLimit}.");
        var candidates = indexes.SelectMany(store => store.Search(query, context)
            .Select(result => new IndexedFileSearchResult(store.DatabasePath, result)));
        return candidates
            .OrderBy(candidate => candidate, new IndexedFileSearchResultComparer(query, context))
            .ThenBy(candidate => candidate.SourceIdentity, StringComparer.Ordinal)
            .Take(query.Limit)
            .ToArray();
    }
}

internal sealed class IndexedFileSearchResultComparer(FileSearchQuery query, FileSearchRankingContext context) : IComparer<IndexedFileSearchResult>
{
    public int Compare(IndexedFileSearchResult? left, IndexedFileSearchResult? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        return FileSearchSorting.Compare(left.Result, right.Result, query, context);
    }
}

internal sealed class SqliteNoCaseComparer : IComparer<string>
{
    public static readonly SqliteNoCaseComparer Instance = new();
    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        return Utf8Comparison.Compare(left, right, foldAscii: true);
    }
}

internal sealed class Utf8BinaryComparer : IComparer<string>
{
    public static readonly Utf8BinaryComparer Instance = new();
    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        return Utf8Comparison.Compare(left, right, foldAscii: false);
    }
}

internal static class Utf8Comparison
{
    public static int Compare(string left, string right, bool foldAscii)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        var length = Math.Min(leftBytes.Length, rightBytes.Length);

        for (var index = 0; index < length; index++)
        {
            var leftByte = foldAscii ? FoldAscii(leftBytes[index]) : leftBytes[index];
            var rightByte = foldAscii ? FoldAscii(rightBytes[index]) : rightBytes[index];
            var comparison = leftByte.CompareTo(rightByte);
            if (comparison != 0) return comparison;
        }

        return leftBytes.Length.CompareTo(rightBytes.Length);
    }

    private static byte FoldAscii(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + ('a' - 'A')) : value;
}

public interface IWindowsShellLauncher
{
    void Open(string path);

    void Reveal(string path, bool isDirectory) => Open(path);
}

internal static class FileSearchSorting
{
    public static int Compare(
        FileSearchResult left,
        FileSearchResult right,
        FileSearchQuery query,
        FileSearchRankingContext context)
    {
        if (query.SortField == FileSearchSortField.Relevance)
        {
            return FileSearchRanking.Compare(left, right, query.NameQuery, context);
        }

        var comparison = query.SortField switch
        {
            FileSearchSortField.Name => ApplyDirection(CompareText(left.Name, right.Name), query.SortDirection),
            FileSearchSortField.Path => CompareNullableText(left.FullPath, right.FullPath, query.SortDirection),
            FileSearchSortField.Size => CompareNullable(left.LogicalSize, right.LogicalSize, query.SortDirection),
            FileSearchSortField.Modified => CompareNullable(left.LastWriteTimeUtcFileTime, right.LastWriteTimeUtcFileTime, query.SortDirection),
            _ => throw new ArgumentOutOfRangeException(nameof(query), "Unknown filesystem search sort field.")
        };
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareText(left.Name, right.Name);
        if (comparison != 0)
        {
            return comparison;
        }

        return StringComparer.Ordinal.Compare(left.FileId.ToString(), right.FileId.ToString());
    }

    public static int CompareFieldCandidate(
        IndexStore.SearchFieldCandidate left,
        IndexStore.SearchFieldCandidate right,
        FileSearchQuery query)
    {
        var comparison = query.SortField switch
        {
            FileSearchSortField.Name => ApplyDirection(CompareText(left.Name, right.Name), query.SortDirection),
            FileSearchSortField.Size => CompareNullable(left.LogicalSize, right.LogicalSize, query.SortDirection),
            FileSearchSortField.Modified => CompareNullable(left.LastWriteTimeUtcFileTime, right.LastWriteTimeUtcFileTime, query.SortDirection),
            _ => throw new ArgumentOutOfRangeException(nameof(query), "The selected sort requires materialized paths.")
        };
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = CompareText(left.Name, right.Name);
        if (comparison != 0)
        {
            return comparison;
        }

        return StringComparer.Ordinal.Compare(left.FileId.ToString(), right.FileId.ToString());
    }

    private static int CompareNullable(long? left, long? right, FileSearchSortDirection direction)
    {
        if (left is null)
        {
            return right is null ? 0 : 1;
        }
        if (right is null)
        {
            return -1;
        }

        return ApplyDirection(left.Value.CompareTo(right.Value), direction);
    }

    private static int CompareNullableText(string? left, string? right, FileSearchSortDirection direction)
    {
        if (left is null)
        {
            return right is null ? 0 : 1;
        }
        if (right is null)
        {
            return -1;
        }

        return ApplyDirection(CompareText(left, right), direction);
    }

    private static int CompareText(string left, string right)
    {
        var comparison = SqliteNoCaseComparer.Instance.Compare(left, right);
        return comparison != 0 ? comparison : Utf8BinaryComparer.Instance.Compare(left, right);
    }

    private static int ApplyDirection(int comparison, FileSearchSortDirection direction) =>
        direction == FileSearchSortDirection.Descending ? -comparison : comparison;
}

public sealed class WindowsShellLauncher : IWindowsShellLauncher
{
    public void Open(string path)
    {
        var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        if (process is null)
        {
            throw new InvalidOperationException("Windows Shell could not open the selected result.");
        }
    }

    public void Reveal(string path, bool isDirectory)
    {
        var target = isDirectory ? Directory.GetParent(path)?.FullName : path;
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new InvalidOperationException("The containing folder is unavailable.");
        }

        if (isDirectory)
        {
            Open(target);
            return;
        }

        var process = Process.Start(new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
            Arguments = $"/select,\"{target.Replace("\"", "\"\"")}\""
        });
        if (process is null)
        {
            throw new InvalidOperationException("Windows Explorer could not reveal the selected result.");
        }
    }
}

public sealed class IndexedEntryOpener
{
    private readonly IWindowsShellLauncher _shell;
    private readonly Func<string, bool> _pathExists;

    public IndexedEntryOpener(IWindowsShellLauncher? shell = null, Func<string, bool>? pathExists = null)
    {
        _shell = shell ?? new WindowsShellLauncher();
        _pathExists = pathExists ?? (path => File.Exists(path) || Directory.Exists(path));
    }

    public void Open(IndexStore store, NativeFileId fileId)
    {
        ArgumentNullException.ThrowIfNull(store);
        var resolution = store.ResolveOpenPath(fileId);
        if (!resolution.Success || string.IsNullOrWhiteSpace(resolution.Path))
            throw new InvalidOperationException($"Indexed entry cannot be resolved: {resolution.Diagnostic ?? "unknown path error"}");
        if (!_pathExists(resolution.Path))
            throw new FileNotFoundException("Indexed path is missing or unavailable.", resolution.Path);
        _shell.Open(resolution.Path);
    }

    public void Reveal(IndexStore store, NativeFileId fileId, bool isDirectory)
    {
        ArgumentNullException.ThrowIfNull(store);
        var resolution = store.ResolveOpenPath(fileId);
        if (!resolution.Success || string.IsNullOrWhiteSpace(resolution.Path))
        {
            throw new InvalidOperationException($"Indexed entry cannot be resolved: {resolution.Diagnostic ?? "unknown path error"}");
        }
        if (!_pathExists(resolution.Path))
        {
            throw new FileNotFoundException("Indexed path is missing or unavailable.", resolution.Path);
        }

        _shell.Reveal(resolution.Path, isDirectory);
    }
}
