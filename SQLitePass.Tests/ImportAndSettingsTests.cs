using SQLitePass.Core.Services;

namespace SQLitePass.Tests;

public class ImportAndSettingsTests
{
    [Theory]
    [InlineData("{\"items\":{}}")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("{\"items\":[{\"type\":\"login\"}]}")]
    public void InvalidBitwardenItemShapesAreRejected(string json) => Assert.Throws<InvalidDataException>(() => ImportExportService.ImportFromBitwardenJson(json, Guid.NewGuid()));

    [Fact]
    public void BitwardenUnsupportedTypesAndExtraPropertiesRemainAvailableForReview()
    {
        const string json = """
        {"items":[{"type":3,"name":"Synthetic card","card":{"number":"1111"},"future":{"value":"retained"}},
        {"type":1,"name":"Synthetic login","login":{"username":"test","password":"test","uris":[{"uri":"https://one.invalid"},{"uri":"https://two.invalid"}]}}]}
        """;
        var entries = ImportExportService.ImportFromBitwardenJson(json, Guid.NewGuid());
        Assert.Equal(3, entries[0].SourceItemType); Assert.Equal(1, entries[1].SourceItemType);
        Assert.Contains("future", Assert.Single(entries[0].CustomFields).Value);
        Assert.Contains("https://two.invalid", Assert.Single(entries[1].CustomFields).Value);
        Assert.All(entries.SelectMany(entry => entry.CustomFields), field => Assert.True(field.IsProtected));
    }
    [Theory]
    [InlineData("a,b\n\"unclosed,value")]
    [InlineData("a,b\nvalue\"quote,other")]
    [InlineData("a,b\n\"closed\"junk,other")]
    public void MalformedCsvIsRejected(string input) => Assert.Throws<InvalidDataException>(() => ImportExportService.ParseCsvRecords(input));
    [Fact]
    public void UnknownColumnsAreNotGuessed() => Assert.Throws<InvalidDataException>(() => ImportExportService.ImportFromBrowserCsv("first,second,third\na,b,c", Guid.NewGuid()));
    [Fact]
    public void ImportsOfficialKeePass1CsvFormat()
    {
        const string input = "\"Account\",\"Login Name\",\"Password\",\"Web Site\",\"Comments\"\r\n\"Mail\",\"fixture\",\"synthetic secret\",\"https://example.invalid\",\"fixture note\"\r\n";
        var entry = Assert.Single(ImportExportService.ImportFromKeePassCsv(input, Guid.NewGuid()));
        Assert.Equal("Mail", entry.Title);
        Assert.Equal("fixture", entry.UserName);
        Assert.Equal("synthetic secret", entry.Password);
        Assert.Equal("https://example.invalid", entry.Url);
        Assert.Equal("fixture note", entry.Notes);
        Assert.Equal("", entry.SourceGroupPath);
    }

    [Fact]
    public void KeePassCsvDecodesBackslashEscapedQuotesAndBackslashes()
    {
        const string input = """
            "Account","Login Name","Password","Web Site","Comments"
            "Mail","fixture","Pass\,word\\path\"quoted","https://example.invalid","first line
            second line"
            """;

        var entry = Assert.Single(ImportExportService.ImportFromKeePassCsv(input, Guid.NewGuid()));
        Assert.Equal("Pass,word\\path\"quoted", entry.Password);
        Assert.Equal("first line\nsecond line", entry.Notes);
    }

    [Fact]
    public void KeePassGroupedCsvDecodesEscapedGroupPathSeparators()
    {
        const string input = """
            "Group","Account","Login Name","Password","Web Site","Comments"
            "Group A\\Group B","Mail","fixture","synthetic secret","https://example.invalid","fixture note"
            """;

        var entry = Assert.Single(ImportExportService.ImportFromKeePassCsv(input, Guid.NewGuid()));
        Assert.Equal("Group A\\Group B", entry.SourceGroupPath);
    }

