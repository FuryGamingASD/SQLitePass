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
            RegisterUnexpectedErrorHandlers(desktop, window);
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
                            if (AppContext.GetData("SQLitePass.WindowsClipboardSyncGuardProbe") is true)
                            {
                                bool passed = await NativeClipboardVerification.VerifyWindowsClipboardSyncGuardAsync(window);
                                desktop.Shutdown(passed ? 0 : 1);
                                return;
                            }
                            if (AppContext.GetData("SQLitePass.LinuxClipboardManagerGuardProbe") is true)
                            {
                                bool passed = await NativeClipboardVerification.VerifyLinuxClipboardManagerGuardAsync(window);
                                desktop.Shutdown(passed ? 0 : 1);
                                return;
                            }
                            if (AppContext.GetData("SQLitePass.ClipboardProbe") is true) await NativeClipboardVerification.VerifyAsync(window);
                            if (AppContext.GetData("SQLitePass.TrayProbe") is true) await NativeTrayVerification.VerifyAsync(window);
                            if (AppContext.GetData("SQLitePass.AutoTypeProbe") is true) await NativeAutoTypeVerification.VerifyAsync();
                            if (OperatingSystem.IsWindows() && AppContext.GetData("SQLitePass.AutoTypeProbe") is true)
                                await NativeWorkspaceVerification.VerifyAsync(window);
                            if (AppContext.GetData("SQLitePass.TrayProbe") is true)
                            {
                                await NativeDesktopSessionVerification.VerifyAsync(window);
                                await NativeDesktopSessionVerification.VerifyUnexpectedDispatcherErrorAsync(window);
                            }
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

    private static void RegisterUnexpectedErrorHandlers(IClassicDesktopStyleApplicationLifetime desktop, VaultWindow window)
    {
        bool recovering = false;
        bool exiting = false;
        Dispatcher.UIThread.UnhandledException += OnDispatcherError;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskError;
        desktop.Exit += (_, _) =>
        {
            Volatile.Write(ref exiting, true);
            Dispatcher.UIThread.UnhandledException -= OnDispatcherError;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskError;
        };

        void OnDispatcherError(object? sender, DispatcherUnhandledExceptionEventArgs args)
        {
            if (recovering) return;
            args.Handled = true;
            _ = RecoverAsync(args.Exception);
        }

        void OnUnobservedTaskError(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            if (Volatile.Read(ref exiting)) return;
            var error = args.Exception;
            Dispatcher.UIThread.Post(() => _ = RecoverAsync(error), DispatcherPriority.Send);
            args.SetObserved();
        }

        async Task RecoverAsync(Exception error)
        {
            if (recovering) return;
            recovering = true;
            try
            {
                AppLogger.LogError("Unexpected desktop error", error);
                if (await window.LockAfterUnexpectedErrorAsync()) return;
            }
            catch (Exception lockError)
            {
                AppLogger.LogError("Could not lock the vault after an unexpected desktop error", lockError);
            }
            finally { recovering = false; }

            desktop.Shutdown(1);
        }
    }
}
