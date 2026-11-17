using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Tests;

public sealed partial class AttachmentFileTests
{
    private sealed class ExportProgress(Action<int> callback) : IProgress<int>
    {
        public void Report(int value) => callback(value);
    }

    [Fact]
    public async Task StreamingCsvMatchesTheExistingFormatWithQuotesUnicodeAndMultilineFields()
    {
        string path = Path.Combine(_folder, "export.csv"); Guid group = Guid.NewGuid();
        var entries = new[] { new PwEntry { GroupId = group, Title = "quoted \"title\"", UserName = "synthetic", Password = "sëcret", Notes = "first\r\nsecond" } };
        var groups = new Dictionary<Guid, string> { [group] = "Root/Subfolder" };
        await ImportExportService.ExportToKeePassCsvFileAsync(path, entries, groups);
        string csv = await File.ReadAllTextAsync(path);
        Assert.Equal(ImportExportService.ExportToKeePassCsv(entries, groups), csv);
        var imported = Assert.Single(ImportExportService.ImportFromKeePassCsv(csv, group));
        Assert.Equal(entries[0].Password, imported.Password); Assert.Equal(entries[0].Notes, imported.Notes);
        Assert.Empty(Directory.GetFiles(_folder, ".SQLitePass-export-*.tmp"));
    }

    [Fact]
    public async Task CancellationDuringCsvWritingPreservesThePreviousDestination()
    {
        string path = Path.Combine(_folder, "export.csv"); await File.WriteAllTextAsync(path, "previous export");
        using var cancellation = new CancellationTokenSource();
        var entries = new[] { new PwEntry { Title = "first" }, new PwEntry { Title = "second" } };
        var progress = new ExportProgress(count => { if (count == 1) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ImportExportService.ExportToKeePassCsvFileAsync(path, entries, new Dictionary<Guid, string>(), cancellation.Token, progress));
        Assert.Equal("previous export", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(_folder, ".SQLitePass-export-*.tmp"));
    }

    [Fact]
    public async Task FailedCsvReplacementPreservesThePreviousDestination()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.Combine(_folder, "export.csv"); await File.WriteAllTextAsync(path, "previous export");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAnyAsync<IOException>(() => ImportExportService.ExportToKeePassCsvFileAsync(path, [new PwEntry { Password = "synthetic" }], new Dictionary<Guid, string>()));
        Assert.Equal("previous export", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(_folder, ".SQLitePass-export-*.tmp"));
    }
}
