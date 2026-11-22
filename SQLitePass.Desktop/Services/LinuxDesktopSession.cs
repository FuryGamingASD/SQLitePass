using Avalonia.Controls;
using SQLitePass.Core.Services;
using Tmds.DBus.Protocol;

namespace SQLitePass.Desktop.Services;

internal sealed class LinuxDesktopSession : IDesktopSession
{
    private const string LogindService = "org.freedesktop.login1";
    private const string ManagerPath = "/org/freedesktop/login1";
    private const string SessionInterface = LogindService + ".Session";
    private const string ManagerInterface = LogindService + ".Manager";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private readonly Action<bool> _lockStateChanged;
    private readonly DBusConnection? _bus;
    private readonly object _state = new();
    private volatile bool _disposed;
    private bool _sessionLocked;
    private bool _suspended;
    private int _lockHintChanges;

    public LinuxDesktopSession(Action<bool> lockStateChanged)
    {
        _lockStateChanged = lockStateChanged;
        if (DBusAddress.System is { Length: > 0 } address)
        {
            _bus = new DBusConnection(address);
            _ = ObserveAsync(_bus);
        }
    }

    private async Task ObserveAsync(DBusConnection bus)
    {
        try
        {
            await bus.ConnectAsync().AsTask().WaitAsync(RequestTimeout).ConfigureAwait(false);
            using var owner = await bus.WatchNameOwnerAsync(LogindService).WaitAsync(RequestTimeout).ConfigureAwait(false);
            while (!_disposed)
            {
                string current = await owner.WaitForOwnerAsync().ConfigureAwait(false);
                var replaced = owner.GetOwnerChangedCancellationToken(current);
                try { await ObserveOwnerAsync(bus, current, replaced).ConfigureAwait(false); }
                catch (OperationCanceledException) when (replaced.IsCancellationRequested) { }
                catch (Exception error) when (error is DBusExceptionBase or TimeoutException or IOException)
                {
                    if (!_disposed && !replaced.IsCancellationRequested)
                        AppLogger.LogError("The current logind service cannot monitor this session", error);
                }
                finally { UpdateLockState(sessionLocked: false, suspended: false); }

                // A logind restart can change the session object path even when the system bus continues running.
                try { await Task.Delay(Timeout.InfiniteTimeSpan, replaced).ConfigureAwait(false); }
                catch (OperationCanceledException) when (replaced.IsCancellationRequested) { }
            }
        }
        catch (Exception exception)
        {
            if (!_disposed) AppLogger.LogError("Linux desktop session monitoring unavailable", exception);
        }
        finally
        {
            UpdateLockState(sessionLocked: false, suspended: false);
            bus.Dispose();
        }
    }

    private async Task ObserveOwnerAsync(DBusConnection bus, string owner, CancellationToken replaced)
    {
        string session = await bus.CallMethodAsync(GetSessionRequest(bus, owner),
            static (message, _) => message.GetBodyReader().ReadObjectPath().ToString()).WaitAsync(RequestTimeout, replaced).ConfigureAwait(false);
        using var lockHint = await bus.WatchPropertiesChangedAsync(owner, session, SessionInterface,
            static (message, _) => ReadLockHint(message), (Notification<bool?> change) =>
            {
                if (change.HasValue && change.Value is { } locked) UpdateLockState(sessionLocked: locked, replaced: replaced);
            }, ObserverFlags.None, emitOnCapturedContext: false).AsTask().WaitAsync(RequestTimeout, replaced).ConfigureAwait(false);
        using var sleep = await bus.WatchSignalAsync(owner, ManagerPath, ManagerInterface, "PrepareForSleep",
            static (message, _) => message.GetBodyReader().ReadBool(), (Notification<bool> change) =>
            {
                if (change.HasValue) UpdateLockState(suspended: change.Value, replaced: replaced);
            }, ObserverFlags.None, emitOnCapturedContext: false).AsTask().WaitAsync(RequestTimeout, replaced).ConfigureAwait(false);
        int hintChanges;
        lock (_state) hintChanges = _lockHintChanges;
        bool initial = await bus.CallMethodAsync(GetLockedHintRequest(bus, owner, session),
            static (message, _) => message.GetBodyReader().ReadVariantValue().GetBool()).WaitAsync(RequestTimeout, replaced).ConfigureAwait(false);
        UpdateLockState(sessionLocked: initial, ifHintChanges: hintChanges, replaced: replaced);
        await Task.Delay(Timeout.InfiniteTimeSpan, replaced).ConfigureAwait(false);
    }

    private static MessageBuffer GetSessionRequest(DBusConnection bus, string owner)
    {
        using var writer = bus.GetMessageWriter();
        if (Environment.GetEnvironmentVariable("XDG_SESSION_ID") is { Length: > 0 } sessionId)
        {
            writer.WriteMethodCallHeader(owner, ManagerPath, ManagerInterface, "GetSession", "s");
            writer.WriteString(sessionId);
        }
        else
        {
            writer.WriteMethodCallHeader(owner, ManagerPath, ManagerInterface, "GetSessionByPID", "u");
            writer.WriteUInt32((uint)Environment.ProcessId);
        }
        return writer.CreateMessage();
    }

    private static MessageBuffer GetLockedHintRequest(DBusConnection bus, string owner, string session)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader(owner, session, "org.freedesktop.DBus.Properties", "Get", "ss");
        writer.WriteString(SessionInterface);
        writer.WriteString("LockedHint");
        return writer.CreateMessage();
    }

    private static bool? ReadLockHint(Message message)
    {
        var reader = message.GetBodyReader();
        if (reader.ReadString() != SessionInterface) return null;
        var changes = reader.ReadDictionaryOfStringToVariantValue();
        return changes.TryGetValue("LockedHint", out var locked) ? locked.GetBool() : null;
    }

    private void UpdateLockState(bool? sessionLocked = null, bool? suspended = null, int? ifHintChanges = null,
        CancellationToken replaced = default)
    {
        lock (_state)
        {
            if (_disposed || replaced.IsCancellationRequested || ifHintChanges is { } expected && expected != _lockHintChanges) return;
            bool wasLocked = _sessionLocked || _suspended;
            if (sessionLocked is { } locked)
            {
                _sessionLocked = locked;
                _lockHintChanges++;
            }
            if (suspended is { } asleep) _suspended = asleep;
            bool isLocked = _sessionLocked || _suspended;
            if (wasLocked != isLocked) _lockStateChanged(isLocked);
        }
    }

    public void Protect(Window window, bool protectCapture) { }

    public void Dispose()
    {
        lock (_state) _disposed = true;
        _bus?.Dispose();
    }
}
