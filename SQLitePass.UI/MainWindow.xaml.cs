using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Dialogs;
using SQLitePass.UI.Services;

namespace SQLitePass.UI
{
    public partial class MainWindow : Window
    {
        private SqliteStorageProvider _storage;
        private CryptoEngine _crypto;
        private VaultMetadata _metadata;

        private readonly ObservableCollection<PwGroup> _rootGroups = new();
        private readonly ObservableCollection<PwEntry> _allEntries = new();
        private ICollectionView _entriesView;

        private PwGroup? _selectedGroup;
        private PwEntry? _selectedEntry;

        private DispatcherTimer? _clipboardTimer;
        private int _clipboardSecondsRemaining;
        private string? _lastCopiedText;

        private DispatcherTimer? _idleLockTimer;
        private bool _previewPasswordRevealed = false;
        private bool _showPasswordColumn = false;
        private bool _isExplicitShutdown = false;

        // TaskbarIcon for System Tray
        private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? _trayIcon;

        // Win32 Global Hotkey (Ctrl+Alt+A) for Auto-Type
        private const int HOTKEY_ID = 9001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint VK_A = 0x41;
        private HwndSource? _hwndSource;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        public MainWindow(SqliteStorageProvider storage, CryptoEngine crypto, VaultMetadata metadata)
        {
            InitializeComponent();
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

            TvGroups.ItemsSource = _rootGroups;

            _entriesView = CollectionViewSource.GetDefaultView(_allEntries);
            _entriesView.Filter = FilterEntry;
            DgEntries.ItemsSource = _entriesView;

            TxtStatusDb.Text = $"Vault: {Path.GetFileName(_storage.FilePath)} • Dual Backup: Attivo";

            InitializeTrayIcon();
            InitializeIdleTimer();

            Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;

            // Auto-lock when workstation is locked (Win+L)
            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon
            {
                Icon = SystemIcons.Shield,
                ToolTipText = "SQLitePass - Vault Protetto"
            };

            var contextMenu = new System.Windows.Controls.ContextMenu();

            var itemShow = new System.Windows.Controls.MenuItem
            {
                Header = "Mostra SQLitePass",
                FontWeight = FontWeights.Bold
            };
            itemShow.Click += (s, e) => ShowAndRestoreWindow();
            contextMenu.Items.Add(itemShow);

            var itemGen = new System.Windows.Controls.MenuItem { Header = "Generatore Password..." };
            itemGen.Click += (s, e) => Dispatcher.Invoke(() => BtnPasswordGenerator_Click(this, new RoutedEventArgs()));
            contextMenu.Items.Add(itemGen);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var itemLock = new System.Windows.Controls.MenuItem { Header = "Blocca Vault" };
            itemLock.Click += (s, e) => Dispatcher.Invoke(() => BtnLock_Click(this, new RoutedEventArgs()));
            contextMenu.Items.Add(itemLock);

            contextMenu.Items.Add(new System.Windows.Controls.Separator());

            var itemExit = new System.Windows.Controls.MenuItem { Header = "Esci" };
            itemExit.Click += (s, e) => Dispatcher.Invoke(MenuExit_Click, this, new RoutedEventArgs());
            contextMenu.Items.Add(itemExit);

            _trayIcon.ContextMenu = contextMenu;
            _trayIcon.TrayMouseDoubleClick += (s, e) => Dispatcher.Invoke(ShowAndRestoreWindow);
        }

        private void ShowAndRestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void InitializeIdleTimer()
        {
            _idleLockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _idleLockTimer.Tick += (s, e) =>
            {
                var settings = AppSettingsManager.Load();
                if (settings.AutoLockMinutes > 0)
                {
                    var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                    if (GetLastInputInfo(ref lii))
                    {
                        uint idleMs = (uint)Environment.TickCount - lii.dwTime;
                        if (idleMs >= (uint)settings.AutoLockMinutes * 60 * 1000)
                        {
                            BtnLock_Click(this, new RoutedEventArgs());
                        }
                    }
                }
            };
            _idleLockTimer.Start();
        }

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                var settings = AppSettingsManager.Load();
                if (settings.AutoLockOnWindowsLock)
                {
                    Dispatcher.Invoke(() => BtnLock_Click(this, new RoutedEventArgs()));
                }
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource.AddHook(HwndHook);

