using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;

namespace SQLitePass.Core.Database;

public sealed partial class SqliteStorageProvider
{
    private string PreservedBackupPath(string purpose) => FilePath + "." + purpose + "-" +
        DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".backup";
    private void CreateSnapshot(string destination)
    {
        using (var source = Open(true))
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = destination, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
        {
            target.Open(); source.BackupDatabase(target);
        }
        using var flush = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        flush.Flush(flushToDisk: true);
    }

    private static void ReplaceBackup(string temporary, string destination)
    {
        if (!File.Exists(destination)) { File.Move(temporary, destination); return; }
        if (File.Exists(destination + ".2")) File.Move(destination + ".2", destination + ".3", overwrite: true);
        if (File.Exists(destination + ".1")) File.Move(destination + ".1", destination + ".2", overwrite: true);
        File.Replace(temporary, destination, destination + ".1");
    }
    private FileStream AcquireBackupLease()
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (_locked) throw new OperationCanceledException("Backup annullato dal blocco del vault.");
            try { return new FileStream(BackupFilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when (FileLease.IsContended(ex) && timer.Elapsed < TimeSpan.FromSeconds(5))
            { Thread.Sleep(50); }
        }
    }

    private void BackupCore()
    {
        if (!_enableBackup) return;
        string temporary = BackupFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // A second process must not publish an older snapshot after a newer backup.
            using var backupLease = AcquireBackupLease();
            using (var current = Open())
            using (var currentTransaction = current.BeginTransaction())
            {
                RequireSession(current, currentTransaction);
                CreateSnapshot(temporary);
                SimulateIoFailure?.Invoke(VaultIoStage.BackupCopied);
                using (var check = Open(true, temporary))
                using (var tx = check.BeginTransaction(deferred: true))
                {
                    if (!CheckIntegrity(check)) throw new InvalidDataException("Il candidato backup non è integro.");
                    var metadata = ReadMetadata(check, tx) ?? throw new InvalidDataException("Header del backup mancante.");
                    Verify(check, tx, metadata, _crypto!);
                }
                SimulateIoFailure?.Invoke(VaultIoStage.BeforeBackupReplacement);
                ReplaceBackup(temporary, BackupFilePath);
                currentTransaction.Commit();
            }
            LastBackupAt = DateTime.UtcNow; LastBackupError = null;
            if (!string.IsNullOrWhiteSpace(_secondaryBackupDirectory))
            {
                if (!Directory.Exists(_secondaryBackupDirectory)) throw new IOException("La destinazione del backup secondario non è disponibile.");
                string destination = Path.Combine(_secondaryBackupDirectory, $"{_metadata!.VaultId:N}.kpd.backup");
                string secondaryTemp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(BackupFilePath, secondaryTemp);
                    using (var flush = new FileStream(secondaryTemp, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) flush.Flush(true);
                    ReplaceBackup(secondaryTemp, destination);
                }
                finally { if (File.Exists(secondaryTemp)) File.Delete(secondaryTemp); }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            LastBackupError = ex.Message;
            AppLogger.LogWarning("Backup non completato: " + ex.GetType().Name);
        }
        finally
        {
            try { DeleteTemporary(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { LastBackupError ??= "Pulizia del file temporaneo non completata."; }
            try { BackupStatusChanged?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { AppLogger.LogWarning("Notifica dello stato backup non consegnata."); }
        }
    }

    public void TriggerAtomicBackup()
    {
        _gate.Wait();
        try
        {
            using (var c = Open()) using (var tx = c.BeginTransaction(deferred: true)) RequireSession(c, tx);
            if (IsReadOnly) throw new InvalidOperationException("Backup sospeso: vault in sola lettura.");
            BackupCore();
        }
        finally { _gate.Release(); }
    }

    // The original vault and its journals are preserved together when replaced in place.
    public Task<string> RestoreBackupAsync(MasterKey masterKey) => Task.Run(async () =>
    {
        if (!_locked) throw new InvalidOperationException("Bloccare il vault prima del ripristino.");
        using var lease = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string stagingDirectory = CreateRecoveryDirectory(FilePath);
        string candidate = Path.Combine(stagingDirectory, "candidate.kpd");
        string preserved = FilePath + ".recovery-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        var movedSidecars = new List<(string source, string preserved)>();
        bool installed = false;
        try
        {
            await AuthenticateRecoveryCopyAsync(BackupFilePath, candidate, masterKey, CancellationToken.None).ConfigureAwait(false);
            SimulateIoFailure?.Invoke(VaultIoStage.RecoveryValidated);
            // Windows can also deny access to the main file through sharing flags. The app lease protects SQLitePass sessions on both systems.
            using var original = File.Exists(FilePath)
                ? new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete) : null;
            foreach (string suffix in new[] { "-wal", "-shm" })
            {
                string sidecar = FilePath + suffix;
                if (File.Exists(sidecar))
                {
                    File.Move(sidecar, preserved + suffix);
                    movedSidecars.Add((sidecar, preserved + suffix));
                }
            }
            SimulateIoFailure?.Invoke(VaultIoStage.BeforeRecoveryReplacement);
            if (File.Exists(FilePath)) File.Replace(candidate, FilePath, preserved);
            else File.Move(candidate, FilePath);
            installed = true;
            return preserved;
        }
        finally
        {
            if (!installed)
                foreach (var (source, saved) in movedSidecars.AsEnumerable().Reverse()) if (File.Exists(saved)) File.Move(saved, source);
            RemoveRecoveryDirectory(stagingDirectory);
        }
    });

    public static Task RecoverBackupCopyAsync(string backupPath, string destination, MasterKey masterKey,
        CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        string final = Path.GetFullPath(destination);
        if (!Directory.Exists(Path.GetDirectoryName(final))) throw new DirectoryNotFoundException("The recovery directory does not exist.");
        RequireUnusedRecoveryPath(final);
        using var storage = new SqliteStorageProvider(backupPath, enableBackup: false);
        string stagingDirectory = CreateRecoveryDirectory(final);
        string candidate = Path.Combine(stagingDirectory, "candidate.kpd");
        try
        {
            await storage.AuthenticateRecoveryCopyAsync(storage.FilePath, candidate, masterKey, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            RequireUnusedRecoveryPath(final);
            File.Move(candidate, final, overwrite: false);
        }
        finally { RemoveRecoveryDirectory(stagingDirectory); }
    }, cancellationToken);

    private static string CreateRecoveryDirectory(string destination) => Directory.CreateDirectory(
        Path.Combine(Path.GetDirectoryName(destination)!, ".sqlitepass-recovery-" + Guid.NewGuid().ToString("N"))).FullName;

    private static void RemoveRecoveryDirectory(string stagingDirectory)
    {
        try { Directory.Delete(stagingDirectory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLogger.LogWarning("The temporary recovery copy could not be removed."); }
    }

    private static void RequireUnusedRecoveryPath(string destination)
    {
        foreach (string suffix in new[] { "", "-wal", "-shm", "-journal", ".lock" })
        {
            if (File.Exists(destination + suffix) || Directory.Exists(destination + suffix))
                throw new IOException("Choose a new path for the recovered vault. The destination is already in use.");
        }
    }

    private async Task AuthenticateRecoveryCopyAsync(string backup, string candidate, MasterKey masterKey, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(backup)) throw new FileNotFoundException("Backup non trovato.", backup);
        using (var source = Open(true, backup))
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = candidate, Pooling = false }.ToString()))
        {
            target.Open();
            source.BackupDatabase(target);
        }
        token.ThrowIfCancellationRequested();
        // A legacy backup is migrated only inside the private staging directory.
        using (var probe = new SqliteStorageProvider(candidate, enableBackup: false))
        {
            if (!await probe.CheckDatabaseIntegrityAsync().ConfigureAwait(false)) throw new InvalidDataException("Il backup non è integro.");
            var metadata = await probe.LoadVaultMetadataAsync().ConfigureAwait(false) ?? throw new InvalidDataException("Backup incompleto.");
            using var key = CryptoEngine.FromMasterKey(masterKey, Convert.FromBase64String(metadata.SaltBase64), metadata.KdfIterations, metadata.KdfMemoryKb, metadata.KdfParallelism);
            token.ThrowIfCancellationRequested();
            await probe.AuthenticateAsync(key).ConfigureAwait(false);
            await probe.ValidateContentsAsync(key, token).ConfigureAwait(false);
        }
        using var flush = new FileStream(candidate, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        flush.Flush(flushToDisk: true);
    }

