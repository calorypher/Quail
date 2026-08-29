namespace Quail.App;

internal sealed record AppLaunchOptions(
    string? TestEventPipeName,
    string? DiagnosticsPath,
    string? SearchPerformanceTracePath,
    SearchPerformanceSessionKind? SearchPerformanceSessionKind,
    bool ShowOnStart,
    int? ExitAfterVisibleReadyCount,
    IReadOnlyList<string> IndexPaths)
{
    public static AppLaunchOptions Parse(IEnumerable<string> arguments)
    {
        string? pipeName = null;
        string? diagnosticsPath = null;
        string? searchPerformanceTracePath = null;
        SearchPerformanceSessionKind? searchPerformanceSessionKind = null;
        var showOnStart = false;
        int? exitAfterVisibleReadyCount = null;
        var indexPaths = new List<string>();
        var values = arguments.ToArray();

        for (var index = 0; index < values.Length; index++)
        {
            switch (values[index])
            {
                case "--test-event-pipe" when index + 1 < values.Length:
                    pipeName = values[++index];
                    break;
                case "--diagnostics-path" when index + 1 < values.Length:
                    diagnosticsPath = values[++index];
                    break;
                case "--search-performance-trace" when index + 1 < values.Length:
                    searchPerformanceTracePath = Path.GetFullPath(values[++index]);
                    break;
                case "--search-performance-session-kind" when index + 1 < values.Length:
                    searchPerformanceSessionKind = SearchPerformanceSessionKind.Parse(values[++index]);
                    break;
                case "--show-on-start":
                    showOnStart = true;
                    break;
                case "--test-exit-after-visible-ready-count" when index + 1 < values.Length:
                    exitAfterVisibleReadyCount = int.Parse(values[++index]);
                    break;
                case "--index" when index + 1 < values.Length && !values[index + 1].StartsWith("--", StringComparison.Ordinal):
                    indexPaths.Add(Path.GetFullPath(values[++index]));
                    break;
                case "--index":
                    throw new ArgumentException("--index requires a database path.");
            }
        }

        if (exitAfterVisibleReadyCount is <= 0)
        {
            throw new ArgumentException("--test-exit-after-visible-ready-count must be positive.");
        }

        if (searchPerformanceSessionKind is not null && searchPerformanceTracePath is null)
        {
            throw new ArgumentException("--search-performance-session-kind requires --search-performance-trace.");
        }

        return new AppLaunchOptions(
            pipeName,
            diagnosticsPath,
            searchPerformanceTracePath,
            searchPerformanceSessionKind,
            showOnStart,
            exitAfterVisibleReadyCount,
            indexPaths);
    }
}
