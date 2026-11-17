using System.Windows;
using SQLitePass.Core.Services;
using Velopack;
using Velopack.Sources;

namespace SQLitePass.UI.Services;

public static class UpdateService
{
    public const string GitHubRepoUrl = "https://github.com/FuryGamingASD/SQLitePass";
    private static bool _running;

    public static async Task CheckForUpdatesAsync(bool silent = false, Func<Task>? beforeRestart = null)
    {
        if (_running) return; _running = true;
        try
        {
            var manager = new UpdateManager(new GithubSource(GitHubRepoUrl, null, false));
            if (!manager.IsInstalled)
            {
                if (!silent) MessageBox.Show(LocalizationManager.GetString("UpdateUnavailable"), LocalizationManager.GetString("MenuCheckUpdates"));
                return;
            }
            var update = await manager.CheckForUpdatesAsync();
            if (update == null)
            {
                if (!silent) MessageBox.Show(LocalizationManager.GetString("UpdateCurrent", manager.CurrentVersion!), LocalizationManager.GetString("MenuCheckUpdates"));
                return;
            }
            if (silent) return;
            if (MessageBox.Show(LocalizationManager.GetString("UpdateConfirm", update.TargetFullRelease.Version), LocalizationManager.GetString("MenuCheckUpdates"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await manager.DownloadUpdatesAsync(update);
            if (beforeRestart != null) await beforeRestart();
            manager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception exception)
        {
            AppLogger.LogError("Update did not complete", exception);
            if (!silent) MessageBox.Show(LocalizationManager.GetString("UpdateFailed"), LocalizationManager.GetString("MenuCheckUpdates"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _running = false; }
    }
}
