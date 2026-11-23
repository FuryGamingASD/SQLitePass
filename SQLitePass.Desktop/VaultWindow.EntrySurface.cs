using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SQLitePass.Core.Services;
using SQLitePass.UI.Controls;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private static string NotePreview(string notes) => notes.Length > 240 ? notes[..240] : notes;

    private async void EntryTitle_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: EntryRow row }) return;
        e.Handled = true;
        await OpenSelectedEntryAsync(row);
    }

    private async void EntryUser_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: EntryRow row }) return;
        e.Handled = true;
        await CopyEntryFieldAsync(password: false, row);
    }

    private async void EntryPassword_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: EntryRow row }) return;
        e.Handled = true;
        await CopyEntryFieldAsync(password: true, row);
    }

    private async void EntryUrl_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: EntryRow row }) return;
        e.Handled = true;
        await OpenEntryUrlAsync(row);
    }

    private async void Entries_KeyDown(object? sender, KeyEventArgs e)
    {
        if (Entries.SelectedItem is not EntryRow row) return;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await OpenSelectedEntryAsync(row);
                break;
            case Key.Delete when DeleteEntry.IsEnabled:
                e.Handled = true;
                DeleteEntry_Click(sender, e);
                break;
        }
    }

    private Task OpenSelectedEntryAsync(EntryRow row) =>
        _session is { Storage.IsReadOnly: false } && !_query.OnlyRecycleBin
            ? OpenEditorAsync(row)
            : RunOperationAsync(async token =>
            {
                if (_session is not { } session) return;
                var entry = await session.Storage.LoadEntryAsync(row.Id, session.Crypto, row.Revision, token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    await ShowOwnedAsync(new EntryEditor(entry, Text, session.Storage.FilePath, readOnly: true));
                }
                finally { entry.ClearSecrets(); }
            });

    private async void OpenUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is EntryRow row) await OpenEntryUrlAsync(row);
    }

    private Task OpenEntryUrlAsync(EntryRow row) => RunOperationAsync(async token =>
    {
        if (_session == null || _query.OnlyRecycleBin || EntryUri(row.Url) is not { } uri) return;
        token.ThrowIfCancellationRequested();
        if (!await Launcher.LaunchUriAsync(uri)) StatusText.Text = Text("ErrorUnexpected");
    });

    private static Uri? EntryUri(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string input = url.Trim();
        if (!input.Contains("://", StringComparison.Ordinal)) input = "https://" + input;
        return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
            uri.Host.Length != 0 && uri.UserInfo.Length == 0 ? uri : null;
    }

    private void UpdateEntryPreview()
    {
        if (EntryPreview == null) return;
        HidePreviewPassword();
        var selected = Entries.SelectedItem as EntryRow;
        EntryPreview.IsVisible = selected != null;
        PreviewTitle.Text = selected?.Title;
        PreviewUser.Text = selected?.UserName;
        PreviewUrl.Text = selected?.Url;
        PreviewSecret.Text = selected?.PasswordMask;
        SetPreviewQuality(null);
    }

    private void SetPreviewQuality(double? score)
    {
        PreviewQuality.IsVisible = score.HasValue;
        PreviewQuality.Value = score ?? 0;
        string? quality = score.HasValue ? Text("QualityScore", score.Value) : null;
        ToolTip.SetTip(PreviewQuality, quality);
        Avalonia.Automation.AutomationProperties.SetName(PreviewQuality, quality);
    }

    private async void PreviewEstimateQuality_Click(object? sender, RoutedEventArgs e) => await RunOperationAsync(async token =>
    {
        if (_session is not { } session || _query.OnlyRecycleBin ||
            Entries.SelectedItem is not EntryRow { HasPassword: true } row) return;

        SetPreviewQuality(null);
        string password = await session.Storage.LoadEntryPasswordAsync(row.Id, session.Crypto, row.Revision, token);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(_session, session) || !ReferenceEquals(Entries.SelectedItem, row)) return;
        if (password.Length > 512)
            StatusText.Text = Text("QualityNotEstimated");
        else if (password.Length > 0)
            SetPreviewQuality(PasswordGenerator.CalculateEntropy(password));
    });

    private async void PreviewReveal_Click(object? sender, RoutedEventArgs e)
    {
        if (_previewRevealTimer.IsEnabled)
        {
            HidePreviewPassword();
            return;
        }
        await RunOperationAsync(async token =>
        {
            if (_session is not { } session || _query.OnlyRecycleBin ||
                Entries.SelectedItem is not EntryRow { HasPassword: true } row) return;
            int request = ++_previewRequest;
            string password = await session.Storage.LoadEntryPasswordAsync(row.Id, session.Crypto, row.Revision, token);
            token.ThrowIfCancellationRequested();
            if (request != _previewRequest || !ReferenceEquals(_session, session) || !ReferenceEquals(Entries.SelectedItem, row)) return;
            PreviewSecret.Text = password;
            PreviewRevealIcon.Kind = AppIconKind.EyeOff;
            LabelAction(PreviewReveal, "HidePassword");
            _previewRevealTimer.Start();
        });
    }

    private void HidePreviewPassword()
    {
        ++_previewRequest;
        _previewRevealTimer.Stop();
        PreviewSecret.Text = (Entries.SelectedItem as EntryRow)?.PasswordMask;
        PreviewRevealIcon.Kind = AppIconKind.Eye;
        LabelAction(PreviewReveal, "ShowPassword");
    }
}
