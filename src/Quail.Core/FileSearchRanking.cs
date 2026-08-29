namespace Quail.Core;

public enum FileSearchTextMatch
{
    Exact,
    Prefix,
    TokenPrefix,
    Substring
}

public enum FileSearchLocation
{
    CurrentUserVisible,
    OtherUserVisible,
    OtherVisible,
    CurrentUserInternal,
    OtherUserInternal,
    OtherInternal,
    SystemHeavy
}

public sealed class FileSearchRankingContext
{
    private static readonly string[] DefaultSystemRoots =
    [
        "Windows",
        "Program Files",
        "Program Files (x86)",
        "ProgramData",
        "$Recycle.Bin",
        "System Volume Information"
    ];

    public FileSearchRankingContext(string? currentUserProfilePath, IEnumerable<string>? systemRootPaths = null)
    {
        CurrentUserProfilePath = NormalizePath(currentUserProfilePath);
        SystemRootPaths = (systemRootPaths ?? Array.Empty<string>())
            .Select(NormalizePath)
            .Where(path => path is not null)
            .Cast<string>()
            .ToArray();
    }

    public string? CurrentUserProfilePath { get; }
    public IReadOnlyList<string> SystemRootPaths { get; }

    public static FileSearchRankingContext ForCurrentMachine()
    {
        var currentUser = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");

        return new FileSearchRankingContext(
            currentUser,
            new[] { systemRoot, programFiles, programFilesX86, commonApplicationData }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!));
    }

    internal static IReadOnlyList<string> GetSegments(string? path)
    {
        var normalized = NormalizePath(path);
        return normalized is null
            ? Array.Empty<string>()
            : normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return path.Replace('/', '\\').TrimEnd('\\');
    }

    internal static IReadOnlyList<string> SystemRootNames => DefaultSystemRoots;
}

public readonly record struct FileSearchRank(FileSearchLocation Location, FileSearchTextMatch TextMatch, int PathDepth, int PathLength);

public static class FileSearchRanking
{
    private const uint HiddenAttribute = 0x2;
    private const uint SystemAttribute = 0x4;
    internal const string TokenSeparators = " -_.()[]{};, +&!@#$^=~`\"'";

    public static FileSearchRank Classify(FileSearchResult result, string query, FileSearchRankingContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(context);

        var pathSegments = FileSearchRankingContext.GetSegments(result.FullPath);
        return new FileSearchRank(
            ClassifyLocation(result, pathSegments, context),
            ClassifyTextMatch(result.Name, query),
            pathSegments.Count,
            result.FullPath?.Length ?? int.MaxValue);
    }

    public static int Compare(FileSearchResult left, FileSearchResult right, string query, FileSearchRankingContext context)
    {
        var leftRank = Classify(left, query, context);
        var rightRank = Classify(right, query, context);

        var comparison = leftRank.Location.CompareTo(rightRank.Location);
        if (comparison != 0) return comparison;
        comparison = leftRank.TextMatch.CompareTo(rightRank.TextMatch);
        if (comparison != 0) return comparison;
        comparison = leftRank.PathDepth.CompareTo(rightRank.PathDepth);
        if (comparison != 0) return comparison;
        comparison = leftRank.PathLength.CompareTo(rightRank.PathLength);
        if (comparison != 0) return comparison;
        comparison = SqliteNoCaseComparer.Instance.Compare(left.Name, right.Name);
        if (comparison != 0) return comparison;
        comparison = Utf8BinaryComparer.Instance.Compare(left.Name, right.Name);
        if (comparison != 0) return comparison;
        comparison = StringComparer.Ordinal.Compare(left.FullPath, right.FullPath);
        if (comparison != 0) return comparison;
        return StringComparer.Ordinal.Compare(left.FileId.ToString(), right.FileId.ToString());
    }

    public static FileSearchTextMatch ClassifyTextMatch(string name, string query)
    {
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) return FileSearchTextMatch.Exact;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return FileSearchTextMatch.Prefix;

        for (var index = 1; index <= name.Length - query.Length; index++)
        {
            if (!IsTokenSeparator(name[index - 1])) continue;
            if (name.AsSpan(index).StartsWith(query, StringComparison.OrdinalIgnoreCase)) return FileSearchTextMatch.TokenPrefix;
        }

        return FileSearchTextMatch.Substring;
    }

    private static FileSearchLocation ClassifyLocation(
        FileSearchResult result,
        IReadOnlyList<string> pathSegments,
        FileSearchRankingContext context)
    {
        if (IsSystemHeavy(pathSegments, context)) return FileSearchLocation.SystemHeavy;

        var currentUserSegments = FileSearchRankingContext.GetSegments(context.CurrentUserProfilePath);
        if (StartsWith(pathSegments, currentUserSegments))
        {
            return IsInternal(pathSegments, currentUserSegments.Count, result.Attributes)
                ? FileSearchLocation.CurrentUserInternal
                : FileSearchLocation.CurrentUserVisible;
        }

        if (currentUserSegments.Count >= 2 && StartsWith(pathSegments, currentUserSegments.Take(currentUserSegments.Count - 1).ToArray()))
        {
            return IsInternal(pathSegments, currentUserSegments.Count, result.Attributes)
                ? FileSearchLocation.OtherUserInternal
                : FileSearchLocation.OtherUserVisible;
        }

        return IsInternal(attributes: result.Attributes)
            ? FileSearchLocation.OtherInternal
            : FileSearchLocation.OtherVisible;
    }

    private static bool IsSystemHeavy(IReadOnlyList<string> pathSegments, FileSearchRankingContext context)
    {
        foreach (var systemRoot in context.SystemRootPaths)
        {
            if (StartsWith(pathSegments, FileSearchRankingContext.GetSegments(systemRoot))) return true;
        }

        if (pathSegments.Count < 2) return false;
        return FileSearchRankingContext.SystemRootNames.Any(name =>
            string.Equals(pathSegments[1], name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInternal(IReadOnlyList<string> pathSegments, int userRootLength, uint attributes)
    {
        if (IsInternal(attributes)) return true;
        return pathSegments.Count > userRootLength && string.Equals(pathSegments[userRootLength], "AppData", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInternal(uint attributes) => (attributes & (HiddenAttribute | SystemAttribute)) != 0;

    private static bool StartsWith(IReadOnlyList<string> value, IReadOnlyList<string> prefix)
    {
        if (prefix.Count == 0 || value.Count < prefix.Count) return false;
        for (var index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(value[index], prefix[index], StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private static bool IsTokenSeparator(char value) => TokenSeparators.IndexOf(value) >= 0;
}
