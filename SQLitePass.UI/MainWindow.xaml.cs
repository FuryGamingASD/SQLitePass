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
using Point = System.Windows.Point;

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

        private readonly ClipboardCoordinator _clipboardCoordinator;
        private System.Windows.Point _dragStartPoint;
        private System.Windows.Point _groupDragStartPoint;
        private PwGroup? _dragCandidateGroup;
        private TreeViewDropAdorner? _dropAdorner;

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

        [DllImport("kernel32.dll")]
        private static extern uint GetTickCount();

        private bool _isLocked = false;
        private bool _isLocking = false;
        private bool _isPerformingAutoType = false;

        public MainWindow(SqliteStorageProvider storage, CryptoEngine crypto, VaultMetadata metadata)
        {
            InitializeComponent();
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

            _clipboardCoordinator = new ClipboardCoordinator(msg => TxtStatusClipboard.Text = msg);

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
            System.Drawing.Icon trayIconImage = SystemIcons.Shield;
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/app.ico");
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using var s = streamInfo.Stream;
                    trayIconImage = new System.Drawing.Icon(s);
                }
            }
            catch { }

            _trayIcon = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon
            {
                Icon = trayIconImage,
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
            if (_isLocked)
            {
                PromptUnlock();
                return;
            }

            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void InitializeIdleTimer()
        {
            _idleLockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _idleLockTimer.Tick += (s, e) =>
            {
                if (_isLocked || _isLocking || _isPerformingAutoType) return;

                var settings = AppSettingsManager.Load();
                if (settings.AutoLockMinutes > 0)
                {
                    var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
                    if (GetLastInputInfo(ref lii))
                    {
                        uint envTicks = GetTickCount();
                        uint idleMs = envTicks >= lii.dwTime ? (envTicks - lii.dwTime) : (uint.MaxValue - lii.dwTime + envTicks);
                        if (idleMs >= (uint)settings.AutoLockMinutes * 60 * 1000)
                        {
                            LockVault(userInitiated: false);
                        }
                    }
                }
            };
            _idleLockTimer.Start();
        }

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (_isPerformingAutoType) return;

            if (e.Reason == SessionSwitchReason.SessionLock)
            {
                var settings = AppSettingsManager.Load();
                if (settings.AutoLockOnWindowsLock)
                {
                    Dispatcher.Invoke(() => LockVault(userInitiated: false));
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
            _clipboardCoordinator.Dispose();
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

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (_isLocked && !_isLocking && !_isPerformingAutoType)
            {
                PromptUnlock();
            }
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
            if (_isLocked || _isPerformingAutoType) return;
            _isPerformingAutoType = true;

            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Global Auto-Type error: {ex.Message}");
            }
            finally
            {
                await Task.Delay(250);
                _isPerformingAutoType = false;
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (_isPerformingAutoType) return;

            var settings = AppSettingsManager.Load();

            if (WindowState == WindowState.Minimized)
            {
                if (settings.AutoLockOnMinimize)
                {
                    LockVault(userInitiated: false);
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
            WindowSecurityService.ApplyProtection(this, settings.ProtectScreenCapture);

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

        private void DgEntries_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool hasSelection = _selectedEntry != null;

            CmCopyUser.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(_selectedEntry?.UserName);
            CmCopyPassword.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(_selectedEntry?.Password);
            CmOpenUrl.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(_selectedEntry?.Url);
            CmAutoType.IsEnabled = hasSelection && (!string.IsNullOrWhiteSpace(_selectedEntry?.UserName) || !string.IsNullOrWhiteSpace(_selectedEntry?.Password));

            CmCopyUser.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            CmCopyPassword.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            CmOpenUrl.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            CmAutoType.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            CmSep1.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            CmEdit.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            CmDuplicate.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            CmDelete.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            CmSep2.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
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

        #region Drag and Drop (Entries to Groups)

        private void DgEntries_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void DgEntries_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _selectedEntry != null)
            {
                System.Windows.Point currentPoint = e.GetPosition(null);
                System.Windows.Vector diff = _dragStartPoint - currentPoint;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    try
                    {
                        DragDrop.DoDragDrop(DgEntries, _selectedEntry, DragDropEffects.Move);
                    }
                    catch { }
                    finally
                    {
                        ClearDropAdorner();
                    }
                }
            }
        }

        private void TvGroups_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _groupDragStartPoint = e.GetPosition(null);
            _dragCandidateGroup = GetGroupByHitTest(e.GetPosition(TvGroups));
        }

        private void TvGroups_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed && _dragCandidateGroup != null)
            {
                System.Windows.Point currentPoint = e.GetPosition(null);
                System.Windows.Vector diff = _groupDragStartPoint - currentPoint;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // Do not allow dragging the main vault root group or Cestino
                    if (_rootGroups.Count > 0 && _dragCandidateGroup.Id == _rootGroups[0].Id)
                        return;
                    if (VaultOperationHandler.IsRecycleBinGroup(_dragCandidateGroup))
                        return;

                    try
                    {
                        DragDrop.DoDragDrop(TvGroups, _dragCandidateGroup, DragDropEffects.Move);
                    }
                    catch { }
                    finally
                    {
                        _dragCandidateGroup = null;
                        ClearDropAdorner();
                    }
                }
            }
        }

        private void TvGroups_PreviewDragOver(object sender, DragEventArgs e)
        {
            var adorner = EnsureDropAdorner();

            if (e.Data.GetDataPresent(typeof(PwEntry)))
            {
                var (targetGroup, targetHeader) = GetGroupAndHeaderFromHitTest(e.GetPosition(TvGroups));

                if (targetGroup != null && targetHeader != null && targetHeader.ActualHeight > 0)
                {
                    Point p = targetHeader.TranslatePoint(new Point(0, 0), TvGroups);
                    double w = Math.Max(targetHeader.ActualWidth, TvGroups.ActualWidth - p.X - 16);
                    double h = targetHeader.ActualHeight;

                    // Highlight the target folder that will become the parent of this entry
                    adorner?.UpdateTarget(new Rect(p.X, p.Y, w, h), DropPosition.Inside);
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }
                else
                {
                    adorner?.Clear();
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }
            }
            else if (e.Data.GetDataPresent(typeof(PwGroup)))
            {
                if (e.Data.GetData(typeof(PwGroup)) is PwGroup draggedGroup)
                {
                    Point mousePt = e.GetPosition(TvGroups);
                    var (targetGroup, targetHeader) = GetGroupAndHeaderFromHitTest(mousePt);

                    if (targetGroup == null || targetHeader == null || targetHeader.ActualHeight <= 0)
                    {
                        // Hovering empty tree space -> allowed (moves to root)
                        adorner?.Clear();
                        e.Effects = DragDropEffects.Move;
                        e.Handled = true;
                        return;
                    }

                    // Cannot drop group onto itself or its descendants
                    if (targetGroup.Id == draggedGroup.Id || IsDescendantOf(targetGroup, draggedGroup))
                    {
                        adorner?.Clear();
                        e.Effects = DragDropEffects.None;
                        e.Handled = true;
                        return;
                    }

                    bool isRoot = _rootGroups.Count > 0 && targetGroup.Id == _rootGroups[0].Id;
                    bool isTrash = VaultOperationHandler.IsRecycleBinGroup(targetGroup);

                    Point p = targetHeader.TranslatePoint(new Point(0, 0), TvGroups);
                    double w = Math.Max(targetHeader.ActualWidth, TvGroups.ActualWidth - p.X - 16);
                    double h = targetHeader.ActualHeight;
                    double relY = (mousePt.Y - p.Y) / h;

                    DropPosition pos;
                    if (relY < 0.28 && !isRoot)
                    {
                        pos = DropPosition.Before;
                    }
                    else if (relY > 0.72 && !isRoot)
                    {
                        pos = DropPosition.After;
                    }
                    else
                    {
                        pos = DropPosition.Inside;
                    }

                    // Cestino is unique and cannot have child subgroups!
                    if (pos == DropPosition.Inside && isTrash)
                    {
                        adorner?.Clear();
                        e.Effects = DragDropEffects.None;
                        e.Handled = true;
                        return;
                    }

                    adorner?.UpdateTarget(new Rect(p.X, p.Y, w, h), pos);
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }
            }

            adorner?.Clear();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void TvGroups_DragLeave(object sender, DragEventArgs e)
        {
            // Only clear when the mouse has actually exited the TvGroups control boundaries
            var pos = e.GetPosition(TvGroups);
            if (pos.X < 0 || pos.Y < 0 || pos.X >= TvGroups.ActualWidth || pos.Y >= TvGroups.ActualHeight)
            {
                ClearDropAdorner();
            }
        }

        private async void TvGroups_Drop(object sender, DragEventArgs e)
        {
            ClearDropAdorner();

            if (e.Data.GetData(typeof(PwEntry)) is PwEntry draggedEntry)
            {
                var (targetGroup, _) = GetGroupAndHeaderFromHitTest(e.GetPosition(TvGroups));

                if (targetGroup != null && draggedEntry.GroupId != targetGroup.Id)
                {
                    try
                    {
                        draggedEntry.GroupId = targetGroup.Id;
                        await _storage.SaveEntryAsync(draggedEntry, _crypto);
                        _entriesView.Refresh();
                        UpdateStatusCounts();

                        if (VaultOperationHandler.IsRecycleBinGroup(targetGroup))
                        {
                            TxtStatusClipboard.Text = $"🗑️ Voce '{draggedEntry.Title}' spostata nel Cestino";
                        }
                        else
                        {
                            TxtStatusClipboard.Text = $"📁 Voce '{draggedEntry.Title}' spostata in '{targetGroup.Name}'";
                        }
                        AppLogger.LogInfo($"Moved entry '{draggedEntry.Title}' to group '{targetGroup.Name}' via drag & drop");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("Drag & drop move entry failed", ex);
                        MessageBox.Show($"Impossibile spostare la voce:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else if (e.Data.GetData(typeof(PwGroup)) is PwGroup draggedGroup)
            {
                Point mousePt = e.GetPosition(TvGroups);
                var (targetGroup, targetHeader) = GetGroupAndHeaderFromHitTest(mousePt);

                // Drop onto empty area -> Move to root
                if (targetGroup == null || targetHeader == null)
                {
                    var rootParent = _rootGroups.FirstOrDefault();
                    if (rootParent != null && draggedGroup.ParentId != rootParent.Id && draggedGroup.Id != rootParent.Id)
                    {
                        await MoveGroupInsideAsync(draggedGroup, rootParent);
                    }
                    return;
                }

                // Prevent dropping onto self or descendant
                if (targetGroup.Id == draggedGroup.Id || IsDescendantOf(targetGroup, draggedGroup))
                    return;

                bool isRoot = _rootGroups.Count > 0 && targetGroup.Id == _rootGroups[0].Id;
                bool isTrash = VaultOperationHandler.IsRecycleBinGroup(targetGroup);

                double h = targetHeader.ActualHeight > 0 ? targetHeader.ActualHeight : 26;
                Point p = targetHeader.TranslatePoint(new Point(0, 0), TvGroups);
                double relY = (mousePt.Y - p.Y) / h;

                if (relY < 0.28 && !isRoot)
                {
                    // Insert BEFORE target
                    await MoveGroupRelativeAsync(draggedGroup, targetGroup, insertBefore: true);
                }
                else if (relY > 0.72 && !isRoot)
                {
                    // Insert AFTER target
                    await MoveGroupRelativeAsync(draggedGroup, targetGroup, insertBefore: false);
                }
                else
                {
                    // Move INSIDE target
                    if (isTrash)
                    {
                        MessageBox.Show("Il Cestino non può contenere sottocartelle.", "Operazione non consentita", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    await MoveGroupInsideAsync(draggedGroup, targetGroup);
                }
            }
        }

        private async Task MoveGroupInsideAsync(PwGroup groupToMove, PwGroup targetParent)
        {
            if (VaultOperationHandler.IsRecycleBinGroup(targetParent))
            {
                MessageBox.Show("Il Cestino non può contenere sottocartelle.", "Operazione non consentita", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var oldCollection = FindGroupParentCollection(groupToMove);
                oldCollection?.Remove(groupToMove);

                groupToMove.ParentId = targetParent.Id;
                groupToMove.SortOrder = targetParent.Children.Count > 0 
                    ? targetParent.Children.Max(c => c.SortOrder) + 1 
                    : 0;

                targetParent.Children.Add(groupToMove);
                targetParent.IsExpanded = true;
                groupToMove.IsSelected = true;

                await _storage.SaveGroupAsync(groupToMove, _crypto);
                TxtStatusClipboard.Text = $"📁 Gruppo '{groupToMove.Name}' spostato dentro '{targetParent.Name}'";
                AppLogger.LogInfo($"Group '{groupToMove.Name}' moved inside '{targetParent.Name}'");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Move group inside failed", ex);
                MessageBox.Show($"Impossibile spostare il gruppo:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task MoveGroupRelativeAsync(PwGroup groupToMove, PwGroup referenceGroup, bool insertBefore)
        {
            try
            {
                var oldCollection = FindGroupParentCollection(groupToMove);
                var targetCollection = FindGroupParentCollection(referenceGroup);

                if (targetCollection == null) return;

                oldCollection?.Remove(groupToMove);
                groupToMove.ParentId = referenceGroup.ParentId;

                int refIndex = targetCollection.IndexOf(referenceGroup);
                if (refIndex < 0) refIndex = targetCollection.Count;

                int insertIndex = insertBefore ? refIndex : refIndex + 1;
                if (insertIndex > targetCollection.Count) insertIndex = targetCollection.Count;

                targetCollection.Insert(insertIndex, groupToMove);
                groupToMove.IsSelected = true;

                // Re-index SortOrder sequentially
                for (int i = 0; i < targetCollection.Count; i++)
                {
                    targetCollection[i].SortOrder = i;
                }

                await _storage.SaveGroupsBatchAsync(targetCollection, _crypto);
                TxtStatusClipboard.Text = $"📁 Gruppo '{groupToMove.Name}' riordinato";
                AppLogger.LogInfo($"Group '{groupToMove.Name}' reordered {(insertBefore ? "before" : "after")} '{referenceGroup.Name}'");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Move group relative failed", ex);
                MessageBox.Show($"Impossibile riordinare il gruppo:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void MenuMoveGroupUp_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null) return;
            if (_rootGroups.Count > 0 && _selectedGroup.Id == _rootGroups[0].Id) return;
            if (VaultOperationHandler.IsRecycleBinGroup(_selectedGroup)) return;

            var col = FindGroupParentCollection(_selectedGroup);
            if (col == null) return;

            int idx = col.IndexOf(_selectedGroup);
            if (idx > 0)
            {
                col.Move(idx, idx - 1);
                for (int i = 0; i < col.Count; i++)
                {
                    col[i].SortOrder = i;
                }
                await _storage.SaveGroupsBatchAsync(col, _crypto);
                _selectedGroup.IsSelected = true;
                TxtStatusClipboard.Text = $"📁 Gruppo '{_selectedGroup.Name}' spostato su";
            }
        }

        private async void MenuMoveGroupDown_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null) return;
            if (_rootGroups.Count > 0 && _selectedGroup.Id == _rootGroups[0].Id) return;
            if (VaultOperationHandler.IsRecycleBinGroup(_selectedGroup)) return;

            var col = FindGroupParentCollection(_selectedGroup);
            if (col == null) return;

            int idx = col.IndexOf(_selectedGroup);
            if (idx >= 0 && idx < col.Count - 1)
            {
                col.Move(idx, idx + 1);
                for (int i = 0; i < col.Count; i++)
                {
                    col[i].SortOrder = i;
                }
                await _storage.SaveGroupsBatchAsync(col, _crypto);
                _selectedGroup.IsSelected = true;
                TxtStatusClipboard.Text = $"📁 Gruppo '{_selectedGroup.Name}' spostato giù";
            }
        }

        private async void MenuMoveGroupToRoot_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup == null) return;
            if (_rootGroups.Count == 0 || _selectedGroup.Id == _rootGroups[0].Id) return;
            if (VaultOperationHandler.IsRecycleBinGroup(_selectedGroup)) return;

            var rootGroup = _rootGroups[0];
            if (_selectedGroup.ParentId == rootGroup.Id)
            {
                TxtStatusClipboard.Text = $"Il gruppo '{_selectedGroup.Name}' è già al livello principale.";
                return;
            }

            await MoveGroupInsideAsync(_selectedGroup, rootGroup);
        }

        private static bool IsDescendantOf(PwGroup candidate, PwGroup root)
        {
            if (candidate.Id == root.Id) return true;
            foreach (var child in root.Children)
            {
                if (IsDescendantOf(candidate, child)) return true;
            }
            return false;
        }

        private ObservableCollection<PwGroup>? FindGroupParentCollection(PwGroup group)
        {
            if (_rootGroups.Contains(group))
                return _rootGroups;

            return FindInCollectionRecursive(_rootGroups, group);
        }

        private static ObservableCollection<PwGroup>? FindInCollectionRecursive(IEnumerable<PwGroup> groups, PwGroup target)
        {
            foreach (var g in groups)
            {
                if (g.Children.Contains(target))
                    return g.Children;

                var inSub = FindInCollectionRecursive(g.Children, target);
                if (inSub != null) return inSub;
            }
            return null;
        }

        private PwGroup? GetGroupByHitTest(System.Windows.Point point)
        {
            var hit = System.Windows.Media.VisualTreeHelper.HitTest(TvGroups, point);
            if (hit == null) return null;

            DependencyObject? current = hit.VisualHit;
            while (current != null && current != TvGroups)
            {
                if (current is TreeViewItem tvi && tvi.DataContext is PwGroup group)
                {
                    return group;
                }
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private TreeViewItem? GetTreeViewItemFromHitTest(System.Windows.Point point)
        {
            var hit = System.Windows.Media.VisualTreeHelper.HitTest(TvGroups, point);
            if (hit == null) return null;

            DependencyObject? current = hit.VisualHit;
            while (current != null && current != TvGroups)
            {
                if (current is TreeViewItem tvi)
                {
                    return tvi;
                }
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private (PwGroup? group, FrameworkElement? rowHeader) GetGroupAndHeaderFromHitTest(System.Windows.Point point)
        {
            var res = HitTestGroupAndHeader(point);
            if (res.group != null) return res;

            // Tolerance check for 1-3px gaps/margins between TreeView items to prevent flickering
            res = HitTestGroupAndHeader(new System.Windows.Point(point.X, Math.Max(0, point.Y - 3)));
            if (res.group != null) return res;

            res = HitTestGroupAndHeader(new System.Windows.Point(point.X, point.Y + 3));
            if (res.group != null) return res;

            return (null, null);
        }

        private (PwGroup? group, FrameworkElement? rowHeader) HitTestGroupAndHeader(System.Windows.Point point)
        {
            var hit = System.Windows.Media.VisualTreeHelper.HitTest(TvGroups, point);
            if (hit == null) return (null, null);

            DependencyObject? current = hit.VisualHit;
            FrameworkElement? foundHeader = null;

            while (current != null && current != TvGroups)
            {
                if (current is FrameworkElement fe && fe.Name == "GroupRowBorder")
                {
                    foundHeader = fe;
                }

                if (current is TreeViewItem tvi && tvi.DataContext is PwGroup group)
                {
                    if (foundHeader == null)
                    {
                        foundHeader = FindVisualChildByName<FrameworkElement>(tvi, "GroupRowBorder")
                                      ?? FindVisualChildByName<FrameworkElement>(tvi, "Bd")
                                      ?? (FrameworkElement)tvi;
                    }
                    return (group, foundHeader);
                }
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return (null, null);
        }

        private static T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && fe.Name == name)
                    return fe;

                var res = FindVisualChildByName<T>(child, name);
                if (res != null) return res;
            }
            return null;
        }

        private TreeViewDropAdorner? EnsureDropAdorner()
        {
            if (_dropAdorner != null) return _dropAdorner;

            var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(TvGroups);
            if (layer != null)
            {
                _dropAdorner = new TreeViewDropAdorner(TvGroups);
                layer.Add(_dropAdorner);
            }
            return _dropAdorner;
        }

        private void ClearDropAdorner()
        {
            _dropAdorner?.Clear();
        }

        private void TvGroups_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool isTrash = _selectedGroup != null && VaultOperationHandler.IsRecycleBinGroup(_selectedGroup);
            bool isRoot = _selectedGroup != null && _rootGroups.Count > 0 && _selectedGroup.Id == _rootGroups[0].Id;

            CmAddSubgroup.IsEnabled = !isTrash;
            CmRenameGroup.IsEnabled = !isTrash && !isRoot;
            CmDeleteGroup.IsEnabled = !isTrash && !isRoot;
            CmMoveGroupUp.IsEnabled = !isTrash && !isRoot;
            CmMoveGroupDown.IsEnabled = !isTrash && !isRoot;
            CmMoveGroupToRoot.IsEnabled = !isTrash && !isRoot;
            CmEmptyTrash.Visibility = isTrash ? Visibility.Visible : Visibility.Collapsed;
            CmTreeSep3.Visibility = isTrash ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

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

            try
            {
                var (movedToTrash, permanentlyDeleted) = await VaultOperationHandler.DeleteOrTrashEntryAsync(
                    _selectedEntry, _storage, _crypto, _rootGroups, this);

                if (movedToTrash)
                {
                    _entriesView.Refresh();
                    UpdatePreviewPanel();
                    UpdateStatusCounts();
                    TxtStatusClipboard.Text = $"🗑️ Voce '{_selectedEntry.Title}' spostata nel Cestino";
                }
                else if (permanentlyDeleted)
                {
                    _allEntries.Remove(_selectedEntry);
                    _entriesView.Refresh();
                    UpdatePreviewPanel();
                    UpdateStatusCounts();
                    TxtStatusClipboard.Text = "🗑️ Voce eliminata definitivamente dal vault";
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Delete/Trash entry failed", ex);
                MessageBox.Show($"Operazione non riuscita:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void MenuEmptyTrash_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int count = await VaultOperationHandler.EmptyRecycleBinAsync(_storage, _crypto, _rootGroups, _allEntries, this);
                if (count > 0)
                {
                    _entriesView.Refresh();
                    UpdatePreviewPanel();
                    UpdateStatusCounts();
                    TxtStatusClipboard.Text = $"🗑️ Cestino svuotato ({count} voci eliminate definitivamente)";
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Empty trash failed", ex);
                MessageBox.Show($"Impossibile svuotare il Cestino:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAddGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedGroup != null && VaultOperationHandler.IsRecycleBinGroup(_selectedGroup))
            {
                MessageBox.Show("Il Cestino non può contenere sottocartelle.", "Operazione non consentita", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
            if (VaultOperationHandler.IsRecycleBinGroup(_selectedGroup))
            {
                MessageBox.Show("Il Cestino di sistema non può essere rinominato.", "Avviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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

            if (_rootGroups.Count > 0 && _selectedGroup.Id == _rootGroups[0].Id)
            {
                MessageBox.Show("Non è possibile eliminare il gruppo radice del vault.", "Avviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (VaultOperationHandler.IsRecycleBinGroup(_selectedGroup))
            {
                MessageBox.Show("Il Cestino di sistema non può essere eliminato. Per eliminare le voci al suo interno, utilizza 'Svuota Cestino'.", "Avviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
                _clipboardCoordinator.CopyWithAutoClear(_selectedEntry.UserName, "User name");
            }
        }

        private void BtnCopyPassword_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry != null && !string.IsNullOrEmpty(_selectedEntry.Password))
            {
                _clipboardCoordinator.CopyWithAutoClear(_selectedEntry.Password, "Password");
            }
        }

        private void BtnClearClipboard_Click(object sender, RoutedEventArgs e)
        {
            _clipboardCoordinator.ClearImmediately();
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
            var entryToType = _selectedEntry;
            if (entryToType == null)
            {
                MessageBox.Show("Seleziona una voce per eseguire l'Auto-Type.", "Informazione", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(entryToType.UserName) && string.IsNullOrWhiteSpace(entryToType.Password))
            {
                MessageBox.Show("Questa voce non contiene credenziali (username o password) da digitare.", "Auto-Type", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isPerformingAutoType = true;
            try
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(400);

                await AutoTypeService.ExecuteAutoTypeAsync(entryToType, IntPtr.Zero, 250);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore durante l'esecuzione dell'Auto-Type:\n{ex.Message}", "Errore Auto-Type", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                await Task.Delay(250);
                _isPerformingAutoType = false;
            }
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
            LockVault(userInitiated: true);
        }

        private void LockVault(bool userInitiated)
        {
            if (_isLocked || _isLocking || _isPerformingAutoType) return;
            _isLocking = true;

            try
            {
                _crypto.Dispose();
                _clipboardCoordinator.ClearImmediately();
                _allEntries.Clear();
                _rootGroups.Clear();
                GridEntryPreview.Visibility = Visibility.Collapsed;
                _isLocked = true;

                // Force garbage collection to purge any plaintext strings from RAM
                GC.Collect();
                GC.WaitForPendingFinalizers();

                AppLogger.LogInfo($"Vault locked (userInitiated: {userInitiated})");

                if (userInitiated)
                {
                    PromptUnlock();
                }
            }
            finally
            {
                _isLocking = false;
            }
        }

        private void PromptUnlock()
        {
            if (!_isLocked) return;

            var login = new LoginWindow();
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                login.Owner = this;
            }

            bool? result = login.ShowDialog();
            if (result == true && login.AuthenticatedCrypto != null && login.StorageProvider != null && login.Metadata != null)
            {
                _storage = login.StorageProvider;
                _crypto = login.AuthenticatedCrypto;
                _metadata = login.Metadata;
                _isLocked = false;
                TxtStatusDb.Text = $"Vault: {Path.GetFileName(_storage.FilePath)} • Dual Backup: Attivo";

                Show();
                WindowState = WindowState.Normal;
                Activate();
                _ = RefreshDataAsync();
            }
            else
            {
                // User cancelled or closed the unlock dialog: stay safely locked in tray or minimized
                var settings = AppSettingsManager.Load();
                if (settings.MinimizeToTray)
                {
                    Hide();
                }
                else
                {
                    WindowState = WindowState.Minimized;
                }
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

                    if (imported.Count > 0)
                    {
                        await _storage.SaveEntriesBatchAsync(imported, _crypto);
                        foreach (var entry in imported)
                        {
                            _allEntries.Add(entry);
                        }
                        _entriesView.Refresh();
                        UpdateStatusCounts();
                    }

                    MessageBox.Show($"Importate con successo {imported.Count} voci da KeePass CSV!", "Importazione Riuscita", MessageBoxButton.OK, MessageBoxImage.Information);
                    AppLogger.LogInfo($"Imported {imported.Count} entries from KeePass CSV");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("KeePass CSV import failed", ex);
                    MessageBox.Show($"Importazione fallita:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void MenuImportBitwardenJson_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Importa Vault Bitwarden (JSON)",
                Filter = "File JSON Bitwarden (*.json)|*.json|Tutti i file (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string content = File.ReadAllText(ofd.FileName);
                    var targetGroupId = _selectedGroup?.Id ?? _rootGroups.FirstOrDefault()?.Id ?? Guid.NewGuid();
                    var imported = ImportExportService.ImportFromBitwardenJson(content, targetGroupId);

                    if (imported.Count > 0)
                    {
                        await _storage.SaveEntriesBatchAsync(imported, _crypto);
                        foreach (var entry in imported)
                        {
                            _allEntries.Add(entry);
                        }
                        _entriesView.Refresh();
                        UpdateStatusCounts();
                    }

                    MessageBox.Show($"Importate con successo {imported.Count} voci da Bitwarden JSON!", "Importazione Riuscita", MessageBoxButton.OK, MessageBoxImage.Information);
                    AppLogger.LogInfo($"Imported {imported.Count} entries from Bitwarden JSON");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Bitwarden JSON import failed", ex);
                    MessageBox.Show($"Importazione fallita:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void MenuImportBrowserCsv_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Importa Password dal Browser (Chrome / Edge / Firefox CSV)",
                Filter = "File CSV Password (*.csv)|*.csv|Tutti i file (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    string content = File.ReadAllText(ofd.FileName);
                    var targetGroupId = _selectedGroup?.Id ?? _rootGroups.FirstOrDefault()?.Id ?? Guid.NewGuid();
                    var imported = ImportExportService.ImportFromBrowserCsv(content, targetGroupId);

                    if (imported.Count > 0)
                    {
                        await _storage.SaveEntriesBatchAsync(imported, _crypto);
                        foreach (var entry in imported)
                        {
                            _allEntries.Add(entry);
                        }
                        _entriesView.Refresh();
                        UpdateStatusCounts();
                    }

                    MessageBox.Show($"Importate con successo {imported.Count} voci dal Browser CSV!", "Importazione Riuscita", MessageBoxButton.OK, MessageBoxImage.Information);
                    AppLogger.LogInfo($"Imported {imported.Count} entries from Browser CSV");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Browser CSV import failed", ex);
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

        private void MenuToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            var settings = AppSettingsManager.Load();
            settings.DarkMode = !settings.DarkMode;
            ThemeManager.ApplyTheme(settings.DarkMode);
            AppSettingsManager.Save(settings);
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
