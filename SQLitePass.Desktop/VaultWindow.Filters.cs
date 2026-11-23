using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private sealed record BrowseOption<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private bool _changingBrowseControls;
    private bool _liveSearch;
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(280) };

    private void InitializeBrowseOptions()
    {
        ExpiryFilter.ItemsSource = new BrowseOption<EntryExpiryFilter>[]
        {
            new(EntryExpiryFilter.All, Text("ExpiryFilterAll")),
            new(EntryExpiryFilter.Expired, Text("ExpiryFilterExpired")),
            new(EntryExpiryFilter.NextThirtyDays, Text("ExpiryFilterSoon"))
        };
        SortField.ItemsSource = new BrowseOption<EntrySortField>[]
        {
            new(EntrySortField.Title, Text("ColTitle")),
            new(EntrySortField.UserName, Text("ColUserName")),
            new(EntrySortField.Password, Text("ColPassword")),
            new(EntrySortField.Url, Text("ColUrl")),
            new(EntrySortField.Notes, Text("ColNotes")),
            new(EntrySortField.UpdatedAt, Text("ColUpdatedAt"))
        };
        AutomationProperties.SetName(ExpiryFilter, Text("ExpiryFilterLabel"));
        AutomationProperties.SetName(TagFilter, Text("TagFilterLabel"));
        AutomationProperties.SetName(SortField, Text("SortByEntryColumn"));
        TagFilter.PlaceholderText = Text("TagFilterLabel");
        ToolTip.SetTip(TagFilter, Text("TagFilterHint"));
        SortDescending.Content = Text("SortEntriesDescending");
        ResetFilters.Content = Text("ResetEntryFilters");
        ResetBrowseOptions();
        SearchBox.TextChanged += QueueLiveSearch;
        TagFilter.TextChanged += QueueLiveSearch;
        _searchTimer.Tick += RunLiveSearch;
        Closed += (_, _) => _searchTimer.Stop();
    }

    private void QueueLiveSearch(object? sender, TextChangedEventArgs e)
    {
        if (_session == null || _changingBrowseControls || _closing || _systemLocked) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void RunLiveSearch(object? sender, EventArgs e)
    {
        _searchTimer.Stop();
        if (_session == null || _closing || _systemLocked) return;
        if (_operation != null) { _searchTimer.Start(); return; }
        _liveSearch = true;
        try { await SearchAsync(); }
        finally
        {
            _liveSearch = false;
            UpdateControls();
        }
    }

    private async void ColumnSort_Click(object? sender, RoutedEventArgs e)
    {
        if (_session == null || _operation != null || sender is not Button { Tag: string tag } ||
            !Enum.TryParse<EntrySortField>(tag, out var sort)) return;
        var option = SortField.Items.OfType<BrowseOption<EntrySortField>>().Single(item => item.Value == sort);
        _changingBrowseControls = true;
        try
        {
            bool same = Equals(SortField.SelectedItem, option);
            SortField.SelectedItem = option;
            SortDescending.IsChecked = same && SortDescending.IsChecked != true;
        }
        finally { _changingBrowseControls = false; }
        await SearchAsync();
    }

    private EntryPageQuery ReadBrowseOptions(EntryPageQuery query) => query with
    {
        SearchText = SearchBox.Text?.Trim() ?? "",
        Tag = TagFilter.Text?.Trim() ?? "",
        Expiry = (ExpiryFilter.SelectedItem as BrowseOption<EntryExpiryFilter>)?.Value ?? EntryExpiryFilter.All,
        ExpiryReferenceDate = DateOnly.FromDateTime(DateTime.Today),
        SortBy = (SortField.SelectedItem as BrowseOption<EntrySortField>)?.Value ?? EntrySortField.Title,
        Descending = SortDescending.IsChecked == true
    };

    private async Task RefreshEntryQueryAsync(VaultSession session, EntryPageQuery query, CancellationToken token)
    {
        try { await LoadPageAsync(session, query, null, token); }
        catch (VaultPageChangedException) when (query.GroupId.HasValue)
        {
            query = query with { GroupId = null, OnlyRecycleBin = false };
            await LoadPageAsync(session, query, null, token);
            StatusText.Text = Text("EntryPageChanged");
        }
        _query = query;
    }

    private async void BrowseOption_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (!_changingBrowseControls && _session != null) await SearchAsync();
    }

    private async void BrowseSort_Click(object? sender, RoutedEventArgs e) => await SearchAsync();

    private async void ResetFilters_Click(object? sender, RoutedEventArgs e)
    {
        ClearSearchFilters();
        await SearchAsync();
    }

    private void ResetBrowseOptions()
    {
        ClearSearchFilters();
        SortField.SelectedIndex = 0;
        SortDescending.IsChecked = false;
    }

    private void ClearSearchFilters()
    {
        _changingBrowseControls = true;
        try
        {
            SearchBox.Clear();
            TagFilter.Clear();
            ExpiryFilter.SelectedIndex = 0;
        }
        finally { _changingBrowseControls = false; }
    }
}
