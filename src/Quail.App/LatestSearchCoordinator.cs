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
    IReadOnlyList<SearchResult>? Results,
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

internal sealed class LatestSearchCoordinator : IDisposable
{
    private readonly Func<SearchRequest, IReadOnlyList<SearchResult>> _search;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _requestSignal = new(0, 1);
    private readonly Task _worker;
    private readonly Action<SearchCoordinatorTraceEvent>? _trace;
    private readonly SearchExecutionLane _lane;
    private SearchRequestState? _pendingRequest;
    private SearchRequestState? _runningRequest;
    private bool _signalPending;
    private long _latestGeneration;
    private bool _disposed;

    public LatestSearchCoordinator(
        Func<string, IReadOnlyList<SearchResult>> search,
        Action<SearchCoordinatorTraceEvent>? trace = null,
        SearchExecutionLane lane = SearchExecutionLane.Interactive)
    {
        ArgumentNullException.ThrowIfNull(search);
        _search = request => search(request.Query);
        _trace = trace;
        _lane = lane;
        _worker = Task.Run(RunAsync);
    }

    private LatestSearchCoordinator(
        Func<SearchRequest, IReadOnlyList<SearchResult>> search,
        Action<SearchCoordinatorTraceEvent>? trace = null,
        SearchExecutionLane lane = SearchExecutionLane.Interactive)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _trace = trace;
        _lane = lane;
        _worker = Task.Run(RunAsync);
    }

    public static LatestSearchCoordinator ForRequests(
        Func<SearchRequest, IReadOnlyList<SearchResult>> search,
        Action<SearchCoordinatorTraceEvent>? trace = null,
        SearchExecutionLane lane = SearchExecutionLane.Interactive) =>
        new(search, trace, lane);

    public event Action<SearchCompletion>? Completed;

    public SearchExecutionLane Lane => _lane;

    public long Request(string query) => Request(query, 0);

    public long Request(string query, long uiGeneration)
        => Request(new SearchRequest(query), uiGeneration);

    public long Request(SearchRequest request) => Request(request, 0);

    public long Request(SearchRequest request, long uiGeneration)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);
        var enqueuedTimestamp = Stopwatch.GetTimestamp();
        long generation;
        SearchCoordinatorTraceEvent? activeSearchTrace = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            generation = ++_latestGeneration;
            if (_runningRequest is not null && Equals(_runningRequest.Request, request))
            {
                _runningRequest.Update(generation, uiGeneration, enqueuedTimestamp);
                if (_runningRequest.SearchStartedTimestamp is long searchStartedTimestamp)
                {
                    activeSearchTrace = new SearchCoordinatorTraceEvent(
                        SearchCoordinatorStage.CoreSearchStarted,
                        generation,
                        uiGeneration,
                        request.Query.Length,
                        enqueuedTimestamp,
                        QueueWait: TimeSpan.Zero,
                        Lane: _lane);
                }
            }
            else if (_pendingRequest is not null && Equals(_pendingRequest.Request, request))
            {
                _pendingRequest.Update(generation, uiGeneration, enqueuedTimestamp);
            }
            else
            {
                _pendingRequest = new SearchRequestState(generation, uiGeneration, request, enqueuedTimestamp);
            }

            _trace?.Invoke(new SearchCoordinatorTraceEvent(SearchCoordinatorStage.RequestEnqueued, generation, uiGeneration, request.Query.Length, enqueuedTimestamp, Lane: _lane));
            if (_runningRequest is null || !Equals(_runningRequest.Request, request))
            {
                SignalWorkerIfNeeded();
            }
        }

        if (activeSearchTrace is SearchCoordinatorTraceEvent traceEvent)
        {
            _trace?.Invoke(traceEvent);
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
            SearchRequestState request;
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

                request = _pendingRequest;
                _pendingRequest = null;
                _runningRequest = request;
            }

            var dequeuedTimestamp = Stopwatch.GetTimestamp();
            _trace?.Invoke(new SearchCoordinatorTraceEvent(SearchCoordinatorStage.WorkerDequeued, request.Generation, request.UiGeneration, request.QueryLength, dequeuedTimestamp, Lane: _lane));
            var searchStartedTimestamp = Stopwatch.GetTimestamp();
            request.SearchStartedTimestamp = searchStartedTimestamp;
            _trace?.Invoke(new SearchCoordinatorTraceEvent(
                SearchCoordinatorStage.CoreSearchStarted,
                request.Generation,
                request.UiGeneration,
                request.QueryLength,
                searchStartedTimestamp,
                QueueWait: Stopwatch.GetElapsedTime(request.EnqueuedTimestamp, searchStartedTimestamp),
                Lane: _lane));
            IReadOnlyList<SearchResult>? results = null;
            Exception? error = null;
            try
            {
                results = _search(request.Request);
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
                if (ReferenceEquals(_runningRequest, request))
                {
                    _runningRequest = null;
                }
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
            throw new ObjectDisposedException(nameof(LatestSearchCoordinator));
        }
    }

    private sealed class SearchRequestState(long generation, long uiGeneration, SearchRequest request, long enqueuedTimestamp)
    {
        public long Generation { get; private set; } = generation;
        public long UiGeneration { get; private set; } = uiGeneration;
        public SearchRequest Request { get; } = request;
        public string Query => Request.Query;
        public int QueryLength => Request.Query.Length;
        public long EnqueuedTimestamp { get; private set; } = enqueuedTimestamp;
        public long? SearchStartedTimestamp { get; set; }

        public void Update(long generation, long uiGeneration, long enqueuedTimestamp)
        {
            Generation = generation;
            UiGeneration = uiGeneration;
            EnqueuedTimestamp = enqueuedTimestamp;
        }
    }
}
