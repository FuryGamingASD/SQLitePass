using System.Globalization;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Styling;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;
using Velopack.Locators;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        bool allowWindowsStartup = OperatingSystem.IsWindows() && _desktopSession != null &&
            string.Equals(VelopackLocator.Current.AppId, "SQLitePass", StringComparison.Ordinal);
        await OpenSettingsAsync(AppSettingsManager.Load, settings => AppSettingsManager.Save(settings, allowWindowsStartup), allowWindowsStartup);
    }

    internal Task OpenSettingsAsync(Func<AppSettings> load, Func<AppSettings, string?> save, bool allowWindowsStartup = false) =>
        RunOperationAsync(async token =>
        {
            var draft = await Task.Run(load, token);
            token.ThrowIfCancellationRequested();
            var dialog = new SettingsWindow(draft, Text, allowWindowsStartup);
            if (!await ShowOwnedAsync(dialog)) return;
            token.ThrowIfCancellationRequested();

            string? startupWarning;
            try { startupWarning = await Task.Run(() => save(dialog.Settings), token); }
            catch (SettingsConflictException)
            {
                StatusText.Text = Text("SettingsConflict");
                return;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                AppLogger.LogError("Could not save application preferences", error);
                StatusText.Text = Text("SettingsSaveFailed");
                return;
            }

            AppSettings saved = dialog.Settings;
            AppSettings previous = _settings;
            _settings = saved;
            Topmost = saved.AlwaysOnTop;
            if (Application.Current is { } app) app.RequestedThemeVariant = saved.DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
            AppLogger.Configure(saved.EnableDiagnosticLogging);
            if (previous.ProtectScreenCapture != saved.ProtectScreenCapture)
                _desktopSession?.Protect(this, saved.ProtectScreenCapture);
            _clipboard?.ShortenTimeout(TimeSpan.FromSeconds(saved.ClipboardClearSeconds));
            ResetIdleTime();

            if (_session is { } current)
                await current.Storage.ConfigureBackupAsync(saved.EnableDualFileBackup, saved.SecondaryBackupDir);
            if (token.IsCancellationRequested) return;

            string language = TranslationCatalog.ResolveLanguage(saved.Language, CultureInfo.CurrentUICulture);
            string message = Text(language == _language ? "SettingsSaved" : "SettingsRestartLanguage");
            if (startupWarning != null) message += " · " + Text("StartupRegistrationWarning");
            StatusText.Text = message;
        });
}
