using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Dialogs
{
    public partial class LoginWindow : Window
    {
        private bool _isMasterPwdRevealed = false;
        private byte[]? _keyFileBytes = null;

        public SqliteStorageProvider? StorageProvider { get; private set; }
        public CryptoEngine? AuthenticatedCrypto { get; private set; }
        public VaultMetadata? Metadata { get; private set; }

        public LoginWindow()
        {
            InitializeComponent();
            var settings = AppSettingsManager.Load();
            SQLitePass.UI.Services.WindowSecurityService.ApplyProtection(this, settings.ProtectScreenCapture);
            LoadRecentVaults();
            Closing += LoginWindow_Closing;
        }

        private void LoginWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_keyFileBytes != null)
            {
                CryptographicOperations.ZeroMemory(_keyFileBytes);
                _keyFileBytes = null;
            }
        }

        private void LoadRecentVaults()
        {
            var settings = AppSettingsManager.Load();
            CmbRecentVaults.ItemsSource = settings.RecentVaults;
            if (!string.IsNullOrEmpty(settings.LastOpenedVaultPath) && File.Exists(settings.LastOpenedVaultPath))
            {
                CmbRecentVaults.Text = settings.LastOpenedVaultPath;
            }
            else if (settings.RecentVaults.Count > 0)
            {
                CmbRecentVaults.SelectedIndex = 0;
            }
            UpdateBackupBadge();
        }

        private void UpdateBackupBadge()
        {
            string path = CmbRecentVaults.Text.Trim();
            if (!string.IsNullOrEmpty(path) && File.Exists(path + ".backup"))
            {
                PanelBackupStatus.Visibility = Visibility.Visible;
            }
            else
            {
                PanelBackupStatus.Visibility = Visibility.Collapsed;
            }
        }

        private void CmbRecentVaults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBackupBadge();
        }

        private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            var settings = AppSettingsManager.Load();
            settings.DarkMode = !settings.DarkMode;
            SQLitePass.UI.Services.ThemeManager.ApplyTheme(settings.DarkMode);
            AppSettingsManager.Save(settings);
        }

        private string GetMasterPassword()
        {
            return _isMasterPwdRevealed ? TxtMasterPasswordPlain.Text : TxtMasterPassword.Password;
        }

        private void BtnToggleMasterPwd_Click(object sender, RoutedEventArgs e)
        {
            _isMasterPwdRevealed = !_isMasterPwdRevealed;
            if (_isMasterPwdRevealed)
            {
                TxtMasterPasswordPlain.Text = TxtMasterPassword.Password;
                TxtMasterPasswordPlain.Visibility = Visibility.Visible;
                TxtMasterPassword.Visibility = Visibility.Collapsed;
                BtnToggleMasterPwd.Content = "🔒";
            }
            else
            {
                TxtMasterPassword.Password = TxtMasterPasswordPlain.Text;
                TxtMasterPassword.Visibility = Visibility.Visible;
                TxtMasterPasswordPlain.Visibility = Visibility.Collapsed;
                BtnToggleMasterPwd.Content = "👁";
            }
        }

        private void ChkUseKeyFile_Changed(object sender, RoutedEventArgs e)
        {
            GridKeyFile.IsEnabled = ChkUseKeyFile.IsChecked == true;
            if (ChkUseKeyFile.IsChecked != true)
            {
                if (_keyFileBytes != null)
                {
                    CryptographicOperations.ZeroMemory(_keyFileBytes);
                    _keyFileBytes = null;
                }
                TxtKeyFilePath.Text = string.Empty;
            }
        }

        private void BtnBrowseKeyFile_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Seleziona Key File",
                Filter = "File Chiave (*.key;*.bin)|*.key;*.bin|Tutti i file (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                TxtKeyFilePath.Text = ofd.FileName;
                _keyFileBytes = File.ReadAllBytes(ofd.FileName);
            }
        }

        private void BtnBrowseVault_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Seleziona Database Vault",
                Filter = "SQLitePass Database (*.kpd;*.db)|*.kpd;*.db|Tutti i file (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                CmbRecentVaults.Text = ofd.FileName;
                UpdateBackupBadge();
            }
        }

        private void BtnRestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            string activeFile = CmbRecentVaults.Text.Trim();
            string backupFile = activeFile + ".backup";

            if (!File.Exists(backupFile))
            {
                MessageBox.Show("Nessun file .backup trovato per questo vault.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                $"Attenzione: il database principale verrà sovrascritto con la copia isolata '{Path.GetFileName(backupFile)}'.\n\nVuoi procedere?",
                "Ripristino da Backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    File.Copy(backupFile, activeFile, overwrite: true);
                    MessageBox.Show("Vault ripristinato con successo dalla copia di backup!", "Completato", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Errore nel ripristino: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnOpenVault_Click(object sender, RoutedEventArgs e)
        {
            string vaultPath = CmbRecentVaults.Text.Trim();
            if (string.IsNullOrEmpty(vaultPath))
            {
                MessageBox.Show("Seleziona o indica il percorso del file di database (.kpd).", "File Mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(vaultPath))
            {
                MessageBox.Show($"Il file specificato non esiste:\n{vaultPath}", "File Non Trovato", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string masterPassword = GetMasterPassword();
            if (string.IsNullOrEmpty(masterPassword) && _keyFileBytes == null)
            {
                MessageBox.Show("Inserisci la Master Password per sbloccare il database.", "Password Richiesta", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var provider = new SqliteStorageProvider(vaultPath);

            try
            {
                // Verifica preventiva di integrità fisica (PRAGMA quick_check)
                bool isIntegrityOk = await provider.CheckDatabaseIntegrityAsync();
                if (!isIntegrityOk)
                {
                    string backupFile = provider.BackupFilePath;
                    bool hasBackup = File.Exists(backupFile);
                    var choice = MessageBox.Show(
                        "Attenzione: il controllo di integrità del database ha rilevato una possibile corruzione del file.\n\n" +
                        (hasBackup 
                            ? "È presente una copia isolata di backup (.backup). Vuoi ripristinarla adesso prima di accedere?" 
                            : "Nessun file di backup trovato. Vuoi tentare comunque di aprire il file?"),
                        "Integrità Database Compromessa",
                        hasBackup ? MessageBoxButton.YesNo : MessageBoxButton.OKCancel,
                        MessageBoxImage.Warning
                    );

                    if (hasBackup && choice == MessageBoxResult.Yes)
                    {
                        File.Copy(backupFile, vaultPath, overwrite: true);
                        AppLogger.LogInfo($"Vault '{vaultPath}' restored from backup due to failed integrity check.");
                        MessageBox.Show("Vault ripristinato con successo dalla copia di backup. Procedi con lo sblocco.", "Ripristino Completato", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (!hasBackup && choice != MessageBoxResult.OK)
                    {
                        return;
                    }
                }

                await provider.InitializeDatabaseAndSchemaAsync();
                var metadata = await provider.LoadVaultMetadataAsync();

                if (metadata == null)
                {
                    MessageBox.Show("Nessun vault inizializzato trovato in questo file. Clicca '+ Nuovo Vault' per crearne uno.", "Vault Incompleto", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Derivazione chiave con Argon2id con pulizia garantita di _keyFileBytes
                byte[] salt = Convert.FromBase64String(metadata.SaltBase64);
                byte[] derivedKey;
                try
                {
                    using var mk = new MasterKey(masterPassword, _keyFileBytes);
                    derivedKey = CryptoEngine.DeriveKeyArgon2(mk, salt, metadata.KdfIterations, metadata.KdfMemoryKb, metadata.KdfParallelism);
                }
                finally
                {
                    if (_keyFileBytes != null)
                    {
                        CryptographicOperations.ZeroMemory(_keyFileBytes);
                        _keyFileBytes = null;
                    }
                }

                var crypto = new CryptoEngine(derivedKey);

                // Verifica canary token
                if (!crypto.VerifyCanaryToken(metadata.VerificationCanary))
                {
                    crypto.Dispose();
                    MessageBox.Show("Master Password non corretta o Key File errato! Accesso negato.", "Autenticazione Fallita", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Successo
                AppSettingsManager.AddRecentVault(vaultPath);
                StorageProvider = provider;
                AuthenticatedCrypto = crypto;
                Metadata = metadata;

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante l'apertura del database:\n{ex.Message}", "Errore Database", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnCreateVault_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Scegli dove salvare il nuovo database del Vault",
                Filter = "SQLitePass Database (*.kpd)|*.kpd",
                FileName = "PasswordVault.kpd",
                DefaultExt = ".kpd"
            };

            if (sfd.ShowDialog() != true) return;

            string vaultPath = sfd.FileName;

            string masterPassword = GetMasterPassword();
            if (string.IsNullOrEmpty(masterPassword) && _keyFileBytes == null)
            {
                MessageBox.Show("Imposta una Master Password per proteggere il nuovo vault.", "Password Richiesta", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var provider = new SqliteStorageProvider(vaultPath);

            try
            {
                await provider.InitializeDatabaseAndSchemaAsync();

                // Salt casuale a 16 byte
                byte[] salt = new byte[16];
                RandomNumberGenerator.Fill(salt);

                byte[] derivedKey;
                int iterations = 3;
                int memoryKb = 64 * 1024;
                int parallelism = 2;

                try
                {
                    using var mk = new MasterKey(masterPassword, _keyFileBytes);
                    derivedKey = CryptoEngine.DeriveKeyArgon2(mk, salt, iterations, memoryKb, parallelism);
                }
                finally
                {
                    if (_keyFileBytes != null)
                    {
                        CryptographicOperations.ZeroMemory(_keyFileBytes);
                        _keyFileBytes = null;
                    }
                }

                var crypto = new CryptoEngine(derivedKey);

                string canary = crypto.GenerateCanaryToken();

                var meta = new VaultMetadata
                {
                    VaultId = Guid.NewGuid(),
                    VaultName = Path.GetFileNameWithoutExtension(vaultPath),
                    SaltBase64 = Convert.ToBase64String(salt),
                    KdfIterations = iterations,
                    KdfMemoryKb = memoryKb,
                    KdfParallelism = parallelism,
                    VerificationCanary = canary,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await provider.SaveVaultMetadataAsync(meta);

                // Gruppi standard di default
                var rootGroup = new PwGroup { Id = Guid.NewGuid(), Name = meta.VaultName, IconIndex = 48 };
                var generalGroup = new PwGroup { Id = Guid.NewGuid(), ParentId = rootGroup.Id, Name = "Generale", IconIndex = 48 };
                var internetGroup = new PwGroup { Id = Guid.NewGuid(), ParentId = rootGroup.Id, Name = "Internet & Web", IconIndex = 1 };
                var emailGroup = new PwGroup { Id = Guid.NewGuid(), ParentId = rootGroup.Id, Name = "Posta Elettronica", IconIndex = 2 };
                var bankingGroup = new PwGroup { Id = Guid.NewGuid(), ParentId = rootGroup.Id, Name = "Banche & Finanza", IconIndex = 3 };
                var recycleGroup = new PwGroup { Id = Guid.NewGuid(), ParentId = rootGroup.Id, Name = "Cestino", IconIndex = 43 };

                await provider.SaveGroupAsync(rootGroup, crypto);
                await provider.SaveGroupAsync(generalGroup, crypto);
                await provider.SaveGroupAsync(internetGroup, crypto);
                await provider.SaveGroupAsync(emailGroup, crypto);
                await provider.SaveGroupAsync(bankingGroup, crypto);
                await provider.SaveGroupAsync(recycleGroup, crypto);

                // Voce di esempio
                var sampleEntry = new PwEntry
                {
                    Id = Guid.NewGuid(),
                    GroupId = generalGroup.Id,
                    Title = "Esempio - Account Sicuro",
                    UserName = "utente@dominio.it",
                    Password = PasswordGenerator.Generate(new PasswordGeneratorOptions { Length = 22 }),
                    Url = "https://keepass.info",
                    Notes = "Benvenuto in SQLitePass!\n\nQuesto database è protetto da cifratura Zero-Knowledge AES-256-GCM + Argon2id.\nUn file .backup isolato viene mantenuto sincronizzato automaticamente.",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await provider.SaveEntryAsync(sampleEntry, crypto);

                AppSettingsManager.AddRecentVault(vaultPath);
                StorageProvider = provider;
                AuthenticatedCrypto = crypto;
                Metadata = meta;

                MessageBox.Show(
                    $"Nuovo vault creato con successo!\n\nFile attivo: {vaultPath}\nFile backup: {vaultPath}.backup",
                    "Vault Inizializzato",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Creazione del vault fallita: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
