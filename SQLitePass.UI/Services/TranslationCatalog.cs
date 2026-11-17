using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SQLitePass.UI.Services;

// This reader has no WPF dependency, so the actual catalogs can be checked without opening windows.
internal static class TranslationCatalog
{
    public static IReadOnlyList<string> Languages { get; } = Array.AsReadOnly(new[] { "it", "en", "fr", "de", "es" });
    private static readonly FrozenDictionary<string, FrozenDictionary<string, string>> Catalogs = Load();

    private static FrozenDictionary<string, FrozenDictionary<string, string>> Load()
    {
        var result = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal);
        foreach (string language in Languages)
        {
            using var stream = typeof(TranslationCatalog).Assembly.GetManifestResourceStream($"SQLitePass.Localization.{language}.json")
                ?? throw new InvalidDataException("Missing language catalog: " + language);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidDataException("Empty language catalog: " + language);
            if (values.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
                throw new InvalidDataException("Blank language resource: " + language);
            result[language] = values.ToFrozenDictionary(StringComparer.Ordinal);
        }
        var reference = result["en"].Keys.ToHashSet(StringComparer.Ordinal);
        if (result.Values.Any(values => !reference.SetEquals(values.Keys))) throw new InvalidDataException("Language catalogs have different keys.");
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static string ResolveLanguage(string? requested, CultureInfo systemCulture)
    {
        string candidate = requested?.Trim().ToLowerInvariant() ?? "";
        if (Catalogs.ContainsKey(candidate)) return candidate;
        string system = systemCulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return Catalogs.ContainsKey(system) ? system : "en";
    }

    public static IReadOnlyDictionary<string, string> GetTexts(string language) => Catalogs.TryGetValue(language, out var values) ? values : Catalogs["en"];
    public static string GetText(string language, string key) => GetTexts(language).TryGetValue(key, out string? text)
        ? text : throw new KeyNotFoundException("Missing UI resource: " + key);
    public static string Format(string language, string key, CultureInfo culture, params object[] args) =>
        args.Length == 0 ? GetText(language, key) : string.Format(culture, GetText(language, key), args);
}
