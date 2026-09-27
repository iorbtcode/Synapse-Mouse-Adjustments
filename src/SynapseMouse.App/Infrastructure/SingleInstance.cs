namespace SynapseMouse.App.Infrastructure;

/// <summary>
/// Ensures one running instance per user session. Launching the app again just asks the running
/// instance to show its window.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\SynapseMouseAdjustments.SingleInstance";
    private const string ShowEventName = @"Local\SynapseMouseAdjustments.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (!created)
        {
            bool owned = false;
            try
            {
                // A previous instance that crashed leaves an abandoned mutex we may take over.
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            if (!owned)
            {
                mutex.Dispose();
                return null;
            }
        }

        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        return new SingleInstance(mutex, showEvent);
    }

    /// <summary>Asks the already-running instance to show its main window.</summary>
    public static void SignalExisting()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // Nothing else to do.
        }
    }

    public void ListenForShowRequests(Action onShow)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread any more.
        }

        _mutex.Dispose();
    }
}