    [Fact]
    public void KeePassCsvMapsReorderedSemicolonColumnsAndPreservesExtraFields()
    {
        const string input = "Password;Folder;Name;Login Name;Website;Comments;TOTP\nsecret;Accounts/Email;Mail;fixture;https://example.invalid;notes;synthetic-seed\n";
        var entry = Assert.Single(ImportExportService.ImportFromKeePassCsv(input, Guid.NewGuid()));
        Assert.Equal("Mail", entry.Title);
        Assert.Equal("fixture", entry.UserName);
        Assert.Equal("secret", entry.Password);
        Assert.Equal("Accounts/Email", entry.SourceGroupPath);
        var extra = Assert.Single(entry.CustomFields);
        Assert.Equal("TOTP", extra.Key);
        Assert.Equal("synthetic-seed", extra.Value);
        Assert.True(extra.IsProtected);
    }

    [Fact]
    public void SQLitePassCsvRoundTripsBackslashesAndQuotesWithoutChangingThem()
    {
        var entry = new SQLitePass.Core.Models.PwEntry
        {
            Title = "Fixture",
            UserName = "fixture-user",
            Password = "first\\\\second\\\"quoted\"",
            Url = "https://example.invalid",
            Notes = "line one\\\\line two"
        };
        var groupId = Guid.NewGuid();
        entry.GroupId = groupId;
        string csv = ImportExportService.ExportToKeePassCsv([entry], new Dictionary<Guid, string> { [groupId] = "General" });

        var imported = Assert.Single(ImportExportService.ImportFromKeePassCsv(csv, Guid.NewGuid()));
        Assert.Equal(entry.Password, imported.Password);
        Assert.Equal(entry.Notes, imported.Notes);
    }

    [Fact]
    public void AmbiguousKeePassCsvAliasesAreRejected() =>
        Assert.Throws<InvalidDataException>(() => ImportExportService.ImportFromKeePassCsv(
            "Group,Title,Account,Username,Password,URL,Notes\ng,t,a,u,p,url,n", Guid.NewGuid()));

