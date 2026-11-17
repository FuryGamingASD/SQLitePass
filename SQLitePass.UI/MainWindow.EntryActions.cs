using System.Windows;
using SQLitePass.Core.Models;
using SQLitePass.UI.Dialogs;
using SQLitePass.UI.Services;

namespace SQLitePass.UI;

public partial class MainWindow
{
    private bool _entryCommandBusy;
    private CancellationTokenSource? _entryDetailsCancellation;

    private async Task<EntryEditDraft> PrepareEntryDraftAsync(PwEntry overview, int generation)
    {
        using var cancellation = new CancellationTokenSource(); _entryDetailsCancellation = cancellation;
        var storage = _storage; var crypto = _crypto; PwEntry? full = null;
        try
        {
            full = await storage.LoadEntryAsync(overview.Id, crypto, overview.Revision, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var draft = await Task.Run(() => EntryEditDraft.Create(full), cancellation.Token);
            if (_isLocked || generation != _sessionGeneration || cancellation.IsCancellationRequested)
            { draft.Dispose(); throw new OperationCanceledException(); }
            return draft;
        }
        finally
        {
            full?.ClearSecrets();
            if (ReferenceEquals(_entryDetailsCancellation, cancellation)) _entryDetailsCancellation = null;
        }
    }

    private async void BtnAddEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _isLocking || _entryCommandBusy) return;
        _entryCommandBusy = true; int generation = _sessionGeneration;
        var storage = _storage; var crypto = _crypto;
        EntryEditDraft? draft = null;
        try
        {
            draft = EntryEditDraft.Create(new PwEntry
            {
                GroupId = _selectedGroup?.Id ?? _metadata.RootGroupId ?? Guid.Empty,
                Title = LocalizationManager.GetString("NewEntryDefault")
            });
            var dialog = new EntryEditDialog(draft) { Owner = this };
            if (dialog.ShowDialog() != true || _isLocked || generation != _sessionGeneration) return;
            if (draft.IsSaveRequired != true) return;
            await storage.SaveEntryAsync(draft.Current, crypto);
            if (_isLocked || generation != _sessionGeneration) return;
            _entryToSelectAfterRefresh = draft.Current.Id; TxtSearch.Clear(); draft.Dispose(); await RefreshDataAsync();
        }
        catch (Exception ex)
        { if (!_isLocked && generation == _sessionGeneration) MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("EntrySaveFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { draft?.Dispose(); _entryCommandBusy = false; }
    }

    private async void BtnEditEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _isLocking || _entryCommandBusy || _selectedEntry == null) return;
        _entryCommandBusy = true; int generation = _sessionGeneration;
        var storage = _storage; var crypto = _crypto; var overview = _selectedEntry;
        try
        {
            using var draft = await PrepareEntryDraftAsync(overview, generation);
            var dialog = new EntryEditDialog(draft) { Owner = this };
            if (dialog.ShowDialog() != true || _isLocked || generation != _sessionGeneration) return;
            if (draft.IsSaveRequired != true) return;
            await storage.SaveEntryAsync(draft.Current, crypto);
            if (_isLocked || generation != _sessionGeneration) return;
            _entryToSelectAfterRefresh = draft.Current.Id; draft.Dispose(); await RefreshDataAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        { if (!_isLocked && generation == _sessionGeneration) MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("EntrySaveFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _entryCommandBusy = false; }
    }

    private async void BtnDuplicateEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _isLocking || _entryCommandBusy || _selectedEntry == null) return;
        _entryCommandBusy = true; int generation = _sessionGeneration;
        var storage = _storage; var crypto = _crypto;
        using var cancellation = new CancellationTokenSource(); _entryDetailsCancellation = cancellation;
        PwEntry? full = null, duplicate = null;
        try
        {
            var overview = _selectedEntry;
            full = await storage.LoadEntryAsync(overview.Id, crypto, overview.Revision, cancellation.Token);
            duplicate = await Task.Run(() => full.DeepClone(newIdentity: true), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_isLocked || generation != _sessionGeneration) return;
            duplicate.Title += LocalizationManager.GetString("EntryCopySuffix");
            await storage.SaveEntryAsync(duplicate, crypto);
            if (_isLocked || generation != _sessionGeneration) return;
            _entryToSelectAfterRefresh = duplicate.Id; full.ClearSecrets(); duplicate.ClearSecrets(); await RefreshDataAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        { if (!_isLocked && generation == _sessionGeneration) MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("EntryDuplicateFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            full?.ClearSecrets(); duplicate?.ClearSecrets(); _entryCommandBusy = false;
            if (ReferenceEquals(_entryDetailsCancellation, cancellation)) _entryDetailsCancellation = null;
        }
    }

    private async void BtnRestoreEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _isLocking || _entryCommandBusy || _selectedEntry is not { } selected || !IsInTrash(selected)) return;
        int generation = _sessionGeneration; var storage = _storage; var crypto = _crypto;
        _entryCommandBusy = true;
        try
        {
            var destinations = GroupPaths(); if (_metadata.RecycleBinGroupId is { } trash) destinations.Remove(trash);
            var dialog = new RestoreEntryDialog(destinations, _metadata.RootGroupId) { Owner = this };
            if (dialog.ShowDialog() != true || _isLocked || generation != _sessionGeneration || dialog.DestinationId is not { } destination) return;
            await storage.RestoreEntryAsync(selected.Id, selected.Revision, destination, crypto);
            if (_isLocked || generation != _sessionGeneration) return;
            _selectedGroup = null; _entryToSelectAfterRefresh = selected.Id;
            await RefreshDataAsync();
            if (!_isLocked && generation == _sessionGeneration) TxtOperationStatus.Text = LocalizationManager.GetString("EntryRestored");
        }
        catch (Exception exception)
        {
            if (!_isLocked && generation == _sessionGeneration)
                MessageBox.Show(this, UiErrorMessages.GetText(exception), LocalizationManager.GetString("OperationFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _entryCommandBusy = false; }
    }
}
