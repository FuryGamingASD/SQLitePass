using System;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Win32;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Dialogs
{
    public partial class ChangeMasterPasswordDialog : Window
    {
        private readonly CryptoEngine _currentCrypto;
        private readonly VaultMetadata _metadata;
        private byte[]? _newKeyFileBytes = null;

        public string NewPassword { get; private set; } = string.Empty;
        public CryptoEngine? NewCrypto { get; private set; }
        public string NewSaltBase64 { get; private set; } = string.Empty;
        public bool EncryptUsernameUrl => ChkEncryptUserUrl.IsChecked == true;

        public ChangeMasterPasswordDialog(CryptoEngine currentCrypto, VaultMetadata metadata)
        {
            InitializeComponent();
            _currentCrypto = currentCrypto ?? throw new ArgumentNullException(nameof(currentCrypto));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

            var settings = AppSettingsManager.Load();
            SQLitePass.UI.Services.WindowSecurityService.ApplyProtection(this, settings.ProtectScreenCapture);

            ChkEncryptUserUrl.IsChecked = _metadata.EncryptUsernameUrl;
            Closing += (s, e) =>
            {
                if (DialogResult != true && _newKeyFileBytes != null)
                {
                    CryptographicOperations.ZeroMemory(_newKeyFileBytes);
                    _newKeyFileBytes = null;
                }
            };

            TxtCurrentPassword.Focus();
        }

        private void TxtNewPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            string pwd = TxtNewPassword.Password;
            double entropy = PasswordGenerator.CalculateEntropy(pwd);
            PbQuality.Value = Math.Min(entropy, 128);
            TxtQualityScore.Text = $"{entropy:F1} bits";
        }

        private void ChkUseKeyFile_Changed(object sender, RoutedEventArgs e)
        {
            GridKeyFile.IsEnabled = ChkUseKeyFile.IsChecked == true;
            if (ChkUseKeyFile.IsChecked != true)
            {
                if (_newKeyFileBytes != null)
                {
                    CryptographicOperations.ZeroMemory(_newKeyFileBytes);
                    _newKeyFileBytes = null;
                }
                TxtKeyFilePath.Text = string.Empty;
            }
        }

        private void BtnBrowseKeyFile_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Seleziona Nuovo File Chiave",
                Filter = "File Chiave (*.key;*.bin)|*.key;*.bin|Tutti i file (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                TxtKeyFilePath.Text = ofd.FileName;
                _newKeyFileBytes = File.ReadAllBytes(ofd.FileName);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string currentPwd = TxtCurrentPassword.Password;
            string newPwd = TxtNewPassword.Password;
            string confirmPwd = TxtConfirmPassword.Password;

            // 1. Verify current password
            byte[] salt = Convert.FromBase64String(_metadata.SaltBase64);
            try
            {
                using var verifyMk = new MasterKey(currentPwd, null);
                byte[] derived = CryptoEngine.DeriveKeyArgon2(verifyMk, salt, _metadata.KdfIterations, _metadata.KdfMemoryKb, _metadata.KdfParallelism);
                using var verifyCrypto = new CryptoEngine(derived);

                if (!verifyCrypto.VerifyCanaryToken(_metadata.VerificationCanary))
                {
                    MessageBox.Show("La master password attuale inserita non è corretta.", "Autenticazione Fallita", MessageBoxButton.OK, MessageBoxImage.Error);
                    TxtCurrentPassword.SelectAll();
                    TxtCurrentPassword.Focus();
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Verifica password fallita: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 2. Validate new password
            if (string.IsNullOrEmpty(newPwd) && (_newKeyFileBytes == null || _newKeyFileBytes.Length == 0))
            {
                MessageBox.Show("Inserisci una nuova Master Password o seleziona un File Chiave.", "Password Richiesta", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtNewPassword.Focus();
                return;
            }

            if (newPwd != confirmPwd)
            {
                MessageBox.Show("La nuova password e la password di conferma non coincidono.", "Errore di Conferma", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtConfirmPassword.SelectAll();
                TxtConfirmPassword.Focus();
                return;
            }

            NewPassword = newPwd;

            // 3. Derive new master key
            try
            {
                byte[] newSalt = new byte[32];
                RandomNumberGenerator.Fill(newSalt);
                using var newMk = new MasterKey(newPwd, _newKeyFileBytes);
                byte[] newDerivedKey = CryptoEngine.DeriveKeyArgon2(newMk, newSalt, _metadata.KdfIterations, _metadata.KdfMemoryKb, _metadata.KdfParallelism);

                NewCrypto = new CryptoEngine(newDerivedKey);
                NewSaltBase64 = Convert.ToBase64String(newSalt);

                if (_newKeyFileBytes != null)
                {
                    CryptographicOperations.ZeroMemory(_newKeyFileBytes);
                    _newKeyFileBytes = null;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Derivazione nuova chiave fallita: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
