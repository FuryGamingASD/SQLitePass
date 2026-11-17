using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
        private readonly ObservableCollection<PwEntry> _visibleEntries = new();
        private ICollectionView _entriesView;

        private PwGroup? _selectedGroup;
        private PwEntry? _selectedEntry;

        private readonly ClipboardCoordinator _clipboardCoordinator;
        private System.Windows.Point _dragStartPoint;
        private System.Windows.Point _groupDragStartPoint;
        private PwGroup? _dragCandidateGroup;
        private TreeViewDropAdorner? _dropAdorner;

        private SystemTrayCoordinator? _trayCoordinator;
        private GlobalHotkeyCoordinator? _hotkeyCoordinator;
        private InactivityLockCoordinator? _inactivityCoordinator;

        private bool _previewPasswordRevealed = false;
        private bool _showPasswordColumn = false;
        private bool _isExplicitShutdown = false;

        private bool _isLocked = false;
        private bool _isLocking = false;
        private bool _isPerformingAutoType = false;
        private System.Threading.CancellationTokenSource _autoTypeCancellation = new();
        private PwEntry? _pendingAutoTypeEntry;
        private int _sessionGeneration;
        private bool _unlockPromptOpen;
        private bool _refreshing;
        private bool _refreshAgain;
        private Guid? _entryToSelectAfterRefresh;
        private CancellationTokenSource? _refreshCancellation;

        private readonly bool _ownsSession;
        public MainWindow(SqliteStorageProvider storage, CryptoEngine crypto, VaultMetadata metadata) : this(storage, crypto, metadata, true) { }
        internal MainWindow(SqliteStorageProvider storage, CryptoEngine crypto, VaultMetadata metadata, bool enableDesktopIntegration)
        {
            _ownsSession = enableDesktopIntegration;
            InitializeComponent();
            InitializeSensitiveReveals();
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            InitializeViewPreferences();

            _clipboardCoordinator = ClipboardCoordinator.Shared;
            _clipboardCoordinator.SetStatusCallback(msg => TxtStatusClipboard.Text = msg);

            TvGroups.ItemsSource = _rootGroups;

            _entriesView = CollectionViewSource.GetDefaultView(_visibleEntries);
            _entriesView.Filter = FilterEntry;
            DgEntries.ItemsSource = _entriesView;
            InitializeEntryBrowsing();

            UpdateBackupStatus();
            _storage.BackupStatusChanged += StorageBackupStatusChanged;

            if (enableDesktopIntegration)
            {
            _trayCoordinator = new SystemTrayCoordinator(
                ShowAndRestoreWindow,
                () => Dispatcher.Invoke(() => BtnPasswordGenerator_Click(this, new RoutedEventArgs())),
                () => Dispatcher.Invoke(() => BtnLock_Click(this, new RoutedEventArgs())),
                () => Dispatcher.Invoke(MenuExit_Click, this, new RoutedEventArgs())
            );

            _hotkeyCoordinator = new GlobalHotkeyCoordinator(this, (foregroundWin, winTitle) =>
            {
                Dispatcher.Invoke(() => HandleGlobalAutoType(foregroundWin, winTitle));
            }, () => TxtOperationStatus.Text = LocalizationManager.GetString("HotkeyUnavailableStatus"));

            _inactivityCoordinator = new InactivityLockCoordinator(
                () => Dispatcher.Invoke(() => LockVault(userInitiated: false)),
                () => !_isLocked && !_isLocking
            );
            }

            if (enableDesktopIntegration) Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
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

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_ownsSession)
            {
                _isLocked = true; _sessionGeneration++; _refreshAgain = false;
                _refreshCancellation?.Cancel();
                HideRevealedSecrets(); CloseSensitiveDialogs();
                ResetEntryBrowsing();
                foreach (var entry in _visibleEntries) entry.ClearSecrets();
                _visibleEntries.Clear(); _storage.BackupStatusChanged -= StorageBackupStatusChanged;
                _clipboardCoordinator.SetStatusCallback(_ => { }); return;
            }
            if (!_isExplicitShutdown)
            {
                var settings = AppSettingsManager.Load();
                if (settings.CloseToTray)
                {
                    e.Cancel = true;
                    if (settings.AutoLockOnMinimize) LockVault(userInitiated: false);
                    Hide();
                    _trayCoordinator?.ShowBalloonTip("SQLitePass", LocalizationManager.GetString("TrayStillRunning"), Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                    return;
                }
            }

            _isLocked = true; _sessionGeneration++; _refreshAgain = false; ResetEntryBrowsing();
            HideRevealedSecrets(); CloseSensitiveDialogs();
            _autoTypeCancellation.Cancel(); _importCancellation?.Cancel(); _refreshCancellation?.Cancel(); _entryDetailsCancellation?.Cancel(); _exportCancellation?.Cancel(); FlushViewPreferences();
            _storage.BackupStatusChanged -= StorageBackupStatusChanged;
            foreach (var entry in _visibleEntries) entry.ClearSecrets();
            _storage.Dispose();
            _inactivityCoordinator?.Dispose();
            _clipboardCoordinator.Dispose();
            _trayCoordinator?.Dispose();
            _trayCoordinator = null;
        }

        protected override void OnClosed(EventArgs e)
        {
            _previewReveal.Dispose(); _columnReveal.Dispose();
            _hotkeyCoordinator?.Dispose();
            _hotkeyCoordinator = null;
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

        private async void HandleGlobalAutoType(IntPtr foregroundWin, string winTitle)
        {
            if (_isLocked || _isLocking || _isPerformingAutoType || foregroundWin == new WindowInteropHelper(this).Handle) return;
            int generation = _sessionGeneration;
            var storage = _storage; var crypto = _crypto; var pending = _pendingAutoTypeEntry;
            _pendingAutoTypeEntry = null;
            _isPerformingAutoType = true;
            _autoTypeCancellation.Dispose(); _autoTypeCancellation = new();
            var cancellation = _autoTypeCancellation;
            List<PwEntry>? candidates = null;
            try
            {
                candidates = await storage.FindAutoTypeCandidatesAsync(crypto, winTitle, pending?.Id, pending?.Revision, cancellation.Token);
                if (_isLocked || generation != _sessionGeneration) return;
                if (candidates.Count != 1)
                {
                    MessageBox.Show(this, LocalizationManager.GetString("AutoTypeNoUniqueMatch"), LocalizationManager.GetString("MenuAutoType"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var selected = candidates[0];
                if (new AutoTypeConfirmDialog(selected.Title, winTitle) { Owner = this }.ShowDialog() != true || _isLocked || generation != _sessionGeneration) return;
                // A confirmation must not authorize credentials from a revision changed meanwhile.
                var confirmed = await storage.LoadAutoTypeEntryAsync(selected.Id, selected.Revision, crypto, cancellation.Token);
                foreach (var entry in candidates) entry.ClearSecrets();
                candidates = [confirmed];
                if (_isLocked || generation != _sessionGeneration || candidates.Count != 1) return;
                await AutoTypeService.ExecuteAutoTypeAsync(candidates[0], foregroundWin, 250, cancellation.Token, expectedTitle: winTitle);
            }
            catch (OperationCanceledException)
            {
                if (!_isLocked && generation == _sessionGeneration) TxtOperationStatus.Text = LocalizationManager.GetString("AutoTypeStopped");
            }
            catch (Exception exception)
            {
                if (!_isLocked && generation == _sessionGeneration) MessageBox.Show(this, UiErrorMessages.GetText(exception), LocalizationManager.GetString("AutoTypeStopped"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (candidates != null) foreach (var entry in candidates) entry.ClearSecrets();
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

            await Task.Delay(3000);
            if (!_isLocked) await UpdateService.CheckForUpdatesAsync(silent: true);
        }

        private async Task RefreshDataAsync() => await RefreshEntryPageAsync(resetPage: true);

        private bool FilterEntry(object item) => !_isLocked && item is PwEntry;

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ScheduleEntryQuery(debounce: true);

        private void TvGroups_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            Guid? old = _selectedGroup?.Id;
            _selectedGroup = e.NewValue as PwGroup;
            if (!_refreshing && old != _selectedGroup?.Id) ScheduleEntryQuery(debounce: false);
        }

        private void TvGroups_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                if (_selectedGroup != null && !VaultOperationHandler.IsRecycleBinGroup(_selectedGroup))
                {
                    MenuRenameGroup_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
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
            CmRestore.Visibility = hasSelection && IsInTrash(_selectedEntry!) ? Visibility.Visible : Visibility.Collapsed;
            CmSep2.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateStatusCounts()
        {
            UpdateEntryPager();
        }

        private void UpdatePreviewPanel()
        {
            _previewReveal.Hide();
            if (_selectedEntry == null)
            {
                PrevTitle.Text = PrevUserName.Text = PrevPassword.Text = PrevUrl.Text = PrevNotes.Text = PrevModified.Text = string.Empty;
                GridEntryPreview.Visibility = Visibility.Collapsed;
                return;
            }

            GridEntryPreview.Visibility = Visibility.Visible;
            PrevTitle.Text = UiText.Preview(_selectedEntry.Title);
            PrevUserName.Text = UiText.Preview(_selectedEntry.UserName, 512);
            _previewPasswordRevealed = false;
            PrevPassword.Text = new string('•', Math.Min(_selectedEntry.Password.Length, 14));

            bool scored = _selectedEntry.Password.Length <= UiText.MaximumScoredPasswordLength;
            double entropy = scored ? PasswordGenerator.CalculateEntropy(_selectedEntry.Password) : 0;
            PrevPbQuality.Value = Math.Min(entropy, 100); PrevPbQuality.Visibility = scored ? Visibility.Visible : Visibility.Collapsed;
            PrevEntropyText.Text = scored ? LocalizationManager.GetString("QualityScore", entropy) : LocalizationManager.GetString("QualityNotEstimated");

            PrevUrl.Text = UiText.Preview(_selectedEntry.Url, 512);
            PrevModified.Text = _selectedEntry.UpdatedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
            PrevNotes.Text = string.IsNullOrEmpty(_selectedEntry.Notes) ? LocalizationManager.GetString("NoNotes") : UiText.Preview(_selectedEntry.Notes, 16384);
        }

        private void BtnPreviewTogglePassword_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedEntry == null) return;
            _previewReveal.Toggle();
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
                string? header = cell.Column.SortMemberPath;
                if (string.Equals(header, "Password", StringComparison.OrdinalIgnoreCase))
                {
                    BtnCopyPassword_Click(sender, e);
                    e.Handled = true;
                    return;
                }
                else if (string.Equals(header, "UserName", StringComparison.OrdinalIgnoreCase))
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

        private void DgEntries_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                if (_selectedEntry != null)
                {
                    BtnDeleteEntry_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Enter)
            {
                if (_selectedEntry != null)
                {
                    BtnEditEntry_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
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
                        DragDrop.DoDragDrop(DgEntries, new DataObject("SQLitePass.EntryId", _selectedEntry.Id.ToString()), DragDropEffects.Move);
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
                    if (_dragCandidateGroup.IsRoot)
                        return;
                    if (VaultOperationHandler.IsRecycleBinGroup(_dragCandidateGroup))
                        return;

                    try
                    {
                        DragDrop.DoDragDrop(TvGroups, new DataObject("SQLitePass.GroupId", _dragCandidateGroup.Id.ToString()), DragDropEffects.Move);
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

            if (ReadDraggedEntry(e) != null)
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
            else if (ReadDraggedGroup(e) != null)
            {
                if (ReadDraggedGroup(e) is PwGroup draggedGroup)
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

                    bool isRoot = targetGroup.IsRoot;
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
            int generation = _sessionGeneration;
            if (_isLocked || _isLocking) return;
            ClearDropAdorner();

            if (ReadDraggedEntry(e) is PwEntry draggedEntry)
            {
                var (targetGroup, _) = GetGroupAndHeaderFromHitTest(e.GetPosition(TvGroups));

                if (targetGroup != null && draggedEntry.GroupId != targetGroup.Id)
                {
                    try
                    {
                        long revision = await _storage.MoveEntryAsync(draggedEntry.Id, draggedEntry.Revision, targetGroup.Id, _crypto);
                        if (_isLocked || generation != _sessionGeneration) return;
                        draggedEntry.GroupId = targetGroup.Id; draggedEntry.Revision = revision;
                        await RefreshDataAsync();
                        if (_isLocked || generation != _sessionGeneration) return;

                        if (VaultOperationHandler.IsRecycleBinGroup(targetGroup))
                        {
                            TxtOperationStatus.Text = LocalizationManager.GetString("EntryTrashed");
                        }
                        else
                        {
                            TxtOperationStatus.Text = LocalizationManager.GetString("OperationComplete");
                        }
                        AppLogger.LogInfo("Vault organization updated.");
                    }
                    catch (Exception ex)
                    {
                        _ = RefreshDataAsync();
                    AppLogger.LogError("Drag & drop move entry failed", ex);
                        ShowGroupError(ex, generation);
                    }
                }
            }
            else if (ReadDraggedGroup(e) is PwGroup draggedGroup)
            {
                Point mousePt = e.GetPosition(TvGroups);
                var (targetGroup, targetHeader) = GetGroupAndHeaderFromHitTest(mousePt);

                // Drop onto empty area -> Move to root
                if (targetGroup == null || targetHeader == null)
                {
                    var rootParent = _rootGroups.FirstOrDefault(g => g.IsRoot);
                    if (rootParent != null && draggedGroup.ParentId != rootParent.Id && draggedGroup.Id != rootParent.Id)
                    {
                        await MoveGroupInsideAsync(draggedGroup, rootParent);
                        if (_isLocked || generation != _sessionGeneration) return;
                    }
                    return;
                }

                // Prevent dropping onto self or descendant
                if (targetGroup.Id == draggedGroup.Id || IsDescendantOf(targetGroup, draggedGroup))
                    return;

                bool isRoot = targetGroup.IsRoot;
                bool isTrash = VaultOperationHandler.IsRecycleBinGroup(targetGroup);

                double h = targetHeader.ActualHeight > 0 ? targetHeader.ActualHeight : 26;
                Point p = targetHeader.TranslatePoint(new Point(0, 0), TvGroups);
                double relY = (mousePt.Y - p.Y) / h;

                if (relY < 0.28 && !isRoot)
                {
                    // Insert BEFORE target
                    await MoveGroupRelativeAsync(draggedGroup, targetGroup, insertBefore: true);
                    if (_isLocked || generation != _sessionGeneration) return;
                }
                else if (relY > 0.72 && !isRoot)
                {
                    // Insert AFTER target
                    await MoveGroupRelativeAsync(draggedGroup, targetGroup, insertBefore: false);
                    if (_isLocked || generation != _sessionGeneration) return;
                }
                else
                {
                    // Move INSIDE target
                    if (isTrash)
                    {
                        MessageBox.Show(LocalizationManager.GetString("FolderNoChildren"), LocalizationManager.GetString("WarningTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    await MoveGroupInsideAsync(draggedGroup, targetGroup);
                    if (_isLocked || generation != _sessionGeneration) return;
                }
            }
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
            bool isRoot = _selectedGroup != null && _selectedGroup.IsRoot;

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

        private async void BtnDeleteEntry_Click(object sender, RoutedEventArgs e)
        {
            int generation = _sessionGeneration;
            if (_isLocked || _isLocking) return;
            if (_selectedEntry == null) return;

            try
            {
                var deletedEntry = _selectedEntry;
                int index = _visibleEntries.IndexOf(deletedEntry);
                Guid? adjacent = index + 1 < _visibleEntries.Count ? _visibleEntries[index + 1].Id : index > 0 ? _visibleEntries[index - 1].Id : null;
                var (movedToTrash, permanentlyDeleted) = await VaultOperationHandler.DeleteOrTrashEntryAsync(
                    deletedEntry, _storage, _crypto, _rootGroups, this);
                if (_isLocked || generation != _sessionGeneration) return;

                if (movedToTrash || permanentlyDeleted)
                {
                    _entryToSelectAfterRefresh = adjacent;
                    DgEntries.SelectedItem = null; deletedEntry.ClearSecrets();
                    await RefreshDataAsync();
                    if (!_isLocked && generation == _sessionGeneration) TxtOperationStatus.Text = LocalizationManager.GetString(movedToTrash ? "EntryTrashed" : "EntryDeleted");
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Delete/Trash entry failed", ex);
                MessageBox.Show(SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("OperationFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void MenuEmptyTrash_Click(object sender, RoutedEventArgs e)
        {
            int generation = _sessionGeneration;
            if (_isLocked || _isLocking) return;
            try
            {
                int count = await VaultOperationHandler.EmptyRecycleBinAsync(_storage, _crypto, _rootGroups, this);
                if (count > 0)
                {
                    if (_isLocked || generation != _sessionGeneration) return;
                    await RefreshDataAsync();
                    if (_isLocked || generation != _sessionGeneration) return;
                    TxtOperationStatus.Text = LocalizationManager.GetString("TrashCleared", count);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Empty trash failed", ex);
                MessageBox.Show(SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("OperationFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAddGroup_Click(object sender, RoutedEventArgs e)
        {
            int generation = _sessionGeneration;
            if (_isLocked || _isLocking) return;
            if (_selectedGroup != null && VaultOperationHandler.IsRecycleBinGroup(_selectedGroup))
            {
                MessageBox.Show(LocalizationManager.GetString("FolderNoChildren"), LocalizationManager.GetString("WarningTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Guid? parentId = _selectedGroup?.Id ?? _metadata.RootGroupId;
            var dlg = new GroupEditDialog() { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                if (_isLocked || generation != _sessionGeneration) return;
                var newGroup = new PwGroup
                {
                    Name = dlg.GroupName,
                    ParentId = parentId
                };

                try
                {
                    await _storage.SaveGroupAsync(newGroup, _crypto);
                    if (_isLocked || generation != _sessionGeneration) return;
                    await RefreshDataAsync();
                }
                catch (Exception ex)
                {
                    ShowGroupError(ex, generation);
                }
            }
        }

        private async void MenuRenameGroup_Click(object sender, RoutedEventArgs e)
        {
            int generation = _sessionGeneration;
            if (_isLocked || _isLocking) return;
            if (_selectedGroup == null) return;
            if (VaultOperationHandler.IsRecycleBinGroup(_selectedGroup))
            {
                MessageBox.Show(LocalizationManager.GetString("ProtectedGroup"), LocalizationManager.GetString("WarningTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selected = _selectedGroup; var dlg = new GroupEditDialog(selected.Name) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                if (_isLocked || generation != _sessionGeneration) return;
                var draft = selected.DeepClone(); draft.Name = dlg.GroupName;
                try
                {
                    await _storage.SaveGroupAsync(draft, _crypto);
                    if (_isLocked || generation != _sessionGeneration) return;
                    await RefreshDataAsync();
                }
                catch (Exception ex)
                {
                    ShowGroupError(ex, generation);
                }
            }
        }

        private async void MenuDeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            int generation = _sessionGeneration;
            if (_isLocked || _isLocking) return;
            if (_selectedGroup == null) return;

            if (_selectedGroup.IsRoot)
            {
                MessageBox.Show(LocalizationManager.GetString("ProtectedGroup"), LocalizationManager.GetString("WarningTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (VaultOperationHandler.IsRecycleBinGroup(_selectedGroup))
            {
                MessageBox.Show(LocalizationManager.GetString("ProtectedGroup"), LocalizationManager.GetString("WarningTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selected = _selectedGroup;
            if (MessageBox.Show(LocalizationManager.GetString("FolderDeleteConfirm"), LocalizationManager.GetString("MenuDeleteGroup"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                try
                {
                    if (_isLocked || generation != _sessionGeneration) return;
                    await _storage.TrashGroupAsync(selected.Id, _crypto);
                    if (_isLocked || generation != _sessionGeneration) return;
                    await RefreshDataAsync();
                    if (_isLocked || generation != _sessionGeneration) return;
                }
                catch (Exception ex)
                {
                    ShowGroupError(ex, generation);
                }
            }
        }

        private void BtnCopyUsername_Click(object sender, RoutedEventArgs e)
        {
            if (_isLocked || _isLocking) return;
            if (_selectedEntry != null && !string.IsNullOrEmpty(_selectedEntry.UserName))
            {
                _clipboardCoordinator.CopyWithAutoClear(_selectedEntry.UserName, LocalizationManager.GetString("ColUserName"));
            }
        }

        private void BtnCopyPassword_Click(object sender, RoutedEventArgs e)
        {
            if (_isLocked || _isLocking) return;
            if (_selectedEntry != null && !string.IsNullOrEmpty(_selectedEntry.Password))
            {
                _clipboardCoordinator.CopyWithAutoClear(_selectedEntry.Password, LocalizationManager.GetString("ColPassword"));
            }
        }

        private void BtnClearClipboard_Click(object sender, RoutedEventArgs e)
        {
            _clipboardCoordinator.ClearImmediately();
        }

        private void BtnOpenUrl_Click(object sender, RoutedEventArgs e)
        {
            if (_isLocked || _isLocking) return;
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
                MessageBox.Show(SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("OperationFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnAutoType_Click(object sender, RoutedEventArgs e)
        {
            if (_isLocked || _selectedEntry == null || !_selectedEntry.AutoTypeEnabled || IsInTrash(_selectedEntry)) return;
            if (_hotkeyCoordinator?.IsRegistered != true)
            {
                MessageBox.Show(this, LocalizationManager.GetString("HotkeyUnavailableMessage"), LocalizationManager.GetString("AutoTypeUnavailable"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _pendingAutoTypeEntry = _selectedEntry;
            MessageBox.Show(this, LocalizationManager.GetString("AutoTypePrepare"), LocalizationManager.GetString("MenuAutoType"), MessageBoxButton.OK, MessageBoxImage.Information);
            WindowState = WindowState.Minimized;
        }

        private void BtnPasswordGenerator_Click(object sender, RoutedEventArgs e)
        {
            var gen = new PasswordGeneratorDialog { Owner = this };
            gen.ShowDialog();
        }

        private async void MenuChangeMasterPassword_Click(object sender, RoutedEventArgs e)
        {
            int generation = _sessionGeneration;
            if (_isLocked) return;

            var dlg = new ChangeMasterPasswordDialog(_crypto, _metadata) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.NewCrypto != null)
            {
                try
                {
                    var oldCrypto = _crypto;
                    var newCrypto = dlg.NewCrypto;

                    await _storage.ChangeMasterKeyAsync(
                        oldCrypto,
                        newCrypto,
                        dlg.NewSaltBase64,
                        _metadata.KdfIterations,
                        _metadata.KdfMemoryKb,
                        _metadata.KdfParallelism,
                        dlg.EncryptUsernameUrl,
                        dlg.UsesKeyFile
                    );

                    if (_isLocked || generation != _sessionGeneration) return;

                    // Update memory state
                    if (_isLocked) { newCrypto.Dispose(); return; }
                    _crypto = newCrypto;
                    _metadata = await _storage.LoadVaultMetadataAsync() ?? throw new InvalidDataException("Header mancante dopo il cambio chiave.");
                    oldCrypto.Dispose();

                    // Refresh in-memory data
                    await RefreshDataAsync();
                    if (_isLocked || generation != _sessionGeneration) return;

                    MessageBox.Show(this, LocalizationManager.GetString("CredentialChanged"), LocalizationManager.GetString("MasterChangeTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    if (!ReferenceEquals(_crypto, dlg.NewCrypto)) dlg.NewCrypto.Dispose();
                    AppLogger.LogError("Failed to change master password", ex);
                    ShowGroupError(ex, generation);
                }
            }
        }

        private async void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_isLocked || _isLocking) return;
            int generation = _sessionGeneration; var storage = _storage;
            var dlg = new SettingsDialog { Owner = this };
            if (dlg.ShowDialog() == true && !_isLocked && generation == _sessionGeneration)
            {
                var settings = AppSettingsManager.Load();
                Topmost = settings.AlwaysOnTop;
                foreach (Window window in Application.Current.Windows) WindowSecurityService.ApplyProtection(window, settings.ProtectScreenCapture);
                await storage.ConfigureBackupAsync(settings.EnableDualFileBackup, settings.SecondaryBackupDir);
                if (_isLocked || generation != _sessionGeneration) return;
                UpdateBackupStatus();
            }
        }

        private void BtnLock_Click(object sender, RoutedEventArgs e)
        {
            LockVault(userInitiated: true);
        }

        private Task _lockCompletion = Task.CompletedTask;
        private void LockVault(bool userInitiated)
        {
            if (_isLocked || _isLocking) return;
            _lockCompletion = LockVaultAsync(userInitiated);
        }

        private async Task LockVaultAsync(bool userInitiated)
        {
            if (_isLocked || _isLocking) return;
            _isLocking = true; _isLocked = true; _sessionGeneration++; ResetEntryBrowsing();
            HideRevealedSecrets();
            _autoTypeCancellation.Cancel(); _importCancellation?.Cancel(); _refreshCancellation?.Cancel(); _entryDetailsCancellation?.Cancel(); _exportCancellation?.Cancel();
            _pendingAutoTypeEntry = null; _entryToSelectAfterRefresh = null; _refreshAgain = false;
            try
            {
                CloseSensitiveDialogs();
                foreach (var entry in _visibleEntries) entry.ClearSecrets();
                _visibleEntries.Clear(); _rootGroups.Clear(); _selectedEntry = null; _selectedGroup = null;
                _dragCandidateGroup = null; ClearDropAdorner();
                UpdatePreviewPanel(); TxtSearch.Clear(); _clipboardCoordinator.ClearImmediately();
                TxtStatusClipboard.Text = TxtOperationStatus.Text = string.Empty;
                _showPasswordColumn = false;
                ColPassword.Binding = new Binding("Password") { Converter = (IValueConverter)Resources["PasswordMaskConverter"] };
                SetVaultSurfaceEnabled(false);
                LockedOverlay.IsEnabled = false;
                _storage.BackupStatusChanged -= StorageBackupStatusChanged;
                await _storage.CloseAsync();
            }
            finally { _isLocking = false; LockedOverlay.IsEnabled = true; }
            if (userInitiated) PromptUnlock();
        }

        private void PromptUnlock()
        {
            if (!_isLocked || _isLocking || _unlockPromptOpen) return;
            _unlockPromptOpen = true;
            var login = new LoginWindow();
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                login.Owner = this;
            }

            bool? result;
            try { result = login.ShowDialog(); }
            finally { _unlockPromptOpen = false; }
            if (result == true && login.AuthenticatedCrypto != null && login.StorageProvider != null && login.Metadata != null)
            {
                _storage = login.StorageProvider;
                _crypto = login.AuthenticatedCrypto;
                _metadata = login.Metadata;
                _isLocked = false;
                SetVaultSurfaceEnabled(true);
                UpdateBackupStatus();
            _storage.BackupStatusChanged += StorageBackupStatusChanged;

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
            if (_isLocked || _isLocking) return;
            _columnReveal.Toggle();
        }

        private async void MenuRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_isLocked || _isLocking) return;
            await RefreshDataAsync();
        }

        private void MenuToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            try { var settings = AppSettingsManager.Update(s => s.DarkMode = !s.DarkMode); ThemeManager.ApplyTheme(settings.DarkMode); }
            catch (IOException ex) { MessageBox.Show(this, SQLitePass.UI.Services.UiErrorMessages.GetText(ex), LocalizationManager.GetString("ThemeSaveFailed"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async void MenuCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await UpdateService.CheckForUpdatesAsync(silent: false, beforeRestart: async () => { LockVault(userInitiated: false); await _lockCompletion; });
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, LocalizationManager.GetString("AboutText", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!), LocalizationManager.GetString("AppTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
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
