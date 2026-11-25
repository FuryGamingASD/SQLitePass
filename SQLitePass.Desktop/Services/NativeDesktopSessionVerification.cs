using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

    internal static async Task VerifyUnexpectedDispatcherErrorAsync(VaultWindow window)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        string profile = (string)AppContext.GetData("SQLitePass.SettingsDirectory")!;
        string path = Path.Combine(profile, "dispatcher-error-fixture.kpd");
        const string credential = "Synthetic-dispatcher-error-probe!";
        using var master = new MasterKey(credential);
        await using (var seed = await VaultSession.CreateAsync(path, master, new AppSettings { EnableDualFileBackup = false },
            "General", "Recycle bin", token)) { }

        var vault = window.FindControl<Grid>("VaultPanel")!;
        var login = window.FindControl<ScrollViewer>("UnlockPanel")!;
        var password = window.FindControl<TextBox>("MasterPassword")!;
        window.FindControl<TextBox>("VaultPath")!.Text = path;
        password.Text = credential;
        await window.OpenVaultAsync().WaitAsync(token);
        if (!vault.IsVisible) throw new InvalidOperationException("The synthetic dispatcher error vault did not open.");

        var recovery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        App.UnexpectedErrorRecoveryProbe = recovery;
        try
        {
            Dispatcher.UIThread.Post(static () => throw new InvalidOperationException("Synthetic dispatcher error."));
            if (!await recovery.Task.WaitAsync(token))
                throw new InvalidOperationException("The synthetic dispatcher error did not complete vault recovery.");
            await WaitForAsync(() => !vault.IsVisible && login.IsVisible && login.IsEnabled && string.IsNullOrEmpty(password.Text), token);
            Console.WriteLine("Verified an unhandled dispatcher error closes the synthetic vault.");
        }
        finally
        {
            if (ReferenceEquals(App.UnexpectedErrorRecoveryProbe, recovery)) App.UnexpectedErrorRecoveryProbe = null;
        }
    }

    internal static async Task<bool> VerifySessionLockInteractivelyAsync(VaultWindow window)
    {
        bool windows = OperatingSystem.IsWindows();
        string platform = windows ? "Windows" : "Linux";
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = deadline.Token;
        window.Title = windows ? "SQLitePass Win+L verification" : "SQLitePass logind session verification";
        var status = window.FindControl<TextBlock>("StatusText")!;
        var vault = window.FindControl<Grid>("VaultPanel")!;
        var login = window.FindControl<ScrollViewer>("UnlockPanel")!;
        string profile = (string)AppContext.GetData("SQLitePass.SettingsDirectory")!;
        string vaultPath = Path.Combine(profile, "desktop-lock-fixture.kpd");
        const string credential = "Synthetic-desktop-session-lock!";
        bool dialogLockObserved = false;
        bool dialogClosedAfterLock = false;
        bool dialogUnlockObserved = false;
        bool operationLockObserved = false;
        bool operationCancelled = false;
        bool operationUnlockObserved = false;
        bool passed = false;
        string stage = "opening synthetic vault";
        try
        {
            using var master = new MasterKey(credential);
            await using (var seed = await VaultSession.CreateAsync(vaultPath, master, new AppSettings { EnableDualFileBackup = false },
                "General", "Recycle bin", token)) { }
            await OpenSyntheticVaultAsync();

            stage = $"waiting for {platform} lock with a dialog open";
            status.Text = windows ? "The Settings dialog is open. Press Win+L now, then unlock Windows."
                : "The Settings dialog is open. Lock the test session now, then unlock it.";
            var dialogOperation = window.OpenSettingsAsync(() => new AppSettings(), _ => null);
            await WaitForAsync(() => dialogOperation.IsCompleted || window.OwnedWindows.OfType<SettingsWindow>().Any(), token);
            var settings = window.OwnedWindows.OfType<SettingsWindow>().SingleOrDefault()
                ?? throw new InvalidOperationException("The synthetic Settings dialog did not open.");
            settings.Title = windows ? "SQLitePass verification: press Win+L to test the open dialog"
                : "SQLitePass verification: lock the session to test the open dialog";
            if (!windows) Console.WriteLine("SQLitePass dialog probe ready.");
            await WaitForAsync(() => !vault.IsVisible && login.IsVisible && !login.IsEnabled, token);
            dialogLockObserved = true;
            stage = $"checking the dialog closed after {platform} lock";
            await WaitForAsync(() => dialogOperation.IsCompleted && !window.OwnedWindows.OfType<SettingsWindow>().Any(), token);
            dialogClosedAfterLock = true;
            stage = $"waiting for {platform} unlock after dialog lock";
            if (!windows) Console.WriteLine("SQLitePass observed the dialog lock.");
            await WaitForAsync(() => login.IsEnabled, token);
            dialogUnlockObserved = true;
            if (!windows) Console.WriteLine("SQLitePass observed the dialog unlock.");
            EnsureSyntheticVaultRemainsLocked();

            await OpenSyntheticVaultAsync();
            stage = $"waiting for {platform} lock during an active operation";
            var operation = window.RunOperationAsync(operationToken => Task.Delay(Timeout.InfiniteTimeSpan, operationToken), canCancel: true);
            await WaitForAsync(() => window.FindControl<Button>("CancelOperation")!.IsVisible, token);
            status.Text = windows ? "A cancellable operation is active. Press Win+L now, then unlock Windows."
                : "A cancellable operation is active. Lock the test session now, then unlock it.";
            window.Title = windows ? "SQLitePass verification: press Win+L to test an active operation"
                : "SQLitePass verification: lock the session to test an active operation";
            if (!windows) Console.WriteLine("SQLitePass operation probe ready.");
            await WaitForAsync(() => !vault.IsVisible && login.IsVisible && !login.IsEnabled, token);
            operationLockObserved = true;
            stage = $"checking operation cancellation after {platform} lock";
            await WaitForAsync(() => operation.IsCompleted, token);
            operationCancelled = true;
            stage = $"waiting for {platform} unlock after operation lock";
            if (!windows) Console.WriteLine("SQLitePass observed the operation lock.");
            await WaitForAsync(() => login.IsEnabled, token);
            operationUnlockObserved = true;
            if (!windows) Console.WriteLine("SQLitePass observed the operation unlock.");
            EnsureSyntheticVaultRemainsLocked();

            passed = true;
            stage = "completed";
            status.Text = "Passed. Desktop lock closed the dialog, cancelled the active operation and left the synthetic vault locked after each unlock.";
        }
        catch (Exception error)
        {
            await window.LockVaultAsync();
            status.Text = error is OperationCanceledException
                ? $"Verification timed out while waiting for {platform} lock and unlock."
                : $"Verification failed. {error.Message}";
        }
        finally
        {
            if (AppContext.GetData("SQLitePass.InteractiveSessionLockReport") is string reportPath)
            {
                await using var report = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await JsonSerializer.SerializeAsync(report, new
                {
                    Passed = passed,
                    SessionLockObserved = dialogLockObserved && operationLockObserved,
                    SessionUnlockObserved = dialogUnlockObserved && operationUnlockObserved,
                    DialogLockObserved = dialogLockObserved,
                    DialogClosedAfterLock = dialogClosedAfterLock,
                    DialogUnlockObserved = dialogUnlockObserved,
                    OperationLockObserved = operationLockObserved,
                    OperationCancelled = operationCancelled,
                    OperationUnlockObserved = operationUnlockObserved,
                    Stage = stage, CompletedAtUtc = DateTimeOffset.UtcNow, Platform = platform,
                    SessionId = windows ? null : Environment.GetEnvironmentVariable("XDG_SESSION_ID"),
                    Version = typeof(App).Assembly.GetName().Version?.ToString()
                });
            }
        }
        return passed;

        async Task OpenSyntheticVaultAsync()
        {
            window.FindControl<TextBox>("VaultPath")!.Text = vaultPath;
            window.FindControl<TextBox>("MasterPassword")!.Text = credential;
            await window.OpenVaultAsync().WaitAsync(token);
            if (!vault.IsVisible) throw new InvalidOperationException("The synthetic verification vault did not open.");
        }

        void EnsureSyntheticVaultRemainsLocked()
        {
            if (vault.IsVisible || !string.IsNullOrEmpty(window.FindControl<TextBox>("MasterPassword")!.Text))
                throw new InvalidOperationException("The synthetic vault was available after the desktop session unlocked.");
        }
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

        if (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") == "XFCE")
        {
            await OpenAsync();
            await CallAsync("SetScreenSaverActive", true);
            await WaitForAsync(() => !vault.IsVisible && login.IsVisible && !login.IsEnabled, token);
            await CallAsync("SetScreenSaverActive", false);
            await WaitForAsync(() => login.IsEnabled, token);
            if (vault.IsVisible) throw new InvalidOperationException("Xfce screen saver deactivation reopened the vault.");
            Console.WriteLine("Verified Xfce screen saver activation locks the vault independently of logind.");
        }

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
