using SQLitePass.Core.Models;

namespace SQLitePass.Tests;

public partial class VaultSecurityTests
{
    [Fact]
    public async Task ExpiryAndExactTagFiltersApplyAcrossPagesAndExcludeTrash()
    {
        using var vault = await TestVault.Create(); var today = new DateOnly(2026, 11, 17);
        var rows = new[]
        {
            new PwEntry { GroupId = vault.Root.Id, Title = "A", Tags = "work; shared", ExpiryDate = new DateTime(2026, 11, 16) },
            new PwEntry { GroupId = vault.Root.Id, Title = "B", Tags = "WORK, personal", ExpiryDate = new DateTime(2026, 11, 17) },
            new PwEntry { GroupId = vault.Root.Id, Title = "C", Tags = "work", ExpiryDate = new DateTime(2026, 12, 17) },
            new PwEntry { GroupId = vault.Root.Id, Title = "D", Tags = "work", ExpiryDate = new DateTime(2026, 12, 18) },
            new PwEntry { GroupId = vault.Root.Id, Title = "E", Tags = "homework", ExpiryDate = new DateTime(2026, 11, 18) },
            new PwEntry { GroupId = vault.Root.Id, Title = "F", Tags = "work" },
            new PwEntry { GroupId = vault.Trash.Id, Title = "trashed", Tags = "work", ExpiryDate = new DateTime(2026, 11, 16) }
        };
        await vault.Storage.SaveEntriesBatchAsync(rows, vault.Crypto);
        var expired = await vault.Storage.LoadEntryPageAsync(vault.Crypto, new(Expiry: EntryExpiryFilter.Expired, Tag: " work ", ExpiryReferenceDate: today));
        Assert.Equal("A", Assert.Single(expired.Entries).Title);
        var query = new EntryPageQuery(PageSize: 1, Expiry: EntryExpiryFilter.NextThirtyDays, Tag: "work", ExpiryReferenceDate: today);
        var first = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query);
        Assert.Equal(2, first.MatchedCount); Assert.Equal("B", Assert.Single(first.Entries).Title);
        var second = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query, first.NextCursor);
        Assert.Equal("C", Assert.Single(second.Entries).Title); Assert.Null(second.NextCursor);
        var back = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query, second.PreviousCursor);
        Assert.Equal("B", Assert.Single(back.Entries).Title);
        var trash = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query with { OnlyRecycleBin = true, Expiry = EntryExpiryFilter.Expired });
        Assert.Equal("trashed", Assert.Single(trash.Entries).Title);
    }

    [Fact]
    public async Task ChangingFilterOrReferenceDateInvalidatesAnExistingCursorWithoutDegradingTheVault()
    {
        using var vault = await TestVault.Create();
        await vault.Storage.SaveEntriesBatchAsync(Enumerable.Range(0, 3).Select(index => new PwEntry { GroupId = vault.Root.Id, Title = index.ToString(), Tags = "work" }), vault.Crypto);
        var query = new EntryPageQuery(PageSize: 1, Tag: "work", ExpiryReferenceDate: new DateOnly(2026, 11, 17));
        var page = await vault.Storage.LoadEntryPageAsync(vault.Crypto, query);
        await Assert.ThrowsAsync<VaultPageChangedException>(() => vault.Storage.LoadEntryPageAsync(vault.Crypto, query with { Tag = "home" }, page.NextCursor));
        await Assert.ThrowsAsync<VaultPageChangedException>(() => vault.Storage.LoadEntryPageAsync(vault.Crypto, query with { ExpiryReferenceDate = new DateOnly(2026, 11, 18) }, page.NextCursor));
        Assert.False(vault.Storage.IsReadOnly);
    }
}
