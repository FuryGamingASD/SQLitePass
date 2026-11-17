using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SQLitePass.UI.Services;

namespace SQLitePass.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("it")]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("es")]
    public void EveryLanguageHasTheSameNonBlankKeys(string language)
    {
        var expected = TranslationCatalog.GetTexts("en"); var actual = TranslationCatalog.GetTexts(language);
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        Assert.All(actual, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    [Theory]
    [InlineData("FR", "it-IT", "fr")]
    [InlineData("de", "es-ES", "de")]
    [InlineData("", "es-MX", "es")]
    [InlineData("unsupported", "nl-NL", "en")]
    public void LanguageResolutionHonorsExplicitChoiceAndSystemFallback(string requested, string system, string expected) =>
        Assert.Equal(expected, TranslationCatalog.ResolveLanguage(requested, CultureInfo.GetCultureInfo(system)));

    [Fact]
    public void UnknownLanguageUsesEnglish() =>
        Assert.Equal(TranslationCatalog.GetText("en", "Save"), TranslationCatalog.GetText("unsupported", "Save"));

    [Fact]
    public void MissingTextIsAnExplicitCatalogError() =>
        Assert.Throws<KeyNotFoundException>(() => TranslationCatalog.GetText("it", "MissingTestResource"));

    [Fact]
    public void FormattedTranslationsKeepAllArgumentsAndFormatSpecifiers()
    {
        static string[] Tokens(string text) => Regex.Matches(text, @"(?<!\{)\{\d+(?:,-?\d+)?(?::[^}]+)?\}(?!\})").Select(match => match.Value).Order().ToArray();
        foreach (var (key, reference) in TranslationCatalog.GetTexts("en"))
        {
            if (!Regex.IsMatch(reference, @"\{\d")) continue;
            var expected = CompositeFormat.Parse(reference);
            foreach (string language in TranslationCatalog.Languages)
            {
                string text = TranslationCatalog.GetText(language, key);
                Assert.Equal(expected.MinimumArgumentCount, CompositeFormat.Parse(text).MinimumArgumentCount);
                Assert.Equal(Tokens(reference), Tokens(text));
            }
        }
    }

    private static IEnumerable<(string Name, string Text)> Sources(string extension)
    {
        var assembly = typeof(LocalizationTests).Assembly;
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("SQLitePass.UiSource.") && n.EndsWith(extension, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream);
            yield return (name, reader.ReadToEnd());
        }
    }

    [Fact]
    public void TextResourcesReferencedByXamlExist()
    {
        string[] properties = ["Text", "Header", "Title", "Content", "ToolTip", "AutomationProperties.Name", "AutomationProperties.HelpText"];
        foreach (var (name, text) in Sources(".xaml"))
            foreach (var attribute in XDocument.Parse(text).Descendants().Attributes().Where(a => properties.Contains(a.Name.LocalName)))
            {
                var match = Regex.Match(attribute.Value, @"^\{DynamicResource (?<key>[^}]+)\}$");
                if (match.Success) Assert.True(TranslationCatalog.GetTexts("en").ContainsKey(match.Groups["key"].Value), name + ": " + attribute.Value);
            }
    }

    [Fact]
    public void LiteralCatalogLookupsAndDynamicResourceAssignmentsExist()
    {
        var resources = Sources(".xaml").SelectMany(source => XDocument.Parse(source.Text).Descendants().Attributes()
            .Where(attribute => attribute.Name.LocalName == "Key").Select(attribute => attribute.Value)).ToHashSet(StringComparer.Ordinal);
        resources.UnionWith(TranslationCatalog.GetTexts("en").Keys); resources.Add("UiFormattingLanguage");
        foreach (var (name, text) in Sources(".cs"))
        {
            foreach (Match match in Regex.Matches(text, "(?:LocalizationManager|L)\\.GetString\\(\\s*\"(?<key>[^\"]+)\""))
                Assert.True(TranslationCatalog.GetTexts("en").ContainsKey(match.Groups["key"].Value), name + ": " + match.Groups["key"].Value);
            foreach (Match match in Regex.Matches(text, "SetResourceReference\\([^,\\r\\n]+,\\s*\"(?<key>[^\"]+)\""))
                Assert.True(resources.Contains(match.Groups["key"].Value), name + ": " + match.Groups["key"].Value);
        }
    }

    [Fact]
    public void NamedInputAndListControlsHaveExplicitAccessibleNames()
    {
        string[] types = ["TextBox", "PasswordBox", "ComboBox", "DatePicker", "Slider", "DataGrid", "ListBox", "TreeView"];
        foreach (var (name, text) in Sources(".xaml"))
            foreach (var element in XDocument.Parse(text).Descendants().Where(element => types.Contains(element.Name.LocalName)))
            {
                if (element.Ancestors().Any(parent => parent.Name.LocalName is "ControlTemplate" or "DataTemplate")) continue;
                Assert.True(element.Attributes().Any(attribute => attribute.Name.LocalName is "AutomationProperties.Name" or "AutomationProperties.LabeledBy"),
                    name + ": " + element.Name.LocalName + " " + element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value);
            }
    }

    [Fact]
    public void VectorIconButtonsHaveExplicitAccessibleNames()
    {
        foreach (var (name, text) in Sources(".xaml"))
            foreach (var button in XDocument.Parse(text).Descendants().Where(element => element.Name.LocalName == "Button" && element.Elements().Any(child => child.Name.LocalName == "AppIcon")))
                Assert.True(button.Attributes().Any(attribute => attribute.Name.LocalName == "AutomationProperties.Name" && !string.IsNullOrWhiteSpace(attribute.Value)),
                    name + ": " + button.ToString(SaveOptions.DisableFormatting));
    }
}