            // Register global hotkey Ctrl+Alt+A
            RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_A);
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_isExplicitShutdown)
            {
                var settings = AppSettingsManager.Load();
                if (settings.CloseToTray)
                {
                    e.Cancel = true;
                    Hide();
                    _trayIcon?.ShowBalloonTip("SQLitePass", "L'applicazione è ancora attiva in background nella System Tray.", Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                    return;
                }
            }

            _idleLockTimer?.Stop();
            _clipboardTimer?.Stop();
            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hwndSource != null)
            {
                var helper = new WindowInteropHelper(this);
                UnregisterHotKey(helper.Handle, HOTKEY_ID);
                _hwndSource.RemoveHook(HwndHook);
            }
            SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
            base.OnClosed(e);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                HandleGlobalAutoType();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private async void HandleGlobalAutoType()
        {
            IntPtr foregroundWin = GetForegroundWindow();
            var titleBuilder = new System.Text.StringBuilder(256);
            GetWindowText(foregroundWin, titleBuilder, 256);
            string winTitle = titleBuilder.ToString();

            // Find matching entry by Window Title or URL domain
            PwEntry? match = _allEntries.FirstOrDefault(e =>
                (!string.IsNullOrEmpty(e.Title) && winTitle.Contains(e.Title, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.Url) && winTitle.Contains(e.Url.Replace("https://", "").Replace("http://", "").Split('/')[0], StringComparison.OrdinalIgnoreCase))
            );

            if (match != null)
            {
                await AutoTypeService.ExecuteAutoTypeAsync(match, foregroundWin, 150);
            }
            else if (_selectedEntry != null)
            {
                await AutoTypeService.ExecuteAutoTypeAsync(_selectedEntry, foregroundWin, 150);
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            var settings = AppSettingsManager.Load();

            if (WindowState == WindowState.Minimized)
            {
                if (settings.AutoLockOnMinimize)
                {
                    BtnLock_Click(this, new RoutedEventArgs());
                    return;
                }

                if (settings.MinimizeToTray)
                {
                    Hide();
                }
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = AppSettingsManager.Load();
            Topmost = settings.AlwaysOnTop;

            if (settings.ShowPasswordColumnDefault)
            {
                MenuTogglePasswords_Click(this, new RoutedEventArgs());
            }

            await RefreshDataAsync();

            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                await UpdateService.CheckForUpdatesAsync(silent: true);
            });
        }

        private async Task RefreshDataAsync()
        {
            try
            {
                _rootGroups.Clear();
                _allEntries.Clear();

                // 1. Carica gruppi gerarchici
                var flatGroups = await _storage.LoadAllGroupsAsync(_crypto);
                var groupDict = flatGroups.ToDictionary(g => g.Id);

                foreach (var group in flatGroups)
                {
                    if (group.ParentId.HasValue && groupDict.TryGetValue(group.ParentId.Value, out var parent))
                    {
                        parent.Children.Add(group);
                    }
                    else
                    {
                        _rootGroups.Add(group);
                    }
                }

                if (_rootGroups.Count > 0)
                {
                    _rootGroups[0].IsExpanded = true;
                    _rootGroups[0].IsSelected = true;
                }

                // 2. Carica tutte le voci
                var entries = await _storage.LoadEntriesAsync(null, _crypto);
                foreach (var entry in entries)
                {
                    _allEntries.Add(entry);
                }

                UpdateStatusCounts();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel caricamento del database SQLite:\n{ex.Message}", "Errore Database", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool FilterEntry(object item)
        {
            if (item is not PwEntry entry) return false;

            string query = TxtSearch.Text?.Trim() ?? "";

            // Ricerca globale nel vault
            if (!string.IsNullOrEmpty(query))
            {
                return (entry.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (entry.UserName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (entry.Url?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (entry.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (entry.Tags?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
            }

            // Altrimenti filtra per gruppo selezionato
            if (_selectedGroup != null && entry.GroupId != _selectedGroup.Id)
            {
                return false;
            }

            return true;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _entriesView.Refresh();
            UpdateStatusCounts();
        }

        private void TvGroups_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _selectedGroup = e.NewValue as PwGroup;
            _entriesView.Refresh();
            UpdateStatusCounts();
        }

        private void DgEntries_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedEntry = DgEntries.SelectedItem as PwEntry;
            UpdatePreviewPanel();
            UpdateStatusCounts();
        }

        private void UpdateStatusCounts()
        {
            int total = _allEntries.Count;
            int filtered = _entriesView.Cast<object>().Count();
            TxtStatusEntriesCount.Text = $"{filtered} di {total} voci visualizzate";
        }

        private void UpdatePreviewPanel()
        {
            if (_selectedEntry == null)
            {
                GridEntryPreview.Visibility = Visibility.Collapsed;
                return;
            }

            GridEntryPreview.Visibility = Visibility.Visible;
            PrevTitle.Text = _selectedEntry.Title;
            PrevUserName.Text = _selectedEntry.UserName;
            _previewPasswordRevealed = false;
            PrevPassword.Text = new string('•', Math.Min(_selectedEntry.Password.Length, 14));

            double entropy = PasswordGenerator.CalculateEntropy(_selectedEntry.Password);
            PrevPbQuality.Value = Math.Min(entropy, 128);
            PrevEntropyText.Text = $"{entropy:F1} bits";

            PrevUrl.Text = _selectedEntry.Url;
            PrevModified.Text = _selectedEntry.UpdatedAt.ToString("yyyy-MM-dd HH:mm");
            PrevNotes.Text = string.IsNullOrEmpty(_selectedEntry.Notes) ? "(Nessuna nota)" : _selectedEntry.Notes;
        }

        private void BtnPreviewTogglePassword_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null) return;
            _previewPasswordRevealed = !_previewPasswordRevealed;
            PrevPassword.Text = _previewPasswordRevealed ? _selectedEntry.Password : new string('•', Math.Min(_selectedEntry.Password.Length, 14));
        }

        private void PrevUrl_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenUrl(_selectedEntry?.Url);
        }

        private void DgEntries_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_selectedEntry == null) return;

            var depObj = e.OriginalSource as DependencyObject;
            while (depObj != null && depObj is not DataGridCell && depObj is not DataGrid)
            {
                depObj = System.Windows.Media.VisualTreeHelper.GetParent(depObj);
            }

            if (depObj is DataGridCell cell)
            {
                string? header = cell.Column.Header?.ToString();
                if (string.Equals(header, "Password", StringComparison.OrdinalIgnoreCase))
                {
                    BtnCopyPassword_Click(sender, e);
                    e.Handled = true;
                    return;
                }
                else if (string.Equals(header, "User Name", StringComparison.OrdinalIgnoreCase))
                {
                    BtnCopyUsername_Click(sender, e);
                    e.Handled = true;
                    return;
                }
                else if (string.Equals(header, "URL", StringComparison.OrdinalIgnoreCase))
                {
                    BtnOpenUrl_Click(sender, e);
                    e.Handled = true;
                    return;
                }
            }

            BtnEditEntry_Click(sender, e);
        }

        #region Actions

        private async void BtnAddEntry_Click(object sender, RoutedEventArgs e)
        {
            var targetGroupId = _selectedGroup?.Id ?? _rootGroups.FirstOrDefault()?.Id ?? Guid.NewGuid();
            var newEntry = new PwEntry
            {
                GroupId = targetGroupId,
                Title = "Nuova Voce",
                AutoTypeSequence = "{USERNAME}{TAB}{PASSWORD}{ENTER}"
            };

            var dlg = new EntryEditDialog(newEntry) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    await _storage.SaveEntryAsync(newEntry, _crypto);
                    _allEntries.Add(newEntry);
                    _entriesView.Refresh();
                    DgEntries.SelectedItem = newEntry;
                    UpdateStatusCounts();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Salvataggio voce fallito:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnEditEntry_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null) return;

            var dlg = new EntryEditDialog(_selectedEntry) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                try
                {
                    await _storage.SaveEntryAsync(_selectedEntry, _crypto);
                    _entriesView.Refresh();
                    UpdatePreviewPanel();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Aggiornamento voce fallito:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnDuplicateEntry_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null) return;

            var dup = new PwEntry
            {
                GroupId = _selectedEntry.GroupId,
                Title = _selectedEntry.Title + " - Copia",
                UserName = _selectedEntry.UserName,
                Password = _selectedEntry.Password,
                Url = _selectedEntry.Url,
                Notes = _selectedEntry.Notes,
                Tags = _selectedEntry.Tags,
                AutoTypeEnabled = _selectedEntry.AutoTypeEnabled,
                AutoTypeSequence = _selectedEntry.AutoTypeSequence
            };

            try
            {
                await _storage.SaveEntryAsync(dup, _crypto);
                _allEntries.Add(dup);
                _entriesView.Refresh();
                DgEntries.SelectedItem = dup;
                UpdateStatusCounts();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Duplicazione voce fallita:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnDeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null) return;

            if (MessageBox.Show($"Eliminare definitivamente la voce '{_selectedEntry.Title}'?", "Conferma Eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    await _storage.DeleteEntryAsync(_selectedEntry.Id);
                    _allEntries.Remove(_selectedEntry);
                    _entriesView.Refresh();
                    UpdatePreviewPanel();
                    UpdateStatusCounts();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Eliminazione voce fallita:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnAddGroup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new GroupEditDialog("Nuovo Gruppo") { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var newGroup = new PwGroup
                {
                    Name = dlg.GroupName,
                    ParentId = _selectedGroup?.Id
                };

                try
                {
                    await _storage.SaveGroupAsync(newGroup, _crypto);
                    if (_selectedGroup != null)
                    {
                        _selectedGroup.Children.Add(newGroup);
                        _selectedGroup.IsExpanded = true;
                    }
                    else
                    {
                        _rootGroups.Add(newGroup);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Creazione gruppo fallita:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void MenuRenameGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null) return;

            var dlg = new GroupEditDialog(_selectedGroup.Name) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _selectedGroup.Name = dlg.GroupName;
                try
                {
                    await _storage.SaveGroupAsync(_selectedGroup, _crypto);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Rinomina gruppo fallita:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void MenuDeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null) return;

            if (MessageBox.Show($"Eliminare il gruppo '{_selectedGroup.Name}' e tutte le voci al suo interno?", "Conferma Eliminazione Gruppo", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    await _storage.DeleteGroupAsync(_selectedGroup.Id);
                    await RefreshDataAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Eliminazione gruppo fallita:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCopyUsername_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry != null && !string.IsNullOrEmpty(_selectedEntry.UserName))
            {
                CopyToClipboardWithAutoClear(_selectedEntry.UserName, "User name");
            }
        }

        private void BtnCopyPassword_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry != null && !string.IsNullOrEmpty(_selectedEntry.Password))
            {
                CopyToClipboardWithAutoClear(_selectedEntry.Password, "Password");
            }
        }

        private void CopyToClipboardWithAutoClear(string text, string description)
        {
            var settings = AppSettingsManager.Load();
            int timeoutSecs = settings.ClipboardClearSeconds > 0 ? settings.ClipboardClearSeconds : 12;

            Clipboard.SetText(text);
            _lastCopiedText = text;
            _clipboardSecondsRemaining = timeoutSecs;

            _clipboardTimer?.Stop();
            _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clipboardTimer.Tick += (s, ev) =>
            {
                _clipboardSecondsRemaining--;
                if (_clipboardSecondsRemaining > 0)
                {
                    TxtStatusClipboard.Text = $"📋 {description} negli appunti ({_clipboardSecondsRemaining}s)";
                }
                else
                {
                    _clipboardTimer?.Stop();
                    try
                    {
                        if (Clipboard.GetText() == _lastCopiedText)
                        {
                            Clipboard.Clear();
                        }
                    }
                    catch { }
                    TxtStatusClipboard.Text = "📋 Appunti cancellati per sicurezza";
                }
            };
            _clipboardTimer.Start();
            TxtStatusClipboard.Text = $"📋 {description} negli appunti ({timeoutSecs}s)";
        }

        private void BtnClearClipboard_Click(object sender, RoutedEventArgs e)
        {
            _clipboardTimer?.Stop();
            Clipboard.Clear();
            TxtStatusClipboard.Text = "📋 Appunti cancellati";
        }

        private void BtnOpenUrl_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(_selectedEntry?.Url);
        }

        private static void OpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossibile aprire l'URL:\n{ex.Message}", "Errore URL", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnAutoType_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null)
            {
                MessageBox.Show("Seleziona una voce per eseguire l'Auto-Type.", "Informazione", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            WindowState = WindowState.Minimized;
            await Task.Delay(400);

            await AutoTypeService.ExecuteAutoTypeAsync(_selectedEntry, IntPtr.Zero, 250);
        }

        private void BtnPasswordGenerator_Click(object sender, RoutedEventArgs e)
        {
            var gen = new PasswordGeneratorDialog { Owner = this };
            gen.ShowDialog();
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var settings = AppSettingsManager.Load();
                Topmost = settings.AlwaysOnTop;
            }
        }

        private void BtnLock_Click(object sender, RoutedEventArgs e)
        {
            _crypto.Dispose();
            _allEntries.Clear();
            _rootGroups.Clear();

            var login = new LoginWindow();
            if (login.ShowDialog() == true && login.AuthenticatedCrypto != null && login.StorageProvider != null && login.Metadata != null)
            {
                _storage = login.StorageProvider;
                _crypto = login.AuthenticatedCrypto;
                _metadata = login.Metadata;
                TxtStatusDb.Text = $"Vault: {Path.GetFileName(_storage.FilePath)} • Dual Backup: Attivo";
                _ = RefreshDataAsync();
            }
            else
            {
                _isExplicitShutdown = true;
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void MenuTogglePasswords_Click(object sender, RoutedEventArgs e)
        {
            _showPasswordColumn = !_showPasswordColumn;
            if (_showPasswordColumn)
            {
                ColPassword.Binding = new Binding("Password");
            }
            else
            {
                ColPassword.Binding = new Binding("Password") { Converter = (IValueConverter)Resources["PasswordMaskConverter"] };
            }
        }

        private async void MenuRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshDataAsync();
        }

        private async void MenuImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Importa CSV di KeePass",
                Filter = "File CSV (*.csv)|*.csv|Tutti i file (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string content = File.ReadAllText(ofd.FileName);
                    var targetGroupId = _selectedGroup?.Id ?? _rootGroups.FirstOrDefault()?.Id ?? Guid.NewGuid();
                    var imported = ImportExportService.ImportFromKeePassCsv(content, targetGroupId);

                    foreach (var entry in imported)
                    {
                        await _storage.SaveEntryAsync(entry, _crypto);
                        _allEntries.Add(entry);
                    }
                    _entriesView.Refresh();
                    UpdateStatusCounts();
                    MessageBox.Show($"Importate con successo {imported.Count} voci!", "Importazione Riuscita", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Importazione fallita:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void MenuExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Title = "Esporta CSV KeePass",
                Filter = "File CSV (*.csv)|*.csv",
                FileName = "SQLitePass_Export.csv"
            };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    var groupMap = _rootGroups.ToDictionary(g => g.Id, g => g.Name);
                    string csv = ImportExportService.ExportToKeePassCsv(_allEntries, groupMap);
                    File.WriteAllText(sfd.FileName, csv);
                    MessageBox.Show("Esportazione completata con successo!", "Esportato", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Esportazione fallita:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void MenuCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await UpdateService.CheckForUpdatesAsync(silent: false);
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "SQLitePass v1.0\n\n" +
                "Zero-Knowledge Password Manager con Storage SQLite Resiliente\n" +
                "• SQLite con WAL Mode (anti-corruzione su riavvio/crash di Windows)\n" +
                "• Cifratura a livello di record AES-256-GCM + Argon2id\n" +
                "• Dual Backup atomico sincronizzato (.kpd.backup)\n" +
                "• Minimize to Tray & Auto-Lock per inattività e blocco Windows\n" +
                "• Auto-Type globale (Ctrl+Alt+A), Generatore password ed Import/Export\n" +
                "• 100% leggero, portatile e senza server da installare.",
                "Informazioni su SQLitePass",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            _isExplicitShutdown = true;
            Close();
            System.Windows.Application.Current.Shutdown();
        }

        #endregion
    }
}
