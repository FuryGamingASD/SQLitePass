using Avalonia.Controls;
using SQLitePass.Core.Services;
using Tmds.DBus.Protocol;

namespace SQLitePass.Desktop.Services;

internal sealed class LinuxDesktopSession : IDesktopSession
{
    private const string LogindService = "org.freedesktop.login1";
    private const string XfceScreenSaver = "org.xfce.ScreenSaver";
    private const string XfceScreenSaverPath = "/org/xfce/ScreenSaver";
    private const string ManagerPath = "/org/freedesktop/login1";
    private const string SessionInterface = LogindService + ".Session";
    private const string ManagerInterface = LogindService + ".Manager";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private readonly Action<bool> _lockStateChanged;
    private readonly DBusConnection? _systemBus;
    private readonly DBusConnection? _sessionBus;
    private readonly object _state = new();
    private volatile bool _disposed;
    private bool _sessionLocked;
    private bool _suspended;
    private bool _screenSaverActive;
    private int _lockHintChanges;
    private int _screenSaverChanges;

    private bool IsLocked => _sessionLocked || _suspended || _screenSaverActive;

    public LinuxDesktopSession(Action<bool> lockStateChanged)
    {
        _lockStateChanged = lockStateChanged;
        if (DBusAddress.System is { Length: > 0 } systemAddress)
        {
            _systemBus = new DBusConnection(systemAddress);
            _ = ObserveAsync(_systemBus, LogindService, ObserveLogindAsync,
                () => UpdateLockState(sessionLocked: false, suspended: false));
        }

        string? desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        if (desktop?.Split(':').Contains("XFCE", StringComparer.OrdinalIgnoreCase) == true &&
            DBusAddress.Session is { Length: > 0 } sessionAddress)
        {
            // Xfce Screensaver publishes activation on the session bus and may leave logind's LockedHint unchanged.
            _sessionBus = new DBusConnection(sessionAddress);
            _ = ObserveAsync(_sessionBus, XfceScreenSaver, ObserveXfceScreenSaverAsync,
                () => UpdateLockState(screenSaverActive: false));
        }
    }

    private async Task ObserveAsync(DBusConnection bus, string service,
        Func<DBusConnection, string, CancellationToken, Task> observeOwner, Action reset)
    {
        try
        {
            await bus.ConnectAsync().AsTask().WaitAsync(RequestTimeout).ConfigureAwait(false);
            using var owner = await bus.WatchNameOwnerAsync(service).WaitAsync(RequestTimeout).ConfigureAwait(false);
            while (!_disposed)
            {
                string current = await owner.WaitForOwnerAsync().ConfigureAwait(false);
                var replaced = owner.GetOwnerChangedCancellationToken(current);
                try { await observeOwner(bus, current, replaced).ConfigureAwait(false); }
                catch (OperationCanceledException) when (replaced.IsCancellationRequested) { }
                catch (Exception error) when (error is DBusExceptionBase or TimeoutException or IOException)
                {
                    if (!_disposed && !replaced.IsCancellationRequested)
                        AppLogger.LogError($"Could not monitor the desktop session through {service}", error);
                }
                finally { reset(); }

                // A replacement service can have a new owner and session object on the same bus.
                try { await Task.Delay(Timeout.InfiniteTimeSpan, replaced).ConfigureAwait(false); }
                catch (OperationCanceledException) when (replaced.IsCancellationRequested) { }
            }
        }
        catch (Exception exception)
        {
            if (!_disposed) AppLogger.LogError($"Desktop session monitoring through {service} is unavailable", exception);
        }
        finally
        {
            reset();
            bus.Dispose();
        }
    }

    private async Task ObserveLogindAsync(DBusConnection bus, string owner, CancellationToken replaced)
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

    private async Task ObserveXfceScreenSaverAsync(DBusConnection bus, string owner, CancellationToken replaced)
    {
        using var active = await bus.WatchSignalAsync(owner, XfceScreenSaverPath, XfceScreenSaver, "ActiveChanged",
            static (message, _) => message.GetBodyReader().ReadBool(), (Notification<bool> change) =>
            {
                if (change.HasValue) UpdateLockState(screenSaverActive: change.Value, replaced: replaced);
            }, ObserverFlags.None, emitOnCapturedContext: false).AsTask().WaitAsync(RequestTimeout, replaced).ConfigureAwait(false);
        int changes;
        lock (_state) changes = _screenSaverChanges;
        bool initial = await bus.CallMethodAsync(GetScreenSaverActiveRequest(bus, owner),
            static (message, _) => message.GetBodyReader().ReadBool()).WaitAsync(RequestTimeout, replaced).ConfigureAwait(false);
        UpdateLockState(screenSaverActive: initial, ifScreenSaverChanges: changes, replaced: replaced);
        await Task.Delay(Timeout.InfiniteTimeSpan, replaced).ConfigureAwait(false);
    }

    private static MessageBuffer GetScreenSaverActiveRequest(DBusConnection bus, string owner)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader(owner, XfceScreenSaverPath, XfceScreenSaver, "GetActive");
        return writer.CreateMessage();
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

    private void UpdateLockState(bool? sessionLocked = null, bool? suspended = null, bool? screenSaverActive = null,
        int? ifHintChanges = null, int? ifScreenSaverChanges = null, CancellationToken replaced = default)
    {
        lock (_state)
        {
            if (_disposed || replaced.IsCancellationRequested ||
                (ifHintChanges is { } hints && hints != _lockHintChanges) ||
                (ifScreenSaverChanges is { } activations && activations != _screenSaverChanges)) return;

            bool wasLocked = IsLocked;
            if (sessionLocked is { } locked)
            {
                _sessionLocked = locked;
                _lockHintChanges++;
            }
            if (suspended is { } asleep) _suspended = asleep;
            if (screenSaverActive is { } active)
            {
                _screenSaverActive = active;
                _screenSaverChanges++;
            }

            bool isLocked = IsLocked;
            if (wasLocked != isLocked) _lockStateChanged(isLocked);
        }
    }

    public void Protect(Window window, bool protectCapture) { }

    public void Dispose()
    {
        lock (_state) _disposed = true;
        _systemBus?.Dispose();
        _sessionBus?.Dispose();
    }
}
