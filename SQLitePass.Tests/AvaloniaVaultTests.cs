using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
                    new PwEntry { GroupId = group.Id, Title = "Synthetic portal", UserName = "portal-user", Password = "sample-portal" }
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
                    window.FindControl<TextBox>("SearchBox")!.Text = "portal";
                    await window.SearchAsync();
                    var row = Assert.IsType<EntryRow>(Assert.Single(entryList.Items));
                    Assert.Equal("Synthetic portal", row.Title);
                    editedEntryId = row.Id;

                    var editing = window.OpenEditorAsync(row);
                    var editor = await FindEditorAsync(window, editing, timeout.Token);
                    editor.FindControl<TextBox>("UserName")!.Text = "edited-portal-user";
                    editor.Save_Click(null, new Avalonia.Interactivity.RoutedEventArgs());
                    await editing;
                    Assert.Equal("edited-portal-user", Assert.IsType<EntryRow>(Assert.Single(entryList.Items)).UserName);
                    Assert.True(string.IsNullOrEmpty(editor.FindControl<TextBox>("Password")!.Text));

                    row = Assert.IsType<EntryRow>(Assert.Single(entryList.Items));
                    editing = window.OpenEditorAsync(row);
                    editor = await FindEditorAsync(window, editing, timeout.Token);
                    editor.FindControl<TextBox>("UserName")!.Text = "discarded-user";

                    await window.LockVaultAsync();
                    await editing;
                    Assert.False(vaultPanel.IsVisible);
                    Assert.False(editor.IsVisible);
                    Assert.Empty(entryList.Items);
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

    private static async Task<EntryEditor> FindEditorAsync(VaultWindow owner, Task operation, CancellationToken token)
    {
        while (true)
        {
            if (owner.OwnedWindows.OfType<EntryEditor>().SingleOrDefault() is { } editor) return editor;
            if (operation.IsCompleted) throw new InvalidOperationException("The editor operation ended before opening.");
            await Task.Delay(10, token);
        }
    }
}
