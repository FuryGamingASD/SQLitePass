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

    [Fact]
    public async Task StoredDraftReadsTheOriginalOnlyOnSaveAndClearsTheReloadedModel()
    {
        var original = new PwEntry { Title = "Original", Password = "synthetic original", Revision = 3 };
        var attachment = new PwAttachment { Data = [1, 2, 3], FileName = "synthetic.bin" };
        original.Attachments.Add(attachment);
        original.CustomFields.Add(new PwCustomField { Key = "token", Value = "synthetic token" });
        var editable = original.DeepClone();
        int reads = 0;
        using var draft = EntryEditDraft.TakeStoredEntry(editable, _ =>
        {
            reads++;
            return Task.FromResult(original);
        });
        Assert.Same(editable, draft.Current);
        Assert.Equal(0, reads);
        draft.Current.Password = "synthetic replacement";
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => draft.PrepareForSaveAsync(new CancellationToken(canceled: true)));
        Assert.Equal(0, reads);
        Assert.True(await draft.PrepareForSaveAsync());
        Assert.True(await draft.PrepareForSaveAsync());
        Assert.Equal(1, reads);

        var history = Assert.Single(draft.Current.History);
        var snapshot = EntrySerialization.Deserialize(history.SnapshotJson);
        try
        {
            Assert.Equal("synthetic original", snapshot.Password);
            Assert.Equal("synthetic token", Assert.Single(snapshot.CustomFields).Value);
            Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(snapshot.Attachments).Data);
            Assert.Empty(snapshot.History);
            Assert.Empty(original.Password);
            Assert.All(attachment.Data, value => Assert.Equal(0, value));
        }
        finally { snapshot.ClearSecrets(); }
    }

    [Fact]
    public void DroppingAStoredDraftClearsItsContentsWithoutReadingTheOriginal()
    {
        var editable = new PwEntry { Password = "synthetic", Revision = 1 };
        var attachment = new PwAttachment { Data = [1, 2, 3] };
        editable.Attachments.Add(attachment);
        using (EntryEditDraft.TakeStoredEntry(editable, _ => throw new InvalidOperationException("Unexpected reload")))
        {
            Assert.NotEmpty(editable.Password);
        }
        Assert.Empty(editable.Password);
        Assert.All(attachment.Data, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task CancellationDuringOriginalReloadClearsTheReloadedEntryAndLeavesHistoryUntouched()
    {
        var original = new PwEntry { Password = "synthetic original", Revision = 1 };
        var editable = original.DeepClone();
        editable.Password = "synthetic edit";
        using var cancellation = new CancellationTokenSource();
        using var draft = EntryEditDraft.TakeStoredEntry(editable, _ =>
        {
            cancellation.Cancel();
            return Task.FromResult(original);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => draft.PrepareForSaveAsync(cancellation.Token));
        Assert.Empty(original.Password);
        Assert.Empty(draft.Current.History);
        Assert.Null(draft.IsSaveRequired);
    }
}
