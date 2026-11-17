using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Core.Database;

public sealed partial class SqliteStorageProvider
{
    public async Task ExportCsvAsync(string path, CryptoEngine crypto, CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
    {
        CheckCrypto(crypto);
        string destination = Path.GetFullPath(path);
        if (destination.Equals(FilePath, StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith(FilePath + ".", StringComparison.OrdinalIgnoreCase) ||
            destination.Equals(FilePath + "-wal", StringComparison.OrdinalIgnoreCase) ||
            destination.Equals(FilePath + "-shm", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The vault and its recovery files cannot be an export destination.", nameof(path));
        await ReadAsync((connection, transaction, key, metadata) =>
        {
            var groups = ReadGroups(connection, transaction, key, metadata);
            ValidateTree(groups, []); ValidateSystemGroups(groups, metadata);
            var byId = groups.ToDictionary(group => group.Id);
            var paths = new Dictionary<Guid, string>();
            string GroupPath(PwGroup group)
            {
                if (paths.TryGetValue(group.Id, out var known)) return known;
                return paths[group.Id] = group.ParentId.HasValue ? GroupPath(byId[group.ParentId.Value]) + "/" + group.Name : group.Name;
            }
            foreach (var group in groups) GroupPath(group);
            IEnumerable<PwEntry> Entries()
            {
                foreach (var entry in EnumerateEntries(connection, transaction, key, metadata, includeDetails: false,
                    cancellationToken: cancellationToken))
                {
                    try
                    {
                        if (!byId.ContainsKey(entry.GroupId)) throw new InvalidDataException("Entry without a group.");
                        if (entry.GroupId != metadata.RecycleBinGroupId) yield return entry;
                    }
                    finally { entry.ClearSecrets(); }
                }
            }
            ImportExportService.WriteCsvFile(destination, Entries(), paths, cancellationToken, progress);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }
}
