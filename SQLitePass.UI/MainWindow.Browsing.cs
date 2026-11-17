using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SQLitePass.Core.Models;
using SQLitePass.UI.Services;

namespace SQLitePass.UI;

public partial class MainWindow
{
    private const int EntryPageSize = 200;
    private EntryPageCursor? _pageCursor;
    private EntryPageCursor? _nextPageCursor;
    private EntryPageCursor? _previousPageCursor;
    private readonly DispatcherTimer _entrySearchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private EntrySortField _pageSort = EntrySortField.Title;
    private bool _pageDescending;
    private int _pageMatchedCount;
    private int _pageFirstIndex;
    private int _browseGeneration;
    private bool _browseInitialized;
    private bool _queuedPageReset;
    private bool _suppressPageFocus;
    private bool _verificationPage;
    private bool _suppressFilters;
    private DateOnly _expiryReferenceDate = DateOnly.FromDateTime(DateTime.Today);

    private void InitializeEntryBrowsing()
    {
        _browseInitialized = true;
        _entrySearchTimer.Tick += async (_, _) =>
        {
            _entrySearchTimer.Stop();
            await RefreshEntryPageAsync(resetPage: true);
        };
        UpdateEntryPager();
    }

    private EntryPageQuery CurrentEntryQuery() => new(
        GroupId: _selectedGroup?.IsRoot == false && !_selectedGroup.IsRecycleBin ? _selectedGroup.Id : null,
        SearchText: TxtSearch.Text?.Trim() ?? "",
        OnlyRecycleBin: _selectedGroup?.IsRecycleBin == true,
        SortBy: _pageSort, Descending: _pageDescending, PageSize: EntryPageSize,
        Expiry: Enum.TryParse<EntryExpiryFilter>(CmbExpiryFilter.SelectedValue?.ToString(), out var expiry) ? expiry : EntryExpiryFilter.All,
        Tag: TxtTagFilter.Text?.Trim() ?? "", ExpiryReferenceDate: _expiryReferenceDate);

    private void EntryFilter_Changed(object sender, RoutedEventArgs args)
    { if (!_suppressFilters) ScheduleEntryQuery(debounce: sender is TextBox); }

    private void ClearEntryFilters_Click(object sender, RoutedEventArgs args)
    {
        _suppressFilters = true;
        try { TxtTagFilter.Clear(); TxtSearch.Clear(); CmbExpiryFilter.SelectedIndex = 0; }
        finally { _suppressFilters = false; }
        ScheduleEntryQuery(debounce: false);
    }

    private void ShowAllEntries_Click(object sender, RoutedEventArgs args)
    {
        if (_isLocked) return;
        _suppressFilters = true;
        try { _selectedGroup = _rootGroups.FirstOrDefault(group => group.IsRoot); if (_selectedGroup != null) _selectedGroup.IsSelected = true; }
        finally { _suppressFilters = false; }
        ScheduleEntryQuery(debounce: false);
    }

    private void UpdateEntryScope(IReadOnlyDictionary<Guid, PwGroup> groups)
    {
        string scope = LocalizationManager.GetString("AllActiveEntries");
        if (_selectedGroup != null && !_selectedGroup.IsRoot && (_selectedGroup.IsRecycleBin || string.IsNullOrWhiteSpace(TxtSearch.Text)))
        {
            var parts = new List<string>();
            PwGroup? current = _selectedGroup; int characters = 0;
            while (current != null)
            {
                string part = UiText.Preview(current.Name, 128); parts.Add(part); characters += part.Length;
                if (parts.Count >= 16 || characters >= 768)
                { if (current.ParentId.HasValue) parts.Add("…"); break; }
                current = current.ParentId.HasValue && groups.TryGetValue(current.ParentId.Value, out var parent) ? parent : null;
            }
            parts.Reverse(); scope = string.Join(" › ", parts);
        }
        TxtEntryScope.Text = _isLocked ? "" : UiText.Preview(scope, 1024);
        TxtEntryScope.ToolTip = TxtEntryScope.Text;
        TxtSearch.SetResourceReference(System.Windows.Automation.AutomationProperties.NameProperty,
            _selectedGroup?.IsRecycleBin == true ? "SearchRecycleBinScope" : "SearchActiveScope");
    }

    private void ScheduleEntryQuery(bool debounce)
    {
        if (!_browseInitialized || _verificationPage || _suppressFilters || _isLocked || _isLocking) return;
        _browseGeneration++; _suppressPageFocus = true;
        _refreshCancellation?.Cancel(); _entrySearchTimer.Stop();
        if (debounce) _entrySearchTimer.Start();
        else _ = RefreshEntryPageAsync(resetPage: true);
    }

