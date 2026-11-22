using Avalonia.Controls;
using Avalonia.Interactivity;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private IEnumerable<GroupChoice> FolderChoices => Groups.Items.OfType<GroupChoice>();
    private PwGroup? SelectedFolder => (Groups.SelectedItem as GroupChoice)?.Folder;
    private Guid? RootFolderId => FolderChoices.FirstOrDefault(choice => choice.Folder?.IsRoot == true)?.Id;

    private void SetVaultCommandLabels()
    {
        FileMenu.Header = Text("MenuFile");
        ChangeCredential.Header = Text("MenuChangeMasterPassword");
        ImportKeePass.Header = Text("MenuImportCsv");
        ImportBitwarden.Header = Text("MenuImportBitwardenJson");
        ImportBrowser.Header = Text("MenuImportBrowserCsv");
        ExportKeePass.Header = Text("MenuExportCsv");
        ClearClipboard.Header = Text("MenuClearClipboard");
        PasswordGeneratorCommand.Header = Text("MenuPasswordGenerator");
        CheckUpdates.Header = Text("MenuCheckUpdates");
        SettingsCommand.Header = Text("MenuSettings");
        ExitApplication.Header = Text("MenuExit");
        FolderMenu.Header = Text("FoldersAndGroups");
        CreateFolder.Header = Text("MenuAddGroup");
        RenameFolder.Header = Text("MenuRenameGroup");
        MoveFolder.Header = Text("MoveFolderToFolder");
        MoveFolderUp.Header = Text("MenuMoveGroupUp");
        MoveFolderDown.Header = Text("MenuMoveGroupDown");
        DeleteFolder.Header = Text("MenuDeleteGroup");
        EntryMenu.Header = Text("EntriesList");
        CopyUser.Header = Text("TbCopyUser");
        CopyPassword.Header = Text("TbCopyPassword");
        AutoTypeEntry.Header = Text("MenuAutoType");
        DuplicateEntry.Header = Text("MenuDuplicateEntry");
        MoveEntryCommand.Header = Text("MoveEntryToFolder");
        DeleteEntry.Header = Text("MenuDeleteEntry");
        RestoreEntry.Header = Text("RestoreEntry");
        EmptyRecycleBin.Header = Text("MenuEmptyTrash");
    }

    private void UpdateVaultCommands()
    {
        var folder = SelectedFolder;
        bool writable = _session is { Storage.IsReadOnly: false };
        bool hasEntry = Entries.SelectedItem is EntryRow;
        ChangeCredential.IsEnabled = writable;
        ClearClipboard.IsEnabled = _clipboard != null;
        CopyUser.IsEnabled = CopyPassword.IsEnabled = _clipboard != null && hasEntry && !_query.OnlyRecycleBin;
        AutoTypeEntry.IsEnabled = _autoTypeAvailable && hasEntry && !_query.OnlyRecycleBin;
        ImportKeePass.IsEnabled = ImportBitwarden.IsEnabled = ImportBrowser.IsEnabled = writable;
        ExportKeePass.IsEnabled = _session != null;
        bool editableFolder = writable && folder is { IsRoot: false, IsRecycleBin: false };
        CreateFolder.IsEnabled = writable && !_query.OnlyRecycleBin;
        RenameFolder.IsEnabled = writable && folder is { IsRecycleBin: false };
        MoveFolder.IsEnabled = DeleteFolder.IsEnabled = editableFolder;
        MoveFolderUp.IsEnabled = MoveFolderDown.IsEnabled = editableFolder;
        AddEntry.IsEnabled = writable && !_query.OnlyRecycleBin;
        EditEntry.IsEnabled = DuplicateEntry.IsEnabled = MoveEntryCommand.IsEnabled = writable && hasEntry && !_query.OnlyRecycleBin;
        DeleteEntry.IsEnabled = writable && hasEntry;
        RestoreEntry.IsEnabled = writable && hasEntry && _query.OnlyRecycleBin;
        EmptyRecycleBin.IsEnabled = writable && _query.OnlyRecycleBin;
    }

    private async void CreateFolder_Click(object? sender, RoutedEventArgs e) => await OpenFolderEditorAsync(rename: false);
    private async void RenameFolder_Click(object? sender, RoutedEventArgs e) => await OpenFolderEditorAsync(rename: true);

    internal Task OpenFolderEditorAsync(bool rename) => RunOperationAsync(async token =>
    {
        if (_session is not { } session || _query.OnlyRecycleBin) return;
        var current = SelectedFolder;
        if (rename && current == null) return;
        var editor = new FolderDialog(Text, rename ? current!.Name : null);
        if (!await ShowOwnedAsync(editor)) return;
        token.ThrowIfCancellationRequested();

        var group = rename ? current!.DeepClone() : new PwGroup { ParentId = current?.Id ?? RootFolderId };
        group.Name = editor.ResultName;
        await session.Storage.SaveGroupAsync(group, session.Crypto);
        await ReloadFolderAsync(session, group.Id, token);
    });

    private async void MoveFolder_Click(object? sender, RoutedEventArgs e) => await MoveFolderAsync();
    private async void MoveFolderUp_Click(object? sender, RoutedEventArgs e) => await ReorderFolderAsync(moveDown: false);
    private async void MoveFolderDown_Click(object? sender, RoutedEventArgs e) => await ReorderFolderAsync(moveDown: true);
    private async void DeleteFolder_Click(object? sender, RoutedEventArgs e) => await DeleteFolderAsync();

    internal Task MoveFolderAsync() => RunOperationAsync(async token =>
    {
        if (_session is not { } session || SelectedFolder is not { IsRoot: false, IsRecycleBin: false } group) return;
        var choices = FolderChoices.Where(choice => choice.Folder is { IsRecycleBin: false }).ToArray();
        var excluded = choices.Where(choice => choice.Folder!.ParentId.HasValue).ToLookup(choice => choice.Folder!.ParentId!.Value);
        var descendants = new List<Guid> { group.Id };
        for (int counter = 0; counter < descendants.Count; counter++)
            descendants.AddRange(excluded[descendants[counter]].Select(choice => choice.Id!.Value));
        var subtree = descendants.ToHashSet();
        var destinations = choices.Where(choice => !subtree.Contains(choice.Id!.Value) && choice.Id != group.ParentId).ToArray();
        if (await ChooseDestinationAsync(destinations, "MoveFolderToFolder", "MoveDestination") is not { } destination) return;
        token.ThrowIfCancellationRequested();
        await session.Storage.MoveGroupAsync(group.Id, group.Revision, destination, session.Crypto);
        await ReloadFolderAsync(session, group.Id, token);
    });

    internal Task ReorderFolderAsync(bool moveDown) => RunOperationAsync(async token =>
    {
        if (_session is not { } session || SelectedFolder is not { IsRoot: false, IsRecycleBin: false } group) return;
        await session.Storage.ReorderGroupAsync(group.Id, group.Revision, moveDown, session.Crypto);
        await ReloadFolderAsync(session, group.Id, token);
    });

    internal Task DeleteFolderAsync() => RunOperationAsync(async token =>
    {
        if (_session is not { } session || SelectedFolder is not { IsRoot: false, IsRecycleBin: false } group) return;
        if (!await ConfirmAsync(Text("MenuDeleteGroup"), Text("FolderDeleteConfirm"))) return;
        token.ThrowIfCancellationRequested();
        await session.Storage.TrashGroupAsync(group.Id, group.Revision, Text("RecycleBin"), session.Crypto);
        await ReloadFolderAsync(session, group.ParentId, token);
    });

    private async void DuplicateEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is EntryRow row) await DuplicateEntryAsync(row);
    }

    internal Task DuplicateEntryAsync(EntryRow row) => RunOperationAsync(async token =>
    {
        if (_session is not { } session || _query.OnlyRecycleBin) return;
        var source = await session.Storage.LoadEntryAsync(row.Id, session.Crypto, row.Revision, token);
        PwEntry? copy = null;
        try
        {
            copy = await Task.Run(() => source.DeepClone(newIdentity: true), token);
            copy.Title += Text("EntryCopySuffix");
            token.ThrowIfCancellationRequested();
            await session.Storage.SaveEntryAsync(copy, session.Crypto);
        }
        finally
        {
            source.ClearSecrets();
            copy?.ClearSecrets();
        }
        await LoadPageAsync(session, _query, null, token);
    });

    private async void MoveEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is EntryRow row) await MoveEntryAsync(row, restore: false);
    }

    private async void RestoreEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is EntryRow row) await MoveEntryAsync(row, restore: true);
    }

    internal Task MoveEntryAsync(EntryRow row, bool restore) => RunOperationAsync(async token =>
    {
        if (_session is not { } session || _query.OnlyRecycleBin != restore) return;
        var destinations = FolderChoices.Where(choice => choice.Folder is { IsRecycleBin: false }).ToArray();
        var destination = await ChooseDestinationAsync(destinations, restore ? "RestoreEntry" : "MoveEntryToFolder",
            restore ? "RestoreDestination" : "MoveDestination");
        if (!destination.HasValue) return;
        token.ThrowIfCancellationRequested();
        if (restore) await session.Storage.RestoreEntryAsync(row.Id, row.Revision, destination.Value, session.Crypto);
        else await session.Storage.MoveEntryAsync(row.Id, row.Revision, destination.Value, session.Crypto);
        await ReloadFolderAsync(session, destination, token);
        if (restore) StatusText.Text = Text("EntryRestored");
    });

    private async void DeleteEntry_Click(object? sender, RoutedEventArgs e)
    {
        if (Entries.SelectedItem is EntryRow row) await DeleteEntryAsync(row);
    }

    internal Task DeleteEntryAsync(EntryRow row) => RunOperationAsync(async token =>
    {
        if (_session is not { } session) return;
        if (_query.OnlyRecycleBin)
        {
            if (!await ConfirmAsync(Text("Delete"), Text("TrashPermanentConfirm"))) return;
            token.ThrowIfCancellationRequested();
            await session.Storage.DeleteEntryAsync(row.Id, row.Revision);
        }
        else await session.Storage.TrashEntryAsync(row.Id, row.Revision, Text("RecycleBin"), session.Crypto);
        await LoadPageAsync(session, _query, null, token);
        StatusText.Text = Text(_query.OnlyRecycleBin ? "EntryDeleted" : "EntryTrashed");
    });

    private async void EmptyRecycleBin_Click(object? sender, RoutedEventArgs e) => await EmptyRecycleBinAsync();

    internal Task EmptyRecycleBinAsync() => RunOperationAsync(async token =>
    {
        if (_session is not { } session || !_query.OnlyRecycleBin || _query.GroupId is not { } trash) return;
        var revisions = await session.Storage.LoadEntryRevisionsAsync(trash, session.Crypto, token);
        if (revisions.Count == 0)
        {
            StatusText.Text = Text("TrashEmpty");
            return;
        }
        if (!await ConfirmAsync(Text("MenuEmptyTrash"), Text("TrashEmptyConfirm", revisions.Count))) return;
        token.ThrowIfCancellationRequested();
        await session.Storage.DeleteEntriesBatchAsync(revisions);
        await LoadPageAsync(session, _query, null, token);
        StatusText.Text = Text("TrashCleared", revisions.Count);
    });

    private async Task<Guid?> ChooseDestinationAsync(GroupChoice[] choices, string title, string label)
    {
        if (choices.Length == 0) return null;
        var dialog = new FolderDialog(Text, title, label, choices);
        return await ShowOwnedAsync(dialog) ? dialog.Destination : null;
    }

    private async Task ReloadFolderAsync(VaultSession session, Guid? folder, CancellationToken token)
    {
        await RefreshEntryQueryAsync(session, _query with { GroupId = folder, OnlyRecycleBin = false, SearchText = "" }, token);
        SearchBox.Clear();
    }
}
