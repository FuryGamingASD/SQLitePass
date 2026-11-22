using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using SQLitePass.Core.Services;
using Tmds.DBus.Protocol;

namespace SQLitePass.Desktop.Services;

internal sealed class DesktopTray : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly DispatcherTimer _monitor = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Action _lostHost;
    private readonly string? _sessionAddress;
    private DBusConnection? _connection;
    private bool _polling;
    private bool _disposed;

    internal DesktopTray(Func<string, object[], string> text, Action show, Action lockVault, Action exit, Action lostHost)
    {
        _lostHost = lostHost;
        _sessionAddress = OperatingSystem.IsLinux() ? DBusAddress.Session : null;
        using var icon = AssetLoader.Open(new Uri("avares://SQLitePass.Desktop/Resources/app.ico"));
        var menu = new NativeMenu();
        AddItem(menu, text("TrayShow", []), show);
        AddItem(menu, text("TbLock", []), lockVault);
        menu.Items.Add(new NativeMenuItemSeparator());
        AddItem(menu, text("Exit", []), exit);
        _icon = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = text("TrayTooltip", []), Menu = menu, IsVisible = true };
        _icon.Clicked += (_, _) => Dispatcher.UIThread.Post(show);
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });
        if (OperatingSystem.IsWindows()) CanHide = HasWindowsTaskbar();
        _monitor.Tick += RefreshAvailability;
        _monitor.Start();
    }

    internal bool CanHide { get; private set; }

    private static void AddItem(NativeMenu menu, string label, Action action)
    {
        var item = new NativeMenuItem { Header = label };
        item.Click += (_, _) => Dispatcher.UIThread.Post(action);
        menu.Items.Add(item);
    }

    private async void RefreshAvailability(object? sender, EventArgs args)
    {
        if (_disposed || _polling) return;
        _polling = true;
        try
        {
            bool ready = OperatingSystem.IsWindows() ? HasWindowsTaskbar() : await HasLinuxTrayHostAsync();
            if (_disposed) return;
            bool lost = CanHide && !ready;
            CanHide = ready;
            if (lost) _lostHost();
        }
        catch (Exception error)
        {
            AppLogger.LogError("Unable to check system tray availability", error);
            if (!_disposed && CanHide)
            {
                CanHide = false;
                _lostHost();
            }
        }
        finally { _polling = false; }
    }

    private async Task<bool> HasLinuxTrayHostAsync()
    {
        if (string.IsNullOrEmpty(_sessionAddress)) return false;
        const string watcher = "org.kde.StatusNotifierWatcher";
        var deadline = TimeSpan.FromSeconds(1);
        try
        {
            if (_connection == null)
            {
                _connection = new DBusConnection(_sessionAddress);
                await _connection.ConnectAsync().AsTask().WaitAsync(deadline);
            }
            return await _connection.CallMethodAsync(GetWatcherProperties(_connection, watcher), static (message, _) =>
            {
                var data = message.GetBodyReader().ReadDictionaryOfStringToVariantValue();
                string prefix = FormattableString.Invariant($"org.kde.StatusNotifierItem-{Environment.ProcessId}-");
                return data.TryGetValue("IsStatusNotifierHostRegistered", out var host) && host.GetBool() &&
                    data.TryGetValue("RegisteredStatusNotifierItems", out var items) &&
                    items.GetArray<string>().Any(item => item.StartsWith(prefix, StringComparison.Ordinal));
            }).WaitAsync(deadline);
        }
        catch (Exception error) when (error is DBusExceptionBase or TimeoutException or IOException or ObjectDisposedException)
        {
            _connection?.Dispose();
            _connection = null;
            return false;
        }
    }

    private static MessageBuffer GetWatcherProperties(DBusConnection connection, string watcher)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(watcher, "/StatusNotifierWatcher", "org.freedesktop.DBus.Properties", "GetAll", "s");
        writer.WriteString(watcher);
        return writer.CreateMessage();
    }

    private static bool HasWindowsTaskbar()
    {
        IntPtr handle = FindWindowW("Shell_TrayWnd", null);
        return handle != IntPtr.Zero && SendMessageTimeoutW(handle, 0, UIntPtr.Zero, IntPtr.Zero, 2, 200, out _) != IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitor.Stop();
        _connection?.Dispose();
        if (Application.Current is { } app) TrayIcon.SetIcons(app, null);
        _icon.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam,
        uint flags, uint milliseconds, out UIntPtr result);
}
