using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI;

public partial class MainWindow
{
    private void CloseSensitiveDialogs()
    {
        foreach (Window owned in OwnedWindows.Cast<Window>().ToArray())
        {
            SensitiveWindowService.ClearControls(owned);
            if (owned is SQLitePass.UI.Dialogs.ChangeMasterPasswordDialog change) { change.CancelForLock(); continue; }
            if (owned is SQLitePass.UI.Dialogs.EntryEditDialog editor) { editor.CancelForLock(); continue; }
            owned.Visibility = Visibility.Hidden; owned.Close();
        }
    }
    private bool IsInTrash(PwEntry entry) => entry.GroupId == _metadata.RecycleBinGroupId;
    private void StorageBackupStatusChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(UpdateBackupStatus);
    }
    private void UpdateBackupStatus()
    {
        string status = _storage.IsReadOnly ? SQLitePass.UI.Services.LocalizationManager.GetString("BackupReadOnly") :
            !_storage.BackupEnabled ? SQLitePass.UI.Services.LocalizationManager.GetString("BackupDisabled") :
            _storage.LastBackupError != null ? SQLitePass.UI.Services.LocalizationManager.GetString("BackupFailed") :
            _storage.LastBackupAt.HasValue ? SQLitePass.UI.Services.LocalizationManager.GetString("BackupVerifiedAt", _storage.LastBackupAt.Value.ToLocalTime()) :
            SQLitePass.UI.Services.LocalizationManager.GetString("BackupNotChecked");
        TxtStatusDb.Text = $"{System.IO.Path.GetFileName(_storage.FilePath)} · {status}";
        TxtStatusDb.ToolTip = status;
    }
    private void SetVaultSurfaceEnabled(bool enabled)
    {
        foreach (UIElement child in VaultSurface.Children) if (!ReferenceEquals(child, LockedOverlay)) child.IsEnabled = enabled;
        LockedOverlay.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
    }
    private void BtnUnlock_Click(object sender, RoutedEventArgs e) => PromptUnlock();
    internal bool LockAfterUnexpectedError()
    {
        LockVault(userInitiated: false);
        return _isLocked;
    }
    private PwEntry? ReadDraggedEntry(DragEventArgs e) =>
        e.Data.GetData("SQLitePass.EntryId", false) is string id && Guid.TryParse(id, out var key) ? _visibleEntries.FirstOrDefault(entry => entry.Id == key) : null;
    private PwGroup? ReadDraggedGroup(DragEventArgs e)
    {
        if (e.Data.GetData("SQLitePass.GroupId", false) is not string id || !Guid.TryParse(id, out var key)) return null;
        PwGroup? Find(IEnumerable<PwGroup> groups)
        {
            foreach (var group in groups) { if (group.Id == key) return group; var nested = Find(group.Children); if (nested != null) return nested; }
            return null;
        }
        return Find(_rootGroups);
    }

    private Dictionary<Guid, string> GroupPaths()
    {
        var result = new Dictionary<Guid, string>();
        void Visit(IEnumerable<PwGroup> groups, string parent)
        {
            foreach (var group in groups)
            {
                string path = string.IsNullOrEmpty(parent) ? group.Name : parent + "/" + group.Name;
                result.Add(group.Id, path); Visit(group.Children, path);
            }
        }
        Visit(_rootGroups, ""); return result;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (_isLocked || _isLocking || e.Handled) return;
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None && _refreshCancellation != null)
        { _refreshCancellation.Cancel(); e.Handled = true; return; }
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        { e.Handled = true; MenuRefresh_Click(this, new RoutedEventArgs()); return; }
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        Action? command = e.Key switch
        {
            Key.I => () => BtnAddEntry_Click(this, new RoutedEventArgs()),
            Key.G => () => BtnAddGroup_Click(this, new RoutedEventArgs()),
            Key.L => () => BtnLock_Click(this, new RoutedEventArgs()),
            Key.M => () => MenuChangeMasterPassword_Click(this, new RoutedEventArgs()),
            Key.W => () => BtnPasswordGenerator_Click(this, new RoutedEventArgs()),
            Key.H => () => MenuTogglePasswords_Click(this, new RoutedEventArgs()),
            Key.Up when Keyboard.FocusedElement is not TextBoxBase => () => MenuMoveGroupUp_Click(this, new RoutedEventArgs()),
            Key.Down when Keyboard.FocusedElement is not TextBoxBase => () => MenuMoveGroupDown_Click(this, new RoutedEventArgs()),
            Key.V when Keyboard.FocusedElement is not TextBoxBase and not PasswordBox => () => BtnAutoType_Click(this, new RoutedEventArgs()),
            _ => null
        };
        if (command == null) return;
        e.Handled = true; command();
    }
}
