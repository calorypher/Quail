namespace Quail.App;

internal sealed class SearchPerformanceRenderWaiter
{
    private string? _awaitedQuery;
    private long? _awaitedUiGeneration;
    private TaskCompletionSource? _completion;

    public Task PrepareForQuery(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        _awaitedQuery = query.Trim();
        _awaitedUiGeneration = null;
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _completion.Task;
    }

    public void ObserveProcessedInput(string query, long uiGeneration)
    {
        if (string.IsNullOrWhiteSpace(query) ||
            _completion is null ||
            !string.Equals(_awaitedQuery, query, StringComparison.Ordinal))
        {
            return;
        }

        _awaitedUiGeneration = uiGeneration;
    }

    public void ObserveFirstTextRender(long uiGeneration)
    {
        if (_awaitedUiGeneration != uiGeneration)
        {
            return;
        }

        _completion?.TrySetResult();
        _awaitedQuery = null;
        _awaitedUiGeneration = null;
        _completion = null;
    }
}