    private void ResetEntryBrowsing()
    {
        _entrySearchTimer.Stop(); _browseGeneration++;
        _pageCursor = _nextPageCursor = _previousPageCursor = null;
        _pageMatchedCount = _pageFirstIndex = 0; _queuedPageReset = false;
        _pageSort = EntrySortField.Title; _pageDescending = false;
        if (_browseInitialized)
        {
            foreach (var column in DgEntries.Columns) column.SortDirection = null;
            _suppressFilters = true;
            try { TxtTagFilter.Clear(); CmbExpiryFilter.SelectedIndex = 0; }
            finally { _suppressFilters = false; }
            TxtEntryScope.Text = ""; TxtEntryScope.ToolTip = null;
            UpdateEntryPager();
        }
    }

    private async Task<bool> RefreshEntryPageAsync(bool resetPage)
    {
        if (_isLocked || _isLocking || !_browseInitialized) return false;
        _entrySearchTimer.Stop();
        if (resetPage)
        {
            _expiryReferenceDate = DateOnly.FromDateTime(DateTime.Today);
            _pageCursor = _nextPageCursor = _previousPageCursor = null;
            _browseGeneration++; _refreshCancellation?.Cancel();
        }
        if (_refreshing) { _refreshAgain = true; _queuedPageReset |= resetPage; return false; }
        _refreshing = true; UpdateEntryPager();
        using var cancellation = new CancellationTokenSource(); _refreshCancellation = cancellation;
        int generation = _sessionGeneration, request = _browseGeneration;
        var storage = _storage; var crypto = _crypto;
        var query = CurrentEntryQuery();
        Guid? focus = resetPage ? _entryToSelectAfterRefresh ?? (!_suppressPageFocus ? _selectedEntry?.Id : null) : null;
        _suppressPageFocus = false;
        try
        {
            var progress = new Progress<VaultReadProgress>(value =>
            {
                if (_isLocked || generation != _sessionGeneration || request != _browseGeneration || cancellation.IsCancellationRequested) return;
                TxtOperationStatus.Text = LocalizationManager.GetString(value.Phase == VaultReadPhase.Verifying ? "VaultVerifying" : "VaultLoading", value.Completed, value.Total);
            });
            var page = await storage.LoadEntryPageAsync(crypto, query, _pageCursor, focus, cancellation.Token, progress);
            if (_isLocked || generation != _sessionGeneration || request != _browseGeneration || cancellation.IsCancellationRequested)
            {
                foreach (var entry in page.Entries) entry.ClearSecrets();
                return false;
            }
            ApplyEntryPage(page);
            TxtOperationStatus.Text = LocalizationManager.GetString("OperationComplete");
            return true;
        }
        catch (VaultPageChangedException)
        {
            if (!_isLocked && generation == _sessionGeneration && request == _browseGeneration)
            {
                _pageCursor = _nextPageCursor = _previousPageCursor = null; _selectedGroup = null;
                _browseGeneration++; _refreshAgain = _queuedPageReset = true; _suppressPageFocus = true;
                TxtOperationStatus.Text = LocalizationManager.GetString("EntryPageChanged");
            }
        }
        catch (OperationCanceledException)
        {
            if (!_isLocked && generation == _sessionGeneration && request == _browseGeneration)
            { TxtOperationStatus.Text = LocalizationManager.GetString("VaultLoadCancelled"); UpdateBackupStatus(); }
        }
        catch (Exception exception)
        {
            if (!_isLocked && generation == _sessionGeneration && request == _browseGeneration)
            {
                foreach (var entry in _visibleEntries) entry.ClearSecrets();
                _visibleEntries.Clear(); _rootGroups.Clear(); _selectedEntry = null; _selectedGroup = null;
                _pageCursor = _nextPageCursor = _previousPageCursor = null; _pageFirstIndex = _pageMatchedCount = 0;
                UpdatePreviewPanel(); UpdateBackupStatus();
                if (!_ownsSession) throw new InvalidOperationException("Synthetic entry browsing failed.", exception);
                MessageBox.Show(this, UiErrorMessages.GetText(exception), LocalizationManager.GetString("LoadFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            _refreshing = false;
            if (ReferenceEquals(_refreshCancellation, cancellation)) _refreshCancellation = null;
            UpdateEntryPager();
            if (_refreshAgain && !_isLocked && !_isLocking)
            {
                _refreshAgain = false; bool reset = _queuedPageReset; _queuedPageReset = false;
                _ = RefreshEntryPageAsync(reset);
            }
        }
        return false;
    }

    private void ApplyEntryPage(VaultEntryPage page)
    {
        Guid? selectedGroup = _selectedGroup?.Id, selectedEntry = _entryToSelectAfterRefresh ?? _selectedEntry?.Id;
        _metadata = page.Metadata; ApplyGroupExpansion(page.Groups, _metadata.VaultId);
        foreach (var entry in _visibleEntries) entry.ClearSecrets();
        _visibleEntries.Clear(); _rootGroups.Clear();
        var map = page.Groups.ToDictionary(group => group.Id);
        foreach (var group in page.Groups)
        {
            if (group.ParentId.HasValue) map[group.ParentId.Value].Children.Add(group);
            else _rootGroups.Add(group);
        }
        foreach (var entry in page.Entries) _visibleEntries.Add(entry);
        _selectedGroup = page.Groups.FirstOrDefault(group => group.Id == selectedGroup) ?? page.Groups.FirstOrDefault(group => group.IsRoot);
        if (_selectedGroup != null) _selectedGroup.IsSelected = true;
        _pageCursor = null; _nextPageCursor = page.NextCursor; _previousPageCursor = page.PreviousCursor;
        _pageFirstIndex = page.FirstIndex; _pageMatchedCount = page.MatchedCount;
        DgEntries.SelectedItem = page.Entries.FirstOrDefault(entry => entry.Id == selectedEntry);
        _entryToSelectAfterRefresh = null;
        _entriesView.Refresh(); UpdatePreviewPanel(); UpdateBackupStatus(); UpdateEntryPager(); UpdateEntryScope(map);
        foreach (var column in DgEntries.Columns)
            column.SortDirection = column.SortMemberPath == _pageSort.ToString() ?
                _pageDescending ? ListSortDirection.Descending : ListSortDirection.Ascending : null;
    }

    internal void ApplyVerificationPage(VaultEntryPage page)
    {
        if (_ownsSession) throw new InvalidOperationException("This hook is limited to isolated verification windows.");
        _verificationPage = true;
        _refreshing = true;
        try { ApplyEntryPage(page); }
        finally { _refreshing = false; UpdateEntryPager(); }
    }

    internal async Task ApplyVerificationFiltersAsync(string tag, EntryExpiryFilter expiry)
    {
        if (_ownsSession) throw new InvalidOperationException("This hook is limited to isolated verification windows.");
        _suppressFilters = true;
        try { TxtTagFilter.Text = tag; CmbExpiryFilter.SelectedValue = expiry.ToString(); }
        finally { _suppressFilters = false; }
        await RefreshEntryPageAsync(resetPage: true);
    }

    private void UpdateEntryPager()
    {
        if (!_browseInitialized) return;
        int first = _isLocked || _visibleEntries.Count == 0 ? 0 : _pageFirstIndex;
        string status = LocalizationManager.GetString("EntriesPageRange", first, first == 0 ? 0 : first + _visibleEntries.Count - 1, _isLocked ? 0 : _pageMatchedCount);
        TxtEntryPageRange.Text = TxtStatusEntriesCount.Text = status;
        BtnPreviousEntryPage.IsEnabled = !_isLocked && !_refreshing && _previousPageCursor != null;
        BtnNextEntryPage.IsEnabled = !_isLocked && !_refreshing && _nextPageCursor != null;
        EmptyEntriesPanel.Visibility = !_isLocked && !_refreshing && _visibleEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnEmptyViewAddEntry.Visibility = _selectedGroup?.IsRecycleBin == true ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void BtnNextEntryPage_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _refreshing || _nextPageCursor == null) return;
        int request = _browseGeneration; _pageCursor = _nextPageCursor;
        if (!await RefreshEntryPageAsync(resetPage: false) && !_isLocked && request == _browseGeneration)
        { _pageCursor = null; UpdateEntryPager(); }
    }

    private async void BtnPreviousEntryPage_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _refreshing || _previousPageCursor == null) return;
        int request = _browseGeneration; _pageCursor = _previousPageCursor;
        if (!await RefreshEntryPageAsync(resetPage: false) && !_isLocked && request == _browseGeneration)
        { _pageCursor = null; UpdateEntryPager(); }
    }

    private void DgEntries_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (!Enum.TryParse<EntrySortField>(e.Column.SortMemberPath, out var sort)) return;
        _pageDescending = _pageSort == sort && !_pageDescending;
        _pageSort = sort; ScheduleEntryQuery(debounce: false);
    }
}
