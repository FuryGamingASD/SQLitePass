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

    private Task CopyEntryFieldAsync(bool password, EntryRow? target = null) => RunOperationAsync(async token =>
    {
        var row = target ?? Entries.SelectedItem as EntryRow;
        if (_session is not { } session || _clipboard is not { } clipboard || _query.OnlyRecycleBin || row == null) return;
        string value = password
            ? await session.Storage.LoadEntryPasswordAsync(row.Id, session.Crypto, row.Revision, token)
            : await session.Storage.LoadEntryUserNameAsync(row.Id, session.Crypto, row.Revision, token);
        token.ThrowIfCancellationRequested();
        if (value.Length == 0 || !ReferenceEquals(_session, session)) return;
        int seconds = Math.Clamp(_settings.ClipboardClearSeconds, 3, 300);
        bool copied = await clipboard.CopyAsync(value, TimeSpan.FromSeconds(seconds), token);
        token.ThrowIfCancellationRequested();
        ClipboardStatus.Text = copied ? Text("ClipboardCopiedFor", Text(password ? "ColPassword" : "ColUserName"), seconds) : ClipboardCopyFailureText(clipboard);
    });

    private async void ClearClipboard_Click(object? sender, RoutedEventArgs e) => await RunOperationAsync(async _ =>
    {
        if (_clipboard is { } clipboard) PresentClipboardClear(await clipboard.ClearAsync());
    });

    private async void PasswordGenerator_Click(object? sender, RoutedEventArgs e) => await RunOperationAsync(async token =>
    {
        await ShowOwnedAsync(CreatePasswordGenerator(token, allowUse: false));
    });

    private PasswordGeneratorDialog CreatePasswordGenerator(CancellationToken token, bool allowUse)
    {
        var dialog = new PasswordGeneratorDialog(Text, _clipboard is { } clipboard ? async password =>
        {
            int seconds = Math.Clamp(_settings.ClipboardClearSeconds, 3, 300);
            bool copied = await clipboard.CopyAsync(password, TimeSpan.FromSeconds(seconds), token);
            token.ThrowIfCancellationRequested();
            return copied ? Text("ClipboardCopiedFor", Text("GenResult").TrimEnd(' ', ':'), seconds) : ClipboardCopyFailureText(clipboard);
        } : null, allowUse);
        if (allowUse) ProtectDialog(dialog);
        return dialog;
    }

    private void ClipboardExpired(ClipboardClearResult result)
    {
        if (!_closing && _session != null && _operation == null) PresentClipboardClear(result);
    }

    private void PresentClipboardClear(ClipboardClearResult result)
    {
        if (result == ClipboardClearResult.Cleared) ClipboardStatus.Text = Text("ClipboardCleared");
        if (result == ClipboardClearResult.Busy) ClipboardStatus.Text = Text("ClipboardClearFailed");
    }

    private string ClipboardCopyFailureText(SessionClipboard clipboard) => clipboard.CopyBlockedReason switch
    {
        ClipboardCopyBlockedReason.WindowsClipboardSyncEnabled => Text("ClipboardSyncBlocked"),
        ClipboardCopyBlockedReason.SecureWindowsClipboardUnavailable => Text("ClipboardSecurityUnavailable"),
        _ => Text("ClipboardBusy")
    };
}
