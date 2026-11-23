using Avalonia.Controls;
using SQLitePass.Core.Services;

namespace SQLitePass.Desktop;

public sealed partial class VaultWindow
{
    private readonly Dictionary<Window, (string Name, WindowSizePreference? Size)> _windowPresentation = new();
    private readonly HashSet<Window> _observedWindowSizes = new();
    private bool _vaultLayoutStarted;

    private async Task EnterVaultLayoutAsync()
    {
        if (_vaultLayoutStarted) return;
        _vaultLayoutStarted = true;
        await SaveWindowSizeAsync(this);
        MinWidth = 620;
        MinHeight = 460;
        Width = 1100;
        Height = 720;
        PrepareWindowSize(this);
    }

    private void PrepareWindowSize(Window window)
    {
        if (!_saveWorkspacePreferences || !window.CanResize || window.SizeToContent != SizeToContent.Manual) return;
        string name = window == this && !_vaultLayoutStarted ? "LoginWindow" : window.GetType().Name;
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
