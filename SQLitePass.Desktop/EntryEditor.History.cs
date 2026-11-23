using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SQLitePass.Core.Models;

namespace SQLitePass.Desktop;

public sealed partial class EntryEditor
{
    private sealed class HistoryChoice(PwEntryHistory revision)
    {
        public PwEntryHistory Revision { get; } = revision;
        public override string ToString() => Revision.SavedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + " · " + Revision.Title;
    }

    private void InitializeHistory()
    {
        RestoreHistory.Content = _text("HistoryRestoreTitle", []);
        ClearHistory.Content = _text("ClearHistory", []);
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        History.ItemsSource = _draft.History.OrderByDescending(revision => revision.SavedAt).Select(revision => new HistoryChoice(revision)).ToArray();
        ClearHistory.IsEnabled = _draft.History.Count != 0;
    }

    private void History_SelectionChanged(object? sender, SelectionChangedEventArgs e) => RestoreHistory.IsEnabled = History.SelectedItem is HistoryChoice;

    private async void RestoreHistory_Click(object? sender, RoutedEventArgs e) => await RestoreSelectedHistoryAsync();

    internal Task RestoreSelectedHistoryAsync() => History.SelectedItem is HistoryChoice selected
        ? RestoreHistoryAsync(selected.Revision) : Task.CompletedTask;

    private Task RestoreHistoryAsync(PwEntryHistory history) => RunEditorOperationAsync(async token =>
    {
        if (!await ConfirmHistoryAsync(_text("HistoryRestoreTitle", []), _text("HistoryRestoreConfirm", [history.SavedAt.ToLocalTime()]), token)) return;
        token.ThrowIfCancellationRequested();
        PwEntry? snapshot = null;
        try
        {
            if (!string.IsNullOrEmpty(history.SnapshotJson))
                snapshot = await Task.Run(() => EntrySerialization.Deserialize(history.SnapshotJson), token);
            token.ThrowIfCancellationRequested();
            if (snapshot != null)
            {
                SetCredentialFields(snapshot);
                foreach (var field in _draft.CustomFields) field.Value = "";
                _draft.CustomFields.Clear();
                foreach (var field in snapshot.CustomFields) _draft.CustomFields.Add(field);
                _editDraft.ReplaceAttachmentsFrom(snapshot);
                snapshot.CustomFields.Clear();
            }
            EntryTitle.Text = history.Title;
            UserName.Text = history.UserName;
            Password.Text = history.Password;
            Url.Text = history.Url;
            Notes.Text = history.Notes;
            Sections.SelectedItem = CredentialsTab;
        }
        finally { snapshot?.ClearSecrets(); }
    }, "HistoryRestoreFailed");

    private async void ClearHistory_Click(object? sender, RoutedEventArgs e) => await ClearHistoryAsync();

    internal Task ClearHistoryAsync() => RunEditorOperationAsync(async token =>
    {
        if (!_editDraft.HistoryLoaded) await LoadHistoryAsync(token);
        if (!await ConfirmHistoryAsync(_text("ClearHistory", []), _text("ClearHistoryConfirm", []), token)) return;
        token.ThrowIfCancellationRequested();
        foreach (var revision in _draft.History)
            revision.Title = revision.UserName = revision.Password = revision.Url = revision.Notes = revision.SnapshotJson = "";
        _draft.History.Clear();
        MarkEdited();
        RefreshHistory();
    }, "HistoryRestoreFailed");

    private async Task<bool> ConfirmHistoryAsync(string title, string message, CancellationToken token)
    {
        var accept = new Button { Content = _text("Ok", []) };
        var cancel = new Button { Content = _text("Cancel", []), IsCancel = true };
        var dialog = new Window
        {
            Title = title, Width = 450, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 12, Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { accept, cancel } }
                }
            }
        };
        if (Owner is VaultWindow window) window.ProtectDialog(dialog);
        accept.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        _confirmation = dialog;
        try { return await dialog.ShowDialog<bool>(this).WaitAsync(token); }
        finally { _confirmation = null; }
    }
}
