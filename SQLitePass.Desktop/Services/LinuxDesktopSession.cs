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
    private readonly Action<bool> _lockStateChanged;
    private readonly DBusConnection? _bus;
    private readonly object _state = new();
    private bool _disposed;
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
        var timeout = TimeSpan.FromSeconds(5);
        try
        {
            await bus.ConnectAsync().AsTask().WaitAsync(timeout).ConfigureAwait(false);
            string session = await bus.CallMethodAsync(GetSessionRequest(bus),
                static (message, _) => message.GetBodyReader().ReadObjectPath().ToString()).WaitAsync(timeout).ConfigureAwait(false);
            await bus.WatchPropertiesChangedAsync(LogindService, session, SessionInterface,
                static (message, _) => ReadLockHint(message), (Notification<bool?> change) =>
                {
                    if (change.HasValue && change.Value is { } locked) UpdateLockState(sessionLocked: locked);
                }, ObserverFlags.None, emitOnCapturedContext: false).AsTask().WaitAsync(timeout).ConfigureAwait(false);
            await bus.WatchSignalAsync(LogindService, ManagerPath, ManagerInterface, "PrepareForSleep",
                static (message, _) => message.GetBodyReader().ReadBool(), (Notification<bool> change) =>
                {
                    if (change.HasValue) UpdateLockState(suspended: change.Value);
                }, ObserverFlags.None, emitOnCapturedContext: false).AsTask().WaitAsync(timeout).ConfigureAwait(false);
            int hintChanges;
            lock (_state) hintChanges = _lockHintChanges;
            bool initial = await bus.CallMethodAsync(GetLockedHintRequest(bus, session),
                static (message, _) => message.GetBodyReader().ReadVariantValue().GetBool()).WaitAsync(timeout).ConfigureAwait(false);
            UpdateLockState(sessionLocked: initial, ifHintChanges: hintChanges);
            await bus.DisconnectedAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            AppLogger.LogError("Linux desktop session monitoring unavailable", exception);
        }
        finally
        {
            lock (_state)
            {
                if (!_disposed && (_sessionLocked || _suspended)) _lockStateChanged(false);
            }
            bus.Dispose();
        }
    }

    private static MessageBuffer GetSessionRequest(DBusConnection bus)
    {
        using var writer = bus.GetMessageWriter();
        if (Environment.GetEnvironmentVariable("XDG_SESSION_ID") is { Length: > 0 } sessionId)
        {
            writer.WriteMethodCallHeader(LogindService, ManagerPath, ManagerInterface, "GetSession", "s");
            writer.WriteString(sessionId);
        }
        else
        {
            writer.WriteMethodCallHeader(LogindService, ManagerPath, ManagerInterface, "GetSessionByPID", "u");
            writer.WriteUInt32((uint)Environment.ProcessId);
        }
        return writer.CreateMessage();
    }

    private static MessageBuffer GetLockedHintRequest(DBusConnection bus, string session)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader(LogindService, session, "org.freedesktop.DBus.Properties", "Get", "ss");
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

    private void UpdateLockState(bool? sessionLocked = null, bool? suspended = null, int? ifHintChanges = null)
    {
        lock (_state)
        {
            if (_disposed || ifHintChanges is { } expected && expected != _lockHintChanges) return;
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

    public void Protect(Window window) { }

    public void Dispose()
    {
        lock (_state) _disposed = true;
        _bus?.Dispose();
    }
}
