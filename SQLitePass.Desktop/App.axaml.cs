using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = AppSettingsManager.Load();
            AppLogger.Configure(settings.EnableDiagnosticLogging);
            RequestedThemeVariant = settings.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
            var window = new VaultWindow(settings, useNativeSession: true);
            desktop.MainWindow = window;
            if (AppContext.GetData("SQLitePass.StartupProbe") is true)
            {
                window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    bool opened = window.IsVisible && window.TryGetPlatformHandle() is { Handle: var handle } && handle != IntPtr.Zero;
                    if (opened) window.Close();
                    else desktop.Shutdown(1);
                }, DispatcherPriority.Background);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
