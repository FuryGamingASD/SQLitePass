using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Core.Database;

public sealed partial class SqliteStorageProvider
{
    private readonly record struct EntryOrderKey(string Value, Guid Id);

    private static EntryOrderKey OrderKey(PwEntry entry, EntrySortField field) => new(field switch
    {
        EntrySortField.UserName => entry.UserName ?? "",
        EntrySortField.Password => entry.Password ?? "",
        EntrySortField.Url => entry.Url ?? "",
        EntrySortField.Notes => entry.Notes ?? "",
        EntrySortField.UpdatedAt => entry.UpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        _ => entry.Title ?? ""
    }, entry.Id);

    private static IComparer<EntryOrderKey> OrderComparer(bool descending) => Comparer<EntryOrderKey>.Create((a, b) =>
    {
        int value = StringComparer.OrdinalIgnoreCase.Compare(a.Value, b.Value);
        if (value != 0) return descending ? -value : value;
        return a.Id.CompareTo(b.Id);
    });

    private static string SnapshotStamp(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT authentication FROM sp_metadata WHERE singleton=1";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes((string)command.ExecuteScalar()!)));
    }

    private static string QueryStamp(EntryPageQuery query) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(query)));

    private static long EstimatedEntryBytes(PwEntry entry) => 1024L + 2L *
        ((long)(entry.Title?.Length ?? 0) + (entry.UserName?.Length ?? 0) + (entry.Password?.Length ?? 0) +
        (entry.Url?.Length ?? 0) + (entry.Notes?.Length ?? 0) + (entry.Tags?.Length ?? 0) + (entry.AutoTypeSequence?.Length ?? 0));

    private static bool Matches(PwEntry entry, EntryPageQuery query, VaultMetadata metadata)
    {
        bool trash = entry.GroupId == metadata.RecycleBinGroupId;
        if (query.OnlyRecycleBin ? !trash : trash) return false;
        if (query.Expiry != EntryExpiryFilter.All)
        {
            if (!entry.ExpiryDate.HasValue) return false;
            var date = DateOnly.FromDateTime(entry.ExpiryDate.Value);
            var reference = query.ExpiryReferenceDate!.Value;
            if (query.Expiry == EntryExpiryFilter.Expired ? date >= reference : date < reference || date > reference.AddDays(30)) return false;
        }
        if (query.Tag.Trim() is { Length: > 0 } tag &&
            !(entry.Tags ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(tag, StringComparer.OrdinalIgnoreCase)) return false;
        string search = query.SearchText.Trim();
        if (search.Length == 0) return !query.GroupId.HasValue || entry.GroupId == query.GroupId;
        // Searching from a normal folder remains a global search of active entries.
        return entry.Title?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
            entry.UserName?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
            entry.Url?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
            entry.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
            entry.Tags?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }

    public Task<VaultEntryPage> LoadEntryPageAsync(CryptoEngine crypto, EntryPageQuery query,
        EntryPageCursor? cursor = null, Guid? focusEntryId = null, CancellationToken cancellationToken = default,
        IProgress<VaultReadProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.PageSize is < 1 or > 500 || query.MaximumPageBytes is < 1024 or > 64L * 1024 * 1024 ||
            query.SearchText == null || query.SearchText.Length > 4096 || !Enum.IsDefined(query.SortBy) ||
            !Enum.IsDefined(query.Expiry) || query.Tag == null || query.Tag.Length > 256 ||
            query.ExpiryReferenceDate > DateOnly.MaxValue.AddDays(-30))
            throw new ArgumentException("Unsupported entry query.", nameof(query));
        if (query.Expiry != EntryExpiryFilter.All && query.ExpiryReferenceDate == null)
            query = query with { ExpiryReferenceDate = DateOnly.FromDateTime(DateTime.Today) };
        CheckCrypto(crypto);
        return ReadAsync((connection, transaction, key, metadata) =>
        {
            string stamp = SnapshotStamp(connection, transaction), queryStamp = QueryStamp(query);
            if (cursor != null && (cursor.SnapshotStamp != stamp || cursor.QueryStamp != queryStamp)) throw new VaultPageChangedException();
            var groups = ReadGroups(connection, transaction, key, metadata);
            ValidateTree(groups, []); ValidateSystemGroups(groups, metadata);
            var groupIds = groups.Select(group => group.Id).ToHashSet();
            if (query.GroupId.HasValue && !groupIds.Contains(query.GroupId.Value)) throw new VaultPageChangedException();
            var order = OrderComparer(query.Descending);
            bool backward = cursor?.Backward == true;
            var selectionOrder = backward ? Comparer<EntryOrderKey>.Create((a, b) => order.Compare(b, a)) : order;
            // Reversed comparer makes the farthest candidate the first one to evict.
            var heap = new PriorityQueue<PwEntry, EntryOrderKey>(query.PageSize + 2,
                Comparer<EntryOrderKey>.Create((a, b) => selectionOrder.Compare(b, a)));
            EntryPageCursor? start = cursor;
            EntryOrderKey? startKey = null;
            if ((cursor?.EntryId ?? focusEntryId) is Guid anchorId)
            {
                var anchor = ReadEntries(connection, transaction, key, metadata, includeDetails: false, entryId: anchorId,
                    cancellationToken: cancellationToken).SingleOrDefault();
                try
                {
                    if (anchor != null && Matches(anchor, query, metadata))
                    {
                        startKey = OrderKey(anchor, query.SortBy);
                        start ??= new(anchor.Id, stamp, queryStamp, IncludeBoundary: true);
                    }
                    else if (cursor != null) throw new VaultPageChangedException();
                }
                finally { anchor?.ClearSecrets(); }
            }
            bool includeBoundary = start?.IncludeBoundary == true;
            int matched = 0, total = 0, before = 0;
            long retainedBytes = 0;
            EntryOrderKey? excludedBoundary = null;
            List<PwEntry>? selected = null;
            try
            {
                foreach (var entry in EnumerateEntries(connection, transaction, key, metadata, includeDetails: false,
                    cancellationToken: cancellationToken, progress: progress))
                {
                    bool retained = false;
                    try
                    {
                        total++;
                        if (!groupIds.Contains(entry.GroupId)) throw new InvalidDataException("Entry without a group.");
                        if (!Matches(entry, query, metadata)) continue;
                        matched++;
                        var entryKey = OrderKey(entry, query.SortBy);
                        if (startKey is { } boundaryKey)
                        {
                            int comparison = selectionOrder.Compare(entryKey, boundaryKey);
                            if (comparison < 0 || comparison == 0 && !includeBoundary) { before++; continue; }
                        }
                        if (excludedBoundary is { } boundary && selectionOrder.Compare(entryKey, boundary) >= 0) continue;
                        heap.Enqueue(entry, entryKey); retained = true; retainedBytes += EstimatedEntryBytes(entry);
                        while (heap.Count > query.PageSize || heap.Count > 1 && retainedBytes > query.MaximumPageBytes)
                        {
                            var evicted = heap.Dequeue(); var evictedKey = OrderKey(evicted, query.SortBy);
                            retainedBytes -= EstimatedEntryBytes(evicted);
                            // Keep a contiguous prefix: never skip a large row and include later small rows.
                            if (excludedBoundary == null || selectionOrder.Compare(evictedKey, excludedBoundary.Value) < 0) excludedBoundary = evictedKey;
                            evicted.ClearSecrets();
                        }
                    }
                    finally { if (!retained) entry.ClearSecrets(); }
                }
                cancellationToken.ThrowIfCancellationRequested();
                selected = heap.UnorderedItems.Select(item => item.Element).OrderBy(entry => OrderKey(entry, query.SortBy), order).ToList();
                int firstIndex = selected.Count == 0 ? 0 : backward ? matched - before - selected.Count + 1 : before + 1;
                EntryPageCursor? next = null, previous = null;
                if (selected.Count > 0 && firstIndex + selected.Count - 1 < matched)
                    next = new(selected[^1].Id, stamp, queryStamp);
                if (firstIndex > 1)
                    previous = new(selected[0].Id, stamp, queryStamp, Backward: true);
                return new VaultEntryPage(metadata, groups, selected, matched, total, firstIndex, start, next, previous);
            }
            catch
            {
                foreach (var item in heap.UnorderedItems) item.Element.ClearSecrets();
                if (selected != null) foreach (var entry in selected) entry.ClearSecrets();
                throw;
            }
        }, cancellationToken, progress);
    }

    public Task<List<AutoTypeCandidate>> FindAutoTypeCandidatesAsync(CryptoEngine crypto, string windowTitle,
        Guid? selectedEntryId = null, long? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        CheckCrypto(crypto);
        return ReadAsync((connection, transaction, key, metadata) =>
        {
            var result = new List<AutoTypeCandidate>();
            foreach (var entry in EnumerateEntries(connection, transaction, key, metadata, entryId: selectedEntryId,
                cancellationToken: cancellationToken, projection: EntrySerialization.AutoTypeMatchOptions))
            {
                try
                {
                    if (selectedEntryId.HasValue && expectedRevision.HasValue && entry.Revision != expectedRevision)
                        throw new VaultConflictException("Entry changed before Auto-Type.");
                    bool matches = entry.AutoTypeEnabled && entry.GroupId != metadata.RecycleBinGroupId &&
                        (selectedEntryId.HasValue ||
                        !string.IsNullOrWhiteSpace(entry.Title) && windowTitle.Contains(entry.Title, StringComparison.OrdinalIgnoreCase) ||
                        Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host) && windowTitle.Contains(uri.Host, StringComparison.OrdinalIgnoreCase));
                    if (matches) result.Add(new(entry.Id, entry.Revision, entry.Title));
                    // Two candidates are sufficient to reject an ambiguous match.
                    if (result.Count == 2) break;
                }
                finally { entry.ClearSecrets(); }
            }
            cancellationToken.ThrowIfCancellationRequested(); return result;
        }, cancellationToken);
    }

    public Task<PwEntry> LoadAutoTypeEntryAsync(Guid entryId, long expectedRevision, CryptoEngine crypto,
        CancellationToken cancellationToken = default)
    {
        CheckCrypto(crypto);
        return ReadAsync((connection, transaction, key, metadata) =>
        {
            var entry = ReadEntries(connection, transaction, key, metadata, entryId: entryId,
                cancellationToken: cancellationToken, projection: EntrySerialization.AutoTypeExecutionOptions).SingleOrDefault()
                ?? throw new VaultConflictException("The Auto-Type entry no longer exists.");
            if (entry.Revision != expectedRevision || !entry.AutoTypeEnabled || entry.GroupId == metadata.RecycleBinGroupId)
            { entry.ClearSecrets(); throw new VaultConflictException("The confirmed Auto-Type entry changed or is unavailable."); }
            return entry;
        }, cancellationToken);
    }

    public Task<IReadOnlyDictionary<Guid, long>> LoadEntryRevisionsAsync(Guid groupId, CryptoEngine crypto,
        CancellationToken cancellationToken = default)
    {
        CheckCrypto(crypto);
        return ReadAsync<IReadOnlyDictionary<Guid, long>>((connection, transaction, key, metadata) =>
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT id,revision FROM sp_entries WHERE group_id=$group";
            command.Parameters.AddWithValue("$group", groupId.ToString());
            using var reader = command.ExecuteReader(); var result = new Dictionary<Guid, long>();
            while (reader.Read()) { cancellationToken.ThrowIfCancellationRequested(); result.Add(Guid.Parse(reader.GetString(0)), reader.GetInt64(1)); }
            return result;
        }, cancellationToken);
    }
}
