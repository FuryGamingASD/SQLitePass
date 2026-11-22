using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.Desktop;

namespace SQLitePass.Tests;

public sealed partial class AvaloniaVaultTests
{
    [Fact]
    public async Task SharedEditorRecoversAnEncryptedRevisionWithAttachmentAndCustomFields()
    {
        const string password = "Synthetic-entry-details!";
        var directory = Directory.CreateTempSubdirectory("SQLitePass-Avalonia-");
        string path = Path.Combine(directory.FullName, "details.kpd");
        string attachmentPath = Path.Combine(directory.FullName, "fixture.txt");
        string export = Path.Combine(directory.FullName, "attachment-export.txt");
        var settings = new AppSettings { Language = "en", LastOpenedVaultPath = path, EnableDualFileBackup = false };
        DateTime expiry = new(2030, 2, 14);
        Guid entryId;
        try
        {
            File.WriteAllText(attachmentPath, "synthetic draft attachment");
            using var master = new MasterKey(password);
            await using (var vault = await VaultSession.CreateAsync(path, master, settings, "General", "Recycle bin"))
            {
                var entry = new PwEntry
                {
                    Title = "Earlier entry", GroupId = vault.Metadata.RootGroupId!.Value, Password = "earlier-secret", ExpiryDate = expiry
                };
                entryId = entry.Id;
                entry.CustomFields.Add(new PwCustomField { EntryId = entry.Id, Key = "Recovery hint", Value = "history-only", IsProtected = true });
                entry.Attachments.Add(new PwAttachment { EntryId = entry.Id, FileName = "earlier.bin", FileSize = 3, Data = [4, 5, 6] });
                entry.History.Add(entry.CreateHistorySnapshot());
                entry.Title = "Current entry";
                entry.ExpiryDate = null;
                try { await vault.Storage.SaveEntryAsync(entry, vault.Crypto); }
                finally { entry.ClearSecrets(); }
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            await using var desktop = HeadlessUnitTestSession.StartNew(typeof(HeadlessStartup));
            await desktop.Dispatch(async () =>
            {
                var window = new VaultWindow(settings);
                window.Show();
                try
                {
                    window.FindControl<TextBox>("MasterPassword")!.Text = password;
                    await window.OpenVaultAsync();
                    var row = Assert.IsType<EntryRow>(Assert.Single(window.FindControl<ListBox>("Entries")!.Items));
                    var editing = window.OpenEditorAsync(row);
                    var editor = await FindDialogAsync<EntryEditor>(window, editing, timeout.Token);
                    var passwordField = editor.FindControl<TextBox>("Password")!;
                    editor.FindControl<Button>("GeneratePassword")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var generator = await FindDialogAsync<PasswordGeneratorDialog>(editor, editing, timeout.Token);
                    string? generated = generator.FindControl<TextBlock>("Result")!.Text;
                    Assert.NotNull(generated);
                    Assert.Equal(20, generated.Length);
                    Assert.Contains(generated, char.IsUpper);
                    Assert.Contains(generated, char.IsLower);
                    Assert.Contains(generated, char.IsDigit);
                    generator.FindControl<Button>("UsePassword")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Yield();
                    Assert.Equal(generated, passwordField.Text);
                    Assert.Empty(generator.FindControl<TextBlock>("Result")!.Text!);
                    editor.RevealPassword_Click(null, new RoutedEventArgs());
                    Assert.True(passwordField.RevealPassword);
                    var copyEvent = new RoutedEventArgs(TextBox.CopyingToClipboardEvent);
                    passwordField.RaiseEvent(copyEvent);
                    Assert.True(copyEvent.Handled);
                    editor.FindControl<TabControl>("Sections")!.SelectedIndex = 2;
                    Assert.False(passwordField.RevealPassword);

                    await editor.AddAttachmentAsync(_ => Task.FromResult<string?>(attachmentPath));
                    var attachments = editor.FindControl<ListBox>("Attachments")!;
                    var draftAttachment = attachments.Items.OfType<PwAttachment>().Single(item => item.FileName == "fixture.txt");
                    await editor.ExportAttachmentAsync(draftAttachment, (_, _) => Task.FromResult<string?>(path));
                    Assert.Contains("failed", editor.FindControl<TextBlock>("ValidationMessage")!.Text, StringComparison.OrdinalIgnoreCase);
                    await editor.ExportAttachmentAsync(draftAttachment, (_, _) => Task.FromResult<string?>(export));
                    Assert.Equal(await File.ReadAllBytesAsync(attachmentPath, timeout.Token), await File.ReadAllBytesAsync(export, timeout.Token));
                    editor.RemoveSelectedAttachment();
                    Assert.All(draftAttachment.Data, value => Assert.Equal(0, value));

                    editor.FindControl<ListBox>("History")!.SelectedIndex = 0;
                    await ConfirmAsync(editor, editor.RestoreSelectedHistoryAsync(), true, timeout.Token);
                    Assert.Equal("Earlier entry", editor.FindControl<TextBox>("EntryTitle")!.Text);
                    Assert.Equal(expiry, editor.FindControl<DatePicker>("Expiry")!.SelectedDate!.Value.Date);
                    Assert.Equal("earlier.bin", Assert.IsType<PwAttachment>(Assert.Single(attachments.Items)).FileName);
                    editor.FindControl<TextBox>("FieldName")!.Text = "New field";
                    editor.FindControl<TextBox>("FieldValue")!.Text = "saved-inline";
                    var sequence = editor.FindControl<TextBox>("Sequence")!;
                    sequence.Text = "{UNSUPPORTED}";
                    editor.Save_Click(null, new RoutedEventArgs());
                    Assert.True(editor.IsVisible);
                    sequence.Text = "{S:New field}{TAB}{PASSWORD}";
                    editor.Save_Click(null, new RoutedEventArgs());
                    await editing;
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
                Assert.Equal("Earlier entry", saved.Title);
                Assert.Equal(expiry, saved.ExpiryDate);
                Assert.Equal("history-only", saved.CustomFields.Single(field => field.Key == "Recovery hint").Value);
                Assert.Equal("saved-inline", saved.CustomFields.Single(field => field.Key == "New field").Value);
                Assert.Equal(new byte[] { 4, 5, 6 }, Assert.Single(saved.Attachments).Data);
                Assert.Equal(2, saved.History.Count);
            }
            finally { saved.ClearSecrets(); }
        }
        finally { directory.Delete(recursive: true); }
    }
}
