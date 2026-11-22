using System.Diagnostics;
using Avalonia.Controls;
using Tmds.DBus.Protocol;

namespace SQLitePass.Desktop.Services;

internal static class NativeTrayVerification
{
    internal static bool UsesXWaylandCloseProbe => OperatingSystem.IsLinux() &&
        Environment.GetEnvironmentVariable("SQLITEPASS_VERIFY_XWAYLAND_DESKTOP") == "1";

    internal static async Task VerifyAsync(VaultWindow window)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        if (OperatingSystem.IsWindows())
        {
            await WaitForAsync(() => window.TrayCanHide, "responsive Windows taskbar", token);
            window.WindowState = WindowState.Minimized;
            await WaitForAsync(() => !window.IsVisible, "window hidden after minimize", token);
            window.ShowFromTray();
            await WaitForAsync(() => window.IsVisible && window.WindowState != WindowState.Minimized, "window restored from tray", token);
        }
        else
        {
            if (window.TrayCanHide) throw new InvalidOperationException("The isolated session already has a tray host.");
            if (!UsesXWaylandCloseProbe)
            {
                window.WindowState = WindowState.Minimized;
                await WaitForAsync(() => window.WindowState == WindowState.Minimized, "window manager minimized the window", token);
                await Task.Delay(TimeSpan.FromSeconds(1.2), token);
            }
            if (!window.IsVisible) throw new InvalidOperationException("The window was hidden without a tray host.");
            window.ShowFromTray();

            string watcher = Environment.GetEnvironmentVariable("SQLITEPASS_TRAY_PROBE_WATCHER")
                ?? throw new InvalidOperationException("The isolated tray watcher is unavailable.");
            var start = new ProcessStartInfo("/usr/bin/python3") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(watcher);
            using var peer = Process.Start(start) ?? throw new InvalidOperationException("Could not run the tray watcher.");
            try
            {
                await WaitForAsync(() => window.TrayCanHide, "item registered with the StatusNotifier host", token);
                RequestHide();
                await WaitForAsync(() => !window.IsVisible, "window hidden in the tray", token);
                await ActivateStatusNotifierAsync(token);
                await WaitForAsync(() => window.IsVisible && window.WindowState != WindowState.Minimized, "window restored by StatusNotifier activation", token);
                RequestHide();
                await WaitForAsync(() => !window.IsVisible, "restored window hidden a second time", token);
                peer.Kill(entireProcessTree: true);
                await peer.WaitForExitAsync(token);
                await WaitForAsync(() => !window.TrayCanHide && window.IsVisible, "window restored after losing its tray host", token);
            }
            finally
            {
                if (!peer.HasExited) peer.Kill(entireProcessTree: true);
            }
        }
        Console.WriteLine(UsesXWaylandCloseProbe
            ? "Verified XWayland close-to-tray, StatusNotifier activation and restoration after host loss."
            : "Verified desktop tray minimize and restore behavior.");

        void RequestHide()
        {
            // Exercise the close-to-tray preference on Sway; Openbox covers minimization.
            if (UsesXWaylandCloseProbe) window.Close();
            else window.WindowState = WindowState.Minimized;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, string stage, CancellationToken token)
    {
        try
        {
            while (!condition()) await Task.Delay(40, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw new TimeoutException($"The tray probe timed out waiting for {stage}.");
        }
    }

    private static async Task ActivateStatusNotifierAsync(CancellationToken token)
    {
        using var bus = new DBusConnection(DBusAddress.Session!);
        await bus.ConnectAsync().AsTask().WaitAsync(token);
        var names = await bus.ListServicesAsync().WaitAsync(token);
        string prefix = FormattableString.Invariant($"org.kde.StatusNotifierItem-{Environment.ProcessId}-");
        string service = names.Single(name => name.StartsWith(prefix, StringComparison.Ordinal));
        await bus.CallMethodAsync(CreateActivationRequest(bus, service)).WaitAsync(token);
    }

    private static MessageBuffer CreateActivationRequest(DBusConnection bus, string service)
    {
        using var message = bus.GetMessageWriter();
        message.WriteMethodCallHeader(service, "/StatusNotifierItem", "org.kde.StatusNotifierItem", "Activate", "ii");
        message.WriteInt32(0);
        message.WriteInt32(0);
        return message.CreateMessage();
    }
}
