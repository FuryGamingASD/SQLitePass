using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.Desktop;

namespace SQLitePass.Tests;

public sealed class AvaloniaVaultTests
{
    private sealed class HeadlessStartup
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    [Fact]
    public async Task SharedDesktopRejectsBadCredentialsAndClearsThePageOnLock()
    {
        const string password = "Synthetic-only-Passphrase-2026!";
        string directory = Directory.CreateTempSubdirectory("SQLitePass-Avalonia-").FullName;
        string path = Path.Combine(directory, "synthetic.kpd");
        var settings = new AppSettings { Language = "en", LastOpenedVaultPath = path, EnableDualFileBackup = false };
        Guid editedEntryId = Guid.Empty;
        try
        {
            using var master = new MasterKey(password);
            await using (var vault = await VaultSession.CreateAsync(path, master, settings, "General", "Recycle bin"))
            {
                var group = (await vault.Storage.LoadAllGroupsAsync(vault.Crypto)).Single(item => item.Name == "General");
                await vault.Storage.SaveEntriesBatchAsync([
                    new PwEntry { GroupId = group.Id, Title = "Synthetic mail", UserName = "mail-user", Password = "sample-mail" },
                    new PwEntry { GroupId = group.Id, Title = "Synthetic portal", UserName = "portal-user", Password = "sample-portal" },
                    new PwEntry { GroupId = vault.Metadata.RecycleBinGroupId!.Value, Title = "Synthetic deleted entry" }
                ], vault.Crypto);
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var desktop = HeadlessUnitTestSession.StartNew(typeof(HeadlessStartup));
            await desktop.Dispatch(async () =>
            {
                var window = new VaultWindow(settings);
                var passwordBox = window.FindControl<TextBox>("MasterPassword")!;
                var vaultPanel = window.FindControl<Grid>("VaultPanel")!;
                var entryList = window.FindControl<ListBox>("Entries")!;
                window.Show();
                try
                {
                    passwordBox.Text = "incorrect synthetic password";
                    await window.OpenVaultAsync();
                    Assert.False(vaultPanel.IsVisible);
                    Assert.Contains("credential", window.FindControl<TextBlock>("StatusText")!.Text!, StringComparison.OrdinalIgnoreCase);
                    Assert.True(string.IsNullOrEmpty(passwordBox.Text));

                    passwordBox.Text = password;
                    await window.OpenVaultAsync();
                    Assert.True(vaultPanel.IsVisible);
                    Assert.Equal(2, entryList.Items.Count);
                    var groups = window.FindControl<ComboBox>("Groups")!;
                    await window.SelectGroupAsync(groups.Items.OfType<GroupChoice>().Single(item => item.IsRecycleBin));
                    Assert.Equal("Synthetic deleted entry", Assert.IsType<EntryRow>(Assert.Single(entryList.Items)).Title);
                    await window.SelectGroupAsync(groups.Items.OfType<GroupChoice>().Single(item => !item.Id.HasValue));
                    Assert.Equal(2, entryList.Items.Count);
                    window.FindControl<TextBox>("SearchBox")!.Text = "portal";
                    await window.SearchAsync();
                    var row = Assert.IsType<EntryRow>(Assert.Single(entryList.Items));
                    Assert.Equal("Synthetic portal", row.Title);
                    editedEntryId = row.Id;

                    var editing = window.OpenEditorAsync(row);
                    var editor = await FindDialogAsync<EntryEditor>(window, editing, timeout.Token);
                    editor.FindControl<TextBox>("UserName")!.Text = "edited-portal-user";
                    editor.Save_Click(null, new Avalonia.Interactivity.RoutedEventArgs());
                    await editing;
                    Assert.Equal("edited-portal-user", Assert.IsType<EntryRow>(Assert.Single(entryList.Items)).UserName);
                    Assert.True(string.IsNullOrEmpty(editor.FindControl<TextBox>("Password")!.Text));

                    row = Assert.IsType<EntryRow>(Assert.Single(entryList.Items));
                    editing = window.OpenEditorAsync(row);
                    editor = await FindDialogAsync<EntryEditor>(window, editing, timeout.Token);
                    editor.FindControl<TextBox>("UserName")!.Text = "discarded-user";

                    await window.LockVaultAsync();
                    await editing;
                    Assert.False(vaultPanel.IsVisible);
                    Assert.False(editor.IsVisible);
                    Assert.Empty(entryList.Items);
                    Assert.Empty(groups.Items);
                    Assert.True(string.IsNullOrEmpty(passwordBox.Text));
                    Assert.True(string.IsNullOrEmpty(window.FindControl<TextBox>("SearchBox")!.Text));
                }
                finally
                {
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Close();
                    await closed.Task.WaitAsync(timeout.Token);
                }
                return true;
            }, timeout.Token);

            await using var reopened = await VaultSession.OpenAsync(path, master, settings, () => Task.FromResult(false));
            Assert.NotNull(reopened);
            var saved = await reopened.Storage.LoadEntryAsync(editedEntryId, reopened.Crypto);
            try
            {
                Assert.Equal("edited-portal-user", saved.UserName);
                Assert.Equal("portal-user", Assert.Single(saved.History).UserName);
            }
            finally { saved.ClearSecrets(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SharedDesktopMovesFolderEntriesToTrashAndRestoresThemToTheChosenDestination()
    {
        const string password = "Synthetic-folder-workflow!";
        var directory = Directory.CreateTempSubdirectory("SQLitePass-Avalonia-");
        string path = Path.Combine(directory.FullName, "folders.kpd");
        var settings = new AppSettings { Language = "en", LastOpenedVaultPath = path, EnableDualFileBackup = false };
        Guid entryId;
        try
        {
            using var master = new MasterKey(password);
            await using (var vault = await VaultSession.CreateAsync(path, master, settings, "General", "Recycle bin"))
            {
                var general = (await vault.Storage.LoadAllGroupsAsync(vault.Crypto)).Single(folder => folder.Name == "General");
                var entry = new PwEntry { GroupId = general.Id, Title = "Synthetic entry", Password = "test-only" };
                entryId = entry.Id;
                try { await vault.Storage.SaveEntryAsync(entry, vault.Crypto); }
                finally { entry.ClearSecrets(); }
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var desktop = HeadlessUnitTestSession.StartNew(typeof(HeadlessStartup));
            await desktop.Dispatch(async () =>
            {
                var window = new VaultWindow(settings);
                var groups = window.FindControl<ComboBox>("Groups")!;
                var entries = window.FindControl<ListBox>("Entries")!;
                GroupChoice Folder(string name) => groups.Items.OfType<GroupChoice>().Single(choice => choice.Folder?.Name == name);
                window.Show();
                try
                {
                    window.FindControl<TextBox>("MasterPassword")!.Text = password;
                    await window.OpenVaultAsync();

                    await NameFolderAsync(window, "Archive", false, timeout.Token);
                    await NameFolderAsync(window, "Projects", false, timeout.Token);
                    await NameFolderAsync(window, "Work", true, timeout.Token);
                    Assert.Equal(Folder("Archive").Id, Folder("Work").Folder!.ParentId);
                    await ChooseFolderAsync(window, window.MoveFolderAsync(), "General", timeout.Token);
                    Assert.Equal(Folder("General").Id, Folder("Work").Folder!.ParentId);
                    await ChooseFolderAsync(window, window.MoveFolderAsync(), "Archive", timeout.Token);
                    await window.SelectGroupAsync(Folder("Archive"));
                    await NameFolderAsync(window, "Inbox", false, timeout.Token);
                    await window.ReorderFolderAsync(moveDown: false);
                    Assert.True(groups.Items.IndexOf(Folder("Inbox")) < groups.Items.IndexOf(Folder("Work")));

                    await window.SelectGroupAsync(Folder("General"));
                    var entry = Assert.IsType<EntryRow>(Assert.Single(entries.Items));
                    await window.DuplicateEntryAsync(entry);
                    var copy = entries.Items.OfType<EntryRow>().Single(row => row.Title == "Synthetic entry - Copy");
                    Assert.NotEqual(entry.Id, copy.Id);
                    await ChooseFolderAsync(window, window.MoveEntryAsync(entry, restore: false), "Work", timeout.Token);
                    Assert.Equal(entryId, Assert.IsType<EntryRow>(Assert.Single(entries.Items)).Id);

                    await window.SelectGroupAsync(Folder("Archive"));
                    await ConfirmAsync(window, window.DeleteFolderAsync(), false, timeout.Token);
                    Assert.NotNull(Folder("Work"));
                    await ConfirmAsync(window, window.DeleteFolderAsync(), true, timeout.Token);
                    Assert.DoesNotContain(groups.Items.OfType<GroupChoice>(), choice => choice.Folder?.Name is "Archive" or "Work" or "Inbox");

                    await window.SelectGroupAsync(Folder("Recycle bin"));
                    var recoverable = Assert.IsType<EntryRow>(Assert.Single(entries.Items));
                    Assert.Equal(entryId, recoverable.Id);
                    await ChooseFolderAsync(window, window.MoveEntryAsync(recoverable, restore: true), "General", timeout.Token);
                    var restored = entries.Items.OfType<EntryRow>().Single(row => row.Id == entryId);
                    await window.DeleteEntryAsync(copy);
                    await window.SelectGroupAsync(Folder("Recycle bin"));
                    Assert.Equal(copy.Id, Assert.IsType<EntryRow>(Assert.Single(entries.Items)).Id);
                    await ConfirmAsync(window, window.EmptyRecycleBinAsync(), true, timeout.Token);
                    Assert.Empty(entries.Items);
                    Assert.True(restored.Revision > entry.Revision);
                }
                finally
                {
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Close();
                    await closed.Task.WaitAsync(timeout.Token);
                }
                return true;
            }, timeout.Token);

            await using var reopened = await VaultSession.OpenAsync(path, master, settings, () => Task.FromResult(false));
            Assert.NotNull(reopened);
            var saved = await reopened.Storage.LoadEntryAsync(entryId, reopened.Crypto);
            try
            {
                Assert.Equal("test-only", saved.Password);
                Assert.Equal("General", (await reopened.Storage.LoadAllGroupsAsync(reopened.Crypto)).Single(folder => folder.Id == saved.GroupId).Name);
            }
            finally { saved.ClearSecrets(); }
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task NameFolderAsync(VaultWindow window, string name, bool rename, CancellationToken token)
    {
        var operation = window.OpenFolderEditorAsync(rename);
        var dialog = await FindDialogAsync<FolderDialog>(window, operation, token);
        dialog.FindControl<TextBox>("FolderName")!.Text = name;
        dialog.Accept_Click(null, new RoutedEventArgs());
        await operation;
    }

    [Fact]
    public async Task SharedDesktopRotatesToKeyFileOnlyAndRecoversAnEarlierBackupIntoANewVault()
    {
        const string password = "Test-only-credential-@#€";
        var directory = Directory.CreateTempSubdirectory("SQLitePass-Avalonia-");
        string path = Path.Combine(directory.FullName, "credential.kpd");
        string earlier = Path.Combine(directory.FullName, "earlier.kpd");
        string recovered = Path.Combine(directory.FullName, "recovered.kpd");
        string keyFile = Path.Combine(directory.FullName, "synthetic.key");
        var settings = new AppSettings { Language = "en", LastOpenedVaultPath = path, EnableDualFileBackup = true };
        try
        {
            File.WriteAllBytes(keyFile, Enumerable.Repeat((byte)0x42, 32).ToArray());
            using var initial = new MasterKey(password);
            await using (var vault = await VaultSession.CreateAsync(path, initial, settings, "General", "Recycle bin"))
            {
                var entry = new PwEntry { Title = "Recovery fixture", GroupId = vault.Metadata.RootGroupId!.Value, Password = "sample-data" };
                try { await vault.Storage.SaveEntryAsync(entry, vault.Crypto); }
                finally { entry.ClearSecrets(); }
                File.Copy(vault.Storage.BackupFilePath, earlier + ".backup");
            }
            byte[] originalBackup = File.ReadAllBytes(earlier + ".backup");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await using var desktop = HeadlessUnitTestSession.StartNew(typeof(HeadlessStartup));
            await desktop.Dispatch(async () =>
            {
                var window = new VaultWindow(settings);
                var vaultPath = window.FindControl<TextBox>("VaultPath")!;
                var passwordBox = window.FindControl<TextBox>("MasterPassword")!;
                var keyFileBox = window.FindControl<TextBox>("KeyFilePath")!;
                var status = window.FindControl<TextBlock>("StatusText")!;
                var vaultPanel = window.FindControl<Grid>("VaultPanel")!;
                window.Show();
                try
                {
                    passwordBox.Text = password;
                    await window.OpenVaultAsync();
                    Assert.True(vaultPanel.IsVisible);

                    var rotation = window.ChangeCredentialAsync();
                    var dialog = await FindDialogAsync<CredentialChangeDialog>(window, rotation, timeout.Token);
                    dialog.FindControl<TextBox>("CurrentPassword")!.Text = "incorrect";
                    dialog.FindControl<TextBox>("ReplacementFile")!.Text = keyFile;
                    dialog.FindControl<TextBox>("ReplacementPassword")!.Text = "mismatch";
                    dialog.Save_Click(null, new RoutedEventArgs());
                    Assert.True(dialog.FindControl<TextBlock>("ValidationMessage")!.IsVisible);
                    dialog.FindControl<TextBox>("ReplacementPassword")!.Clear();
                    dialog.Save_Click(null, new RoutedEventArgs());
                    await rotation;
                    Assert.Contains("incorrect", status.Text, StringComparison.OrdinalIgnoreCase);

                    rotation = window.ChangeCredentialAsync();
                    dialog = await FindDialogAsync<CredentialChangeDialog>(window, rotation, timeout.Token);
                    dialog.FindControl<TextBox>("CurrentPassword")!.Text = password;
                    dialog.FindControl<TextBox>("ReplacementFile")!.Text = keyFile;
                    dialog.Save_Click(null, new RoutedEventArgs());
                    await rotation;
                    Assert.Contains("credential was updated", status.Text, StringComparison.OrdinalIgnoreCase);
                    Assert.Empty(dialog.FindControl<TextBox>("CurrentPassword")!.Text ?? "");
                    await window.LockVaultAsync();

                    passwordBox.Text = password;
                    await window.OpenVaultAsync();
                    Assert.False(vaultPanel.IsVisible);
                    keyFileBox.Text = keyFile;
                    await window.OpenVaultAsync();
                    Assert.True(vaultPanel.IsVisible);
                    await window.LockVaultAsync();

                    vaultPath.Text = earlier;
                    passwordBox.Text = "incorrect";
                    await ConfirmAsync(window, window.RecoverBackupAsync(_ => Task.FromResult<string?>(recovered)), true, timeout.Token);
                    Assert.False(File.Exists(recovered));
                    passwordBox.Text = password;
                    await ConfirmAsync(window, window.RecoverBackupAsync(_ => Task.FromResult<string?>(recovered)), true, timeout.Token);
                    Assert.Equal(recovered, vaultPath.Text);
                    Assert.True(File.Exists(recovered));
                    Assert.False(File.Exists(earlier));
                    Assert.Equal(originalBackup, File.ReadAllBytes(earlier + ".backup"));
                    Assert.Empty(Directory.EnumerateDirectories(directory.FullName, ".sqlitepass-recovery-*"));
                    passwordBox.Text = password;
                    await window.OpenVaultAsync();
                    Assert.True(vaultPanel.IsVisible);
                    Assert.Equal("Recovery fixture", Assert.IsType<EntryRow>(Assert.Single(window.FindControl<ListBox>("Entries")!.Items)).Title);
                }
                finally
                {
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.Closed += (_, _) => closed.TrySetResult();
                    window.Close();
                    await closed.Task.WaitAsync(timeout.Token);
                }
                return true;
            }, timeout.Token);
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task ChooseFolderAsync(VaultWindow window, Task operation, string name, CancellationToken token)
    {
        var dialog = await FindDialogAsync<FolderDialog>(window, operation, token);
        var destinations = dialog.FindControl<ComboBox>("FolderDestination")!;
        destinations.SelectedItem = destinations.Items.OfType<GroupChoice>().Single(choice => choice.Folder?.Name == name);
        dialog.Accept_Click(null, new RoutedEventArgs());
        await operation;
    }

    private static async Task ConfirmAsync(VaultWindow window, Task operation, bool accept, CancellationToken token)
    {
        var dialog = await FindDialogAsync<Window>(window, operation, token);
        var button = dialog.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, accept ? "OK" : "Cancel"));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await operation;
    }

    private static async Task<T> FindDialogAsync<T>(VaultWindow owner, Task operation, CancellationToken token) where T : Window
    {
        while (true)
        {
            if (owner.OwnedWindows.OfType<T>().SingleOrDefault() is { } dialog) return dialog;
            if (operation.IsCompleted) throw new InvalidOperationException("The operation ended before opening its dialog.");
            await Task.Delay(10, token);
        }
    }
}
