using System.Windows;
using SQLitePass.Core.Models;
using SQLitePass.UI.Services;

namespace SQLitePass.UI;

public partial class MainWindow
{
    private bool _groupCommandBusy;
    private async Task MoveGroupInsideAsync(PwGroup group, PwGroup target)
    {
        if (_isLocked || _isLocking || _groupCommandBusy) return;
        if (target.IsRecycleBin || group.IsRoot || group.IsRecycleBin) return;
        _groupCommandBusy = true; int generation = _sessionGeneration;
        var storage = _storage; var crypto = _crypto;
        try
        {
            var draft = group.DeepClone(); draft.ParentId = target.Id;
            draft.SortOrder = target.Children.Count == 0 ? 0 : checked(target.Children.Max(child => child.SortOrder) + 1);
            await storage.SaveGroupAsync(draft, crypto);
            if (_isLocked || generation != _sessionGeneration) return;
            await RefreshDataAsync();
        }
        catch (Exception ex) { ShowGroupError(ex, generation); }
        finally { _groupCommandBusy = false; }
    }

    private async Task MoveGroupRelativeAsync(PwGroup group, PwGroup reference, bool insertBefore)
    {
        if (_isLocked || _isLocking || _groupCommandBusy || group.Id == reference.Id || group.IsRoot || group.IsRecycleBin) return;
        var siblings = FindGroupParentCollection(reference); if (siblings == null) return;
        _groupCommandBusy = true; int generation = _sessionGeneration;
        var storage = _storage; var crypto = _crypto;
        try
        {
            var drafts = siblings.Where(item => item.Id != group.Id).Select(item => item.DeepClone()).ToList();
            int index = drafts.FindIndex(item => item.Id == reference.Id); if (index < 0) return;
            var moved = group.DeepClone(); moved.ParentId = reference.ParentId; drafts.Insert(insertBefore ? index : index + 1, moved);
            for (int i = 0; i < drafts.Count; i++) drafts[i].SortOrder = i;
            await storage.SaveGroupsBatchAsync(drafts, crypto);
            if (_isLocked || generation != _sessionGeneration) return;
            await RefreshDataAsync();
        }
        catch (Exception ex) { ShowGroupError(ex, generation); }
        finally { _groupCommandBusy = false; }
    }

    private async void MenuMoveGroupUp_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _isLocking || _selectedGroup is not { } selected) return;
        var siblings = FindGroupParentCollection(selected); int index = siblings?.IndexOf(selected) ?? -1;
        if (index > 0) await MoveGroupRelativeAsync(selected, siblings![index - 1], true);
    }

    private async void MenuMoveGroupDown_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _isLocking || _selectedGroup is not { } selected) return;
        var siblings = FindGroupParentCollection(selected); int index = siblings?.IndexOf(selected) ?? -1;
        if (siblings != null && index >= 0 && index < siblings.Count - 1) await MoveGroupRelativeAsync(selected, siblings[index + 1], false);
    }

    private async void MenuMoveGroupToRoot_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _isLocking || _selectedGroup is not { } selected) return;
        var root = _rootGroups.FirstOrDefault(group => group.IsRoot); if (root == null || selected.IsRoot || selected.IsRecycleBin) return;
        if (selected.ParentId == root.Id) { TxtOperationStatus.Text = LocalizationManager.GetString("FolderAtRoot"); return; }
        await MoveGroupInsideAsync(selected, root);
    }

    private void ShowGroupError(Exception exception, int generation)
    {
        if (!_isLocked && generation == _sessionGeneration) MessageBox.Show(this, UiErrorMessages.GetText(exception), LocalizationManager.GetString("OperationFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
