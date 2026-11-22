using System.Diagnostics;
using Avalonia.Controls;
using Tmds.DBus.Protocol;

namespace SQLitePass.Desktop.Services;

internal static class NativeTrayVerification
{
    internal static async Task VerifyAsync(VaultWindow window)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        if (OperatingSystem.IsWindows())
        {
            await WaitForAsync(() => window.TrayCanHide, token);
            window.WindowState = WindowState.Minimized;
            await WaitForAsync(() => !window.IsVisible, token);
            window.ShowFromTray();
            await WaitForAsync(() => window.IsVisible && window.WindowState != WindowState.Minimized, token);
        }
        else
        {
            if (window.TrayCanHide) throw new InvalidOperationException("The isolated session already has a tray host.");
            window.WindowState = WindowState.Minimized;
            await Task.Delay(TimeSpan.FromSeconds(1.2), token);
            if (!window.IsVisible) throw new InvalidOperationException("The window was hidden without a tray host.");
            window.ShowFromTray();

            string watcher = Environment.GetEnvironmentVariable("SQLITEPASS_TRAY_PROBE_WATCHER")
                ?? throw new InvalidOperationException("The isolated tray watcher is unavailable.");
            var start = new ProcessStartInfo("/usr/bin/python3") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(watcher);
            using var peer = Process.Start(start) ?? throw new InvalidOperationException("Could not run the tray watcher.");
            try
            {
                await WaitForAsync(() => window.TrayCanHide, token);
                window.WindowState = WindowState.Minimized;
                await WaitForAsync(() => !window.IsVisible, token);
                await ActivateStatusNotifierAsync(token);
                await WaitForAsync(() => window.IsVisible && window.WindowState != WindowState.Minimized, token);
                window.WindowState = WindowState.Minimized;
                await WaitForAsync(() => !window.IsVisible, token);
                peer.Kill(entireProcessTree: true);
                await peer.WaitForExitAsync(token);
                await WaitForAsync(() => !window.TrayCanHide && window.IsVisible, token);
            }
            finally
            {
                if (!peer.HasExited) peer.Kill(entireProcessTree: true);
            }
        }
        Console.WriteLine("Verified desktop tray minimize and restore behavior.");
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(40, token);
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
