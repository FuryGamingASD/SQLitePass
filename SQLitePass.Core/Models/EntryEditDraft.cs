using System.Security.Cryptography;

namespace SQLitePass.Core.Models;

public sealed class EntryEditDraft : IDisposable
{
    public PwEntry Current { get; }
    public PwEntryHistory Original => _original ?? throw new InvalidOperationException("The original revision is loaded on save.");
    private readonly PwEntryHistory? _original;
    private readonly Func<CancellationToken, Task<PwEntry>>? _loadOriginal;
    private Func<CancellationToken, Task<PwEntry>>? _loadAttachments;
    private Func<CancellationToken, Task<PwEntry>>? _loadHistory;
    private readonly Guid[]? _originalHistory;
    public bool AttachmentsLoaded => _loadAttachments == null;
    public bool HistoryLoaded => _loadHistory == null;
    private bool? _prepared;
    public bool? IsSaveRequired => _prepared;
    private bool _disposed;
    private EntryEditDraft(PwEntry current, PwEntryHistory? original, Func<CancellationToken, Task<PwEntry>>? loadOriginal = null,
        Func<CancellationToken, Task<PwEntry>>? loadAttachments = null, Func<CancellationToken, Task<PwEntry>>? loadHistory = null)
    {
        Current = current;
        _original = original;
        _loadOriginal = loadOriginal;
        _loadAttachments = loadAttachments;
        _loadHistory = loadHistory;
        _originalHistory = loadHistory == null ? current.History.Select(item => item.Id).ToArray() : null;
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

    internal static EntryEditDraft TakeDeferredEntry(PwEntry entry, Func<CancellationToken, Task<PwEntry>> loadOriginal,
        Func<CancellationToken, Task<PwEntry>> loadAttachments, Func<CancellationToken, Task<PwEntry>> loadHistory) =>
        new(entry, original: null, loadOriginal, loadAttachments, loadHistory);

    public Task LoadAttachmentsAsync(CancellationToken token = default) => LoadDetailsAsync(_loadAttachments, ReplaceAttachmentsFrom, token);
    public Task LoadHistoryAsync(CancellationToken token = default) => LoadDetailsAsync(_loadHistory, AdoptHistory, token);

    private async Task LoadDetailsAsync(Func<CancellationToken, Task<PwEntry>>? load, Action<PwEntry> adopt, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (load == null) return;
        var details = await load(token);
        try
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            adopt(details);
        }
        finally { details.ClearSecrets(); }
    }

    public void ReplaceAttachmentsFrom(PwEntry source)
    {
        foreach (var attachment in Current.Attachments) CryptographicOperations.ZeroMemory(attachment.Data);
        Current.Attachments.Clear();
        foreach (var attachment in source.Attachments) Current.Attachments.Add(attachment);
        source.Attachments.Clear();
        _loadAttachments = null;
        if (HistoryLoaded) Current.MarkDetailsLoaded();
    }

    private void AdoptHistory(PwEntry source)
    {
        foreach (var revision in source.History) Current.History.Add(revision);
        source.History.Clear();
        _loadHistory = null;
        if (AttachmentsLoaded) Current.MarkDetailsLoaded();
    }

    // Compare on a worker, then update on the caller's context: collections may be bound to a UI.
    // Preparation finalizes this draft; the editor must not modify it afterwards.
    public async Task<bool> PrepareForSaveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_prepared.HasValue) return _prepared.Value;
        PwEntry? storedOriginal = null;
        PwEntryHistory? storedSnapshot = null;
        try
        {
            PwEntryHistory original;
            Guid[]? originalHistory = _originalHistory;
            if (_loadOriginal != null)
            {
                storedOriginal = await _loadOriginal(cancellationToken);
                if (storedOriginal.Id != Current.Id || storedOriginal.Revision != Current.Revision)
                    throw new VaultConflictException("The entry changed while it was being edited.");
                storedSnapshot = await Task.Run(storedOriginal.CreateHistorySnapshot, cancellationToken);
                original = storedSnapshot;
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_prepared.HasValue) return _prepared.Value;
                originalHistory ??= storedOriginal.History.Select(item => item.Id).ToArray();
                if (!AttachmentsLoaded) ReplaceAttachmentsFrom(storedOriginal);
                if (!HistoryLoaded) AdoptHistory(storedOriginal);
            }
            else original = Original;
            var changes = await Task.Run(() => (
                Content: !EntrySerialization.SnapshotEquals(Current, original.SnapshotJson),
                History: !originalHistory!.SequenceEqual(Current.History.Select(item => item.Id))), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_prepared.HasValue) return _prepared.Value;
            if (Current.Revision > 0 && !changes.Content && !changes.History) return (_prepared = false).Value;
            if (Current.Revision > 0 && changes.Content)
            {
                Current.History.Add(new PwEntryHistory
                {
                    EntryId = Current.Id, Title = original.Title, UserName = original.UserName, Password = original.Password,
                    Url = original.Url, Notes = original.Notes, SnapshotJson = original.SnapshotJson, SavedAt = DateTime.UtcNow
                });
            }
            Current.UpdatedAt = DateTime.UtcNow;
            return (_prepared = true).Value;
        }
        finally
        {
            storedOriginal?.ClearSecrets();
            storedSnapshot?.ClearSensitiveValues();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Current.ClearSecrets();
        if (_original is { } original)
            original.ClearSensitiveValues();
    }
}
