using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Services;
using SQLitePass.Desktop;

namespace SQLitePass.Tests;

public sealed partial class AvaloniaVaultTests
{
    [Fact]
    public async Task SharedSettingsValidateAndApplyPreferencesToAnOpenVault()
    {
        const string password = "Synthetic-settings-passphrase!";
        var directory = Directory.CreateTempSubdirectory("SQLitePass-Avalonia-settings-");
        string vaultPath = Path.Combine(directory.FullName, "fixture.kpd");
        string secondaryFolder = Path.Combine(directory.FullName, "secondary");
        string concurrentPath = Path.Combine(directory.FullName, "other.kpd");
        var store = new AppSettingsStore(Path.Combine(directory.FullName, "profile"));
        try
        {
            store.Save(new AppSettings
            {
                Language = "en", LastOpenedVaultPath = vaultPath, StartWithWindows = true,
                ShowPasswordColumnDefault = true, EnableDualFileBackup = true
            });
            using var master = new MasterKey(password);
            await using (var seed = await VaultSession.CreateAsync(vaultPath, master, store.Load(), "General", "Recycle bin")) { }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            await using var desktop = HeadlessUnitTestSession.StartNew(typeof(HeadlessStartup));
            await desktop.Dispatch(async () =>
            {
                var window = new VaultWindow(store.Load());
                window.Show();
                try
                {
                    var cancelled = window.OpenSettingsAsync(store.Load, Persist);
                    var dialog = await FindDialogAsync<SettingsWindow>(window, cancelled, timeout.Token);
                    dialog.FindControl<TextBox>("ClipboardSeconds")!.Text = "2";
                    Click(dialog, "Save");
                    Assert.True(dialog.IsVisible);
                    Assert.True(dialog.FindControl<TextBlock>("Validation")!.IsVisible);
                    Click(dialog, "Cancel");
                    await cancelled;
                    Assert.Equal(12, store.Load().ClipboardClearSeconds);

                    window.FindControl<TextBox>("MasterPassword")!.Text = password;
                    await window.OpenVaultAsync();
                    Assert.True(window.FindControl<Grid>("VaultPanel")!.IsVisible);
                    var saving = window.OpenSettingsAsync(store.Load, Persist);
                    dialog = await FindDialogAsync<SettingsWindow>(window, saving, timeout.Token);
                    dialog.FindControl<TextBox>("ClipboardSeconds")!.Text = "19";
                    dialog.FindControl<TextBox>("IdleMinutes")!.Text = "17";
                    dialog.FindControl<CheckBox>("AlwaysOnTop")!.IsChecked = true;
                    dialog.FindControl<CheckBox>("DarkTheme")!.IsChecked = false;
                    dialog.FindControl<CheckBox>("AutomaticBackup")!.IsChecked = false;
                    dialog.FindControl<TextBox>("BackupFolder")!.Text = secondaryFolder;
                    store.Update(settings => settings.LastOpenedVaultPath = concurrentPath);
                    Click(dialog, "Save");
                    await saving;

                    Assert.True(window.Topmost);
                    Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
                    Assert.Equal("Automatic backup disabled", window.FindControl<TextBlock>("BackupStatus")!.Text);
                    var saved = store.Load();
                    Assert.Equal(19, saved.ClipboardClearSeconds);
                    Assert.Equal(17, saved.AutoLockMinutes);
                    Assert.Equal(secondaryFolder, saved.SecondaryBackupDir);
                    Assert.Equal(concurrentPath, saved.LastOpenedVaultPath);
                    Assert.True(saved.ShowPasswordColumnDefault);
                    Assert.True(saved.StartWithWindows);
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

        string? Persist(AppSettings settings) { store.Save(settings); return null; }
        static void Click(Window dialog, string button) => dialog.FindControl<Button>(button)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
