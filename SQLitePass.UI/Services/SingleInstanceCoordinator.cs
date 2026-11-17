using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SQLitePass.UI.Services;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private static readonly string InstanceSuffix = CreateInstanceSuffix();
    private static readonly string MutexName = $@"Local\SQLitePass-{InstanceSuffix}-Mutex";
    private static readonly string ActivationName = $@"Local\SQLitePass-{InstanceSuffix}-Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly RegisteredWaitHandle _waitRegistration;
    private Action? _activationHandler;
    private int _activationPending;
    private int _disposed;

    private SingleInstanceCoordinator(Mutex mutex, EventWaitHandle activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _waitRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            static (state, timedOut) =>
            {
                if (!timedOut) ((SingleInstanceCoordinator)state!).OnActivationRequested();
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

    private void OnActivationRequested()
    {
        var handler = Volatile.Read(ref _activationHandler);
        if (handler == null)
        {
            Interlocked.Exchange(ref _activationPending, 1);
            return;
        }

        handler();
    }

    private static SingleInstanceCoordinator? CreatePrimary(Mutex mutex)
    {
        try
        {
            var activationEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ActivationName);
            return new SingleInstanceCoordinator(mutex, activationEvent);
        }
        catch
        {
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
        _activationEvent.Dispose();
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
