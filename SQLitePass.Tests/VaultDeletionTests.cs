using SQLitePass.Core.Models;

namespace SQLitePass.Tests;

public partial class VaultSecurityTests
{
    [Fact]
    public async Task StaleConfirmationCannotPermanentlyDeleteANewerEntry()
    {
        using var vault = await TestVault.Create(backup: true); var entry = await vault.Add();
        long confirmedRevision = entry.Revision;
        entry.Password = "updated after confirmation"; await vault.Storage.SaveEntryAsync(entry, vault.Crypto);
        byte[] backup = File.ReadAllBytes(vault.Storage.BackupFilePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Storage.DeleteEntryAsync(entry.Id, confirmedRevision));
        Assert.Equal(entry.Password, (await vault.Storage.LoadEntryAsync(entry.Id, vault.Crypto)).Password);
        Assert.Equal(backup, File.ReadAllBytes(vault.Storage.BackupFilePath));
        Assert.False(vault.Storage.IsReadOnly);
    }

    [Fact]
    public async Task StaleRevisionRollsBackTheWholePermanentDeletionBatch()
    {
        using var vault = await TestVault.Create(); var first = await vault.Add("one"); var second = await vault.Add("two");
        var confirmed = new Dictionary<Guid, long> { [first.Id] = first.Revision, [second.Id] = second.Revision };
        second.Password = "updated"; await vault.Storage.SaveEntryAsync(second, vault.Crypto);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Storage.DeleteEntriesBatchAsync(confirmed));
        Assert.Equal(2, (await vault.Storage.LoadEntriesAsync(null, vault.Crypto)).Count);
        confirmed[second.Id] = second.Revision;
        await vault.Storage.DeleteEntriesBatchAsync(confirmed);
        Assert.Empty(await vault.Storage.LoadEntriesAsync(null, vault.Crypto));
    }

    [Fact]
    public async Task RestoreFromTrashPreservesUnloadedDetailsAndRejectsRepeatedRestore()
    {
        using var vault = await TestVault.Create(); var entry = await vault.Add();
        entry.CustomFields.Add(new PwCustomField { Key = "protected", Value = "retained", IsProtected = true });
        entry.Attachments.Add(new PwAttachment { FileName = "synthetic.bin", Data = [1, 2, 3], FileSize = 3 });
        await vault.Storage.SaveEntryAsync(entry, vault.Crypto);
        long trashRevision = await vault.Storage.MoveEntryAsync(entry.Id, entry.Revision, vault.Trash.Id, vault.Crypto);
        long restoredRevision = await vault.Storage.RestoreEntryAsync(entry.Id, trashRevision, vault.Root.Id, vault.Crypto);
        var restored = await vault.Storage.LoadEntryAsync(entry.Id, vault.Crypto);
        Assert.Equal(vault.Root.Id, restored.GroupId); Assert.Equal(restoredRevision, restored.Revision);
        Assert.Equal("retained", Assert.Single(restored.CustomFields).Value);
        Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(restored.Attachments).Data);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Storage.RestoreEntryAsync(entry.Id, restored.Revision, vault.Root.Id, vault.Crypto));
    }

    [Fact]
    public async Task RestoreRejectsTrashMissingDestinationsAndStaleRevisions()
    {
        using var vault = await TestVault.Create(); var entry = await vault.Add();
        long revision = await vault.Storage.MoveEntryAsync(entry.Id, entry.Revision, vault.Trash.Id, vault.Crypto);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Storage.RestoreEntryAsync(entry.Id, revision, vault.Trash.Id, vault.Crypto));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Storage.RestoreEntryAsync(entry.Id, revision, Guid.NewGuid(), vault.Crypto));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Storage.RestoreEntryAsync(entry.Id, revision - 1, vault.Root.Id, vault.Crypto));
        Assert.Equal(vault.Trash.Id, (await vault.Storage.LoadEntryAsync(entry.Id, vault.Crypto)).GroupId);
    }
}
