using System.Text.Json;
using Quail.App;

namespace Quail.Core.Tests;

public sealed class SearchPerformanceTraceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuailTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Trace_records_only_privacy_safe_search_metadata()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "trace.jsonl");
        using (var trace = new SearchPerformanceTrace(path, SearchPerformanceSessionKind.Parse("warm-same-session")))
        {
            trace.RecordSessionStart(new Quail.App.SearchIndexScale(1, 888_708, 123_456_789, 0));
            trace.RecordScenarioStarted("ordinary-name");
            trace.RecordInput(7, 4);
            trace.RecordCoordinator(new SearchCoordinatorTraceEvent(SearchCoordinatorStage.RequestEnqueued, 3, 7, 4, System.Diagnostics.Stopwatch.GetTimestamp()));
            trace.RecordResultMapping(7, 3, 50, TimeSpan.FromMilliseconds(1));
            trace.RecordFirstTextRender(7, 3, 50);
            trace.RecordIconCompleted(7, 3, 0, TimeSpan.FromMilliseconds(2), applied: true);
            trace.RecordScenarioCompleted();
        }

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("sensitive-query-value", text, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fileName", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("machine", text, StringComparison.OrdinalIgnoreCase);

        var events = File.ReadLines(path).Select(line => JsonSerializer.Deserialize<SearchPerformanceTraceEvent>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web))!).ToArray();
        Assert.NotEmpty(events);
        Assert.All(events, traceEvent =>
        {
            Assert.NotEmpty(traceEvent.RunId);
            Assert.Equal("warm-same-session", traceEvent.SessionKind);
            Assert.True(traceEvent.MonotonicMilliseconds >= 0);
        });
        Assert.Contains(events, traceEvent => traceEvent.Stage == "first-text-results-rendered" && traceEvent.ResultCount == 50);
        Assert.Contains(events, traceEvent => traceEvent.Stage == "request-enqueued" && traceEvent.Lane == "Interactive");
        Assert.Contains(events, traceEvent => traceEvent.Stage == "scenario-start" && traceEvent.ScenarioId == "ordinary-name");
    }

    [Fact]
    public async Task Coordinator_trace_keeps_enqueue_start_and_completion_on_one_clock()
    {
        var events = new List<SearchCoordinatorTraceEvent>();
        using var completed = new SemaphoreSlim(0);
        using var coordinator = new LatestSearchCoordinator(
            _ => [],
            traceEvent => events.Add(traceEvent));
        coordinator.Completed += _ => completed.Release();

        coordinator.Request("four", uiGeneration: 11);
        await completed.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            [
                SearchCoordinatorStage.RequestEnqueued,
                SearchCoordinatorStage.WorkerDequeued,
                SearchCoordinatorStage.CoreSearchStarted,
                SearchCoordinatorStage.CoreSearchCompleted
            ],
            events.Select(traceEvent => traceEvent.Stage));
        Assert.All(events, traceEvent =>
        {
            Assert.Equal(1, traceEvent.Generation);
            Assert.Equal(11, traceEvent.UiGeneration);
            Assert.Equal(4, traceEvent.QueryLength);
            Assert.Equal(SearchExecutionLane.Interactive, traceEvent.Lane);
        });
        Assert.True(events.Zip(events.Skip(1)).All(pair => pair.First.Timestamp <= pair.Second.Timestamp));
    }

    [Fact]
    public void Trace_leaves_warmup_input_unlabelled_before_the_measured_scenario()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "trace.jsonl");
        using (var trace = new SearchPerformanceTrace(path, SearchPerformanceSessionKind.Parse("warm-same-session")))
        {
            trace.RecordInput(1, 6);
            trace.RecordScenarioStarted("ordinary-name");
            trace.RecordInput(2, 5);
            trace.RecordScenarioCompleted();
        }

        var events = File.ReadLines(path)
            .Select(line => JsonSerializer.Deserialize<SearchPerformanceTraceEvent>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .ToArray();

        Assert.Contains(events, traceEvent => traceEvent.Stage == "input-observed" && traceEvent.UiGeneration == 1 && traceEvent.ScenarioId is null);
        Assert.Contains(events, traceEvent => traceEvent.Stage == "input-observed" && traceEvent.UiGeneration == 2 && traceEvent.ScenarioId == "ordinary-name");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
