using SQLitePass.Core.Models;

namespace SQLitePass.Tests;

public sealed class EntryEditDraftTests
{
    [Fact]
    public async Task UnchangedExistingDraftDoesNotCreateARevisionOrChangeItsTimestamp()
    {
        var source = new PwEntry { Title = "Synthetic", Password = "synthetic", Revision = 3 };
        using var draft = EntryEditDraft.Create(source);
        Assert.False(await draft.PrepareForSaveAsync()); Assert.False(await draft.PrepareForSaveAsync());
        Assert.Equal(source.UpdatedAt, draft.Current.UpdatedAt); Assert.Empty(draft.Current.History);
    }

    [Fact]
    public async Task ChangedDraftKeepsOneCompleteOriginalRevisionAndPreparationIsIdempotent()
    {
        var source = new PwEntry { Title = "Original", Password = "original secret", Revision = 3 };
        source.CustomFields.Add(new PwCustomField { Key = "token", Value = "original token" });
        source.Attachments.Add(new PwAttachment { Data = [1, 2, 3], FileName = "synthetic.bin" });
        using var draft = EntryEditDraft.Create(source); draft.Current.Password = "changed";
        Assert.True(await draft.PrepareForSaveAsync()); Assert.True(await draft.PrepareForSaveAsync());
        var revision = Assert.Single(draft.Current.History);
        var original = EntrySerialization.Deserialize(revision.SnapshotJson);
        try
        {
            Assert.Equal("original secret", original.Password);
            Assert.Equal("original token", Assert.Single(original.CustomFields).Value);
            Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(original.Attachments).Data);
        }
        finally { original.ClearSecrets(); source.ClearSecrets(); }
    }

    [Fact]
    public async Task ClearingOnlyHistoryStillRequiresSavingWithoutRestoringTheDeletedHistory()
    {
        var source = new PwEntry { Revision = 3 }; source.History.Add(source.CreateHistorySnapshot());
        using var draft = EntryEditDraft.Create(source); draft.Current.History.Clear();
        Assert.True(await draft.PrepareForSaveAsync()); Assert.Empty(draft.Current.History); Assert.Single(source.History);
    }

    [Fact]
    public async Task NewDraftIsSaveableAndCancelledPreparationDoesNotAddHistory()
    {
        using var fresh = EntryEditDraft.Create(new PwEntry()); Assert.True(await fresh.PrepareForSaveAsync()); Assert.Empty(fresh.Current.History);
        using var existing = EntryEditDraft.Create(new PwEntry { Revision = 1 }); existing.Current.Title = "changed";
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => existing.PrepareForSaveAsync(cancellation.Token)); Assert.Empty(existing.Current.History);
        Assert.True(await existing.PrepareForSaveAsync()); Assert.Single(existing.Current.History);
    }
}
