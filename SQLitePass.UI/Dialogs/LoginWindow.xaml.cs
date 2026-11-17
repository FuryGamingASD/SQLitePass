using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Dialogs;

public partial class LoginWindow : Window
{
    private bool _isMasterPwdRevealed;
    private bool _busy;
    private volatile bool _sessionLocked;
    private int _credentialGeneration;
    public SqliteStorageProvider? StorageProvider { get; private set; }
    public CryptoEngine? AuthenticatedCrypto { get; private set; }
    public VaultMetadata? Metadata { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        var settings = AppSettingsManager.Load();
        WindowSecurityService.ApplyProtection(this, settings.ProtectScreenCapture);
        CmbRecentVaults.ItemsSource = settings.RecentVaults;
        CmbRecentVaults.Text = settings.LastOpenedVaultPath ?? settings.RecentVaults.FirstOrDefault() ?? "";
        UpdateBackupBadge();
        Closing += (_, e) => e.Cancel = _busy;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        Closed += (_, _) => { Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch; TxtMasterPassword.Clear(); TxtMasterPasswordPlain.Clear(); };
        Loaded += (_, _) => TxtMasterPassword.Focus();
    }
    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock)
        {
            _sessionLocked = true; System.Threading.Interlocked.Increment(ref _credentialGeneration);
            Dispatcher.BeginInvoke(() =>
            {
                TxtMasterPassword.Clear(); TxtMasterPasswordPlain.Clear();
                _isMasterPwdRevealed = false; TxtMasterPassword.Visibility = Visibility.Visible; TxtMasterPasswordPlain.Visibility = Visibility.Collapsed;
                foreach (Window dialog in OwnedWindows.Cast<Window>().ToArray())
                { SensitiveWindowService.ClearControls(dialog); dialog.Close(); }
            });
        }
        else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) _sessionLocked = false;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy; if (Content is UIElement content) content.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : null;
    }
    private void UpdateBackupBadge() => PanelBackupStatus.Visibility = File.Exists(CmbRecentVaults.Text.Trim() + ".backup") ? Visibility.Visible : Visibility.Collapsed;
    private void CmbRecentVaults_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (PanelBackupStatus != null) Dispatcher.BeginInvoke(UpdateBackupBadge); }
    private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        try { var settings = AppSettingsManager.Update(s => s.DarkMode = !s.DarkMode); ThemeManager.ApplyTheme(settings.DarkMode); }
        catch (IOException ex) { MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("ThemeSaveFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private string GetMasterPassword() => _isMasterPwdRevealed ? TxtMasterPasswordPlain.Text : TxtMasterPassword.Password;
    private void BtnToggleMasterPwd_Click(object sender, RoutedEventArgs e)
    {
        _isMasterPwdRevealed = !_isMasterPwdRevealed;
        if (_isMasterPwdRevealed) { TxtMasterPasswordPlain.Text = TxtMasterPassword.Password; TxtMasterPassword.Clear(); }
        else { TxtMasterPassword.Password = TxtMasterPasswordPlain.Text; TxtMasterPasswordPlain.Clear(); }
        TxtMasterPassword.Visibility = _isMasterPwdRevealed ? Visibility.Collapsed : Visibility.Visible;
        TxtMasterPasswordPlain.Visibility = _isMasterPwdRevealed ? Visibility.Visible : Visibility.Collapsed;
        BtnToggleMasterPwd.Content = new Controls.AppIcon { Kind = _isMasterPwdRevealed ? Controls.AppIconKind.EyeOff : Controls.AppIconKind.Eye };
        BtnToggleMasterPwd.SetResourceReference(ToolTipProperty, _isMasterPwdRevealed ? "HidePassword" : "ShowPassword");
        BtnToggleMasterPwd.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty, _isMasterPwdRevealed ? "HidePassword" : "ShowPassword");
    }
    private void ChkUseKeyFile_Changed(object sender, RoutedEventArgs e)
    {
        if (GridKeyFile == null) return;
        GridKeyFile.IsEnabled = ChkUseKeyFile.IsChecked == true;
        if (!GridKeyFile.IsEnabled) TxtKeyFilePath.Clear();
    }
    private void BtnBrowseKeyFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = LocalizationManager.GetString("KeyFileFilter") };
        if (dialog.ShowDialog(this) == true) TxtKeyFilePath.Text = dialog.FileName;
    }
    private void BtnBrowseVault_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "SQLitePass (*.kpd;*.db)|*.kpd;*.db" };
        if (dialog.ShowDialog(this) == true) { CmbRecentVaults.Text = dialog.FileName; UpdateBackupBadge(); }
    }
    private Task<MasterKey> ReadMasterKeyAsync()
    {
        string password = GetMasterPassword();
        bool useFile = ChkUseKeyFile.IsChecked == true;
        string path = TxtKeyFilePath.Text;
        if (useFile && !File.Exists(path)) throw new UserInputException("SelectKeyFile");
        return Task.Run(() => useFile ? MasterKey.FromFile(password, path) : new MasterKey(password));
    }
    private SqliteStorageProvider Provider(string path)
    {
        var settings = AppSettingsManager.Load();
        return new SqliteStorageProvider(path, settings.EnableDualFileBackup, settings.SecondaryBackupDir);
    }
    private void RequireCurrentCredential(int generation)
    {
        if (_sessionLocked || generation != _credentialGeneration)
            throw new OperationCanceledException(LocalizationManager.GetString("WindowsLockCancelled"));
    }
    private static void RememberVault(string path)
    {
        try { AppSettingsManager.AddRecentVault(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLogger.LogWarning("Il vault è aperto, ma non è stato possibile aggiornare l'elenco dei file recenti."); }
    }
    private async void BtnRestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        int generation = _credentialGeneration;
        if (_busy) return;
        SetBusy(true);
        try
        {
            using var master = await ReadMasterKeyAsync();
            RequireCurrentCredential(generation);
            if (MessageBox.Show(this, LocalizationManager.GetString("RestoreConfirm"), LocalizationManager.GetString("RestoreTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            SetBusy(true); using var provider = Provider(CmbRecentVaults.Text.Trim());
            if (_sessionLocked || generation != _credentialGeneration) throw new OperationCanceledException(LocalizationManager.GetString("WindowsLockCancelled"));
            string preserved = await provider.RestoreBackupAsync(master);
            MessageBox.Show(this, LocalizationManager.GetString("RestoreComplete", preserved), LocalizationManager.GetString("RestoreTitle"));
        }
        catch (Exception ex) { MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("RestoreFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { SetBusy(false); }
    }
    private async void BtnOpenVault_Click(object sender, RoutedEventArgs e)
    {
        int generation = _credentialGeneration;
        if (_busy) return;
        SetBusy(true);
        SqliteStorageProvider? provider = null; CryptoEngine? crypto = null;
        try
        {
            string path = CmbRecentVaults.Text.Trim();
            if (!File.Exists(path)) throw new UserInputException("SelectExistingVault");
            using var master = await ReadMasterKeyAsync(); SetBusy(true); provider = Provider(path);
            RequireCurrentCredential(generation);
            if (!await provider.CheckDatabaseIntegrityAsync()) throw new InvalidDataException(LocalizationManager.GetString("VaultNotIntact"));
            var metadata = await provider.LoadVaultMetadataAsync() ?? throw new InvalidDataException(LocalizationManager.GetString("VaultIncomplete"));
            if (metadata.FormatVersion == 1 && MessageBox.Show(this,
                LocalizationManager.GetString("UpgradeFormatConfirm"),
                LocalizationManager.GetString("UpgradeFormatTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            crypto = await Task.Run(() => CryptoEngine.FromMasterKey(master, Convert.FromBase64String(metadata.SaltBase64), metadata.KdfIterations, metadata.KdfMemoryKb, metadata.KdfParallelism));
            RequireCurrentCredential(generation);
            await Task.Run(() => provider.AuthenticateAsync(crypto));
            if (_sessionLocked || generation != _credentialGeneration) throw new OperationCanceledException(LocalizationManager.GetString("WindowsLockCancelled"));
            Metadata = await provider.LoadVaultMetadataAsync();
            if (_sessionLocked || generation != _credentialGeneration) throw new OperationCanceledException(LocalizationManager.GetString("WindowsLockCancelled"));
            StorageProvider = provider; AuthenticatedCrypto = crypto; provider = null; crypto = null;
            RememberVault(path); SetBusy(false); DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("UnlockFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { crypto?.Dispose(); provider?.Dispose(); SetBusy(false); }
    }
    private async void BtnCreateVault_Click(object sender, RoutedEventArgs e)
    {
        int generation = _credentialGeneration;
        if (_busy) return;
        SetBusy(true);
        CryptoEngine? crypto = null; SqliteStorageProvider? provider = null;
        try
        {
            using var master = await ReadMasterKeyAsync();
            RequireCurrentCredential(generation);
            string password = GetMasterPassword();
            if (!string.IsNullOrEmpty(password) && new PasswordConfirmationDialog(password) { Owner = this }.ShowDialog() != true) return;
            var dialog = new SaveFileDialog { Filter = "SQLitePass (*.kpd)|*.kpd", FileName = "PasswordVault.kpd", DefaultExt = ".kpd" };
            if (dialog.ShowDialog(this) != true) return;
            if (File.Exists(dialog.FileName)) throw new UserInputException("VaultCreationNoOverwrite");
            SetBusy(true); byte[] salt = RandomNumberGenerator.GetBytes(16);
            crypto = await Task.Run(() => CryptoEngine.FromMasterKey(master, salt));
            var metadata = new VaultMetadata
            {
                VaultName = Path.GetFileNameWithoutExtension(dialog.FileName), SaltBase64 = Convert.ToBase64String(salt),
                VerificationCanary = crypto.GenerateCanaryToken(), UsesKeyFile = ChkUseKeyFile.IsChecked == true
            };
            var root = new PwGroup { Name = metadata.VaultName, IsRoot = true };
            var general = new PwGroup { ParentId = root.Id, Name = LocalizationManager.GetString("DefaultGeneralGroup") };
            var trash = new PwGroup { ParentId = root.Id, Name = LocalizationManager.GetString("RecycleBin"), IconIndex = 43, IsRecycleBin = true };
            var settings = AppSettingsManager.Load();
            if (_sessionLocked || generation != _credentialGeneration) throw new OperationCanceledException(LocalizationManager.GetString("WindowsLockCancelled"));
            provider = await SqliteStorageProvider.CreateVaultAsync(dialog.FileName, metadata, crypto, [root, general, trash], [], settings.EnableDualFileBackup, settings.SecondaryBackupDir);
            RequireCurrentCredential(generation);
            Metadata = await provider.LoadVaultMetadataAsync();
            RequireCurrentCredential(generation);
            StorageProvider = provider; AuthenticatedCrypto = crypto; provider = null; crypto = null;
            RememberVault(dialog.FileName); SetBusy(false); DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("CreateVaultFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { crypto?.Dispose(); provider?.Dispose(); SetBusy(false); }
    }
    private void BtnExit_Click(object sender, RoutedEventArgs e) { if (!_busy) DialogResult = false; }
}
