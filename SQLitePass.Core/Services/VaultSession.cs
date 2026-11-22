using System.Security.Cryptography;
using SQLitePass.Core.Crypto;
using SQLitePass.Core.Database;
using SQLitePass.Core.Models;

namespace SQLitePass.Core.Services;

public sealed class VaultSession : IAsyncDisposable
{
    private VaultSession(SqliteStorageProvider storage, CryptoEngine crypto, VaultMetadata metadata)
    {
        Storage = storage;
        Crypto = crypto;
        Metadata = metadata;
    }

    public SqliteStorageProvider Storage { get; }
    public CryptoEngine Crypto { get; }
    public VaultMetadata Metadata { get; }

    public static async Task<VaultSession?> OpenAsync(string path, MasterKey master, AppSettings settings,
        Func<Task<bool>> confirmLegacyMigration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(path)) throw new FileNotFoundException("Vault file not found.", path);
        var storage = new SqliteStorageProvider(path, settings.EnableDualFileBackup, settings.SecondaryBackupDir);
        CryptoEngine? crypto = null;
        bool opened = false;
        try
        {
            if (!await storage.CheckDatabaseIntegrityAsync()) throw new InvalidDataException("Vault integrity check failed.");
            var metadata = await ReadMetadataAsync(storage);
            cancellationToken.ThrowIfCancellationRequested();
            bool legacy = metadata.FormatVersion == 1;
            if (legacy && !await confirmLegacyMigration()) return null;

            crypto = await Task.Run(() => CryptoEngine.FromMasterKey(master, Convert.FromBase64String(metadata.SaltBase64),
                metadata.KdfIterations, metadata.KdfMemoryKb, metadata.KdfParallelism), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => storage.AuthenticateAsync(crypto, allowLegacyMigration: legacy), cancellationToken);
            metadata = await ReadMetadataAsync(storage);
            cancellationToken.ThrowIfCancellationRequested();

            opened = true;
            return new VaultSession(storage, crypto, metadata);
        }
        finally
        {
            if (!opened)
            {
                await storage.CloseAsync();
                crypto?.Dispose();
            }
        }
    }

    public static async Task<VaultSession> CreateAsync(string path, MasterKey master, AppSettings settings,
        string generalGroupName, string recycleBinName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path)) throw new IOException("An existing vault cannot be overwritten.");
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        var crypto = await Task.Run(() => CryptoEngine.FromMasterKey(master, salt), cancellationToken);
        SqliteStorageProvider? storage = null;
        bool opened = false;
        try
        {
            var metadata = new VaultMetadata
            {
                VaultName = Path.GetFileNameWithoutExtension(path),
                SaltBase64 = Convert.ToBase64String(salt),
                VerificationCanary = crypto.GenerateCanaryToken(),
                UsesKeyFile = master.UsesKeyFile
            };
            var root = new PwGroup { Name = metadata.VaultName, IsRoot = true };
            var general = new PwGroup { ParentId = root.Id, Name = generalGroupName };
            var trash = new PwGroup { ParentId = root.Id, Name = recycleBinName, IconIndex = 43, IsRecycleBin = true };
            cancellationToken.ThrowIfCancellationRequested();
            storage = await SqliteStorageProvider.CreateVaultAsync(path, metadata, crypto, [root, general, trash], [],
                settings.EnableDualFileBackup, settings.SecondaryBackupDir);
            metadata = await ReadMetadataAsync(storage);
            cancellationToken.ThrowIfCancellationRequested();

            opened = true;
            return new VaultSession(storage, crypto, metadata);
        }
        finally
        {
            if (!opened)
            {
                if (storage != null) await storage.CloseAsync();
                crypto.Dispose();
            }
        }
    }

    private static async Task<VaultMetadata> ReadMetadataAsync(SqliteStorageProvider storage) =>
        await storage.LoadVaultMetadataAsync() ?? throw new InvalidDataException("Vault metadata is missing.");

    public ValueTask DisposeAsync() => new(Storage.CloseAsync());
}
