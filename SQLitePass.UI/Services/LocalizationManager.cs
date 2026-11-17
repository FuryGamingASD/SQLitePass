using System.Globalization;
using System.Windows;

namespace SQLitePass.UI.Services;

public static class LocalizationManager
{
    public static string CurrentLanguage { get; private set; } = "it";
    public static event EventHandler? LanguageChanged;

    public static void ApplyLanguage(string? language)
    {
        CurrentLanguage = TranslationCatalog.ResolveLanguage(language, CultureInfo.InstalledUICulture);
        if (Application.Current?.Resources is { } resources)
        {
            foreach (var (key, value) in TranslationCatalog.GetTexts(CurrentLanguage)) resources[key] = value;
            resources["UiFormattingLanguage"] = WindowPresentationService.FormattingLanguage;
        }
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string GetString(string key, params object[] args) => TranslationCatalog.Format(CurrentLanguage, key, CultureInfo.CurrentCulture, args);
}
