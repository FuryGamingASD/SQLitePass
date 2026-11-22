using Avalonia.Interactivity;
using SQLitePass.Core.Services;
using SQLitePass.Desktop.Services;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private SessionClipboard? _clipboard;

    internal SessionClipboard? NativeClipboard => _clipboard;

    private void InitializeClipboard()
    {
        try
        {
            _clipboard = SessionClipboard.Create(this);
            if (_clipboard != null) _clipboard.Expired += ClipboardExpired;
        }
        catch (Exception error) { AppLogger.LogError("The desktop clipboard is unavailable", error); }
        UpdateVaultCommands();
    }

    private async void CopyUser_Click(object? sender, RoutedEventArgs e) => await CopyEntryFieldAsync(password: false);
    private async void CopyPassword_Click(object? sender, RoutedEventArgs e) => await CopyEntryFieldAsync(password: true);

    private Task CopyEntryFieldAsync(bool password) => RunOperationAsync(async token =>
    {
        if (_session is not { } session || _clipboard is not { } clipboard || _query.OnlyRecycleBin || Entries.SelectedItem is not EntryRow row) return;
        var overview = await session.Storage.LoadEntryOverviewAsync(row.Id, session.Crypto, row.Revision, token);
        try
        {
            string value = password ? overview.Password : overview.UserName;
            if (value.Length == 0) return;
            int seconds = Math.Clamp(_settings.ClipboardClearSeconds, 3, 300);
            bool copied = await clipboard.CopyAsync(value, TimeSpan.FromSeconds(seconds), token);
            token.ThrowIfCancellationRequested();
            StatusText.Text = copied ? Text("ClipboardCopiedFor", Text(password ? "ColPassword" : "ColUserName"), seconds) : Text("ClipboardBusy");
        }
        finally { overview.ClearSecrets(); }
    });

    private async void ClearClipboard_Click(object? sender, RoutedEventArgs e) => await RunOperationAsync(async _ =>
    {
        if (_clipboard is { } clipboard) PresentClipboardClear(await clipboard.ClearAsync());
    });

    private void ClipboardExpired(ClipboardClearResult result)
    {
        if (!_closing && _session != null && _operation == null) PresentClipboardClear(result);
    }

    private void PresentClipboardClear(ClipboardClearResult result)
    {
        if (result == ClipboardClearResult.Cleared) StatusText.Text = Text("ClipboardCleared");
        if (result == ClipboardClearResult.Busy) StatusText.Text = Text("ClipboardClearFailed");
    }
}
