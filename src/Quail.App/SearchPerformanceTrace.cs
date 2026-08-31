using System.Diagnostics;
using System.Text.Json;

namespace Quail.App;

internal sealed record SearchIndexScale(
    int IndexCount,
    long RecordCount,
    long DatabaseBytes,
    int UnavailableIndexCount);

internal sealed class SearchPerformanceSessionKind
{
    private SearchPerformanceSessionKind(string value) => Value = value;

    public string Value { get; }

    public static SearchPerformanceSessionKind Parse(string value) => value switch
    {
        "fresh-process-first-search" => new SearchPerformanceSessionKind(value),
        "warm-same-session" => new SearchPerformanceSessionKind(value),
        _ => throw new ArgumentException("--search-performance-session-kind must be fresh-process-first-search or warm-same-session.")
    };
}

internal sealed class SearchPerformanceTrace : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly long _originTimestamp;
    private readonly string _runId = string.Empty;
    private readonly string _sessionKind = string.Empty;
    private readonly StreamWriter? _writer;
    private bool _disposed;

    public SearchPerformanceTrace(string? path, SearchPerformanceSessionKind? sessionKind)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _originTimestamp = Stopwatch.GetTimestamp();
        _runId = Guid.NewGuid().ToString("N");
        _sessionKind = sessionKind?.Value ?? "unspecified";
        _writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
    }

    public bool IsEnabled => _writer is not null;

    public void RecordSessionStart() => Record("session-start", includeProcessMetrics: true);

    public void RecordSessionStart(SearchIndexScale scale)
    {
        Record("session-start", indexCount: scale.IndexCount, recordCount: scale.RecordCount, databaseBytes: scale.DatabaseBytes, unavailableIndexCount: scale.UnavailableIndexCount, includeProcessMetrics: true);
    }

    public void RecordInput(long uiGeneration, int queryLength) =>
        Record("input-observed", uiGeneration, queryLength: queryLength);

    public void RecordShortQueryDeferred(long uiGeneration, int queryLength) =>
        Record("short-query-deferred", uiGeneration, queryLength: queryLength);

    public void RecordShortQueryReleased(long uiGeneration, int queryLength) =>
        Record("short-query-released", uiGeneration, queryLength: queryLength);

    public void RecordCoordinator(SearchCoordinatorTraceEvent traceEvent)
    {
        var stage = traceEvent.Stage switch
        {
            SearchCoordinatorStage.RequestEnqueued => "request-enqueued",
            SearchCoordinatorStage.WorkerDequeued => "worker-dequeued",
            SearchCoordinatorStage.CoreSearchStarted => "core-search-started",
            SearchCoordinatorStage.CoreSearchCompleted => "core-search-completed",
            _ => throw new ArgumentOutOfRangeException(nameof(traceEvent))
        };

        Record(
            stage,
            traceEvent.UiGeneration,
            traceEvent.Generation,
            traceEvent.QueryLength,
            lane: traceEvent.Lane.ToString(),
            durationMilliseconds: traceEvent.Duration?.TotalMilliseconds,
            queueWaitMilliseconds: traceEvent.QueueWait?.TotalMilliseconds,
            timestamp: traceEvent.Timestamp);
    }

    public void RecordCompletionDispatch(SearchCompletion completion) =>
        Record("completion-dispatched", completion.UiGeneration, completion.Generation, timestamp: completion.SearchCompletedTimestamp);

    public void RecordUiDispatchStarted(SearchCompletion completion) =>
        Record("ui-dispatch-started", completion.UiGeneration, completion.Generation);

    public void RecordResultMapping(long uiGeneration, long searchGeneration, int resultCount, TimeSpan duration) =>
        Record("result-mapping-completed", uiGeneration, searchGeneration, resultCount: resultCount, durationMilliseconds: duration.TotalMilliseconds);

    public void RecordResultApply(long uiGeneration, long searchGeneration, int resultCount, TimeSpan duration) =>
        Record("result-apply-completed", uiGeneration, searchGeneration, resultCount: resultCount, durationMilliseconds: duration.TotalMilliseconds);

    public void RecordSelectionAndScroll(long uiGeneration, long searchGeneration, TimeSpan duration) =>
        Record("selection-scroll-completed", uiGeneration, searchGeneration, durationMilliseconds: duration.TotalMilliseconds);

    public void RecordSourceStatus(long uiGeneration, long searchGeneration, TimeSpan duration) =>
        Record("source-status-completed", uiGeneration, searchGeneration, durationMilliseconds: duration.TotalMilliseconds);

    public void RecordFirstTextRender(long uiGeneration, long searchGeneration, int resultCount) =>
        Record("first-text-results-rendered", uiGeneration, searchGeneration, resultCount: resultCount, includeProcessMetrics: true);

    public void RecordIconStarted(long uiGeneration, long searchGeneration, int resultIndex) =>
        Record("icon-load-started", uiGeneration, searchGeneration, resultIndex: resultIndex);

    public void RecordIconCompleted(long uiGeneration, long searchGeneration, int resultIndex, TimeSpan duration, bool applied) =>
        Record(applied ? "icon-load-applied" : "icon-load-completed-not-applied", uiGeneration, searchGeneration, resultIndex: resultIndex, durationMilliseconds: duration.TotalMilliseconds);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
        }
    }

    private void Record(
        string stage,
        long uiGeneration = 0,
        long searchGeneration = 0,
        int? queryLength = null,
        string? lane = null,
        int? resultCount = null,
        int? resultIndex = null,
        double? durationMilliseconds = null,
        double? queueWaitMilliseconds = null,
        long? timestamp = null,
        int? indexCount = null,
        long? recordCount = null,
        long? databaseBytes = null,
        int? unavailableIndexCount = null,
        bool includeProcessMetrics = false)
    {
        if (_writer is null)
        {
            return;
        }

        var eventTimestamp = timestamp ?? Stopwatch.GetTimestamp();
        double? cpuMilliseconds = null;
        long? workingSetBytes = null;
        if (includeProcessMetrics)
        {
            using var process = Process.GetCurrentProcess();
            cpuMilliseconds = process.TotalProcessorTime.TotalMilliseconds;
            workingSetBytes = process.WorkingSet64;
        }

        var payload = new SearchPerformanceTraceEvent(
            _runId,
            _sessionKind,
            stage,
            Stopwatch.GetElapsedTime(_originTimestamp, eventTimestamp).TotalMilliseconds,
            uiGeneration,
            searchGeneration,
            queryLength,
            lane,
            resultCount,
            resultIndex,
            durationMilliseconds,
            queueWaitMilliseconds,
            indexCount,
            recordCount,
            databaseBytes,
            unavailableIndexCount,
            cpuMilliseconds,
            workingSetBytes);

        lock (_gate)
        {
            if (!_disposed)
            {
                _writer.WriteLine(JsonSerializer.Serialize(payload, SerializerOptions));
                if (stage is "core-search-started" or "core-search-completed" or "first-text-results-rendered" or "icon-load-applied" or "icon-load-completed-not-applied")
                {
                    _writer.Flush();
                }
            }
        }
    }
}

internal sealed record SearchPerformanceTraceEvent(
    string RunId,
    string SessionKind,
    string Stage,
    double MonotonicMilliseconds,
    long UiGeneration,
    long SearchGeneration,
    int? QueryLength,
    string? Lane,
    int? ResultCount,
    int? ResultIndex,
    double? DurationMilliseconds,
    double? QueueWaitMilliseconds,
    int? IndexCount,
    long? RecordCount,
    long? DatabaseBytes,
    int? UnavailableIndexCount,
    double? ProcessCpuMilliseconds,
    long? WorkingSetBytes);
