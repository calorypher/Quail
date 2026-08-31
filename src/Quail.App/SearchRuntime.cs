using Quail.Core;

namespace Quail.App;

internal sealed class SearchRuntime : IDisposable
{
    private readonly Action _dispose;
    private bool _disposed;

    public SearchRuntime(
        SearchApplicationService search,
        Func<bool> hasSources,
        Func<SearchIndexScale> getIndexScale,
        Func<IndexFreshness?> getFreshness,
        Action dispose)
    {
        Search = search ?? throw new ArgumentNullException(nameof(search));
        HasSources = hasSources ?? throw new ArgumentNullException(nameof(hasSources));
        GetIndexScale = getIndexScale ?? throw new ArgumentNullException(nameof(getIndexScale));
        GetFreshness = getFreshness ?? throw new ArgumentNullException(nameof(getFreshness));
        _dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
    }

    public SearchApplicationService Search { get; }
    public Func<bool> HasSources { get; }
    public Func<SearchIndexScale> GetIndexScale { get; }
    public Func<IndexFreshness?> GetFreshness { get; }

    public event Action? SourcesChanged;

    public void NotifySourcesChanged() => SourcesChanged?.Invoke();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispose();
    }

}
