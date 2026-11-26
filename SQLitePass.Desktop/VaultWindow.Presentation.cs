using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using SQLitePass.Core.Services;
using SQLitePass.UI.Controls;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private readonly Dictionary<Window, (string Name, WindowSizePreference? Size)> _windowPresentation = new();
    private readonly HashSet<Window> _observedWindowSizes = new();
    private bool _vaultLayoutStarted;
    private bool _restoreVaultMaximized;

    private void ApplyLoginLayout()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        MinWidth = MinHeight = 0;
        MaxWidth = screen != null ? Math.Min(510, screen.WorkingArea.Width / screen.Scaling) : 510;
        MaxHeight = screen != null ? screen.WorkingArea.Height / screen.Scaling : double.PositiveInfinity;
        CanResize = false;
        WorkspaceResizeEdges.IsVisible = false;
        WindowDecorations = WindowDecorations.None;
        SizeToContent = SizeToContent.Height;
        Width = MaxWidth;
        Height = double.NaN;
    }

    private void InitializeWindowControls()
    {
        LabelAction(WindowMinimize, "WindowMinimize");
        LabelAction(WindowClose, "Close");
        UpdateWindowMaximize();
        PropertyChanged += (_, change) =>
        {
            if (change.Property != WindowStateProperty) return;
            UpdateWindowMaximize();
            UpdateWorkspaceResizeEdges();
        };
    }

    private void RefreshWindowsTaskbarIcon()
    {
        if (!OperatingSystem.IsWindows()) return;
        ShowInTaskbar = true;
        using var iconStream = AssetLoader.Open(new Uri("avares://SQLitePass.Desktop/Resources/app.ico"));
        Icon = new WindowIcon(iconStream);
    }

    private void UpdateWindowMaximize()
    {
        bool maximized = WindowState == WindowState.Maximized;
        WindowMaximizeIcon.Kind = maximized ? AppIconKind.WindowRestore : AppIconKind.WindowMaximize;
        LabelAction(WindowMaximize, maximized ? "WindowRestore" : "WindowMaximize");
    }

    private void WindowHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (_vaultLayoutStarted && e.ClickCount == 2) ToggleWindowMaximized();
            else BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void WindowMinimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void WindowMaximize_Click(object? sender, RoutedEventArgs e) => ToggleWindowMaximized();

    private void WindowClose_Click(object? sender, RoutedEventArgs e) => Close();

    private void ToggleWindowMaximized() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateWorkspaceResizeEdges() =>
        WorkspaceResizeEdges.IsVisible = OperatingSystem.IsLinux() && _vaultLayoutStarted && WindowState == WindowState.Normal;

    private void WorkspaceResizeEdge_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Border { Tag: WindowEdge edge } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void EnterVaultLayout()
    {
        if (_vaultLayoutStarted) return;
        _vaultLayoutStarted = true;
        SizeToContent = SizeToContent.Manual;
        MaxWidth = MaxHeight = double.PositiveInfinity;
        CanResize = true;
        WindowDecorations = OperatingSystem.IsLinux() ? WindowDecorations.None : WindowDecorations.BorderOnly;
        Width = 1100;
        Height = 720;
        MinWidth = 620;
        MinHeight = 460;
        PrepareWindowSize(this);
        VaultToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        BrowseFiltersToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double nativeBorder = WindowDecorations == WindowDecorations.None ? 0 : 16;
        double toolbarMinimum = VaultToolbar.DesiredSize.Width + 24 + nativeBorder;
        double filtersMinimum = 380 + 5 + 26 + BrowseFiltersToolbar.DesiredSize.Width + nativeBorder;
        double contentMinimum = Math.Ceiling(Math.Max(toolbarMinimum, filtersMinimum));
        if (contentMinimum > MinWidth)
        {
            MinWidth = contentMinimum;
            Width = Math.Max(Width, contentMinimum);
        }
        Classes.Set("wide", Width >= 1000);
        ApplyEntryColumnDefinitions();
        Dispatcher.UIThread.Post(RefreshWindowsTaskbarIcon, DispatcherPriority.Background);
        if (_restoreVaultMaximized && WindowState == WindowState.Normal) WindowState = WindowState.Maximized;
        UpdateWorkspaceResizeEdges();
    }

    private Task LeaveVaultLayoutAsync()
    {
        if (!_vaultLayoutStarted) return Task.CompletedTask;
        _vaultLayoutStarted = false;
        var save = SaveWindowSizeAsync(this);
        _restoreVaultMaximized = WindowState == WindowState.Maximized;
        if (_restoreVaultMaximized) WindowState = WindowState.Normal;
        ApplyLoginLayout();
        return save;
    }

    private void PrepareWindowSize(Window window)
    {
        if (!_saveWorkspacePreferences || !window.CanResize || window.SizeToContent != SizeToContent.Manual) return;
        string name = window.GetType().Name;
        string legacy = window switch
        {
            VaultWindow when _vaultLayoutStarted => "MainWindow",
            EntryEditor => "EntryEditDialog",
            SettingsWindow => "SettingsDialog",
            _ => name
        };
        var preference = _settings.WindowSizes.GetValueOrDefault(name) ?? _settings.WindowSizes.GetValueOrDefault(legacy);
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen != null)
        {
            double maxWidth = Math.Max(320, screen.WorkingArea.Width / screen.Scaling - 24);
            double maxHeight = Math.Max(200, screen.WorkingArea.Height / screen.Scaling - 24);
            window.MinWidth = Math.Min(window.MinWidth, maxWidth);
            window.MinHeight = Math.Min(window.MinHeight, maxHeight);
            if (preference != null || double.IsFinite(window.Width))
                window.Width = Math.Clamp(preference?.Width ?? window.Width, window.MinWidth, maxWidth);
            if (preference != null || double.IsFinite(window.Height))
                window.Height = Math.Clamp(preference?.Height ?? window.Height, window.MinHeight, maxHeight);
        }
        var size = window.Bounds is { Width: > 0, Height: > 0 } bounds
            ? new WindowSizePreference(bounds.Width, bounds.Height) : preference;
        _windowPresentation[window] = (name, size);
        if (!_observedWindowSizes.Add(window)) return;
        window.Closed += (_, _) => _observedWindowSizes.Remove(window);
        window.SizeChanged += (_, _) =>
        {
            if (!_windowPresentation.TryGetValue(window, out var presentation) || window.WindowState != WindowState.Normal ||
                window.Bounds is not { Width: > 0, Height: > 0 } resized) return;
            _windowPresentation[window] = (presentation.Name, new WindowSizePreference(resized.Width, resized.Height));
        };
    }

    private async Task SaveWindowSizeAsync(Window window)
    {
        if (!_windowPresentation.Remove(window, out var presentation) || presentation.Size is not { } size) return;
        _settings.WindowSizes[presentation.Name] = size;
        try { await Task.Run(() => AppSettingsManager.Update(settings => settings.WindowSizes[presentation.Name] = size)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AppLogger.LogError("Could not save window dimensions", error);
        }
    }
}
