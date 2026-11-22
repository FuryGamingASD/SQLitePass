using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class CredentialChangeDialog : Window
{
    private readonly Func<string, object[], string> _text;
    private readonly bool _currentRequiresFile;
    private bool _closed;
    private bool _picking;
    private (string CurrentPassword, string CurrentFile, string ReplacementPassword, string ReplacementFile)? _accepted;

    public CredentialChangeDialog() : this((key, _) => key, false) { }

    internal CredentialChangeDialog(Func<string, object[], string> text, bool currentRequiresFile)
    {
        _text = text;
        _currentRequiresFile = currentRequiresFile;
        InitializeComponent();
        Title = text("MasterChangeTitle", []);
        Description.Text = text("MasterChangeDescription", []);
        CurrentPasswordLabel.Text = text("CurrentMasterPassword", []);
        CurrentFileLabel.Text = text("CurrentKeyFile", []);
        ReplacementLabel.Text = text("NewMasterPassword", []);
        ConfirmationLabel.Text = text("ConfirmMasterPassword", []);
        ReplacementFileLabel.Text = text("NewKeyFile", []);
        BrowseCurrent.Content = BrowseReplacement.Content = text("Browse", []);
        BackupInfo.Text = text("PreviousKeyBackupInfo", []);
        CancelAction.Content = text("Cancel", []);
        SaveAction.Content = text("SaveNewCredential", []);
        Opened += (_, _) => CurrentPassword.Focus();
        Closed += (_, _) =>
        {
            _closed = true;
            CurrentPassword.Clear(); CurrentFile.Clear(); ReplacementPassword.Clear(); ConfirmationPassword.Clear(); ReplacementFile.Clear();
        };
    }

    private async void BrowseCurrent_Click(object? sender, RoutedEventArgs e) => await BrowseKeyFileAsync(CurrentFile);
    private async void BrowseReplacement_Click(object? sender, RoutedEventArgs e) => await BrowseKeyFileAsync(ReplacementFile);

    private async Task BrowseKeyFileAsync(TextBox target)
    {
        if (_picking) return;
        _picking = true;
        SaveAction.IsEnabled = false;
        try
        {
            var items = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [FilePickerFileTypes.All] });
            if (_closed || items.Count == 0) return;
            if (items[0].TryGetLocalPath() is { } path) target.Text = path;
            else ShowValidation("SelectKeyFile");
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                AppLogger.LogError("Unable to select a key file", ex);
                ShowValidation("SelectKeyFile");
            }
        }
        finally
        {
            _picking = false;
            if (!_closed) SaveAction.IsEnabled = true;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    internal void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_closed || _picking) return;
        var currentPassword = CurrentPassword.Text ?? "";
        var currentFile = CurrentFile.Text?.Trim() ?? "";
        var replacementPassword = ReplacementPassword.Text ?? "";
        var replacementFile = ReplacementFile.Text?.Trim() ?? "";
        if (replacementPassword != (ConfirmationPassword.Text ?? ""))
        {
            ShowValidation("PasswordMismatch");
            return;
        }
        bool needsCurrentFile = _currentRequiresFile || currentFile.Length != 0 || currentPassword.Length == 0;
        bool needsReplacementFile = replacementFile.Length != 0 || replacementPassword.Length == 0;
        if ((needsCurrentFile && !File.Exists(currentFile)) || (needsReplacementFile && !File.Exists(replacementFile)))
        {
            ShowValidation("SelectKeyFile");
            return;
        }
        _accepted = (currentPassword, currentFile, replacementPassword, replacementFile);
        Close(true);
    }

    private void ShowValidation(string key)
    {
        ValidationMessage.Text = _text(key, []);
        ValidationMessage.IsVisible = true;
    }

    internal Task<(MasterKey Current, MasterKey Replacement)> TakeCredentialsAsync(CancellationToken token)
    {
        var form = _accepted ?? throw new InvalidOperationException("The credential form was not submitted.");
        _accepted = null;
        return Task.Run(() =>
        {
            var current = ReadCredential(form.CurrentPassword, form.CurrentFile);
            try { return (current, ReadCredential(form.ReplacementPassword, form.ReplacementFile)); }
            catch { current.Dispose(); throw; }
        }, token);
    }

    private static MasterKey ReadCredential(string password, string path) => path.Length == 0
        ? new MasterKey(password) : MasterKey.FromFile(password, path);
}
