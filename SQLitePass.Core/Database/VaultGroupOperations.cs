using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Database;

public sealed partial class SqliteStorageProvider
{
    public Task TrashGroupAsync(Guid groupId, CryptoEngine crypto) => TrashGroupAsync(groupId, null, null, crypto);

    public Task TrashGroupAsync(Guid groupId, long expectedRevision, string recycleBinName, CryptoEngine crypto) =>
        TrashGroupAsync(groupId, (long?)expectedRevision, recycleBinName, crypto);

    private Task TrashGroupAsync(Guid groupId, long? expectedRevision, string? recycleBinName, CryptoEngine crypto)
    {
        CheckCrypto(crypto);
        return WriteAsync((c, tx, key, metadata) =>
        {
            var groups = ReadGroups(c, tx, key, metadata);
            ValidateTree(groups, []);
            RequireEditableGroup(groups, metadata, groupId, expectedRevision);
            Guid trash = GetOrCreateRecycleBin(c, tx, key, metadata, recycleBinName);
            var children = groups.ToLookup(group => group.ParentId);
            var subtree = new List<Guid> { groupId };
            for (int index = 0; index < subtree.Count; index++)
                subtree.AddRange(children[subtree[index]].Select(group => group.Id));
            if (subtree.Contains(trash)) throw new InvalidOperationException("The group contains the recycle bin.");

            foreach (var (id, revision) in EntryIdentities(c, tx, subtree.ToHashSet())) MoveEntry(c, tx, id, revision, trash, key, metadata);
            foreach (var id in subtree.AsEnumerable().Reverse()) Execute(c, tx, "DELETE FROM sp_groups WHERE id=$id", ("$id", id.ToString()));
        }, preservedBackupPurpose: "pre-trash-group");
    }

    public Task MoveGroupAsync(Guid groupId, long expectedRevision, Guid destinationId, CryptoEngine crypto)
    {
        CheckCrypto(crypto);
        return WriteAsync((c, tx, key, metadata) =>
        {
            var groups = ReadGroups(c, tx, key, metadata);
            ValidateTree(groups, []);
            var group = RequireEditableGroup(groups, metadata, groupId, expectedRevision);
            if (destinationId == metadata.RecycleBinGroupId || groups.All(item => item.Id != destinationId))
                throw new VaultConflictException("The destination folder is unavailable.");
            if (group.ParentId == destinationId) return;
            var map = groups.ToDictionary(item => item.Id);
            for (Guid? ancestor = destinationId; ancestor.HasValue; ancestor = map[ancestor.Value].ParentId)
                if (ancestor == groupId) throw new VaultConflictException("A folder cannot be moved inside itself.");
            group.ParentId = destinationId;
            group.SortOrder = checked(groups.Where(item => item.Id != groupId && item.ParentId == destinationId)
                .Select(item => item.SortOrder).DefaultIfEmpty(-1).Max() + 1);
            try { ValidateTree(groups, []); }
            catch (InvalidDataException) { throw new VaultConflictException("The destination would make the folder hierarchy too deep."); }
            WriteGroup(c, tx, group, key, metadata);
        });
    }

    public Task ReorderGroupAsync(Guid groupId, long expectedRevision, bool moveDown, CryptoEngine crypto)
    {
        CheckCrypto(crypto);
        return WriteAsync((c, tx, key, metadata) =>
        {
            var groups = ReadGroups(c, tx, key, metadata);
            var group = RequireEditableGroup(groups, metadata, groupId, expectedRevision);
            var siblings = groups.Where(item => item.ParentId == group.ParentId && item.Id != metadata.RootGroupId &&
                item.Id != metadata.RecycleBinGroupId).ToList();
            int index = siblings.FindIndex(item => item.Id == groupId);
            int destination = index + (moveDown ? 1 : -1);
            if (destination < 0 || destination >= siblings.Count) return;
            (siblings[index], siblings[destination]) = (siblings[destination], siblings[index]);
            for (int order = 0; order < siblings.Count; order++)
            {
                if (siblings[order].SortOrder == order) continue;
                siblings[order].SortOrder = order;
                WriteGroup(c, tx, siblings[order], key, metadata);
            }
        });
    }

    private static PwGroup RequireEditableGroup(List<PwGroup> groups, VaultMetadata metadata, Guid id, long? expectedRevision)
    {
        if (id == metadata.RootGroupId || id == metadata.RecycleBinGroupId)
            throw new InvalidOperationException("The vault root and recycle bin are protected.");
        var group = groups.FirstOrDefault(item => item.Id == id);
        if (group == null || (expectedRevision.HasValue && group.Revision != expectedRevision))
            throw new VaultConflictException("The folder changed after selection. Refresh before retrying.");
        return group;
    }

    private static Guid GetOrCreateRecycleBin(SqliteConnection c, SqliteTransaction tx, CryptoEngine crypto, VaultMetadata metadata, string? name)
    {
        if (metadata.RecycleBinGroupId is { } id) return id;
        if (string.IsNullOrWhiteSpace(name) || metadata.RootGroupId is not { } root)
            throw new InvalidOperationException("The vault does not contain a recycle bin.");
        var group = new PwGroup { Name = name, ParentId = root, IconIndex = 43, IsRecycleBin = true };
        WriteGroup(c, tx, group, crypto, metadata);
        metadata.RecycleBinGroupId = group.Id;
        return group.Id;
    }
}
