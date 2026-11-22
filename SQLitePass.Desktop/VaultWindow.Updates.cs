using Avalonia.Interactivity;
using Avalonia.Threading;
using SQLitePass.Core.Services;
using Velopack;
using Velopack.Sources;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private const string PublicReleaseRepository = "https://github.com/FuryGamingASD/SQLitePass";

    private async void CheckUpdates_Click(object? sender, RoutedEventArgs e) =>
        await CheckUpdatesAsync(new GithubSource(PublicReleaseRepository, null, prerelease: false));

    internal async Task<bool> CheckUpdatesAsync(IUpdateSource source, string[]? restartArgs = null)
    {
        UpdateManager? manager = null;
        VelopackAsset? prepared = null;
        await RunOperationAsync(async token =>
        {
            try
            {
                string channel = OperatingSystem.IsWindows() ? "win" : "linux";
                manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = channel });
                if (!manager.IsInstalled)
                {
                    StatusText.Text = Text("UpdateUnavailable");
                    return;
                }
                if (!string.Equals(manager.AppId, "SQLitePass", StringComparison.Ordinal))
                {
                    StatusText.Text = Text("UpdatePublicPackageRequired");
                    return;
                }

                var available = await manager.CheckForUpdatesAsync().WaitAsync(token);
                token.ThrowIfCancellationRequested();
                if (available == null)
                {
                    StatusText.Text = Text("UpdateCurrent", manager.CurrentVersion!);
                    return;
                }
                if (!string.Equals(available.TargetFullRelease.PackageId, manager.AppId, StringComparison.Ordinal))
                    throw new InvalidDataException("The update feed supplied an unexpected application identity.");

                string title = Text("MenuCheckUpdates").Replace("_", "", StringComparison.Ordinal).TrimEnd('.');
                if (!await ConfirmAsync(title, Text("UpdateConfirm", available.TargetFullRelease.Version))) return;
                token.ThrowIfCancellationRequested();
                StatusText.Text = Text("UpdateDownloading", 0);
                await manager.DownloadUpdatesAsync(available, percent => Dispatcher.UIThread.Post(() =>
                {
                    if (_operation?.Token == token && !token.IsCancellationRequested)
                        StatusText.Text = Text("UpdateDownloading", percent);
                }), token);
                token.ThrowIfCancellationRequested();
                prepared = available.TargetFullRelease;
            }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            {
                AppLogger.LogError("The release update could not be prepared", error);
                StatusText.Text = Text("UpdateFailed");
            }
        }, canCancel: true);

        if (manager == null || prepared == null || _closing || _systemLocked) return false;
        try
        {
            await LockVaultAsync();
            if (_closing || _systemLocked) return false;
            manager.WaitExitThenApplyUpdates(prepared, silent: false, restart: true, restartArgs);
            RequestExit();
            return true;
        }
        catch (Exception error)
        {
            AppLogger.LogError("The prepared release update could not start", error);
            StatusText.Text = Text("UpdateFailed");
            return false;
        }
    }
}
