using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.Desktop;

public sealed partial class SettingsWindow : Window
{
    private readonly Func<string, object[], string> _text;
    private readonly bool _allowWindowsStartup;
    private readonly MachinePolicyState _machinePolicy;
    private bool _pickingFolder;
    private bool _closed;

    public SettingsWindow() : this(new AppSettings(), (key, _) => key, false) { }

    internal SettingsWindow(AppSettings settings, Func<string, object[], string> text, bool allowWindowsStartup,
        MachinePolicyState? machinePolicy = null)
    {
        Settings = settings;
        _text = text;
        _allowWindowsStartup = allowWindowsStartup;
        _machinePolicy = machinePolicy ?? MachinePolicyState.Unmanaged;
        InitializeComponent();
        Title = text("SettingsTitle", []);
        AppearanceHeading.Text = text("SettingsSectionAppearance", []);
        LanguageLabel.Text = text("SettingsLanguage", []);
        var systemLanguage = new ComboBoxItem { Content = text("SystemLanguage", []), Tag = "" };
        LanguageChoice.Items.Add(systemLanguage);
        foreach (string code in TranslationCatalog.Languages)
            LanguageChoice.Items.Add(new ComboBoxItem { Content = CultureInfo.GetCultureInfo(code).NativeName, Tag = code });
        LanguageChoice.SelectedItem = LanguageChoice.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, settings.Language)) ?? systemLanguage;
        SetOption(DarkTheme, "SettingsDarkTheme", settings.DarkMode);
        SetOption(ShowPasswordsOnUnlock, "SettingsShowPasswordDefault", settings.ShowPasswordColumnDefault);
        SetOption(DiagnosticLogging, "SettingsDiagnosticLogging", settings.EnableDiagnosticLogging);

        SecurityHeading.Text = text("SettingsSectionSecurity", []);
        ClipboardLabel.Text = text("SettingsClipboardSeconds", []);
        ClipboardSeconds.Text = settings.ClipboardClearSeconds.ToString(CultureInfo.CurrentCulture);
        IdleLabel.Text = text("SettingsIdleMinutes", []);
        IdleMinutes.Text = _machinePolicy.EffectiveAutoLockMinutes(settings.AutoLockMinutes).ToString(CultureInfo.CurrentCulture);
        SetOption(LockOnMinimize, "SettingsLockOnWindowMinimize", _machinePolicy.LockOnMinimize(settings.AutoLockOnMinimize));
        SetOption(LockOnSession, "SettingsLockOnSession", _machinePolicy.LockOnSessionLock(settings.AutoLockOnWindowsLock));
        LockOnMinimize.IsEnabled = !_machinePolicy.IsValid || _machinePolicy.Options?.ForceLockOnMinimize != true;
        LockOnSession.IsEnabled = !_machinePolicy.IsValid || _machinePolicy.Options?.ForceLockOnSessionLock != true;
        MachinePolicyStatus.IsVisible = _machinePolicy.IsPresent;
        MachinePolicyStatus.Text = _machinePolicy.IsValid ? text("SettingsMachinePolicy", []) : text("MachinePolicyInvalid", []);
        SetOption(ProtectCapture, "SettingsProtectScreenCapture", settings.ProtectScreenCapture);
        ProtectCapture.IsVisible = OperatingSystem.IsWindows();

        TrayHeading.Text = text("SettingsSectionTray", []);
        SetOption(MinimizeToTray, "SettingsMinimizeToTray", settings.MinimizeToTray);
        SetOption(CloseToTray, "SettingsCloseToTray", settings.CloseToTray);
        SetOption(AlwaysOnTop, "SettingsAlwaysOnTop", settings.AlwaysOnTop);
        SetOption(StartWithWindows, "SettingsStartWithWindows", settings.StartWithWindows);
        StartWithWindows.IsVisible = allowWindowsStartup;

        BackupHeading.Text = text("SettingsSectionBackup", []);
        SetOption(AutomaticBackup, "SettingsDualFileBackup", settings.EnableDualFileBackup);
        BackupDescription.Text = text("SettingsDualFileDesc", []);
        BackupFolderLabel.Text = text("SettingsSecondaryDir", []);
        BackupFolder.Text = settings.SecondaryBackupDir;
        BrowseFolder.Content = text("Browse", []);
        Save.Content = text("Save", []);
        Cancel.Content = text("Cancel", []);
        Opened += (_, _) => BrowseFolder.IsEnabled = StorageProvider.CanPickFolder;
        Closed += (_, _) => _closed = true;
    }

    internal AppSettings Settings { get; }

    private void SetOption(CheckBox option, string label, bool value)
    {
        string caption = _text(label, []);
        option.Content = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(option, caption);
        option.IsChecked = value;
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_closed || _pickingFolder) return;
        _pickingFolder = true;
        BrowseFolder.IsEnabled = Save.IsEnabled = false;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = BackupFolderLabel.Text });
            if (_closed || folders.Count == 0) return;
            if (folders[0].TryGetLocalPath() is { } path)
            {
                BackupFolder.Text = path;
                Validation.IsVisible = false;
            }
            else ShowValidation("SettingsBackupDirectoryInvalid", BackupFolder);
        }
        catch (Exception error)
        {
            AppLogger.LogError("Could not open the backup destination picker", error);
            if (!_closed) ShowValidation("SettingsBackupDirectoryInvalid", BackupFolder);
        }
        finally
        {
            _pickingFolder = false;
            if (!_closed) BrowseFolder.IsEnabled = Save.IsEnabled = true;
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_closed || _pickingFolder) return;
        if (!int.TryParse(ClipboardSeconds.Text, out int seconds) || seconds is < 3 or > 300)
        {
            ShowValidation("ClipboardTimeoutInvalid", ClipboardSeconds);
            return;
        }
        if (!int.TryParse(IdleMinutes.Text, out int minutes) || minutes is < 0 or > 60)
        {
            ShowValidation("SettingsIdleInvalid", IdleMinutes);
            return;
        }
        if (_machinePolicy.IsValid && _machinePolicy.Options?.MaxAutoLockMinutes is int maximum && (minutes == 0 || minutes > maximum))
        {
            Validation.Text = _text("SettingsMachinePolicyAutoLockInvalid", [maximum]);
            Validation.IsVisible = true;
            IdleMinutes.Focus();
            return;
        }

        string? backupFolder = BackupFolder.Text?.Trim();
        if (string.IsNullOrEmpty(backupFolder)) backupFolder = null;
        else
        {
            try
            {
                if (!Path.IsPathFullyQualified(backupFolder)) throw new ArgumentException();
                backupFolder = Path.GetFullPath(backupFolder);
            }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
            {
                ShowValidation("SettingsBackupDirectoryInvalid", BackupFolder);
                return;
            }
        }

        Settings.Language = (LanguageChoice.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        Settings.DarkMode = DarkTheme.IsChecked == true;
        Settings.ShowPasswordColumnDefault = ShowPasswordsOnUnlock.IsChecked == true;
        Settings.EnableDiagnosticLogging = DiagnosticLogging.IsChecked == true;
        Settings.ClipboardClearSeconds = seconds;
        Settings.AutoLockMinutes = minutes;
        if (_machinePolicy.Options?.ForceLockOnMinimize != true)
            Settings.AutoLockOnMinimize = LockOnMinimize.IsChecked == true;
        if (_machinePolicy.Options?.ForceLockOnSessionLock != true)
            Settings.AutoLockOnWindowsLock = LockOnSession.IsChecked == true;
        if (OperatingSystem.IsWindows()) Settings.ProtectScreenCapture = ProtectCapture.IsChecked == true;
        Settings.MinimizeToTray = MinimizeToTray.IsChecked == true;
        Settings.CloseToTray = CloseToTray.IsChecked == true;
        Settings.AlwaysOnTop = AlwaysOnTop.IsChecked == true;
        if (_allowWindowsStartup) Settings.StartWithWindows = StartWithWindows.IsChecked == true;
        Settings.EnableDualFileBackup = AutomaticBackup.IsChecked == true;
        Settings.SecondaryBackupDir = backupFolder;
        Close(true);
    }

    private void ShowValidation(string message, Control target)
    {
        Validation.Text = _text(message, []);
        Validation.IsVisible = true;
        target.Focus();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
