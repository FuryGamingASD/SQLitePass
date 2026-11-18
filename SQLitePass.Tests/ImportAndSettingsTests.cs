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
    [Theory]
    [InlineData("Group,Title,Password,Username,URL,Notes\ng,t,secret,user,https://example.invalid,n")]
    [InlineData("Group,Title,Username,Password,URL,Other\ng,t,user,secret,https://example.invalid,n")]
    public void KeePassColumnsMustMatchTheDeclaredOrder(string input) =>
        Assert.Throws<InvalidDataException>(() => ImportExportService.ImportFromKeePassCsv(input, Guid.NewGuid()));
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
