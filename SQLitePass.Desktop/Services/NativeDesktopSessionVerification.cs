using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Services;
using Tmds.DBus.Protocol;

namespace SQLitePass.Desktop.Services;

internal static class NativeDesktopSessionVerification
{
    private const string LoginService = "org.freedesktop.login1";
    private const string ProbeInterface = "org.sqlitepass.Verification.Session";

    internal static async Task VerifyAsync(VaultWindow window)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        if (OperatingSystem.IsWindows()) await VerifyCaptureAsync(window, timeout.Token);
        else await VerifyLogindAsync(window, timeout.Token);
    }

    [SupportedOSPlatform("windows")]
    private static async Task VerifyCaptureAsync(VaultWindow window, CancellationToken token)
    {
        IntPtr handle = window.TryGetPlatformHandle()!.Handle;
        AssertCaptureAffinity(handle, enabled: true);
        foreach (bool enabled in new[] { false, true })
        {
            var operation = window.OpenSettingsAsync(AppSettingsManager.Load,
                settings => AppSettingsManager.Save(settings, applyWindowsStartup: false));
            await WaitForAsync(() => operation.IsCompleted || window.OwnedWindows.OfType<SettingsWindow>().Any(), token);
            var settings = window.OwnedWindows.OfType<SettingsWindow>().Single();
            settings.FindControl<CheckBox>("ProtectCapture")!.IsChecked = enabled;
            settings.FindControl<Button>("Save")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await operation.WaitAsync(token);
            AssertCaptureAffinity(handle, enabled);
        }
        Console.WriteLine("Verified the native Windows capture-affinity flag changes when the preference is saved.");
    }

    private static void AssertCaptureAffinity(IntPtr handle, bool enabled)
    {
        if (!GetWindowDisplayAffinity(handle, out uint value) || (enabled ? value is not (1 or 0x11) : value != 0))
            throw new InvalidOperationException($"The window capture affinity did not match the saved setting. Flag: {value}.");
    }

    private static async Task VerifyLogindAsync(VaultWindow window, CancellationToken token)
    {
        string? address = DBusAddress.Session;
        if (string.IsNullOrEmpty(address) || DBusAddress.System != address)
            throw new InvalidOperationException("The session fixture must run on the isolated desktop bus.");
        using var bus = new DBusConnection(address);
        await bus.ConnectAsync().AsTask().WaitAsync(token);
        while (!await bus.CallMethodAsync(ControlRequest(bus, "WasObserved"),
            static (message, _) => message.GetBodyReader().ReadBool()).WaitAsync(token))
            await Task.Delay(40, token);

        var profile = (string)AppContext.GetData("SQLitePass.SettingsDirectory")!;
        string vaultPath = Path.Combine(profile, "session-fixture.kpd");
        const string credential = "Synthetic-session-probe!";
        using var master = new MasterKey(credential);
        await using (var seed = await VaultSession.CreateAsync(vaultPath, master, new AppSettings { EnableDualFileBackup = false },
            "General", "Recycle bin", token)) { }

        var vault = window.FindControl<Grid>("VaultPanel")!;
        var login = window.FindControl<ScrollViewer>("UnlockPanel")!;
        window.FindControl<TextBox>("VaultPath")!.Text = vaultPath;

        await OpenAsync();
        var clipboard = window.NativeClipboard ?? throw new InvalidOperationException("The verification clipboard is unavailable.");
        if (!await clipboard.CopyAsync("Synthetic text from an open vault session", TimeSpan.FromSeconds(30), token))
            throw new InvalidOperationException("The test could not seed the session clipboard.");
        await CallAsync("SetLocked", true);
        await WaitForAsync(() => !vault.IsVisible && login.IsVisible && !login.IsEnabled, token);
        if (!string.IsNullOrEmpty(await window.Clipboard!.TryGetTextAsync()))
            throw new InvalidOperationException("The desktop session lock left the app-owned clipboard text available.");
        await CallAsync("SetLocked", false);
        await WaitForAsync(() => login.IsEnabled, token);

        await OpenAsync();
        await CallAsync("SetSleeping", true);
        await WaitForAsync(() => !vault.IsVisible && login.IsVisible && !login.IsEnabled, token);
        await CallAsync("SetSleeping", false);
        await WaitForAsync(() => login.IsEnabled, token);

        await CallAsync("SetLocked", true);
        await WaitForAsync(() => !login.IsEnabled, token);
        await CallAsync("Stop");
        await WaitForAsync(() => login.IsEnabled, token);
        await OpenAsync();
        await window.CheckDesktopIdleAsync(TimeSpan.FromMinutes(61)).WaitAsync(token);
        if (vault.IsVisible || !login.IsVisible)
            throw new InvalidOperationException("Inactivity did not lock the vault after logind stopped.");
        Console.WriteLine("Verified D-Bus logind lock and suspend delivery, and inactivity fallback after logind disappears.");

        async Task OpenAsync()
        {
            window.FindControl<TextBox>("MasterPassword")!.Text = credential;
            await window.OpenVaultAsync().WaitAsync(token);
            if (!vault.IsVisible) throw new InvalidOperationException("The synthetic session test vault did not open.");
        }

        Task CallAsync(string method, bool? value = null) => bus.CallMethodAsync(ControlRequest(bus, method, value)).WaitAsync(token);
    }

    private static MessageBuffer ControlRequest(DBusConnection bus, string method, bool? value = null)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader(LoginService, "/org/freedesktop/login1", ProbeInterface, method, value.HasValue ? "b" : null);
        if (value is { } flag) writer.WriteBool(flag);
        return writer.CreateMessage();
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(40, token);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
}
