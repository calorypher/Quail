using Quail.App;

namespace Quail.Core.Tests;

public sealed class AppLaunchOptionsTests
{
    [Fact]
    public void Parse_accepts_repeated_indexes_in_input_order()
    {
        var options = AppLaunchOptions.Parse(["--index", "one.db", "--index", "two.db", "--show-on-start", "--test-event-pipe", "events", "--diagnostics-path", "diagnostics.log", "--test-exit-after-visible-ready-count", "2"]);

        Assert.Equal([Path.GetFullPath("one.db"), Path.GetFullPath("two.db")], options.IndexPaths);
        Assert.True(options.ShowOnStart);
        Assert.Equal("events", options.TestEventPipeName);
        Assert.Equal("diagnostics.log", options.DiagnosticsPath);
        Assert.Null(options.SearchPerformanceTracePath);
        Assert.Null(options.SearchPerformanceSessionKind);
        Assert.Equal(2, options.ExitAfterVisibleReadyCount);
    }

    [Fact]
    public void Parse_allows_no_index_and_rejects_missing_or_option_index_values()
    {
        Assert.Empty(AppLaunchOptions.Parse([]).IndexPaths);
        Assert.Throws<ArgumentException>(() => AppLaunchOptions.Parse(["--index"]));
        Assert.Throws<ArgumentException>(() => AppLaunchOptions.Parse(["--index", "--show-on-start"]));
        Assert.Throws<ArgumentException>(() => AppLaunchOptions.Parse(["--test-exit-after-visible-ready-count", "0"]));
        Assert.Throws<ArgumentException>(() => AppLaunchOptions.Parse(["--search-performance-session-kind", "warm-same-session"]));
        Assert.Throws<ArgumentException>(() => AppLaunchOptions.Parse(["--search-performance-trace", "trace.jsonl", "--search-performance-session-kind", "not-a-session-kind"]));
    }

    [Fact]
    public void Parse_accepts_private_search_performance_trace_options()
    {
        var options = AppLaunchOptions.Parse(["--search-performance-trace", "trace.jsonl", "--search-performance-session-kind", "fresh-process-first-search"]);

        Assert.Equal(Path.GetFullPath("trace.jsonl"), options.SearchPerformanceTracePath);
        Assert.Equal("fresh-process-first-search", options.SearchPerformanceSessionKind!.Value);
    }

    [Fact]
    public void Parse_does_not_support_legacy_milestone_arguments()
    {
        var options = AppLaunchOptions.Parse([
            "--m08-pipe", "legacy-pipe",
            "--m08-diagnostics", "legacy-diagnostics.log",
            "--m08-show-on-start",
            "--m08-test-exit-after-visible-ready-count", "2",
            "--m10-pipe", "legacy-pipe",
            "--m10-diagnostics", "legacy-diagnostics.log",
            "--m10-show-on-start",
            "--m10-test-exit-after-visible-ready-count", "2"]);

        Assert.Null(options.TestEventPipeName);
        Assert.Null(options.DiagnosticsPath);
        Assert.False(options.ShowOnStart);
        Assert.Null(options.ExitAfterVisibleReadyCount);
    }
}
