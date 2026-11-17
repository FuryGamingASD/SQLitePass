using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Win32;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Dialogs;

public partial class ChangeMasterPasswordDialog : Window
{
    private readonly VaultMetadata _metadata;
    private bool _busy;
    private bool _cancelled;
    public CryptoEngine? NewCrypto { get; private set; }
    public string NewSaltBase64 { get; private set; } = "";
    public bool EncryptUsernameUrl => true;
    public bool UsesKeyFile => ChkUseKeyFile.IsChecked == true;
    public void CancelForLock()
    {
        _cancelled = true; _busy = false;
        TxtCurrentPassword.Clear(); TxtNewPassword.Clear(); TxtConfirmPassword.Clear();
        NewCrypto?.Dispose(); DialogResult = false;
    }

    public ChangeMasterPasswordDialog(CryptoEngine currentCrypto, VaultMetadata metadata)
    {
        InitializeComponent(); _metadata = metadata;
        if (currentCrypto.IsDisposed) throw new InvalidOperationException(LocalizationManager.GetString("VaultLocked"));
        WindowSecurityService.ApplyProtection(this, AppSettingsManager.Load().ProtectScreenCapture);
        Closing += (_, e) => e.Cancel = _busy;
        Closed += (_, _) => { TxtCurrentPassword.Clear(); TxtNewPassword.Clear(); TxtConfirmPassword.Clear(); if (DialogResult != true) NewCrypto?.Dispose(); };
    }
    private void TxtNewPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (PbQuality == null) return;
        using var secure = TxtNewPassword.SecurePassword;
        if (secure.Length > UiText.MaximumScoredPasswordLength)
        {
            PbQuality.Visibility = Visibility.Collapsed; TxtQualityScore.Text = LocalizationManager.GetString("QualityNotEstimated"); return;
        }
        PbQuality.Visibility = Visibility.Visible;
        double estimate = PasswordGenerator.CalculateEntropy(TxtNewPassword.Password);
        PbQuality.Value = Math.Min(estimate, 100); TxtQualityScore.Text = LocalizationManager.GetString("QualityScore", estimate);
    }
    private void ChkUseKeyFile_Changed(object sender, RoutedEventArgs e)
    {
        if (GridKeyFile == null) return;
        GridKeyFile.IsEnabled = ChkUseKeyFile.IsChecked == true;
        if (!GridKeyFile.IsEnabled) TxtKeyFilePath.Clear();
    }
    private static string? Browse(Window owner)
    {
        var dialog = new OpenFileDialog { Filter = LocalizationManager.GetString("KeyFileFilter") };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
    private void BtnBrowseCurrentKeyFile_Click(object sender, RoutedEventArgs e) { var path = Browse(this); if (path != null) TxtCurrentKeyFile.Text = path; }
    private void BtnBrowseKeyFile_Click(object sender, RoutedEventArgs e) { var path = Browse(this); if (path != null) TxtKeyFilePath.Text = path; }
    private static MasterKey ReadKey(string password, string? path, bool required)
    {
        if (required || !string.IsNullOrEmpty(path))
        {
            if (!File.Exists(path)) throw new UserInputException("SelectKeyFile");
            return MasterKey.FromFile(password, path!);
        }
        return new MasterKey(password);
    }
    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            if (TxtNewPassword.Password != TxtConfirmPassword.Password) throw new UserInputException("PasswordMismatch");
            string currentPassword = TxtCurrentPassword.Password, currentPath = TxtCurrentKeyFile.Text;
            string newPassword = TxtNewPassword.Password, newPath = TxtKeyFilePath.Text;
            bool useFile = UsesKeyFile;
            _busy = true; ((UIElement)Content).IsEnabled = false;
            using var current = await Task.Run(() => ReadKey(currentPassword, currentPath, _metadata.UsesKeyFile));
            using var replacement = await Task.Run(() => ReadKey(newPassword, useFile ? newPath : null, useFile));
            using var verify = await Task.Run(() => CryptoEngine.FromMasterKey(current, Convert.FromBase64String(_metadata.SaltBase64), _metadata.KdfIterations, _metadata.KdfMemoryKb, _metadata.KdfParallelism));
            if (_cancelled) return;
            if (!verify.VerifyCanaryToken(_metadata.VerificationCanary)) throw new UserInputException("CurrentCredentialIncorrect");
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            NewCrypto = await Task.Run(() => CryptoEngine.FromMasterKey(replacement, salt, _metadata.KdfIterations, _metadata.KdfMemoryKb, _metadata.KdfParallelism));
            if (_cancelled) { NewCrypto.Dispose(); return; }
            NewSaltBase64 = Convert.ToBase64String(salt); _busy = false; DialogResult = true;
        }
        catch (Exception ex) { if (!_cancelled) MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("MasterChangeTitle"), MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _busy = false; ((UIElement)Content).IsEnabled = true; }
    }
    private void BtnCancel_Click(object sender, RoutedEventArgs e) { if (!_busy) DialogResult = false; }
}
