using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SQLitePass.Core.Database;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private async void RestoreBackup_Click(object? sender, RoutedEventArgs e) => await RecoverBackupAsync(PickRecoveryDestinationAsync);

    internal Task RecoverBackupAsync(Func<string, Task<string?>> chooseDestination) => RunOperationAsync(async token =>
    {
        if (!_machinePolicy.CanOpenVault)
        {
            StatusText.Text = Text("MachinePolicyInvalid");
            return;
        }
        if (_session != null) return;
        string path = VaultPath.Text?.Trim() ?? "";
        string backup = path + ".backup";
        if (path.Length == 0 || !File.Exists(backup))
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Text("LoginRestoreBackup"),
                FileTypeFilter = [new FilePickerFileType("SQLitePass") { Patterns = ["*.backup", "*.backup.*"] }, FilePickerFileTypes.All]
            });
            token.ThrowIfCancellationRequested();
            if (files.Count == 0) return;
            backup = RequireLocalFile(files[0]);
        }

        if (!await ConfirmAsync(Text("RestoreTitle"), Text("RecoveryCopyConfirm"))) return;
        token.ThrowIfCancellationRequested();
        string? destination = await chooseDestination(backup);
        token.ThrowIfCancellationRequested();
        if (destination == null) return;
        using var master = await ReadCredentialAsync(token);
        await SqliteStorageProvider.RecoverBackupCopyAsync(backup, destination, master, token);
        token.ThrowIfCancellationRequested();
        VaultPath.Text = destination;
        StatusText.Text = Text("RecoveryCopyComplete");
    });

    private async Task<string?> PickRecoveryDestinationAsync(string backupPath)
    {
        string name = Path.GetFileName(backupPath);
        int suffix = name.LastIndexOf(".backup", StringComparison.OrdinalIgnoreCase);
        if (suffix > 0) name = name[..suffix];
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Text("RestoreTitle"),
            SuggestedFileName = Path.GetFileNameWithoutExtension(name) + "-recovered.kpd",
            DefaultExtension = "kpd",
            FileTypeChoices = [new FilePickerFileType("SQLitePass") { Patterns = ["*.kpd"] }]
        });
        return file == null ? null : RequireLocalFile(file);
    }

    private void OnBackupStatusChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateBackupStatus);

    private void UpdateBackupStatus()
    {
        if (_session?.Storage is not { } storage) return;
        BackupStatus.Text = storage.IsReadOnly ? Text("BackupReadOnly") :
            !storage.BackupEnabled ? Text("BackupDisabled") :
            storage.LastBackupError != null ? Text("BackupFailed") :
            storage.LastBackupAt is { } updated ? Text("BackupVerifiedAt", updated.ToLocalTime()) : Text("BackupNotChecked");
    }
}
