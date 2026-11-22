using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Database;

public sealed partial class SqliteStorageProvider
{
    private static List<(Guid Id, long Revision)> EntryIdentities(SqliteConnection c, SqliteTransaction tx, HashSet<Guid>? groups = null)
    {
        using var command = c.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT id,revision,group_id FROM sp_entries";
        using var reader = command.ExecuteReader(); var result = new List<(Guid, long)>();
        while (reader.Read())
            if (groups == null || groups.Contains(Guid.Parse(reader.GetString(2)))) result.Add((Guid.Parse(reader.GetString(0)), reader.GetInt64(1)));
        return result;
    }

    private static long MoveEntry(SqliteConnection c, SqliteTransaction tx, Guid id, long expectedRevision, Guid targetGroupId,
        CryptoEngine crypto, VaultMetadata metadata, Guid? expectedGroupId = null)
    {
        var entry = ReadEntries(c, tx, crypto, metadata, entryId: id).SingleOrDefault()
            ?? throw new VaultConflictException("La voce non esiste più. Aggiornare il vault.");
        try
        {
            if (entry.Revision != expectedRevision || expectedGroupId.HasValue && entry.GroupId != expectedGroupId.Value)
                throw new VaultConflictException("La voce è cambiata in un'altra sessione. Aggiornare e riprovare.");
            entry.GroupId = targetGroupId; WriteEntry(c, tx, entry, crypto, metadata); return checked(entry.Revision + 1);
        }
        finally { entry.ClearSecrets(); }
    }

    public async Task<long> MoveEntryAsync(Guid entryId, long expectedRevision, Guid targetGroupId, CryptoEngine crypto)
    {
        CheckCrypto(crypto); long revision = 0;
        await WriteAsync((c, tx, key, metadata) => revision = MoveEntry(c, tx, entryId, expectedRevision, targetGroupId, key, metadata)).ConfigureAwait(false);
        return revision;
    }

    public Task TrashEntryAsync(Guid entryId, long expectedRevision, string recycleBinName, CryptoEngine crypto)
    {
        CheckCrypto(crypto);
        return WriteAsync((c, tx, key, metadata) =>
        {
            Guid trash = GetOrCreateRecycleBin(c, tx, key, metadata, recycleBinName);
            MoveEntry(c, tx, entryId, expectedRevision, trash, key, metadata);
        });
    }

    public async Task<long> RestoreEntryAsync(Guid entryId, long expectedRevision, Guid targetGroupId, CryptoEngine crypto)
    {
        CheckCrypto(crypto); long revision = 0;
        await WriteAsync((c, tx, key, metadata) =>
        {
            if (!metadata.RecycleBinGroupId.HasValue || targetGroupId == metadata.RecycleBinGroupId)
                throw new InvalidOperationException("Choose a destination outside the recycle bin.");
            if (!ReadGroups(c, tx, key, metadata).Any(group => group.Id == targetGroupId))
                throw new VaultConflictException("The destination no longer exists.");
            revision = MoveEntry(c, tx, entryId, expectedRevision, targetGroupId, key, metadata, metadata.RecycleBinGroupId);
        }).ConfigureAwait(false);
        return revision;
    }

    public Task ValidateContentsAsync(CryptoEngine crypto, CancellationToken cancellationToken = default)
    {
        CheckCrypto(crypto);
        return ReadAsync((c, tx, key, metadata) =>
        {
            var groups = ReadGroups(c, tx, key, metadata); ValidateTree(groups, []); ValidateSystemGroups(groups, metadata);
            var groupIds = groups.Select(group => group.Id).ToHashSet();
            foreach (var (id, _) in EntryIdentities(c, tx))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = ReadEntries(c, tx, key, metadata, entryId: id, cancellationToken: cancellationToken).Single();
                try { if (!groupIds.Contains(entry.GroupId)) throw new InvalidDataException("Voce senza gruppo."); }
                finally { entry.ClearSecrets(); }
            }
            return true;
        }, cancellationToken);
    }
}
