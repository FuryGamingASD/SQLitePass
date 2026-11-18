using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Database;

public sealed partial class SqliteStorageProvider : IDisposable
{
    private const int CurrentFormat = 2;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CryptoEngine? _crypto;
    private VaultMetadata? _metadata;
    private Guid _epoch;
    private volatile bool _locked = true;
    private bool _bootstrapAllowed;
    private FileStream? _lease;
    internal Action<VaultIoStage>? SimulateIoFailure { get; set; }
    private bool _enableBackup;
    private string? _secondaryBackupDirectory;
    public string FilePath { get; }
    public string BackupFilePath => FilePath + ".backup";
    public bool IsReadOnly { get; private set; }
    public string? LastBackupError { get; private set; }
    public DateTime? LastBackupAt { get; private set; }
    public bool BackupEnabled => _enableBackup;
    public event EventHandler? BackupStatusChanged;
    public void ConfigureBackup(bool enabled, string? secondaryDirectory) => ConfigureBackupAsync(enabled, secondaryDirectory).GetAwaiter().GetResult();
    public async Task ConfigureBackupAsync(bool enabled, string? secondaryDirectory)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _enableBackup = enabled; _secondaryBackupDirectory = secondaryDirectory; }
        finally { _gate.Release(); }
    }

    public SqliteStorageProvider(string filePath, bool enableBackup = true, string? secondaryBackupDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Percorso del vault mancante.", nameof(filePath));
        FilePath = Path.GetFullPath(filePath); _enableBackup = enableBackup; _secondaryBackupDirectory = secondaryBackupDirectory;
    }

    private SqliteConnection Open(bool readOnly = false, string? path = null)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path ?? FilePath, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5 }.ToString());
        try
        {
            c.Open(); Execute(c, null, "PRAGMA trusted_schema=OFF;");
            if (!readOnly) Execute(c, null, "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;");
            return c;
        }
        catch { c.Dispose(); throw; }
    }

    private static void Execute(SqliteConnection c, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    private static bool HasTable(SqliteConnection c, string table)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$table";
        cmd.Parameters.AddWithValue("$table", table); return (long)cmd.ExecuteScalar()! == 1;
    }
    private static void CreateSchema(SqliteConnection c, SqliteTransaction? tx) => Execute(c, tx, """
        CREATE TABLE IF NOT EXISTS sp_metadata(singleton INTEGER PRIMARY KEY CHECK(singleton=1), metadata TEXT NOT NULL, authentication TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS sp_groups(id TEXT PRIMARY KEY, parent_id TEXT NULL REFERENCES sp_groups(id) DEFERRABLE INITIALLY DEFERRED,
            revision INTEGER NOT NULL CHECK(revision>0), payload BLOB NOT NULL);
        CREATE TABLE IF NOT EXISTS sp_entries(id TEXT PRIMARY KEY, group_id TEXT NOT NULL REFERENCES sp_groups(id),
            revision INTEGER NOT NULL CHECK(revision>0), payload BLOB NOT NULL);
        CREATE INDEX IF NOT EXISTS sp_groups_parent ON sp_groups(parent_id);
        CREATE INDEX IF NOT EXISTS sp_entries_group ON sp_entries(group_id);
        PRAGMA user_version=2;
        """);

    public Task InitializeDatabaseAndSchemaAsync() => Task.Run(() =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using (new FileStream(FilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)) { }
        using var c = Open(); Execute(c, null, "PRAGMA journal_mode=WAL;"); CreateSchema(c, null);
        _bootstrapAllowed = true;
    });
    public Task<bool> VaultExistsAsync() => Task.FromResult(File.Exists(FilePath));
    public Task<bool> CheckDatabaseIntegrityAsync() => Task.Run(() =>
    {
        try { using var c = Open(true); return CheckIntegrity(c); }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidDataException) { return false; }
    });
    private static bool CheckIntegrity(SqliteConnection c)
    {
        using var cmd = c.CreateCommand(); cmd.CommandText = "PRAGMA quick_check";
        using (var r = cmd.ExecuteReader()) if (!r.Read() || r.GetString(0) != "ok" || r.Read()) return false;
        cmd.CommandText = "PRAGMA foreign_key_check"; using var foreign = cmd.ExecuteReader(); return !foreign.Read();
    }
    private static VaultMetadata? ReadMetadata(SqliteConnection c, SqliteTransaction? tx = null)
    {
        using (var version = c.CreateCommand())
        {
            version.Transaction = tx; version.CommandText = "PRAGMA user_version";
            if (Convert.ToInt32(version.ExecuteScalar()) > CurrentFormat) throw new InvalidDataException("Versione del vault più recente dell'applicazione.");
        }
        if (!HasTable(c, "sp_metadata")) return LegacyVaultReader.Metadata(c, tx);
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT metadata FROM sp_metadata WHERE singleton=1";
        if (cmd.ExecuteScalar() is not string json) return null;
        var m = JsonSerializer.Deserialize<VaultMetadata>(json) ?? throw new InvalidDataException("Header vuoto.");
        if (m.FormatVersion != CurrentFormat) throw new InvalidDataException("Versione del vault non supportata. Aggiornare SQLitePass.");
        CryptoEngine.ValidateKdf(Convert.FromBase64String(m.SaltBase64), m.KdfIterations, m.KdfMemoryKb, m.KdfParallelism);
        return m;
    }
    public Task<VaultMetadata?> LoadVaultMetadataAsync() => Task.Run(() => { using var c = Open(true); return ReadMetadata(c); });

    // Bootstrap only; an initialized header cannot be replaced through this API.
    public Task SaveVaultMetadataAsync(VaultMetadata m) => Task.Run(() =>
    {
        if (!_bootstrapAllowed) throw new InvalidOperationException("L'header può essere inizializzato solo dalla sessione che ha creato il nuovo file.");
        using var c = Open(); using var tx = c.BeginTransaction();
        if (ReadMetadata(c, tx) != null) throw new InvalidOperationException("Il vault è già inizializzato.");
        m.FormatVersion = CurrentFormat; m.EncryptUsernameUrl = true; WriteMetadata(c, tx, m, ""); tx.Commit();
    });

    public async Task AuthenticateAsync(CryptoEngine crypto)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_crypto != null) throw new InvalidOperationException("Sessione già aperta.");
            _lease = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
            using var c = Open(true); var m = ReadMetadata(c) ?? throw new InvalidDataException("Vault non inizializzato.");
            CryptoEngine.ValidateKdf(Convert.FromBase64String(m.SaltBase64), m.KdfIterations, m.KdfMemoryKb, m.KdfParallelism);
            if (!crypto.VerifyCanaryToken(m.VerificationCanary)) throw new CryptographicException("Credenziale del vault non corretta.");
            _crypto = crypto; _metadata = m; _epoch = m.KeyEpoch; _locked = false;
            if (m.FormatVersion == 1)
            {
                using var write = Open(); Execute(write, null, "PRAGMA secure_delete=ON;");
                using var tx = write.BeginTransaction();
                if (HasTable(write, "sp_metadata")) throw new InvalidOperationException("Un'altra sessione ha aggiornato il formato. Riaprire il vault.");
                var current = LegacyVaultReader.Metadata(write, tx) ?? throw new InvalidDataException("Header legacy mancante.");
                if (current.VaultId != m.VaultId || current.SaltBase64 != m.SaltBase64 || current.VerificationCanary != m.VerificationCanary ||
                    current.KdfIterations != m.KdfIterations || current.KdfMemoryKb != m.KdfMemoryKb || current.KdfParallelism != m.KdfParallelism)
                    throw new InvalidOperationException("Il vault è cambiato durante lo sblocco. Riaprirlo.");
                m = current; _metadata = current;
                var groups = LegacyVaultReader.Groups(write, tx); ValidateTree(groups, []);
                var migration = LegacyVaultReader.ValidateReferences(write, tx);
                bool legacyPrivateFields = m.EncryptUsernameUrl;
                var groupIds = groups.Select(group => group.Id).ToHashSet();
                // Validate the complete legacy source before changing the schema, without retaining its secrets.
                foreach (var entry in LegacyVaultReader.Entries(write, crypto, legacyPrivateFields, tx, migration))
                    if (!groupIds.Contains(entry.GroupId)) throw new InvalidDataException("Voce legacy senza gruppo.");
                CreateSnapshot(PreservedBackupPath("pre-v2"));
                CreateSchema(write, tx);
                m.FormatVersion = CurrentFormat; m.EncryptUsernameUrl = true; m.KeyEpoch = Guid.NewGuid();
                m.UsesKeyFile = crypto.DerivedWithKeyFile ?? false;
                m.RootGroupId = groups.FirstOrDefault(g => g.ParentId == null)?.Id;
                var legacyTrash = groups.Where(g => g.IconIndex == 43).ToList();
                if (legacyTrash.Count == 0) legacyTrash = groups.Where(g => new[] { "Cestino", "Trash", "Corbeille", "Papierkorb", "Papelera" }.Contains(g.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                if (legacyTrash.Count > 1) throw new InvalidDataException("Più cartelle del vecchio vault corrispondono al cestino. Risolvere l'ambiguità su una copia prima della migrazione.");
                m.RecycleBinGroupId = legacyTrash.SingleOrDefault()?.Id;
                ValidateSystemGroups(groups, m);
                foreach (var group in groups) WriteGroup(write, tx, group, crypto, m);
                foreach (var entry in LegacyVaultReader.Entries(write, crypto, legacyPrivateFields, tx, migration))
                {
                    if (!groupIds.Contains(entry.GroupId)) throw new InvalidDataException("Voce legacy senza gruppo.");
                    WriteEntry(write, tx, entry, crypto, m);
                }
                Execute(write, tx, "DROP TABLE kp_history; DROP TABLE kp_attachments; DROP TABLE kp_custom_fields; DROP TABLE kp_entries; DROP TABLE kp_groups; DROP TABLE kp_vault_metadata;");
                Seal(write, tx, m, crypto); SimulateIoFailure?.Invoke(VaultIoStage.BeforeCommit); tx.Commit(); _epoch = m.KeyEpoch;
                Execute(write, null, "PRAGMA wal_checkpoint(TRUNCATE);");
            }
            else
            {
                using var verify = Open(); using var tx = verify.BeginTransaction();
                using var cmd = verify.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT authentication FROM sp_metadata WHERE singleton=1";
                string auth = (string)cmd.ExecuteScalar()!;
                if (auth.Length == 0)
                {
                    if (!_bootstrapAllowed) throw new InvalidDataException("Firma del vault mancante. Il file non è un nuovo vault creato da questa sessione.");
                    cmd.CommandText = "SELECT (SELECT COUNT(*) FROM sp_groups)+(SELECT COUNT(*) FROM sp_entries)";
                    if ((long)cmd.ExecuteScalar()! != 0) throw new InvalidDataException("Firma dell'header assente.");
                    Seal(verify, tx, m, crypto);
                }
                else Verify(verify, tx, m, crypto);
                tx.Commit();
            }
            _bootstrapAllowed = false;
        }
        catch { _bootstrapAllowed = false; _lease?.Dispose(); _lease = null; _crypto = null; _metadata = null; _locked = true; throw; }
        finally { _gate.Release(); }
    }

    private static void WriteMetadata(SqliteConnection c, SqliteTransaction tx, VaultMetadata m, string auth) => Execute(c, tx,
        "INSERT INTO sp_metadata VALUES(1,$metadata,$auth) ON CONFLICT(singleton) DO UPDATE SET metadata=$metadata,authentication=$auth",
        ("$metadata", JsonSerializer.Serialize(m)), ("$auth", auth));
    private static string Context(VaultMetadata m, string table, Guid id, Guid? parent, long revision) =>
        $"SQLitePass/v2/{m.VaultId:D}/{table}/{id:D}/{parent?.ToString("D") ?? "root"}/{revision.ToString(CultureInfo.InvariantCulture)}";

    private static string Manifest(SqliteConnection c, SqliteTransaction tx, VaultMetadata m,
        CancellationToken cancellationToken = default, IProgress<VaultReadProgress>? progress = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(m)));
        void AppendLine(string value)
        {
            hash.AppendData("\n"u8); hash.AppendData(Encoding.UTF8.GetBytes(value));
        }
        int completed = 0, total = 0;
        if (progress != null)
        {
            using var count = c.CreateCommand(); count.Transaction = tx;
            count.CommandText = "SELECT (SELECT COUNT(*) FROM sp_groups)+(SELECT COUNT(*) FROM sp_entries)";
            total = Convert.ToInt32(count.ExecuteScalar()); progress.Report(new(VaultReadPhase.Verifying, 0, total));
        }
        using (var schema = c.CreateCommand())
        {
            schema.Transaction = tx;
            schema.CommandText = "SELECT type,name,tbl_name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name";
            using var reader = schema.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendLine(JsonSerializer.Serialize(new object[]
                { reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? "" : reader.GetString(3) }));
            }
        }
        foreach (string table in new[] { "sp_groups", "sp_entries" })
        {
            // Selecting rowid lets GetStream use SqliteBlob rather than copying the whole BLOB into a MemoryStream.
            using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = $"SELECT *,rowid FROM {table} ORDER BY id COLLATE BINARY";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendLine(JsonSerializer.Serialize(new object[]
                    { table, reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1), reader.GetInt64(2), PayloadHash(reader) }));
                completed++;
                if (completed % 50 == 0 || completed == total) progress?.Report(new(VaultReadPhase.Verifying, completed, total));
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static string PayloadHash(SqliteDataReader reader)
    {
        using var stream = reader.GetStream(3);
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Un record supera il limite supportato di 64 MB.");
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static void Seal(SqliteConnection c, SqliteTransaction tx, VaultMetadata m, CryptoEngine crypto)
    {
        m.UpdatedAt = DateTime.UtcNow;
        WriteMetadata(c, tx, m, crypto.EncryptString("SQLitePass manifest v2", "SQLitePass/manifest/" + Manifest(c, tx, m)));
    }
    private static void Verify(SqliteConnection c, SqliteTransaction tx, VaultMetadata m, CryptoEngine crypto,
        CancellationToken cancellationToken = default, IProgress<VaultReadProgress>? progress = null)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT authentication FROM sp_metadata WHERE singleton=1";
        string auth = (string?)cmd.ExecuteScalar() ?? throw new InvalidDataException("Firma del vault mancante.");
        if (crypto.DecryptString(auth, "SQLitePass/manifest/" + Manifest(c, tx, m, cancellationToken, progress)) != "SQLitePass manifest v2") throw new CryptographicException("Integrità del vault non valida.");
    }
    private VaultMetadata RequireSession(SqliteConnection c, SqliteTransaction tx, CancellationToken cancellationToken = default,
        IProgress<VaultReadProgress>? progress = null)
    {
        if (_locked || _crypto == null || _crypto.IsDisposed) throw new InvalidOperationException("Il vault è bloccato.");
        var m = ReadMetadata(c, tx) ?? throw new InvalidDataException("Header mancante.");
        if (m.KeyEpoch != _epoch || m.VerificationCanary != _metadata!.VerificationCanary)
        {
            _locked = true; _crypto.Dispose();
            throw new InvalidOperationException("La chiave è cambiata in un'altra sessione. Bloccare e riaprire il vault.");
        }
        Verify(c, tx, m, _crypto, cancellationToken, progress); return m;
    }
    private Task<T> ReadAsync<T>(Func<SqliteConnection, SqliteTransaction, CryptoEngine, VaultMetadata, T> action,
        CancellationToken cancellationToken = default, IProgress<VaultReadProgress>? progress = null) => Task.Run(async () =>
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var c = Open(); using var tx = c.BeginTransaction(deferred: true);
            var m = RequireSession(c, tx, cancellationToken, progress);
            cancellationToken.ThrowIfCancellationRequested(); return action(c, tx, _crypto!, m);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or InvalidDataException)
        { IsReadOnly = true; throw new InvalidDataException("Verifica di integrità fallita. Nessun dato è stato riscritto: usare un backup verificato.", ex); }
        finally { _gate.Release(); }
    });
    private Task WriteAsync(Action<SqliteConnection, SqliteTransaction, CryptoEngine, VaultMetadata> action, CancellationToken cancellationToken = default,
        string? preservedBackupPurpose = null) => Task.Run(async () =>
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); var originalCrypto = _crypto; bool committed = false;
        try
        {
            if (IsReadOnly) throw new InvalidOperationException("Il vault è in sola lettura dopo un errore di integrità.");
            using (var c = Open()) using (var tx = c.BeginTransaction())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var m = RequireSession(c, tx);
                if (preservedBackupPurpose != null) CreateSnapshot(PreservedBackupPath(preservedBackupPurpose));
                action(c, tx, _crypto!, m);
                cancellationToken.ThrowIfCancellationRequested();
                if (_locked) throw new OperationCanceledException("Operazione annullata dal blocco del vault.");
                Seal(c, tx, m, _crypto!);
                cancellationToken.ThrowIfCancellationRequested();
                if (_locked) throw new OperationCanceledException("Operazione annullata dal blocco del vault.");
                SimulateIoFailure?.Invoke(VaultIoStage.BeforeCommit);
                tx.Commit(); committed = true; _metadata = m; _epoch = m.KeyEpoch;
            }
            if (!ReferenceEquals(originalCrypto, _crypto)) originalCrypto?.Dispose();
            BackupCore();
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        { IsReadOnly = true; throw new InvalidDataException("Integrità non valida: scrittura annullata.", ex); }
        finally { if (!committed) _crypto = originalCrypto; _gate.Release(); }
    });
    private void CheckCrypto(CryptoEngine crypto)
    { if (!ReferenceEquals(_crypto, crypto) || crypto.IsDisposed || _locked) throw new InvalidOperationException("Sessione del vault non valida."); }

    private static List<PwGroup> ReadGroups(SqliteConnection c, SqliteTransaction tx, CryptoEngine crypto, VaultMetadata m)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT id,parent_id,revision,payload FROM sp_groups";
        using var r = cmd.ExecuteReader(); var result = new List<PwGroup>();
        while (r.Read())
        {
            var id = Guid.Parse(r.GetString(0)); Guid? parent = r.IsDBNull(1) ? null : Guid.Parse(r.GetString(1)); long revision = r.GetInt64(2);
            byte[] plain = crypto.DecryptBytes((byte[])r[3], Context(m, "groups", id, parent, revision));
            try
            {
                var g = JsonSerializer.Deserialize<PwGroup>(plain, EntrySerialization.Options) ?? throw new InvalidDataException("Gruppo vuoto.");
                if (g.Id != id || g.ParentId != parent || g.Revision != revision) throw new InvalidDataException("Identità del gruppo non valida.");
                g.IsRoot = id == m.RootGroupId; g.IsRecycleBin = id == m.RecycleBinGroupId; result.Add(g);
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        return result.OrderBy(g => g.SortOrder).ThenBy(g => g.CreatedAt).ToList();
    }
    private static List<PwEntry> ReadEntries(SqliteConnection c, SqliteTransaction tx, CryptoEngine crypto, VaultMetadata m,
        Guid? groupId = null, bool includeDetails = true, Guid? entryId = null,
        CancellationToken cancellationToken = default, IProgress<VaultReadProgress>? progress = null)
    {
        var result = new List<PwEntry>();
        try
        {
            foreach (var entry in EnumerateEntries(c, tx, crypto, m, groupId, includeDetails, entryId, cancellationToken, progress)) result.Add(entry);
            return result;
        }
        catch { foreach (var entry in result) entry.ClearSecrets(); throw; }
    }

    // The consumer owns each yielded model and must clear models it does not retain.
    private static IEnumerable<PwEntry> EnumerateEntries(SqliteConnection c, SqliteTransaction tx, CryptoEngine crypto, VaultMetadata m,
        Guid? groupId = null, bool includeDetails = true, Guid? entryId = null,
        CancellationToken cancellationToken = default, IProgress<VaultReadProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT id,group_id,revision,payload FROM sp_entries" + (groupId.HasValue ? " WHERE group_id=$group" : "");
        if (groupId.HasValue) cmd.Parameters.AddWithValue("$group", groupId.Value.ToString());
        if (entryId.HasValue)
        {
            cmd.CommandText += groupId.HasValue ? " AND id=$id" : " WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", entryId.Value.ToString());
        }
        int total = 0;
        if (progress != null)
        {
            using var count = c.CreateCommand(); count.Transaction = tx; count.CommandText = "SELECT COUNT(*) FROM sp_entries";
            total = Convert.ToInt32(count.ExecuteScalar()); progress.Report(new(VaultReadPhase.LoadingEntries, 0, total));
        }
        using var r = cmd.ExecuteReader(); int completed = 0;
        while (r.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Guid.Parse(r.GetString(0)); var group = Guid.Parse(r.GetString(1)); long revision = r.GetInt64(2);
            byte[] plain = crypto.DecryptBytes((byte[])r[3], Context(m, "entries", id, group, revision));
            PwEntry entry;
            try
            {
                entry = JsonSerializer.Deserialize<PwEntry>(plain, includeDetails ? EntrySerialization.Options : EntrySerialization.OverviewOptions)
                    ?? throw new InvalidDataException("Voce vuota.");
                if (entry.Id != id || entry.GroupId != group || entry.Revision != revision)
                { entry.ClearSecrets(); throw new InvalidDataException("Identità della voce non valida."); }
                if (!includeDetails) entry.MarkAsOverview();
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
            // Report only after ownership has passed to the consumer, including on cancellation.
            yield return entry;
            completed++;
            if (completed % 50 == 0 || completed == total) progress?.Report(new(VaultReadPhase.LoadingEntries, completed, total));
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
    public Task<List<PwGroup>> LoadAllGroupsAsync(CryptoEngine crypto)
    { CheckCrypto(crypto); return ReadAsync((c, tx, key, m) => { var groups = ReadGroups(c, tx, key, m); ValidateTree(groups, []); return groups; }); }
    public Task<List<PwEntry>> LoadEntriesAsync(Guid? groupId, CryptoEngine crypto)
    { CheckCrypto(crypto); return ReadAsync((c, tx, key, m) => ReadEntries(c, tx, key, m, groupId)); }
    public Task<PwEntry> LoadEntryAsync(Guid entryId, CryptoEngine crypto, long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        CheckCrypto(crypto);
        return ReadAsync((c, tx, key, m) =>
        {
            var entry = ReadEntries(c, tx, key, m, entryId: entryId, cancellationToken: cancellationToken).SingleOrDefault()
                ?? throw new VaultConflictException("La voce non esiste più. Aggiornare il vault.");
            if (expectedRevision.HasValue && entry.Revision != expectedRevision.Value)
            { entry.ClearSecrets(); throw new VaultConflictException("La voce è cambiata in un'altra sessione. Aggiornare e riprovare."); }
            return entry;
        }, cancellationToken);
    }
    public Task<VaultSnapshot> LoadSnapshotAsync(CryptoEngine crypto, bool includeDetails = true,
        CancellationToken cancellationToken = default, IProgress<VaultReadProgress>? progress = null)
    {
        CheckCrypto(crypto);
        return ReadAsync((c, tx, key, metadata) =>
        {
            var groups = ReadGroups(c, tx, key, metadata);
            var entries = ReadEntries(c, tx, key, metadata, includeDetails: includeDetails, cancellationToken: cancellationToken, progress: progress);
            try { ValidateTree(groups, entries); ValidateSystemGroups(groups, metadata); return new VaultSnapshot(metadata, groups, entries); }
            catch { foreach (var entry in entries) entry.ClearSecrets(); throw; }
        }, cancellationToken, progress);
    }

    private static void ValidateTree(List<PwGroup> groups, IEnumerable<PwEntry> entries)
    {
        var map = groups.ToDictionary(g => g.Id);
        foreach (var group in groups)
        {
            var seen = new HashSet<Guid>(); PwGroup? current = group;
            while (current != null)
            {
                if (!seen.Add(current.Id) || seen.Count > 64) throw new InvalidDataException("Gerarchia ciclica o troppo profonda.");
                if (current.ParentId.HasValue && !map.ContainsKey(current.ParentId.Value)) throw new InvalidDataException("Gruppo orfano.");
                current = current.ParentId.HasValue ? map[current.ParentId.Value] : null;
            }
        }
        if (entries.Any(e => !map.ContainsKey(e.GroupId))) throw new InvalidDataException("Voce senza gruppo.");
    }
    private static void ValidateSystemGroups(List<PwGroup> groups, VaultMetadata metadata)
    {
        var root = groups.FirstOrDefault(g => g.Id == metadata.RootGroupId);
        if (groups.Count > 0 && (root == null || root.ParentId.HasValue)) throw new InvalidDataException("La radice del vault deve esistere al livello principale.");
        if (metadata.RecycleBinGroupId.HasValue)
        {
            var trash = groups.FirstOrDefault(g => g.Id == metadata.RecycleBinGroupId);
            if (trash == null || trash.Id == metadata.RootGroupId || trash.ParentId != metadata.RootGroupId || groups.Any(g => g.ParentId == trash.Id))
                throw new InvalidDataException("Il cestino deve essere una cartella di sistema senza sottocartelle, sotto la radice.");
        }
    }
    private static void WriteGroup(SqliteConnection c, SqliteTransaction tx, PwGroup group, CryptoEngine crypto, VaultMetadata m)
    {
        var copy = JsonSerializer.Deserialize<PwGroup>(JsonSerializer.Serialize(group, EntrySerialization.Options), EntrySerialization.Options)!;
        copy.Revision = group.Revision + 1; copy.UpdatedAt = DateTime.UtcNow;
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(copy, EntrySerialization.Options);
        try { Upsert(c, tx, "sp_groups", "parent_id", copy.Id, copy.ParentId, group.Revision, crypto.EncryptBytes(plain, Context(m, "groups", copy.Id, copy.ParentId, copy.Revision))); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static void WriteEntry(SqliteConnection c, SqliteTransaction tx, PwEntry entry, CryptoEngine crypto, VaultMetadata m)
    {
        if (!entry.HasLoadedDetails) throw new InvalidOperationException("Una vista parziale non può essere salvata. Caricare prima i dettagli completi.");
        var copy = entry.CreateStorageCopy(); copy.Revision = entry.Revision + 1; copy.UpdatedAt = DateTime.UtcNow;
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(copy, EntrySerialization.Options);
        try
        {
            if (plain.Length > 64 * 1024 * 1024 - 28) throw new InvalidDataException("La voce e la sua cronologia superano 64 MB. Ridurre allegati o revisioni.");
            Upsert(c, tx, "sp_entries", "group_id", copy.Id, copy.GroupId, entry.Revision, crypto.EncryptBytes(plain, Context(m, "entries", copy.Id, copy.GroupId, copy.Revision)));
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    private static void Upsert(SqliteConnection c, SqliteTransaction tx, string table, string parentColumn, Guid id, Guid? parent, long expected, byte[] payload)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = expected == 0 ? $"INSERT INTO {table}(id,{parentColumn},revision,payload) VALUES($id,$parent,1,$payload)" :
            $"UPDATE {table} SET {parentColumn}=$parent,revision=$next,payload=$payload WHERE id=$id AND revision=$expected";
        cmd.Parameters.AddWithValue("$id", id.ToString()); cmd.Parameters.AddWithValue("$parent", parent?.ToString() ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$payload", payload); cmd.Parameters.AddWithValue("$expected", expected); cmd.Parameters.AddWithValue("$next", expected + 1);
        if (cmd.ExecuteNonQuery() != 1) throw new VaultConflictException("Record modificato in un'altra sessione. Aggiornare e riprovare.");
    }
    public Task SaveGroupAsync(PwGroup group, CryptoEngine crypto) => SaveGroupsBatchAsync([group], crypto);
    public async Task SaveGroupsBatchAsync(IEnumerable<PwGroup> groups, CryptoEngine crypto)
    {
        CheckCrypto(crypto); var list = groups.ToList();
        await WriteAsync((c, tx, key, m) =>
        {
            foreach (var group in list) WriteGroup(c, tx, group, key, m);
            var all = ReadGroups(c, tx, key, m); ValidateTree(all, []);
            m.RootGroupId ??= all.FirstOrDefault(g => g.ParentId == null)?.Id;
            m.RecycleBinGroupId ??= list.FirstOrDefault(g => g.IsRecycleBin)?.Id;
            ValidateSystemGroups(all, m);
        }).ConfigureAwait(false);
        foreach (var g in list) { g.Revision++; g.IsRoot = g.Id == _metadata!.RootGroupId; g.IsRecycleBin = g.Id == _metadata.RecycleBinGroupId; }
    }
    public Task SaveEntryAsync(PwEntry entry, CryptoEngine crypto, bool encryptUsernameUrl = true) => SaveEntriesBatchAsync([entry], crypto, encryptUsernameUrl);
    public async Task SaveEntriesBatchAsync(IEnumerable<PwEntry> entries, CryptoEngine crypto, bool encryptUsernameUrl = true)
    {
        CheckCrypto(crypto); var list = entries.ToList();
        if (list.Count > 100000) throw new InvalidDataException("Troppe voci in una singola operazione.");
        await WriteAsync((c, tx, key, m) => { foreach (var entry in list) WriteEntry(c, tx, entry, key, m); }).ConfigureAwait(false);
        foreach (var entry in list) { entry.Revision++; entry.UpdatedAt = DateTime.UtcNow; }
    }
    public Task DeleteEntryAsync(Guid entryId) => DeleteEntriesBatchAsync([entryId]);

    // UI confirmations refer to specific revisions. A later edit must not be deleted silently.
    public Task DeleteEntryAsync(Guid entryId, long expectedRevision) =>
        DeleteEntriesBatchAsync(new Dictionary<Guid, long> { [entryId] = expectedRevision });

    public Task DeleteEntriesBatchAsync(IReadOnlyDictionary<Guid, long> expectedRevisions)
    {
        var revisions = expectedRevisions.ToArray();
        return WriteAsync((c, tx, key, m) =>
        {
            foreach (var (id, revision) in revisions)
            {
                using var command = c.CreateCommand(); command.Transaction = tx;
                command.CommandText = "DELETE FROM sp_entries WHERE id=$id AND revision=$revision";
                command.Parameters.AddWithValue("$id", id.ToString());
                command.Parameters.AddWithValue("$revision", revision);
                if (command.ExecuteNonQuery() != 1)
                    throw new VaultConflictException("Record modified in another session. Refresh before retrying.");
            }
        }, preservedBackupPurpose: "pre-delete");
    }
    public Task DeleteEntriesBatchAsync(IEnumerable<Guid> entryIds)
    {
        var ids = entryIds.ToList();
        return WriteAsync((c, tx, key, m) => { foreach (var id in ids) Execute(c, tx, "DELETE FROM sp_entries WHERE id=$id", ("$id", id.ToString())); }, preservedBackupPurpose: "pre-delete");
    }
    public Task DeleteGroupAsync(Guid groupId) => WriteAsync((c, tx, key, m) =>
    {
        if (groupId == m.RootGroupId || groupId == m.RecycleBinGroupId) throw new InvalidOperationException("Gruppo di sistema protetto.");
        var all = ReadGroups(c, tx, key, m); var ids = new HashSet<Guid> { groupId }; bool added;
        do { added = false; foreach (var g in all) if (g.ParentId.HasValue && ids.Contains(g.ParentId.Value)) added |= ids.Add(g.Id); } while (added);
        foreach (var id in ids) Execute(c, tx, "DELETE FROM sp_entries WHERE group_id=$id", ("$id", id.ToString()));
        foreach (var id in ids) Execute(c, tx, "DELETE FROM sp_groups WHERE id=$id", ("$id", id.ToString()));
    }, preservedBackupPurpose: "pre-delete-group");
    public Task TrashGroupAsync(Guid groupId, CryptoEngine crypto)
    {
        CheckCrypto(crypto);
        return WriteAsync((c, tx, key, m) =>
        {
            if (groupId == m.RootGroupId || groupId == m.RecycleBinGroupId || !m.RecycleBinGroupId.HasValue)
                throw new InvalidOperationException("Gruppo di sistema protetto o cestino mancante.");
            var groups = ReadGroups(c, tx, key, m); var ids = new HashSet<Guid> { groupId }; bool added;
            do { added = false; foreach (var g in groups) if (g.ParentId.HasValue && ids.Contains(g.ParentId.Value)) added |= ids.Add(g.Id); } while (added);
            if (ids.Contains(m.RecycleBinGroupId.Value)) throw new InvalidOperationException("Il gruppo contiene il cestino di sistema.");
            foreach (var (id, revision) in EntryIdentities(c, tx, ids)) MoveEntry(c, tx, id, revision, m.RecycleBinGroupId.Value, key, m);
            foreach (var id in ids) Execute(c, tx, "DELETE FROM sp_groups WHERE id=$id", ("$id", id.ToString()));
        }, preservedBackupPurpose: "pre-trash-group");
    }
    public async Task ChangeMasterKeyAsync(CryptoEngine oldCrypto, CryptoEngine newCrypto, string salt, int iterations, int memoryKb, int parallelism, bool encryptUsernameUrl, bool usesKeyFile = false)
    {
        CheckCrypto(oldCrypto); CryptoEngine.ValidateKdf(Convert.FromBase64String(salt), iterations, memoryKb, parallelism);
        await WriteAsync((c, tx, key, m) =>
        {
            var groups = ReadGroups(c, tx, key, m); var entries = EntryIdentities(c, tx);
            var originalMetadata = JsonSerializer.Deserialize<VaultMetadata>(JsonSerializer.Serialize(m))!;
            CreateSnapshot(PreservedBackupPath("pre-rekey"));
            m.SaltBase64 = salt; m.KdfIterations = iterations; m.KdfMemoryKb = memoryKb; m.KdfParallelism = parallelism;
            m.VerificationCanary = newCrypto.GenerateCanaryToken(); m.KeyEpoch = Guid.NewGuid(); m.UsesKeyFile = newCrypto.DerivedWithKeyFile ?? usesKeyFile; m.EncryptUsernameUrl = true;
            foreach (var group in groups) WriteGroup(c, tx, group, newCrypto, m);
            foreach (var (id, _) in entries)
            {
                var entry = ReadEntries(c, tx, key, originalMetadata, entryId: id).Single();
                try { WriteEntry(c, tx, entry, newCrypto, m); }
                finally { entry.ClearSecrets(); }
            }
            _crypto = newCrypto;
        }).ConfigureAwait(false);
    }
    public Task VacuumAsync() => Task.CompletedTask;
    public void Dispose()
    {
        CloseAsync().GetAwaiter().GetResult();
    }
    public async Task CloseAsync()
    {
        _locked = true; _crypto?.Dispose();
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _crypto?.Dispose(); _crypto = null; _lease?.Dispose(); _lease = null; }
        finally { _gate.Release(); }
    }
}
