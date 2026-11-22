using Avalonia;
using Velopack;

namespace SQLitePass.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build().Run();
        if (args is ["--verify-autotype-peer", var resultPath, var scenario])
        {
            if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted") return 2;
            AppContext.SetData("SQLitePass.AutoTypeProbePeer", new[] { resultPath, scenario });
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        if (args is not (["--verify-startup"] or ["--verify-startup-and-clipboard"] or ["--verify-desktop"]))
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        bool verifyTray = args is ["--verify-desktop"];
        bool verifyClipboard = verifyTray || args is ["--verify-startup-and-clipboard"];
        if (verifyClipboard && Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted") return 2;

        // The packaged executable can prove startup without opening a vault or loading a user's profile.
        var directory = Directory.CreateTempSubdirectory("SQLitePass-startup-");
        try
        {
            AppContext.SetData("SQLitePass.SettingsDirectory", directory.FullName);
            AppContext.SetData("SQLitePass.StartupProbe", true);
            AppContext.SetData("SQLitePass.ClipboardProbe", verifyClipboard);
            AppContext.SetData("SQLitePass.TrayProbe", verifyTray);
            AppContext.SetData("SQLitePass.AutoTypeProbe", verifyTray);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        finally { directory.Delete(recursive: true); }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
