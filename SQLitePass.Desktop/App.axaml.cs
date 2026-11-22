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
            if (AppContext.GetData("SQLitePass.StartupProbe") is true)
            {
                window.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
                {
                    try
                    {
                        if (!window.IsVisible || window.TryGetPlatformHandle() is not { Handle: var handle } || handle == IntPtr.Zero)
                            throw new InvalidOperationException("The native window did not open.");
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
        base.OnFrameworkInitializationCompleted();
    }
}
