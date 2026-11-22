using Avalonia;
using Velopack;

namespace SQLitePass.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build().Run();
        if (args is not ["--verify-startup"]) return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // The packaged executable can prove startup without opening a vault or loading a user's profile.
        var directory = Directory.CreateTempSubdirectory("SQLitePass-startup-");
        try
        {
            AppContext.SetData("SQLitePass.SettingsDirectory", directory.FullName);
            AppContext.SetData("SQLitePass.StartupProbe", true);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        finally { directory.Delete(recursive: true); }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
