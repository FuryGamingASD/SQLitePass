namespace SQLitePass.Core.Models;

public enum EntrySortField { Title, UserName, Password, Url, Notes, UpdatedAt }
public enum EntryExpiryFilter { All, Expired, NextThirtyDays }

public sealed record EntryPageQuery(
    Guid? GroupId = null,
    string SearchText = "",
    bool OnlyRecycleBin = false,
    EntrySortField SortBy = EntrySortField.Title,
    bool Descending = false,
    int PageSize = 200,
    long MaximumPageBytes = 8L * 1024 * 1024,
    EntryExpiryFilter Expiry = EntryExpiryFilter.All,
    string Tag = "",
    DateOnly? ExpiryReferenceDate = null);

// Cursors belong to one authenticated snapshot and one query. They are never persisted.
public sealed record EntryPageCursor(string SortValue, Guid EntryId, string SnapshotStamp,
    string QueryStamp, bool IncludeBoundary = false, bool Backward = false);

public sealed record VaultEntryPage(VaultMetadata Metadata, List<PwGroup> Groups, List<PwEntry> Entries,
    int MatchedCount, int TotalCount, int FirstIndex, EntryPageCursor? CurrentCursor, EntryPageCursor? NextCursor,
    EntryPageCursor? PreviousCursor = null);

public sealed class VaultPageChangedException() : InvalidOperationException(
    "The vault changed between pages. Restart the query from the first page.");
