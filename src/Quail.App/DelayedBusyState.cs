namespace Quail.App;

internal sealed class DelayedBusyState
{
    internal static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(225);

    private long? _pendingGeneration;

    public bool IsVisible { get; private set; }

    public void Begin(long generation)
    {
        _pendingGeneration = generation;
        IsVisible = false;
    }

    public bool TryShow(long generation)
    {
        if (_pendingGeneration != generation || IsVisible)
        {
            return false;
        }

        IsVisible = true;
        return true;
    }

    public bool Complete(long generation)
    {
        if (_pendingGeneration != generation)
        {
            return false;
        }

        _pendingGeneration = null;
        IsVisible = false;
        return true;
    }

    public void Cancel()
    {
        _pendingGeneration = null;
        IsVisible = false;
    }
}
