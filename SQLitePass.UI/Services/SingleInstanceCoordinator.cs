using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Services;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private static readonly string InstanceSuffix = CreateInstanceSuffix();
    private static readonly string MutexName = $@"Local\SQLitePass-{InstanceSuffix}-Mutex";
    private static readonly string ActivationName = $@"Local\SQLitePass-{InstanceSuffix}-Activate";
    private static readonly string ShutdownName = $@"Local\SQLitePass-{InstanceSuffix}-Shutdown";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly EventWaitHandle _shutdownEvent;
    private readonly RegisteredWaitHandle _waitRegistration;
    private readonly RegisteredWaitHandle _shutdownRegistration;
    private Action? _activationHandler;
    private Action? _shutdownHandler;
    private int _activationPending;
    private int _shutdownPending;
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

    public static SingleInstanceCoordinator? StartOrSignalExisting(out bool shouldStart)
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        bool ownsMutex;
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }

        if (ownsMutex)
        {
            shouldStart = true;
            return CreatePrimary(mutex);
        }

        mutex.Dispose();
        if (TrySignalExisting())
        {
            shouldStart = false;
            return null;
        }

        // The existing process may be exiting or still creating its activation event.
        // Retry ownership once; if coordination is unavailable, start visibly as a fallback.
        var retryMutex = new Mutex(initiallyOwned: false, MutexName);
        try
        {
            try { ownsMutex = retryMutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }
            if (ownsMutex)
            {
                shouldStart = true;
                return CreatePrimary(retryMutex);
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
        Volatile.Write(ref _activationHandler, handler);
        if (Interlocked.Exchange(ref _activationPending, 0) != 0)
            handler();
    }

    public void RegisterShutdownHandler(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Volatile.Write(ref _shutdownHandler, handler);
        if (Interlocked.Exchange(ref _shutdownPending, 0) != 0)
            handler();
    }

    private void OnActivationRequested()
    {
        var handler = Volatile.Read(ref _activationHandler);
        if (handler == null)
        {
            Interlocked.Exchange(ref _activationPending, 1);
            return;
        }

        try { handler(); }
        catch (Exception exception) { AppLogger.LogWarning("Window activation request failed: " + exception.GetType().Name); }
    }

    private void OnShutdownRequested()
    {
        var handler = Volatile.Read(ref _shutdownHandler);
        if (handler == null)
        {
            Interlocked.Exchange(ref _shutdownPending, 1);
            return;
        }

        try { handler(); }
        catch (Exception exception) { AppLogger.LogWarning("Application shutdown request failed: " + exception.GetType().Name); }
    }

    private static SingleInstanceCoordinator? CreatePrimary(Mutex mutex)
    {
        EventWaitHandle? activationEvent = null;
        EventWaitHandle? shutdownEvent = null;
        try
        {
            activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ActivationName);
            shutdownEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ShutdownName);
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

    private static bool TrySignalExisting()
    {
        long deadline = Environment.TickCount64 + 1000;
        do
        {
            try
            {
                using var activationEvent = EventWaitHandle.OpenExisting(ActivationName);
                AllowExistingInstanceToForeground();
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

    public static void ShutdownForUpdateOrUninstall()
    {
        try
        {
            using var shutdownEvent = EventWaitHandle.OpenExisting(ShutdownName);
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

        using var mutex = new Mutex(initiallyOwned: false, MutexName);
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

    private static void AllowExistingInstanceToForeground()
    {
        Process? latest = null;
        foreach (var process in Process.GetProcessesByName("SQLitePass"))
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
        string identity = WindowsIdentity.GetCurrent().User?.Value
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
