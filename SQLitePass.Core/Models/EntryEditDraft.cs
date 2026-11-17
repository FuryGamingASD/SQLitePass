namespace SQLitePass.Core.Models;

public sealed class EntryEditDraft : IDisposable
{
    public PwEntry Current { get; }
    public PwEntryHistory Original { get; }
    private readonly Guid[] _originalHistory;
    private bool? _prepared;
    public bool? IsSaveRequired => _prepared;
    private bool _disposed;
    private EntryEditDraft(PwEntry current, PwEntryHistory original)
    { Current = current; Original = original; _originalHistory = current.History.Select(item => item.Id).ToArray(); }

    public static EntryEditDraft Create(PwEntry source)
    {
        var current = source.DeepClone();
        try { return new(current, source.CreateHistorySnapshot()); }
        catch { current.ClearSecrets(); throw; }
    }

    // Compare on a worker, then update on the caller's context: collections may be bound to a UI.
    // Preparation finalizes this draft; the editor must not modify it afterwards.
    public async Task<bool> PrepareForSaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_prepared.HasValue) return _prepared.Value;
        var changes = await Task.Run(() => (
            Content: EntrySerialization.Serialize(Current, includeHistory: false) != Original.SnapshotJson,
            History: !_originalHistory.SequenceEqual(Current.History.Select(item => item.Id))), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Current.Revision > 0 && !changes.Content && !changes.History) return (_prepared = false).Value;
        if (Current.Revision > 0 && changes.Content)
            Current.History.Add(new PwEntryHistory
            {
                EntryId = Current.Id, Title = Original.Title, UserName = Original.UserName, Password = Original.Password,
                Url = Original.Url, Notes = Original.Notes, SnapshotJson = Original.SnapshotJson, SavedAt = DateTime.UtcNow
            });
        Current.UpdatedAt = DateTime.UtcNow;
        return (_prepared = true).Value;
    }

    public void Dispose()
    {
        _disposed = true;
        Current.ClearSecrets();
        Original.Title = Original.Password = Original.UserName = Original.Url = Original.Notes = Original.SnapshotJson = string.Empty;
    }
}
