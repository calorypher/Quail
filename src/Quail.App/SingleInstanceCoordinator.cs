namespace Quail.App;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = "Local\\Quail.DesktopShell";
    private const string ActivationEventName = "Local\\Quail.DesktopShell.Activate";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activationEvent;
    private readonly RegisteredWaitHandle? _registeredWait;

    private SingleInstanceCoordinator(Mutex mutex, bool isPrimary)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
        if (!isPrimary)
        {
            return;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => ActivationRequested?.Invoke(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public bool IsPrimary { get; }

    public event Action? ActivationRequested;

    public static SingleInstanceCoordinator Acquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var isPrimary);
        return new SingleInstanceCoordinator(mutex, isPrimary);
    }

    public void ActivatePrimary()
    {
        using var activationEvent = EventWaitHandle.OpenExisting(ActivationEventName);
        activationEvent.Set();
    }

    public void Dispose()
    {
        _registeredWait?.Unregister(null);
        _activationEvent?.Dispose();
        if (IsPrimary)
        {
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
    }
}
