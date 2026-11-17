using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using SQLitePass.Core.Services;

namespace SQLitePass.UI.Services;

internal static class WindowPresentationService
{
    private static bool _registered;
    private static Task _pendingSave = Task.CompletedTask;
    public static void Register()
    {
        if (_registered) return; _registered = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) =>
        {
            if (sender is Window window && ReferenceEquals(e.OriginalSource, window) && window.Tag is not PresentationApplied)
            {
                window.SetResourceReference(FrameworkElement.LanguageProperty, "UiFormattingLanguage");
                window.Tag = new PresentationApplied();
                string name = window.GetType().Name;
                var area = SystemParameters.WorkArea;
                window.MinWidth = Math.Min(window.MinWidth, Math.Max(320, area.Width - 24));
                window.MinHeight = Math.Min(window.MinHeight, Math.Max(200, area.Height - 24));
                if (AppSettingsManager.Load().WindowSizes.TryGetValue(name, out var saved) && window.ResizeMode != ResizeMode.NoResize)
                { window.Width = Math.Clamp(saved.Width, window.MinWidth, Math.Max(window.MinWidth, area.Width - 24)); window.Height = Math.Clamp(saved.Height, window.MinHeight, Math.Max(window.MinHeight, area.Height - 24)); }
                else
                { if (double.IsFinite(window.Width)) window.Width = Math.Min(window.Width, area.Width - 24); if (double.IsFinite(window.Height)) window.Height = Math.Min(window.Height, area.Height - 24); }
                window.Closed += (_, _) =>
                {
                    if (window.ResizeMode == ResizeMode.NoResize) return;
                    var bounds = window.RestoreBounds;
                    var size = new WindowSizePreference(bounds.Width, bounds.Height);
                    if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height)) return;
                    _pendingSave = _pendingSave.ContinueWith(_ => AppSettingsManager.Update(settings => settings.WindowSizes[name] = size), TaskScheduler.Default);
                };
            }
        }), handledEventsToo: true);
    }

    public static XmlLanguage FormattingLanguage => XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
    private sealed class PresentationApplied;
    public static void Flush()
    {
        try { _pendingSave.GetAwaiter().GetResult(); }
        catch (Exception exception) { AppLogger.LogWarning("Window sizes could not be saved: " + exception.GetType().Name); }
    }
}
