namespace SQLitePass.Core.Models;

public sealed class EntryEditDraft : IDisposable
{
    public PwEntry Current { get; }
    public PwEntryHistory Original => _original ?? throw new InvalidOperationException("The original revision is loaded on save.");
    private readonly PwEntryHistory? _original;
    private readonly Func<CancellationToken, Task<PwEntry>>? _loadOriginal;
    private readonly Guid[] _originalHistory;
    private bool? _prepared;
    public bool? IsSaveRequired => _prepared;
    private bool _disposed;
    private EntryEditDraft(PwEntry current, PwEntryHistory? original, Func<CancellationToken, Task<PwEntry>>? loadOriginal = null)
    {
        Current = current;
        _original = original;
        _loadOriginal = loadOriginal;
        _originalHistory = current.History.Select(item => item.Id).ToArray();
    }

    public static EntryEditDraft Create(PwEntry source)
    {
        var current = source.DeepClone();
        try { return new(current, source.CreateHistorySnapshot()); }
        catch { current.ClearSecrets(); throw; }
    }

    // The draft owns the loaded model. The loader must return a separately owned model of the original revision.
    public static EntryEditDraft TakeStoredEntry(PwEntry entry, Func<CancellationToken, Task<PwEntry>> loadOriginal)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(loadOriginal);
        if (!entry.HasLoadedDetails || entry.Revision <= 0)
            throw new ArgumentException("The draft requires a complete stored entry.", nameof(entry));
        return new(entry, original: null, loadOriginal);
    }

    // Compare on a worker, then update on the caller's context: collections may be bound to a UI.
    // Preparation finalizes this draft; the editor must not modify it afterwards.
    public async Task<bool> PrepareForSaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_prepared.HasValue) return _prepared.Value;
        PwEntry? storedOriginal = null;
        try
        {
            if (_loadOriginal != null)
            {
                storedOriginal = await _loadOriginal(cancellationToken);
                if (storedOriginal.Id != Current.Id || storedOriginal.Revision != Current.Revision)
                    throw new VaultConflictException("The entry changed while it was being edited.");
            }
            var changes = await Task.Run(() =>
            {
                var original = storedOriginal?.CreateHistorySnapshot() ?? Original;
                return (
                    Original: original,
                    Content: EntrySerialization.Serialize(Current, includeHistory: false) != original.SnapshotJson,
                    History: !_originalHistory.SequenceEqual(Current.History.Select(item => item.Id)));
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_prepared.HasValue) return _prepared.Value;
            if (Current.Revision > 0 && !changes.Content && !changes.History) return (_prepared = false).Value;
            if (Current.Revision > 0 && changes.Content)
            {
                var original = changes.Original;
                Current.History.Add(new PwEntryHistory
                {
                    EntryId = Current.Id, Title = original.Title, UserName = original.UserName, Password = original.Password,
                    Url = original.Url, Notes = original.Notes, SnapshotJson = original.SnapshotJson, SavedAt = DateTime.UtcNow
                });
            }
            Current.UpdatedAt = DateTime.UtcNow;
            return (_prepared = true).Value;
        }
        finally { storedOriginal?.ClearSecrets(); }
    }

    public void Dispose()
    {
        _disposed = true;
        Current.ClearSecrets();
        if (_original is { } original)
            original.Title = original.Password = original.UserName = original.Url = original.Notes = original.SnapshotJson = string.Empty;
    }
}
