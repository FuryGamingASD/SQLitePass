using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Database;

public sealed partial class SqliteStorageProvider
{
    public async Task<VaultImportResult> ImportEntriesAsync(IEnumerable<PwEntry> entries, Guid targetGroupId,
        CryptoEngine crypto, CancellationToken cancellationToken = default, IProgress<VaultImportProgress>? progress = null)
    {
        CheckCrypto(crypto); var source = entries.ToList();
        if (source.Count is < 1 or > 100000) throw new InvalidDataException("Importare da 1 a 100.000 voci per operazione.");
        int createdCount = 0;
        await WriteAsync((c, tx, key, metadata) =>
        {
            var groups = ReadGroups(c, tx, key, metadata);
            ValidateTree(groups, []); ValidateSystemGroups(groups, metadata);
            var target = groups.FirstOrDefault(g => g.Id == targetGroupId)
                ?? throw new InvalidDataException("Il gruppo di destinazione non esiste più. Aggiornare il vault e riprovare.");
            if (target.Id == metadata.RecycleBinGroupId) throw new InvalidDataException("Non è possibile importare nel cestino.");
            var children = new Dictionary<Guid, Dictionary<string, List<PwGroup>>>();
            void Index(PwGroup group)
            {
                if (!group.ParentId.HasValue) return;
                if (!children.TryGetValue(group.ParentId.Value, out var names))
                    children[group.ParentId.Value] = names = new(StringComparer.OrdinalIgnoreCase);
                if (!names.TryGetValue(group.Name, out var matches)) names[group.Name] = matches = [];
                matches.Add(group);
            }
            foreach (var group in groups) Index(group);
            progress?.Report(new(0, source.Count));
            foreach (var (entry, index) in source.Select((entry, index) => (entry, index)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Revision != 0) throw new InvalidDataException("L'importazione accetta solo nuove voci.");
                Guid parent = target.Id;
                var explicitSegments = entry.SourceGroupSegments;
                var names = explicitSegments?.ToArray() ?? (entry.SourceGroupPath ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (explicitSegments == null && target.Id == metadata.RootGroupId && names.Length > 0 && names[0].Equals(target.Name, StringComparison.OrdinalIgnoreCase))
                    names = names[1..];
                if (names.Length > 63) throw new InvalidDataException("Percorso delle cartelle troppo profondo.");
                foreach (string name in names)
                {
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 256) throw new InvalidDataException("Nome cartella non valido o superiore a 256 caratteri.");
                    if (children.TryGetValue(parent, out var siblings) && siblings.TryGetValue(name, out var matches))
                    {
                        if (matches.Count != 1) throw new InvalidDataException("Il percorso importato corrisponde a più cartelle. Rinominare le cartelle duplicate prima di riprovare.");
                        if (matches[0].Id == metadata.RecycleBinGroupId) throw new InvalidDataException("Un percorso importato corrisponde al cestino. Scegliere un'altra destinazione.");
                        parent = matches[0].Id;
                    }
                    else
                    {
                        if (++createdCount > 10000) throw new InvalidDataException("L'importazione crea più di 10.000 cartelle.");
                        var group = new PwGroup { Name = name, ParentId = parent };
                        WriteGroup(c, tx, group, key, metadata); groups.Add(group); Index(group); parent = group.Id;
                    }
                }
                var copy = entry.DeepClone();
                try { copy.GroupId = parent; WriteEntry(c, tx, copy, key, metadata); }
                finally { copy.ClearSecrets(); }
                if ((index + 1) % 50 == 0 || index + 1 == source.Count) progress?.Report(new(index + 1, source.Count));
            }
            ValidateTree(groups, []); ValidateSystemGroups(groups, metadata);
        }, cancellationToken).ConfigureAwait(false);
        return new(source.Count, createdCount);
    }
}
