using System.Security.Cryptography;
using DgNet.Keepass;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;
using KdbxDatabase = DgNet.Keepass.Database;
using KdbxEntry = DgNet.Keepass.Entry;
using KdbxGroup = DgNet.Keepass.Group;

namespace SQLitePass.Core.Services;

/// <summary>Reads authenticated KDBX 4 files and imports them only into a new SQLitePass vault.</summary>
public static class KdbxImportService
{
    private const long MaximumSourceBytes = 128L * 1024 * 1024;
    private const int MaximumKeyFileBytes = 4 * 1024 * 1024;
    private const int MaximumArgon2MemoryKib = 512 * 1024;
    private const int MaximumArgon2Iterations = 10;
    private const int MaximumArgon2Parallelism = 16;
    private const ulong MaximumAesKdfRounds = 20_000_000;
    private const int MaximumEntries = 100_000;
    private const int MaximumGroups = 10_000;
    private const int MaximumDepth = 63;

    public static Task<KdbxImportPlan> ReadAsync(string sourcePath, string sourcePassword, string? sourceKeyFilePath = null,
        CancellationToken cancellationToken = default)
    {
        if (sourcePassword == null) throw new ArgumentNullException(nameof(sourcePassword));
        return Task.Run(() => Read(sourcePath, sourcePassword, sourceKeyFilePath, cancellationToken), cancellationToken);
    }