    [Fact]
    public void CsvParserDetectsTabSeparatedRecords() =>
        Assert.Equal(new[] { "a", "b" }, Assert.Single(ImportExportService.ParseCsvRecords("a\tb\nc\td")));
    [Theory]
    [InlineData("url,username,password,pwd\nhttps://example.invalid,user,one,two")]
    [InlineData("url,username,login,password\nhttps://example.invalid,one,two,secret")]
    public void DuplicateBrowserFieldAliasesAreRejected(string input) =>
        Assert.Throws<InvalidDataException>(() => ImportExportService.ImportFromBrowserCsv(input, Guid.NewGuid()));
    [Fact]
    public void PredictablePasswordsAreNotReportedAsStrong()
    {
        Assert.Equal(0, PasswordGenerator.CalculateEntropy(new string('a', 28)));
        Assert.InRange(PasswordGenerator.CalculateEntropy("Password123!"), 0, 20);
        Assert.Equal(20, PasswordGenerator.CalculateEntropy("À9!PaSSwORd£6$"));
        Assert.InRange(PasswordGenerator.CalculateEntropy("Ab1!Ab1!Ab1!Ab1!"), 0, 30);
    }
    [Fact]
    public void EmptyCharacterSelectionIsRejected() => Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(new PasswordGeneratorOptions
    { IncludeUpperCase = false, IncludeLowerCase = false, IncludeDigits = false, IncludeSpecial = false }));
    [Fact]
    public void SettingsAreValidatedAndPreviousVersionCanBeRecovered()
    {
        string folder = Path.Combine(Path.GetTempPath(), "SQLitePass-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AppSettingsStore(folder);
            store.Save(new AppSettings { ClipboardClearSeconds = 9999, AutoLockMinutes = -1, RecentVaults = null! });
            var settings = store.Load(); Assert.Equal(300, settings.ClipboardClearSeconds); Assert.Equal(5, settings.AutoLockMinutes); Assert.Empty(settings.RecentVaults);
            settings.ClipboardClearSeconds = 20; store.Save(settings);
            File.WriteAllText(Path.Combine(folder, "appsettings.json"), "{broken");
            Assert.Equal(300, store.Load().ClipboardClearSeconds);
        }
        finally
        {
            string resolved = Path.GetFullPath(folder);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("SQLitePass-settings-")) throw new InvalidOperationException("Invalid test cleanup path.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }

    private sealed class SettingsFixture : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "SQLitePass-settings-" + Guid.NewGuid().ToString("N"));
        public AppSettingsStore Store => new(Folder);
        public void Dispose()
        {
            string resolved = Path.GetFullPath(Folder);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("SQLitePass-settings-")) throw new InvalidOperationException("Invalid test cleanup path.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }

    [Fact]
    public void ConcurrentPreferenceEditsMergeIndependentChanges()
    {
        using var fixture = new SettingsFixture();
        var firstStore = fixture.Store; var secondStore = fixture.Store;
        var first = firstStore.Load(); var second = secondStore.Load();
        first.DarkMode = false; firstStore.Save(first);
        second.ClipboardClearSeconds = 30; secondStore.Save(second);
        var saved = fixture.Store.Load();
        Assert.False(saved.DarkMode); Assert.Equal(30, saved.ClipboardClearSeconds);
        Assert.False(second.DarkMode); Assert.Equal(2, saved.Revision);
        second.AutoLockMinutes = 10; secondStore.Save(second);
        Assert.False(fixture.Store.Load().DarkMode);
    }

    [Fact]
    public void ConflictingPreferenceEditsAreRejectedWithoutOverwriting()
    {
        using var fixture = new SettingsFixture(); var store = fixture.Store;
        var first = store.Load(); var second = fixture.Store.Load();
        first.ClipboardClearSeconds = 20; store.Save(first);
        second.ClipboardClearSeconds = 30;
        Assert.Throws<SettingsConflictException>(() => fixture.Store.Save(second));
        Assert.Equal(20, store.Load().ClipboardClearSeconds); Assert.Equal(0, second.Revision);
    }

    [Fact]
    public async Task ConcurrentSettingsUpdatesDoNotLoseIncrements()
    {
        using var fixture = new SettingsFixture();
        fixture.Store.Update(s => s.ClipboardClearSeconds = 3);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var store = fixture.Store;
            for (int i = 0; i < 8; i++) store.Update(s => s.ClipboardClearSeconds++);
        })));
        var saved = fixture.Store.Load();
        Assert.Equal(35, saved.ClipboardClearSeconds); Assert.Equal(33, saved.Revision);
    }

    [Fact]
    public async Task SettingsUpdateWaitsForLeaseHeldBeyondPreviousTimeout()
    {
        using var fixture = new SettingsFixture();
        Directory.CreateDirectory(fixture.Folder);
        using var heldLease = new FileStream(Path.Combine(fixture.Folder, "appsettings.json.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var updateStarted = new ManualResetEventSlim();
        AppSettings? saved = null; Exception? failure = null;
        var writer = new Thread(() =>
        {
            updateStarted.Set();
            try { saved = fixture.Store.Update(settings => settings.ClipboardClearSeconds = 21); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        writer.Start();
        Assert.True(updateStarted.Wait(TimeSpan.FromSeconds(10)));

        await Task.Delay(TimeSpan.FromSeconds(5.5));
        Assert.False(writer.Join(0));
        heldLease.Dispose();

        Assert.True(writer.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.NotNull(saved);
        Assert.Equal(21, saved.ClipboardClearSeconds);
    }

    [Fact]
    public void SavingAfterRecoveryPreservesReadableBackup()
    {
        using var fixture = new SettingsFixture(); var store = fixture.Store;
        store.Update(s => s.ClipboardClearSeconds = 20);
        store.Update(s => s.ClipboardClearSeconds = 30);
        string path = Path.Combine(fixture.Folder, "appsettings.json");
        File.WriteAllText(path, "{broken");
        var recovered = store.Load(); Assert.Equal(20, recovered.ClipboardClearSeconds);
        recovered.DarkMode = false; store.Save(recovered);
        File.WriteAllText(path, "{broken-again");
        Assert.Equal(20, store.Load().ClipboardClearSeconds);
        File.Delete(path);
        Assert.Equal(20, store.Load().ClipboardClearSeconds);
    }

    [Fact]
    public void FolderExpansionDoesNotOverwriteOtherPreferencesOrOtherVaults()
    {
        using var fixture = new SettingsFixture(); var dialog = fixture.Store.Load();
        string first = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}", second = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}";
        fixture.Store.Update(s => s.GroupExpansion[first] = false);
        fixture.Store.Update(s => s.GroupExpansion[second] = true);
        dialog.DarkMode = false; fixture.Store.Save(dialog);
        var saved = fixture.Store.Load();
        Assert.False(saved.GroupExpansion[first]); Assert.True(saved.GroupExpansion[second]); Assert.False(saved.DarkMode);
    }
}