    private static void DeleteTemporary(string path)
    {
        foreach (string suffix in new[] { "", "-wal", "-shm", ".lock" })
            if (File.Exists(path + suffix)) File.Delete(path + suffix);
    }

    public static async Task<SqliteStorageProvider> CreateVaultAsync(string path, VaultMetadata metadata, CryptoEngine crypto,
        IEnumerable<PwGroup> groups, IEnumerable<PwEntry> entries, bool enableBackup = true, string? secondaryBackupDirectory = null)
    {
        string final = Path.GetFullPath(path);
        if (File.Exists(final)) throw new IOException("Il file esiste già. Scegliere un nuovo nome: i vault esistenti non vengono sovrascritti.");
        if (crypto.DerivedWithKeyFile.HasValue) metadata.UsesKeyFile = crypto.DerivedWithKeyFile.Value;
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        string temporary = final + ".create-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stage = new SqliteStorageProvider(temporary, enableBackup: false))
            {
                await stage.InitializeDatabaseAndSchemaAsync().ConfigureAwait(false);
                await stage.SaveVaultMetadataAsync(metadata).ConfigureAwait(false);
                await stage.AuthenticateAsync(crypto).ConfigureAwait(false);
                await stage.SaveGroupsBatchAsync(groups, crypto).ConfigureAwait(false);
                await stage.SaveEntriesBatchAsync(entries, crypto).ConfigureAwait(false);
                // Transfer ownership of the key to the published session.
                stage._crypto = null;
            }
            // Closed connections checkpoint the WAL before the atomic rename.
            File.Move(temporary, final);
            var result = new SqliteStorageProvider(final, enableBackup, secondaryBackupDirectory);
            try { await result.AuthenticateAsync(crypto).ConfigureAwait(false); result.TriggerAtomicBackup(); return result; }
            catch { result.Dispose(); throw; }
        }
        finally { DeleteTemporary(temporary); }
    }
}
