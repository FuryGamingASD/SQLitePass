using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using SQLitePass.Core.Services;
using SQLitePass.Desktop.Services;

namespace SQLitePass.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (AppContext.GetData("SQLitePass.AutoTypeProbePeer") is string[] { Length: 2 } peer)
            {
                desktop.MainWindow = NativeAutoTypeVerification.CreatePeerWindow(peer[0], peer[1]);
                base.OnFrameworkInitializationCompleted();
                return;
            }
            var settings = AppSettingsManager.Load();
            if (AppContext.GetData("SQLitePass.TrayProbe") is true && NativeTrayVerification.UsesXWaylandCloseProbe)
                settings.CloseToTray = true;
            bool sessionLockProbe = AppContext.GetData("SQLitePass.InteractiveSessionLockProbe") is true;
            if (sessionLockProbe)
            {
                settings.AutoLockMinutes = 0;
                settings.EnableDualFileBackup = false;
            }
            AppLogger.Configure(settings.EnableDiagnosticLogging);
            RequestedThemeVariant = settings.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
            var window = new VaultWindow(settings, useNativeSession: true);
            desktop.MainWindow = window;
            if (OperatingSystem.IsWindows() && Program.WindowsInstance is { } instance)
            {
                instance.RegisterActivationHandler(() => Dispatcher.UIThread.Post(window.ShowFromTray));
                instance.RegisterShutdownHandler(() => Dispatcher.UIThread.Post(window.RequestExit));
            }
            if (desktop.Args?.Contains("--minimized", StringComparer.OrdinalIgnoreCase) == true)
                window.StartInTray();
            var updateProbe = AppContext.GetData("SQLitePass.AppImageUpdateProbe") as string[];
            if (AppContext.GetData("SQLitePass.StartupProbe") is true || updateProbe != null)
            {
                window.Opened += StartProbe;
                void StartProbe(object? sender, EventArgs args)
                {
                    window.Opened -= StartProbe;
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try
                        {
                            if (!window.IsVisible || window.TryGetPlatformHandle() is not { Handle: var handle } || handle == IntPtr.Zero)
                                throw new InvalidOperationException("The native window did not open.");
                            if (updateProbe != null)
                            {
                                await NativeAppImageUpdateVerification.VerifyAsync(window, updateProbe);
                                return;
                            }
                            if (sessionLockProbe)
                            {
                                bool passed = await NativeDesktopSessionVerification.VerifySessionLockInteractivelyAsync(window);
                                if (OperatingSystem.IsLinux()) desktop.Shutdown(passed ? 0 : 1);
                                return;
                            }
                            if (AppContext.GetData("SQLitePass.ClipboardProbe") is true) await NativeClipboardVerification.VerifyAsync(window);
                            if (AppContext.GetData("SQLitePass.TrayProbe") is true) await NativeTrayVerification.VerifyAsync(window);
                            if (AppContext.GetData("SQLitePass.AutoTypeProbe") is true) await NativeAutoTypeVerification.VerifyAsync();
                            if (AppContext.GetData("SQLitePass.TrayProbe") is true) await NativeDesktopSessionVerification.VerifyAsync(window);
                            window.Close();
                        }
                        catch (Exception error)
                        {
                            Console.Error.WriteLine(error);
                            await window.LockVaultAsync();
                            desktop.Shutdown(1);
                        }
                    }, DispatcherPriority.Background);
                }
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
