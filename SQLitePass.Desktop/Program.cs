using System.Runtime.Versioning;
using Avalonia;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;
using Velopack;

namespace SQLitePass.Desktop;

internal static class Program
{
    internal static SingleInstanceCoordinator? WindowsInstance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        var velopack = VelopackApp.Build().SetAutoApplyOnStartup(false);
        string applicationName = string.Equals(Path.GetFileName(Environment.ProcessPath), "SQLitePass.exe", StringComparison.OrdinalIgnoreCase)
            ? "SQLitePass" : "SQLitePass.Desktop";
        if (OperatingSystem.IsWindows()) ConfigureWindowsUpdateHooks(velopack, applicationName);
        velopack.Run();
        if (args is ["--verify-appimage-update", _, _] or ["--verify-appimage-restarted", _])
        {
            if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted") return 2;
            AppContext.SetData("SQLitePass.SettingsDirectory", Path.Combine(args[^1], "profile"));
            AppContext.SetData("SQLitePass.AppImageUpdateProbe", args);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        if (args is ["--verify-autotype-peer", var resultPath, var scenario])
        {
            if (Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted") return 2;
            AppContext.SetData("SQLitePass.AutoTypeProbePeer", new[] { resultPath, scenario });
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        if (args is not (["--verify-startup"] or ["--verify-startup-and-clipboard"] or ["--verify-desktop"] or
            ["--verify-win-session-lock"] or ["--verify-win-session-lock", _] or ["--verify-linux-session-lock", _]))
        {
            if (!OperatingSystem.IsWindows()) return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            using var instance = SingleInstanceCoordinator.StartOrSignalExisting(out bool shouldStart, applicationName);
            if (!shouldStart) return 0;
            WindowsInstance = instance;
            try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
            finally { WindowsInstance = null; }
        }
        bool verifyTray = args is ["--verify-desktop"];
        bool verifyClipboard = verifyTray || args is ["--verify-startup-and-clipboard"];
        bool verifyWinSessionLock = args is ["--verify-win-session-lock"] or ["--verify-win-session-lock", _];
        bool verifyLinuxSessionLock = args is ["--verify-linux-session-lock", _];
        if (verifyClipboard && Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted") return 2;
        if (verifyWinSessionLock && !OperatingSystem.IsWindows()) return 2;
        if (verifyLinuxSessionLock && !OperatingSystem.IsLinux()) return 2;
        bool verifySessionLock = verifyWinSessionLock || verifyLinuxSessionLock;
        string? lockReport = verifySessionLock && args.Length == 2 ? Path.GetFullPath(args[1]) : null;
        if (lockReport != null && (File.Exists(lockReport) || !Directory.Exists(Path.GetDirectoryName(lockReport)))) return 2;

        // The packaged executable can prove startup without opening a vault or loading a user's profile.
        var directory = Directory.CreateTempSubdirectory("SQLitePass-startup-");
        try
        {
            AppContext.SetData("SQLitePass.SettingsDirectory", directory.FullName);
            AppContext.SetData("SQLitePass.StartupProbe", true);
            AppContext.SetData("SQLitePass.ClipboardProbe", verifyClipboard);
            AppContext.SetData("SQLitePass.TrayProbe", verifyTray);
            AppContext.SetData("SQLitePass.AutoTypeProbe", verifyTray);
            AppContext.SetData("SQLitePass.InteractiveSessionLockProbe", verifySessionLock);
            AppContext.SetData("SQLitePass.InteractiveSessionLockReport", lockReport);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }
        finally { directory.Delete(recursive: true); }
    }

    [SupportedOSPlatform("windows")]
    private static void ConfigureWindowsUpdateHooks(VelopackApp velopack, string applicationName)
    {
        velopack.OnBeforeUpdateFastCallback(_ => SingleInstanceCoordinator.ShutdownForUpdateOrUninstall(applicationName));
        velopack.OnBeforeUninstallFastCallback(_ =>
        {
            SingleInstanceCoordinator.ShutdownForUpdateOrUninstall(applicationName);
            AppSettingsManager.RemoveStartupRegistrationForUninstall();
        });
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
