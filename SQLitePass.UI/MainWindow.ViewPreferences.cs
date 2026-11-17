using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI;

public partial class MainWindow
{
    private readonly Dictionary<string, bool> _groupExpansion = new();
    private readonly Dictionary<string, bool> _pendingGroupExpansion = new();
    private readonly DispatcherTimer _groupExpansionTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Task _groupExpansionSave = Task.CompletedTask;
    private bool _viewPreferencesClosing;

    private void InitializeViewPreferences()
    {
        foreach (var pair in AppSettingsManager.Load().GroupExpansion) _groupExpansion[pair.Key] = pair.Value;
        _groupExpansionTimer.Tick += SaveGroupExpansion_Tick;
    }

    private void ApplyGroupExpansion(IEnumerable<PwGroup> groups, Guid vaultId)
    {
        foreach (var group in groups)
            if (_groupExpansion.TryGetValue($"{vaultId:N}/{group.Id:N}", out bool expanded)) group.IsExpanded = expanded;
    }

    private void GroupExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _refreshing || _viewPreferencesClosing || e.OriginalSource is not TreeViewItem item || item.DataContext is not PwGroup group) return;
        string key = $"{_metadata.VaultId:N}/{group.Id:N}";
        _groupExpansion[key] = item.IsExpanded; _pendingGroupExpansion[key] = item.IsExpanded;
        _groupExpansionTimer.Stop(); _groupExpansionTimer.Start();
    }

    private static void PersistExpansion(Dictionary<string, bool> values) => AppSettingsManager.Update(settings =>
    {
        foreach (var (key, value) in values)
        { settings.GroupExpansion.Remove(key); settings.GroupExpansion[key] = value; }
    });

    private async void SaveGroupExpansion_Tick(object? sender, EventArgs e)
    {
        _groupExpansionTimer.Stop();
        if (_viewPreferencesClosing || _pendingGroupExpansion.Count == 0) return;
        if (!_groupExpansionSave.IsCompleted) { _groupExpansionTimer.Start(); return; }
        var values = new Dictionary<string, bool>(_pendingGroupExpansion);
        bool saved = false;
        try
        {
            _groupExpansionSave = Task.Run(() => PersistExpansion(values));
            await _groupExpansionSave; saved = true;
            foreach (var (key, value) in values)
                if (_pendingGroupExpansion.TryGetValue(key, out bool pending) && pending == value) _pendingGroupExpansion.Remove(key);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            AppLogger.LogWarning("Stato di espansione delle cartelle non salvato.");
            if (!_isLocked && !_viewPreferencesClosing) TxtOperationStatus.Text = LocalizationManager.GetString("FolderStateSaveWarning");
        }
        if (saved && !_viewPreferencesClosing && _pendingGroupExpansion.Count > 0) _groupExpansionTimer.Start();
    }

    private void FlushViewPreferences()
    {
        _viewPreferencesClosing = true; _groupExpansionTimer.Stop();
        try { _groupExpansionSave.GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { AppLogger.LogWarning("Salvataggio dello stato cartelle non completato."); }
        if (_pendingGroupExpansion.Count == 0) return;
        try { PersistExpansion(new(_pendingGroupExpansion)); _pendingGroupExpansion.Clear(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { AppLogger.LogWarning("Stato delle cartelle non salvato alla chiusura."); }
    }
}
