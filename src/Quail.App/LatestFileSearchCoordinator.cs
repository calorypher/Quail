using System.Diagnostics;
using Quail.Core;

namespace Quail.App;

internal enum SearchExecutionLane
{
    Interactive,
    ShortQuery
}

internal sealed record SearchCompletion(
    long Generation,
    long UiGeneration,
    IReadOnlyList<IndexedFileSearchResult>? Results,
    Exception? Error,
    TimeSpan Duration,
    bool IsCurrent,
    long EnqueuedTimestamp,
    long SearchStartedTimestamp,
    long SearchCompletedTimestamp,
    SearchExecutionLane Lane);

internal enum SearchCoordinatorStage
{
    RequestEnqueued,
    WorkerDequeued,
    CoreSearchStarted,
    CoreSearchCompleted
}

internal readonly record struct SearchCoordinatorTraceEvent(
    SearchCoordinatorStage Stage,
    long Generation,
    long UiGeneration,
    int QueryLength,
    long Timestamp,
    TimeSpan? Duration = null,
    TimeSpan? QueueWait = null,
    SearchExecutionLane Lane = SearchExecutionLane.Interactive);

internal sealed class LatestFileSearchCoordinator : IDisposable
{
    private readonly Func<string, IReadOnlyList<IndexedFileSearchResult>> _search;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _requestSignal = new(0, 1);
    private readonly Task _worker;
    private readonly Action<SearchCoordinatorTraceEvent>? _trace;
    private readonly SearchExecutionLane _lane;
    private (long Generation, long UiGeneration, string Query, int QueryLength, long EnqueuedTimestamp)? _pendingRequest;
    private bool _signalPending;
    private long _latestGeneration;
    private bool _disposed;

    public LatestFileSearchCoordinator(
        Func<string, IReadOnlyList<IndexedFileSearchResult>> search,
        Action<SearchCoordinatorTraceEvent>? trace = null,
        SearchExecutionLane lane = SearchExecutionLane.Interactive)
    {
        _search = search;
        _trace = trace;
        _lane = lane;
        _worker = Task.Run(RunAsync);
    }

    public event Action<SearchCompletion>? Completed;

    public SearchExecutionLane Lane => _lane;

    public long Request(string query) => Request(query, 0);

    public long Request(string query, long uiGeneration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var enqueuedTimestamp = Stopwatch.GetTimestamp();
        long generation;
        lock (_gate)
        {
            ThrowIfDisposed();
            generation = ++_latestGeneration;
            _pendingRequest = (generation, uiGeneration, query, query.Length, enqueuedTimestamp);
            _trace?.Invoke(new SearchCoordinatorTraceEvent(SearchCoordinatorStage.RequestEnqueued, generation, uiGeneration, query.Length, enqueuedTimestamp, Lane: _lane));
            SignalWorkerIfNeeded();
        }

        return generation;
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _latestGeneration++;
                _pendingRequest = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _latestGeneration++;
            _pendingRequest = null;
            SignalWorkerIfNeeded();
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            await _requestSignal.WaitAsync();
            (long Generation, long UiGeneration, string Query, int QueryLength, long EnqueuedTimestamp) request;
            lock (_gate)
            {
                _signalPending = false;
                if (_disposed)
                {
                    return;
                }

                if (_pendingRequest is null)
                {
                    continue;
                }

                request = _pendingRequest.Value;
                _pendingRequest = null;
            }

            var dequeuedTimestamp = Stopwatch.GetTimestamp();
            _trace?.Invoke(new SearchCoordinatorTraceEvent(SearchCoordinatorStage.WorkerDequeued, request.Generation, request.UiGeneration, request.QueryLength, dequeuedTimestamp, Lane: _lane));
            var searchStartedTimestamp = Stopwatch.GetTimestamp();
            _trace?.Invoke(new SearchCoordinatorTraceEvent(
                SearchCoordinatorStage.CoreSearchStarted,
                request.Generation,
                request.UiGeneration,
                request.QueryLength,
                searchStartedTimestamp,
                QueueWait: Stopwatch.GetElapsedTime(request.EnqueuedTimestamp, searchStartedTimestamp),
                Lane: _lane));
            IReadOnlyList<IndexedFileSearchResult>? results = null;
            Exception? error = null;
            try
            {
                results = _search(request.Query);
            }
            catch (Exception exception)
            {
                error = exception;
            }

            var searchCompletedTimestamp = Stopwatch.GetTimestamp();
            var duration = Stopwatch.GetElapsedTime(searchStartedTimestamp, searchCompletedTimestamp);
            _trace?.Invoke(new SearchCoordinatorTraceEvent(
                SearchCoordinatorStage.CoreSearchCompleted,
                request.Generation,
                request.UiGeneration,
                request.QueryLength,
                searchCompletedTimestamp,
                Duration: duration,
                Lane: _lane));
            bool current;
            bool deliverCompletion;
            lock (_gate)
            {
                current = !_disposed && request.Generation == _latestGeneration;
                deliverCompletion = !_disposed;
            }

            if (deliverCompletion)
            {
                Completed?.Invoke(new SearchCompletion(
                    request.Generation,
                    request.UiGeneration,
                    results,
                    error,
                    duration,
                    current,
                    request.EnqueuedTimestamp,
                    searchStartedTimestamp,
                    searchCompletedTimestamp,
                    _lane));
            }
        }
    }

    private void SignalWorkerIfNeeded()
    {
        if (_signalPending)
        {
            return;
        }

        _signalPending = true;
        _requestSignal.Release();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LatestFileSearchCoordinator));
        }
    }
}
