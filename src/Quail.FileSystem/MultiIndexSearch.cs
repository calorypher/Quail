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
        foreach (var index in indexes) index.EnsureSearchReady();

        var candidates = indexes.SelectMany(store => store.Search(query, context)
            .Select(result => new IndexedFileSearchResult(store.DatabasePath, result)));
        return candidates
            .OrderBy(candidate => candidate, new IndexedFileSearchResultComparer(query.NameQuery, context))
            .ThenBy(candidate => candidate.SourceIdentity, StringComparer.Ordinal)
            .Take(query.Limit)
            .ToArray();
    }
}

internal sealed class IndexedFileSearchResultComparer(string query, FileSearchRankingContext context) : IComparer<IndexedFileSearchResult>
{
    public int Compare(IndexedFileSearchResult? left, IndexedFileSearchResult? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        return FileSearchRanking.Compare(left.Result, right.Result, query, context);
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
}
