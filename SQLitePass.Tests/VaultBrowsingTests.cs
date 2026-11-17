using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Tests;

public partial class VaultSecurityTests
{
    [Fact]
    public async Task AByteLimitedPageNeverSkipsAnOversizedRowToIncludeLaterSmallRows()
    {
        using var vault = await TestVault.Create();
        var entries = new[]
        {
            new PwEntry { GroupId = vault.Root.Id, Title = "A" },
            new PwEntry { GroupId = vault.Root.Id, Title = "B", Notes = new string('x', 10000) },
            new PwEntry { GroupId = vault.Root.Id, Title = "C" },
            new PwEntry { GroupId = vault.Root.Id, Title = "D" }
        };
        // Deliberately insert out of order to exercise the bounded selection heap.
        await vault.Storage.SaveEntriesBatchAsync([entries[0], entries[2], entries[1], entries[3]], vault.Crypto);
        var query = new EntryPageQuery(PageSize: 200, MaximumPageBytes: 4096);
        var first = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query);
        Assert.Equal("A", Assert.Single(first.Entries).Title); Assert.NotNull(first.NextCursor);
        var large = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query, first.NextCursor);
        Assert.Equal("B", Assert.Single(large.Entries).Title); Assert.Equal(2, large.FirstIndex);
        var last = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query, large.NextCursor);
        Assert.Equal(new[] { "C", "D" }, last.Entries.Select(entry => entry.Title)); Assert.Null(last.NextCursor);
        var back = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query, last.PreviousCursor);
        Assert.Equal("B", Assert.Single(back.Entries).Title); Assert.Equal(2, back.FirstIndex);
        var beginning = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query, back.PreviousCursor);
        Assert.Equal("A", Assert.Single(beginning.Entries).Title); Assert.Null(beginning.PreviousCursor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeysetPagesHaveStableOrderingWithoutDuplicatesOrMissingEntries(bool descending)
    {
        using var vault = await TestVault.Create();
        var entries = Enumerable.Range(0, 13).Select(index => new PwEntry
        {
            GroupId = vault.Root.Id, Title = index % 2 == 0 ? "Same" : "same", UserName = index.ToString(), Password = "synthetic"
        }).ToArray();
        await vault.Storage.SaveEntriesBatchAsync(entries, vault.Crypto);
        var query = new EntryPageQuery(PageSize: 3, Descending: descending);
        EntryPageCursor? cursor = null; var ids = new List<Guid>();
        do
        {
            var page = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query, cursor);
            Assert.InRange(page.Entries.Count, 1, 3); Assert.Equal(13, page.MatchedCount); Assert.Equal(ids.Count + 1, page.FirstIndex);
            Assert.All(page.Entries, entry => Assert.False(entry.HasLoadedDetails));
            ids.AddRange(page.Entries.Select(entry => entry.Id)); cursor = page.NextCursor;
            foreach (var entry in page.Entries) entry.ClearSecrets();
        } while (cursor != null);
        Assert.Equal(entries.Select(entry => entry.Id).Order(), ids);
        Assert.Equal(13, ids.Distinct().Count());
    }

    [Fact]
    public async Task SortAndFocusApplyAcrossTheVaultRatherThanToTheVisiblePage()
    {
        using var vault = await TestVault.Create();
        var entries = Enumerable.Range(0, 10).Select(index => new PwEntry { GroupId = vault.Root.Id, Title = index.ToString("D2"), UserName = (9 - index).ToString("D2") }).ToArray();
        await vault.Storage.SaveEntriesBatchAsync(entries, vault.Crypto);
        var query = new EntryPageQuery(SortBy: EntrySortField.UserName, PageSize: 2);
        var first = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query);
        Assert.Equal(new[] { "09", "08" }, first.Entries.Select(entry => entry.Title));
        var descending = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query with { Descending = true });
        Assert.Equal(new[] { "00", "01" }, descending.Entries.Select(entry => entry.Title));
        var focused = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query, focusEntryId: entries[3].Id);
        Assert.Equal(7, focused.FirstIndex); Assert.Equal(entries[3].Id, focused.Entries[0].Id);
        Assert.True(focused.CurrentCursor!.IncludeBoundary);
        var next = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query, focused.NextCursor);
        Assert.Equal(new[] { "01", "00" }, next.Entries.Select(entry => entry.Title));
    }

    [Fact]
    public async Task SearchIsGlobalForActiveEntriesAndTrashSearchIsConfinedToTrash()
    {
        using var vault = await TestVault.Create();
        var folder = new PwGroup { Name = "Other", ParentId = vault.Root.Id }; await vault.Storage.SaveGroupAsync(folder, vault.Crypto);
        var active = new PwEntry { GroupId = folder.Id, Title = "active", Notes = "global needle" };
        var trash = new PwEntry { GroupId = vault.Trash.Id, Title = "trashed needle" };
        await vault.Storage.SaveEntriesBatchAsync([active, trash], vault.Crypto);
        var normal = await vault.Storage.LoadEntryPageAsync(vault.Crypto, new(GroupId: vault.Root.Id, SearchText: "needle", PageSize: 1));
        Assert.Equal(active.Id, Assert.Single(normal.Entries).Id); Assert.Equal(1, normal.MatchedCount);
        var recycled = await vault.Storage.LoadEntryPageAsync(vault.Crypto, new(SearchText: "needle", OnlyRecycleBin: true, PageSize: 1));
        Assert.Equal(trash.Id, Assert.Single(recycled.Entries).Id); Assert.Equal(1, recycled.MatchedCount);
    }

    [Fact]
    public async Task AWriteInvalidatesOldPageCursorsWithoutDegradingTheVault()
    {
        using var vault = await TestVault.Create(); await vault.Add("one"); await vault.Add("two");
        var query = new EntryPageQuery(PageSize: 1);
        var first = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query);
        await vault.Add("three");
        await Assert.ThrowsAsync<VaultPageChangedException>(() => vault.Storage.LoadEntryPageAsync(vault.Crypto, query, first.NextCursor));
        Assert.False(vault.Storage.IsReadOnly);
        Assert.Equal(3, (await vault.Storage.LoadEntryPageAsync(vault.Crypto, query)).MatchedCount);
    }

    [Fact]
    public async Task APageCursorCannotBeReusedForAnotherFilter()
    {
        using var vault = await TestVault.Create(); await vault.Add(); await vault.Add();
        var page = await vault.Storage.LoadEntryPageAsync(vault.Crypto, new(PageSize: 1));
        await Assert.ThrowsAsync<VaultPageChangedException>(() => vault.Storage.LoadEntryPageAsync(vault.Crypto,
            new(SearchText: "unrelated", PageSize: 1), page.NextCursor));
        Assert.False(vault.Storage.IsReadOnly);
    }

    [Fact]
    public async Task APageStillAuthenticatesRowsOutsideItsVisibleResults()
    {
        using var vault = await TestVault.Create(); await vault.Add(); var altered = await vault.Add();
        vault.Sql($"UPDATE sp_entries SET payload=zeroblob(length(payload)) WHERE id='{altered.Id}'");
        await Assert.ThrowsAsync<InvalidDataException>(() => vault.Storage.LoadEntryPageAsync(vault.Crypto, new(PageSize: 1, SearchText: "absent")));
        Assert.True(vault.Storage.IsReadOnly);
    }

    [Fact]
    public async Task CancelledPageLoadingLeavesTheVaultReadable()
    {
        using var vault = await TestVault.Create(); await vault.Add(); await vault.Add();
        using var cancellation = new CancellationTokenSource();
        var progress = new ReadProgress(value => { if (value.Phase == VaultReadPhase.LoadingEntries && value.Completed > 0) cancellation.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vault.Storage.LoadEntryPageAsync(vault.Crypto,
            new(PageSize: 1), cancellationToken: cancellation.Token, progress: progress));
        Assert.False(vault.Storage.IsReadOnly); Assert.Equal(2, (await vault.Storage.LoadEntriesAsync(null, vault.Crypto)).Count);
    }

    [Fact]
    public async Task PagedOverviewCannotOverwriteUnloadedDetails()
    {
        using var vault = await TestVault.Create(); var entry = await vault.Add();
        entry.CustomFields.Add(new PwCustomField { Key = "protected", Value = "retained", IsProtected = true });
        await vault.Storage.SaveEntryAsync(entry, vault.Crypto);
        var overview = Assert.Single((await vault.Storage.LoadEntryPageAsync(vault.Crypto, new())).Entries);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Storage.SaveEntryAsync(overview, vault.Crypto));
        Assert.Equal("retained", Assert.Single((await vault.Storage.LoadEntryAsync(entry.Id, vault.Crypto)).CustomFields).Value);
    }

    [Fact]
    public async Task GlobalExportAndTrashRevisionQueriesAreIndependentOfTheVisiblePage()
    {
        using var vault = await TestVault.Create();
        var active = Enumerable.Range(0, 7).Select(index => new PwEntry { GroupId = vault.Root.Id, Title = index.ToString("D2"), Password = "synthetic" }).ToArray();
        var trash = Enumerable.Range(0, 4).Select(index => new PwEntry { GroupId = vault.Trash.Id, Title = "trash" }).ToArray();
        await vault.Storage.SaveEntriesBatchAsync(active.Concat(trash), vault.Crypto);
        Assert.Equal(2, (await vault.Storage.LoadEntryPageAsync(vault.Crypto, new(PageSize: 2))).Entries.Count);
        string path = Path.Combine(vault.DirectoryPath, "export.csv");
        await vault.Storage.ExportCsvAsync(path, vault.Crypto);
        var exported = ImportExportService.ImportFromKeePassCsv(await File.ReadAllTextAsync(path), vault.Root.Id);
        Assert.Equal(7, exported.Count); Assert.DoesNotContain(exported, entry => entry.Title == "trash");
        var revisions = await vault.Storage.LoadEntryRevisionsAsync(vault.Trash.Id, vault.Crypto);
        Assert.Equal(4, revisions.Count); await vault.Storage.DeleteEntriesBatchAsync(revisions);
        Assert.Empty(await vault.Storage.LoadEntryRevisionsAsync(vault.Trash.Id, vault.Crypto));
        Assert.Equal(7, (await vault.Storage.LoadEntriesAsync(null, vault.Crypto)).Count);
    }

    private sealed class CsvProgress(Action<int> report) : IProgress<int> { public void Report(int value) => report(value); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CsvWriterRejectsAnExistingOrLateDatabaseDestination(bool appearsDuringExport)
    {
        using var vault = await TestVault.Create(); var entry = await vault.Add();
        string destination = Path.Combine(vault.DirectoryPath, "database-with-csv-extension.csv");
        byte[] original = await File.ReadAllBytesAsync(vault.Storage.FilePath);
        if (!appearsDuringExport) await File.WriteAllBytesAsync(destination, original);
        var progress = new CsvProgress(_ => { if (appearsDuringExport) File.WriteAllBytes(destination, original); });
        await Assert.ThrowsAsync<ArgumentException>(() => ImportExportService.ExportToKeePassCsvFileAsync(destination,
            [entry], new Dictionary<Guid, string>(), progress: progress));
        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Equal(entry.Id, Assert.Single((await vault.Storage.LoadEntryPageAsync(vault.Crypto, new())).Entries).Id);
        Assert.Empty(Directory.GetFiles(vault.DirectoryPath, ".SQLitePass-export-*.tmp"));
    }

    [Fact]
    public async Task CancelledGlobalExportKeepsItsDestinationAndTheVaultReadable()
    {
        using var vault = await TestVault.Create(); await vault.Add(); await vault.Add();
        string path = Path.Combine(vault.DirectoryPath, "export.csv"); await File.WriteAllTextAsync(path, "previous");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vault.Storage.ExportCsvAsync(path, vault.Crypto, cancellation.Token,
            new CsvProgress(count => { if (count == 1) cancellation.Cancel(); })));
        Assert.Equal("previous", await File.ReadAllTextAsync(path)); Assert.False(vault.Storage.IsReadOnly);
        Assert.Empty(Directory.GetFiles(vault.DirectoryPath, ".SQLitePass-export-*.tmp"));
        Assert.Equal(2, (await vault.Storage.LoadEntriesAsync(null, vault.Crypto)).Count);
    }

    [Fact]
    public async Task GlobalAutoTypeFindsOffPageEntriesAndRejectsStaleExplicitSelection()
    {
        using var vault = await TestVault.Create();
        await vault.Storage.SaveEntryAsync(new PwEntry { GroupId = vault.Root.Id, Title = "A visible entry" }, vault.Crypto);
        var target = new PwEntry { GroupId = vault.Root.Id, Title = "Z controlled target", Password = "synthetic" };
        await vault.Storage.SaveEntryAsync(target, vault.Crypto);
        var page = await vault.Storage.LoadEntryPageAsync(vault.Crypto, new(PageSize: 1));
        Assert.DoesNotContain(page.Entries, entry => entry.Id == target.Id);
        var candidates = await vault.Storage.FindAutoTypeCandidatesAsync(vault.Crypto, "Z controlled target - synthetic application");
        Assert.Equal(target.Id, Assert.Single(candidates).Id);
        long previous = target.Revision; target.Password = "changed"; await vault.Storage.SaveEntryAsync(target, vault.Crypto);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Storage.FindAutoTypeCandidatesAsync(vault.Crypto, "controlled target", target.Id, previous));
    }

    [Fact]
    public async Task GlobalAutoTypeRejectsDisabledAndTrashedEntriesAndReportsAmbiguity()
    {
        using var vault = await TestVault.Create();
        await vault.Storage.SaveEntriesBatchAsync([
            new PwEntry { GroupId = vault.Root.Id, Title = "match", AutoTypeEnabled = false },
            new PwEntry { GroupId = vault.Trash.Id, Title = "match" }
        ], vault.Crypto);
        Assert.Empty(await vault.Storage.FindAutoTypeCandidatesAsync(vault.Crypto, "match"));
        await vault.Storage.SaveEntriesBatchAsync([
            new PwEntry { GroupId = vault.Root.Id, Title = "match" },
            new PwEntry { GroupId = vault.Root.Id, Title = "match" },
            new PwEntry { GroupId = vault.Root.Id, Title = "match" }
        ], vault.Crypto);
        Assert.Equal(2, (await vault.Storage.FindAutoTypeCandidatesAsync(vault.Crypto, "match")).Count);
    }

    [Fact]
    public async Task ConfirmedAutoTypeLoadsCustomFieldsAndRechecksTrashAndEnabledState()
    {
        using var vault = await TestVault.Create(); var entry = await vault.Add();
        entry.AutoTypeSequence = "{S:token}";
        entry.CustomFields.Add(new PwCustomField { Key = "token", Value = "synthetic token", IsProtected = true });
        await vault.Storage.SaveEntryAsync(entry, vault.Crypto);
        var confirmed = await vault.Storage.LoadAutoTypeEntryAsync(entry.Id, entry.Revision, vault.Crypto);
        Assert.Equal("synthetic token", Assert.Single(AutoTypeService.Parse(confirmed)).Text);
        confirmed.ClearSecrets();
        entry.AutoTypeEnabled = false; await vault.Storage.SaveEntryAsync(entry, vault.Crypto);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Storage.LoadAutoTypeEntryAsync(entry.Id, entry.Revision, vault.Crypto));
        entry.AutoTypeEnabled = true; await vault.Storage.SaveEntryAsync(entry, vault.Crypto);
        long revision = await vault.Storage.MoveEntryAsync(entry.Id, entry.Revision, vault.Trash.Id, vault.Crypto);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Storage.LoadAutoTypeEntryAsync(entry.Id, revision, vault.Crypto));
    }
}
