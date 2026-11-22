using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Services;

[SupportedOSPlatform("windows")]
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private static readonly string InstanceSuffix = CreateInstanceSuffix();
    private static string GetObjectName(string applicationName, string name) => $@"Local\{applicationName}-{InstanceSuffix}-{name}";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly EventWaitHandle _shutdownEvent;
    private readonly RegisteredWaitHandle _waitRegistration;
    private readonly RegisteredWaitHandle _shutdownRegistration;
    private readonly object _handlersLock = new();
    private Action? _activationHandler;
    private Action? _shutdownHandler;
    private bool _activationPending;
    private bool _shutdownPending;
    private int _disposed;

    private SingleInstanceCoordinator(Mutex mutex, EventWaitHandle activationEvent, EventWaitHandle shutdownEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _shutdownEvent = shutdownEvent;
        _waitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            static (state, timedOut) =>
            {
                if (!timedOut) ((SingleInstanceCoordinator)state!).OnActivationRequested();
            },
            this,
            Timeout.Infinite,
            executeOnlyOnce: false);
        _shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
            _shutdownEvent,
            static (state, timedOut) =>
            {
                if (!timedOut) ((SingleInstanceCoordinator)state!).OnShutdownRequested();
            },
            this,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public static SingleInstanceCoordinator? StartOrSignalExisting(out bool shouldStart, string applicationName = "SQLitePass")
    {
        string mutexName = GetObjectName(applicationName, "Mutex");
        var mutex = new Mutex(initiallyOwned: false, mutexName);
        bool ownsMutex;
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }

        if (ownsMutex)
        {
            shouldStart = true;
            return CreatePrimary(mutex, applicationName);
        }

        mutex.Dispose();
        if (TrySignalExisting(applicationName))
        {
            shouldStart = false;
            return null;
        }

        // The existing process may be exiting or still creating its activation event.
        // Retry ownership once; if coordination is unavailable, start visibly as a fallback.
        var retryMutex = new Mutex(initiallyOwned: false, mutexName);
        try
        {
            try { ownsMutex = retryMutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (ownsMutex)
            {
                shouldStart = true;
                return CreatePrimary(retryMutex, applicationName);
            }
        }
        catch
        {
            // Let the UI start even if the optional single-instance coordination fails.
        }

        retryMutex.Dispose();
        shouldStart = true;
        return null;
    }

    public void RegisterActivationHandler(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        bool pending;
        lock (_handlersLock)
        {
            _activationHandler = handler;
            pending = _activationPending;
            _activationPending = false;
        }
        if (pending) handler();
    }

    public void RegisterShutdownHandler(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        bool pending;
        lock (_handlersLock)
        {
            _shutdownHandler = handler;
            pending = _shutdownPending;
            _shutdownPending = false;
        }
        if (pending) handler();
    }

    private void OnActivationRequested()
    {
        Action? handler;
        lock (_handlersLock)
        {
            handler = _activationHandler;
            if (handler == null) _activationPending = true;
        }

        try { handler?.Invoke(); }
        catch (Exception exception) { AppLogger.LogWarning("Window activation request failed: " + exception.GetType().Name); }
    }

    private void OnShutdownRequested()
    {
        Action? handler;
        lock (_handlersLock)
        {
            handler = _shutdownHandler;
            if (handler == null) _shutdownPending = true;
        }

        try { handler?.Invoke(); }
        catch (Exception exception) { AppLogger.LogWarning("Application shutdown request failed: " + exception.GetType().Name); }
    }

    private static SingleInstanceCoordinator? CreatePrimary(Mutex mutex, string applicationName)
    {
        EventWaitHandle? activationEvent = null;
        EventWaitHandle? shutdownEvent = null;
        try
        {
            activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                GetObjectName(applicationName, "Activate"));
            shutdownEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                GetObjectName(applicationName, "Shutdown"));
            return new SingleInstanceCoordinator(mutex, activationEvent, shutdownEvent);
        }
        catch
        {
            activationEvent?.Dispose();
            shutdownEvent?.Dispose();
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
            mutex.Dispose();
            return null;
        }
    }

    private static bool TrySignalExisting(string applicationName)
    {
        long deadline = Environment.TickCount64 + 1000;
        do
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(GetObjectName(applicationName, "Activate"));
                AllowExistingInstanceToForeground(applicationName);
                activationEvent.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        } while (Environment.TickCount64 < deadline);

        return false;
    }

    public static void ShutdownForUpdateOrUninstall(string applicationName = "SQLitePass")
    {
        try
        {
            using var shutdownEvent = EventWaitHandle.OpenExisting(GetObjectName(applicationName, "Shutdown"));
            shutdownEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        using var mutex = new Mutex(initiallyOwned: false, GetObjectName(applicationName, "Mutex"));
        try
        {
            if (mutex.WaitOne(TimeSpan.FromSeconds(10)))
                mutex.ReleaseMutex();
        }
        catch (AbandonedMutexException)
        {
            mutex.ReleaseMutex();
        }
    }

    private static void AllowExistingInstanceToForeground(string applicationName)
    {
        Process? latest = null;
        foreach (var process in Process.GetProcessesByName(applicationName))
        {
            try
            {
                if (process.Id == Environment.ProcessId)
                {
                    process.Dispose();
                    continue;
                }

                if (latest == null || process.StartTime > latest.StartTime)
                {
                    latest?.Dispose();
                    latest = process;
                }
                else
                {
                    process.Dispose();
                }
            }
            catch
            {
                process.Dispose();
            }
        }

        if (latest == null) return;
        try { NativeMethods.AllowSetForegroundWindow((uint)latest.Id); }
        finally { latest.Dispose(); }
    }

    private static string CreateInstanceSuffix()
    {
        using var currentUser = WindowsIdentity.GetCurrent();
        string identity = currentUser.User?.Value
            ?? $"{Environment.UserDomainName}\\{Environment.UserName}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash.AsSpan(0, 12));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _waitRegistration.Unregister(null);
        _shutdownRegistration.Unregister(null);
        _activationEvent.Dispose();
        _shutdownEvent.Dispose();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AllowSetForegroundWindow(uint processId);
    }
}
