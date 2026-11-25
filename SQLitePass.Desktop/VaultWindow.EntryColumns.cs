using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private static readonly double[] DefaultEntryColumnWeights = [2, 1.35, 0.74, 1.65, 0.91, 0.75];
    private double[] _entryColumnWeights = NormalizeEntryColumnWeights(DefaultEntryColumnWeights);
    private bool _entryColumnResizeActive;

    private static readonly int[] EntryHeaderColumnIndexes = [0, 2, 4, 6, 8, 10];
    private static readonly double[] EntryColumnMinimumWidths = [70, 70, 70, 70, 70, 70];
    private bool EntryColumnsAreWide => (Bounds.Width > 0 ? Bounds.Width : Width) >= 1000;

    private void InitializeEntryColumnResizing()
    {
        foreach (var splitter in new[] { ResizeTitleUser, ResizeUserPassword, ResizePasswordUrl, ResizeUrlNotes, ResizeNotesUpdated })
            splitter.AddHandler(InputElement.PointerPressedEvent, EntryColumnResizer_PointerPressed,
                RoutingStrategies.Bubble, handledEventsToo: true);

        EntryHeaderGrid.AddHandler(InputElement.PointerReleasedEvent, EntryHeaderGrid_PointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);

        SetResizeLabel(ResizeTitleUser, TitleColumn.Text, UserColumn.Text);
        SetResizeLabel(ResizeUserPassword, UserColumn.Text, PasswordColumn.Text);
        SetResizeLabel(ResizePasswordUrl, PasswordColumn.Text, UrlColumn.Text);
        SetResizeLabel(ResizeUrlNotes, UrlColumn.Text, NotesColumn.Text);
        SetResizeLabel(ResizeNotesUpdated, NotesColumn.Text, UpdatedColumn.Text);
    }

    private void SetResizeLabel(Control splitter, string? left, string? right)
    {
        string label = Text("ResizeColumnsBetween", left ?? string.Empty, right ?? string.Empty);
        AutomationProperties.SetName(splitter, label);
        ToolTip.SetTip(splitter, label);
    }

    private void LoadEntryColumnWidths(Guid vaultId)
    {
        string key = vaultId.ToString("N");
        _entryColumnWeights = _settings.EntryColumnWidths.TryGetValue(key, out var widths) && widths is { Length: 6 }
            ? NormalizeEntryColumnWeights(widths)
            : NormalizeEntryColumnWeights(DefaultEntryColumnWeights);
        ApplyEntryColumnDefinitions();
    }

    private static double[] NormalizeEntryColumnWeights(IReadOnlyList<double> widths)
    {
        if (widths.Count != 6 || widths.Any(width => !double.IsFinite(width) || width <= 0))
            return [1d / 6, 1d / 6, 1d / 6, 1d / 6, 1d / 6, 1d / 6];

        double total = widths.Sum();
        if (!double.IsFinite(total) || total <= 0)
            return [1d / 6, 1d / 6, 1d / 6, 1d / 6, 1d / 6, 1d / 6];
        return widths.Select(width => width / total).ToArray();
    }

    private void ApplyEntryColumnDefinitions()
    {
        if (EntryHeaderGrid.ColumnDefinitions.Count != 11) return;

        bool showOptionalColumns = EntryColumnsAreWide;
        for (int i = 0; i < EntryHeaderColumnIndexes.Length; i++)
        {
            int headerIndex = EntryHeaderColumnIndexes[i];
            bool visible = i < 4 || showOptionalColumns;
            var definition = EntryHeaderGrid.ColumnDefinitions[headerIndex];
            definition.MinWidth = visible ? EntryColumnMinimumWidths[i] : 0;
            definition.Width = visible ? new GridLength(_entryColumnWeights[i], GridUnitType.Star) : new GridLength(0);
        }

        EntryHeaderGrid.ColumnDefinitions[7].Width = showOptionalColumns ? new GridLength(6) : new GridLength(0);
        EntryHeaderGrid.ColumnDefinitions[9].Width = showOptionalColumns ? new GridLength(6) : new GridLength(0);
        EntryHeaderGrid.ColumnDefinitions[7].MinWidth = EntryHeaderGrid.ColumnDefinitions[9].MinWidth = 0;
        EntryHeaderGrid.ColumnDefinitions[7].MaxWidth = EntryHeaderGrid.ColumnDefinitions[9].MaxWidth = 6;

        SyncEntryRowColumnWidths(_entryColumnWeights);
    }

    private void EntryHeaderGrid_LayoutUpdated(object? sender, EventArgs e)
    {
        if (!_entryColumnResizeActive) return;
        SyncEntryRowColumnWidths(ReadEntryColumnWeightsFromHeader());
    }

    private void EntryRowGrid_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Grid row) ApplyEntryRowColumnWidths(row, _entryColumnWeights);
    }

    private void EntryColumnResizer_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            _entryColumnResizeActive = true;
    }

    private void EntryColumnResizer_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_entryColumnResizeActive) return;
        _entryColumnResizeActive = false;
        Dispatcher.UIThread.Post(() => _ = CommitEntryColumnWidthsAsync(), DispatcherPriority.Background);
    }

    private void EntryHeaderGrid_PointerReleased(object? sender, PointerReleasedEventArgs e) =>
        EntryColumnResizer_PointerReleased(sender, e);

    private double[] ReadEntryColumnWeightsFromHeader()
    {
        double[] widths = new double[6];
        bool showOptionalColumns = EntryColumnsAreWide;
        for (int i = 0; i < widths.Length; i++)
        {
            if (i >= 4 && !showOptionalColumns)
            {
                widths[i] = _entryColumnWeights[i];
                continue;
            }

            double actual = EntryHeaderGrid.ColumnDefinitions[EntryHeaderColumnIndexes[i]].ActualWidth;
            widths[i] = double.IsFinite(actual) && actual > 0 ? actual : _entryColumnWeights[i];
        }

        return NormalizeEntryColumnWeights(widths);
    }

    private async Task CommitEntryColumnWidthsAsync()
    {
        if (_session is null || _closing) return;

        _entryColumnWeights = ReadEntryColumnWeightsFromHeader();
        ApplyEntryColumnDefinitions();
        string key = _session.Metadata.VaultId.ToString("N");
        double[] savedWidths = (double[])_entryColumnWeights.Clone();
        _settings.EntryColumnWidths[key] = savedWidths;
        try
        {
            var saved = await Task.Run(() => AppSettingsManager.Update(settings => settings.EntryColumnWidths[key] = savedWidths));
            _settings.EntryColumnWidths[key] = saved.EntryColumnWidths[key];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError("Could not save entry column widths", error);
            if (!_closing && _session is not null) StatusText.Text = Text("ColumnWidthSaveWarning");
        }
    }

    private void SyncEntryRowColumnWidths(IReadOnlyList<double> weights)
    {
        if (Entries is null) return;
        foreach (var row in Entries.GetVisualDescendants().OfType<Grid>()
                     .Where(grid => grid.Tag is string tag && tag == "EntryColumns"))
            ApplyEntryRowColumnWidths(row, weights);
    }

    private void ApplyEntryRowColumnWidths(Grid row, IReadOnlyList<double> weights)
    {
        if (row.ColumnDefinitions.Count != 6) return;
        bool showOptionalColumns = EntryColumnsAreWide;
        for (int i = 0; i < 6; i++)
        {
            bool visible = i < 4 || showOptionalColumns;
            var definition = row.ColumnDefinitions[i];
            definition.MinWidth = visible ? EntryColumnMinimumWidths[i] : 0;
            var width = visible ? new GridLength(weights[i], GridUnitType.Star) : new GridLength(0);
            if (definition.Width != width) definition.Width = width;
        }
    }
}