    public static async Task<(VaultSession Session, KdbxImportReport Report)> CreateNewVaultAsync(KdbxImportPlan plan,
        string destinationPath, MasterKey destinationMasterKey, AppSettings settings, string generalGroupName,
        string recycleBinName, CancellationToken cancellationToken = default,
        IProgress<VaultImportProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(destinationMasterKey);
        ArgumentNullException.ThrowIfNull(settings);
        if (plan.Report.EntriesImported is < 1 or > MaximumEntries)
            throw new InvalidDataException("Il database KeePass non contiene voci importabili o supera il limite supportato.");

        string destination = Path.GetFullPath(destinationPath);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("Il file di destinazione esiste già e non verrà sovrascritto.");

        string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("Percorso di destinazione non valido.", nameof(destinationPath));
        Directory.CreateDirectory(parent);
        cancellationToken.ThrowIfCancellationRequested();

        string stagingDirectory = Path.Combine(parent, $".{Path.GetFileName(destination)}.import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        string stagingPath = Path.Combine(stagingDirectory, Path.GetFileName(destination));
        VaultSession? staging = null;
        bool published = false;
        byte[]? publishedHash = null;

        try
        {
            // Backups are configured only after the completed vault has been atomically published.
            var stagingSettings = new AppSettings { EnableDualFileBackup = false };
            staging = await VaultSession.CreateAsync(stagingPath, destinationMasterKey, stagingSettings,
                generalGroupName, recycleBinName, cancellationToken).ConfigureAwait(false);
            var groups = await staging.Storage.LoadAllGroupsAsync(staging.Crypto).ConfigureAwait(false);
            Guid rootId = staging.Metadata.RootGroupId ?? throw new InvalidDataException("Il vault temporaneo non ha un gruppo principale.");
            var targets = groups.Where(group => group.ParentId == rootId &&
                group.Name.Equals(generalGroupName, StringComparison.OrdinalIgnoreCase) && !group.IsRecycleBin).ToArray();
            if (targets.Length != 1) throw new InvalidDataException("Il vault temporaneo non contiene un gruppo di importazione univoco.");

            await staging.Storage.ImportEntriesAsync(plan.Entries, targets[0].Id, staging.Crypto,
                cancellationToken, progress).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!await staging.Storage.CheckDatabaseIntegrityAsync().ConfigureAwait(false))
                throw new InvalidDataException("Il vault SQLitePass temporaneo non ha superato il controllo d’integrità.");

            await staging.DisposeAsync().ConfigureAwait(false);
            staging = null;
            RemoveStagingSidecars(stagingDirectory, stagingPath);

            // This same-volume move is the only operation that makes the destination visible.
            publishedHash = await HashFileAsync(stagingPath, cancellationToken).ConfigureAwait(false);
            File.Move(stagingPath, destination, overwrite: false);
            published = true;

            // The database is complete at this point. Opening it is not cancellable so a late
            // cancellation cannot leave a successfully imported vault reported as rolled back.
            var finalSession = await VaultSession.OpenAsync(destination, destinationMasterKey, settings,
                () => Task.FromResult(true), CancellationToken.None).ConfigureAwait(false)
                ?? throw new InvalidDataException("Non è stato possibile aprire il vault importato.");
            return (finalSession, plan.Report);
        }
        catch
        {
            if (staging != null) await staging.DisposeAsync().ConfigureAwait(false);
            if (published && publishedHash != null)
            {
                try
                {
                    if (File.Exists(destination))
                    {
                        byte[] currentHash = await HashFileAsync(destination, CancellationToken.None).ConfigureAwait(false);
                        try
                        {
                            if (CryptographicOperations.FixedTimeEquals(publishedHash, currentHash))
                                File.Delete(destination);
                        }
                        finally { CryptographicOperations.ZeroMemory(currentHash); }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
        finally
        {
            if (publishedHash != null) CryptographicOperations.ZeroMemory(publishedHash);
            try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static KdbxImportPlan Read(string sourcePath, string sourcePassword, string? sourceKeyFilePath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        byte[]? keyFileBytes = ReadKeyFileSnapshot(sourceKeyFilePath);
        try
        {
        string fullSourcePath = Path.GetFullPath(sourcePath);
        using var sourceStream = new FileStream(fullSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (sourceStream.Length < 16 || sourceStream.Length > MaximumSourceBytes)
            throw new InvalidDataException("Il file KDBX è vuoto, incompleto o supera il limite di importazione di 128 MiB.");
        ValidateKdbx4Header(sourceStream);
        token.ThrowIfCancellationRequested();
        if (sourcePassword.Length == 0 && keyFileBytes is null)
            throw new ArgumentException("Inserire la password KeePass o selezionare un file chiave.", nameof(sourcePassword));

        var compositeKey = new CompositeKey();
        if (sourcePassword.Length != 0) compositeKey.AddPassword(sourcePassword);
        if (keyFileBytes != null)
        {
            compositeKey.AddKeyFile(keyFileBytes);
            CryptographicOperations.ZeroMemory(keyFileBytes);
            keyFileBytes = null;
        }

        using var database = new KdbxDatabase(fullSourcePath, compositeKey);
        List<PwEntry> entries = [];
        sourceStream.Position = 0;
        try
        {
            new KdbxReader(database).ReadFrom(sourceStream);
            token.ThrowIfCancellationRequested();
            var usedGroupIds = new HashSet<Guid>();
            var unsupported = new Dictionary<string, int?>(StringComparer.Ordinal);
            var groupSegments = new List<string>();
            var groupIds = new List<Guid>();
            int groupCount = 0;
            int recycleBinEntries = 0;
            Guid recycleBinId = database.Metadata.RecycleBinUuid;

            void AddUnsupported(string code, int? count)
            {
                if (count is <= 0) return;
                unsupported[code] = unsupported.TryGetValue(code, out var previous)
                    ? (previous.HasValue && count.HasValue ? previous.Value + count.Value : null)
                    : count;
            }

            void Walk(KdbxGroup group, int depth, bool root, bool underRecycleBin)
            {
                token.ThrowIfCancellationRequested();
                if (!root && depth > MaximumDepth)
                    throw new InvalidDataException("La gerarchia KeePass supera i 63 livelli supportati.");
                bool inRecycleBin = underRecycleBin || (recycleBinId != Guid.Empty && group.Uuid == recycleBinId);
                if (!inRecycleBin && group.Groups.Where(child => recycleBinId == Guid.Empty || child.Uuid != recycleBinId)
                    .GroupBy(child => child.Name, StringComparer.OrdinalIgnoreCase).Any(siblings => siblings.Count() > 1))
                    throw new InvalidDataException("Il database KeePass contiene cartelle omonime allo stesso livello che non possono essere importate senza unirle.");
                if (!root && !inRecycleBin)
                {
                    if (++groupCount > MaximumGroups) throw new InvalidDataException("Il database KeePass supera il limite di 10.000 gruppi.");
                    if (string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 256)
                        throw new InvalidDataException("Il database KeePass contiene un nome di gruppo vuoto o superiore a 256 caratteri.");
                    groupSegments.Add(group.Name);
                    groupIds.Add(group.Uuid);
                    AddUnsupported("KdbxUnsupportedGroupNotes", group.Notes.Length == 0 ? 0 : 1);
                    AddUnsupported("KdbxUnsupportedGroupRules", group.EnableAutoType.HasValue || group.EnableSearching.HasValue ? 1 : 0);
                    AddUnsupported("KdbxUnsupportedCustomIcons", group.CustomIconUuid == Guid.Empty ? 0 : 1);
                    AddUnsupported("KdbxUnsupportedGroupDates", group.Times.CreationTime == default && group.Times.LastModificationTime == default ? 0 : 1);
                    AddUnsupported("KdbxUnsupportedDefaultFolderIcons", 1);
                }

                foreach (var entry in group.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (inRecycleBin) { recycleBinEntries++; continue; }
                    if (entries.Count >= MaximumEntries) throw new InvalidDataException("Il database KeePass supera il limite di 100.000 voci.");
                    foreach (Guid id in groupIds) usedGroupIds.Add(id);
                    PwEntry converted = ConvertEntry(entry, groupSegments);
                    if (converted.AutoTypeEnabled)
                    {
                        try { AutoTypeService.ValidateSequence(converted); }
                        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or OverflowException)
                        {
                            converted.AutoTypeEnabled = false;
                            AddUnsupported("KdbxUnsupportedAutoTypeSequences", 1);
                        }
                    }
                    entries.Add(converted);
                    AddUnsupported("KdbxUnsupportedCustomIcons", entry.CustomIconUuid == Guid.Empty ? 0 : 1);
                    AddUnsupported("KdbxUnsupportedAlternateColorsAndUrls", string.IsNullOrEmpty(entry.ForegroundColor) &&
                        string.IsNullOrEmpty(entry.BackgroundColor) && string.IsNullOrEmpty(entry.OverrideUrl) ? 0 : 1);
                    AddUnsupported("KdbxUnsupportedAutoTypeAssociations", entry.AutoType.Associations.Count);
                    AddUnsupported("KdbxUnsupportedAdvancedAutoType", entry.AutoType.DataTransferObfuscation == 0 ? 0 : 1);
                    AddUnsupported("KdbxUnsupportedAttachmentProtectionFlags", entry.Binaries.Count(binary => binary.IsProtected));
                    AddUnsupported("KdbxUnsupportedAttachmentDates", entry.Binaries.Count);
                    AddUnsupported("KdbxUnsupportedStandardFieldProtectionFlags", entry.Strings.Count(field =>
                        IsStandardField(field.Key) && field.Value.Protected));
                    AddUnsupported("KdbxUnsupportedKeePassReferences", entry.Strings.Values.Count(value => value.Value.Contains("{REF:", StringComparison.OrdinalIgnoreCase)));
                    AddUnsupported("KdbxUnsupportedHistoryBeyondLimit", Math.Max(0, entry.History.Count - 50));
                    AddUnsupported("KdbxUnsupportedUsageAndMoveDates", entry.Times.UsageCount != 0 || entry.Times.LocationChanged != default ? 1 : 0);
                }

                foreach (var child in group.Groups)
                {
                    int childDepth = root ? 1 : depth + 1;
                    Walk(child, childDepth, root: false, inRecycleBin);
                }

                if (!root && !inRecycleBin)
                {
                    groupSegments.RemoveAt(groupSegments.Count - 1);
                    groupIds.RemoveAt(groupIds.Count - 1);
                }
            }

            Walk(database.RootGroup, 0, root: true, underRecycleBin: false);
            if (entries.Count == 0) throw new InvalidDataException("Il database KeePass non contiene voci attive da importare.");
            int emptyGroups = groupCount - usedGroupIds.Count;
            AddUnsupported("KdbxUnsupportedEmptyGroups", emptyGroups);
            AddUnsupported("KdbxUnsupportedCustomIcons", database.Metadata.CustomIcons.Count);
            AddUnsupported("KdbxUnsupportedRecycleBinEntries", recycleBinEntries);
            AddUnsupported("KdbxUnsupportedDatabaseMetadata", null);
            AddUnsupported("KdbxUnsupportedFolderTags", null);
            AddUnsupported("KdbxUnsupportedKeePassUuids", entries.Count + groupCount);

            ZeroSourceBinaries(database);

            var unsupportedItems = unsupported.Select(pair => new KdbxUnsupportedData(pair.Key, pair.Value)).ToArray();
            var report = new KdbxImportReport(
                database.Version.ToString(),
                usedGroupIds.Count,
                entries.Count,
                recycleBinEntries,
                [
                    "KdbxPreservedActiveFolderHierarchy",
                    "KdbxPreservedStandardEntryFields",
                    "KdbxPreservedCustomFields",
                    "KdbxPreservedTagsIconsAndDates",
                    "KdbxPreservedAttachments",
                    "KdbxPreservedHistory",
                    "KdbxPreservedAutoType",
                    "KdbxPreservedDestinationEncryption"
                ],
                unsupportedItems);
            return new KdbxImportPlan(entries, report);
        }
        catch
        {
            try { ZeroSourceBinaries(database); }
            catch (InvalidOperationException) { }
            // Converted records may already own duplicate secret strings and attachment buffers.
            foreach (var entry in entries) entry.ClearSecrets();
            throw;
        }
        }
        finally
        {
            if (keyFileBytes != null) CryptographicOperations.ZeroMemory(keyFileBytes);
        }
    }

    private static PwEntry ConvertEntry(KdbxEntry source, IReadOnlyList<string> groupSegments)
    {
        var entry = new PwEntry
        {
            Id = Guid.NewGuid(),
            GroupId = Guid.Empty,
            SourceGroupSegments = groupSegments.ToArray(),
            Title = source.Title,
            UserName = source.UserName,
            Password = source.Password,
            Url = source.Url,
            Notes = source.Notes,
            Tags = source.Tags,
            IconIndex = Math.Max(0, source.IconId),
            AutoTypeEnabled = source.AutoType.Enabled,
            AutoTypeSequence = source.AutoType.DefaultSequence,
            CreatedAt = Utc(source.Times.CreationTime),
            UpdatedAt = Utc(source.Times.LastModificationTime),
            LastAccessAt = Utc(source.Times.LastAccessTime),
            ExpiryDate = source.Times.Expires ? Utc(source.Times.ExpiryTime) : null,
        };

        foreach (var (key, field) in source.Strings)
        {
            if (IsStandardField(key)) continue;
            entry.CustomFields.Add(new PwCustomField { EntryId = entry.Id, Key = key, Value = field.Value, IsProtected = field.Protected });
        }

        foreach (var binary in source.Binaries)
        {
            entry.Attachments.Add(new PwAttachment
            {
                EntryId = entry.Id,
                FileName = binary.Name,
                Data = binary.Data.ToArray(),
                FileSize = binary.Data.LongLength,
                CreatedAt = entry.CreatedAt
            });
        }

        foreach (var previous in source.History.TakeLast(50))
        {
            PwEntry? snapshot = null;
            try
            {
                snapshot = ConvertEntry(previous, groupSegments);
                string json = EntrySerialization.Serialize(snapshot, includeHistory: false);
                entry.History.Add(new PwEntryHistory
                {
                    EntryId = entry.Id,
                    Title = snapshot.Title,
                    UserName = snapshot.UserName,
                    Password = snapshot.Password,
                    Url = snapshot.Url,
                    Notes = snapshot.Notes,
                    SnapshotJson = json,
                    SavedAt = Utc(previous.Times.LastModificationTime)
                });
            }
            finally { snapshot?.ClearSecrets(); }
        }

        return entry;
    }

    private static IEnumerable<KdbxEntry> EnumerateEntries(KdbxGroup group)
    {
        foreach (var entry in group.Entries) yield return entry;
        foreach (var child in group.Groups)
            foreach (var entry in EnumerateEntries(child)) yield return entry;
    }

    private static void ZeroSourceBinaries(KdbxDatabase database)
    {
        foreach (var sourceEntry in EnumerateEntries(database.RootGroup))
            foreach (var binary in sourceEntry.Binaries)
                CryptographicOperations.ZeroMemory(binary.Data);
        foreach (var icon in database.Metadata.CustomIcons)
            CryptographicOperations.ZeroMemory(icon.Data);
    }

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static bool IsStandardField(string key) =>
        key.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("UserName", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Password", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("URL", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Notes", StringComparison.OrdinalIgnoreCase);

    private static byte[]? ReadKeyFileSnapshot(string? sourceKeyFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceKeyFilePath)) return null;
        using var keyFile = new FileStream(Path.GetFullPath(sourceKeyFilePath), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (keyFile.Length is < 1 or > MaximumKeyFileBytes)
            throw new InvalidDataException("Il file chiave KeePass è vuoto o supera il limite supportato di 4 MiB.");
        byte[] snapshot = new byte[(int)keyFile.Length];
        try
        {
            keyFile.ReadExactly(snapshot);
            if (keyFile.ReadByte() != -1)
                throw new InvalidDataException("Il file chiave KeePass è cambiato durante la lettura e supera il limite supportato di 4 MiB.");
            return snapshot;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(snapshot);
            throw;
        }
    }

    private static void ValidateKdbx4Header(Stream stream)
    {
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var header = KdbxHeader.Read(reader);
        if (header.Version.Major != 4)
            throw new NotSupportedException("L’importazione supporta i database KDBX 4.x; KDBX 3.x non viene importato.");

        if (header.KdfParameters == null || !header.KdfParameters.TryGetValue("$UUID", out object? kdfUuidValue) ||
            kdfUuidValue is not byte[] kdfUuidBytes || kdfUuidBytes.Length != 16)
            throw new InvalidDataException("Il file KDBX non contiene un identificatore KDF valido.");
        Guid kdfUuid = GuidRfc4122.FromBytes(kdfUuidBytes);
        if ((kdfUuid == Argon2Kdf.Argon2dUuid || kdfUuid == Argon2Kdf.Argon2idUuid) &&
            (!header.KdfParameters.TryGetValue("V", out object? version) || version is not uint formatVersion || formatVersion != 0x13))
            throw new NotSupportedException("Il file KDBX deve specificare Argon2 versione 1.3 nel campo V.");

        IKdf kdf = header.CreateKdf();
        switch (kdf)
        {
            case Argon2Kdf argon:
                if (argon.Salt.Length is < 8 or > 1024 || argon.Parallelism is < 1 or > MaximumArgon2Parallelism ||
                    argon.MemoryKib < Math.Max(8, argon.Parallelism * 8) || argon.MemoryKib > MaximumArgon2MemoryKib ||
                    argon.Iterations is < 1 or > MaximumArgon2Iterations)
                    throw new InvalidDataException("I parametri Argon2 del file KDBX superano i limiti sicuri di importazione (512 MiB, 10 iterazioni, parallelismo 16).");
                break;
            case AesKdf aes:
                if (aes.Seed.Length != 32 || aes.Rounds is < 1 or > MaximumAesKdfRounds)
                    throw new InvalidDataException("I parametri AES-KDF del file KDBX superano il limite sicuro di 20 milioni di round.");
                break;
            default:
                throw new NotSupportedException("Il file KDBX usa un algoritmo di derivazione chiavi non supportato.");
        }
    }

    private static async Task<byte[]> HashFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
    }

    private static void RemoveStagingSidecars(string stagingDirectory, string stagingPath)
    {
        foreach (string file in Directory.EnumerateFiles(stagingDirectory))
        {
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(stagingPath), StringComparison.OrdinalIgnoreCase)) continue;
            string name = Path.GetFileName(file);
            if (name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-shm", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                continue;
            }
            throw new IOException("Il vault temporaneo contiene un file inatteso e non verrà pubblicato.");
        }
    }
}
